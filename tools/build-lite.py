#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["pyyaml"]
# ///
"""Cost-bounded unattended story driver.

Build Lite deliberately retains git/PR and deterministic verification gates from
``build-loop.py`` while replacing the unbounded build/review conversation with
at most: one plan, one implementation, one targeted repair, and one focused
review for risky changes.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import re
import sys
import time
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Sequence


REPO_ROOT = Path(__file__).resolve().parent.parent
LOG_ROOT = REPO_ROOT / ".build-lite" / "logs"
PLAN_ROOT = REPO_ROOT / ".build-lite" / "plans"
RESULT_ROOT = REPO_ROOT / ".build-lite" / "results"
BASE_BRANCH = "main"
PLAN_MODEL = "minimax-coding-plan/MiniMax-M3.1-Flash"
IMPLEMENT_MODEL = "minimax-coding-plan/MiniMax-M3"
REVIEW_MODEL = "minimax-coding-plan/MiniMax-M3.1-Flash"
PHASE_MAX_ATTEMPTS = 2


def load_legacy():
    """Load shared, battle-tested git and backlog helpers without executing it."""
    path = REPO_ROOT / "tools" / "build-loop.py"
    spec = importlib.util.spec_from_file_location("build_loop_shared", path)
    if spec is None or spec.loader is None:
        raise RuntimeError("cannot load tools/build-loop.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


legacy = load_legacy()


@dataclass(frozen=True)
class Risk:
    required: bool
    reasons: tuple[str, ...] = ()


SENSITIVE_PATHS: tuple[tuple[str, str], ...] = (
    ("src/Shared/Contracts/", "contrato compartido"),
    ("src/Server/Infrastructure/Persistence/", "persistencia"),
    ("src/Client/Infrastructure/Persistence/", "persistencia"),
    ("src/Server/Host/", "red o host"),
    ("src/Client/Host/", "red o host"),
    ("Installation", "instalación"),
    ("Security", "seguridad"),
    ("Signature", "firma de paquete"),
)


def log(message: str) -> None:
    print(f"[{datetime.now():%H:%M:%S}] {message}", flush=True)


def relative(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def story_paths(story) -> tuple[Path, Path, Path]:
    plan = PLAN_ROOT / f"{story.label}.md"
    result = RESULT_ROOT / f"{story.label}.json"
    return plan, result, LOG_ROOT / story.label


def changed_paths(base: str) -> list[str]:
    output = legacy.git("diff", "--name-only", f"{base}..HEAD", check=False).stdout
    return [line.strip() for line in output.splitlines() if line.strip()]


def classify_risk(paths: Sequence[str]) -> Risk:
    reasons: list[str] = []
    for path in paths:
        folded = path.replace("\\", "/")
        for marker, reason in SENSITIVE_PATHS:
            if marker.lower() in folded.lower() and reason not in reasons:
                reasons.append(reason)
    return Risk(required=bool(reasons), reasons=tuple(reasons))


def phase_command(agent: str, model: str, prompt: str) -> list[str]:
    return [
        "opencode", "run", "--format", "json", "--dangerously-skip-permissions", "--agent", agent,
        "--model", model, prompt,
    ]


def run_phase(
    name: str,
    model: str,
    prompt: str,
    log_dir: Path,
    timeout_minutes: int,
    retries: int = PHASE_MAX_ATTEMPTS,
):
    """Run a bounded provider phase; a retry is technical only, never a new loop."""
    for attempt in range(1, retries + 1):
        log(f"{name}: attempt {attempt} of {retries}")
        result = legacy.run_command(
            phase_command("build", model, prompt),
            log_path=log_dir / f"{name}-{attempt}.jsonl",
            timeout=timeout_minutes * 60,
        )
        if result.ok:
            return result
    raise legacy.DriverError(f"{name} failed after {retries} technical attempt(s); see {log_dir}")


def plan_prompt(story, plan: Path) -> str:
    return f"""Planifica exclusivamente la Story {story.short_id}: {story.title}.
Lee su sección en {relative(legacy.EPICS_FILE)} y el contexto compilado de la épica si existe.
Escribe un plan breve en {relative(plan)} con: alcance, ficheros concretos, tareas ordenadas,
pruebas, y riesgos de contrato/persistencia/red/instalación/seguridad. No implementes código,
no leas el backlog entero y no solicites aprobación. El plan debe caber en 500 palabras."""


def implementation_prompt(story, plan: Path) -> str:
    return f"""Implementa exclusivamente la Story {story.short_id}: {story.title}.
Lee {relative(plan)} y la sección correspondiente de {relative(legacy.EPICS_FILE)}. Implementa
el código y las pruebas descritas, ejecuta las pruebas más cercanas que existan y deja el árbol
listo para commit. No replanifiques, no lances revisores, no abras PR y no cambies otras stories."""


def repair_prompt(story, evidence: str) -> str:
    return f"""Repara exclusivamente el fallo confirmado de la Story {story.short_id}.
Evidencia: {evidence}
Haz el cambio mínimo y sus pruebas. No replanifiques, no revises de nuevo, no abras PR y no
modifiques alcance ajeno."""


def review_prompt(story, diff_file: Path, risk: Risk) -> str:
    reasons = ", ".join(risk.reasons)
    return f"""Revisa el diff de la Story {story.short_id} solo por estos riesgos: {reasons}.
Lee {relative(diff_file)} y los hunks necesarios. Responde exactamente `OK` si no hay un defecto
S0/S1 confirmado. Si lo hay, responde una sola línea `BLOCKING: <evidencia verificable y arreglo
local>`; no inventes hallazgos, no propongas mejoras, no clasifiques hipótesis, no modifiques
ficheros ni lances subagentes."""


def write_diff(base: str, output: Path) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    result = legacy.git("diff", "--binary", "--no-ext-diff", f"{base}..HEAD", check=False)
    output.write_text(result.stdout, encoding="utf-8")


def blocking_review_line(transcript: str) -> str:
    """Accept only the intentionally narrow blocking-review protocol."""
    for line in transcript.splitlines():
        if line.strip().startswith("BLOCKING:"):
            return line.strip()
    return ""


def review_result(log_path: Path) -> str:
    if not log_path.exists():
        return ""
    session, transcript = legacy.parse_skill_output(log_path)
    del session
    return blocking_review_line(transcript)


def verification_commands() -> tuple:
    return tuple(command for command in legacy.VERIFICATION_COMMANDS if command.label != "build-loop self test")


def run_verifications(log_dir: Path) -> list[tuple]:
    results = []
    own = legacy.run_command(("uv", "run", "tools/build-lite.py", "--self-test"), log_path=log_dir / "verify-build-lite.log")
    results.append(("build-lite self test", own))
    for command in verification_commands():
        result = legacy.run_command(command.argv, log_path=log_dir / f"verify-{command.label.replace(' ', '-')}.log", timeout=60 * 60)
        results.append((command.label, result))
    return results


def verify_or_repair(story, log_dir: Path, repair_available: bool = True) -> bool:
    results = run_verifications(log_dir)
    failed = [label for label, result in results if not result.ok]
    if not failed:
        return False
    if not repair_available:
        raise legacy.DriverError(f"verification failed after the repair budget was spent: {', '.join(failed)}")
    evidence = "; ".join(failed)
    run_phase("repair", IMPLEMENT_MODEL, repair_prompt(story, evidence), log_dir, timeout_minutes=30)
    second = run_verifications(log_dir / "after-repair")
    failed = [label for label, result in second if not result.ok]
    if failed:
        raise legacy.DriverError(f"verification still fails after the single repair: {', '.join(failed)}")
    return True


def commit_story(story) -> None:
    if not legacy.git("status", "--porcelain").stdout.strip():
        return
    legacy.git("add", "-A")
    legacy.git("commit", "-m", f"feat({story.label}): {story.title}")


def result_body(story, risk: Risk, base: str) -> str:
    review = "no necesaria (cambio interno)" if not risk.required else f"focalizada: {', '.join(risk.reasons)}"
    return f"""## Build Lite

- **Historia:** {story.short_id} — {story.title}
- **Base:** `{base}`
- **Revisión LLM:** {review}
- **Verificación:** `build-lite --self-test`, `dotnet build`, `dotnet test`, codificación y backlog en verde.
- **Presupuesto:** un plan, una implementación, una reparación dirigida como máximo y una revisión para cambios sensibles.
"""


def process_story(story, args: argparse.Namespace) -> None:
    legacy.fetch_origin()
    base = legacy.git("rev-parse", f"origin/{BASE_BRANCH}").stdout.strip()
    plan, result_file, log_dir = story_paths(story)
    log_dir.mkdir(parents=True, exist_ok=True)

    if legacy.branch_exists(story.branch):
        legacy.git("checkout", story.branch)
        legacy.preserve_pending_work(story)
    else:
        legacy.git("checkout", "-b", story.branch, f"origin/{BASE_BRANCH}")
    if not legacy.is_base_ancestor():
        legacy.rebase_onto_main()
        base = legacy.git("rev-parse", f"origin/{BASE_BRANCH}").stdout.strip()

    if not plan.exists():
        run_phase("plan", PLAN_MODEL, plan_prompt(story, plan), log_dir, timeout_minutes=10)
    if not plan.exists() or not plan.read_text(encoding="utf-8", errors="replace").strip():
        raise legacy.DriverError(f"planner did not create {relative(plan)}")

    run_phase("implementation", IMPLEMENT_MODEL, implementation_prompt(story, plan), log_dir, timeout_minutes=30)
    commit_story(story)
    repair_used = verify_or_repair(story, log_dir)
    commit_story(story)

    paths = changed_paths(base)
    if not paths:
        raise legacy.DriverError("Build Lite produced no story change")
    risk = classify_risk(paths)
    diff_file = log_dir / "review.diff"
    write_diff(base, diff_file)
    if risk.required:
        result = run_phase("review", REVIEW_MODEL, review_prompt(story, diff_file, risk), log_dir, timeout_minutes=10)
        blocking = review_result(result.log or Path())
        if blocking:
            if repair_used:
                raise legacy.DriverError(
                    "the focused review found a blocker after the single repair budget was spent: " + blocking
                )
            run_phase("review-repair", IMPLEMENT_MODEL, repair_prompt(story, blocking), log_dir, timeout_minutes=30)
            commit_story(story)
            verify_or_repair(story, log_dir / "after-review-repair", repair_available=False)

    legacy.fetch_origin()
    main_after = legacy.git("rev-parse", f"origin/{BASE_BRANCH}").stdout.strip()
    if main_after != base:
        legacy.rebase_onto_main()
        base = main_after
        verify_or_repair(story, log_dir / "after-rebase", repair_available=False)

    result_file.parent.mkdir(parents=True, exist_ok=True)
    result_file.write_text(json.dumps({"story": story.key, "base": base, "risk": list(risk.reasons), "paths": paths}, indent=2), encoding="utf-8")
    legacy.git("add", relative(result_file))
    legacy.git("commit", "-m", f"chore(build-lite): record story {story.short_id} result")
    legacy.update_and_commit_sprint_status(story)
    subject, _ = legacy.branch_commit_message(story)
    if not legacy.is_conventional_subject(subject):
        raise legacy.DriverError(f"non-conventional story subject: {subject!r}")
    legacy.push_branch(story.branch)
    pr = legacy.find_open_pull_request(story.branch)
    body = result_body(story, risk, base)
    if pr:
        number, _ = pr
        legacy.gh("pr", "edit", str(number), "--title", subject, "--body", body)
    else:
        number, _ = legacy.open_pull_request(story, subject, body)
    if not args.no_merge:
        legacy.merge_pull_request(number, subject, "")
        revision = legacy.git("rev-parse", "HEAD").stdout.strip()
        deadline = time.time() + 120
        while time.time() < deadline:
            legacy.git("fetch", "origin", BASE_BRANCH)
            if legacy.git("merge-base", "--is-ancestor", revision, f"origin/{BASE_BRANCH}", check=False).ok:
                break
            time.sleep(10)
        else:
            raise legacy.DriverError(f"the merge of pull request #{number} is not in origin/main yet")
        merged_status = legacy.read_sprint_status()
        if str(merged_status.get(story.key, "")).lower() != "done":
            raise legacy.DriverError(f"story {story.short_id} is not done after merge")


def self_test() -> int:
    failures = []
    if not classify_risk(["src/Server/Application/Catalog/RegisterGame.cs"]).required:
        pass
    else:
        failures.append("internal code was classified as sensitive")
    risk = classify_risk(["src/Shared/Contracts/InstallationManifest.cs"])
    if not risk.required or "contrato compartido" not in risk.reasons:
        failures.append("shared contracts were not classified as sensitive")
    command = phase_command("build", PLAN_MODEL, "plan")
    if command[:8] != ["opencode", "run", "--format", "json", "--dangerously-skip-permissions", "--agent", "build", "--model"]:
        failures.append("phase command is malformed")
    if PHASE_MAX_ATTEMPTS != 2:
        failures.append("phase retry budget is not fixed at two technical attempts")
    if blocking_review_line("OK\n"):
        failures.append("an OK review was parsed as blocking")
    if not blocking_review_line("note\nBLOCKING: an S1 is proven\n"):
        failures.append("a blocking review result was not parsed")
    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1
    print("the build-lite self test passed")
    return 0


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run the cost-bounded Build Lite story driver.")
    parser.add_argument("--story", help="process one story as N.M")
    parser.add_argument("--max-stories", type=int, default=0)
    parser.add_argument("--no-merge", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args(argv)
    if args.max_stories < 0:
        parser.error("--max-stories must be zero or greater")
    if args.story and not re.fullmatch(r"\d+\.\d+", args.story):
        parser.error("--story must be N.M")
    return args


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    if args.self_test:
        return self_test()
    legacy.require_clean_tree()
    legacy.fetch_origin()
    status = legacy.read_sprint_status()
    if args.story:
        epic, number = (int(part) for part in args.story.split("."))
        stories = [legacy.story_by_id(epic, number, status)]
    else:
        stories = legacy.pending_stories(status)
    if args.max_stories:
        stories = stories[:args.max_stories]
    if args.dry_run:
        for story in stories:
            print(f"{story.short_id}: {story.title}")
        return 0
    for story in stories:
        try:
            process_story(story, args)
        except legacy.DriverError:
            # A provider failure can leave a useful plan or partial implementation
            # in the tree. Commit it as recoverable WIP before surfacing the stop.
            legacy.preserve_pending_work(story)
            raise
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except legacy.DriverError as error:
        print(f"STOP: {error}", file=sys.stderr)
        raise SystemExit(1)
