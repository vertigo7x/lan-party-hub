---
title: 'Build Lite paralelo para historias independientes'
type: 'feature'
created: '2026-11-04'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '5a394657229e0e20c9abbc762bdebb7387f78460'
context:
  - 'docs/unattended-loop.md'
  - '_bmad-output/specs/spec-build-lite-parallel/SPEC.md'
  - '_bmad-output/specs/spec-build-lite-parallel/execution-model.md'
---

<frozen-after-approval reason="human-owned intent Ã¢â‚¬â€ do not modify unless human renegotiates">

## Intent

**Problem:** `tools/build-lite.py` construye todo el backlog de forma estrictamente secuencial. Las historias que no se afectan entre sÃƒÂ­ desperdician tiempo esperando aun cuando hay capacidad para ejecutar hasta cuatro agentes de desarrollo.

**Approach:** El driver planificarÃƒÂ¡ una ventana deslizante de hasta cuatro historias cuya independencia pueda demostrarse, ejecutarÃƒÂ¡ cada worker en un worktree aislado y conservarÃƒÂ¡ rebase, verificaciÃƒÂ³n final, PR y squash merge como una cola serial contra `origin/main`.

## Boundaries & Constraints

**Always:** Mantener el presupuesto por historia, las verificaciones deterministas, la polÃƒÂ­tica de PR/squash y la prohibiciÃƒÂ³n de push directo a `main`. Los planes declararÃƒÂ¡n `reads`, `writes`, `shared_surfaces` y `depends_on`. Historias de contrato compartido, persistencia, red, instalaciÃƒÂ³n, seguridad, firma o con plan invÃƒÂ¡lido se ejecutan en exclusiva. Los worktrees temporales estarÃƒÂ¡n bajo `.build-lite/worktrees/`; los fallidos se preservan.

**Never:** Inferir independencia solo por rutas diferentes, ejecutar mÃƒÂ¡s de cuatro workers, compartir el checkout canÃƒÂ³nico, fusionar en paralelo, eliminar el worktree de una historia fallida ni alterar la semÃƒÂ¡ntica de recuperaciÃƒÂ³n de una historia existente.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Plan elegible | Metadatos vÃƒÂ¡lidos, sin intersecciones ni dependencias pendientes | Entra en la ventana paralela con worktree y rama propios | N/A |
| Plan ambiguo | Falta una clave o formato invÃƒÂ¡lido | Se ejecuta en exclusiva | Registrar razÃƒÂ³n en el resultado |
| Superficie sensible | `shared_surfaces` declara persistencia, red, instalaciÃƒÂ³n, seguridad, firma o contrato | Espera a que no haya otro worker y se ejecuta sola | Registrar razÃƒÂ³n en el resultado |
| Merge ajeno | `origin/main` avanza mientras termina un worker | Rebase y verificaciones se repiten antes de su PR/merge | Preservar rama/worktree si rebase o verificaciÃƒÂ³n falla |
| Falla worker | ImplementaciÃƒÂ³n o verificaciÃƒÂ³n falla | Otros workers ya activos pueden completar; el worktree fallido queda preservado | El driver termina con error tras recoger resultados |

</frozen-after-approval>

## Code Map

- `tools/build-lite.py` -- driver actual; `main()` itera secuencialmente y `process_story()` mezcla ejecuciÃƒÂ³n, verificaciÃƒÂ³n y publicaciÃƒÂ³n.
- `tools/build-loop.py` -- `run_command(..., cwd=...)` permite ejecutar comandos en worktrees; `git()` y `gh()` operan hoy en `REPO_ROOT` y requieren un wrapper con directorio explÃƒÂ­cito.
- `docs/unattended-loop.md` -- contrato operativo actual que declara una historia por vez; debe describir la nueva ventana y la cola serial de integraciÃƒÂ³n.
- `_bmad-output/specs/spec-build-lite-parallel/execution-model.md` -- contrato de metadatos del plan e invariantes de coordinaciÃƒÂ³n.

## Tasks & Acceptance

**Execution:**
- [x] `tools/build-lite.py` -- extraer ejecuciÃƒÂ³n aislada de publicaciÃƒÂ³n, aÃƒÂ±adir plan estructurado, clasificaciÃƒÂ³n conservadora de elegibilidad, ventana de cuatro workers y ciclo serial de integraciÃƒÂ³n -- habilita concurrencia sin relajar las puertas.
- [x] `tools/build-loop.py` -- permitir comandos Git dirigidos a un `cwd` explÃƒÂ­cito o aÃƒÂ±adir helper equivalente reutilizable -- evita que un worker opere accidentalmente sobre el checkout canÃƒÂ³nico.
- [x] `tools/build-lite.py` -- crear, conservar y limpiar worktrees bajo `.build-lite/worktrees/` segÃƒÂºn el resultado -- hace recuperable cada worker.
- [x] `tools/build-lite.py` -- ampliar `--self-test` con metadatos invÃƒÂ¡lidos, intersecciones, exclusividad, lÃƒÂ­mite de cuatro y preservaciÃƒÂ³n de fallos -- protege la polÃƒÂ­tica del scheduler.
- [x] `docs/unattended-loop.md` -- documentar planificaciÃƒÂ³n, exclusividad, recuperaciÃƒÂ³n y merge serial -- mantiene el procedimiento operativo consistente.

**Acceptance Criteria:**
- Given cinco historias con planes independientes vÃƒÂ¡lidos, when el driver procesa el backlog, then nunca inicia mÃƒÂ¡s de cuatro workers y agenda la quinta al liberarse una plaza.
- Given dos historias con escrituras o superficies compartidas, when el scheduler evalÃƒÂºa ambas, then no se ejecutan simultÃƒÂ¡neamente.
- Given una historia sensible o con metadatos incompletos, when el scheduler la evalÃƒÂºa, then la ejecuta en exclusiva y registra el motivo.
- Given que otra historia se fusiona mientras un worker termina, when se integra ese worker, then se rebasa y completa todas las verificaciones contra el `origin/main` actualizado antes de abrir o fusionar su PR.
- Given que un worker falla, when el driver finaliza, then conserva su rama, worktree y logs y no borra su evidencia.

## Implementation Notes

- Se separ? ejecuci?n aislada e integraci?n serial; los worktrees preservan fallos y los cambios reales deben respetar la huella declarada.
- Se corrigieron exclusividad, dependencias previas, conflictos lectura-escritura, recuperaci?n `--story` y publicaci?n con `cwd` del worker.

## Spec Change Log

## Review Triage Log

### 2026-11-04 Review pass

- high: corregida la exclusividad asim?trica, la resoluci?n de dependencias y el acceso del revisor al diff del worktree.
- high: corregidos l?mites de admisi?n, recuperaci?n del worktree y helpers Git dirigidos por `cwd`.

## Design Notes

La concurrencia se limita a preparar y verificar cambios aislados. La mutaciÃƒÂ³n de estado compartido (`sprint-status.yaml`, PR y `main`) continÃƒÂºa en un ÃƒÂºnico coordinador; la serializaciÃƒÂ³n en ese borde permite volver a probar una rama sobre la realidad que finalmente se fusionarÃƒÂ¡.

## Verification

**Commands:**
- `uv run tools/build-lite.py --self-test` -- expected: todos los invariantes del scheduler pasan.
- `dotnet build LANPartyHub.sln` -- expected: build correcto.
- `dotnet test LANPartyHub.sln` -- expected: suite completa verde.
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: sin CJK ni U+FFFD.
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: backlog consistente.
