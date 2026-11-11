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

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `tools/build-lite.py` construye todo el backlog de forma estrictamente secuencial. Las historias que no se afectan entre sí desperdician tiempo esperando aun cuando hay capacidad para ejecutar hasta cuatro agentes de desarrollo.

**Approach:** El driver planificará una ventana deslizante de hasta cuatro historias cuya independencia pueda demostrarse, ejecutará cada worker en un worktree aislado y conservará rebase, verificación final, PR y squash merge como una cola serial contra `origin/main`.

## Boundaries & Constraints

**Always:** Mantener el presupuesto por historia, las verificaciones deterministas, la política de PR/squash y la prohibición de push directo a `main`. Los planes declararán `reads`, `writes`, `shared_surfaces` y `depends_on`. Historias de contrato compartido, persistencia, red, instalación, seguridad, firma o con plan inválido se ejecutan en exclusiva. Los worktrees temporales estarán bajo `.build-lite/worktrees/`; los fallidos se preservan.

**Never:** Inferir independencia solo por rutas diferentes, ejecutar más de cuatro workers, compartir el checkout canónico, fusionar en paralelo, eliminar el worktree de una historia fallida ni alterar la semántica de recuperación de una historia existente.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Plan elegible | Metadatos válidos, sin intersecciones ni dependencias pendientes | Entra en la ventana paralela con worktree y rama propios | N/A |
| Plan ambiguo | Falta una clave o formato inválido | Se ejecuta en exclusiva | Registrar razón en el resultado |
| Superficie sensible | `shared_surfaces` declara persistencia, red, instalación, seguridad, firma o contrato | Espera a que no haya otro worker y se ejecuta sola | Registrar razón en el resultado |
| Merge ajeno | `origin/main` avanza mientras termina un worker | Rebase y verificaciones se repiten antes de su PR/merge | Preservar rama/worktree si rebase o verificación falla |
| Falla worker | Implementación o verificación falla | Otros workers ya activos pueden completar; el worktree fallido queda preservado | El driver termina con error tras recoger resultados |

</frozen-after-approval>

## Code Map

- `tools/build-lite.py` -- driver actual; `main()` itera secuencialmente y `process_story()` mezcla ejecución, verificación y publicación.
- `tools/build-loop.py` -- `run_command(..., cwd=...)` permite ejecutar comandos en worktrees; `git()` y `gh()` operan hoy en `REPO_ROOT` y requieren un wrapper con directorio explícito.
- `docs/unattended-loop.md` -- contrato operativo actual que declara una historia por vez; debe describir la nueva ventana y la cola serial de integración.
- `_bmad-output/specs/spec-build-lite-parallel/execution-model.md` -- contrato de metadatos del plan e invariantes de coordinación.

## Tasks & Acceptance

**Execution:**
- [x] `tools/build-lite.py` -- extraer ejecución aislada de publicación, añadir plan estructurado, clasificación conservadora de elegibilidad, ventana de cuatro workers y ciclo serial de integración -- habilita concurrencia sin relajar las puertas.
- [x] `tools/build-loop.py` -- permitir comandos Git dirigidos a un `cwd` explícito o añadir helper equivalente reutilizable -- evita que un worker opere accidentalmente sobre el checkout canónico.
- [x] `tools/build-lite.py` -- crear, conservar y limpiar worktrees bajo `.build-lite/worktrees/` según el resultado -- hace recuperable cada worker.
- [x] `tools/build-lite.py` -- ampliar `--self-test` con metadatos inválidos, intersecciones, exclusividad, límite de cuatro y preservación de fallos -- protege la política del scheduler.
- [x] `docs/unattended-loop.md` -- documentar planificación, exclusividad, recuperación y merge serial -- mantiene el procedimiento operativo consistente.

**Acceptance Criteria:**
- Given cinco historias con planes independientes válidos, when el driver procesa el backlog, then nunca inicia más de cuatro workers y agenda la quinta al liberarse una plaza.
- Given dos historias con escrituras o superficies compartidas, when el scheduler evalúa ambas, then no se ejecutan simultáneamente.
- Given una historia sensible o con metadatos incompletos, when el scheduler la evalúa, then la ejecuta en exclusiva y registra el motivo.
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

La concurrencia se limita a preparar y verificar cambios aislados. La mutación de estado compartido (`sprint-status.yaml`, PR y `main`) continúa en un único coordinador; la serialización en ese borde permite volver a probar una rama sobre la realidad que finalmente se fusionará.

## Verification

**Commands:**
- `uv run tools/build-lite.py --self-test` -- expected: todos los invariantes del scheduler pasan.
- `dotnet build LANPartyHub.sln` -- expected: build correcto.
- `dotnet test LANPartyHub.sln` -- expected: suite completa verde.
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: sin CJK ni U+FFFD.
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: backlog consistente.
