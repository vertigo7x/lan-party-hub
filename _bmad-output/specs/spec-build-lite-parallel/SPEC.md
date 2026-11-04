---
id: SPEC-build-lite-parallel
companions:
  - execution-model.md
sources:
  - ../../forge/build-lite-parallel-backlog/forged-idea.md
---

# Build Lite paralelo

## Why

El backlog secuencial prolonga innecesariamente el tiempo total de entrega. El driver debe aprovechar historias independientes sin rebajar las puertas de calidad ni la entrada a `main` exclusivamente por PR con squash.

## Capabilities

- **CAP-1**
  - **intent:** El driver programa hasta cuatro historias independientes para reducir el tiempo total del backlog.
  - **success:** Nunca hay más de cuatro workers de implementación activos y una historia terminada habilita otra elegible.
- **CAP-2**
  - **intent:** El driver decide la independencia desde un plan estructurado de cada historia.
  - **success:** Los planes sin `reads`, `writes`, `shared_surfaces` y `depends_on` válidos se ejecutan en exclusiva.
- **CAP-3**
  - **intent:** Cada worker ejecuta su historia aislado de los demás.
  - **success:** Cada worker tiene rama y worktree propios; ningún checkout ni artefacto de build se comparte.
- **CAP-4**
  - **intent:** El driver conserva una integración segura de las historias paralelas.
  - **success:** Antes de cada PR y merge, la rama se rebasa, verifica y fusiona serialmente contra el `origin/main` vigente.

## Constraints

- Cambios de contratos compartidos, persistencia, red, instalación o seguridad son exclusivos.
- Se conservan presupuesto, reparaciones, verificaciones, PR y squash merge de Build Lite actuales.
- Los worktrees efímeros viven bajo `.build-lite/worktrees/`.

## Non-goals

- Ejecutar en paralelo una historia cuya independencia no se pueda demostrar.
- Fusionar en paralelo o permitir push directo a `main`.
- Cambiar el contrato funcional de las historias del backlog.

## Success signal

- Un lote de historias internas independientes ejecuta hasta cuatro workers, preserva los resultados recuperables y fusiona cada historia validada sin que una rama use una base obsoleta.
