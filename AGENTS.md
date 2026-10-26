<!-- bmad:context -->
<!-- Verified 2026-10-26 against a91eed7. Managed by bmad-project-context; edits inside this block are replaced on refresh. Keep anything you want preserved outside the markers. -->

## lan-party-hub

Desktop platform to distribute, install and launch games over a managed LAN. Planned stack: .NET 10 + Avalonia, modular monolith. Greenfield — no solution or source code yet, only planning artifacts. Master plan and phase gates: `docs/plan.md`.

## Policy

- One branch per story, PR with review before merging; never push straight to `main`.
- Never add client authentication or authorization: the LAN is trusted by design, the only client gating is version compatibility (FR-4, AD-6). Package trust is a separate concern — signatures, not sessions.
- Cap automated fix/review cycles at two per story, then escalate; never reopen a closed finding without new evidence (`docs/plan.md` §8).
- Do not change a shared contract without checking its consumers first (`docs/plan.md`).

## Where things are

- Planning artifacts: `_bmad-output/planning-artifacts/{briefs,prds,ux-designs,architecture,epics}/`; each run folder keeps an append-only `.memlog.md` — append with `uv run _bmad/scripts/memlog.py`, never hand-edit.
- Decisions and spikes: `architecture/.../ARCHITECTURE-SPINE.md` (AD-1..AD-32, S-1..S-5) — read the AD before touching a module, contract or entity.
- Executable backlog with acceptance criteria: `epics/.../epics.md`; requirements in `prds/.../prd.md`.

## Running and verifying

- `python` is not on PATH: use `py -3` (3.10.11) or `uv run` for scripts, e.g. `uv run _bmad/scripts/memlog.py`.
- Target `net10.0`: SDK 10.0.403 is installed. Pin it in `global.json` at the repo root — without that file `dotnet` resolves to whichever SDK is newest, and that set has changed twice on this machine.
- Avalonia 12.1.4 ships `net10.0` assets natively plus `net8.0`; it has no `net9.0` assets, so net9.0 falls back to net8.0.
- Git reports LF→CRLF on every Markdown edit (`core.autocrlf=true`): informational, not an error.
- Check Markdown encoding before committing: `powershell -NoProfile -File tools/check-encoding.ps1` — fails on stray CJK or U+FFFD.
- Check the epic backlog before committing: `powershell -NoProfile -File tools/check-epics.ps1` — fails on count/parity gaps, story-ID gaps, out-of-range IDs or AR traceability drift.

## Conventions that differ from defaults

- Planning documents and explanations in Spanish; identifiers, contracts and AD/AC codes in English.

## Known pitfalls

- Stray CJK has repeatedly been injected into Spanish text by editors, and a review once reported "CJK: PASS - 0" on a corrupted file. `tools/check-encoding.ps1` is the guard; fix what it reports instead of editing around it.
- `epics/.../reviews/` cite a `verify_epics.py` script that does not exist in this repo; the real guard is `tools/check-epics.ps1` — use the counts recorded in each review.

<!-- /bmad:context -->
