---
title: 'Build Lite: plan válido y diagnóstico preciso'
type: 'bugfix'
created: '2026-11-09'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** La 9.2 se detuvo antes de implementar: el planificador escribió un plan de 758 palabras con encabezado incorrecto y roles en vez de rutas, pero el driver comunicó erróneamente que no existía.

**Approach:** Dar al planificador rutas candidatas reales y un ejemplo compacto del formato obligatorio; rechazar planes sin rutas concretas o demasiado largos con motivos exactos. Conservar el plan fallido como evidencia; fusionar el arreglo por PR y probar solo la 9.2 con MiniMax M3 para implementar.

</frozen-after-approval>

## Implementation Notes

- Corregir `tools/build-lite.py` en las rutas de planificación secuencial y coordinada, sin modificar la implementación de la historia ni los modelos fijados.
- El plan rechazado de la 9.2 se copió a `.build-lite/logs/9-2/rejected-plan-1.md`; el original sigue intacto hasta integrar el arreglo.
- Se añaden rutas candidatas reales de la 9.2, ejemplo de JSON y formato obligatorio; `plan_errors` comprueba también los metadatos y presenta todos los motivos de rechazo.
- Se amplían las autopruebas para reproducir el rechazo de encabezado, longitud y roles; se actualiza `docs/unattended-loop.md`.
- Tras la revisión se reconoce `contrato` como superficie sensible, se verifica el ID del encabezado, se exige `schema_changes` y se registra el rechazo en el diario con copia local. `reads` admite directorios existentes como antes.
- `uv run tools/build-lite.py --self-test`, `dotnet test LANPartyHub.sln --nologo -v q`, `tools/check-epics.ps1` y `tools/check-bmad-build-auto-customization.ps1` pasaron.

## Review Triage Log

- 1 `medium`: `contrato` no era sensible; corregido.
- 2 `false`: las superficies arbitrarias sirven para detectar conflictos entre historias; no todas son clases sensibles.
- 3 `medium`: ID ajeno aceptado; corregido.
- 4 `medium`: lectura de directorio rechazada; corregido.
- 5 `medium`: `_bmad-output/` aceptado; crear nuevos directorios sigue fuera del alcance de este arreglo.
- 6 `maybe-false`: la ruta paralela ya detenía la ejecución ante planes fallidos; evaluar el tratamiento de workers activos al probar backlog, no en la prueba única 9.2.
- 7 `false`: una sola planificación es el presupuesto acordado; corregir el plan en otra pasada no está autorizado.
- 8 `medium`: diario no reflejaba rechazo; corregido con `plan-validation`.
- 9 `false`: la evidencia es un log local ignorado deliberadamente, conservado también para rechazos futuros.
- 10 `medium`: las rutas precargadas se limitan explícitamente a 9.2; generalizarlo al backlog sería otro cambio independiente.
- 11 `low`: se añadieron comprobaciones de errores del plan; comprobaciones adicionales del prompt quedan fuera de la reproducción.
- 12 `low`: documentación afinada con reglas exactas y ubicación del registro.
- 13 `medium`: `schema_changes` opcional; ahora obligatorio.
- 14 `low`: parte de los mensajes heredados siguen en inglés, sin afectar el motivo del rechazo.
- 15 `false`: la ruta `oneshot` exige únicamente intención y notas, no la plantilla completa.
