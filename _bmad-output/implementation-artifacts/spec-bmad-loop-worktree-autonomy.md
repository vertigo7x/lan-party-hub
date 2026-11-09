---
title: 'Evitar bloqueos por artefactos propios de bmad-loop'
type: 'bugfix'
created: '2026-11-09'
status: 'done'
route: 'dispatch'
review_loop_iteration: 0
baseline_commit: 'e0564d40824c024a636a9d5359d1584eed1a09ac'
context:
  - 'AGENTS.md'
  - 'docs/unattended-loop.md'
  - '_bmad/custom/bmad-build-auto.toml'
  - 'tools/build-loop.py'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Una ejecución aislada de `bmad-loop` puede crear el contexto de la épica antes de la comprobación de limpieza de `bmad-build-auto`. La misma comprobación trata ese artefacto propio como trabajo ajeno, marca la story como `blocked` y pausa todo el run, aunque el worktree se creó limpio por el orquestador.

**Approach:** Ajustar la política del workflow automático para que la puerta inicial siga protegiendo contra cambios preexistentes o ajenos, pero permita únicamente los artefactos de planificación que el propio workflow acaba de generar en el worktree de su story. Conservar la obligación de terminar con los cambios revisados y commiteados antes de que el orquestador integre la rama.

## Boundaries & Constraints

**Always:** Mantener aislamiento por worktree y rama por story; detectar suciedad preexistente al inicio; no hacer push, PR ni merge desde el agente; conservar el bloqueo para cambios fuera de los artefactos autorizados; y dejar evidencia explícita de cualquier excepción aplicada.

**Never:** Ignorar indiscriminadamente `git status`; borrar o resetear trabajo existente; modificar `sprint-status.yaml`, la lógica del motor bmad-loop instalada fuera del repositorio o los artefactos de una story distinta; ni reducir las verificaciones de build, tests, codificación o backlog.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Worktree nuevo | Árbol limpio al activar; el workflow crea `epic-N-context.md` | La comprobación posterior reconoce el archivo como artefacto propio y continúa | El artefacto queda incluido en el commit final si corresponde |
| Suciedad ajena | Archivo modificado o sin seguimiento antes de activar | La story se bloquea antes de planificar | Reporta `dirty working tree` con las rutas |
| Suciedad mixta | Artefacto propio más una ruta ajena | La story se bloquea | Reporta las rutas no autorizadas; no descarta ninguna |
| Reanudación | Worktree preservado de un intento anterior | La excepción no oculta cambios de implementación pendientes | Se reanuda solo por el mecanismo del orquestador o se bloquea con evidencia |

</frozen-after-approval>

## Code Map

- `_bmad/custom/bmad-build-auto.toml` -- personalización versionada que inyecta las reglas de activación y los hechos persistentes del workflow automático.
- `.agents/skills/bmad-build-auto/step-01-clarify-and-route.md` -- plantilla instalada que realiza una segunda comprobación de árbol limpio después de compilar el contexto; no se editará directamente porque se sobrescribe en actualizaciones.
- `.bmad-loop/runs/20261109-075854-282f/journal.jsonl` -- evidencia de que el run creó un worktree limpio y se pausó por `dirty working tree`.
- `.bmad-loop/runs/20261109-075854-282f/logs/9-1-tabla-densa-de-clientes-conectados-dev-1.log` -- evidencia del orden: compilación de `epic-9-context.md`, segunda comprobación y bloqueo.
- `.bmad-loop/policy.toml` -- confirma aislamiento `worktree`, una rama por story y conservación de worktrees fallidos.
- `tools/check-bmad-build-auto-customization.ps1` -- renderiza el workflow efectivo y confirma que la regla autónoma llega a la sesión.
- `tools/check-encoding.ps1` -- guarda de codificación ampliada para cubrir la personalización TOML que consume el workflow.
- `tools/build-loop.py` -- registra la nueva guarda entre las verificaciones deterministas del driver y de Build Lite.

## Tasks & Acceptance

**Execution:**
- [x] `_bmad/custom/bmad-build-auto.toml` -- redefinir la regla de control de suciedad para distinguir una línea base limpia de los únicos artefactos de planificación creados durante el mismo workflow -- evita que una instrucción genérica contradiga el orden real de `bmad-build-auto`.
- [x] `_bmad/custom/bmad-build-auto.toml` -- exigir que la excepción compare rutas exactas, no admita cambios rastreados ni archivos fuera de `epic-<N>-context.md` y la spec activa, y mantenga el bloqueo ante cualquier mezcla -- preserva la protección frente a trabajo humano o de otra story.
- [x] `tools/check-bmad-build-auto-customization.ps1` -- renderizar el workflow efectivo y afirmar que conserva la regla de VCS, las rutas exactas y la rama de bmad-loop -- detecta una actualización de BMAD que deje inerte la personalización.
- [x] `tools/check-encoding.ps1` -- incluir ficheros TOML entre las entradas examinadas -- protege el texto de política que consume el agente.
- [x] `tools/build-loop.py` -- ejecutar la nueva guarda con las verificaciones deterministas del driver y Build Lite -- impide integrar una historia si la personalización no llega al workflow.
- [x] `_bmad-output/implementation-artifacts/spec-bmad-loop-worktree-autonomy.md` -- registrar en notas de implementación la evidencia del run y el resultado de una renderización del skill que compruebe que la instrucción personalizada está presente -- deja trazabilidad reproducible.
- [x] `tools/check-encoding.ps1` y `tools/check-epics.ps1` -- ejecutar las guardas documentales obligatorias -- confirma que el cambio de política no corrompe los artefactos del repositorio.

**Acceptance Criteria:**
- Given un worktree de bmad-loop limpio al inicio, when `bmad-build-auto` genera el contexto de la épica antes de su segunda puerta de VCS, then la story no termina con `blocked: dirty working tree` solo por ese contexto y por su spec activa.
- Given una ruta modificada o sin seguimiento que no es un artefacto generado y autorizado por el workflow de esa story, when se ejecuta la puerta de VCS, then la story termina bloqueada e identifica la suciedad.
- Given artefactos autorizados junto a cualquier cambio ajeno, when se ejecuta la puerta de VCS, then no se concede la excepción y no se borra ni resetea ningún archivo.
- Given la personalización actualizada, when se vuelve a renderizar `bmad-build-auto`, then la regla autónoma aparece en el workflow renderizado sin editar los archivos instalados bajo `.agents/skills`.
- Given una actualización de BMAD que elimine los puntos de inyección usados por la personalización, when se ejecutan las verificaciones del driver, then la guarda de personalización falla antes de integrar una historia.

## Implementation Notes

- Confirmado en `.bmad-loop/runs/20261109-075854-282f`: el worktree se abrió desde `e0564d4` limpio; `bmad-build-auto` compiló `epic-9-context.md` y su segunda puerta de VCS se bloqueó por ese único archivo. El esqueleto de spec se creó después, como resultado del bloqueo.
- La personalización mantiene la comprobación de VCS y reemplaza únicamente el HALT posterior genérico para los dos artefactos identificados de la story actual. No admite conflictos, rutas adicionales, specs ajenas ni cambios no asociados.
- Se renderizó `bmad-build-auto` con `render_skill.py`; el workflow generado contiene tanto la regla de línea base como la excepción limitada. No se modificó `.agents/skills/bmad-build-auto`, porque sus archivos se reemplazan al actualizar BMAD.
- La revisión corrigió dos casos adicionales: contextos regenerados que ya están versionados y worktrees de bmad-loop con ramas `bmad-loop/<run_id>/<story_key>`. La regla ahora permite solo las dos rutas explícitas asociadas a la épica y story actuales, tanto en un primer intento como al recuperar un worktree conservado.
- `tools/check-bmad-build-auto-customization.ps1` renderiza y aserta los cuatro fragmentos críticos del workflow. Se añadió a `VERIFICATION_COMMANDS`; `tools/check-encoding.ps1` ahora cubre Markdown y TOML.
- `tools/check-encoding.ps1`, `tools/check-epics.ps1` y `uv run tools/build-loop.py --self-test` finalizaron correctamente. `bmad-loop validate` se ejecutó antes del commit y falló porque esta implementación aún estaba sin confirmar; también informó que `pwsh` no estaba disponible en PATH para Psmux, un problema de entorno independiente.

## Spec Change Log

## Review Triage Log

- `[bad_spec]` La primera versión admitía solo contextos sin seguimiento y no reconocía la rama de bmad-loop; se amplió la regla a las rutas exactas de contexto/spec de la story y a ambos patrones de rama, evitando repetir el bloqueo en worktrees recuperados.
- `[patch]` No existía una verificación que demostrase que la personalización alcanzaba el workflow renderizado; se añadió una guarda determinista y se incorporó al conjunto de verificaciones.
- `[patch]` La guarda de codificación no analizaba TOML; ahora analiza Markdown y TOML bajo las rutas configuradas.

## Design Notes

La fuente editable y versionada es la configuración de personalización. La plantilla instalada contiene la comprobación que entra en conflicto y declara que se sobrescribe en actualizaciones; editarla arreglaría un único checkout y perdería el arreglo al actualizar BMAD. La personalización debe expresar una excepción estrecha y verificable, basada en una línea base limpia al inicio de la sesión y en una lista positiva de salidas generadas por el flujo.

## Verification

**Commands:**
- `uv run --no-cache _bmad/scripts/render_skill.py --project-root C:\PROJECTS\lan-party-hub --skill C:\PROJECTS\lan-party-hub\.agents\skills\bmad-build-auto` -- expected: genera un workflow que incluye la política autónoma.
- `powershell -NoProfile -File tools/check-bmad-build-auto-customization.ps1` -- expected: confirma que el workflow renderizado conserva la regla autónoma.
- `uv run tools/build-loop.py --self-test` -- expected: las verificaciones del driver, incluida la nueva guarda, siguen siendo válidas.
- `powershell -NoProfile -File tools/check-encoding.ps1` -- expected: sin caracteres no permitidos ni U+FFFD.
- `powershell -NoProfile -File tools/check-epics.ps1` -- expected: backlog y trazabilidad válidos.

## Auto Run Result

Status: done

Resultado: la personalización permite los artefactos propios y las ramas aisladas de bmad-loop, y una guarda determinista verifica que la regla llega al workflow renderizado.
