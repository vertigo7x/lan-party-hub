# Bucle de construcción desatendido

Este documento explica cómo se construye el proyecto entero sin intervención humana por
historia: qué corre, qué decide y qué detiene el bucle.

El mecanismo tiene dos piezas con responsabilidades separadas:

| Pieza | Dueño de | Qué hace |
|---|---|---|
| `bmad-build-auto` | el código | Planifica, implementa con un subagente, revisa con cuatro capas, parchea, verifica y deja un commit en la rama. No hace push, no abre PR y no fusiona. |
| `tools/build-loop.py` | el control de versiones | Crea la rama, lanza el skill, vuelve a pasar las verificaciones, actualiza el sprint status, hace push, abre el PR y lo fusiona. |

La separación no es cosmética: deja la decisión de fusionar en un sitio que se puede revisar
y razonar, en lugar de enterrada dentro de la historia.

## Requisitos

- `uv` en el PATH (el driver se lanza con `uv run`).
- `opencode` en el PATH: es el host que ejecuta el skill.
- `gh` autenticado (`gh auth login`) con permiso de push y de crear PR.
- El SDK de .NET que fija `global.json`.
- `powershell` en el PATH: dos de los cuatro comandos de verificación lo usan.

El driver comprueba todo eso antes de tocar nada y se detiene con un mensaje claro si algo
falta.

## Cómo se lanza

```powershell
uv run tools/build-loop.py
```

Eso recorre el backlog ejecutable entero, una historia por vez, y se detiene solo cuando se
acaba el backlog o cuando una historia no supera las puertas.

Opciones:

```powershell
uv run tools/build-loop.py --dry-run              # imprime el plan y no toca nada
uv run tools/build-loop.py --story 2.1            # una sola historia
uv run tools/build-loop.py --max-stories 3        # las tres siguientes
uv run tools/build-loop.py --no-merge             # abre el PR y se para antes de fusionar
uv run tools/build-loop.py --self-test            # comprueba el driver y sale
uv run tools/build-loop.py --timeout-minutes 360  # techo de tiempo del skill por historia
uv run tools/build-loop.py --verification-timeout-minutes 30
uv run tools/build-loop.py --model minimax-coding-plan/MiniMax-M3
uv run tools/build-loop.py --log-dir .build-loop/logs
```

El driver lanza el skill con `opencode run --dangerously-skip-permissions`: sin esa bandera el
agente se detiene en cada permiso y el bucle no puede desatenderse. El modelo viene fijado
por defecto (`minimax-coding-plan/MiniMax-M3`) y se cambia con `--model`, porque el modelo
del agente por defecto no refresca su token en este entorno y una ejecución que no puede
contestar no deja spec detrás.

Los logs de cada historia quedan en `.build-loop/logs/<sello>/<epica>-<historia>/`: `driver.log`
es la traza del propio driver, `skill.log` la sesión del skill, `verification/` el resultado de
las cinco comprobaciones y `verify-*.log` el detalle de cada una.

## Qué hace el driver con cada historia

1. Sincroniza `main` y crea la rama `story/<epica>-<historia>-<slug>` desde ahí.
2. Lanza `opencode run --auto "bmad-build-auto <intent>"` dentro de la rama.
3. Comprueba que `main` no se ha movido durante el run: la historia no toca `main`.
4. Lee el `status` del frontmatter del spec que ha escrito el skill.
5. Aplica la puerta de fusión (abajo). Si no pasa, para el bucle.
6. Vuelve a pasar las verificaciones: el autotest del driver y los cuatro comandos del
   proyecto.
7. Hace push de la rama, abre el PR con el cuerpo construido a partir del `## Auto Run Result`
   del spec y lo fusiona con squash.
8. Comprueba que la fusión está en `origin/main` y que la historia se lee como `done`, vuelve a
   `main` y sigue con la historia siguiente.

El squash conserva el mensaje de commit convencional en inglés que escribió el skill, y el
cuerpo del PR queda en español, como el resto de la documentación del proyecto.

Si `main` ha avanzado mientras la historia corría, el driver rebasa la rama sobre `main` y
vuelve a pasar las verificaciones antes de continuar.

## Puertas antes de fusionar

Una historia se fusiona solo si cumple **todas**. Si alguna falla, el bucle se detiene y deja
la historia en su rama, con el spec y los logs apuntando al sitio exacto del problema.

1. **Puerta del spec.** El `status` del frontmatter tiene que ser `done`, el spec tiene que ser
   el de esa historia y estar commiteado en la rama, y tiene que registrar al menos una pasada
   de revisión: un spec sin triage log es un spec que nadie revisó, diga lo que diga su
   estado. La revisión de cuatro capas es lo que sustituye a la revisión humana, así que su
   evidencia es lo que la puerta exige.
2. **Puerta de severidad.** No puede quedar diferido un hallazgo de severidad bloqueante
   (`high`, `S0`, `S1`). La escala de `docs/plan.md` §8 no permite declarar una historia
   completa con S0/S1 abiertos. Un `high` parcheado no bloquea; un `high` diferido, sí. Un
   `high (unverified)` viene de un `maybe-false` —no se pudo confirmar el defecto— y no
   bloquea, pero se copia al cuerpo del PR. Un diferido sin severidad bloquea: no se puede
   fusionar en automático lo que nadie ha clasificado.
3. **Puerta de ciclos.** Como máximo dos ciclos automáticos de corrección y revisión por
   historia. El driver cuenta las pasadas de revisión del triage log (la primera pasada no es
   un ciclo) y el `review_loop_iteration` del spec.
4. **Puerta de verificación.** El autotest del driver y los cuatro comandos del proyecto en
   verde, ejecutados por el driver y no solo por el skill: `dotnet build`, `dotnet test`,
   `check-encoding.ps1` y `check-epics.ps1`.

`followup_review_recommended: true` **no** bloquea el merge. Es la señal de que el trabajo
converge con un riesgo residual nombrado, y ese riesgo se copia al cuerpo del PR para que
quede a la vista. Bloquear con esa señal pararía el bucle en casi todas las historias.

Además de las cuatro puertas, el driver se para si el asunto del commit no es un
conventional commit, si el spec no tiene su bullet `**Resumen.**` (el cuerpo del PR quedaría
vacío) o si `main` se movió durante el run de la historia.

## Cuándo se detiene el bucle

- La historia no supera una de las cuatro puertas.
- El skill termina bloqueado, deja el spec en un estado distinto de `done` o falla con un
  código de salida distinto de cero.
- `main` se movió durante el run de la historia.
- Un comando de verificación falla. En ese caso el driver hace push de la rama antes de
  parar, para que el trabajo no se quede solo en el disco.
- El rebase sobre `main` entra en conflicto. El driver aborta el rebase y deja la rama como
  estaba.
- El push, la creación del PR o el merge fallan.
- La fusión no aparece en `origin/main`, o la historia no se lee como `done` en el sprint
  status fusionado.
- Se agotan las historias del backlog.

Una historia que no está en `done` se considera pendiente, esté en `backlog`, `review` o
`in-progress`: el bucle termina lo que quede antes de pasar a la siguiente.

## Cómo reanudarlo

El estado vive en git y en `sprint-status.yaml`, no en la memoria del proceso: relanzar el
driver es retomarlo.

```powershell
uv run tools/build-loop.py --story 2.2   # seguir por una historia concreta
uv run tools/build-loop.py                # seguir por el backlog
```

Si una historia quedó a medias con su PR abierto, el driver lo detecta y reanuda desde el
paso de verificación y fusión, sin volver a implementar la historia. Si la rama se quedó
abandonada sin PR ni spec commiteado, la descarta y vuelve a empezar esa historia desde
`main`.

Para deshacer un rebase a medias a mano: `git rebase --abort`.

## Qué cambió respecto a la política anterior

Hasta el 2026-10-29 la política era «PR con revisión humana antes de fusionar» y el bucle se
paraba al terminar cada historia esperando a que alguien revisara y fusionara el PR.

Ahora:

- La revisión que precede a la fusión es la del propio skill (cuatro capas: blind hunter, edge
  case hunter, verification gap e intent alignment) más los cuatro comandos de verificación del
  proyecto, ejecutados de nuevo por el driver.
- El driver fusiona el PR y sigue con la historia siguiente.
- La puerta «la historia anterior tiene que estar fusionada en `main`» **sigue en pie**: es lo
  que garantiza que cada historia empieza sobre el trabajo ya aceptado. El driver la satisface
  él mismo.
- Lo que ya **no** se aplica: la revisión humana previa a cada fusión.

`main` sigue sin recibir pushes directos: solo entra por squash de un PR. La política vive en
`_bmad/custom/bmad-build-auto.toml` (capa de equipo, commiteada) y en este documento.

La overriden personal `_bmad/custom/bmad-build-auto.user.toml` **no** puede cambiar esta
política: las listas del `customize` se concatenan entre capas, así que un hecho personal se
añade al hecho del equipo en vez de sustituirlo, y el agente recibe las dos instrucciones
contradictorias. Para cambiar la política hay que editar el archivo de equipo.
