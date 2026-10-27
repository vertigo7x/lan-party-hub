# Trabajo diferido

- source_spec: `_bmad-output/implementation-artifacts/spec-1-1-estructura-de-solucion-y-contratos-compartidos.md`
  summary: El DTO de `GET /api/v1/server/info` (`{ name, versions, anchorPublicKey, anchorFingerprint }`) no está en la enumeración exhaustiva de AD-18, de modo que `Shared` no tiene hoy el contrato de esa respuesta.
  evidence: La espina define el endpoint en la tabla de operaciones REST y AD-18 declara exhaustiva la lista de contratos de `Shared` sin incluirlo; es un hueco de la espina, no de esta historia. Decidirlo exige enmendar AD-18 (o declarar el DTO fuera de `Shared`), y la historia que lo consuma es la 1.2 o la 4.4.
