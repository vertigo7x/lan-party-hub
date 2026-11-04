---
title: 'Detener Build Lite tras el primer fallo y recuperar la Story 5.3'
type: 'bugfix'
created: '2026-11-04'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Tras el fallo de la Story 5.3, Build Lite admitió la 6.1 aunque declara esa dependencia. Además, la huella declarada de la 5.3 omitió dos helpers de progreso ya implementados.

**Approach:** Cerrar la admisión del scheduler ante el primer fallo y conservar los workers activos; completar la huella del plan de la 5.3 para permitir que la validación cubra el trabajo real.

</frozen-after-approval>

## Implementation Notes

- La resolución de dependencias continúa tras referencias ya completadas y detecta dependencias posteriores aún pendientes.
- El scheduler cierra admisiones tras un fallo, espera los workers ya iniciados y preserva su trabajo sin integrarlo.
- El plan canónico local de la 5.3 declara los tres archivos que la implementación añadió fuera de la huella inicial.

## Review Triage Log

- `high`: corregida la resolución que omitía dependencias posteriores al encontrar una previa completada.
- `high`: completada la huella canónica de la 5.3 para que el worker no falle tras verificaciones verdes.
- `medium`: los workers en vuelo y resultados verificados se preservan explícitamente al cerrar admisiones.
