---
title: 'Recuperación dirigida de fallos de verificación en Build Lite'
type: 'bugfix'
created: '2026-11-06'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
context:
  - 'docs/unattended-loop.md'
baseline_commit: '2cc2eae73ed25d0803b96b9ec73d45698a016bb8'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Cuando una verificación determinista falla, el reparador recibe solo etiquetas genéricas como `dotnet test`. Puede dedicar todo su presupuesto a explorar el repositorio o a investigar falsos positivos sin ejecutar una comprobación concluyente, dejando una historia bloqueada aunque el arreglo sea local.

**Approach:** Añadir una recuperación dirigida dentro del presupuesto de reparación existente. El driver entregará al reparador la evidencia concreta del comando fallido y le exigirá aislar el fallo, modificar solo lo necesario y ejecutar la comprobación afectada. Si no obtiene una comprobación verde demostrable, conservará la evidencia y detendrá la historia; nunca saltará una puerta ni admitirá la siguiente historia.

## Boundaries & Constraints

**Always:** Mantener los límites actuales de un intento de implementación y una reparación dirigida con un único reintento técnico; conservar worktree, rama, journal y logs; volver a ejecutar la batería completa antes de publicar; registrar evidencia reproducible de la verificación fallida y del objetivo de recuperación.

**Never:** Añadir un ciclo de agentes, aumentar tiempos o reintentos, convertir un fallo en éxito por texto del agente, modificar los criterios de merge, admitir otra historia tras un fallo, ni ejecutar comandos destructivos sobre procesos ajenos al worktree.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Fallo de prueba | `dotnet test` falla con nombres de pruebas | El prompt de reparación contiene el log, el comando y el objetivo de prueba identificado; el worker ejecuta primero la comprobación focalizada tras corregir | Si sigue roja o no es identificable, se conserva el log y falla la historia |
| Fallo no focalizable | Build, codificación o backlog falla | El prompt incluye el comando y el fragmento final de su log, sin inventar una prueba concreta | La reparación actúa sobre el comando fallido y la batería completa sigue siendo obligatoria |
| Reparación sin comprobación | El worker cambia archivos, pero no ejecuta la comprobación indicada | El driver no acepta la salida como recuperación demostrada | Se registra fallo/estancamiento sin consumir un presupuesto adicional |
| Reparación correcta | La comprobación focalizada pasa | El driver corre las verificaciones completas existentes | Solo continúa si todas pasan |

</frozen-after-approval>

## Code Map

- `tools/build-lite.py` — `run_verifications()` conserva los resultados y logs; `verify_or_repair()` hoy reduce el fallo a etiquetas y llama a `repair_prompt()` sin evidencia concreta. Debe construir el contrato de recuperación, vigilar la comprobación focalizada y mantener la batería final.
- `tools/build-loop.py` — define `VerificationCommand`, `CommandResult` y los comandos deterministas reutilizados por Build Lite; no debe cambiar sus presupuestos globales.
- `docs/unattended-loop.md` — contrato operativo del presupuesto, recuperación y condiciones de parada; debe explicar la nueva recuperación sin contradicción.
- `tools/build-lite.py:self_test()` — cubre invariantes del driver sin proveedor; debe simular fallo focalizable, fallo no focalizable y ausencia de comprobación dirigida.

## Tasks & Acceptance

**Execution:**
- [x] `tools/build-lite.py` -- preservar resultado, argv y fragmento seguro de los logs fallidos; derivar un objetivo focalizado solo cuando la salida de la prueba lo demuestra.
- [x] `tools/build-lite.py` -- sustituir el prompt genérico de reparación por un contrato dirigido que exija el comando/objetivo indicado, un cambio mínimo y evidencia de su ejecución; detectar la ausencia de esa evidencia sin abrir otro intento.
- [x] `tools/build-lite.py` -- mantener las verificaciones completas posteriores y el comportamiento de preservación/stop existente.
- [x] `tools/build-lite.py` -- extender el self-test determinista de la recuperación dirigida, incluidos fallback y rechazo de una reparación sin prueba.
- [x] `docs/unattended-loop.md` -- documentar el nuevo caso, sus límites y que no evita las puertas de calidad.

**Acceptance Criteria:**
- Given una salida de `dotnet test` que identifica una prueba fallida, when empieza la reparación, then el worker recibe el nombre, el comando y el fragmento de evidencia, y debe ejecutar la comprobación focalizada antes de que el driver considere la reparación demostrada.
- Given un fallo sin prueba identificable, when empieza la reparación, then el worker recibe el comando y el log relevante sin una prueba inventada, y la batería completa sigue siendo la autoridad final.
- Given una reparación que no ejecuta la comprobación dirigida, when termina la fase, then la historia queda preservada y bloqueada sin reintentos, tiempo ni admisión adicionales.
- Given una comprobación focalizada verde, when el driver revalida la historia, then todas las verificaciones deterministas existentes deben pasar antes de revisión, PR o merge.

## Implementation Notes

- Sin migraciones, mutaciones de datos compartidos ni efectos externos nuevos.
- La evidencia debe limitarse a rutas y colas de logs ya generados, evitando inyectar el log completo en el prompt.

## Verification

**Commands:**
- `uv run tools/build-lite.py --self-test` -- expected: cubre contrato de recuperación dirigido sin proveedor.
- `dotnet build LANPartyHub.sln --no-restore` -- expected: compilación correcta.
- `dotnet test LANPartyHub.sln --no-restore` -- expected: batería verde; la E2E de LAN real puede quedar omitida según su contrato.
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: sin CJK ni U+FFFD.
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: backlog y trazabilidad válidos.
