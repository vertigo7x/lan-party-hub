---
title: 'Eliminar la carrera en las pruebas de disponibilidad del servidor'
type: 'bugfix'
created: '2026-11-04'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Las pruebas de disponibilidad enumeraban una lista mientras el callback asíncrono añadía estados, provocando fallos intermitentes.

**Approach:** Usar una cola concurrente para capturar publicaciones y mantener las mismas aserciones sobre su orden y contenido.

</frozen-after-approval>

## Implementation Notes

- Las cinco suscripciones de `ServerAvailabilityMonitorTests` usan `ConcurrentQueue<ServerAvailability>`.
- `WaitForAvailableAsync` acepta `IEnumerable<ServerAvailability>` para conservar la lectura de instantáneas.

## Review Triage Log

- Sin hallazgos bloqueantes. La revisión confirmó que las aserciones mantienen la semántica FIFO y que la enumeración concurrente es segura.
