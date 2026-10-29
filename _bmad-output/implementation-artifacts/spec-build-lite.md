---
title: 'Build Lite: pipeline acotado para historias'
type: 'refactor'
created: '2026-10-29'
status: 'in-progress'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: '240acabf52bb95660e32f4b798364724417d3e5d'
context:
  - '{project-root}/docs/plan.md'
  - '{project-root}/AGENTS.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** El driver actual delega cada historia a `bmad-build-auto`, que vuelve a planificar, implementa, lanza cuatro revisores que pueden forzar hallazgos, y retoma sesiones repetidamente. Ese flujo ha consumido un presupuesto de tokens y tiempo desproporcionado para el avance obtenido.

**Approach:** Sustituir el camino crítico del driver por Build Lite: un plan breve, una implementación acotada, verificaciones deterministas y una revisión LLM opcional según riesgo. Mantener rama, PR, squash merge y las verificaciones del proyecto como puertas obligatorias.

## Boundaries & Constraints

**Always:** ejecutar los comandos de verificación existentes antes del PR; preservar los requisitos de rama por historia, PR y squash merge; detener la historia ante un bloqueo confirmado; registrar logs suficientes para retomar y diagnosticar; usar el contexto de la historia y no el árbol de planeamiento completo.

**Never:** modificar la Story 2.2 ni borrar el driver actual hasta haber validado Build Lite; reintroducir una revisión que obligue a inventar hallazgos; reintentar indefinidamente; hacer push directo a `main`; rebajar los checks de compilación, pruebas, codificación o backlog.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Historia interna | Plan y código sin contratos sensibles | Implementación, checks verdes y PR sin revisión LLM | Si un check falla, una reparación dirigida; si persiste, parar |
| Historia sensible | Cambia contrato, persistencia, red, instalación o seguridad | Checks verdes y una revisión rápida focalizada | Hallazgo bloqueante local: una reparación; de alcance: parar |
| Sin progreso o proveedor caído | Run agotado o stall | La rama y el log quedan preservados | Un reintento técnico máximo; después, detener |
| Base moved | `main` avanza antes del PR | Rebase y checks sobre la nueva base | Conflicto o check fallido: detener sin fusionar |

</frozen-after-approval>

## Code Map

- `tools/build-loop.py` -- driver actual: selección de historias, ramas, verificaciones, PR y merge; hoy invoca `bmad-build-auto`, exige evidencia de cuatro capas y permite sesiones repetidas.
- `docs/unattended-loop.md` -- contrato operativo y documentación del driver; describe las puertas y cómo reanudarlo.
- `_bmad/custom/bmad-build-auto.toml` -- política del flujo antiguo; debe conservarse como compatibilidad, pero Build Lite no debe depender de sus revisores forzados.
- `opencode.json` -- agentes y presupuestos disponibles; `general` y `openai-reviewer` usan el modelo Flash para tareas rápidas.

## Tasks & Acceptance

**Execution:**
- [x] `tools/build-lite.py` -- crear un driver independiente que reutilice del driver actual la lectura del backlog, gestión de ramas/PR/merge, preservación de WIP y verificaciones, pero ejecute plan, implementación, reparación y revisión focalizada con presupuestos finitos -- evitar el ciclo de `bmad-build-auto`.
- [x] `tools/build-lite.py` -- clasificar riesgo desde el diff y rutas afectadas; no invocar revisión LLM para cambios internos cubiertos y usar un único revisor rápido para contratos, persistencia, red, instalación o seguridad -- hacer la revisión proporcional al riesgo.
- [x] `tools/build-lite.py` -- incluir autotests de parsing de resultados, clasificación de riesgo, presupuestos y construcción de comandos -- proteger el comportamiento del nuevo driver sin depender de un proveedor.
- [x] `docs/unattended-loop.md` -- documentar Build Lite como camino operativo y conservar el driver anterior como contingencia explícita -- permitir operar y diagnosticar el nuevo flujo.
- [x] `AGENTS.md` -- actualizar el bloque no generado de modo automático para nombrar Build Lite y sus puertas -- evitar que futuros agentes relancen el flujo caro por defecto.

**Acceptance Criteria:**
- Given una historia pendiente, when se ejecuta `uv run tools/build-lite.py --story N.M`, then el driver crea o retoma su rama y ejecuta como máximo un plan, una implementación y una reparación dirigida antes de detenerse.
- Given un cambio que solo toca código interno y pruebas, when todas las verificaciones deterministas pasan, then el driver no invoca un revisor LLM antes de abrir y fusionar el PR.
- Given un diff que toca contratos, persistencia, red, instalación o seguridad, when las verificaciones pasan, then el driver ejecuta exactamente una revisión LLM focalizada y bloquea el merge ante un hallazgo confirmado de severidad bloqueante.
- Given un run que se queda sin progreso o falla el proveedor, when ya ocurrió un reintento técnico, then el driver preserva el WIP, deja logs y se detiene sin abrir ni fusionar un PR.
- Given Build Lite termina una historia sin bloqueos, when prepara el merge, then ejecuta `dotnet build`, `dotnet test`, `check-encoding.ps1` y `check-epics.ps1`, abre un PR y entra en `main` exclusivamente mediante squash merge.

## Implementation Notes

El driver antiguo se mantiene sin cambios para poder comparar y recuperar historias ya iniciadas. La migración de la Story 2.2 se hará después de integrar Build Lite: su rama se rebasará sobre `main` y se retomará con el nuevo driver.

Build Lite carga las utilidades maduras de `tools/build-loop.py` para backlog, ramas, PR, squash merge y estado de sprint. Mantiene separado el presupuesto: dos intentos técnicos por fase, una reparación semántica por historia y una revisión LLM solamente para rutas sensibles.

## Spec Change Log

## Review Triage Log

## Design Notes

El plan y la implementación se ejecutan como tareas separadas y con prompts mínimos. El resultado del plan se guarda dentro de `.build-lite/` por historia para que el implementador reciba solo AC, ficheros, pruebas y riesgos relevantes. La política de riesgo se decide localmente por rutas y por el diff; no se usa un LLM para decidir si hay que revisar.

## Verification

**Commands:**
- `uv run tools/build-lite.py --self-test` -- expected: pasa los casos unitarios sintéticos del driver.
- `dotnet build LANPartyHub.sln` -- expected: compilación correcta.
- `dotnet test LANPartyHub.sln` -- expected: todas las pruebas pasan.
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: sin caracteres inválidos.
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: backlog y trazabilidad válidos.
