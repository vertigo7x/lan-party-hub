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

1. **Plan:** un agente rápido recibe la sección puntual de la historia, el contexto compilado de la épica y rutas candidatas existentes (9.2), sin explorar el repositorio. Su primera acción escribe `.build-lite/plans/<epica>-<historia>.md` con `# Story N.M`, `## Alcance`, `## Ficheros`, tareas, pruebas, riesgos y metadatos `reads`/`writes` con rutas concretas (300 caracteres mínimo; 500 palabras máximo). `reads` debe existir; `writes` debe apuntar a un directorio existente bajo `src/`, `tests/`, `tools/`, `docs/` o `_bmad-output/`. El driver acepta el artefacto válido sin esperar al cierre de la sesión y se detiene si no aparece en 90 segundos; el límite general de la fase sigue siendo cinco minutos. Si se rechaza, registra tamaño, encabezados, metadatos y rutas incorrectas en `plan-validation` y conserva una copia en logs locales, sin iniciar implementación. El plan declara obligatoriamente `schema_changes` (booleano).
2. **Contexto:** un agente rápido escribe `.build-lite/contexts/<epica>-<historia>.md` con la evidencia acotada de la historia: requisitos y AC, decisiones aplicables, APIs o patrones a reutilizar, archivos y pruebas relevantes, comandos y exclusiones. Se limita a 700 palabras.
3. **Implementación:** MiniMax implementa ese plan, ese contexto y sus pruebas. No vuelve a leer el backlog ni a planificar.
4. **Verificación determinista:** antes de la batería, los cambios de entidad o un esquema declarado exigen una migración versionada. Después: `build-lite --self-test`, `dotnet build`, test focalizado del contrato del hub si cambió alguno de sus extremos, `dotnet test`, `check-encoding.ps1`, `check-bmad-build-auto-customization.ps1` y `check-epics.ps1`. Un fallo de preflight detiene la batería costosa y consume el mismo presupuesto de reparación dirigida, no un atajo de la puerta final.
5. **Reparación:** como máximo una, dirigida por un fallo de verificación o por un hallazgo bloqueante. Ante una prueba fallida identificable, el driver entrega su comando, la cola del log y el nombre de la prueba al reparador; este debe demostrar una prueba filtrada y verde para esa prueba o su clase antes de que el driver acepte la recuperación. Para otro fallo entrega el comando y su evidencia y exige una ejecución verde de ese comando, sin inventar una prueba. Si no hay comprobación demostrable o vuelve a fallar, el driver se detiene.
6. **Revisión focalizada:** solo existe para cambios de contratos compartidos, persistencia, red, instalación, seguridad o firma de paquetes. El paquete enumera todos los archivos cambiados y entrega únicamente los hunks sensibles; el revisor puede inspeccionar hasta tres consumidores concretos. Un paquete sensible demasiado grande se detiene sin aprobar, y un timeout del único intento de cinco minutos no se interpreta como `OK`. Una reparación confirmada tiene un único intento de quince minutos; después siempre se repite la batería completa.

Un fallo del proveedor recibe, como máximo, un reintento técnico de su misma fase. La única excepción de recuperación no consume presupuesto: si el último intento de contexto expira después de escribir un paquete recién generado y estructuralmente completo, el driver lo sella contra el hash del plan y continúa. Una salida parcial, un paquete obsoleto o cualquier otro fallo siguen deteniendo la historia. El trabajo pendiente se commitea en la rama de la historia antes de que el driver se detenga.

## Preparación del contexto

El contexto es una entrada de ejecución, no una fuente de verdad adicional: el plan conserva la huella y la historia conserva los criterios.

- **Fuentes.** La fase usa el plan, la sección puntual de la historia en el backlog, el contexto compilado de la épica cuando existe y las rutas declaradas en el plan. No relee el backlog entero.
- **Límites.** El preparador no implementa, no cambia la huella, no modifica contratos y no amplía alcance; su paquete lo declara de forma explícita.
- **Caché local.** El paquete vive en `.build-lite/contexts/`, no se versiona, no cuenta como cambio de implementación y no participa en la decisión de paralelismo. `.gitignore` lo excluye y el driver nunca lo incluye en la huella ni en el commit de la historia.
- **Reanudación.** Una historia recuperada reutiliza su paquete completo sin volver a pagar la fase solo si corresponde al hash de su plan. Si el último intento expiró después de escribir un paquete estructuralmente completo, el journal conserva el timeout y añade una entrada `complete` que explica la recuperación; entonces el paquete se sella y se reutiliza. Si el plan cambió, falta o está incompleto, se regenera antes de implementar. Si ya hay trabajo de implementación preservado, el driver verifica ese trabajo sin preparar contexto.
- **Worker.** El paquete se copia al worktree de la historia y el prompt de implementación exige leerlo junto con el plan, limita la exploración a sus rutas y pide un bloqueo demostrado cuando la evidencia no alcanza.

## Progreso y salidas

Cada fase registra su estado en `.build-lite/logs/<epica>-<historia>/phases.json`: `complete`, `timeout`, `stalled`, `tool_failure`, `verification_failure` o `escalated`. Esto permite distinguir un límite técnico de una trayectoria sin avance.

Durante implementación, un cambio funcional en el worktree, una prueba ejecutada o un bloqueo demostrado son hitos observables. Las pruebas y bloqueos se toman exclusivamente de eventos JSON de herramientas completadas: nunca del prompt, la cabecera del log, el contexto ni el texto del usuario. Para declarar un bloqueo, el worker emite `BLOQUEO:` con `Write-Output` o `echo` tras obtener su evidencia. Si no aparece ningún hito durante 15 minutos, el driver clasifica la fase como `stalled`, detiene el proceso, preserva worktree y logs, y no consume un reintento técnico ni admite otra historia. La implementación y la reparación final siguen fijadas a `minimax-coding-plan/MiniMax-M3`.

## Revisión proporcional al riesgo

Los cambios internos y sus pruebas no consumen una revisión LLM cuando las verificaciones deterministas están en verde. Para una superficie sensible, el driver genera `.build-lite/logs/<epica>-<historia>/review.diff` y pide una sola revisión sobre el riesgo identificado. El revisor puede responder `OK` o `BLOCKING:` con una evidencia verificable; no se le exige encontrar defectos.

Esto cumple `docs/plan.md` §8: los S0/S1 no se fusionan abiertos, cada hallazgo requiere evidencia y no hay revisiones completas repetidas sin cambios relevantes.

## Ejecución paralela

El driver se inicia desde el checkout canónico en `main`: antes de leer `sprint-status.yaml`, avanza ese checkout por fast-forward hasta `origin/main`. Así una fusión recién confirmada no vuelve a admitirse por un estado local obsoleto. Después puede mantener una ventana deslizante de hasta cuatro workers para historias cuya independencia esté demostrada por el bloque JSON del plan (`reads`, `writes`, `shared_surfaces`, `depends_on`). Cada worker usa una rama y un worktree bajo `.build-lite/worktrees/`.

Los cambios de contratos, persistencia, red, instalación, seguridad o firma, los planes inválidos y las huellas que se intersectan se ejecutan en exclusiva. Mientras haya un worker exclusivo activo, el coordinador no planifica candidatas: espera y recoge su resultado antes de volver a admitir trabajo. Un fallo preserva worktree, rama y logs; no borra evidencia.

## Ramas, PR y merge

Cada worker parte de `origin/main`, en `story/<epica>-<historia>-<slug>`. Al recuperar un worktree preservado, el driver lo rebasa antes de comparar su diff y decidir si reutiliza trabajo; así los cambios ya fusionados en `main` no parecen parte de la historia. El coordinador integra resultados de uno en uno: antes de abrir el PR vuelve a ejecutar las verificaciones y, si `main` avanzó durante la historia, rebasa la rama y las repite sin crear una nueva conversación de IA.

El resultado se registra en `.build-lite/results/<epica>-<historia>.json`. Si las puertas pasan, el driver actualiza `sprint-status.yaml`, hace push, abre un PR y lo fusiona con squash. El journal solo marca la historia como completa cuando el merge y la comprobación post-merge confirman que está en `origin/main`. No hay push directo a `main`.

## Cuándo se detiene

- El plan no se puede escribir o el proveedor agota su reintento técnico sin dejar el artefacto completo que la fase valida.
- La implementación o la única reparación no deja las verificaciones en verde.
- La implementación se estanca sin un hito observable durante su ventana de progreso.
- La revisión focalizada confirma un S0/S1 que no cabe en la reparación restante.
- El rebase sobre `main` tiene conflicto o las verificaciones fallan tras él.
- El push, PR, merge o la comprobación post-merge fallan.

En todos los casos, la rama y los logs quedan preservados. Para reanudar una historia concreta se usa `uv run tools/build-lite.py --story N.M`.
