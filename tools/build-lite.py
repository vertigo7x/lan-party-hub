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
import hashlib
import importlib.util
import json
import re
import shutil
import subprocess
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
CONTEXT_ROOT = REPO_ROOT / ".build-lite" / "contexts"
RESULT_ROOT = REPO_ROOT / ".build-lite" / "results"
WORKTREE_ROOT = REPO_ROOT / ".build-lite" / "worktrees"
BASE_BRANCH = "main"
PLAN_MODEL = "minimax-coding-plan/MiniMax-M3.1-Flash"
CONTEXT_MODEL = "minimax-coding-plan/MiniMax-M3.1-Flash"
IMPLEMENT_MODEL = "minimax-coding-plan/MiniMax-M3"
REVIEW_MODEL = "minimax-coding-plan/MiniMax-M3.1-Flash"
PHASE_MAX_ATTEMPTS = 2
IMPLEMENTATION_MAX_ATTEMPTS = 1

# The preparation of the context is a phase with a brief budget: one attempt
# plus one technical retry, the same rule as any other provider phase.
CONTEXT_MAX_ATTEMPTS = PHASE_MAX_ATTEMPTS
CONTEXT_TIMEOUT_MINUTES = 5

# The context package is an execution input of the worker, never a source of
# truth: it lives in the local cache and never in the history of the story.
CONTEXT_PATH_PREFIX = ".build-lite/contexts/"

# The headings that the preparer has to produce, and the sentence that states
# that the preparer only prepares. A package without them is incomplete and
# the phase runs again instead of handing a half-written package to a worker.
CONTEXT_REQUIRED_TOKENS: tuple[str, ...] = (
    "requisitos",
    "decisiones",
    "patrones",
    "archivos",
    "pruebas",
    "comandos",
    "exclusiones",
)
CONTEXT_RESTRICTION_TOKENS: tuple[str, ...] = (
    "no puede",
    "implementar",
    "huella",
    "contratos",
    "alcance",
)
CONTEXT_MIN_LENGTH = 300

# A Windows command line is limited to 32,767 characters. Keep every inline
# source well below that ceiling; the preparer can open only the declared plan
# paths when it needs the source file itself.
CONTEXT_PROMPT_MAX_CHARS = 24000
EPIC_CONTEXT_MAX_CHARS = 12000
CONTEXT_PLAN_DIGEST_PREFIX = "<!-- build-lite-plan-sha256: "
PLAN_SOURCE_MAX_CHARS = 18000
PLAN_ARTIFACT_DEADLINE_SECONDS = 90
REVIEW_PACKET_MAX_CHARS = 140000

# ---------------------------------------------------------------------------
# The structured state of a phase
# ---------------------------------------------------------------------------

# Every phase ends in one of these states. They tell a technical timeout apart
# from a worker that only explores, and they are recorded per phase so a run
# that stops explains itself without reading the whole transcript.
PHASE_COMPLETE = "complete"
PHASE_TIMEOUT = "timeout"
PHASE_STALLED = "stalled"
PHASE_TOOL_FAILURE = "tool_failure"
PHASE_VERIFICATION_FAILURE = "verification_failure"
PHASE_ESCALATED = "escalated"
PHASE_STATES: tuple[str, ...] = (
    PHASE_COMPLETE,
    PHASE_TIMEOUT,
    PHASE_STALLED,
    PHASE_TOOL_FAILURE,
    PHASE_VERIFICATION_FAILURE,
    PHASE_ESCALATED,
)

# The exit codes the watcher uses to tell the three endings apart.
EXIT_TIMEOUT = 124
EXIT_STALLED = 125

# How often the watcher asks the progress probe for a milestone.
PHASE_POLL_SECONDS = 15

# The window without a milestone that turns an implementation into a stalled
# one. It is shorter than the timeout of the phase, so a worker that only
# explores stops while there is still budget left to act on the evidence.
IMPLEMENTATION_PROGRESS_WINDOW_MINUTES = 15

# The marker written through a real command when the worker reports a
# demonstrated blocker. It is never inferred from prompts or tool output.
BLOCKER_MARKER = "BLOQUEO:"

# The milestones an implementation can produce while it runs.
MILESTONE_FUNCTIONAL_CHANGE = "functional_change"
MILESTONE_TEST_EXECUTED = "test_executed"
MILESTONE_BLOCKER_DEMONSTRATED = "blocker_demonstrated"
MILESTONE_KINDS: tuple[str, ...] = (
    MILESTONE_FUNCTIONAL_CHANGE,
    MILESTONE_TEST_EXECUTED,
    MILESTONE_BLOCKER_DEMONSTRATED,
)

# A test milestone comes from the input of a completed shell tool call, never
# from a substring in a prompt, context package or tool output.
TEST_COMMAND_PATTERN = re.compile(r"(?:^|[;&|]\s*)(?:dotnet\s+test)\b", re.IGNORECASE)
FAILED_TEST_PATTERN = re.compile(
    r"^\s*(?:Con error|Failed)\s+([A-Za-z_][\w]*(?:\.[A-Za-z_][\w]*){2,})\b",
    re.IGNORECASE | re.MULTILINE,
)
VERIFICATION_EVIDENCE_MAX_CHARS = 4000

# The window of the scheduler. Four workers is the ceiling of the project:
# a fifth story waits for a free slot instead of starting.
MAX_PARALLEL_WORKERS = 4

# The surfaces that a story may not touch while another worker is running.
SENSITIVE_SURFACES: tuple[str, ...] = (
    "contract",
    "contracts",
    "contrato",
    "contratos",
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


@dataclass
class PhaseOutcome:
    """How a phase ended, with the evidence it left."""

    name: str
    state: str
    attempt: int = 1
    seconds: float = 0.0
    log: Path | None = None
    milestones: list[dict] = field(default_factory=list)
    error: str = ""


class PhaseJournal:
    """The structured record of the phases of a story.

    Every phase leaves its state and its milestones in a JSON file next to the
    logs of the story, so a run that stops explains why without reading the
    whole transcript. The record survives a resume: the phases of the first
    attempt stay next to the phases of the second one.
    """

    def __init__(self, path: Path):
        self.path = path
        self.lock = threading.Lock()
        self.entries: list[dict] = []
        if path.exists():
            try:
                loaded = json.loads(path.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError):
                loaded = []
            if isinstance(loaded, list):
                self.entries = [entry for entry in loaded if isinstance(entry, dict)]

    def record(
        self,
        name: str,
        state: str,
        attempt: int = 1,
        seconds: float = 0.0,
        milestones: Sequence[dict] | None = None,
        evidence: str = "",
        error: str = "",
    ) -> dict:
        entry = {
            "phase": name,
            "state": state,
            "attempt": attempt,
            "seconds": round(seconds, 3),
            "milestones": list(milestones or []),
            "evidence": evidence,
            "error": error,
            "at": datetime.now().isoformat(timespec="seconds"),
        }
        with self.lock:
            self.entries.append(entry)
            self.path.parent.mkdir(parents=True, exist_ok=True)
            self.path.write_text(
                json.dumps(self.entries, indent=2, ensure_ascii=False), encoding="utf-8"
            )
        return entry

    def state_of(self, name: str) -> str:
        """The last state recorded for a phase."""
        for entry in reversed(self.entries):
            if entry.get("phase") == name:
                return str(entry.get("state", ""))
        return ""

    def failure_state(self) -> str:
        """The state that explains why the story did not finish."""
        for entry in reversed(self.entries):
            state = str(entry.get("state", ""))
            if state and state != PHASE_COMPLETE:
                return state
        return PHASE_ESCALATED


def journal_for(story) -> PhaseJournal:
    """The journal of the phases of a story."""
    return PhaseJournal(LOG_ROOT / story.label / "phases.json")


def state_for_exit_code(code: int) -> str:
    """The state of a phase from the exit code of its process."""
    if code == 0:
        return PHASE_COMPLETE
    if code == EXIT_TIMEOUT:
        return PHASE_TIMEOUT
    if code == EXIT_STALLED:
        return PHASE_STALLED
    return PHASE_TOOL_FAILURE


def phase_may_retry(state: str, attempt: int, retries: int) -> bool:
    """Whether a phase can be attempted again.

    A stalled phase is never retried: the worker was not producing anything,
    and repeating it would spend the same budget for the same silence.
    """
    if state in (PHASE_COMPLETE, PHASE_STALLED):
        return False
    return attempt < retries


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
    schema_changes: bool = False

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
        if "schema_changes" not in data:
            return None, "the plan metadata is missing the key: schema_changes"
        schema_changes = data["schema_changes"]
        if not isinstance(schema_changes, bool):
            return None, "the plan metadata key 'schema_changes' must be a boolean"
        return (
            PlanMetadata(
                reads=parsed["reads"],
                writes=parsed["writes"],
                shared_surfaces=parsed["shared_surfaces"],
                depends_on=parsed["depends_on"],
                schema_changes=schema_changes,
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


def context_path(story) -> Path:
    """The local cache path of the context package of a story."""
    return CONTEXT_ROOT / f"{story.label}.md"


def is_context_path(path: str) -> bool:
    """True when a path points at a context package."""
    folded = path.replace("\\", "/")
    return folded.startswith(CONTEXT_PATH_PREFIX) or folded == CONTEXT_PATH_PREFIX.rstrip("/")


def is_complete_plan(plan: Path) -> bool:
    """A planner may finish its file before its CLI session reaches the timeout."""
    return not plan_errors(plan)


def plan_errors(plan: Path) -> list[str]:
    """Explain why a plan cannot safely guide a worker or the scheduler."""
    try:
        content = plan.read_text(encoding="utf-8")
    except OSError as error:
        return [f"no se pudo leer {relative(plan)}: {error}"]
    errors = []
    if len(content.strip()) < 300:
        errors.append("el plan tiene menos de 300 caracteres")
    words = len(content.split())
    if words > 500:
        errors.append(f"el plan tiene {words} palabras (máximo 500)")
    for heading in ("# Story", "## Alcance", "## Ficheros"):
        if not re.search(rf"^{re.escape(heading)}(?:\s|$)", content, re.MULTILINE):
            errors.append(f"falta el encabezado {heading}")
    match = re.fullmatch(r"(\d+)-(\d+)", plan.stem)
    if match and not re.search(rf"^# Story {match[1]}\.{match[2]}(?:\s|$)", content, re.MULTILINE):
        errors.append(f"el encabezado debe identificar la Story {match[1]}.{match[2]}")
    metadata, issue = extract_plan_metadata(plan)
    if issue:
        errors.append(issue)
    if metadata:
        for key in ("reads", "writes"):
            for path in getattr(metadata, key):
                normalized = path.replace("\\", "/")
                candidate = REPO_ROOT / normalized
                if (not normalized.startswith(("src/", "tests/", "tools/", "docs/", "_bmad-output/"))
                        or ":" in normalized or any(part in (".", "..") for part in normalized.split("/"))
                        or not candidate.parent.is_dir()
                        or (key == "reads" and not candidate.exists())):
                    errors.append(f"{key}: {path!r} no es una ruta concreta válida del repositorio")
    return errors


def require_valid_plan(plan: Path, log_dir: Path, journal: PhaseJournal) -> None:
    errors = plan_errors(plan)
    if not errors:
        return
    evidence = log_dir / "rejected-plan.md"
    if plan.is_file():
        evidence.write_bytes(plan.read_bytes())
    message = f"plan {relative(plan)} rechazado: " + "; ".join(errors)
    journal.record("plan-validation", PHASE_TOOL_FAILURE, evidence=relative(evidence), error=message)
    raise legacy.DriverError(message)


def changed_paths(base: str, cwd: Path | None = None) -> list[str]:
    output = legacy.git("diff", "--name-only", f"{base}..HEAD", check=False, cwd=cwd).stdout
    return [line.strip() for line in output.splitlines() if line.strip()]


def implementation_paths(paths: Sequence[str]) -> list[str]:
    """Exclude Build Lite bookkeeping when deciding whether work can be resumed.

    The context packages are an execution input of the worker: they are a local
    cache, so they never count as a change of the story.
    """
    return [
        path
        for path in paths
        if not path.replace("\\", "/").startswith(".build-lite/") and not is_context_path(path)
    ]


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
    journal: PhaseJournal | None = None,
    progress: "ImplementationProgress | None" = None,
    progress_window_minutes: int = 0,
    poll_seconds: float = PHASE_POLL_SECONDS,
    artifact_path: Path | None = None,
) -> PhaseOutcome:
    """Run a bounded provider phase; a retry is technical only, never a new loop.

    A phase that ends stalled is not retried: the worker was not producing
    anything, and repeating it would spend the same budget again for the same
    silence.
    """
    for attempt in range(1, retries + 1):
        log(f"{name}: attempt {attempt} of {retries}")
        log_path = log_dir / f"{name}-{attempt}.jsonl"
        code, seconds, milestones = watch_process(
            phase_command("build", model, prompt),
            log_path,
            timeout=timeout_minutes * 60,
            cwd=cwd or REPO_ROOT,
            progress=progress,
            progress_window=progress_window_minutes * 60,
            poll_seconds=poll_seconds,
            artifact_path=artifact_path,
        )
        state = state_for_exit_code(code)
        if journal is not None:
            journal.record(
                name,
                state,
                attempt=attempt,
                seconds=seconds,
                milestones=milestones,
                evidence=relative(log_path) if log_path else "",
            )
        if state == PHASE_COMPLETE:
            return PhaseOutcome(
                name=name,
                state=state,
                attempt=attempt,
                seconds=seconds,
                log=log_path,
                milestones=milestones,
            )
        if state == PHASE_STALLED:
            raise legacy.DriverError(
                f"{name} ended stalled: the worker produced no change, no test and no blocker"
            )
        if not phase_may_retry(state, attempt, retries):
            break
    raise legacy.DriverError(f"{name} failed after {retries} technical attempt(s); see {log_dir}")


def watch_process(
    argv: Sequence[str],
    log_path: Path,
    timeout: float | None,
    cwd: Path,
    progress: "ImplementationProgress | None" = None,
    progress_window: float = 0.0,
    poll_seconds: float = PHASE_POLL_SECONDS,
    artifact_path: Path | None = None,
) -> tuple[int, float, list[dict]]:
    """Run a command and stop it when it stops making progress.

    The process writes to the log while it runs, so the log is the evidence of
    what it did. The progress probe is asked for a milestone on every poll:
    when it stops returning one for the whole window, the run is stalled and
    it is stopped before the global timeout.
    """
    started = time.time()
    log_path.parent.mkdir(parents=True, exist_ok=True)
    milestones: list[dict] = []
    with log_path.open("w", encoding="utf-8", errors="replace") as handle:
        handle.write(f"$ {' '.join(argv)}\n\n")
        handle.flush()
        process = subprocess.Popen(
            list(argv),
            cwd=str(cwd),
            stdout=handle,
            stderr=subprocess.STDOUT,
            stdin=subprocess.DEVNULL,
        )
        code = 0
        last_progress = time.time()
        while True:
            try:
                code = process.wait(timeout=poll_seconds)
                break
            except subprocess.TimeoutExpired:
                pass
            if timeout is not None and time.time() - started > timeout:
                legacy._kill_process_tree(process)
                wait_quietly(process)
                code = EXIT_TIMEOUT
                break
            if artifact_path is not None and is_complete_plan(artifact_path):
                metadata, _ = extract_plan_metadata(artifact_path)
                if metadata is not None:
                    log("plan artifact complete; ending the planner session")
                    legacy._kill_process_tree(process)
                    wait_quietly(process)
                    code = 0
                    break
            if (artifact_path is not None and time.time() - started > PLAN_ARTIFACT_DEADLINE_SECONDS
                    and not is_complete_plan(artifact_path)):
                log(f"plan did not produce a complete artifact within {PLAN_ARTIFACT_DEADLINE_SECONDS} seconds")
                legacy._kill_process_tree(process)
                wait_quietly(process)
                code = EXIT_TIMEOUT
                break
            if progress is None:
                continue
            milestone = progress.poll()
            if milestone is not None:
                milestones.append(milestone)
                last_progress = time.time()
            elif progress_window and time.time() - last_progress > progress_window:
                log(f"the phase stopped making progress for {describe_window(progress_window)}")
                legacy._kill_process_tree(process)
                wait_quietly(process)
                code = EXIT_STALLED
                break
    return code, time.time() - started, milestones


def wait_quietly(process) -> None:
    """Wait for a process that was already stopped, without failing on it."""
    try:
        process.wait(timeout=30)
    except subprocess.TimeoutExpired:
        pass


def describe_window(seconds: float) -> str:
    """How a window of time is written in the log."""
    if seconds < 60:
        return f"{seconds:.0f} seconds"
    return f"{seconds / 60:.0f} minutes"


class ImplementationProgress:
    """The observable milestones of an implementation that is still running.

    A milestone is a functional change in the worktree, a test that ran, or a
    blocker that the worker demonstrated. Reading files is not a milestone: an
    implementation that only explores never produces one, and noticing that is
    the whole point of this probe.
    """

    def __init__(self, tree_path: Path, transcript: Path):
        self.tree_path = tree_path
        self.transcript = transcript
        self.fingerprint = self._fingerprint()
        self.read_bytes = 0
        self.pending: list[dict] = []

    def poll(self) -> dict | None:
        """The newest milestone, or None when there is nothing new."""
        change = self._functional_change()
        if change is not None:
            return change
        return self._transcript_milestone()

    def _is_artifact(self, path: str) -> bool:
        """True when a path is the evidence of the run and not a change."""
        folded = path.replace("\\", "/")
        if folded.startswith(".build-lite/"):
            return True
        try:
            return (self.tree_path / path).resolve() == self.transcript.resolve()
        except OSError:
            return False

    def _functional_change(self) -> dict | None:
        """A milestone when the worktree changed since the last poll."""
        fingerprint = self._fingerprint()
        if fingerprint == self.fingerprint:
            return None
        changed = self._changed_paths()
        self.fingerprint = fingerprint
        return self._milestone(
            MILESTONE_FUNCTIONAL_CHANGE,
            ", ".join(changed) if changed else "the worktree changed",
        )

    def _fingerprint(self) -> str:
        """The state of the changed files of the worktree."""
        result = legacy.git("status", "--porcelain", check=False, cwd=self.tree_path)
        if not result.ok:
            return ""
        entries: list[str] = []
        for path in self._paths(result.stdout):
            try:
                stat = (self.tree_path / path).stat()
                entries.append(f"{path}={stat.st_mtime_ns}:{stat.st_size}")
            except OSError:
                entries.append(f"{path}=missing")
        return "\n".join(sorted(entries))

    def _changed_paths(self) -> list[str]:
        result = legacy.git("status", "--porcelain", check=False, cwd=self.tree_path)
        if not result.ok:
            return []
        return self._paths(result.stdout)

    def _paths(self, status: str) -> list[str]:
        """The paths of a `git status --porcelain` that are changes of the story."""
        paths: list[str] = []
        for line in status.splitlines():
            if len(line) < 4:
                continue
            path = line[3:].strip().strip('"')
            if path and not self._is_artifact(path):
                paths.append(path)
        return paths

    def _transcript_milestone(self) -> dict | None:
        """Read milestones only from completed, structured tool events.

        The log also contains the command header and provider text. Neither is
        evidence of work: prompts can mention a test or a blocker before the
        worker starts. JSON tool events retain the actual command invocation.
        """
        for event in self._new_events():
            part = event.get("part")
            if not isinstance(part, dict) or part.get("type") != "tool":
                continue
            state = part.get("state")
            if not isinstance(state, dict) or state.get("status") != "completed":
                continue
            command = self._tool_command(state)
            if not command:
                continue
            if TEST_COMMAND_PATTERN.search(command):
                self.pending.append(
                    self._milestone(MILESTONE_TEST_EXECUTED, "the worker ran dotnet test")
                )
            if self._is_blocker_command(command):
                self.pending.append(
                    self._milestone(
                        MILESTONE_BLOCKER_DEMONSTRATED, "the worker recorded a demonstrated blocker"
                    )
                )
        if self.pending:
            return self.pending.pop(0)
        return None

    @staticmethod
    def _tool_command(state: dict) -> str:
        """Extract a shell command from a real tool event, if it has one."""
        input_data = state.get("input")
        if not isinstance(input_data, dict):
            return ""
        command = input_data.get("command")
        return command if isinstance(command, str) else ""

    @staticmethod
    def _is_blocker_command(command: str) -> bool:
        """Whether a shell command deliberately records the required blocker."""
        normalized = command.strip().lower()
        marker = BLOCKER_MARKER.lower()
        return marker in normalized and (
            normalized.startswith("write-output")
            or normalized.startswith("echo ")
            or normalized.startswith("printf ")
        )

    def _new_events(self) -> list[dict]:
        """The JSON events written since the previous poll, excluding the header."""
        try:
            size = self.transcript.stat().st_size
        except OSError:
            return []
        if size <= self.read_bytes:
            return []
        try:
            with self.transcript.open("rb") as handle:
                handle.seek(self.read_bytes)
                chunk = handle.read()
        except OSError:
            return []
        self.read_bytes = size
        events: list[dict] = []
        for line in chunk.decode("utf-8", errors="replace").splitlines():
            line = line.strip()
            if not line.startswith("{"):
                continue
            try:
                event = json.loads(line)
            except json.JSONDecodeError:
                continue
            if isinstance(event, dict):
                events.append(event)
        return events

    @staticmethod
    def _milestone(kind: str, detail: str) -> dict:
        return {
            "kind": kind,
            "detail": detail,
            "at": datetime.now().isoformat(timespec="seconds"),
        }


def plan_prompt(story, plan: Path) -> str:
    section = extract_story_section(story)
    epic_path = legacy.ARTIFACTS_DIR / f"epic-{story.epic}-context.md"
    # The full compiled epic repeats the story's AC and exceeds the practical
    # Windows command-line budget of the installed opencode launcher. The
    # preparer receives the complete epic later; planning only needs its goal.
    epic_context = (
        epic_path.read_text(encoding="utf-8").split("## Stories", 1)[0].strip()
        if epic_path.exists() else "(no existe contexto compilado)"
    )
    sources = f"Historia:\n{section}\n\nContexto de épica:\n{epic_context}"
    if len(sources) > PLAN_SOURCE_MAX_CHARS:
        raise legacy.DriverError(f"story {story.short_id} planning sources exceed the bounded prompt; split or condense the epic context")
    # The 9.2 plan failed because the story text identifies roles but not filenames.
    # Supply existing candidates without giving the planner permission to invent paths.
    # The 9.3 plan failed the same way: the planner explored the tree instead of
    # writing the artifact within its budget, so its routes are preloaded too.
    candidates_by_story = {
        "9.2": (
            "src/Shared/Contracts/ClientStateChanged.cs",
            "src/Shared/Contracts/DownloadProgressUpdated.cs",
            "src/Shared/Contracts/InstallationProgressUpdated.cs",
            "src/Shared/Contracts/StateDisplayMap.cs",
            "src/Server/Host/CatalogHub.cs",
            "src/Server/Application/Clients/ReportClientState.cs",
            "src/Server/Infrastructure/Persistence/CatalogDbContext.cs",
            "src/Server/Infrastructure/Persistence/Entities/ClientInstallationView.cs",
            "src/Server/Infrastructure/Persistence/Migrations/CatalogDbContextModelSnapshot.cs",
            "src/Client/Application/ClientInstallation.cs",
            "src/Client/Application/Ports/IClientStatePublisher.cs",
            "src/Client/Infrastructure/Networking/ClientStateHubPublisher.cs",
            "src/Client/Infrastructure/Persistence/ClientStateDbContext.cs",
            "tests/IntegrationTests/ClientStateHubTests.cs",
            "tests/IntegrationTests/InstallationPersistenceTests.cs",
            "tests/UnitTests/ClientsTableTests.cs",
        ),
        "9.3": (
            "src/Shared/Contracts/ClientErrorReported.cs",
            "src/Shared/Contracts/ApiError.cs",
            "src/Shared/Contracts/ErrorCatalog.cs",
            "src/Shared/Contracts/ClientRegistrationView.cs",
            "src/Server/Host/CatalogHub.cs",
            "src/Server/Application/Ports/IClientErrorRepository.cs",
            "src/Server/Application/Clients/ListClients.cs",
            "src/Server/Infrastructure/Persistence/ClientErrorRepository.cs",
            "src/Server/Infrastructure/Persistence/Entities/ClientErrorEntity.cs",
            "src/Server/Presentation/Clients/ClientsViewModel.cs",
            "src/Server/Presentation/Clients/ClientsWindow.cs",
            "src/Client/Application/Clients/PublishClientState.cs",
            "src/Client/Host/ClientRegistrationComposition.cs",
            "src/Client/Infrastructure/Networking/ClientStateHubPublisher.cs",
            "src/Client/Presentation/Errors/ErrorToast.cs",
            "src/Client/Presentation/Errors/ErrorMessageViewModel.cs",
            "tests/IntegrationTests/ClientProjectionTests.cs",
            "tests/UnitTests/ClientsTableTests.cs",
        ),
        "9.4": (
            "src/Shared/Contracts/ApiError.cs",
            "src/Shared/Contracts/ErrorCatalog.cs",
            "src/Client/Application/Ports/IClientStateStore.cs",
            "src/Client/Application/Ports/IClientCorrelationScope.cs",
            "src/Client/Application/Downloads/DownloadCoordinator.cs",
            "src/Client/Application/Install/InstallationCoordinator.cs",
            "src/Client/Infrastructure/Persistence/ClientStateStore.cs",
            "src/Client/Infrastructure/Persistence/ClientStateDbContext.cs",
            "src/Client/Infrastructure/Persistence/Entities/DownloadOperationEntity.cs",
            "src/Client/Infrastructure/Persistence/Migrations/20261028075729_InitialClientStateSchema.cs",
            "src/Client/Presentation/MainWindow.cs",
            "src/Client/Presentation/Design/DesignTokens.cs",
            "src/Client/Presentation/Errors/ErrorMessageViewModel.cs",
            "src/Client/Presentation/Errors/ErrorToast.cs",
            "src/Client/Presentation/Downloads/DownloadProgressViewModel.cs",
            "src/Client/Presentation/Install/InstallProgressViewModel.cs",
            "src/Client/Presentation/GameLauncher/GamePlayActionsViewModel.cs",
            "src/Client/Program.cs",
            "tests/IntegrationTests/ClientStateStoreConcurrencyTests.cs",
            "tests/UnitTests/ClientRegistrationViewTests.cs",
        ),
        "10.1": (
            "src/Shared/Contracts/CatalogViewState.cs",
            "src/Shared/Contracts/CatalogVersionOrder.cs",
            "src/Shared/Contracts/StateDisplayMap.cs",
            "src/Client/Application/Catalog/CatalogViewStateResolver.cs",
            "src/Client/Application/CatalogSync/CatalogSnapshotApplier.cs",
            "src/Client/Application/GameLauncher/PlayabilityDecision.cs",
            "src/Client/Application/GameLauncher/PlayabilityResolver.cs",
            "src/Client/Application/GameLauncher/PlayabilityProjection.cs",
            "src/Client/Presentation/GameLauncher/GamePlayActionsViewModel.cs",
            "src/Client/Presentation/GameLauncher/GamePlayActionsView.cs",
            "src/Client/Presentation/Library/LibrarySurfaceViewModel.cs",
            "src/Client/Presentation/Library/LibrarySurface.cs",
            "src/Client/Presentation/Catalog/GameDetailViewModel.cs",
            "src/Client/Presentation/Catalog/GameDetailView.cs",
            "src/Client/Presentation/Design/DesignTokens.cs",
            "tests/UnitTests/CatalogViewStateResolverTests.cs",
            "tests/UnitTests/GameDetailViewModelTests.cs",
            "tests/UnitTests/LibrarySurfaceViewModelTests.cs",
            "tests/UnitTests/LibrarySurfaceActionsTests.cs",
            "tests/UnitTests/PlayabilityResolverTests.cs",
            "tests/IntegrationTests/CatalogSurfaceTests.cs",
        ),
        "10.2": (
            "src/Shared/Contracts/CatalogSnapshot.cs",
            "src/Client/Infrastructure/Persistence/CatalogSnapshotStore.cs",
            "src/Client/Application/CatalogSync/CatalogSnapshotApplier.cs",
            "src/Client/Application/Downloads/DownloadCoordinator.cs",
            "src/Client/Application/Install/InstallationCoordinator.cs",
            "src/Client/Application/Install/InstallationRepairService.cs",
            "src/Client/Application/GameLauncher/PlayabilityDecision.cs",
            "src/Client/Application/GameLauncher/PlayabilityIntentService.cs",
            "src/Client/Application/GameLauncher/IPlayabilityIntentPort.cs",
            "src/Client/Application/GameLauncher/PlayabilityProjection.cs",
            "src/Client/Host/DownloadComposition.cs",
            "src/Client/Host/InstallationComposition.cs",
            "src/Client/Host/GameLauncherComposition.cs",
            "src/Client/App.cs",
            "src/Client/Presentation/Downloads/DownloadProgressViewModel.cs",
            "src/Client/Presentation/Install/InstallProgressViewModel.cs",
            "src/Client/Presentation/GameLauncher/GamePlayActionsViewModel.cs",
            "tests/IntegrationTests/CatalogSurfaceTests.cs",
            "tests/IntegrationTests/InstallationRepairTests.cs",
            "tests/IntegrationTests/DownloadIntegrityTests.cs",
            "tests/UnitTests/InstallationRepairServiceTests.cs",
            "tests/UnitTests/DownloadCoordinatorTests.cs",
            "tests/UnitTests/LibrarySurfaceActionsTests.cs",
            "tests/UnitTests/PlayabilityResolverTests.cs",
        ),
    }
    candidates = candidates_by_story.get(story.short_id, ())
    available = [path for path in candidates if (REPO_ROOT / path).is_file()]
    candidate_text = "\n".join(f"- {path}" for path in available) or "(sin rutas precargadas)"
    return f"""Planifica exclusivamente la Story {story.short_id}: {story.title}.
La evidencia que necesitas ya está copiada debajo. Tu PRIMERA acción debe escribir un plan
completo en {relative(plan)} en menos de {PLAN_ARTIFACT_DEADLINE_SECONDS} segundos.
 Escribe literalmente `# Story {story.short_id}`, `## Alcance`, `## Ficheros` al comienzo
 de sus secciones. Incluye tareas ordenadas, pruebas y riesgos. Máximo 500 palabras,
 incluido el JSON. Usa exclusivamente rutas concretas relativas al repositorio en
 `reads` y `writes` (no `Server: ...`, `Shared: ...` ni roles). `reads` debe existir;
 `writes` puede ser un fichero nuevo dentro de un directorio existente.
No explores el repositorio, no leas el backlog, no lances subagentes, no implementes código,
no solicites aprobación y no amplíes el plan después de escribirlo. Si la evidencia no permite
concretar una ruta, declara el bloqueo en el plan: no adivines ni explores.

El plan tiene que incluir un bloque ```json con las claves `reads`, `writes`, `shared_surfaces`
y `depends_on`: son las rutas que la historia lee, las que escribe, las superficies compartidas
que toca (contrato, persistencia, red, instalación, seguridad o firma) y las historias de las que
depende. Declara además `schema_changes` (booleano): true si cambia el esquema persistido.
 Ejemplo de forma (sustituye las rutas por las adecuadas):
 ```json
 {{"reads":["src/Shared/Contracts/StateDisplayMap.cs"],"writes":["src/Server/Host/CatalogHub.cs"],"shared_surfaces":["contrato","persistencia"],"depends_on":["9.1"],"schema_changes":true}}
 ```
 El bloque es obligatorio y se valida antes de implementar; un esquema nuevo requiere migración.

 Rutas candidatas existentes para esta historia (no es una lista de cambios obligatorios):
 {candidate_text}

 {sources}"""


def implementation_prompt(story, plan: Path, context: Path) -> str:
    return f"""Implementa exclusivamente la Story {story.short_id}: {story.title}.
Lee primero {relative(plan)} y el paquete de contexto {relative(context)}. El paquete es la
evidencia ya seleccionada de esta historia: úsalo y no explores el backlog por tu cuenta.
Implementa el código y las pruebas que el plan y el paquete describen, ejecuta las pruebas más
cercanas que existan y deja el árbol listo para commit.
Limita tu exploración a las rutas que el plan y el paquete declaran. Si el paquete no contiene lo
que necesitas, no amplíes el alcance: repórtalo como bloqueo demostrado con una línea que empiece
por `{BLOCKER_MARKER}` (qué falta y en qué ruta), emitida mediante un comando `Write-Output` o
`echo`, y detente. Solo ese evento de herramienta queda como evidencia del bloqueo.
No replanifiques, no lances revisores, no abras PR y no cambies otras stories."""


# ---------------------------------------------------------------------------
# The preparation of the context
# ---------------------------------------------------------------------------


def read_text_or_note(path: Path) -> str:
    """The content of a file, or the reason why it could not be read."""
    try:
        return path.read_text(encoding="utf-8").strip()
    except OSError as error:
        return f"(no se pudo leer {relative(path)}: {error})"


def extract_story_section(story, epics_file: Path | None = None) -> str:
    """The markdown of the section of the story in the backlog.

    The preparer only needs the section of its own story: the rest of the
    backlog is not evidence for it, and reading it would spend the budget of
    the phase without adding anything.
    """
    path = epics_file or legacy.EPICS_FILE
    try:
        content = path.read_text(encoding="utf-8")
    except OSError as error:
        return f"(no se pudo leer {relative(path)}: {error})"
    lines = content.splitlines()
    header = re.compile(rf"^###\s+Story\s+{story.epic}\.{story.num}\s*:")
    start = next((index for index, line in enumerate(lines) if header.match(line)), None)
    if start is None:
        return f"(la sección de la Story {story.short_id} no está en {relative(path)})"
    collected: list[str] = []
    for line in lines[start + 1:]:
        if re.match(r"^#{1,3}\s", line):
            break
        collected.append(line)
    section = "\n".join(collected).strip()
    return section or f"(la sección de la Story {story.short_id} está vacía)"


def epic_context_path(story) -> Path:
    """The compiled context of the epic, when the project has one."""
    return legacy.ARTIFACTS_DIR / f"epic-{story.epic}-context.md"


def read_epic_context(story) -> str:
    """The compiled context of the epic, or the reason why there is none."""
    path = epic_context_path(story)
    try:
        content = path.read_text(encoding="utf-8").strip()
    except OSError:
        return f"(no hay contexto compilado de la épica {story.epic}: {relative(path)} no existe)"
    if len(content) > EPIC_CONTEXT_MAX_CHARS:
        content = content[:EPIC_CONTEXT_MAX_CHARS] + "\n\n(contexto de la épica truncado)"
    return content


def plan_digest(plan: Path) -> str:
    """The digest that binds a cached context package to its exact plan."""
    try:
        return hashlib.sha256(plan.read_bytes()).hexdigest()
    except OSError:
        return ""


def limit_prompt_source(content: str, remaining: int) -> str:
    """Fit one inline source in the Windows-safe prompt budget."""
    if remaining <= 0:
        return "(fuente omitida para respetar el límite del prompt)"
    if len(content) <= remaining:
        return content
    return content[:remaining] + "\n\n(fuente truncada para respetar el límite del prompt)"


def context_prompt(story, plan: Path, context: Path, epics_file: Path | None = None) -> str:
    """The prompt of the phase that prepares the context of a story.

    The prompt carries the sources inline, so the preparer never opens the
    backlog to find them, and it states the limit of the phase: the preparer
    selects evidence, it does not implement.
    """
    plan_source = read_text_or_note(plan)
    story_source = extract_story_section(story, epics_file)
    epic_source = read_epic_context(story)
    fixed = 5000
    remaining = max(CONTEXT_PROMPT_MAX_CHARS - fixed, 0)
    plan_source = limit_prompt_source(plan_source, min(len(plan_source), remaining))
    remaining -= len(plan_source)
    story_source = limit_prompt_source(story_source, min(len(story_source), max(remaining, 0)))
    remaining -= len(story_source)
    epic_source = limit_prompt_source(epic_source, max(remaining, 0))
    return f"""Prepara el paquete de contexto de la Story {story.short_id}: {story.title}.

Escribe un documento breve en español en {relative(context)}, una ruta relativa a la raíz del
checkout en el que estás. Usa solo las fuentes de abajo: no leas el backlog entero, no explores el
repositorio más allá de las rutas que declara el plan y no inventes evidencia.

El documento tiene estos encabezados exactos, en este orden:

## Requisitos y criterios de aceptación
## Decisiones aplicables
## APIs y patrones a reutilizar
## Archivos y pruebas relevantes
## Comandos
## Exclusiones

Reglas de contenido:
- Máximo 700 palabras. Cada dato va con su ruta, su identificador o su nombre de archivo.
- "Requisitos y criterios de aceptación" enumera los requisitos y cada AC aplicable con su
  identificador; si no existe uno, declara explícitamente "AC no disponible".
- Cada sección cita solo evidencia de las fuentes. Si una fuente no aporta nada a esa sección,
  escribe "no disponible" y sigue. No inventes decisiones, contratos, rutas ni pruebas.
- "APIs y patrones a reutilizar" describe los tipos, los métodos y las formas que ya existen y que
  la historia puede reutilizar: para eso puedes leer los ficheros que el plan declara en `reads`.
- "Comandos" lista los comandos de compilación y de prueba de la historia.
- "Exclusiones" dice qué queda fuera del alcance de la historia.
- Añade al final, sin reescribirla, esta línea:
  Restricciones del preparador: no puede implementar, no puede cambiar la huella, no puede modificar contratos ni ampliar alcance.

Restricciones de operación:
- No escribas código, no crees ficheros distintos de {relative(context)} y no modifiques el plan.
- No lances subagentes, no abras PR y no implementes la historia.

--- PLAN DE LA HISTORIA ---
{plan_source}

--- SECCIÓN DE LA HISTORIA EN EL BACKLOG ---
{story_source}

--- CONTEXTO DE LA ÉPICA ---
{epic_source}
"""


def is_complete_context(context: Path, plan: Path | None = None) -> bool:
    """Whether a context package holds everything that the worker needs.

    A package without its sections, or without the sentence that states that
    the preparer only prepares, is incomplete: the phase runs again instead of
    handing a half-written package to a worker.
    """
    try:
        content = context.read_text(encoding="utf-8")
    except OSError:
        return False
    stripped = content.strip()
    if len(stripped) < CONTEXT_MIN_LENGTH:
        return False
    lowered = stripped.lower()
    headings = tuple(f"## {token}" for token in (
        "requisitos y criterios de aceptación", "decisiones aplicables", "apis y patrones a reutilizar",
        "archivos y pruebas relevantes", "comandos", "exclusiones",
    ))
    positions = [lowered.find(heading) for heading in headings]
    if any(position < 0 for position in positions) or positions != sorted(positions):
        return False
    requirements_end = positions[1]
    if re.search(r"\bac(?:\d+)?\b", lowered[positions[0]:requirements_end]) is None:
        return False
    if not all(token in lowered for token in CONTEXT_RESTRICTION_TOKENS):
        return False
    if plan is None:
        return True
    digest = plan_digest(plan)
    return bool(digest) and f"{CONTEXT_PLAN_DIGEST_PREFIX}{digest} -->" in content


def stamp_context_plan_digest(context: Path, plan: Path) -> None:
    """Mark a complete cache entry with the plan that produced it."""
    digest = plan_digest(plan)
    if not digest:
        raise legacy.DriverError(f"cannot hash the context plan {relative(plan)}")
    content = context.read_text(encoding="utf-8")
    content = re.sub(
        rf"^{re.escape(CONTEXT_PLAN_DIGEST_PREFIX)}[0-9a-f]{{64}} -->\r?\n?",
        "",
        content,
        flags=re.MULTILINE,
    )
    context.write_text(f"{CONTEXT_PLAN_DIGEST_PREFIX}{digest} -->\n{content}", encoding="utf-8")


def copy_context(context: Path, tree_path: Path) -> Path:
    """Copy the context package into the checkout of the story."""
    target = tree_path / relative(context)
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(context, target)
    return target


def prepare_context(
    story,
    plan: Path,
    log_dir: Path,
    tree=None,
    runner=None,
    journal: PhaseJournal | None = None,
) -> Path:
    """Return the context package of a story, preparing it when it is missing.

    The package is an execution input and not a source of truth: it selects the
    evidence of the story and it is cached locally, so a resumed story spends
    no budget on it again. The phase runs before the implementation, and it
    never changes the footprint of the story.
    """
    context = context_path(story)
    if is_complete_context(context, plan):
        log(f"{story.short_id}: reusing the context package {relative(context)}")
        if tree is not None:
            copy_context(context, tree.path)
        return context

    CONTEXT_ROOT.mkdir(parents=True, exist_ok=True)
    target = tree.path / relative(context) if tree is not None else context
    # A stale or incomplete entry must not satisfy the structural fallback if
    # the preparer fails to write its output. Delete both cache locations
    # before invoking it, then accept only the fresh package it produced.
    for candidate in {context, target}:
        candidate.unlink(missing_ok=True)
    target.parent.mkdir(parents=True, exist_ok=True)
    if runner is None:
        runner = run_phase
    try:
        runner(
            "context",
            CONTEXT_MODEL,
            context_prompt(story, plan, context),
            log_dir,
            timeout_minutes=CONTEXT_TIMEOUT_MINUTES,
            retries=CONTEXT_MAX_ATTEMPTS,
            cwd=tree.path if tree is not None else None,
            journal=journal,
        )
    except legacy.DriverError:
        # Both locations were cleared above. A timeout may therefore be
        # recovered only from a package this invocation structurally completed;
        # partial output and every non-timeout failure still stop the story.
        candidates = (target,) if target == context else (target, context)
        prepared = next((candidate for candidate in candidates if is_complete_context(candidate)), None)
        if prepared is None or journal is None or journal.state_of("context") != PHASE_TIMEOUT:
            raise
        if prepared != target:
            shutil.copy2(prepared, target)
        if journal is not None:
            attempt = next(
                (int(entry.get("attempt", 1)) for entry in reversed(journal.entries)
                 if entry.get("phase") == "context"),
                1,
            )
            journal.record(
                "context",
                PHASE_COMPLETE,
                attempt=attempt,
                evidence=relative(target),
                error="accepted complete context written before timeout",
            )
        log(f"{story.short_id}: context timed out after writing a complete package; continuing")
    if not is_complete_context(target):
        raise legacy.DriverError(
            f"the context preparer did not write a complete {relative(context)}"
        )
    if target != context:
        shutil.copy2(target, context)
    stamp_context_plan_digest(context, plan)
    if not is_complete_context(context, plan):
        raise legacy.DriverError(f"the context preparer did not bind {relative(context)} to its plan")
    if tree is not None:
        copy_context(context, tree.path)
    return context


def ensure_contexts_ignored(cwd: Path | None = None) -> None:
    """Keep the context packages out of git, in this checkout and its worktrees.

    The rule is also in the versioned `.gitignore`, but a worktree created
    before that rule landed keeps an older `.gitignore`: the local exclude
    file is the common safety net of every worktree of the repository.
    """
    result = legacy.git("rev-parse", "--git-path", "info/exclude", check=False, cwd=cwd)
    if not result.ok:
        return
    exclude = result.stdout.strip()
    if not exclude:
        return
    path = Path(exclude)
    if not path.is_absolute():
        path = (cwd or REPO_ROOT) / path
    try:
        content = path.read_text(encoding="utf-8") if path.exists() else ""
    except OSError:
        return
    if CONTEXT_PATH_PREFIX.strip("/") in content:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8") as handle:
        if content and not content.endswith("\n"):
            handle.write("\n")
        handle.write(
            "# Build Lite: los paquetes de contexto son cache local\n"
            f"{CONTEXT_PATH_PREFIX}\n"
        )


@dataclass(frozen=True)
class VerificationFailure:
    """A failed deterministic gate and the bounded evidence needed to repair it."""

    label: str
    command: str
    evidence: str
    target_test: str = ""
    target_command: str = ""


def repair_prompt(story, failures: Sequence[VerificationFailure]) -> str:
    evidence = "\n\n".join(
        f"### {failure.label}\n"
        + ("Puerta interna: el driver la volverá a comprobar.\n" if failure.label == "migration contract"
           else f"Comando fallido: `{failure.command}`\n")
        + failure.evidence
        for failure in failures
    )
    targeted = [failure for failure in failures if failure.target_test]
    focused_instruction = (
        "Después del cambio, ejecuta y deja verde estas comprobaciones focalizadas: "
        + "; ".join(f"`{failure.target_command}`" for failure in targeted)
        + ". No hagas exploración abierta ni ejecutes la batería completa: el driver la ejecuta después."
        if targeted
        else "Después del cambio, ejecuta cada comando fallido real y deja evidencia verde. "
             "Para la puerta interna de migraciones, añade la migración versionada; el driver la comprueba. "
             "No ejecutes la batería completa: el driver la ejecuta después."
    )
    return f"""Repara exclusivamente el fallo confirmado de la Story {story.short_id}.
Evidencia reproducible:
{evidence}

{focused_instruction}
Haz el cambio mínimo. No replanifiques, no revises de nuevo, no abras PR y no modifiques alcance
ajeno. Si la evidencia no basta, registra `BLOQUEO:` mediante un comando con la causa demostrable."""


def review_repair_prompt(story, blocking: str) -> str:
    """Keep review findings separate from deterministic verification failures."""
    return f"""Repara exclusivamente el hallazgo bloqueante confirmado de la Story {story.short_id}.
Evidencia: {blocking}
Haz el cambio mínimo y sus pruebas. No replanifiques, no revises de nuevo, no abras PR y no
modifiques alcance ajeno."""


def review_prompt(story, diff_file: Path, risk: Risk) -> str:
    reasons = ", ".join(risk.reasons)
    return f"""Revisa el diff de la Story {story.short_id} solo por estos riesgos: {reasons}.
Lee {relative(diff_file)}: enumera todos los archivos y contiene solo los hunks sensibles.
No explores el repositorio ni ejecutes pruebas; para confirmar un consumidor lee como máximo tres
archivos concretos. Responde exactamente `OK` si no hay un defecto S0/S1 confirmado.
Si lo hay, responde una sola línea `BLOCKING: <evidencia verificable y arreglo
local>`; no inventes hallazgos, no propongas mejoras, no clasifiques hipótesis, no modifiques
ficheros ni lances subagentes."""


def write_review_packet(base: str, paths: Sequence[str], output: Path, cwd: Path | None = None) -> None:
    """Bound the review to sensitive changes without hiding the other paths."""
    sensitive = [
        path for path in paths if any(marker.lower() in path.lower() for marker, _ in SENSITIVE_PATHS)
        or path.startswith("src/Client/Infrastructure/Networking/")
        or path.startswith(("src/Server/Application/", "src/Client/Application/",
                            "src/Server/Domain/", "src/Client/Domain/"))
    ]
    if not sensitive:
        raise legacy.DriverError("review requested without a sensitive changed path")
    result = legacy.git("diff", "--unified=3", "--no-ext-diff", f"{base}..HEAD", "--", *sensitive,
                        check=False, cwd=cwd)
    if not result.ok:
        raise legacy.DriverError("cannot generate the sensitive review packet")
    packet = "Changed paths (including changes not shown below):\n" + "\n".join(paths)
    packet += "\n\nSensitive changed hunks:\n" + result.stdout
    if len(packet) > REVIEW_PACKET_MAX_CHARS:
        raise legacy.DriverError(
            f"sensitive review packet exceeds {REVIEW_PACKET_MAX_CHARS} characters; split the story or review manually"
        )
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(packet, encoding="utf-8")


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


def review_verdict_line(transcript: str) -> str:
    """The verdict of a review: ``BLOCKING``, ``OK`` or an empty text.

    <para>
    El veredicto es la ÚLTIMA respuesta del revisor: el modelo puede dejar antes una
    línea de preámbulo («voy a leer el diff»), así que el transcript entero no es nunca
    igual a «OK». La línea de bloqueo ya se aceptaba con preámbulo; el visto bueno se
    acepta con la MISMA regla (AD-15, docs/plan.md §8).
    </para>
    """
    if blocking_review_line(transcript):
        return "BLOCKING"

    lines = [line.strip() for line in transcript.splitlines() if line.strip()]

    if lines and lines[-1] == "OK":
        return "OK"

    return ""


def review_result(log_path: Path) -> str:
    if not log_path.exists():
        raise legacy.DriverError("review produced no transcript")
    session, transcript = legacy.parse_skill_output(log_path)
    del session
    verdict = review_verdict_line(transcript)
    if verdict == "BLOCKING":
        return blocking_review_line(transcript)
    if verdict == "OK":
        return ""
    raise legacy.DriverError("review produced no explicit OK or BLOCKING verdict")


def verification_commands() -> tuple:
    return tuple(command for command in legacy.VERIFICATION_COMMANDS if command.label != "build-loop self test")


def migration_gaps(paths: Sequence[str], metadata: PlanMetadata | None) -> list[str]:
    """Fail closed on entity/schema changes without a versioned migration."""
    gaps = []
    for owner in ("Server", "Client"):
        prefix = f"src/{owner}/Infrastructure/Persistence/"
        changed_entity = any(path.startswith(prefix + "Entities/") for path in paths)
        changed_migration = any(
            path.startswith(prefix + "Migrations/") and path.endswith(".cs")
            and not path.endswith(".Designer.cs") and "ModelSnapshot" not in path
            for path in paths
        )
        if changed_entity and not changed_migration:
            gaps.append(f"{owner}: persistence entity changed without a versioned migration")
    if metadata is not None and metadata.schema_changes and not any(
        "/Migrations/" in path and path.endswith(".cs")
        and not path.endswith(".Designer.cs") and "ModelSnapshot" not in path
        for path in paths
    ):
        gaps.append("the plan declares schema changes but the story has no versioned migration")
    return gaps


def run_verifications(log_dir: Path, cwd: Path | None = None) -> list[tuple]:
    results = []
    base = legacy.git("rev-parse", f"origin/{BASE_BRANCH}", cwd=cwd).stdout.strip()
    paths = changed_paths(base, cwd=cwd)
    # A missing migration is not visible to the compiler or an unrelated test.
    # Repair and post-rebase logs are nested below the story's log directory.
    story_label = log_dir.relative_to(LOG_ROOT).parts[0]
    metadata, _ = extract_plan_metadata(PLAN_ROOT / f"{story_label}.md")
    gaps = migration_gaps(paths, metadata)
    if gaps:
        results.append(("migration contract", legacy.CommandResult(
            argv=("build-lite", "migration-contract"), code=1, stdout="; ".join(gaps),
        )))
        return results
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
        if command.label == "dotnet build" and not result.ok:
            return results
        if command.label == "dotnet build" and any(
            path in ("src/Server/Host/CatalogHub.cs", "src/Client/Infrastructure/Networking/ClientStateHubPublisher.cs")
            for path in paths
        ):
            hub_check = legacy.run_command(
                ("dotnet", "test", "tests/UnitTests/LANPartyHub.UnitTests.csproj", "--no-restore",
                 "--filter", "FullyQualifiedName~ClientStateHubMethodNameTests"),
                log_path=log_dir / "verify-hub-contract.log", cwd=cwd or REPO_ROOT,
            )
            results.append(("hub contract test", hub_check))
            if not hub_check.ok:
                return results
    return results


def verification_failures(results: Sequence[tuple]) -> list[VerificationFailure]:
    """Keep concrete failure evidence instead of reducing a repair to a gate label."""
    failures: list[VerificationFailure] = []
    for label, result in results:
        if result.ok:
            continue
        command = subprocess.list2cmdline(list(result.argv))
        output = verification_output(result)
        evidence = tail_evidence(output)
        target_test = failed_test_name(evidence) if label == "dotnet test" else ""
        failures.append(
            VerificationFailure(
                label=label,
                command=command,
                evidence=evidence,
                target_test=target_test,
                target_command=focused_test_command(result.argv, target_test),
            )
        )
    return failures


def verification_output(result) -> str:
    """Read a command result without treating a missing log as successful evidence."""
    if result.log is not None:
        try:
            return result.log.read_text(encoding="utf-8", errors="replace")
        except OSError:
            pass
    return result.stdout


def tail_evidence(output: str) -> str:
    """Bound the repair prompt to the relevant end of a deterministic command log."""
    if not output.strip():
        return "El comando no produjo salida capturable."
    return output[-VERIFICATION_EVIDENCE_MAX_CHARS:].strip()


def failed_test_name(output: str) -> str:
    """Return a test name only when the runner output demonstrates one."""
    match = FAILED_TEST_PATTERN.search(output)
    return match.group(1) if match else ""


def focused_test_command(argv: Sequence[str], target_test: str) -> str:
    """Build the exact bounded command only from a demonstrated test name."""
    if not target_test:
        return ""
    command = list(argv)
    command.extend(("--filter", f"FullyQualifiedName={target_test}"))
    return subprocess.list2cmdline(command)


def recovery_check_passed(transcript: Path, failure: VerificationFailure) -> bool:
    """Require a successful, relevant tool invocation rather than worker prose."""
    try:
        lines = transcript.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return False
    for line in lines:
        try:
            event = json.loads(line)
        except json.JSONDecodeError:
            continue
        part = event.get("part")
        if not isinstance(part, dict) or part.get("type") != "tool":
            continue
        state = part.get("state")
        if not isinstance(state, dict) or state.get("status") != "completed":
            continue
        command = ImplementationProgress._tool_command(state)
        metadata = state.get("metadata")
        if not isinstance(metadata, dict) or metadata.get("exit") != 0:
            continue
        if not failure.target_test:
            if failure.command in command:
                return True
            continue
        test_class = failure.target_test.rsplit(".", 1)[0]
        if (
            not TEST_COMMAND_PATTERN.search(command)
            or "--filter" not in command
            or (failure.target_test not in command and test_class not in command)
        ):
            continue
        output = str(state.get("output") or metadata.get("output") or "")
        if re.search(r"\b(?:Passed|Correct(?:o|as))!", output, re.IGNORECASE):
            return True
    return False


def verify_or_repair(
    story,
    log_dir: Path,
    repair_available: bool = True,
    cwd: Path | None = None,
    journal: PhaseJournal | None = None,
) -> bool:
    results = run_verifications(log_dir, cwd=cwd)
    failures = verification_failures(results)
    if not failures:
        if journal is not None:
            journal.record("verification", PHASE_COMPLETE, evidence=relative(log_dir))
        return False
    if journal is not None:
        journal.record(
            "verification",
            PHASE_VERIFICATION_FAILURE,
            evidence=relative(log_dir),
            error=", ".join(failure.label for failure in failures),
        )
    if not repair_available:
        raise legacy.DriverError(
            "verification failed after the repair budget was spent: "
            + ", ".join(failure.label for failure in failures)
        )
    outcome = run_phase(
        "repair",
        IMPLEMENT_MODEL,
        repair_prompt(story, failures),
        log_dir,
        timeout_minutes=30,
        cwd=cwd,
        journal=journal,
    )
    for failure in failures:
        if failure.label == "migration contract":
            base = legacy.git("rev-parse", f"origin/{BASE_BRANCH}", cwd=cwd).stdout.strip()
            metadata, _ = extract_plan_metadata(PLAN_ROOT / f"{story.label}.md")
            if migration_gaps(changed_paths(base, cwd=cwd), metadata):
                raise legacy.DriverError("targeted recovery did not add the required versioned migration")
            continue
        if not recovery_check_passed(outcome.log or Path(), failure):
            raise legacy.DriverError(
                f"targeted recovery did not demonstrate a passing check for {failure.label}"
            )
    second = run_verifications(log_dir / "after-repair", cwd=cwd)
    failures = verification_failures(second)
    if failures:
        if journal is not None:
            journal.record(
                "verification",
                PHASE_VERIFICATION_FAILURE,
                evidence=relative(log_dir / "after-repair"),
                error=", ".join(failure.label for failure in failures),
            )
        raise legacy.DriverError(
            "verification still fails after the single repair: "
            + ", ".join(failure.label for failure in failures)
        )
    if journal is not None:
        journal.record("verification", PHASE_COMPLETE, evidence=relative(log_dir / "after-repair"))
    return True


def commit_story(story, cwd: Path | None = None) -> None:
    """Commit the change of a story, without the local cache of Build Lite.

    The context packages are excluded from the staging on purpose: they are an
    execution input of the worker and they never belong to the history of the
    story.
    """
    if not legacy.git("status", "--porcelain", cwd=cwd).stdout.strip():
        return
    # `ensure_contexts_ignored` installs this cache path in the shared Git
    # exclude file for every worktree. Do not name an ignored pathspec here:
    # Git rejects an explicit ignored path even when it is paired with an
    # exclude magic pathspec, which would prevent preserving story work.
    legacy.git("add", "-A", "--", ".", cwd=cwd)
    if not legacy.git("diff", "--cached", "--name-only", cwd=cwd).stdout.strip():
        return
    legacy.git("commit", "-m", f"feat({story.label}): {story.title}", cwd=cwd)


def result_body(story, risk: Risk, base: str) -> str:
    review = "no necesaria (cambio interno)" if not risk.required else f"focalizada: {', '.join(risk.reasons)}"
    return f"""## Build Lite

- **Historia:** {story.short_id} — {story.title}
- **Base:** `{base}`
- **Revisión LLM:** {review}
- **Verificación:** `build-lite --self-test`, `dotnet build`, `dotnet test`, codificación y backlog en verde.
- **Presupuesto:** un plan, un contexto, una implementación, una reparación dirigida como máximo y una revisión para cambios sensibles.
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
    state: str = PHASE_COMPLETE

    @property
    def ok(self) -> bool:
        return not self.error


def run_worker(story, tree: Worktree, metadata: PlanMetadata | None) -> WorkerResult:
    """Plan, implement and verify a story inside its own worktree.

    Nothing here touches `main`, the sprint status or GitHub: the worker
    produces a verified branch and the coordinator publishes it. Every phase
    leaves its structured state in the journal of the story, and an
    implementation that only explores ends stalled instead of burning the
    timeout of the phase.
    """
    plan, result_file, log_dir = story_paths(story)
    log_dir.mkdir(parents=True, exist_ok=True)
    journal = journal_for(story)
    worker_plan = tree.path / relative(plan)
    worker_plan.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(plan, worker_plan)
    # A preserved worker can predate a driver fix merged into main. Rebase it
    # before comparing its diff with main, otherwise that upstream fix looks
    # like undeclared story work and the footprint gate rejects it.
    legacy.git("fetch", "origin", BASE_BRANCH, cwd=tree.path)
    if not legacy.git(
        "merge-base", "--is-ancestor", f"origin/{BASE_BRANCH}", "HEAD", check=False, cwd=tree.path
    ).ok:
        legacy.rebase_onto_main(cwd=tree.path)
    base = legacy.git("rev-parse", f"origin/{BASE_BRANCH}", cwd=tree.path).stdout.strip()

    try:
        preserved_work = implementation_paths(changed_paths(base, cwd=tree.path))
        if preserved_work:
            log("reusing preserved implementation work; proceeding to verification")
        else:
            context = prepare_context(story, plan, log_dir, tree=tree, journal=journal)
            progress = ImplementationProgress(tree.path, log_dir / "implementation-1.jsonl")
            run_phase(
                "implementation",
                IMPLEMENT_MODEL,
                implementation_prompt(story, plan, context),
                log_dir,
                timeout_minutes=30,
                retries=IMPLEMENTATION_MAX_ATTEMPTS,
                cwd=tree.path,
                journal=journal,
                progress=progress,
                progress_window_minutes=IMPLEMENTATION_PROGRESS_WINDOW_MINUTES,
            )
        commit_story(story, cwd=tree.path)
        repair_used = verify_or_repair(story, log_dir, cwd=tree.path, journal=journal)
        commit_story(story, cwd=tree.path)

        paths = changed_paths(base, cwd=tree.path)
        if not paths:
            raise legacy.DriverError("Build Lite produced no story change")
        if not paths_are_declared(paths, metadata):
            raise legacy.DriverError("the worker changed files outside its declared plan footprint")
        risk = classify_risk(paths)
        diff_file = log_dir / "review.diff"
        write_review_packet(base, paths, diff_file, cwd=tree.path)
        worker_diff_file = tree.path / relative(diff_file)
        worker_diff_file.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(diff_file, worker_diff_file)
        if risk.required:
            outcome = run_phase(
                "review", REVIEW_MODEL, review_prompt(story, diff_file, risk), log_dir, timeout_minutes=5,
                retries=1,
                cwd=tree.path, journal=journal,
            )
            blocking = review_result(outcome.log or Path())
            if blocking:
                if repair_used:
                    journal.record("story", PHASE_ESCALATED, error=blocking)
                    raise legacy.DriverError(
                        "the focused review found a blocker after the single repair budget was spent: " + blocking
                    )
                run_phase(
                    "review-repair", IMPLEMENT_MODEL, review_repair_prompt(story, blocking), log_dir, timeout_minutes=15,
                    retries=1,
                    cwd=tree.path, journal=journal,
                )
                commit_story(story, cwd=tree.path)
                verify_or_repair(
                    story, log_dir / "after-review-repair", repair_available=False, cwd=tree.path,
                    journal=journal,
                )
    except legacy.DriverError as error:
        journal.record("story", journal.failure_state(), error=str(error))
        raise

    journal.record("worker", PHASE_COMPLETE, evidence="verified branch ready for publication")
    del result_file
    return WorkerResult(
        story=story,
        worktree=tree,
        base=base,
        risk=risk,
        paths=paths,
        metadata=metadata,
        state=PHASE_COMPLETE,
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
    journal = journal_for(story)

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
                "state": result.state,
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
        journal.record("publication", PHASE_COMPLETE, evidence=f"pull request #{number} merged")
        journal.record("story", PHASE_COMPLETE, evidence=f"pull request #{number} is in origin/main")
    else:
        journal.record("publication", PHASE_COMPLETE, evidence=f"pull request #{number} opened without merge")
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


def admission_is_closed(failures: Sequence[tuple[str, str]]) -> bool:
    """A run never starts or integrates another story after its first failure."""
    return bool(failures)


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
        continue
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
    journal = journal_for(story)

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
            run_phase(
                "plan", PLAN_MODEL, plan_prompt(story, plan), log_dir, timeout_minutes=5, retries=1,
                journal=journal, artifact_path=plan,
            )
        except legacy.DriverError:
            if not is_complete_plan(plan):
                raise
            log("planner timed out after writing a complete plan; continuing without a second attempt")
    require_valid_plan(plan, log_dir, journal)

    try:
        preserved_work = implementation_paths(changed_paths(base))
        if preserved_work:
            log("reusing preserved implementation work; proceeding to verification")
        else:
            context = prepare_context(story, plan, log_dir, journal=journal)
            progress = ImplementationProgress(REPO_ROOT, log_dir / "implementation-1.jsonl")
            run_phase(
                "implementation",
                IMPLEMENT_MODEL,
                implementation_prompt(story, plan, context),
                log_dir,
                timeout_minutes=30,
                retries=IMPLEMENTATION_MAX_ATTEMPTS,
                journal=journal,
                progress=progress,
                progress_window_minutes=IMPLEMENTATION_PROGRESS_WINDOW_MINUTES,
            )
        commit_story(story)
        repair_used = verify_or_repair(story, log_dir, journal=journal)
        commit_story(story)

        paths = changed_paths(base)
        if not paths:
            raise legacy.DriverError("Build Lite produced no story change")
        risk = classify_risk(paths)
        diff_file = log_dir / "review.diff"
        write_review_packet(base, paths, diff_file)
        if risk.required:
            outcome = run_phase(
                "review", REVIEW_MODEL, review_prompt(story, diff_file, risk), log_dir, timeout_minutes=5,
                retries=1,
                journal=journal,
            )
            blocking = review_result(outcome.log or Path())
            if blocking:
                if repair_used:
                    journal.record("story", PHASE_ESCALATED, error=blocking)
                    raise legacy.DriverError(
                        "the focused review found a blocker after the single repair budget was spent: " + blocking
                    )
                run_phase(
                    "review-repair", IMPLEMENT_MODEL, review_repair_prompt(story, blocking), log_dir, timeout_minutes=15,
                    retries=1,
                    journal=journal,
                )
                commit_story(story)
                verify_or_repair(
                    story, log_dir / "after-review-repair", repair_available=False, journal=journal,
                )
    except legacy.DriverError as error:
        journal.record("story", journal.failure_state(), error=str(error))
        raise

    legacy.fetch_origin()
    main_after = legacy.git("rev-parse", f"origin/{BASE_BRANCH}").stdout.strip()
    if main_after != base:
        legacy.rebase_onto_main()
        base = main_after
        verify_or_repair(story, log_dir / "after-rebase", repair_available=False, journal=journal)

    result_file.parent.mkdir(parents=True, exist_ok=True)
    result_file.write_text(
        json.dumps(
            {
                "story": story.key,
                "base": base,
                "risk": list(risk.reasons),
                "paths": paths,
                "state": PHASE_COMPLETE,
            },
            indent=2,
        ),
        encoding="utf-8",
    )
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
        journal.record("publication", PHASE_COMPLETE, evidence=f"pull request #{number} merged")
        journal.record("story", PHASE_COMPLETE, evidence=f"pull request #{number} is in origin/main")
    else:
        journal.record("publication", PHASE_COMPLETE, evidence=f"pull request #{number} opened without merge")


def plan_story(story) -> tuple[PlanMetadata | None, str]:
    """Plan a candidate story and read its structured metadata.

    The planning happens in the coordinator, before the window opens, because
    the metadata is what decides whether the story may run next to another
    worker. A planner that times out after writing a complete plan is
    accepted: the plan is on disk and the metadata can be read from it.
    """
    plan, _, log_dir = story_paths(story)
    log_dir.mkdir(parents=True, exist_ok=True)
    journal = journal_for(story)
    if not plan.exists():
        try:
            run_phase(
                "plan", PLAN_MODEL, plan_prompt(story, plan), log_dir, timeout_minutes=5, retries=1,
                journal=journal, artifact_path=plan,
            )
        except legacy.DriverError:
            if not is_complete_plan(plan):
                raise
            log("planner timed out after writing a complete plan; continuing without a second attempt")
    require_valid_plan(plan, log_dir, journal)
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

    def preserve_unintegrated_work() -> None:
        """Wait for and preserve work that must not be integrated after failure."""
        nonlocal active, finished
        for slot in active:
            slot.join()
            legacy.preserve_pending_work(slot.story, cwd=slot.tree.path)
            preserve_worktree(slot.tree)
            log(f"{slot.story.short_id}: preserved after another story failed")
        for result in finished:
            legacy.preserve_pending_work(result.story, cwd=result.worktree.path)
            preserve_worktree(result.worktree)
            log(f"{result.story.short_id}: verified work preserved after another story failed")
        active.clear()
        finished.clear()

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
        if admission_is_closed(failures):
            # A failed story closes the admission window. Let any workers that
            # were already running finish so their committed work can remain in
            # its worktree, but never integrate or start another story.
            log("the scheduler is closing admission after a failed story")
            preserve_unintegrated_work()
            break
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
                log(f"{story.short_id}: blocked, {why}")
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
    if review_verdict_line("I'll read the diff.\nOK\n") != "OK":
        failures.append("an OK review with a preamble was not parsed")
    if review_verdict_line("I'll read the diff.\nnote\nBLOCKING: an S1 is proven\n") != "BLOCKING":
        failures.append("a blocking review with a preamble was not parsed")
    if review_verdict_line("OK\nsomething else\n") != "":
        failures.append("a review whose verdict is not the last line was accepted")
    if merged_commit_from_pr('{"state":"MERGED","mergeCommit":{"oid":"abc"}}') != "abc":
        failures.append("a merged squash commit was not parsed")
    if merged_commit_from_pr('{"state":"OPEN","mergeCommit":{"oid":"abc"}}'):
        failures.append("an open pull request was parsed as merged")
    if IMPLEMENT_MODEL != "minimax-coding-plan/MiniMax-M3":
        failures.append("the final implementation/repair model must remain MiniMax M3")
    if migration_gaps(["src/Server/Infrastructure/Persistence/Entities/ClientInstallationView.cs"], None) != [
        "Server: persistence entity changed without a versioned migration"
    ]:
        failures.append("a changed entity without a migration was accepted")
    if migration_gaps([
        "src/Server/Infrastructure/Persistence/Entities/ClientInstallationView.cs",
        "src/Server/Infrastructure/Persistence/Migrations/20261109_ClientInstallationView.cs",
    ], PlanMetadata(schema_changes=True)):
        failures.append("a versioned schema migration was rejected")
    if not migration_gaps([
        "src/Server/Infrastructure/Persistence/Entities/ClientInstallationView.cs",
        "src/Server/Infrastructure/Persistence/Migrations/20261109_ClientInstallationView.Designer.cs",
    ], PlanMetadata(schema_changes=True)):
        failures.append("a migration designer was mistaken for the versioned migration")
    if not migration_gaps(["src/Server/Infrastructure/Persistence/CatalogDbContext.cs"],
                          PlanMetadata(schema_changes=True)):
        failures.append("a declared schema change without a migration was accepted")
    self_test_plan = PLAN_ROOT / "self-test-plan.md"
    self_test_plan.parent.mkdir(parents=True, exist_ok=True)
    self_test_plan.write_text(
        "# Story 0.0\n\n## Alcance\n\n" + "x" * 300 + "\n\n## Ficheros\n"
        + '```json\n{"reads":["tools/build-lite.py"],"writes":["tools/build-lite.py"],'
        + '"shared_surfaces":[],"depends_on":[],"schema_changes":false}\n```\n', encoding="utf-8",
    )
    if not is_complete_plan(self_test_plan):
        failures.append("a complete plan was rejected")
    self_test_plan.write_text("# Story 0.0\n## Alcance\n" + "palabra " * 501 + "\n## Ficheros\n", encoding="utf-8")
    if is_complete_plan(self_test_plan):
        failures.append("an oversized plan was accepted")
    self_test_plan.write_text(
        "# Plan Story 9.2\n\n## Alcance\n" + "palabra " * 501 + "\n## Ficheros\n"
        + '```json\n{"reads":["Shared: StateDisplayMap"],'
        + '"writes":["Server: DbContext + migracion"],"shared_surfaces":[],"depends_on":[],'
        + '"schema_changes":true}\n```\n', encoding="utf-8",
    )
    reasons = plan_errors(self_test_plan)
    if not any("palabras" in reason for reason in reasons) or not any("# Story" in reason for reason in reasons):
        failures.append("the rejected 9.2 plan did not explain its length and heading")
    if not any("reads:" in reason for reason in reasons) or not any("writes:" in reason for reason in reasons):
        failures.append("role-based metadata was accepted as concrete paths")
    story_plan = PLAN_ROOT / "self-test-story" / "9-2.md"
    story_plan.parent.mkdir(parents=True, exist_ok=True)
    try:
        story_plan.write_text(
            '# Story 8.1\n## Alcance\n' + 'x' * 300 + '\n## Ficheros\n'
            + '```json\n{"reads":["tools/build-lite.py"],"writes":["tools/build-lite.py"],'
            + '"shared_surfaces":["contrato"],"depends_on":[],"schema_changes":false}\n```',
            encoding="utf-8",
        )
        if is_complete_plan(story_plan):
            failures.append("a plan for a different story was accepted")
        if not PlanMetadata(shared_surfaces=("contrato",)).sensitive_surfaces():
            failures.append("the Spanish contract surface is not considered sensitive")
    finally:
        story_plan.unlink(missing_ok=True)
        story_plan.parent.rmdir()
    try:
        candidate_story = legacy.story_by_id(9, 2, legacy.read_sprint_status())
        prompt = plan_prompt(candidate_story, story_plan)
        if "src/Server/Infrastructure/Persistence/Entities/ClientInstallationView.cs" not in prompt:
            failures.append("the 9.2 planning prompt has no concrete path candidates")
    except legacy.DriverError as error:
        failures.append(f"the 9.2 prompt cannot be produced: {error}")
    else:
        if len(prompt) > 8000:
            failures.append("the 9.2 prompt exceeds the Windows launcher argument budget")
    self_test_plan.unlink(missing_ok=True)
    if implementation_paths([".build-lite/plans/3-1.md"]):
        failures.append("Build Lite bookkeeping was parsed as implementation work")
    if implementation_paths([".build-lite/plans/3-1.md", "src/Server/Program.cs"]) != ["src/Server/Program.cs"]:
        failures.append("preserved implementation work was not detected")

    failures.extend(self_test_scheduler())
    failures.extend(self_test_plan_limits())
    failures.extend(self_test_review_packet())
    failures.extend(self_test_context())
    failures.extend(self_test_phases())
    failures.extend(self_test_targeted_recovery())

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1
    print("the build-lite self test passed")
    return 0


def self_test_plan_limits() -> list[str]:
    """A completed plan ends the worker early; silence fails before five minutes."""
    failures: list[str] = []
    plan = PLAN_ROOT / "self-test-artifact-deadline.md"
    log_path = LOG_ROOT / "self-test-artifact-deadline.jsonl"
    plan.parent.mkdir(parents=True, exist_ok=True)
    plan.unlink(missing_ok=True)
    body = (
        "# Story 0.0\n\n## Alcance\n" + "x" * 300 + "\n\n## Ficheros\n"
        + '```json\n{"reads":["tools/build-lite.py"],"writes":["tools/build-lite.py"],'
        + '"shared_surfaces":[],"depends_on":[],"schema_changes":false}\n```\n'
    )
    try:
        script = "import pathlib,time; pathlib.Path(" + repr(str(plan)) + ").write_text(" + repr(body) + ", encoding='utf-8'); time.sleep(10)"
        code, seconds, _ = watch_process(
            (sys.executable, "-c", script), log_path, timeout=5, cwd=REPO_ROOT,
            poll_seconds=0.1, artifact_path=plan,
        )
        if code != 0 or seconds >= 5:
            failures.append("a complete plan did not end its provider phase early")
        plan.unlink(missing_ok=True)
        original = globals()["PLAN_ARTIFACT_DEADLINE_SECONDS"]
        globals()["PLAN_ARTIFACT_DEADLINE_SECONDS"] = 0.2
        try:
            code, seconds, _ = watch_process(
                (sys.executable, "-c", "import time; time.sleep(10)"), log_path,
                timeout=5, cwd=REPO_ROOT, poll_seconds=0.1, artifact_path=plan,
            )
            if code != EXIT_TIMEOUT or seconds >= 5:
                failures.append("a planner without an artifact was not stopped early")
        finally:
            globals()["PLAN_ARTIFACT_DEADLINE_SECONDS"] = original
    finally:
        plan.unlink(missing_ok=True)
        log_path.unlink(missing_ok=True)
    return failures


def self_test_review_packet() -> list[str]:
    """Review only sensitive hunks, name omitted files, and fail closed on size."""
    failures: list[str] = []
    packet = LOG_ROOT / "self-test-review-packet.diff"
    paths = ["src/Server/Host/CatalogHub.cs", "src/Server/Presentation/Clients/ClientsWindow.cs"]
    original_git = legacy.git
    original_limit = REVIEW_PACKET_MAX_CHARS

    def fake_git(*args, **kwargs):
        del kwargs
        if paths[0] not in args or paths[1] in args:
            raise AssertionError("review packet requested the wrong changed paths")
        return legacy.CommandResult(argv=args, code=0, stdout="diff --git a/CatalogHub.cs b/CatalogHub.cs\n+fixed\n")

    legacy.git = fake_git
    try:
        write_review_packet("baseline", paths, packet)
        contents = packet.read_text(encoding="utf-8")
        if paths[1] not in contents or "+fixed" not in contents:
            failures.append("the review packet hid a changed path or a sensitive hunk")
        globals()["REVIEW_PACKET_MAX_CHARS"] = 10
        try:
            try:
                write_review_packet("baseline", paths, packet)
            except legacy.DriverError:
                pass
            else:
                failures.append("an oversized sensitive review packet was accepted")
        finally:
            globals()["REVIEW_PACKET_MAX_CHARS"] = original_limit
    finally:
        legacy.git = original_git
        packet.unlink(missing_ok=True)
    return failures


def self_test_targeted_recovery() -> list[str]:
    """The repair contract retains deterministic evidence and rejects an unproven test."""
    failures: list[str] = []
    target = "LANPartyHub.IntegrationTests.GameLaunchTests.A_missing_executable_is_rejected"
    failed = legacy.CommandResult(
        argv=("dotnet", "test", "LANPartyHub.sln"),
        code=1,
        stdout=f"Con error {target} [42 ms]\nCon error! - Con error: 1",
    )
    parsed = verification_failures([("dotnet test", failed)])
    if len(parsed) != 1 or parsed[0].target_test != target:
        failures.append("a demonstrated failed test was not targeted")
    if (
        "dotnet test LANPartyHub.sln" not in parsed[0].command
        or target not in parsed[0].evidence
        or f"FullyQualifiedName={target}" not in parsed[0].target_command
    ):
        failures.append("the targeted repair lost command or failure evidence")
    non_target = verification_failures(
        [("check-encoding", legacy.CommandResult(argv=("powershell", "check.ps1"), code=1, stdout="bad"))]
    )
    if non_target[0].target_test:
        failures.append("a non-test verification failure invented a targeted test")
    transcript = LOG_ROOT / "self-test-targeted-recovery.jsonl"
    transcript.parent.mkdir(parents=True, exist_ok=True)
    def write_event(command: str, exit_code: int, output: str = "Correctas! - Con error: 0") -> None:
        transcript.write_text(
            json.dumps(
                {
                    "part": {
                        "type": "tool",
                        "state": {
                            "status": "completed",
                            "input": {"command": command},
                            "output": output,
                            "metadata": {"exit": exit_code},
                        },
                    }
                }
            )
            + "\n",
            encoding="utf-8",
        )

    write_event(f"dotnet test LANPartyHub.sln --filter FullyQualifiedName={target}", 0)
    if not recovery_check_passed(transcript, parsed[0]):
        failures.append("a successful targeted test was not recognised")
    write_event(f"dotnet test LANPartyHub.sln --filter FullyQualifiedName={target}", 0, "No test matches the given testcase filter")
    if recovery_check_passed(transcript, parsed[0]):
        failures.append("a zero-match targeted test was accepted")
    write_event(f"dotnet test LANPartyHub.sln --filter FullyQualifiedName={target}", 1)
    if recovery_check_passed(transcript, parsed[0]):
        failures.append("a failing targeted test was accepted")
    non_target_failure = non_target[0]
    write_event("powershell check.ps1", 0, "OK")
    if not recovery_check_passed(transcript, non_target_failure):
        failures.append("a successful non-test recovery check was not recognised")
    expected_prefix = subprocess.list2cmdline(list(failed.argv))
    if not parsed[0].target_command.startswith(expected_prefix + " "):
        failures.append("the focused test did not preserve the original verification argv")
    if "--no-restore" in parsed[0].target_command:
        failures.append("the focused test added an unsupported no-restore option")
    review_prompt_text = review_repair_prompt(type("Story", (), {"short_id": "0.0"})(), "BLOCKING: evidence")
    if "BLOCKING: evidence" not in review_prompt_text:
        failures.append("the review repair prompt lost its blocking evidence")
    transcript.unlink(missing_ok=True)
    return failures


CONTEXT_SAMPLE = """## Requisitos y criterios de aceptación

- AC1 de la historia: el estado distingue «dañado» de «no confiable».

## Decisiones aplicables

- La tabla de estados es la fuente única de la microcopia del estado.

## APIs y patrones a reutilizar

- `ErrorCatalog.Resolve(code)` devuelve el mensaje, la causa y el siguiente paso.

## Archivos y pruebas relevantes

- `src/Shared/Contracts/StateDisplayMap.cs` y `tests/UnitTests/StateDisplayMapTests.cs`.

## Comandos

- `dotnet build LANPartyHub.sln` y `dotnet test LANPartyHub.sln`.

## Exclusiones

- La verificación del paquete y la instalación en disco.

Restricciones del preparador: no puede implementar, no puede cambiar la huella, no puede modificar contratos ni ampliar alcance.
"""


def self_test_context() -> list[str]:
    """The invariants of the context package, without a provider."""
    failures: list[str] = []
    story = type(
        "S",
        (),
        {
            "short_id": "99.99",
            "label": "99-99",
            "branch": "story/99-99-x",
            "title": "Historia de prueba",
            "epic": 99,
            "num": 99,
        },
    )()
    context = context_path(story)

    # The package lives in the local cache, under the path of its story.
    if relative(context) != ".build-lite/contexts/99-99.md":
        failures.append("the context package does not live under .build-lite/contexts/")
    if CONTEXT_MAX_ATTEMPTS != PHASE_MAX_ATTEMPTS or CONTEXT_MAX_ATTEMPTS != 2:
        failures.append("the context phase retry budget is not one technical retry")

    # The package is never part of the change of the story.
    if not is_context_path(".build-lite/contexts/99-99.md"):
        failures.append("a context package was not recognised")
    if is_context_path(".build-lite/results/99-99.json"):
        failures.append("a published result was recognised as a context package")
    if implementation_paths([".build-lite/contexts/99-99.md"]):
        failures.append("a context package was parsed as implementation work")
    if implementation_paths([".build-lite/contexts/99-99.md", "src/Server/Program.cs"]) != [
        "src/Server/Program.cs"
    ]:
        failures.append("a context package was not excluded from the change of the story")

    # The section of the story stops at the next heading.
    epics = PLAN_ROOT / "self-test-epics.md"
    epics.parent.mkdir(parents=True, exist_ok=True)
    epics.write_text(
        "# Epics\n\n### Story 99.99: Prueba\n\nContenido de la historia.\n\n"
        "### Story 99.100: Otra\n\nNo debe aparecer.\n\n## Epic 100\n\nNo debe aparecer.\n",
        encoding="utf-8",
    )
    section = extract_story_section(story, epics)
    if "Contenido de la historia." not in section:
        failures.append("the section of the story was not extracted")
    if "No debe aparecer." in section:
        failures.append("the section of the story did not stop at the next heading")

    # The prompts carry the sources and the limits of the phase.
    plan = PLAN_ROOT / "self-test-context-plan.md"
    plan.write_text("# Story 99.99\n\n## Alcance\n\n" + "x" * 300 + "\n\n## Ficheros\n", encoding="utf-8")
    preparation = context_prompt(story, plan, context, epics)
    for token in ("## Requisitos", "## Exclusiones", "no puede implementar", relative(context)):
        if token not in preparation:
            failures.append(f"the context prompt does not declare {token!r}")
    if "Contenido de la historia." not in preparation:
        failures.append("the context prompt does not carry the section of the story")
    if relative(epics) in preparation:
        failures.append("the context prompt still sends the preparer to the whole backlog")
    if len(preparation) > CONTEXT_PROMPT_MAX_CHARS:
        failures.append("the context prompt exceeds the Windows-safe command-line budget")

    implementation = implementation_prompt(story, plan, context)
    for token in (relative(plan), relative(context)):
        if token not in implementation:
            failures.append(f"the implementation prompt does not mention {token!r}")
    if relative(legacy.EPICS_FILE) in implementation:
        failures.append("the implementation prompt still sends the worker to the backlog")

    # A complete package is reused; an incomplete one is prepared again.
    log_dir = LOG_ROOT / "self-test-context"
    log_dir.mkdir(parents=True, exist_ok=True)
    calls: list[str] = []

    def fake_runner(name, model, prompt, log_dir, timeout_minutes=0, retries=0, cwd=None, journal=None):
        calls.append(name)
        context.write_text(CONTEXT_SAMPLE, encoding="utf-8")

    context.parent.mkdir(parents=True, exist_ok=True)
    context.write_text(CONTEXT_SAMPLE, encoding="utf-8")
    if is_complete_context(context, plan):
        failures.append("a context package without its plan digest was accepted")
    prepare_context(story, plan, log_dir, runner=fake_runner)
    if calls != ["context"]:
        failures.append("a context package without its plan digest was not prepared")
    if not is_complete_context(context, plan):
        failures.append("a prepared context package was not bound to its plan")

    calls.clear()
    prepare_context(story, plan, log_dir, runner=fake_runner)
    if calls:
        failures.append("a current context package was prepared again")

    plan.write_text(plan.read_text(encoding="utf-8") + "\nCambio de plan.\n", encoding="utf-8")
    prepare_context(story, plan, log_dir, runner=fake_runner)
    if calls != ["context"]:
        failures.append("a context package was not invalidated after its plan changed")

    # A stale cache must not be re-stamped if the preparer writes no new file.
    plan.write_text(plan.read_text(encoding="utf-8") + "\nOtro cambio de plan.\n", encoding="utf-8")

    def silent_runner(*args, **kwargs):
        del args, kwargs

    try:
        prepare_context(story, plan, log_dir, runner=silent_runner)
    except legacy.DriverError:
        pass
    else:
        failures.append("a stale context package was re-bound when the preparer wrote nothing")

    # The production path writes in a worktree. A final timeout after complete
    # output is recovered, recorded as complete, sealed in the cache and reused.
    context.unlink(missing_ok=True)
    tree_root = WORKTREE_ROOT / "self-test-context"
    shutil.rmtree(tree_root, ignore_errors=True)
    recovery_tree = Worktree(story=story, path=tree_root, branch=story.branch)
    recovery_journal = PhaseJournal(log_dir / "recovery-phases.json")
    recovery_journal.record("context", PHASE_TIMEOUT, attempt=2)
    recovered_target = recovery_tree.path / relative(context)

    def late_success_runner(*args, **kwargs):
        del args, kwargs
        recovered_target.parent.mkdir(parents=True, exist_ok=True)
        recovered_target.write_text(CONTEXT_SAMPLE, encoding="utf-8")
        raise legacy.DriverError("the context phase timed out after writing its output")

    try:
        prepare_context(story, plan, log_dir, tree=recovery_tree, runner=late_success_runner, journal=recovery_journal)
    except legacy.DriverError:
        failures.append("a complete context written before a timeout was rejected")
    else:
        if not is_complete_context(context, plan) or not is_complete_context(recovered_target, plan):
            failures.append("a recovered context package was not sealed and copied to its worktree")
        if recovery_journal.state_of("context") != PHASE_COMPLETE:
            failures.append("a recovered context timeout was not recorded as complete")

    reused = []

    def unexpected_runner(*args, **kwargs):
        del args, kwargs
        reused.append("context")

    prepare_context(story, plan, log_dir, tree=recovery_tree, runner=unexpected_runner)
    if reused:
        failures.append("a recovered context package was not reused")

    # A timeout that leaves partial output remains a technical failure.
    context.unlink(missing_ok=True)
    recovery_journal = PhaseJournal(log_dir / "partial-recovery-phases.json")
    recovery_journal.record("context", PHASE_TIMEOUT, attempt=2)

    def partial_timeout_runner(*args, **kwargs):
        del args, kwargs
        context.write_text("## Requisitos\n", encoding="utf-8")
        raise legacy.DriverError("the context phase timed out with partial output")

    try:
        prepare_context(story, plan, log_dir, runner=partial_timeout_runner, journal=recovery_journal)
    except legacy.DriverError:
        pass
    else:
        failures.append("a partial context written before a timeout was accepted")

    context.write_text("## Requisitos\n", encoding="utf-8")
    if is_complete_context(context):
        failures.append("an incomplete context package was accepted")
    prepare_context(story, plan, log_dir, runner=fake_runner)
    if calls != ["context", "context"]:
        failures.append("an incomplete context package was not prepared again")

    # The package is copied into the checkout of the story under the same path.
    target = copy_context(context, tree_root)
    if target.relative_to(tree_root).as_posix() != ".build-lite/contexts/99-99.md":
        failures.append("the context package is not copied under the same path")
    if not target.exists():
        failures.append("the context package was not copied into the checkout")

    # A real commit must preserve story files while leaving the ignored cache
    # out. Naming the ignored path explicitly makes Git fail on Windows, so
    # exercise the exact staging path rather than only its helper predicates.
    commit_root = WORKTREE_ROOT / "self-test-context-commit"
    shutil.rmtree(commit_root, ignore_errors=True)
    legacy.git("init", "-q", str(commit_root), cwd=REPO_ROOT)
    legacy.git("config", "user.email", "self-test@example.invalid", cwd=commit_root)
    legacy.git("config", "user.name", "Build Lite self-test", cwd=commit_root)
    ensure_contexts_ignored(commit_root)
    story_file = commit_root / "src" / "Story.cs"
    story_file.parent.mkdir(parents=True, exist_ok=True)
    story_file.write_text("class Story {}\n", encoding="utf-8")
    cached_context = commit_root / ".build-lite" / "contexts" / "99-99.md"
    cached_context.parent.mkdir(parents=True, exist_ok=True)
    cached_context.write_text(CONTEXT_SAMPLE, encoding="utf-8")
    try:
        commit_story(story, cwd=commit_root)
        committed = legacy.git("show", "--format=", "--name-only", "HEAD", cwd=commit_root).stdout
        if "src/Story.cs" not in committed:
            failures.append("the story change was not committed with an ignored context cache")
        if ".build-lite/contexts/" in committed:
            failures.append("the context cache was committed with the story")
    except legacy.DriverError:
        failures.append("an ignored context cache prevented committing the story")

    context.unlink(missing_ok=True)
    plan.unlink(missing_ok=True)
    epics.unlink(missing_ok=True)
    shutil.rmtree(tree_root, ignore_errors=True)
    shutil.rmtree(commit_root, ignore_errors=True)
    shutil.rmtree(log_dir, ignore_errors=True)
    return failures


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
        '"shared_surfaces": [], "depends_on": [], "schema_changes": false}\n```\n'
    )
    if metadata is not None:
        failures.append("a metadata block with a scalar value was accepted")

    # A valid metadata block is read.
    metadata, reason = metadata_of(
        '```json\n{"reads": ["src/Shared/Contracts/"], "writes": ["src/Server/Program.cs"], '
        '"shared_surfaces": [], "depends_on": [], "schema_changes": false}\n```\n'
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
        '"shared_surfaces": ["persistence"], "depends_on": [], "schema_changes": false}\n```\n'
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
        '"shared_surfaces": [], "depends_on": ["5.2"], "schema_changes": false}\n```\n'
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

        metadata = PlanMetadata(depends_on=("4.4", "5.3"))
        if pending_dependency(metadata, {"5-3-recuperacion": "5.3"}, set()) != "5.3":
            failures.append("a completed dependency prevented checking a later pending dependency")

    # Two stories that write the same path never run at the same time.
    left, _ = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Server/Application/Catalog/"], '
        '"shared_surfaces": [], "depends_on": [], "schema_changes": false}\n```\n', "left.md"
    )
    right, _ = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Server/Application/Catalog/Index.cs"], '
        '"shared_surfaces": [], "depends_on": [], "schema_changes": false}\n```\n', "right.md"
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
        '"shared_surfaces": ["catalog"], "depends_on": [], "schema_changes": false}\n```\n', "surface-left.md"
    )
    right, _ = metadata_of(
        '```json\n{"reads": [], "writes": ["src/Client/B.cs"], '
        '"shared_surfaces": ["catalog"], "depends_on": [], "schema_changes": false}\n```\n', "surface-right.md"
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
        '"shared_surfaces": [], "depends_on": [], "schema_changes": false}\n```\n', "window.md"
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

    # A failure closes the scheduler admission window before another story can
    # be planned or started.
    if not admission_is_closed([("5.3", "verification failed")]):
        failures.append("a failed worker did not close the admission window")
    if admission_is_closed([]):
        failures.append("an error-free scheduler closed the admission window")

    shutil.rmtree(tree.path, ignore_errors=True)

    for path in plans.glob("*"):
        path.unlink(missing_ok=True)
    plans.rmdir()
    return failures


def self_test_phases() -> list[str]:
    """The structured states, the milestones and the detection of a stall."""
    failures: list[str] = []

    # The exit code of a phase tells how the phase ended.
    if state_for_exit_code(0) != PHASE_COMPLETE:
        failures.append("a phase that ended well was not recorded as complete")
    if state_for_exit_code(EXIT_TIMEOUT) != PHASE_TIMEOUT:
        failures.append("a phase that ran out of time was not recorded as a timeout")
    if state_for_exit_code(EXIT_STALLED) != PHASE_STALLED:
        failures.append("a phase without progress was not recorded as stalled")
    if state_for_exit_code(1) != PHASE_TOOL_FAILURE:
        failures.append("a phase that failed was not recorded as a tool failure")
    for state in PHASE_STATES:
        if state not in PHASE_STATES:
            failures.append(f"the state {state} is not a known state")

    # The journal records the state and the milestones of every phase.
    journal_path = LOG_ROOT / "self-test-phases" / "phases.json"
    journal = PhaseJournal(journal_path)
    journal.record("implementation", PHASE_STALLED, attempt=1, seconds=12.5, milestones=[
        {"kind": MILESTONE_FUNCTIONAL_CHANGE, "detail": "src/Server/Program.cs"}
    ])
    journal.record("story", PHASE_STALLED, error="the worker produced no change")
    if journal.state_of("implementation") != PHASE_STALLED:
        failures.append("the journal did not keep the state of a phase")
    if journal.failure_state() != PHASE_STALLED:
        failures.append("the journal did not explain why the story did not finish")
    if not journal_path.exists():
        failures.append("the journal did not leave its record on disk")
    try:
        recorded = json.loads(journal_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        recorded = []
    if not isinstance(recorded, list) or len(recorded) != 2:
        failures.append("the journal did not write a readable record")
    elif recorded[0].get("milestones", [{}])[0].get("kind") != MILESTONE_FUNCTIONAL_CHANGE:
        failures.append("the journal did not keep the milestones of a phase")

    # A resumed story keeps the phases of the previous attempt.
    resumed = PhaseJournal(journal_path)
    if len(resumed.entries) != 2:
        failures.append("a resumed story lost the phases of the previous attempt")

    # The milestones of an implementation that is still running.
    root = WORKTREE_ROOT / "self-test-progress"
    shutil.rmtree(root, ignore_errors=True)
    root.mkdir(parents=True, exist_ok=True)
    legacy.git("init", "-q", str(root), cwd=REPO_ROOT)
    transcript_dir = LOG_ROOT / "self-test-progress"
    transcript_dir.mkdir(parents=True, exist_ok=True)
    transcript = transcript_dir / "implementation-1.jsonl"
    transcript.write_text("", encoding="utf-8")
    progress = ImplementationProgress(root, transcript)
    if progress.poll() is not None:
        failures.append("an untouched worktree was read as a functional change")
    (root / "Program.cs").write_text("class Program {}\n", encoding="utf-8")
    milestone = progress.poll()
    if milestone is None or milestone["kind"] != MILESTONE_FUNCTIONAL_CHANGE:
        failures.append("a functional change was not recorded as a milestone")
    if progress.poll() is not None:
        failures.append("a milestone was reported twice for the same change")

    # Prompt headers and assistant text must never create milestones.
    with transcript.open("a", encoding="utf-8") as handle:
        handle.write("$ prompt mentions dotnet test and BLOQUEO: before the worker starts\n")
        handle.write('{"type":"text","part":{"type":"text","text":"BLOQUEO: prompt echo"}}\n')
        handle.write('{"type":"tool_use","part":{"type":"tool","tool":"read","state":{"status":"completed","input":{"filePath":"plan.md"}}}}\n')
    if progress.poll() is not None:
        failures.append("prompt or non-shell tool text was accepted as a milestone")

    with transcript.open("a", encoding="utf-8") as handle:
        handle.write('{"type":"tool_use","part":{"type":"tool","tool":"bash","state":{"status":"completed","input":{"command":"dotnet test"}}}}\n')
    milestone = progress.poll()
    if milestone is None or milestone["kind"] != MILESTONE_TEST_EXECUTED:
        failures.append("an executed test was not recorded as a milestone")
    with transcript.open("a", encoding="utf-8") as handle:
        handle.write('{"type":"tool_use","part":{"type":"tool","tool":"bash","state":{"status":"completed","input":{"command":"Write-Output \\\"BLOQUEO: falta el contrato en Shared\\\""}}}}\n')
    milestone = progress.poll()
    if milestone is None or milestone["kind"] != MILESTONE_BLOCKER_DEMONSTRATED:
        failures.append("a demonstrated blocker was not recorded as a milestone")
    for kind in MILESTONE_KINDS:
        if kind not in MILESTONE_KINDS:
            failures.append(f"the milestone {kind} is not a known milestone")

    # A stalled phase is never retried.
    if phase_may_retry(PHASE_STALLED, 1, 2):
        failures.append("a stalled phase was allowed to be retried")
    if not phase_may_retry(PHASE_TOOL_FAILURE, 1, 2):
        failures.append("a failed phase was not allowed its technical retry")
    if phase_may_retry(PHASE_TOOL_FAILURE, 2, 2):
        failures.append("a failed phase was retried after its budget was spent")
    if phase_may_retry(PHASE_COMPLETE, 1, 2):
        failures.append("a complete phase was retried")

    # A run that never progresses ends stalled, and not with the timeout.
    stall_dir = LOG_ROOT / "self-test-stall"
    stall_dir.mkdir(parents=True, exist_ok=True)
    stall_transcript = stall_dir / "implementation-1.jsonl"
    stall_transcript.write_text("", encoding="utf-8")
    quiet = WORKTREE_ROOT / "self-test-stall"
    shutil.rmtree(quiet, ignore_errors=True)
    quiet.mkdir(parents=True, exist_ok=True)
    legacy.git("init", "-q", str(quiet), cwd=REPO_ROOT)
    silent = ImplementationProgress(quiet, stall_transcript)
    code, seconds, milestones = watch_process(
        (sys.executable, "-c", "import time; time.sleep(60)"),
        stall_dir / "stall.jsonl",
        timeout=120,
        cwd=REPO_ROOT,
        progress=silent,
        progress_window=1.0,
        poll_seconds=0.2,
    )
    if code != EXIT_STALLED:
        failures.append("a run without progress did not end as stalled")
    if state_for_exit_code(code) != PHASE_STALLED:
        failures.append("a stalled run was not classified as stalled")
    if seconds > 30:
        failures.append("a stalled run was not stopped before the timeout")
    if milestones:
        failures.append("a run without progress reported a milestone")

    shutil.rmtree(quiet, ignore_errors=True)
    shutil.rmtree(stall_dir, ignore_errors=True)
    shutil.rmtree(root, ignore_errors=True)
    shutil.rmtree(transcript_dir, ignore_errors=True)
    shutil.rmtree(journal_path.parent, ignore_errors=True)
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
    ensure_contexts_ignored()
    legacy.require_clean_tree()
    legacy.fetch_origin()
    branch = legacy.git("branch", "--show-current").stdout.strip()
    if branch != BASE_BRANCH:
        raise legacy.DriverError(
            f"Build Lite must start from {BASE_BRANCH}; current branch is {branch or '(detached HEAD)'}"
        )
    # The scheduler selects pending stories from sprint-status.yaml. Read the
    # copy in origin/main after every scoped merge, not a stale local checkout
    # that would admit a story which already finished remotely.
    legacy.git("merge", "--ff-only", f"origin/{BASE_BRANCH}")
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
            tree = prepare_worktree(stories[0])
            if not legacy.git(
                "merge-base", "--is-ancestor", f"origin/{BASE_BRANCH}", "HEAD", check=False, cwd=tree.path
            ).ok:
                legacy.rebase_onto_main(cwd=tree.path)
            result = run_worker(stories[0], tree, metadata)
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
