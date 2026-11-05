---
title: 'Preparación acotada de contexto para Build Lite'
type: 'feature'
created: '2026-11-05'
status: 'in-review'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'b46c96ba2ade29e02a8ce65897e8dd7eefb63782'
context:
  - 'docs/unattended-loop.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** El worker de implementación recibe el plan, pero aún debe descubrir contratos, decisiones, pruebas y límites por su cuenta. En historias transversales consume su presupuesto de implementación explorando, como ocurrió con la 6.2, en vez de producir y verificar código.

**Approach:** Build Lite incorporará una fase breve de preparación de contexto entre el plan y la implementación. Un agente rápido creará un paquete local por historia con evidencia concreta y restricciones; el worker de desarrollo recibirá exclusivamente el plan y ese paquete para comenzar a implementar de inmediato. El driver registrará hitos verificables por fase y clasificará salidas para detener una implementación que solo explora sin producir progreso.

## Boundaries & Constraints

**Always:** La preparación se ejecuta para cada historia antes de su worker; usa como fuentes el plan, la sección puntual de la historia, el contexto de épica existente cuando aplique y las rutas declaradas en el plan. Produce un documento breve, en español, con requisitos/AC, decisiones aplicables, APIs o patrones a reutilizar, archivos y pruebas relevantes, comandos y exclusiones. Debe declararse explícitamente que no puede implementar, cambiar la huella, modificar contratos ni ampliar alcance. Su artefacto se copia al worktree y el prompt de implementación exige leerlo antes de modificar código. Cada fase deja un estado estructurado (`complete`, `timeout`, `stalled`, `tool_failure`, `verification_failure` o `escalated`) y la implementación registra hitos observables: cambio funcional, prueba ejecutada o bloqueo demostrado.

**Never:** El paquete no se versiona, no cuenta como cambio de implementación, no sustituye al bloque JSON de metadatos ni participa en la decisión de paralelismo. No se vuelve a explorar el backlog entero, no se abre una nueva fase de planificación y no se relajan presupuestos ni puertas de verificación.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Historia nueva | Plan válido y fuentes disponibles | Contexto local acotado, copiado al worktree antes de implementar | Si el agente falla, un único reintento técnico de la fase |
| Historia recuperada | Contexto local completo ya existente | Se reutiliza el paquete para no gastar presupuesto otra vez | Si falta o está incompleto, se regenera antes de ejecutar |
| Contexto de épica ausente | No existe `epic-N-context.md` | El paquete usa solo el plan y las rutas declaradas | No inventa decisiones ni lee artefactos no declarados |
| Artefacto local | Contexto bajo `.build-lite/contexts/` | No aparece en el diff ni en el commit de historia | La validación de huella lo excluye |
| Implementación sin avance | Sin cambio funcional, prueba ni bloqueo demostrado durante la ventana de progreso | Finaliza como `stalled` antes del timeout global y preserva evidencia | No reintenta ni admite otra historia |

</frozen-after-approval>

## Code Map

- `tools/build-lite.py` -- define fases, prompts, rutas locales, copia de artefactos al worktree, ejecución y self-tests; es el único punto de integración del preparador.
- `docs/unattended-loop.md` -- contrato operativo para presupuesto por historia; debe declarar la fase nueva y sus límites.
- `.gitignore` -- ya excluye logs, worktrees y planes locales; debe excluir los paquetes de contexto sin ocultar `.build-lite/results/` publicados.
- `_bmad-output/implementation-artifacts/epic-*-context.md` -- contexto de épica opcional que el preparador puede usar si existe, sin requerir su creación.

## Tasks & Acceptance

**Execution:**
- [x] `tools/build-lite.py` -- añadir raíz y rutas de contexto por historia, prompt y fase `context` de presupuesto breve con un reintento técnico -- entrega evidencia mínima al worker sin cambiar su modelo de concurrencia.
- [x] `tools/build-lite.py` -- copiar el paquete al worktree y actualizar el prompt de implementación para leer plan y contexto, limitar exploración a sus rutas y reportar un bloqueo demostrado -- evita que el worker reinicie la investigación.
- [x] `tools/build-lite.py` -- reutilizar un contexto completo al reanudar y excluir `.build-lite/contexts/` de cambios funcionales -- mantiene recuperación idempotente y huella fiable.
- [x] `tools/build-lite.py` -- ampliar `--self-test` para ruta, prompt, exclusión de huella y reutilización -- protege el contrato sin depender de un proveedor.
- [x] `tools/build-lite.py` -- registrar estado estructurado e hitos de fase, y detectar ausencia de progreso durante implementación -- distingue timeout técnico de estancamiento y preserva evidencia accionable.
- [x] `.gitignore` y `docs/unattended-loop.md` -- tratar los contextos como caché local y documentar plan → contexto → implementación -- alinea árbol de Git y operación.

**Acceptance Criteria:**
- Given una historia con plan válido, when Build Lite inicia su worker, then existe un contexto local completo y el prompt exige leerlo junto con el plan.
- Given un contexto existente y completo de una historia preservada, when se reanuda, then se reutiliza sin invocar otro preparador.
- Given un paquete en `.build-lite/contexts/`, when se calcula el diff y se crea el commit de historia, then el paquete no forma parte de la huella ni del commit.
- Given un fallo del preparador, when agota su único reintento técnico, then el driver se detiene preservando el worktree y los logs, sin implementar parcialmente.
- Given una historia con contexto de épica ausente, when se prepara, then el paquete declara solo evidencia disponible y no inventa decisiones.
- Given una implementación que solo explora, when no produce un cambio funcional, prueba ejecutada o bloqueo demostrado durante la ventana de progreso, then el driver termina como `stalled`, conserva worktree y logs, y no reintenta ni admite otra historia.

## Implementation Notes

## Spec Change Log

## Review Triage Log

## Design Notes

El contexto es una entrada de ejecución, no una fuente de verdad adicional: el plan conserva la huella y la historia conserva los criterios. Separarlo reduce el contexto del agente de desarrollo, hace visible qué evidencia se seleccionó y permite reutilizarla en recuperaciones sin repetir investigación.

## Verification

**Commands:**
- `uv run tools/build-lite.py --self-test` -- esperado: cubre las invariantes nuevas del paquete de contexto.
- `uv run tools/build-loop.py --self-test` -- esperado: no se rompe la infraestructura compartida.
- `powershell -NoProfile -File tools/check-encoding.ps1` -- esperado: sin caracteres corruptos.
- `powershell -NoProfile -File tools/check-epics.ps1` -- esperado: backlog íntegro.
