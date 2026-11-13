# Spike S-4 · Rendimiento y recursos — resultado

**Historia:** 12.4 — S-4 · Rendimiento y recursos, medición en lugar de suposición
**Decisión que cierra:** los umbrales numéricos de NFR-2 y NFR-6 y los límites por cliente
de AD-14
**Carácter:** **NO BLOQUEANTE** — su resultado es una decisión de fase posterior y no es
requisito previo de ninguna capacidad del MVP (AD-21, R-MVP-5)
**Fecha:** 2026-11-12
**Resultado:** **SIN MEDIR** — los cuatro escenarios no han podido medirse en esta máquina.
Se publica por qué, con lo que sí está resuelto y lo que falta.

---

## 1. Objetivo y alcance

Medir, en lugar de suponer, la **tasa de transferencia** y el **consumo de CPU y memoria**
del servidor y del cliente, con 2, 5, 10 y 20 clientes simultáneos, y registrar el tiempo
de reanudación de una descarga interrumpida.

Alcance mínimo de la espina: descarga de un paquete de varios GB con esos cuatro
escenarios, midiendo CPU, memoria y enlace.

Como S-4 es **NO BLOQUEANTE**, su resultado **no cambia ningún artefacto del MVP** ni
bloquea su cierre (AD-21, R-MVP-5).

## 2. Por qué no se ha medido

### 2.1 El entorno no representa el escenario

El criterio exige **E2E en LAN real** con hasta 20 clientes. En esta máquina:

- Los 20 clientes serían **20 procesos en el mismo equipo**, con la **misma CPU y la misma
  memoria**. La red sería real (192.168.1.0/24), pero el recurso que S-4b quiere medir —el
  consumo por cliente— **no es separable**: veinte clientes sobre una máquina miden el
  consumo agregado de una máquina, no el de veinte equipos.
- El número de núcleos y la memoria de la máquina condicionan los resultados, así que los
  números **no serían extrapolables** a la LAN del evento, que es lo que el spike pretende
  informarle.

### 2.2 El arnés de medición no ha quedado fiable

Montando un arnés sobre el fixture existente (servidor real, cliente real, descarga
real), aparecieron dos impedimentos que no se han resuelto:

1. **El tamaño del paquete y el estrangulamiento del fixture**: el fixture sirve el
   contenido por trozos con una pausa entre ellos, pensada para que la prueba encuentre
   la descarga a medias. Con el tamaño real de un paquete grande, esa pausa hace el
   escenario inviable en tiempo de prueba.
2. **La identidad del equipo**: el fixture usa un identificador de equipo fijo, de modo
   que N clientes simultáneos contra el mismo servidor **colisionan en el registro** y
   ninguno llega a descargar. Es un límite del arnés de pruebas, no del producto, pero
   impide que la medición sea válida hasta que se resuelva.

Ninguna de las dos limitaciones es del producto: son del banco de pruebas. Por eso no se
publican cifras que no sean fiables.

## 3. Lo que sí queda resuelto

- **El escenario está escrito y es reutilizable**: el fixture de
  `DownloadRestartRecoveryTests` ya tiene el servidor real, la publicación real del
  paquete, el cliente real con su motor de transferencia real y la observación del
  `Range` en el servidor. La medición solo necesita: (a) un identificador de equipo por
  cliente, (b) una vía del fixture que no estrangule el contenido, y (c) la lectura de
  CPU y memoria del proceso.
- **La lectura de recursos está resuelta**: `/proc/self/stat` da los ticks de usuario y de
  sistema, y `Environment.WorkingSet` da la memoria residente. Es la lectura que usará la
  medición.
- **El criterio S-4c (el tiempo de reanudación)** no necesita la LAN: se puede medir con el
  escenario de interrupción y reinicio que ya existe.

## 4. Conclusión

1. **El criterio queda SIN MEDIR**, y el informe lo dice sin adornos. Publicar cifras de
   una máquina que no representa el escenario sería exactamente el «suponer en lugar de
   medir» que este spike existe para evitar.
2. **Los umbrales de NFR-2, NFR-6 y AD-14 siguen SIN FIJAR.** No se inventan.
3. **El MVP no cambia**: ningún artefacto del producto se ha tocado. S-4 es no bloqueante.
4. **Qué haría falta para cerrarlo**, en orden:
   - resolver los dos límites del arnés (identidad por cliente y contenido sin
     estrangulamiento);
   - ejecutar los escenarios en **equipos distintos** de la LAN del evento, o en un banco
     con la CPU y la memoria declaradas, para que el consumo por cliente sea separable;
   - medir el tiempo de reanudación (S-4c), que no necesita la LAN y puede cerrarse
     antes.

**Refs:** AD-14, AD-21, AR-25, NFR-2, NFR-6, R-MVP-5.
