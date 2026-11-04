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

The driver schedules independent stories in parallel: up to four workers run
at the same time, each one in its own worktree and on its own branch. The
integration of the finished stories stays serial: one coordinator rebases,
re-verifies, opens the pull request and squash-merges them one at a time
against the current ``origin/main``.

The independence of two stories is never guessed from their paths. It comes
from the structured block of the plan of each story (``reads``, ``writes``,
``shared_surfaces`` and ``depends_on``). A story whose plan is missing that
block, or that declares a sensitive surface, runs alone.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import re
import shutil
import sys
import threading
import time
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Sequence


REPO_ROOT = Path(__file__).resolve().parent.parent
LOG_ROOT = REPO_ROOT / ".build-lite" / "logs"
PLAN_ROOT = REPO_ROOT / ".build-lite" / "plans"
RESULT_ROOT = REPO_ROOT / ".build-lite" / "results"
WORKTREE_ROOT = REPO_ROOT / ".build-lite" / "worktrees"
BASE_BRANCH = "main"
PLAN_MODEL = "minimax-coding-plan/MiniMax-M3.1-Flash"
IMPLEMENT_MODEL = "minimax-coding-plan/MiniMax-M3"
REVIEW_MODEL = "minimax-coding-plan/MiniMax-M3.1-Flash"
PHASE_MAX_ATTEMPTS = 2
IMPLEMENTATION_MAX_ATTEMPTS = 1

# The window of the scheduler. Four workers is the ceiling of the project:
# a fifth story waits for a free slot instead of starting.
MAX_PARALLEL_WORKERS = 4

# The surfaces that a story may not touch while another worker is running.
SENSITIVE_SURFACES: tuple[str, ...] = (
    "contract",
    "contracts",
    "shared_contract",
    "shared-contracts",
    "persistence",
    "network",
    "networking",
    "host",
    "installation",
    "install",
    "security",
    "signature",
    "signing",
    "persistencia",
    "red",
    "instalacion",
    "instalación",
    "seguridad",
    "firma",
)


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


# ---------------------------------------------------------------------------
# Structured plan metadata
# ---------------------------------------------------------------------------

PLAN_METADATA_KEYS: tuple[str, ...] = ("reads", "writes", "shared_surfaces", "depends_on")


@dataclass(frozen=True)
class PlanMetadata:
    """The declared footprint of a story, read from its plan.

    The block is the only evidence the scheduler accepts. A story without a
    valid block cannot prove that it is independent, so it runs alone.
    """

    reads: tuple[str, ...] = ()
    writes: tuple[str, ...] = ()
    shared_surfaces: tuple[str, ...] = ()
    depends_on: tuple[str, ...] = ()

    def footprint(self) -> frozenset[str]:
        """The paths and surfaces this story touches."""
        return frozenset(self.writes) | frozenset(self.shared_surfaces)

    def sensitive_surfaces(self) -> tuple[str, ...]:
        """The declared surfaces that forbid running next to another worker."""
        found: list[str] = []
        for surface in self.shared_surfaces:
            folded = surface.strip().lower().replace(" ", "_")
            if folded in SENSITIVE_SURFACES:
                found.append(surface)
                continue
            for marker in SENSITIVE_SURFACES:
                if marker in folded:
                    found.append(surface)
                    break
        for path in self.writes:
            for marker, reason in SENSITIVE_PATHS:
                if marker.lower() in path.replace("\\", "/").lower() and reason not in found:
                    found.append(reason)
        return tuple(found)


def extract_plan_metadata(plan: Path) -> tuple[PlanMetadata | None, str]:
    """Read the structured block of a plan.

    The block is a fenced ```json object with the four keys of the execution
    model. The first fenced block that carries a JSON object is the one; a
    plan without it, or with one that does not carry the four keys, is
    ambiguous and the story runs alone.
    """
    try:
        content = plan.read_text(encoding="utf-8")
    except OSError as error:
        return None, f"the plan cannot be read: {error}"

    for block in re.findall(r"```json\s*\n(.*?)```", content, re.DOTALL):
        stripped = block.strip()
        if not stripped.startswith("{"):
            continue
        try:
            data = json.loads(stripped)
        except json.JSONDecodeError:
            continue
        if not isinstance(data, dict):
            continue
        missing = [key for key in PLAN_METADATA_KEYS if key not in data]
        if missing:
            return None, f"the plan metadata is missing the keys: {', '.join(missing)}"
        parsed: dict[str, tuple[str, ...]] = {}
        for key in PLAN_METADATA_KEYS:
            value = data[key]
            if not isinstance(value, list) or not all(isinstance(item, str) for item in value):
                return None, f"the plan metadata key '{key}' is not a list of strings"
            parsed[key] = tuple(item.strip() for item in value if item.strip())
        return (
            PlanMetadata(
                reads=parsed["reads"],
                writes=parsed["writes"],
                shared_surfaces=parsed["shared_surfaces"],
                depends_on=parsed["depends_on"],
            ),
            "",
        )
    return None, "the plan has no metadata block with reads, writes, shared_surfaces and depends_on"


def normalise_entry(value: str) -> str:
    """The comparable form of a path or a surface."""
    return value.replace("\\", "/").strip().strip("/").lower()


def entries_intersect(left: Sequence[str], right: Sequence[str]) -> bool:
    """True when two declarations share an entry.

    A declared path matches another one when it is the same path or a
    directory that contains it. Two surfaces match when they are equal or
    when one names the other as a prefix, so `catalog` and `catalog/index`
    are the same surface.
    """
    for first in left:
        for second in right:
            a = normalise_entry(first)
            b = normalise_entry(second)
            if not a or not b:
                continue
            if a == b or a.startswith(b + "/") or b.startswith(a + "/"):
                return True
            if "." not in a and "." not in b and (a.startswith(b) or b.startswith(a)):
                return True
    return False


# ---------------------------------------------------------------------------
# Small helpers
# ---------------------------------------------------------------------------


def log(message: str) -> None:
    print(f"[{datetime.now():%H:%M:%S}] {message}", flush=True)


def relative(path: Path) -> str:
    return path.relative_to(REPO_ROOT).as_posix()


def story_paths(story) -> tuple[Path, Path, Path]:
    plan = PLAN_ROOT / f"{story.label}.md"
    result = RESULT_ROOT / f"{story.label}.json"
    return plan, result, LOG_ROOT / story.label


def is_complete_plan(plan: Path) -> bool:
    """A planner may finish its file before its CLI session reaches the timeout."""
    try:
        content = plan.read_text(encoding="utf-8")
    except OSError:
        return False
    return len(content.strip()) >= 300 and all(section in content for section in ("# Story", "## Alcance", "## Ficheros"))


def changed_paths(base: str, cwd: Path | None = None) -> list[str]:
    output = legacy.git("diff", "--name-only", f"{base}..HEAD", check=False, cwd=cwd).stdout
    return [line.strip() for line in output.splitlines() if line.strip()]


def implementation_paths(paths: Sequence[str]) -> list[str]:
    """Exclude Build Lite bookkeeping when deciding whether work can be resumed."""
    return [path for path in paths if not path.replace("\\", "/").startswith(".build-lite/")]


def paths_are_declared(paths: Sequence[str], metadata: PlanMetadata | None) -> bool:
    """True when every real source change is within the declared write footprint."""
    if metadata is None:
        return False
    return all(entries_intersect((path,), metadata.writes) for path in implementation_paths(paths))


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
        "--model", model, f"BUILD_LITE_WORKER:\n{prompt}",
    ]


def run_phase(
    name: str,
    model: str,
    prompt: str,
    log_dir: Path,
    timeout_minutes: int,
    retries: int = PHASE_MAX_ATTEMPTS,
    cwd: Path | None = None,
):
    """Run a bounded provider phase; a retry is technical only, never a new loop."""
    for attempt in range(1, retries + 1):
        log(f"{name}: attempt {attempt} of {retries}")
        result = legacy.run_command(
            phase_command("build", model, prompt),
            log_path=log_dir / f"{name}-{attempt}.jsonl",
            timeout=timeout_minutes * 60,
            cwd=cwd or REPO_ROOT,
        )
        if result.ok:
            return result
    raise legacy.DriverError(f"{name} failed after {retries} technical attempt(s); see {log_dir}")


def plan_prompt(story, plan: Path) -> str:
    return f"""Planifica exclusivamente la Story {story.short_id}: {story.title}.
Lee su sección en {relative(legacy.EPICS_FILE)} y el contexto compilado de la épica si existe.
Escribe un plan breve en {relative(plan)} con: alcance, ficheros concretos, tareas ordenadas,
pruebas, y riesgos de contrato/persistencia/red/instalación/seguridad. No implementes código,
no leas el backlog entero y no solicites aprobación. El plan debe caber en 500 palabras.

El plan tiene que incluir un bloque ```json con las claves `reads`, `writes`, `shared_surfaces`
y `depends_on`: son las rutas que la historia lee, las que escribe, las superficies compartidas
que toca (contrato, persistencia, red, instalación, seguridad o firma) y las historias de las que
depende. Sin ese bloque la historia se ejecuta sola, así que decláralo aunque la superficie sea
interna y vacía."""


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


def write_diff(base: str, output: Path, cwd: Path | None = None) -> None:
    output.parent.mkdir(parents=True, exist_ok=True)
    result = legacy.git("diff", "--binary", "--no-ext-diff", f"{base}..HEAD", check=False, cwd=cwd)
    output.write_text(result.stdout, encoding="utf-8")


def merged_commit_from_pr(payload: str) -> str:
    """Return the squash merge commit only once GitHub reports a merged PR."""
    try:
        data = json.loads(payload)
    except json.JSONDecodeError:
        return ""
    if not isinstance(data, dict) or data.get("state") != "MERGED":
        return ""
    merge_commit = data.get("mergeCommit")
    if not isinstance(merge_commit, dict):
        return ""
    oid = merge_commit.get("oid")
    return oid if isinstance(oid, str) else ""


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


def run_verifications(log_dir: Path, cwd: Path | None = None) -> list[tuple]:
    results = []
    own = legacy.run_command(
        ("uv", "run", "tools/build-lite.py", "--self-test"),
        log_path=log_dir / "verify-build-lite.log",
        cwd=cwd or REPO_ROOT,
    )
    results.append(("build-lite self test", own))
    for command in verification_commands():
        result = legacy.run_command(
            command.argv,
            log_path=log_dir / f"verify-{command.label.replace(' ', '-')}.log",
            timeout=60 * 60,
            cwd=cwd or REPO_ROOT,
        )
        results.append((command.label, result))
    return results


def verify_or_repair(
    story,
    log_dir: Path,
    repair_available: bool = True,
    cwd: Path | None = None,
) -> bool:
    results = run_verifications(log_dir, cwd=cwd)
    failed = [label for label, result in results if not result.ok]
    if not failed:
        return False
    if not repair_available:
        raise legacy.DriverError(f"verification failed after the repair budget was spent: {', '.join(failed)}")
    evidence = "; ".join(failed)
    run_phase("repair", IMPLEMENT_MODEL, repair_prompt(story, evidence), log_dir, timeout_minutes=30, cwd=cwd)
    second = run_verifications(log_dir / "after-repair", cwd=cwd)
    failed = [label for label, result in second if not result.ok]
    if failed:
        raise legacy.DriverError(f"verification still fails after the single repair: {', '.join(failed)}")
    return True


def commit_story(story, cwd: Path | None = None) -> None:
    if not legacy.git("status", "--porcelain", cwd=cwd).stdout.strip():
        return
    legacy.git("add", "-A", cwd=cwd)
    legacy.git("commit", "-m", f"feat({story.label}): {story.title}", cwd=cwd)


def result_body(story, risk: Risk, base: str) -> str:
    review = "no necesaria (cambio interno)" if not risk.required else f"focalizada: {', '.join(risk.reasons)}"
    return f"""## Build Lite

- **Historia:** {story.short_id} — {story.title}
- **Base:** `{base}`
- **Revisión LLM:** {review}
- **Verificación:** `build-lite --self-test`, `dotnet build`, `dotnet test`, codificación y backlog en verde.
- **Presupuesto:** un plan, una implementación, una reparación dirigida como máximo y una revisión para cambios sensibles.
"""


# ---------------------------------------------------------------------------
# Worktrees
# ---------------------------------------------------------------------------


@dataclass
class Worktree:
    """The isolated checkout of a story."""

    story: object
    path: Path
    branch: str

    def exists(self) -> bool:
        return self.path.exists()


def worktree_for(story) -> Worktree:
    return Worktree(story=story, path=legacy.worktree_path_for(WORKTREE_ROOT, story.label), branch=story.branch)


def prepare_worktree(story) -> Worktree:
    """Create the worktree of a story, or recover the one it already had.

    The worktree is created from `origin/main` when the branch is new and it
    is attached to the existing branch otherwise, so the work of an
    interrupted story survives instead of being thrown away.
    """
    tree = worktree_for(story)
    tree.path.parent.mkdir(parents=True, exist_ok=True)
    if legacy.worktree_registered(tree.path) and tree.exists():
        log(f"{story.short_id}: reusing the worktree {tree.path}")
        legacy.preserve_pending_work(story, cwd=tree.path)
        return tree
    legacy.add_worktree(story.branch, tree.path)
    log(f"{story.short_id}: worktree {tree.path} on {story.branch}")
    return tree


def discard_worktree(tree: Worktree) -> None:
    """Remove the worktree of a story that merged."""
    if not legacy.worktree_registered(tree.path):
        return
    legacy.remove_worktree(tree.path, force=True)
    log(f"{tree.story.short_id}: worktree {tree.path} removed after the merge")


def preserve_worktree(tree: Worktree) -> None:
    """Keep the worktree of a story that failed, with its evidence."""
    log(f"{tree.story.short_id}: the worktree {tree.path} is preserved for inspection")


# ---------------------------------------------------------------------------
# The isolated execution of a story
# ---------------------------------------------------------------------------


@dataclass
class WorkerResult:
    """What a worker produced, before any publication."""

    story: object
    worktree: Worktree
    base: str
    risk: Risk
    paths: list[str]
    metadata: PlanMetadata | None = None
    error: str = ""

    @property
    def ok(self) -> bool:
        return not self.error


def run_worker(story, tree: Worktree, metadata: PlanMetadata | None) -> WorkerResult:
    """Plan, implement and verify a story inside its own worktree.

    Nothing here touches `main`, the sprint status or GitHub: the worker
    produces a verified branch and the coordinator publishes it.
    """
    plan, result_file, log_dir = story_paths(story)
    log_dir.mkdir(parents=True, exist_ok=True)
    worker_plan = tree.path / relative(plan)
    worker_plan.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(plan, worker_plan)
    base = legacy.git("rev-parse", f"origin/{BASE_BRANCH}", cwd=tree.path).stdout.strip()

    if implementation_paths(changed_paths(base, cwd=tree.path)):
        log("reusing preserved implementation work; proceeding to verification")
    else:
        run_phase(
            "implementation",
            IMPLEMENT_MODEL,
            implementation_prompt(story, plan),
            log_dir,
            timeout_minutes=30,
            retries=IMPLEMENTATION_MAX_ATTEMPTS,
            cwd=tree.path,
        )
    commit_story(story, cwd=tree.path)
    repair_used = verify_or_repair(story, log_dir, cwd=tree.path)
    commit_story(story, cwd=tree.path)

    paths = changed_paths(base, cwd=tree.path)
    if not paths:
        raise legacy.DriverError("Build Lite produced no story change")
    if not paths_are_declared(paths, metadata):
        raise legacy.DriverError("the worker changed files outside its declared plan footprint")
    risk = classify_risk(paths)
    diff_file = log_dir / "review.diff"
    write_diff(base, diff_file, cwd=tree.path)
    worker_diff_file = tree.path / relative(diff_file)
    worker_diff_file.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(diff_file, worker_diff_file)
    if risk.required:
        result = run_phase(
            "review", REVIEW_MODEL, review_prompt(story, diff_file, risk), log_dir, timeout_minutes=10,
            cwd=tree.path,
        )
        blocking = review_result(result.log or Path())
        if blocking:
            if repair_used:
                raise legacy.DriverError(
                    "the focused review found a blocker after the single repair budget was spent: " + blocking
                )
            run_phase(
                "review-repair", IMPLEMENT_MODEL, repair_prompt(story, blocking), log_dir, timeout_minutes=30,
                cwd=tree.path,
            )
            commit_story(story, cwd=tree.path)
            verify_or_repair(story, log_dir / "after-review-repair", repair_available=False, cwd=tree.path)

    del result_file
    return WorkerResult(
        story=story,
        worktree=tree,
        base=base,
        risk=risk,
        paths=paths,
        metadata=metadata,
    )


# ---------------------------------------------------------------------------
# The serial integration of the finished stories
# ---------------------------------------------------------------------------


def integrate(result: WorkerResult, args: argparse.Namespace) -> None:
    """Rebase, verify, open the pull request and squash-merge a story.

    The integration is serial: it is the only place that touches the sprint
    status, GitHub and `main`. The branch is rebased on the `origin/main` that
    is current at this moment and the verifications run again against it, so
    a story never merges on a base that another story already replaced.
    """
    story = result.story
    tree = result.worktree
    plan, result_file, log_dir = story_paths(story)

    legacy.fetch_origin()
    main_after = legacy.git("rev-parse", f"origin/{BASE_BRANCH}", cwd=tree.path).stdout.strip()
    if main_after != result.base:
        legacy.rebase_onto_main(cwd=tree.path)
        result.base = main_after
        verify_or_repair(story, log_dir / "after-rebase", repair_available=False, cwd=tree.path)

    worker_result_file = tree.path / relative(result_file)
    worker_result_file.parent.mkdir(parents=True, exist_ok=True)
    worker_result_file.write_text(
        json.dumps(
            {
                "story": story.key,
                "base": result.base,
                "risk": list(result.risk.reasons),
                "paths": result.paths,
            },
            indent=2,
        ),
        encoding="utf-8",
    )
    legacy.git("add", relative(result_file), cwd=tree.path)
    legacy.git("commit", "-m", f"chore(build-lite): record story {story.short_id} result", cwd=tree.path)
    legacy.update_and_commit_sprint_status(story, cwd=tree.path)
    subject, _ = legacy.branch_commit_message(story, cwd=tree.path)
    if not legacy.is_conventional_subject(subject):
        raise legacy.DriverError(f"non-conventional story subject: {subject!r}")
    legacy.push_branch(story.branch, cwd=tree.path)
    pr = legacy.find_open_pull_request(story.branch, cwd=tree.path)
    body = result_body(story, result.risk, result.base)
    if pr:
        number, _ = pr
        legacy.gh("pr", "edit", str(number), "--title", subject, "--body", body, cwd=tree.path)
    else:
        number, _ = legacy.open_pull_request(story, subject, body, cwd=tree.path)
    if not args.no_merge:
        legacy.merge_pull_request(number, subject, "")
        deadline = time.time() + 120
        while time.time() < deadline:
            legacy.git("fetch", "origin", BASE_BRANCH)
            pr_state = legacy.gh("pr", "view", str(number), "--json", "state,mergeCommit")
            merge_commit = merged_commit_from_pr(pr_state.stdout)
            if merge_commit and legacy.git(
                "merge-base", "--is-ancestor", merge_commit, f"origin/{BASE_BRANCH}", check=False
            ).ok:
                break
            time.sleep(10)
        else:
            raise legacy.DriverError(f"the merge of pull request #{number} is not in origin/main yet")
        status_file = relative(legacy.SPRINT_STATUS_FILE)
        merged_status = legacy.parse_development_status(
            legacy.git("show", f"origin/{BASE_BRANCH}:{status_file}").stdout
        )
        if str(merged_status.get(story.key, "")).lower() != "done":
            raise legacy.DriverError(f"story {story.short_id} is not done after merge")
    del plan


# ---------------------------------------------------------------------------
# The scheduler
# ---------------------------------------------------------------------------


@dataclass
class Slot:
    """A worker running, or ready to run, a story."""

    story: object
    tree: Worktree
    metadata: PlanMetadata | None = None
    thread: threading.Thread | None = None
    result: WorkerResult | None = None
    error: str = ""

    def start(self) -> None:
        self.thread = threading.Thread(
            target=self._run, name=f"worker-{self.story.short_id}", daemon=True
        )
        self.thread.start()

    def _run(self) -> None:
        try:
            self.result = run_worker(self.story, self.tree, self.metadata)
        except legacy.DriverError as error:
            self.error = str(error)
        except Exception as error:  # noqa: BLE001 - the worker must report, not crash the driver
            self.error = f"unexpected {type(error).__name__}: {error}"

    def join(self, timeout: float | None = None) -> None:
        if self.thread is not None:
            self.thread.join(timeout)

    @property
    def finished(self) -> bool:
        return self.thread is not None and not self.thread.is_alive()

    @property
    def ok(self) -> bool:
        return self.finished and self.error == "" and self.result is not None


def slot_is_exclusive(slot: Slot) -> bool:
    """Whether a running slot is allowed to share the worker window."""
    return slot.metadata is None or bool(slot.metadata.sensitive_surfaces())


def active_window_is_exclusive(active: Sequence[Slot]) -> bool:
    """Whether an active worker forbids planning or admitting another story.

    An ambiguous plan and every sensitive surface run alone. Planning another
    story while one is active cannot change that decision, and it can hold the
    scheduler in a long planner call instead of harvesting the worker result.
    """
    return any(slot_is_exclusive(slot) for slot in active)


def pending_dependency(
    metadata: PlanMetadata,
    pending_keys: dict[str, str],
    done_keys: set[str],
) -> str:
    """The story this one still depends on, if any."""
    for dependency in metadata.depends_on:
        short = dependency.strip()
        if not short:
            continue
        if short in done_keys or any(
            key == short or key.startswith(f"{short.replace('.', '-')}-") for key in done_keys
        ):
            continue
        if short in pending_keys:
            return short
        # The dependency may be written as `N.M` while the sprint status uses
        # the full key; the scheduler resolves it by its short identifier.
        for key in pending_keys:
            if key == short or key.startswith(f"{short.replace('.', '-')}-"):
                return short
        # A dependency absent from the queue was completed before this run.
        return ""
    return ""


def conflicts_with(metadata: PlanMetadata, other: PlanMetadata) -> bool:
    """True when two stories write the same path or touch the same surface."""
    if entries_intersect(metadata.writes, other.writes):
        return True
    if entries_intersect(metadata.shared_surfaces, other.shared_surfaces):
        return True
    if entries_intersect(metadata.writes, other.shared_surfaces):
        return True
    if entries_intersect(other.writes, metadata.shared_surfaces):
        return True
    if entries_intersect(metadata.reads, other.writes):
        return True
    if entries_intersect(other.reads, metadata.writes):
        return True
    return False


def eligibility(
    metadata: PlanMetadata | None,
    metadata_reason: str,
    active: Sequence[Slot],
    pending_keys: dict[str, str],
    done_keys: set[str],
) -> tuple[bool, str]:
    """Whether a story may run next to another worker, and why not.

    A story runs alone when its plan does not prove its independence: no
    metadata block, a sensitive surface, a pending dependency, or a footprint
    that intersects the one of a worker that is already running.
    """
    if metadata is None:
        return False, metadata_reason or "the plan metadata is missing or invalid"
    if len(active) >= MAX_PARALLEL_WORKERS:
        return False, "the parallel window is full"
    for slot in active:
        other = slot.metadata
        if other is None:
            return False, f"story {slot.story.short_id} has invalid metadata and runs exclusively"
        if slot_is_exclusive(slot):
            return False, f"story {slot.story.short_id} has a sensitive surface and runs exclusively"
    sensitive = metadata.sensitive_surfaces()
    if sensitive:
        return False, f"it declares a sensitive surface: {', '.join(sensitive)}"
    dependency = pending_dependency(metadata, pending_keys, done_keys)
    if dependency:
        return False, f"it depends on {dependency}, which is not done yet"
    for slot in active:
        other = getattr(slot, "metadata", None)
        if other is None:
            continue
        if conflicts_with(metadata, other):
            return False, f"it intersects the footprint of story {slot.story.short_id}, which is running"
    return True, ""


# ---------------------------------------------------------------------------
# The driver
# ---------------------------------------------------------------------------


def run_story_exclusively(story, args: argparse.Namespace) -> WorkerResult:
    """Run one story on its own, without another worker running."""
    tree = prepare_worktree(story)
    slot = Slot(story=story, tree=tree)
    slot.start()
    slot.join()
    if slot.result is not None:
        return slot.result
    raise legacy.DriverError(slot.error or f"story {story.short_id} failed")


def process_story(story, args: argparse.Namespace) -> None:
    """Run one story alone, in the canonical checkout.

    This is the recovery path of a story that is already in flight: it keeps
    the semantics of the sequential driver, because a story that is being
    recovered has work in the canonical tree that a worktree would not see.
    """
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
        try:
            run_phase("plan", PLAN_MODEL, plan_prompt(story, plan), log_dir, timeout_minutes=5, retries=1)
        except legacy.DriverError:
            if not is_complete_plan(plan):
                raise
            log("planner timed out after writing a complete plan; continuing without a second attempt")
    if not is_complete_plan(plan):
        raise legacy.DriverError(f"planner did not create {relative(plan)}")

    if implementation_paths(changed_paths(base)):
        log("reusing preserved implementation work; proceeding to verification")
    else:
        run_phase(
            "implementation",
            IMPLEMENT_MODEL,
            implementation_prompt(story, plan),
            log_dir,
            timeout_minutes=30,
            retries=IMPLEMENTATION_MAX_ATTEMPTS,
        )
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
        deadline = time.time() + 120
        while time.time() < deadline:
            legacy.git("fetch", "origin", BASE_BRANCH)
            pr_state = legacy.gh("pr", "view", str(number), "--json", "state,mergeCommit")
            merge_commit = merged_commit_from_pr(pr_state.stdout)
            if merge_commit and legacy.git(
                "merge-base", "--is-ancestor", merge_commit, f"origin/{BASE_BRANCH}", check=False
            ).ok:
                break
            time.sleep(10)
        else:
            raise legacy.DriverError(f"the merge of pull request #{number} is not in origin/main yet")
        status_file = relative(legacy.SPRINT_STATUS_FILE)
        merged_status = legacy.parse_development_status(
            legacy.git("show", f"origin/{BASE_BRANCH}:{status_file}").stdout
        )
        if str(merged_status.get(story.key, "")).lower() != "done":
            raise legacy.DriverError(f"story {story.short_id} is not done after merge")


def plan_story(story) -> tuple[PlanMetadata | None, str]:
    """Plan a candidate story and read its structured metadata.

    The planning happens in the coordinator, before the window opens, because
    the metadata is what decides whether the story may run next to another
    worker. A planner that times out after writing a complete plan is
    accepted: the plan is on disk and the metadata can be read from it.
    """
    plan, _, log_dir = story_paths(story)
    log_dir.mkdir(parents=True, exist_ok=True)
    if not plan.exists():
        try:
            run_phase("plan", PLAN_MODEL, plan_prompt(story, plan), log_dir, timeout_minutes=5, retries=1)
        except legacy.DriverError:
            if not is_complete_plan(plan):
                raise
            log("planner timed out after writing a complete plan; continuing without a second attempt")
    if not is_complete_plan(plan):
        raise legacy.DriverError(f"planner did not create {relative(plan)}")
    return extract_plan_metadata(plan)


def schedule(stories: Sequence, args: argparse.Namespace) -> int:
    """Run the backlog, in parallel where the independence can be proved.

    The window holds up to four workers. A story that cannot prove its
    independence runs alone, after the workers that are running finish. The
    integration of the finished stories is serial.
    """
    if not stories:
        return 0

    queue = list(stories)
    active: list[Slot] = []
    finished: list[WorkerResult] = []
    failures: list[tuple[str, str]] = []
    done_keys: set[str] = set()
    started = 0
    limit = args.max_stories if args.max_stories else len(queue)
    planned: dict[str, tuple[PlanMetadata | None, str]] = {}

    pending_keys = {story.key: story.short_id for story in queue}

    def plan_for(story) -> tuple[PlanMetadata | None, str]:
        """Plan each queued story at most once during a scheduler run."""
        if story.key not in planned:
            planned[story.key] = plan_story(story)
        return planned[story.key]

    def harvest() -> None:
        """Move the workers that finished into the integration queue."""
        nonlocal active
        still_active: list[Slot] = []
        for slot in active:
            if not slot.finished:
                still_active.append(slot)
                continue
            if slot.ok and slot.result is not None:
                finished.append(slot.result)
            else:
                preserve_worktree(slot.tree)
                failures.append((slot.story.short_id, slot.error or "the worker failed"))
        active = still_active

    def admit(story, metadata: PlanMetadata | None) -> Slot:
        """Start the worker of a story in its own worktree."""
        nonlocal started
        tree = prepare_worktree(story)
        slot = Slot(story=story, tree=tree, metadata=metadata)
        slot.start()
        active.append(slot)
        started += 1
        return slot

    while queue or active or finished:
        harvest()
        if finished:
            # The integration is serial: one story at a time against main.
            result = finished.pop(0)
            try:
                integrate(result, args)
                done_keys.add(result.story.key)
                discard_worktree(result.worktree)
                log(f"{result.story.short_id}: merged")
            except legacy.DriverError as error:
                preserve_worktree(result.worktree)
                failures.append((result.story.short_id, str(error)))
            continue

        if not active:
            # No worker is running and nothing is ready to integrate: the
            # next story starts, and it starts alone because there is nothing
            # to run next to.
            if not queue:
                break
            if started >= limit:
                queue.clear()
                break
            story = queue.pop(0)
            metadata, reason = plan_for(story)
            if metadata is None:
                log(f"{story.short_id}: {reason}; the story runs alone")
            eligible, why = eligibility(metadata, reason, [], pending_keys, done_keys)
            if not eligible and metadata is not None and "depends on" in why:
                failures.append((story.short_id, why))
                continue
            admit(story, metadata)
            continue

        if active_window_is_exclusive(active):
            # Do not spend time planning stories that cannot run until the
            # exclusive worker is harvested.
            time.sleep(1)
            continue

        # There is room in the window: admit the next story when it can prove
        # its independence from the workers that are running.
        room = MAX_PARALLEL_WORKERS - len(active)
        while room > 0 and queue and started < limit:
            story = next((candidate for candidate in queue if eligibility(*plan_for(candidate), active, pending_keys, done_keys)[0]), None)
            if story is None:
                break
            metadata, reason = plan_for(story)
            eligible, why = eligibility(metadata, reason, active, pending_keys, done_keys)
            if not eligible:
                # The story cannot run next to the workers that are running.
                # It waits for the window to empty instead of guessing.
                log(f"{story.short_id}: waits, {why}")
                break
            queue.remove(story)
            admit(story, metadata)
            room -= 1

        time.sleep(1)

    if failures:
        for story_id, error in failures:
            log(f"FAIL {story_id}: {error}")
        return 1
    return 0


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
    if command[-1] != "BUILD_LITE_WORKER:\nplan":
        failures.append("phase command is missing the Build Lite worker marker")
    if PHASE_MAX_ATTEMPTS != 2:
        failures.append("phase retry budget is not fixed at two technical attempts")
    if IMPLEMENTATION_MAX_ATTEMPTS != 1:
        failures.append("implementation retry budget is not fixed at one attempt")
    if blocking_review_line("OK\n"):
        failures.append("an OK review was parsed as blocking")
    if not blocking_review_line("note\nBLOCKING: an S1 is proven\n"):
        failures.append("a blocking review result was not parsed")
    if merged_commit_from_pr('{"state":"MERGED","mergeCommit":{"oid":"abc"}}') != "abc":
        failures.append("a merged squash commit was not parsed")
    if merged_commit_from_pr('{"state":"OPEN","mergeCommit":{"oid":"abc"}}'):
        failures.append("an open pull request was parsed as merged")
    self_test_plan = PLAN_ROOT / "self-test-plan.md"
    self_test_plan.parent.mkdir(parents=True, exist_ok=True)
    self_test_plan.write_text("# Story 0.0\n\n## Alcance\n\n" + "x" * 300 + "\n\n## Ficheros\n", encoding="utf-8")
    if not is_complete_plan(self_test_plan):
        failures.append("a complete plan was rejected")
    self_test_plan.unlink(missing_ok=True)
    if implementation_paths([".build-lite/plans/3-1.md"]):
        failures.append("Build Lite bookkeeping was parsed as implementation work")
    if implementation_paths([".build-lite/plans/3-1.md", "src/Server/Program.cs"]) != ["src/Server/Program.cs"]:
        failures.append("preserved implementation work was not detected")

    failures.extend(self_test_scheduler())

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1
    print("the build-lite self test passed")
    return 0


def self_test_scheduler() -> list[str]:
    """The invariants of the parallel scheduler, against synthetic plans."""
    failures: list[str] = []
    plans = PLAN_ROOT / "self-test-scheduler"
    plans.mkdir(parents=True, exist_ok=True)

    def write(name: str, body: str) -> Path:
        path = plans / name
        path.write_text(body, encoding="utf-8")
        return path

    def metadata_of(body: str, name: str = "plan.md"):
        return extract_plan_metadata(write(name, body))

    # A plan without the metadata block is ambiguous and runs alone.
    metadata, reason = metadata_of(
        "# Story 1.1\n\n## Alcance\n\nalgo\n\n## Ficheros\n\n- `src/Server/Program.cs`\n"
    )
    if metadata is not None:
        failures.append("a plan without a metadata block was accepted")
    if not reason:
        failures.append("a plan without a metadata block did not record a reason")

    # A metadata block with a missing key is invalid.
    metadata, reason = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Server/Program.cs"]}\n```\n'
    )
    if metadata is not None:
        failures.append("a metadata block with a missing key was accepted")
    if "shared_surfaces" not in reason or "depends_on" not in reason:
        failures.append("an invalid metadata block did not name the missing keys")

    # A metadata block with a value that is not a list of strings is invalid.
    metadata, _ = metadata_of(
        '```json\n{"reads": [], "writes": "src/Server/Program.cs", '
        '"shared_surfaces": [], "depends_on": []}\n```\n'
    )
    if metadata is not None:
        failures.append("a metadata block with a scalar value was accepted")

    # A valid metadata block is read.
    metadata, reason = metadata_of(
        '```json\n{"reads": ["src/Shared/Contracts/"], "writes": ["src/Server/Program.cs"], '
        '"shared_surfaces": [], "depends_on": []}\n```\n'
    )
    if metadata is None:
        failures.append("a valid metadata block was rejected")
    else:
        if metadata.writes != ("src/Server/Program.cs",):
            failures.append("the writes of a metadata block were not read")
        if metadata.reads != ("src/Shared/Contracts/",):
            failures.append("the reads of a metadata block were not read")

    # A sensitive surface runs alone.
    metadata, _ = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Server/Program.cs"], '
        '"shared_surfaces": ["persistence"], "depends_on": []}\n```\n'
    )
    if metadata is None:
        failures.append("a metadata block with a sensitive surface was rejected")
    else:
        eligible, why = eligibility(metadata, "", [], {}, set())
        if eligible:
            failures.append("a story with a sensitive surface was admitted to the parallel window")
        if "persistence" not in why:
            failures.append("a sensitive surface did not record its reason")

        sensitive_slot = Slot(story=object(), tree=object(), metadata=metadata)
        if not active_window_is_exclusive([sensitive_slot]):
            failures.append("a sensitive active worker did not block further planning")

    ambiguous_slot = Slot(story=object(), tree=object(), metadata=None)
    if not active_window_is_exclusive([ambiguous_slot]):
        failures.append("an ambiguous active worker did not block further planning")

    independent_slot = Slot(
        story=object(),
        tree=object(),
        metadata=PlanMetadata(writes=("src/Server/Program.cs",)),
    )
    if active_window_is_exclusive([independent_slot]):
        failures.append("an independent active worker blocked further planning")

    # A pending dependency runs alone.
    metadata, _ = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Server/Program.cs"], '
        '"shared_surfaces": [], "depends_on": ["5.2"]}\n```\n'
    )
    if metadata is None:
        failures.append("a metadata block with a dependency was rejected")
    else:
        eligible, why = eligibility(metadata, "", [], {"5-2-descarga": "5.2"}, set())
        if eligible:
            failures.append("a story with a pending dependency was admitted to the parallel window")
        if "5.2" not in why:
            failures.append("a pending dependency did not record its reason")
        eligible, _ = eligibility(metadata, "", [], {"5-2-descarga": "5.2"}, {"5-2-descarga"})
        if not eligible:
            failures.append("a story whose dependency is done was kept out of the parallel window")

    # Two stories that write the same path never run at the same time.
    left, _ = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Server/Application/Catalog/"], '
        '"shared_surfaces": [], "depends_on": []}\n```\n', "left.md"
    )
    right, _ = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Server/Application/Catalog/Index.cs"], '
        '"shared_surfaces": [], "depends_on": []}\n```\n', "right.md"
    )
    if left is None or right is None:
        failures.append("the intersection fixture could not be built")
    else:
        if not conflicts_with(left, right):
            failures.append("two stories writing the same path were considered independent")
        if conflicts_with(
            left,
            PlanMetadata(writes=("src/Server/Application/Other/",), shared_surfaces=()),
        ):
            failures.append("two stories writing different paths were considered conflicting")

    # Two stories that declare the same surface never run at the same time.
    left, _ = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Server/A.cs"], '
        '"shared_surfaces": ["catalog"], "depends_on": []}\n```\n', "surface-left.md"
    )
    right, _ = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Client/B.cs"], '
        '"shared_surfaces": ["catalog"], "depends_on": []}\n```\n', "surface-right.md"
    )
    if left is None or right is None:
        failures.append("the surface fixture could not be built")
    elif not conflicts_with(left, right):
        failures.append("two stories sharing a surface were considered independent")

    # The window never holds more than four workers.
    if MAX_PARALLEL_WORKERS != 4:
        failures.append("the parallel window is not fixed at four workers")
    slots = [Slot(story=object(), tree=object()) for _ in range(MAX_PARALLEL_WORKERS)]
    metadata, _ = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Server/C.cs"], '
        '"shared_surfaces": [], "depends_on": []}\n```\n', "window.md"
    )
    if metadata is None:
        failures.append("the window fixture could not be built")
    else:
        eligible, _ = eligibility(metadata, "", slots, {}, set())
        if eligible:
            failures.append("a fifth worker was admitted to the window")

    # A failed worker keeps its worktree: it is never removed.
    story = type("S", (), {"short_id": "9.9", "label": "9-9", "branch": "story/9-9-x"})()
    tree = Worktree(story=story, path=WORKTREE_ROOT / "9-9", branch=story.branch)
    tree.path.mkdir(parents=True, exist_ok=True)
    preserve_worktree(tree)
    if not tree.path.exists():
        failures.append("the worktree of a failed story was removed")
    elif tree.path.exists() and not any(WORKTREE_ROOT.glob("9-9")):
        failures.append("the worktree of a failed story was not preserved")

    shutil.rmtree(tree.path, ignore_errors=True)

    for path in plans.glob("*"):
        path.unlink(missing_ok=True)
    plans.rmdir()
    return failures


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
    if args.story:
        # One story on its own keeps the recovery semantics of the sequential
        # driver. A preserved parallel worktree must be resumed in place: Git
        # refuses to check out its branch in the canonical checkout.
        tree = worktree_for(stories[0])
        if legacy.worktree_registered(tree.path):
            metadata, _ = plan_story(stories[0])
            result = run_worker(stories[0], prepare_worktree(stories[0]), metadata)
            integrate(result, args)
            if not args.no_merge:
                discard_worktree(result.worktree)
            return 0
        process_story(stories[0], args)
        return 0
    try:
        return schedule(stories, args)
    except legacy.DriverError:
        legacy.preserve_pending_work(stories[0])
        raise


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except legacy.DriverError as error:
        print(f"STOP: {error}", file=sys.stderr)
        raise SystemExit(1)
