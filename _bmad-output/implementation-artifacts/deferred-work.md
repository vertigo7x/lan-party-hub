# Trabajo diferido

- source_spec: `_bmad-output/implementation-artifacts/spec-1-1-estructura-de-solucion-y-contratos-compartidos.md`
  summary: El DTO de `GET /api/v1/server/info` (`{ name, versions, anchorPublicKey, anchorFingerprint }`) no está en la enumeración exhaustiva de AD-18, de modo que `Shared` no tiene hoy el contrato de esa respuesta.
  evidence: La espina define el endpoint en la tabla de operaciones REST y AD-18 declara exhaustiva la lista de contratos de `Shared` sin incluirlo; es un hueco de la espina, no de esta historia. Decidirlo exige enmendar AD-18 (o declarar el DTO fuera de `Shared`), y la historia que lo consuma es la 1.2 o la 4.4.

- source_spec: `_bmad-output/implementation-artifacts/spec-build-lite-plan-schema.md`
  summary: Generalizar la obtención de rutas candidatas para historias posteriores a la 9.2.
  evidence: El planificador de la 9.2 recibe rutas precargadas, pero otras historias mantienen el listado vacío y pueden necesitar explorar su código para producir rutas concretas.
- source_spec: `_bmad-output/implementation-artifacts/spec-build-lite-plan-schema.md`
  summary: Verificar el comportamiento del plan rechazado mientras ejecutan otros workers en el scheduler paralelo.
  evidence: Un DriverError de plan_story podría escapar del scheduler antes de registrar un FAIL por historia; falta una prueba de ejecución paralela que confirme si afecta workers activos. No aplica a --story 9.2.
- source_spec: `_bmad-output/implementation-artifacts/spec-build-lite-plan-schema.md`
  summary: Admitir rutas de escritura bajo nuevos directorios con una validación segura.
  evidence: La validación actual exige que el directorio padre exista; nuevas carpetas de módulos requieren un mecanismo explícito para declararlas sin confundir roles con rutas.
