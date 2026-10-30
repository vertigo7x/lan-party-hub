<!-- bmad:context -->
<!-- Verified 2026-10-26 against a91eed7. Managed by bmad-project-context; edits inside this block are replaced on refresh. Keep anything you want preserved outside the markers. -->

## lan-party-hub

Desktop platform to distribute, install and launch games over a managed LAN. Planned stack: .NET 10 + Avalonia, modular monolith. Greenfield — no solution or source code yet, only planning artifacts. Master plan and phase gates: `docs/plan.md`.

## Policy

- One branch per story, PR reviewed by the skill's four layers plus the four project verifications and merged automatically by `tools/build-loop.py`; never push straight to `main`.
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

## Modo automático (bucle de historias)

`tools/build-lite.py` es el camino predeterminado para una historia sin intervención humana: prepara un plan breve, implementa con presupuesto acotado, ejecuta verificaciones deterministas y solo revisa con IA los cambios sensibles. Abre el PR y lo fusiona con squash cuando todas las puertas pasan.

- Se invoca por historia (`uv run tools/build-lite.py --story N.M`) o para el backlog entero; una historia se termina y fusiona antes de empezar la siguiente.
- El presupuesto máximo es un plan, una implementación, una reparación dirigida y una revisión focalizada solo cuando cambia contrato, persistencia, red, instalación, seguridad o firma.
- **Puerta de fusión**: la historia anterior tiene que estar fusionada en `main` antes de empezar la siguiente. Las verificaciones de build, tests, codificación y backlog son obligatorias.
- `bmad-build-auto` y `tools/build-loop.py` permanecen como contingencia para recuperar el flujo histórico, no como predeterminado.

## Bucle desatendido (Build Lite)

`tools/build-lite.py` recorre el backlog sin atención humana: crea o retoma la rama de cada historia, ejecuta las fases con presupuesto fijo, vuelve a pasar las verificaciones, abre el PR y lo fusiona con squash.

```powershell
uv run tools/build-lite.py             # recorre el backlog entero
uv run tools/build-lite.py --dry-run   # imprime el plan y no toca nada
uv run tools/build-lite.py --self-test # comprueba el driver y sale
```

- Las puertas antes de fusionar son verificaciones en verde, una reparación dirigida como máximo y, para cambios sensibles, ningún S0/S1 confirmado abierto. El bucle se detiene en la primera que falle.
- La revisión humana previa a la fusión se sustituye por verificaciones deterministas y una revisión focalizada proporcional al riesgo. `main` sigue entrando solo por squash de un PR, nunca por push directo.
- Procedimiento, puertas, motivos de parada y cómo reanudarlo: `docs/unattended-loop.md`. El bloque `bmad:context` de este fichero se regenera en cada refresco.

## Excepción para workers de Build Lite

Cuando un prompt empieza exactamente con `BUILD_LITE_WORKER:`, es una fase ya acotada y autorizada por `tools/build-lite.py`. **No cargues ningún skill, incluido `bmad-build`**: ejecuta solo el encargo del prompt usando las herramientas de repositorio necesarias, sin crear specs, replanificar ni iniciar otro workflow. Esta excepción solo evita la recursión dentro del worker; el proceso padre conserva sus puertas de plan, verificación, PR y merge.

## Worktree canónico

Usa exclusivamente `C:\PROJECTS\lan-party-hub` para todo el trabajo, incluidos logs de Build Lite. No crees ni cambies a worktrees temporales salvo que el usuario lo solicite expresamente.
