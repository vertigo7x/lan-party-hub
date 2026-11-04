---
title: 'Evitar planificación durante la exclusividad activa de Build Lite'
type: 'bugfix'
created: '2026-11-04'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** El scheduler de Build Lite invoca planificadores de historias posteriores mientras una historia exclusiva sigue en ejecución. Eso bloquea la recogida oportuna del worker y provoca planes repetidos e innecesarios.

**Approach:** El scheduler debe esperar sin planificar cuando cualquier worker activo es exclusivo y debe cubrir ese comportamiento con una prueba de regresión. Los planes generados durante la ejecución son estado local y no deben ensuciar Git.

</frozen-after-approval>

## Implementation Notes

- Se centralizó la definición de worker exclusivo en `slot_is_exclusive` y el scheduler espera sin invocar planificadores mientras exista uno activo.
- Se almacenan los metadatos de planificación por historia durante el run para evitar reevaluar el mismo plan en cada iteración.
- Los planes nuevos bajo `.build-lite/plans/` se tratan como caché local; los resultados publicados continúan versionados.

## Review Triage Log

- `high`: la planificación podía bloquear el coordinador con un worker exclusivo activo; corregido con la guarda previa a la admisión.
- `medium`: la regla de exclusividad estaba duplicada entre la admisión y la nueva guarda; corregido con `slot_is_exclusive`.
- `medium`: los planes nuevos ensuciaban el checkout canónico tras un run interrumpido; corregido ignorando la caché local. Los planes históricos ya versionados se conservan para no reescribir evidencia previa.
- `low`: se documentó que el coordinador no planifica candidatas mientras una historia exclusiva está activa.
