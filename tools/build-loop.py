#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["pyyaml"]
# ///
"""build-loop — the unattended driver for the `bmad-build-auto` story loop.

The driver walks the executable backlog one story at a time. For each story it
creates the story branch from `main`, runs the `bmad-build-auto` skill inside
it, re-runs the project verifications, opens the pull request and squash-merges
it into `main`. The loop stops at the first story it cannot prove is complete.

Division of labour:

  * The skill owns the code. It plans, implements, reviews in four layers,
    patches, verifies and commits on the branch. It never pushes.
  * The driver owns version control and GitHub. It creates the branch, updates
    the sprint status, pushes, opens the pull request and merges it.

That split keeps `main` reachable only through a pull request that passed the
project's verifications, and it keeps the merge decision in a place that can be
reasoned about instead of buried inside the story run.

Usage:

    uv run tools/build-loop.py                 # walk the whole backlog
    uv run tools/build-loop.py --dry-run       # print the plan, change nothing
    uv run tools/build-loop.py --story 2.1     # run one story
    uv run tools/build-loop.py --no-merge      # open the PR, stop before merging
    uv run tools/build-loop.py --self-test     # check the driver itself
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
import tempfile
import time
import unicodedata
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
from typing import Iterable, Sequence

import yaml

REPO_ROOT = Path(__file__).resolve().parent.parent

EPICS_FILE = REPO_ROOT / "_bmad-output/planning-artifacts/epics/epics-lan-party-hub-2026-10-26/epics.md"
SPRINT_STATUS_FILE = REPO_ROOT / "_bmad-output/implementation-artifacts/sprint-status.yaml"
ARTIFACTS_DIR = REPO_ROOT / "_bmad-output/implementation-artifacts"
DEFAULT_LOG_DIR = REPO_ROOT / ".build-loop/logs"

BRANCH_PREFIX = "story"
BASE_BRANCH = "main"

# The project caps automatic fix/review cycles at two per story (docs/plan.md
# section 8). The skill enforces the cap as a HALT; the driver checks it too,
# counting the review passes recorded in the spec's triage log, so it never
# merges a story that overran the cap.
MAX_REVIEW_CYCLES = 2

# Severities that block a merge. `docs/plan.md` section 8 grades S0 and S1 as
# blocking; the skill's triage grades them `high`. A severity marked
# `unverified` comes from a `maybe-false` finding, which is not a finding: it
# is surfaced in the pull request body instead of blocking the loop.
BLOCKING_SEVERITIES = {"s0", "s1", "high", "critical", "blocker", "alta", "bloqueante"}

DEFAULT_COMMAND_TIMEOUT = 600.0
DEFAULT_VERIFICATION_TIMEOUT_MINUTES = 60.0

# A run that stops producing output for this long is not working, it is
# waiting for a provider request that will never come back.
DEFAULT_STALL_TIMEOUT_MINUTES = 12.0

# The model the unattended runs use. The default agent's own model fails to
# refresh its token in this environment, and a run that cannot answer leaves no
# spec behind, so the model is pinned and can be changed with --model.
DEFAULT_MODEL = "minimax-coding-plan/MiniMax-M3"

STORY_KEY_PATTERN = re.compile(r"^(\d+)-(\d+)-(.+)$")
SPEC_FILE_PATTERN = re.compile(r"^spec-(\d+)[.-](\d+)-.+\.md$")


@dataclass(frozen=True)
class VerificationCommand:
    label: str
    argv: tuple[str, ...]
    display: str


VERIFICATION_COMMANDS: tuple[VerificationCommand, ...] = (
    VerificationCommand(
        "build-loop self test",
        ("uv", "run", "tools/build-loop.py", "--self-test"),
        "uv run tools/build-loop.py --self-test",
    ),
    VerificationCommand(
        "dotnet build",
        ("dotnet", "build", "LANPartyHub.sln"),
        "dotnet build LANPartyHub.sln",
    ),
    VerificationCommand(
        "dotnet test",
        ("dotnet", "test", "LANPartyHub.sln"),
        "dotnet test LANPartyHub.sln",
    ),
    VerificationCommand(
        "check-encoding",
        ("powershell", "-NoProfile", "-File", "tools/check-encoding.ps1"),
        "powershell -NoProfile -File tools/check-encoding.ps1",
    ),
    VerificationCommand(
        "check-epics",
        ("powershell", "-NoProfile", "-File", "tools/check-epics.ps1"),
        "powershell -NoProfile -File tools/check-epics.ps1",
    ),
)


@dataclass
class CommandResult:
    argv: Sequence[str]
    code: int
    log: Path | None = None
    stdout: str = ""
    seconds: float = 0.0

    @property
    def ok(self) -> bool:
        return self.code == 0


@dataclass
class Story:
    epic: int
    num: int
    key: str
    title: str
    slug: str

    @property
    def short_id(self) -> str:
        return f"{self.epic}.{self.num}"

    @property
    def branch(self) -> str:
        return f"{BRANCH_PREFIX}/{self.epic}-{self.num}-{self.slug}"

    @property
    def label(self) -> str:
        return f"{self.epic}-{self.num}"


@dataclass
class GateResult:
    passed: bool
    reasons: list[str] = field(default_factory=list)


@dataclass
class StoryOutcome:
    story: Story
    merged: bool
    reason: str = ""
    pr_number: int | None = None
    pr_url: str | None = None


class DriverError(RuntimeError):
    """A condition that stops the loop."""


# ---------------------------------------------------------------------------
# Shell helpers
# ---------------------------------------------------------------------------


_LOG_HANDLE = None


def _log(message: str) -> None:
    stamp = datetime.now().strftime("%H:%M:%S")
    line = f"[{stamp}] {message}"
    print(line, flush=True)
    if _LOG_HANDLE is not None:
        _LOG_HANDLE.write(line + "\n")
        _LOG_HANDLE.flush()


def _open_driver_log(path: Path) -> None:
    global _LOG_HANDLE
    path.parent.mkdir(parents=True, exist_ok=True)
    _LOG_HANDLE = path.open("a", encoding="utf-8", errors="replace")


def _kill_process_tree(process: subprocess.Popen) -> None:
    """Kill the child and everything it spawned."""
    if sys.platform == "win32":
        subprocess.run(
            ("taskkill", "/PID", str(process.pid), "/T", "/F"),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
    else:
        process.kill()


def run_command(
    argv: Sequence[str],
    log_path: Path | None = None,
    timeout: float | None = DEFAULT_COMMAND_TIMEOUT,
    cwd: Path = REPO_ROOT,
) -> CommandResult:
    """Run a command, streaming its output to `log_path` when given."""
    started = time.time()
    if log_path is None:
        try:
            completed = subprocess.run(
                list(argv),
                cwd=str(cwd),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=timeout,
                stdin=subprocess.DEVNULL,
            )
        except subprocess.TimeoutExpired:
            return CommandResult(argv=argv, code=124, seconds=time.time() - started)
        return CommandResult(
            argv=argv,
            code=completed.returncode,
            stdout=(completed.stdout or "") + (completed.stderr or ""),
            seconds=time.time() - started,
        )

    log_path.parent.mkdir(parents=True, exist_ok=True)
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
        try:
            code = process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            _kill_process_tree(process)
            try:
                process.wait(timeout=30)
            except subprocess.TimeoutExpired:
                pass
            code = 124
    return CommandResult(argv=argv, code=code, log=log_path, seconds=time.time() - started)


def run_command_watched(
    argv: Sequence[str],
    log_path: Path,
    timeout: float | None = DEFAULT_COMMAND_TIMEOUT,
    stall_timeout: float | None = None,
    cwd: Path = REPO_ROOT,
) -> CommandResult:
    """Run a command and kill it when its output stops growing.

    A provider request that never answers looks exactly like a long run: the
    process is alive and the log does not move. The stall timeout tells the
    two apart, and the loop continues the session instead of waiting for a
    request that will never come back.
    """
    started = time.time()
    log_path.parent.mkdir(parents=True, exist_ok=True)
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
        last_size = -1
        last_change = time.time()
        while True:
            try:
                code = process.wait(timeout=15)
                break
            except subprocess.TimeoutExpired:
                pass
            if timeout is not None and time.time() - started > timeout:
                _kill_process_tree(process)
                try:
                    process.wait(timeout=30)
                except subprocess.TimeoutExpired:
                    pass
                code = 124
                break
            try:
                size = log_path.stat().st_size
            except OSError:
                size = -1
            if size != last_size:
                last_size = size
                last_change = time.time()
            elif stall_timeout is not None and time.time() - last_change > stall_timeout:
                _log(f"the command stopped producing output for {stall_timeout / 60:.0f} minutes")
                _kill_process_tree(process)
                try:
                    process.wait(timeout=30)
                except subprocess.TimeoutExpired:
                    pass
                code = 125
                break
    return CommandResult(argv=argv, code=code, log=log_path, seconds=time.time() - started)


def git(*args: str, check: bool = True, timeout: float | None = DEFAULT_COMMAND_TIMEOUT) -> CommandResult:
    result = run_command(("git", *args), timeout=timeout)
    if check and not result.ok:
        raise DriverError(f"git {' '.join(args)} failed ({result.code}): {result.stdout.strip()}")
    return result


def gh(*args: str, check: bool = True, timeout: float | None = DEFAULT_COMMAND_TIMEOUT) -> CommandResult:
    result = run_command(("gh", *args), timeout=timeout)
    if check and not result.ok:
        raise DriverError(f"gh {' '.join(args)} failed ({result.code}): {result.stdout.strip()}")
    return result


# ---------------------------------------------------------------------------
# Backlog reading
# ---------------------------------------------------------------------------


def slugify(title: str) -> str:
    """Kebab-case slug without accents, matching the existing story branches."""
    decomposed = unicodedata.normalize("NFKD", title)
    ascii_text = "".join(ch for ch in decomposed if not unicodedata.combining(ch))
    slug = re.sub(r"[^a-zA-Z0-9]+", "-", ascii_text).strip("-").lower()
    return slug or "story"


def parse_development_status(text: str) -> dict:
    try:
        data = yaml.safe_load(text) or {}
    except yaml.YAMLError as error:
        raise DriverError(f"the sprint status is not valid YAML: {error}") from error
    if not isinstance(data, dict):
        raise DriverError("the sprint status root is not a mapping")
    status = data.get("development_status") or {}
    if not isinstance(status, dict):
        raise DriverError("the sprint status does not expose a 'development_status' mapping")
    return status


def read_sprint_status(path: Path = SPRINT_STATUS_FILE) -> dict:
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as error:
        raise DriverError(f"{path} cannot be read: {error}") from error
    return parse_development_status(text)


def read_story_titles(epics_file: Path = EPICS_FILE) -> dict[tuple[int, int], str]:
    """Every story title in the backlog, read once."""
    pattern = re.compile(r"^###\s+Story\s+(\d+)\.(\d+)\s*:\s*(.+?)\s*$")
    try:
        content = epics_file.read_text(encoding="utf-8")
    except OSError as error:
        raise DriverError(f"{epics_file} cannot be read: {error}") from error
    titles: dict[tuple[int, int], str] = {}
    for line in content.splitlines():
        match = pattern.match(line)
        if match:
            titles[(int(match.group(1)), int(match.group(2)))] = match.group(3).strip()
    return titles


def read_story_title(epic: int, num: int, epics_file: Path = EPICS_FILE) -> str:
    title = read_story_titles(epics_file).get((epic, num))
    if title is None:
        raise DriverError(f"Story {epic}.{num} is not declared in {epics_file}")
    return title


def pending_stories(status: dict | None = None) -> list[Story]:
    """Every story that is not finished yet, in epic/story order.

    A story left in `review` or `in-progress` is still pending: the loop has
    to finish it before moving on, otherwise the epic never closes.
    """
    status = status if status is not None else read_sprint_status()
    candidates: list[tuple[int, int, str]] = []
    for key, value in status.items():
        match = STORY_KEY_PATTERN.match(str(key))
        if not match:
            continue
        if str(value).strip().lower() == "done":
            continue
        candidates.append((int(match.group(1)), int(match.group(2)), str(key)))
    candidates.sort()
    titles = read_story_titles()
    stories: list[Story] = []
    for epic, num, key in candidates:
        title = titles.get((epic, num))
        if title is None:
            raise DriverError(
                f"the sprint status declares {key} but the backlog has no '### Story {epic}.{num}'"
            )
        stories.append(Story(epic=epic, num=num, key=key, title=title, slug=slugify(title)))
    return stories


def select_next_story(status: dict | None = None) -> Story | None:
    """The lowest pending story in epic/story order."""
    stories = pending_stories(status)
    return stories[0] if stories else None


def story_key_for(epic: int, num: int, status: dict) -> str | None:
    """The sprint status key of a story, which is not always the full slug."""
    for key in status:
        match = STORY_KEY_PATTERN.match(str(key))
        if match and int(match.group(1)) == epic and int(match.group(2)) == num:
            return str(key)
    return None


def story_by_id(epic: int, num: int, status: dict) -> Story:
    title = read_story_title(epic, num)
    key = story_key_for(epic, num, status)
    if key is None:
        raise DriverError(f"story {epic}.{num} has no entry in {SPRINT_STATUS_FILE}")
    return Story(epic=epic, num=num, key=key, title=title, slug=slugify(title))


def find_spec(story: Story, artifacts: Path = ARTIFACTS_DIR) -> Path | None:
    """The spec file the skill wrote for this story, if any.

    The skill leads the spec slug with the tracking identifier, so the name
    is `spec-<epic>-<num>-<slug>.md`; a dot between the numbers is accepted
    too. When the skill writes a second spec for the same story it appends a
    `-2` suffix, so the newest one wins.
    """
    matches: list[Path] = []
    for candidate in artifacts.glob("spec-*.md"):
        match = SPEC_FILE_PATTERN.match(candidate.name)
        if match and int(match.group(1)) == story.epic and int(match.group(2)) == story.num:
            matches.append(candidate)
    if not matches:
        return None
    return max(matches, key=lambda path: (path.stat().st_mtime, path.name))


def read_frontmatter(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        return {}
    for index, line in enumerate(lines[1:], start=1):
        if line.strip() == "---":
            try:
                data = yaml.safe_load("\n".join(lines[1:index])) or {}
            except yaml.YAMLError as error:
                raise DriverError(f"the frontmatter of {path.name} is not valid YAML: {error}") from error
            return data if isinstance(data, dict) else {}
    return {}


def read_text_preserving_newlines(path: Path) -> str:
    """Read a file without translating its line endings."""
    with path.open("r", encoding="utf-8", newline="") as handle:
        return handle.read()


def write_text_preserving_newlines(path: Path, text: str) -> None:
    """Write a file without translating its line endings."""
    with path.open("w", encoding="utf-8", newline="") as handle:
        handle.write(text)


def read_section(text: str, heading: str) -> str:
    """The body of a `## heading` section, up to the next `##` heading."""
    pattern = re.compile(rf"^##\s+{re.escape(heading)}\s*$", re.MULTILINE)
    match = pattern.search(text)
    if not match:
        return ""
    rest = text[match.end():]
    next_heading = re.search(r"^##\s+", rest, re.MULTILINE)
    return rest[: next_heading.start()] if next_heading else rest


def read_bullet(section: str, label: str) -> str:
    """The text of a bullet whose bold label is `label`.

    The skill writes two shapes: `- **Label.** text` and `- **Label:** text`,
    and the body of a bullet can wrap onto the following lines.
    """
    pattern = re.compile(
        rf"^-\s+\*\*{re.escape(label)}\s*[.:]?\*\*\s*(.*?)(?=\n-\s+\*\*|\n\n|\Z)",
        re.MULTILINE | re.DOTALL,
    )
    match = pattern.search(section)
    if not match:
        return ""
    return " ".join(match.group(1).split())


def review_pass_count(spec_text: str) -> int:
    """How many review passes the triage log records."""
    section = read_section(spec_text, "Review Triage Log")
    return len(re.findall(r"^###\s+.*Review pass\s*$", section, re.MULTILINE))


def deferred_high_findings(spec_text: str) -> list[str]:
    """Findings the triage log graded `high` and routed to `defer`."""
    section = read_section(spec_text, "Review Triage Log")
    rows = re.findall(r"^\s*-\s+`\[high\]`.*?`\[defer\]`\s*(.*)$", section, re.MULTILINE)
    return [row.strip() for row in rows]


# ---------------------------------------------------------------------------
# Sprint status
# ---------------------------------------------------------------------------


def epic_is_complete(epic: int, status: dict) -> bool:
    stories = [
        value
        for key, value in status.items()
        if (match := STORY_KEY_PATTERN.match(str(key))) and int(match.group(1)) == epic
    ]
    return bool(stories) and all(str(value).strip().lower() == "done" for value in stories)


def update_sprint_status(story: Story, path: Path = SPRINT_STATUS_FILE) -> str:
    """Mark the story as done and refresh the epic state, in place."""
    text = read_text_preserving_newlines(path)

    def replace_entry(source: str, key: str, value: str) -> str:
        # The lookahead leaves the line ending untouched, so a CRLF file stays CRLF.
        pattern = re.compile(rf"^([ \t]*){re.escape(key)}[ \t]*:[^\r\n]*(?=\r?$)", re.MULTILINE)
        if not pattern.search(source):
            raise DriverError(f"sprint status has no entry for '{key}'")
        return pattern.sub(lambda m: f"{m.group(1)}{key}: {value}", source, count=1)

    text = replace_entry(text, story.key, "done")
    # The epic state is computed from the mapping the story transition
    # produced, otherwise the last story of an epic could never close it.
    status = parse_development_status(text)
    epic_value = "done" if epic_is_complete(story.epic, status) else "in-progress"
    text = replace_entry(text, f"epic-{story.epic}", epic_value)
    timestamp = datetime.now().strftime("%m-%d-%Y %H:%M")
    text = re.sub(
        r"^last_updated:[^\r\n]*(?=\r?$)",
        f"last_updated: {timestamp}",
        text,
        count=1,
        flags=re.MULTILINE,
    )
    write_text_preserving_newlines(path, text)
    return epic_value


# ---------------------------------------------------------------------------
# Gates
# ---------------------------------------------------------------------------


def evaluate_merge_gate(meta: dict, spec_text: str = "") -> GateResult:
    """The story is mergeable only when nothing is left open on it."""
    reasons: list[str] = []

    status = str(meta.get("status", "")).strip().lower()
    if status != "done":
        reasons.append(f"the spec status is '{status or 'missing'}', not 'done'")

    # The four layer review is what replaced the human review, so the spec has
    # to carry its evidence: a spec with no recorded review pass was not
    # reviewed, whatever its status says.
    passes = review_pass_count(spec_text)
    if passes == 0:
        reasons.append("the spec records no review pass, so the four layer review did not run")

    deferred = meta.get("deferred")
    if deferred is None:
        deferred = []
    if not isinstance(deferred, list):
        reasons.append("the 'deferred' frontmatter field is not a list")
    else:
        for item in deferred:
            if not isinstance(item, dict):
                reasons.append(f"a deferred entry is malformed: {item!r}")
                continue
            severity = str(item.get("severity", "")).strip()
            if not severity:
                reasons.append(
                    f"a deferred finding has no severity: {str(item.get('summary', '')).strip() or 'unnamed'}"
                )
                continue
            head = severity.split()[0].lower()
            if head in BLOCKING_SEVERITIES and "unverified" not in severity.lower():
                summary = str(item.get("summary", "")).strip() or "unnamed finding"
                reasons.append(f"a blocking severity finding is deferred: {summary}")

    cycles = meta.get("review_loop_iteration")
    try:
        cycles = int(cycles)
    except (TypeError, ValueError):
        cycles = 0
    # The first review pass is not a repair cycle; every pass after it is one.
    cycles_used = max(passes - 1, cycles)
    if cycles_used > MAX_REVIEW_CYCLES:
        reasons.append(
            f"the story used {cycles_used} repair cycles, over the cap of {MAX_REVIEW_CYCLES}"
        )

    for row in deferred_high_findings(spec_text):
        reasons.append(f"the triage log defers a high finding: {row}")

    return GateResult(passed=not reasons, reasons=reasons)


def run_verifications(
    log_dir: Path, timeout_minutes: float = DEFAULT_VERIFICATION_TIMEOUT_MINUTES
) -> list[tuple[VerificationCommand, CommandResult]]:
    """The driver's own gate first, then the four project verifications."""
    results: list[tuple[VerificationCommand, CommandResult]] = []
    for item in VERIFICATION_COMMANDS:
        log_path = log_dir / f"verify-{item.label.replace(' ', '-')}.log"
        _log(f"verification: {item.label}")
        result = run_command(item.argv, log_path=log_path, timeout=timeout_minutes * 60)
        results.append((item, result))
        state = "OK" if result.ok else f"FAIL ({result.code})"
        _log(f"verification: {item.label} -> {state} in {result.seconds:.0f}s")
    return results


# ---------------------------------------------------------------------------
# Git and GitHub operations
# ---------------------------------------------------------------------------


def preserve_pending_work(story: Story) -> None:
    """Commit the work of an interrupted story so it is never lost.

    The work of a story that did not finish is uncommitted, and uncommitted
    work only exists in the working tree: it cannot survive a checkout, and
    the driver would have to be launched from a dirty branch with an outdated
    copy of itself. Committing it as bookkeeping keeps the tree clean and the
    story recoverable. The subject starts with `chore(` on purpose, so it is
    left out of the squashed commit that lands on `main`.
    """
    if not branch_exists(story.branch):
        return
    branch = git("rev-parse", "--abbrev-ref", "HEAD", check=False).stdout.strip()
    if branch != story.branch:
        return
    if not git("status", "--porcelain").stdout.strip():
        return
    _log(f"committing the pending work of story {story.short_id} so it is not lost")
    git("add", "-A")
    git("commit", "-m", f"chore(story): work in progress for story {story.short_id}")


def require_clean_tree() -> None:
    """No tracked file may be modified before the loop starts.

    Untracked files are allowed: they are the work of an interrupted story
    (a spec that was never committed, the code the implementation subagent
    wrote before the run died) and they belong to the branch the story resumes
    on. They are committed with the story, not thrown away.
    """
    modified = git("diff", "--name-only").stdout.strip()
    if modified:
        raise DriverError(f"the working tree has modified tracked files:\n{modified}")
    staged = git("diff", "--cached", "--name-only").stdout.strip()
    if staged:
        raise DriverError(f"the working tree has staged changes:\n{staged}")


def fetch_origin() -> None:
    """Refresh the remote references without touching the working tree.

    The driver never checks `main` out while a story has work in progress: a
    checkout carries the staged files along and the story's branch stops being
    the only place where they live.
    """
    git("fetch", "origin", "--prune")


def branch_exists(branch: str) -> bool:
    return git("rev-parse", "--verify", "--quiet", f"refs/heads/{branch}", check=False).ok


def remote_branch_exists(branch: str) -> bool:
    return git("rev-parse", "--verify", "--quiet", f"refs/remotes/origin/{branch}", check=False).ok


def create_branch(branch: str) -> None:
    """Create the story branch from `main`."""
    git("checkout", "-b", branch, BASE_BRANCH)


def is_base_ancestor() -> bool:
    """True when `origin/main` is already contained in the current branch."""
    git("fetch", "origin", BASE_BRANCH)
    result = git("merge-base", "--is-ancestor", f"origin/{BASE_BRANCH}", "HEAD", check=False)
    return result.ok


def rebase_onto_main() -> None:
    """Rebase the story branch onto the current main, keeping the work.

    The work of an interrupted story is usually uncommitted, and a rebase
    refuses a dirty tree. It is stashed, the branch is rebased and the work
    comes back staged, so the story's own commit stays the first one and its
    subject is the one that lands on `main`.
    """
    _log("main moved while the story ran: rebasing the branch onto it")
    dirty = git("status", "--porcelain").stdout.strip()
    stashed = False
    if dirty:
        git("stash", "push", "--include-untracked", "-m", "build-loop rebase")
        stashed = True
    try:
        git("rebase", f"origin/{BASE_BRANCH}")
    except DriverError:
        git("rebase", "--abort", check=False)
        if stashed:
            git("stash", "pop", "--index", check=False)
        raise DriverError("the rebase onto main conflicted; the branch was left untouched")
    if stashed:
        try:
            git("stash", "pop", "--index")
        except DriverError as error:
            raise DriverError(
                f"the rebase onto main worked but the work of the story could not be restored: {error}"
            )


def push_branch(branch: str) -> None:
    """Push the story branch, replacing a previously published attempt."""
    if remote_branch_exists(branch):
        git("push", "--force-with-lease", "origin", branch)
    else:
        git("push", "--set-upstream", "origin", branch)


def open_pull_request(story: Story, title: str, body: str) -> tuple[int, str]:
    result = gh(
        "pr",
        "create",
        "--base",
        BASE_BRANCH,
        "--head",
        story.branch,
        "--title",
        title,
        "--body",
        body,
    )
    url = ""
    for line in result.stdout.splitlines():
        if "github.com" in line and "/pull/" in line:
            url = line.strip()
    match = re.search(r"/pull/(\d+)", url)
    if match:
        return int(match.group(1)), url
    number = gh("pr", "view", "--json", "number,url").stdout
    fields = json.loads(number or "{}")
    if "number" not in fields:
        raise DriverError(f"could not read the pull request number from: {result.stdout.strip()}")
    return int(fields["number"]), str(fields.get("url", ""))


def find_open_pull_request(branch: str) -> tuple[int, str] | None:
    result = gh("pr", "list", "--head", branch, "--state", "open", "--json", "number,url", check=False)
    if not result.ok:
        raise DriverError(f"gh pr list failed: {result.stdout.strip()}")
    try:
        items = json.loads(result.stdout or "[]")
    except json.JSONDecodeError as error:
        raise DriverError(f"gh pr list returned unreadable output: {error}") from error
    if not items:
        return None
    return int(items[0]["number"]), str(items[0]["url"])


def discard_untracked_specs(story: Story) -> None:
    """Remove the untracked specs of an abandoned attempt.

    `git checkout -B` restores the tracked files but leaves the untracked
    ones, and a spec left behind would collide with the one the next attempt
    writes.
    """
    listing = git("status", "--porcelain", "--untracked-files=all").stdout
    prefix = f"spec-{story.label}-"
    for line in listing.splitlines():
        if not line.startswith("?? "):
            continue
        path = line[3:].strip().strip('"')
        name = Path(path).name
        if name.startswith(prefix) and SPEC_FILE_PATTERN.match(name):
            target = REPO_ROOT / path
            if target.exists():
                _log(f"removing the spec left by the abandoned attempt: {path}")
                target.unlink()


def merge_pull_request(pr_number: int, subject: str, body: str) -> None:
    gh("pr", "merge", str(pr_number), "--squash", "--subject", subject, "--body", body)


def delete_remote_branch(branch: str) -> None:
    """Remove the story branch from the remote once the merge landed."""
    if remote_branch_exists(branch):
        git("push", "origin", "--delete", branch, check=False)


def parse_commit_messages(raw: str) -> list[str]:
    """Split the output of `git log --format=%H%x1f%B%x1e` into commit messages."""
    messages: list[str] = []
    for chunk in raw.split("\x1e"):
        chunk = chunk.strip("\n")
        if not chunk.strip():
            continue
        _, _, message = chunk.partition("\x1f")
        message = message.strip("\n")
        if message:
            messages.append(message)
    return messages


def select_commit_message(messages: Sequence[str], skip_prefix: str = "chore(") -> tuple[str, str]:
    """The subject and body of the squashed commit, from the branch messages.

    The oldest commit carries the story, so its subject is the one that lands
    on `main`. The driver's own bookkeeping commits are left out: they are part
    of the story branch but not part of the story.
    """
    story_messages = [message for message in messages if not message.startswith(skip_prefix)]
    if not story_messages:
        raise DriverError("the story branch carries no story commit")
    first = story_messages[0]
    subject, _, remainder = first.partition("\n")
    body_parts = [remainder.strip()] if remainder.strip() else []
    body_parts.extend(message for message in story_messages[1:])
    return subject.strip(), "\n\n".join(part for part in body_parts if part)


def branch_commit_message(base: str = f"origin/{BASE_BRANCH}") -> tuple[str, str]:
    """The conventional commit message the story branch carries."""
    raw = git("log", "--reverse", "--format=%H%x1f%B%x1e", f"{base}..HEAD").stdout
    messages = parse_commit_messages(raw)
    if not messages:
        raise DriverError(f"the story branch carries no commit against {base}")
    return select_commit_message(messages)


def is_conventional_subject(subject: str) -> bool:
    """The repository only accepts conventional commits."""
    pattern = r"^(feat|fix|chore|docs|refactor|test|perf|build|ci|style|revert)(\([^)]+\))?!?: .+"
    return re.match(pattern, subject.strip()) is not None


# ---------------------------------------------------------------------------
# The story run
# ---------------------------------------------------------------------------


def build_intent(story: Story) -> str:
    epics_relative = EPICS_FILE.relative_to(REPO_ROOT).as_posix()
    return (
        f"Historia {story.short_id} — {story.title}. "
        f"Story key del sprint status: {story.key}. "
        f"Backlog vigente: {epics_relative}, sección Story {story.short_id}. "
        "Implementa exclusivamente el alcance de esta historia."
    )


def run_skill(
    story: Story,
    log_dir: Path,
    timeout_minutes: float,
    model: str,
    agent: str = "",
    session_id: str = "",
    spec_path: Path | None = None,
    spec_status: str = "",
    attempt: int = 1,
    stall_timeout_minutes: float = DEFAULT_STALL_TIMEOUT_MINUTES,
) -> tuple[CommandResult, str]:
    """Run the skill and return its result plus the opencode session id.

    The session id is what makes a story resumable: a run that ends before the
    spec is `done` has usually spent its step allowance, and continuing the
    same session resets that allowance and picks the work up where it
    stopped instead of starting over.
    """
    if session_id:
        if spec_path is not None:
            prompt = (
                f"Continua la historia {story.short_id} desde donde la dejaste. El spec esta en "
                f"{spec_path.relative_to(REPO_ROOT).as_posix()} con status `{spec_status or 'draft'}`: "
                "retomalo desde el paso del skill que le corresponde y termina el workflow "
                "(implementacion, revision en cuatro capas, verificacion y commit). "
                "No pidas decisiones a una persona y no esperes aprobaciones."
            )
        else:
            prompt = (
                f"Continua la historia {story.short_id} desde donde la dejaste y termina el workflow "
                "del skill: spec, implementacion, revision en cuatro capas, verificacion y commit. "
                "No pidas decisiones a una persona y no esperes aprobaciones."
            )
    else:
        prompt = f"bmad-build-auto {build_intent(story)}"
        if spec_path is not None and spec_status != "done":
            prompt += (
                f" El spec de esta historia ya esta en {spec_path.relative_to(REPO_ROOT).as_posix()} "
                f"con status `{spec_status or 'draft'}`: retomalo desde el paso del skill que le "
                "corresponde en vez de empezar de cero."
            )

    log_dir.mkdir(parents=True, exist_ok=True)
    json_log = log_dir / f"skill-{attempt}.jsonl"
    text_log = log_dir / f"skill-{attempt}.log"
    _log(f"running the bmad-build-auto skill for story {story.short_id} (attempt {attempt})")
    _log(f"log: {text_log}")

    argv = ["opencode", "run", "--format", "json", "--dangerously-skip-permissions"]
    if model:
        argv.extend(["--model", model])
    if agent:
        argv.extend(["--agent", agent])
    if session_id:
        argv.extend(["--session", session_id])
    argv.append(prompt)

    result = run_command_watched(
        argv,
        log_path=json_log,
        timeout=timeout_minutes * 60,
        stall_timeout=stall_timeout_minutes * 60,
    )
    discovered, transcript = parse_skill_output(json_log)
    text_log.write_text(transcript, encoding="utf-8")
    (log_dir / "skill.log").write_text(transcript, encoding="utf-8")
    return result, discovered or session_id


def parse_skill_output(path: Path) -> tuple[str, str]:
    """The session id and a readable transcript out of the json event stream."""
    session_id = ""
    lines: list[str] = []
    if not path.exists():
        return session_id, ""
    for raw in path.read_text(encoding="utf-8", errors="replace").splitlines():
        raw = raw.strip()
        if not raw.startswith("{"):
            continue
        try:
            event = json.loads(raw)
        except json.JSONDecodeError:
            continue
        session_id = session_id or str(event.get("sessionID", ""))
        kind = event.get("type", "")
        part = event.get("part") or {}
        if kind == "text":
            text = str(part.get("text", "")).strip()
            if text:
                lines.append(text)
        elif kind == "tool":
            tool = str(part.get("tool", ""))
            state = str(part.get("state", ""))
            status = (state.get("status") if isinstance(state, dict) else "") or ""
            detail = ""
            if isinstance(state, dict):
                detail = str(state.get("title", "") or state.get("output", "") or "")
            lines.append(f"[{tool}] {status} {detail}".strip()[:400])
    return session_id, "\n".join(lines) + "\n"


def log_tail(path: Path | None, lines: int = 15) -> str:
    """The last lines of a log, for an error message that has to stand alone."""
    if path is None or not path.exists():
        return ""
    content = path.read_text(encoding="utf-8", errors="replace").splitlines()
    return "\n".join(content[-lines:])


def build_pull_request_body(
    story: Story,
    spec_text: str,
    meta: dict,
    verifications: Iterable[tuple[VerificationCommand, CommandResult]],
    branch: str = "",
    revision: str = "",
    spec_path: str = "",
) -> str:
    result_section = read_section(spec_text, "Auto Run Result")
    summary = read_bullet(result_section, "Resumen")
    files = read_bullet(result_section, "Ficheros")
    review = read_bullet(result_section, "Revisión")
    risks = read_bullet(result_section, "Riesgos residuales")
    followup = read_bullet(result_section, "Revisión de seguimiento recomendada")

    deferred = meta.get("deferred") or []
    deferred_text = ""
    if isinstance(deferred, list) and deferred:
        lines = []
        for item in deferred:
            if not isinstance(item, dict):
                continue
            severity = str(item.get("severity", "")).strip() or "sin severidad"
            summary_text = str(item.get("summary", "")).strip()
            lines.append(f"- `{severity}` {summary_text}")
        deferred_text = "\n".join(lines)

    lines: list[str] = []
    lines.append("## Qué entrega")
    lines.append("")
    lines.append(f"**Historia {story.short_id} — {story.title}.**")
    lines.append("")
    if summary:
        lines.append(summary)
        lines.append("")
    if files:
        lines.append("## Ficheros")
        lines.append("")
        lines.append(files)
        lines.append("")
    if review:
        lines.append("## Revisión")
        lines.append("")
        lines.append(review)
        lines.append("")
    lines.append("## Verificación")
    lines.append("")
    lines.append("```powershell")
    for command, result in verifications:
        state = "PASS" if result.ok else f"FAIL ({result.code})"
        lines.append(f"{command.display}  # {state}")
    lines.append("```")
    lines.append("")
    if deferred_text:
        lines.append("## Diferidos")
        lines.append("")
        lines.append(deferred_text)
        lines.append("")
    if risks:
        lines.append("## Riesgos residuales")
        lines.append("")
        lines.append(risks)
        lines.append("")
    if followup:
        recommended = str(meta.get("followup_review_recommended", "")).strip().lower()
        lines.append("## Revisión de seguimiento")
        lines.append("")
        lines.append(f"`followup_review_recommended`: {recommended or 'no declarado'}.")
        lines.append("")
        lines.append(followup)
        lines.append("")
    lines.append("## Traza")
    lines.append("")
    lines.append(f"- Rama: `{branch}`")
    lines.append(f"- Spec: `{spec_path}`")
    lines.append(f"- Revisión: `{revision}`")
    lines.append("")
    lines.append("---")
    lines.append("")
    lines.append(
        "Fusionado automáticamente por `tools/build-loop.py`: el skill pasa los cuatro comandos "
        "del proyecto durante la historia y el driver los vuelve a pasar antes de abrir el PR."
    )
    return "\n".join(lines).rstrip() + "\n"


def update_and_commit_sprint_status(story: Story) -> None:
    epic_value = update_sprint_status(story)
    _log(f"sprint status: story {story.label} done, epic-{story.epic} {epic_value}")
    git("add", str(SPRINT_STATUS_FILE.relative_to(REPO_ROOT)))
    if git("diff", "--cached", "--quiet", check=False).ok:
        _log("the sprint status was already up to date")
        return
    git("commit", "-m", f"chore(sprint): mark story {story.short_id} as done")


def spec_belongs_to_story(spec: Path, story: Story) -> bool:
    """The spec is the one of this story."""
    meta = read_frontmatter(spec)
    key = str(meta.get("story_key", "")).strip()
    if key and not key.startswith(f"{story.label}-"):
        return False
    return True


def ensure_committed(path: Path) -> None:
    """Commit a file the skill left out of its own commit."""
    relative = path.relative_to(REPO_ROOT).as_posix()
    if not git("status", "--porcelain", "--", relative).stdout.strip():
        return
    _log(f"committing the file the skill left out: {relative}")
    git("add", relative)
    git("commit", "-m", f"chore(spec): record {path.name}")


def process_story(story: Story, args: argparse.Namespace, log_dir: Path) -> StoryOutcome:
    _log("=" * 72)
    _log(f"story {story.short_id} — {story.title}")
    _log(f"branch: {story.branch}")
    _log("=" * 72)

    fetch_origin()
    existing_pr = find_open_pull_request(story.branch)
    main_before = git("rev-parse", f"origin/{BASE_BRANCH}").stdout.strip()

    # The reuse decision needs the story branch checked out: the spec of an
    # interrupted attempt only exists in that branch, and whether it is
    # committed there is what tells an interrupted story from an abandoned one.
    spec: Path | None = find_spec(story)
    reuse = False
    if branch_exists(story.branch):
        git("checkout", story.branch)
        spec = find_spec(story)
        reuse = existing_pr is not None or spec is not None
        if not reuse:
            _log(f"discarding the abandoned branch {story.branch}")
            git("checkout", "-B", story.branch, f"origin/{BASE_BRANCH}")
            discard_untracked_specs(story)
            spec = None
    else:
        git("checkout", "-b", story.branch, f"origin/{BASE_BRANCH}")
        spec = find_spec(story)

    if existing_pr and spec and str(read_frontmatter(spec).get("status", "")).lower() == "done":
        _log(f"resuming: pull request #{existing_pr[0]} is already open and the spec is done")
    else:
        session_id = ""
        for attempt in range(1, args.max_attempts + 1):
            status = str(read_frontmatter(spec).get("status", "")).lower() if spec else ""
            skill_result, session_id = run_skill(
                story,
                log_dir,
                args.timeout_minutes,
                args.model,
                args.agent,
                session_id,
                spec_path=spec,
                spec_status=status,
                attempt=attempt,
                stall_timeout_minutes=args.stall_timeout_minutes,
            )
            _log(f"the skill finished with exit code {skill_result.code}")
            if skill_result.code == 125:
                _log(
                    f"the run stalled; continuing the session {session_id} "
                    f"(attempt {attempt + 1} of {args.max_attempts})"
                )
                if attempt < args.max_attempts and session_id:
                    continue
                raise DriverError(
                    f"the skill run stalled and could not be continued; "
                    f"read {log_dir / f'skill-{attempt}.log'}"
                )
            if not skill_result.ok:
                raise DriverError(
                    f"the bmad-build-auto run failed with exit code {skill_result.code}; "
                    f"read {log_dir / f'skill-{attempt}.log'}"
                )
            main_after = git("rev-parse", f"origin/{BASE_BRANCH}").stdout.strip()
            if main_after != main_before:
                raise DriverError(
                    "main moved while the skill ran: the story must not touch main, so the loop stops"
                )
            spec = find_spec(story)
            status = str(read_frontmatter(spec).get("status", "")).lower() if spec else ""
            if status == "done":
                break
            if attempt < args.max_attempts and session_id:
                _log(
                    f"the spec is '{status or 'missing'}': continuing the session {session_id} "
                    f"(attempt {attempt + 1} of {args.max_attempts})"
                )
                continue
            raise DriverError(
                f"the skill left the spec of story {story.short_id} in '{status or 'missing'}' "
                f"after {attempt} attempts; read {log_dir / f'skill-{attempt}.log'} before retrying.\n"
                f"Last lines of the skill log:\n{log_tail(log_dir / f'skill-{attempt}.log')}"
            )

    meta = read_frontmatter(spec)
    spec_text = spec.read_text(encoding="utf-8")
    gate = evaluate_merge_gate(meta, spec_text)
    if not gate.passed:
        raise DriverError(f"story {story.short_id} is not mergeable: " + "; ".join(gate.reasons))
    if not spec_belongs_to_story(spec, story):
        raise DriverError(
            f"the spec {spec.name} does not belong to story {story.short_id} or is not committed "
            "on the branch"
        )

    if not is_base_ancestor():
        rebase_onto_main()

    verifications = run_verifications(
        log_dir / "verification", timeout_minutes=args.verification_timeout_minutes
    )
    failed = [command.label for command, result in verifications if not result.ok]
    if failed:
        push_branch(story.branch)
        raise DriverError(
            f"the project verifications failed: {', '.join(failed)}. The branch {story.branch} "
            f"was pushed so the work is not lost; the logs are in {log_dir / 'verification'}."
        )

    # The sprint status changes are part of the story and go in the same pull
    # request, once the verifications have passed.
    update_and_commit_sprint_status(story)
    ensure_committed(spec)

    subject, body = branch_commit_message()
    if not is_conventional_subject(subject):
        raise DriverError(
            f"the story commit subject is not a conventional commit: {subject!r}. The repository "
            "only accepts conventional commits, so the loop stops here."
        )
    revision = git("rev-parse", "HEAD").stdout.strip()
    pull_request_body = build_pull_request_body(
        story,
        spec_text,
        meta,
        verifications,
        branch=story.branch,
        revision=revision,
        spec_path=spec.relative_to(REPO_ROOT).as_posix(),
    )
    if not read_bullet(read_section(spec_text, "Auto Run Result"), "Resumen"):
        raise DriverError(
            "the spec has no 'Resumen' bullet under '## Auto Run Result': the pull request body "
            "would be empty, so the loop stops here"
        )

    push_branch(story.branch)
    if existing_pr:
        pr_number, pr_url = existing_pr
        gh("pr", "edit", str(pr_number), "--title", subject, "--body", pull_request_body)
    else:
        pr_number, pr_url = open_pull_request(story, subject, pull_request_body)
    _log(f"pull request #{pr_number}: {pr_url}")

    if args.no_merge:
        return StoryOutcome(story=story, merged=False, reason="--no-merge", pr_number=pr_number, pr_url=pr_url)

    merge_pull_request(pr_number, subject, body)
    _log(f"merged pull request #{pr_number} into {BASE_BRANCH}")

    git("fetch", "origin", BASE_BRANCH)
    git("checkout", BASE_BRANCH)
    git("reset", "--hard", f"origin/{BASE_BRANCH}")
    delete_remote_branch(story.branch)
    if branch_exists(story.branch):
        git("branch", "-D", story.branch)

    # The merge has to be in `origin/main` and the story has to read as done,
    # otherwise the loop would pick the same story again.
    if not git("merge-base", "--is-ancestor", revision, f"origin/{BASE_BRANCH}", check=False).ok:
        raise DriverError(
            f"the merge of pull request #{pr_number} is not in origin/main yet; the loop stops here"
        )
    landed = read_sprint_status()
    if str(landed.get(story.key, "")).strip().lower() != "done":
        raise DriverError(
            f"story {story.short_id} does not read as done in the merged sprint status; "
            "the loop stops here"
        )

    return StoryOutcome(story=story, merged=True, pr_number=pr_number, pr_url=pr_url)


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------


def preflight(args: argparse.Namespace) -> None:
    required = {"git", "gh", "opencode", "dotnet", "powershell", "uv"}
    required.update(command.argv[0] for command in VERIFICATION_COMMANDS)
    for tool in sorted(required):
        if shutil.which(tool) is None:
            raise DriverError(f"'{tool}' is not on PATH")
    auth = gh("auth", "status", check=False)
    if not auth.ok:
        raise DriverError(f"gh is not authenticated:\n{auth.stdout.strip()}")
    remotes = git("remote").stdout
    if "origin" not in remotes.split():
        raise DriverError("the repository has no 'origin' remote")
    if (REPO_ROOT / ".git/rebase-merge").exists() or (REPO_ROOT / ".git/rebase-apply").exists():
        raise DriverError("the repository is in the middle of a rebase")
    # The driver must run the copy of itself that is on `main`: a story branch
    # carries the copy it was created with, which is older.
    current = git("rev-parse", "--abbrev-ref", "HEAD").stdout.strip()
    if current != BASE_BRANCH:
        raise DriverError(
            f"the driver must be launched from '{BASE_BRANCH}', not from '{current}': a story "
            "branch carries an outdated copy of the driver"
        )
    if args.model:
        models = run_command(("opencode", "models"), timeout=120)
        if not models.ok:
            raise DriverError(
                "opencode cannot start in this project:\n"
                + (models.stdout.strip() or models.stderr.strip() or "no output")
            )
        if args.model not in models.stdout:
            raise DriverError(
                f"the model '{args.model}' is not available to opencode; pick another one with "
                "--model"
            )
    require_clean_tree()


def run_self_test(log_path: Path | None = None) -> CommandResult:
    """Exercise the pure parts of the driver.

    The assertions run against synthetic fixtures, so the self test keeps
    working as the backlog advances; the repository is only used for the
    checks that need real artefacts.
    """
    failures: list[str] = []

    def check(condition: bool, description: str) -> None:
        if not condition:
            failures.append(description)

    fixture_spec = """---
status: 'done'
story_key: '7-3-fixture'
deferred:
  - summary: >-
      a deferred low finding
    evidence: |-
      it is real
    severity: low
---

## Review Triage Log

### 2026-10-28 — Review pass

- verdicts: 2 findings — high 0, medium 1, low 1, false 0, maybe-false 0
- findings:
  - `[medium]` `[patch]` something was patched
  - `[low]` `[reject]` something was rejected

## Auto Run Result

- **Resumen.** El fixture entrega algo.
- **Ficheros.** `src/Thing.cs`.
- **Revisión.** 2 hallazgos, 1 parche.
- **Riesgos residuales.** ninguno.
- **Revisión de seguimiento recomendada:** `false`. No hay riesgo.
"""

    try:
        check(
            slugify("Alta y modificación de videojuegos con metadatos completos")
            == "alta-y-modificacion-de-videojuegos-con-metadatos-completos",
            "slugify does not strip accents the way the story branches do",
        )
        check(
            slugify("Story 1.4 — Observabilidad correlacionada") == "story-1-4-observabilidad-correlacionada",
            "slugify does not collapse punctuation",
        )

        # The spec parsing, against a fixture that does not depend on the backlog.
        with tempfile.TemporaryDirectory() as temporary:
            artifacts = Path(temporary)
            spec_file = artifacts / "spec-7-3-fixture.md"
            spec_file.write_text(fixture_spec, encoding="utf-8")
            parsed = read_frontmatter(spec_file)
            check(str(parsed.get("status", "")).lower() == "done", "the frontmatter status is not parsed")
            check(bool(parsed.get("deferred")), "the deferred list is not parsed")
            check(
                str((parsed.get("deferred") or [{}])[0].get("severity", "")).lower() == "low",
                "the deferred severity is not parsed",
            )

            fixture_story = Story(epic=7, num=3, key="7-3-fixture", title="Fixture", slug="fixture")
            found = find_spec(fixture_story, artifacts)
            check(found is not None and found.name == "spec-7-3-fixture.md", "find_spec does not find the spec")
            check(find_spec(Story(epic=9, num=9, key="9-9", title="None", slug="none"), artifacts) is None,
                  "find_spec returns a spec for another story")

        result_section = read_section(fixture_spec, "Auto Run Result")
        check(bool(result_section.strip()), "the Auto Run Result section cannot be read")
        check(bool(read_bullet(result_section, "Resumen")), "the summary bullet cannot be read")
        check(bool(read_bullet(result_section, "Revisión")), "the review bullet cannot be read")
        check(
            bool(read_bullet(result_section, "Revisión de seguimiento recomendada")),
            "the follow-up bullet cannot be read",
        )
        check(
            read_bullet(result_section, "Revisión de seguimiento recomendada").startswith("`false`"),
            "the follow-up bullet does not carry its value",
        )
        check(review_pass_count(fixture_spec) == 1, "the review pass count is wrong")
        check(not deferred_high_findings(fixture_spec), "the fixture defers a high finding")
        check(
            evaluate_merge_gate(parsed, fixture_spec).passed,
            "a reviewed and clean spec should pass the gate",
        )

        fixture_story = Story(epic=7, num=3, key="7-3-fixture", title="Fixture", slug="fixture")
        passing = [
            (command, CommandResult(argv=command.argv, code=0))
            for command in VERIFICATION_COMMANDS
        ]
        body = build_pull_request_body(fixture_story, fixture_spec, parsed, passing)
        check("## Revisión de seguimiento" in body, "the pull request body drops the follow-up section")
        check("## Qué entrega" in body, "the pull request body drops the delivery section")
        check("## Verificación" in body, "the pull request body drops the verification section")
        check("## Traza" in body, "the pull request body drops the trace section")
        for command in VERIFICATION_COMMANDS:
            check(command.display in body, f"the pull request body drops {command.display}")
            check(f"{command.display}  # PASS" in body, f"the pull request body does not report {command.display}")
        check("## Diferidos" in body, "the pull request body drops the deferred findings")

        # The merge gate truth table.
        check(not evaluate_merge_gate({"status": "done"}, "").passed,
              "a spec with no review evidence should not pass the gate")
        check(not evaluate_merge_gate({"status": "blocked"}, fixture_spec).passed,
              "a blocked spec should not pass the gate")
        check(not evaluate_merge_gate({"status": "missing"}, fixture_spec).passed,
              "a spec without status should not pass the gate")
        check(
            not evaluate_merge_gate({"status": "done", "deferred": [{"severity": "high"}]}, fixture_spec).passed,
            "a deferred high severity finding should not pass the gate",
        )
        check(
            evaluate_merge_gate({"status": "done", "deferred": [{"severity": "low"}]}, fixture_spec).passed,
            "a deferred low severity finding should pass the gate",
        )
        check(
            evaluate_merge_gate({"status": "done", "deferred": [{"severity": "high (unverified)"}]}, fixture_spec).passed,
            "an unverified maybe-false finding should not block the gate",
        )
        check(
            not evaluate_merge_gate({"status": "done", "deferred": [{"summary": "no severity"}]}, fixture_spec).passed,
            "a deferred finding without severity should not pass the gate",
        )
        check(
            not evaluate_merge_gate({"status": "done", "deferred": ["plain text"]}, fixture_spec).passed,
            "a malformed deferred entry should not pass the gate",
        )
        check(
            not evaluate_merge_gate(
                {"status": "done", "review_loop_iteration": 3},
                fixture_spec,
            ).passed,
            "a story over the cycle cap should not pass the gate",
        )
        three_passes = (
            "## Review Triage Log\n\n### 2026-10-28 — Review pass\n\n"
            "### 2026-10-28 — Review pass\n\n### 2026-10-28 — Review pass\n"
        )
        check(
            evaluate_merge_gate({"status": "done"}, three_passes).passed,
            "two repair cycles should still pass the gate",
        )
        four_passes = three_passes + "\n### 2026-10-28 — Review pass\n"
        check(
            not evaluate_merge_gate({"status": "done"}, four_passes).passed,
            "three repair cycles should not pass the gate",
        )

        # The triage log backstop: it has to be the reason that blocks, not the
        # missing review evidence.
        deferral_log = (
            "## Review Triage Log\n\n### 2026-10-28 — Review pass\n\n"
            "- verdicts: 1 findings — high 1, medium 0, low 0, false 0, maybe-false 0\n"
            "- findings:\n"
            "  - `[high]` `[defer]` a high finding was deferred\n"
        )
        rows = deferred_high_findings(deferral_log)
        check(len(rows) == 1, "the deferred high finding in the triage log is not detected")
        reasons = evaluate_merge_gate({"status": "done"}, deferral_log).reasons
        check(
            any("triage log defers a high finding" in reason for reason in reasons),
            "the gate does not block on a high finding deferred in the triage log",
        )

        # The squashed commit message.
        messages = parse_commit_messages("abc123\x1ffeat: the subject\n\nthe body.\n\x1e")
        check(messages == ["feat: the subject\n\nthe body."], "parse_commit_messages lost a commit message")

        # The skill event stream: the session id and a readable transcript.
        with tempfile.TemporaryDirectory() as temporary:
            events = Path(temporary) / "skill.jsonl"
            events.write_text(
                '{"type":"step_start","sessionID":"ses_fixture","part":{}}\n'
                '{"type":"text","sessionID":"ses_fixture","part":{"text":"hola"}}\n'
                '{"type":"tool","sessionID":"ses_fixture","part":{"tool":"bash",'
                '"state":{"status":"completed","title":"dotnet build"}}}\n'
                "not json\n",
                encoding="utf-8",
            )
            session_id, transcript = parse_skill_output(events)
            check(session_id == "ses_fixture", "the session id is not read from the event stream")
            check("hola" in transcript, "the transcript does not carry the text of the run")
            check("bash" in transcript, "the transcript does not carry the tool calls")
            check(parse_skill_output(Path(temporary) / "missing.jsonl") == ("", ""),
                  "a missing event stream does not degrade quietly")
        subject, commit_body = select_commit_message(
            ["feat: the subject\n\nthe body.", "chore(sprint): mark story 7.3 as done"]
        )
        check(subject == "feat: the subject", "the squash subject is not the oldest commit's subject")
        check("the body." in commit_body, "the squash body loses the story commit body")
        check("chore(sprint)" not in commit_body, "the squash body carries the driver's own chore commit")
        try:
            select_commit_message(["chore(spec): record spec-7-3.md"])
            check(False, "a branch with only bookkeeping commits should not produce a squash subject")
        except DriverError:
            check(True, "a branch with only bookkeeping commits should not produce a squash subject")
        check(is_conventional_subject("feat: the subject"), "a conventional subject is rejected")
        check(not is_conventional_subject("wip"), "a non conventional subject is accepted")
        check(not is_conventional_subject("Arreglando cosas"), "a Spanish subject is accepted")

        # The sprint status rewrite, against a synthetic status file.
        with tempfile.TemporaryDirectory() as temporary:
            copy = Path(temporary) / "sprint-status.yaml"
            crlf = "development_status:\r\n  epic-7: backlog\r\n  7-1-first: done\r\n  7-2-second: done\r\n  7-3-third: backlog\r\n"
            copy.write_bytes(crlf.encode("utf-8"))
            target = Story(epic=7, num=3, key="7-3-third", title="Third", slug="third")
            update_sprint_status(target, copy)
            updated = read_sprint_status(copy)
            check(str(updated.get(target.key, "")).lower() == "done", "the story was not marked as done")
            check(
                str(updated.get("epic-7", "")).lower() == "done",
                "the last story of an epic should close the epic",
            )
            data = copy.read_bytes()
            bare_lf = data.count(b"\n") - data.count(b"\r\n")
            check(bare_lf == 0, "the sprint status rewrite introduced mixed line endings")

            copy.write_bytes(
                (
                    "development_status:\r\n  epic-7: backlog\r\n  7-1-first: done\r\n"
                    "  7-2-second: backlog\r\n  7-3-third: backlog\r\n"
                ).encode("utf-8")
            )
            update_sprint_status(target, copy)
            updated = read_sprint_status(copy)
            check(
                str(updated.get("epic-7", "")).lower() == "in-progress",
                "an epic with pending stories should stay in progress",
            )

        # The command runner, with and without a log file.
        with tempfile.TemporaryDirectory() as temporary:
            logs = Path(temporary)
            quiet = run_command((sys.executable, "-c", "print('hello')"))
            check(quiet.ok, "run_command does not report a successful command")
            check("hello" in quiet.stdout, "run_command does not capture the output")

            logged = run_command((sys.executable, "-c", "print('logged')"), log_path=logs / "out.log")
            check(logged.ok, "run_command does not report a successful command with a log")
            check(logged.log is not None and logged.log.exists(), "run_command does not write the log file")
            check(
                logged.log is not None and "logged" in logged.log.read_text(encoding="utf-8"),
                "run_command does not stream the output to the log file",
            )

            failed = run_command((sys.executable, "-c", "raise SystemExit(3)"), log_path=logs / "fail.log")
            check(not failed.ok and failed.code == 3, "run_command does not report the exit code")

            timed_out = run_command(
                (sys.executable, "-c", "import time; time.sleep(30)"),
                log_path=logs / "slow.log",
                timeout=1,
            )
            check(not timed_out.ok and timed_out.code == 124, "run_command does not time out")

            stalled = run_command_watched(
                (sys.executable, "-c", "import time; time.sleep(60)"),
                log_path=logs / "stalled.log",
                timeout=120,
                stall_timeout=1,
            )
            check(
                not stalled.ok and stalled.code == 125,
                "run_command_watched does not kill a command that stops producing output",
            )

        # The real repository, for the checks that need real artefacts.
        try:
            status = read_sprint_status()
            for story in pending_stories(status):
                check(
                    str(status.get(story.key, "")).strip().lower() != "done",
                    f"pending_stories returned {story.key}, which is already done",
                )
                check(
                    story.branch.startswith(f"{BRANCH_PREFIX}/{story.epic}-{story.num}-"),
                    f"pending_stories produced a branch outside the convention: {story.branch}",
                )
        except DriverError as error:
            _log(f"warning: the backlog is not consistent: {error}")
    except DriverError as error:
        failures.append(f"the self test could not run: {error}")
    except Exception as error:  # noqa: BLE001 - the self test must report, not crash
        failures.append(f"the self test raised {type(error).__name__}: {error}")

    lines = [f"FAIL: {failure}" for failure in failures]
    if lines:
        lines.append(f"the self test found {len(failures)} failures")
    else:
        lines.append("the self test passed")

    if log_path is None:
        for line in lines:
            _log(line)
        return CommandResult(argv=("self-test",), code=1 if failures else 0, stdout="\n".join(lines))

    log_path.parent.mkdir(parents=True, exist_ok=True)
    log_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return CommandResult(argv=("self-test",), code=1 if failures else 0, log=log_path, stdout="\n".join(lines))


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Run the unattended bmad-build-auto story loop.")
    parser.add_argument(
        "--max-stories",
        type=int,
        default=0,
        help="how many stories to process; 0 means the whole backlog",
    )
    parser.add_argument("--story", help="process one story, given as N.M")
    parser.add_argument(
        "--timeout-minutes",
        type=float,
        default=240.0,
        help="how long the skill may run for one story",
    )
    parser.add_argument(
        "--verification-timeout-minutes",
        type=float,
        default=DEFAULT_VERIFICATION_TIMEOUT_MINUTES,
        help="how long each verification command may run",
    )
    parser.add_argument(
        "--model",
        default=DEFAULT_MODEL,
        help="the model the skill runs on; the default one has to be reachable without a refresh",
    )
    parser.add_argument("--agent", default="", help="the opencode agent the skill runs on")
    parser.add_argument(
        "--max-attempts",
        type=int,
        default=8,
        help="how many times the skill may run for one story, continuing the same session",
    )
    parser.add_argument(
        "--stall-timeout-minutes",
        type=float,
        default=DEFAULT_STALL_TIMEOUT_MINUTES,
        help="how long a run may go without producing output before it is killed",
    )
    parser.add_argument(
        "--no-merge",
        action="store_true",
        help="open the pull request but stop before merging it",
    )
    parser.add_argument("--dry-run", action="store_true", help="print the plan and change nothing")
    parser.add_argument("--self-test", action="store_true", help="run the driver self test and exit")
    parser.add_argument("--log-dir", default=str(DEFAULT_LOG_DIR), help="where the run logs go")
    args = parser.parse_args(argv)

    if args.max_stories < 0:
        parser.error("--max-stories must be zero or greater")
    if args.timeout_minutes <= 0:
        parser.error("--timeout-minutes must be greater than zero")
    if args.verification_timeout_minutes <= 0:
        parser.error("--verification-timeout-minutes must be greater than zero")
    if args.max_attempts < 1:
        parser.error("--max-attempts must be at least one")
    if args.stall_timeout_minutes <= 0:
        parser.error("--stall-timeout-minutes must be greater than zero")
    if args.story and not re.fullmatch(r"\d+\.\d+", args.story):
        parser.error("--story must be given as N.M")
    return args


def main(argv: Sequence[str] | None = None) -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    args = parse_args(argv)
    log_root = Path(args.log_dir)
    if not log_root.is_absolute():
        log_root = REPO_ROOT / log_root
    started_at = datetime.now()

    if args.self_test:
        return run_self_test().code

    if args.dry_run:
        try:
            status = read_sprint_status()
            planned = pending_stories(status)
            if args.story:
                epic, num = (int(part) for part in args.story.split(".", 1))
                planned = [story_by_id(epic, num, status)]
                first = select_next_story(status)
                if first is not None and first.label != planned[0].label:
                    _log(
                        f"warning: story {planned[0].short_id} is not the next pending story "
                        f"({first.short_id} is): the driver would refuse to run it"
                    )
            limit = args.max_stories if args.max_stories > 0 else len(planned)
            _log(f"the working tree is {'clean' if not git('status', '--porcelain').stdout.strip() else 'dirty'}")
            if not planned:
                _log("no story is pending: the executable backlog is empty")
                return 0
            _log(f"the plan covers {min(limit, len(planned))} of {len(planned)} pending stories")
            for item in planned[:limit]:
                _log(f"would run story {item.short_id} — {item.title}")
                _log(f"  branch: {item.branch}")
                _log(f"  intent: {build_intent(item)}")
            return 0
        except DriverError as error:
            _log(f"STOP: {error}")
            return 2

    try:
        preflight(args)
    except DriverError as error:
        _log(f"STOP: {error}")
        return 2

    processed = 0
    while args.max_stories == 0 or processed < args.max_stories:
        try:
            if args.story:
                epic, num = (int(part) for part in args.story.split(".", 1))
                status = read_sprint_status()
                story = story_by_id(epic, num, status)
                state = str(status.get(story.key, "")).strip().lower()
                if state == "done":
                    raise DriverError(f"story {story.short_id} is already done")
                first = select_next_story(status)
                if first is None or first.label != story.label:
                    following = f"the next pending story is {first.short_id}" if first else "none is"
                    raise DriverError(
                        f"story {story.short_id} is not the next pending story ({following}): "
                        "the driver never skips a story"
                    )
            else:
                story = select_next_story()
                if story is None:
                    _log("the executable backlog is empty: the loop is finished")
                    break
        except DriverError as error:
            _log(f"STOP: {error}")
            return 2

        story_log_dir = log_root / started_at.strftime("%Y%m%d-%H%M%S") / story.label
        _open_driver_log(story_log_dir / "driver.log")
        try:
            outcome = process_story(story, args, story_log_dir)
        except DriverError as error:
            _log(f"STOP: {error}")
            preserve_pending_work(story)
            _log(f"the story is left on its branch; logs: {story_log_dir}")
            return 1
        except Exception as error:  # noqa: BLE001 - the loop must report, not crash
            _log(f"STOP: unexpected {type(error).__name__}: {error}")
            preserve_pending_work(story)
            _log(f"the story is left on its branch; logs: {story_log_dir}")
            return 1

        processed += 1
        if outcome.merged:
            _log(f"story {story.short_id} merged into {BASE_BRANCH} as pull request #{outcome.pr_number}")
        else:
            _log(f"story {story.short_id} is ready in pull request #{outcome.pr_number}; stopping as asked")
            break
        if args.story:
            break

    _log(f"the loop processed {processed} stories in {(datetime.now() - started_at).total_seconds() / 60:.1f} minutes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
