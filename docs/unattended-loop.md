# Bucle de construcción desatendido

Build Lite construye historias del backlog con un presupuesto fijo de IA. Conserva las puertas de calidad y de GitHub del proyecto, pero elimina la conversación larga de planificar, revisar cuatro veces y replanificar la misma historia.

## Responsabilidades

| Pieza | Dueño de | Qué hace |
|---|---|---|
| `tools/build-lite.py` | Historia y GitHub | Plan breve, implementación, una reparación dirigida si hace falta, revisión proporcional al riesgo, verificaciones, PR y squash merge. |
| `tools/build-loop.py` | Contingencia | Conserva el workflow histórico de `bmad-build-auto` para recuperar ejecuciones antiguas; no es el camino predeterminado. |

## Cómo se lanza

```powershell
uv run tools/build-lite.py
uv run tools/build-lite.py --story 2.2
uv run tools/build-lite.py --max-stories 3
uv run tools/build-lite.py --no-merge
uv run tools/build-lite.py --dry-run
uv run tools/build-lite.py --self-test
```

Requiere `uv`, `opencode`, `git`, `gh`, `dotnet` y `powershell` en el PATH, y una sesión autenticada de `gh` con permiso para crear PR y hacer push.

## Presupuesto por historia

Cada historia puede usar estas fases, y ninguna se convierte en un bucle:

1. **Plan:** un agente rápido escribe `.build-lite/plans/<epica>-<historia>.md` con alcance, ficheros, pruebas y riesgos. Se limita a 500 palabras.
2. **Implementación:** MiniMax implementa ese plan y sus pruebas. No vuelve a leer el backlog ni a planificar.
3. **Verificación determinista:** `build-lite --self-test`, `dotnet build`, `dotnet test`, `check-encoding.ps1` y `check-epics.ps1`.
4. **Reparación:** como máximo una, dirigida por un fallo de verificación o por un hallazgo bloqueante. Si vuelve a fallar, el driver se detiene.
5. **Revisión focalizada:** solo existe para cambios de contratos compartidos, persistencia, red, instalación, seguridad o firma de paquetes. Usa un único revisor rápido y solo puede informar un S0/S1 con evidencia concreta.

Un fallo del proveedor recibe, como máximo, un reintento técnico de su misma fase. El trabajo pendiente se commitea en la rama de la historia antes de que el driver se detenga.

## Revisión proporcional al riesgo

Los cambios internos y sus pruebas no consumen una revisión LLM cuando las verificaciones deterministas están en verde. Para una superficie sensible, el driver genera `.build-lite/logs/<epica>-<historia>/review.diff` y pide una sola revisión sobre el riesgo identificado. El revisor puede responder `OK` o `BLOCKING:` con una evidencia verificable; no se le exige encontrar defectos.

Esto cumple `docs/plan.md` §8: los S0/S1 no se fusionan abiertos, cada hallazgo requiere evidencia y no hay revisiones completas repetidas sin cambios relevantes.

## Ramas, PR y merge

El driver procesa una historia por vez, desde `origin/main`, en `story/<epica>-<historia>-<slug>`. Antes de abrir el PR vuelve a ejecutar las verificaciones. Si `main` avanzó durante la historia, rebasa la rama y repite las verificaciones sin crear una nueva conversación de IA.

El resultado se registra en `.build-lite/results/<epica>-<historia>.json`. Si las puertas pasan, el driver actualiza `sprint-status.yaml`, hace push, abre un PR y lo fusiona con squash. No hay push directo a `main`.

## Cuándo se detiene

- El plan no se puede escribir o el proveedor agota su reintento técnico.
- La implementación o la única reparación no deja las verificaciones en verde.
- La revisión focalizada confirma un S0/S1 que no cabe en la reparación restante.
- El rebase sobre `main` tiene conflicto o las verificaciones fallan tras él.
- El push, PR, merge o la comprobación post-merge fallan.

En todos los casos, la rama y los logs quedan preservados. Para reanudar una historia concreta se usa `uv run tools/build-lite.py --story N.M`.
