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

- source_spec: `_bmad-output/implementation-artifacts/spec-9-2-proyeccion-de-instalaciones-y-de-operaciones-en-curso.md`
  summary: El cierre del cliente resuelve `DisposeAsync()` de forma síncrona (`GetAwaiter().GetResult()`) en el camino del hilo de la interfaz.
  evidence: El patrón es PREEXISTENTE en el cierre del cliente y no lo introduce la 9.2, así que queda fuera de esta historia; no se ha demostrado que la continuación capture el hilo de la interfaz. Para confirmarlo hay que observar el hilo que ejecuta la continuación de la disposición asíncrona durante el cierre y comprobar si bloquea ese hilo.

- source_spec: `_bmad-output/planning-artifacts/epics/epics-lan-party-hub-2026-10-26/epics.md`
  summary: Ningún camino del cliente publica `ClientErrorReported` al servidor, así que el registro de errores del organizador solo se llenaría desde las pruebas.
  evidence: La 9.3 deja listo el receptor (método del hub, caso de uso y persistencia con sus tres partes) y la vista que los muestra, pero el AC1 empieza por «Given un cliente que reporta un error» y hoy el contrato no tiene emisor: ni el coordinador de descargas ni el de instalaciones publican el error que catalogan. La 9.4 declara que el registro del cliente «presenta» los errores y no los produce, así que el hueco de la emisión queda sin asignar en el backlog y necesita una historia propia.
