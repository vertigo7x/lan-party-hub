# Spike S-5 · Reanudación de paquetes grandes — resultado

**Historia:** 12.3 — S-5 · Reanudación de paquetes grandes, validación del offset persistido
**Decisión que cierra:** el mecanismo de reanudación de AD-11 (offset persistido)
**Carácter:** **NO BLOQUEANTE** — su resultado es una decisión de fase posterior y no es
requisito previo de ninguna capacidad del MVP (AD-21, R-MVP-5)
**Fecha:** 2026-11-12
**Resultado:** **PARCIAL** — el mecanismo de reanudación está verificado en su forma
básica, pero la condición de escala (7+ GB) y la de N interrupciones **no han podido
medirse**. Se publica el resultado con esa reserva, y con una observación que merece
atención.

---

## 1. Objetivo y alcance

Validar experimentalmente la reanudación de un paquete grande con reinicio del proceso
del cliente, para que el punto más frágil del MVP (AD-11: el offset persistido) tenga
**evidencia** y no suposición.

Alcance mínimo de la espina: una descarga de **7+ GB** interrumpida y reanudada **varias
veces**, con reinicio del proceso del cliente.

Como S-5 es **NO BLOQUEANTE**, su resultado **no cambia ningún artefacto del MVP** ni
bloquea su cierre: si el spike no se ejecuta o fracasa, el MVP sigue igual (AD-21,
R-MVP-5).

## 2. Configuración del experimento

| Elemento | Valor |
|---|---|
| Mecanismo | Transferencia HTTP con `Range`, offset persistido en SQLite (AD-11) |
| Entorno | `enp2s0`, 192.168.1.0/24, 375 GB libres |
| Servidor | `TestServer` real, con el contenido servido por HTTP de verdad |
| Cliente | arranque **real** del cliente (`ClientProgram.StartClientAsync`): aplica las migraciones, emite la identidad y **reconcilia el progreso persistido contra el parcial del disco** |
| Doble | ninguno en lo observado: el `Range` se observa en el servidor, el offset en el SQLite de verdad y el parcial en el disco (AD-16) |

## 3. Criterios y evidencia

### (a) La reanudación parte del offset **persistido**, no de cero — S-5a

**Verificado.** *Evidencia:* `tests/IntegrationTests/DownloadRestartRecoveryTests.cs` →
`A_download_that_was_interrupted_resumes_from_the_persisted_progress_after_a_restart`.

La observación es de las dos puntas: el **servidor** recibe el `Range` que empieza en el
offset persistido (`bytes={offset}-`, y nunca `bytes=0-`) y el **cliente** presenta la
operación como «Reanudando desde …» con ese mismo offset. ✔

### (b) El reinicio del proceso reconcilia el parcial y el registro **sin divergencia** — S-5b

**Verificado en su forma básica.** *Evidencia:* el mismo test —el offset persistido nunca
es mayor que lo que el parcial contiene en disco, y el progreso efectivo es la
**intersección** de lo persistido y lo que el archivo parcial tiene (AD-11, AD-24)—, más
`Every_interrupted_download_resumes_from_the_persisted_progress`. ✔

### (c) Con N interrupciones, el hash final **coincide** — S-5c

**SIN MEDIR.** La cobertura existente comprueba **una** interrupción seguida de una
reanudación completa; no comprueba N interrupciones encadenadas ni el hash final tras
ellas.

El intento de cerrar este criterio se encontró con un problema **no resuelto** y, por eso,
no se publica como verificado: en una sesión **interrumpida** posterior, la primera
petición registrada al servidor fue `bytes=0-`, mientras que en la sesión **no**
interrumpida (que es la que usa la prueba existente) la petición es `bytes={offset}-`. No
se ha determinado si es un artefacto del momento en que se registra la petición o una
divergencia real entre el registro y el parcial. **Queda como observación abierta**, no
como veredicto.

### (d) La escala de 7+ GB

**SIN MEDIR.** La máquina tiene espacio de sobra (375 GB libres), pero el tiempo de
transferencia y de hashing de un paquete de 7+ GB multiplica por diez el coste de la suite
de integración, y el criterio (c) —que es el que da sentido a la escala— sigue abierto. La
medición a escala se hará con el criterio (c) cerrado, no antes.

## 4. Conclusión

1. **El mecanismo de reanudación funciona** en su forma básica: el offset persistido se
   respeta, el cliente no vuelve a pedir desde cero en la reanudación y el progreso
   efectivo es la intersección con lo que hay en disco (AD-11, AD-24).
2. **Quedan dos mediciones**: N interrupciones con el hash final, y la escala de 7+ GB.
3. **Una observación abierta**: la primera petición registrada en una sesión interrumpida
   fue `bytes=0-`. No está cerrada y no se afirma que sea un defecto; sí que conviene
   mirarla antes de confiar en la reanudación a escala, que es justo lo que este spike
   quería medir.
4. **El MVP no cambia**: ningún artefacto del producto se ha tocado. S-5 es no bloqueante
   y, de momento, tampoco cierra ninguna decisión de fase posterior.

**Qué haría falta para cerrar la reserva:** (1) cerrar la observación de la sesión
interrumpida —con el registro de peticiones por sesión, no por descarga—; (2) repetir el
escenario con N=5 interrupciones y comprobar el hash final; (3) repetirlo con un paquete de
7+ GB. El escenario ya está escrito (`DownloadRestartRecoveryTests`) y solo necesita
extensión.

**Refs:** AD-11, AD-16, AD-20, AD-21, AD-24, NFR-9, R-MVP-5, SM-2, UX-DR-45.
