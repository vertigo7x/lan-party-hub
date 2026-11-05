---
title: 'Recuperación de contexto completo tras timeout de Build Lite'
type: 'bugfix'
created: '2026-11-05'
status: 'done'
route: 'oneshot'
review_loop_iteration: 0
context:
  - 'docs/unattended-loop.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** La Story 7.1 agotó el tiempo del preparador de contexto aunque este escribió un paquete estructuralmente completo; el driver descartó la salida antes de sellarla con el hash del plan.

**Approach:** Build Lite aceptará únicamente un paquete recién escrito que supere la validación estructural después del timeout final, lo sellará contra el plan y registrará su recuperación como completa; conservará el rechazo para cualquier salida parcial u otro error técnico.

</frozen-after-approval>

## Implementation Notes

- Se acepta únicamente una salida nueva que pasa la validación estructural tras el timeout final; después se sella con el SHA-256 del plan como cualquier preparación normal. El self-test reproduce el worktree, el copiado a caché, el sellado, la reutilización y el rechazo de una salida parcial.

## Review Triage Log

- medium — Se documentó la excepción operacional y el estado recuperado del journal; evita contradicción entre timeout previo y continuación válida.
- high — La recuperación añade una entrada `complete` al journal tras comprobar que el último estado de contexto fue `timeout`; un fallo posterior conserva su causa real.
- medium — Se restringió la aceptación a timeout y validación estructural, comprobando ambas rutas recién limpiadas; salida parcial y otros errores siguen propagándose.
- medium — El self-test cubre worktree, caché, sellado, reutilización y timeout con salida parcial.
- false — La especificación mínima sigue la ruta `oneshot` y el template permite omitir las secciones no necesarias.
