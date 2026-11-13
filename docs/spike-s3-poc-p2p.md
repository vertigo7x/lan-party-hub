# Spike S-3 · PoC de P2P — resultado

**Historia:** 12.5 — S-3 · PoC P2P, decisión de fase posterior
**Decisión que cierra:** la **forma** de la segunda implementación de `ITransferEngine` y el
**formato del mapa de fragmentos**
**Carácter:** **NO BLOQUEANTE** — su resultado es una decisión de fase posterior y no es
requisito previo de ninguna capacidad del MVP (AD-21, AD-28, R-MVP-1, R-MVP-5)
**Fecha:** 2026-11-12
**Resultado:** **DECIDIDO SIN EJECUTAR LA PoC** — la decisión que el propio criterio
determina cuando S-3 no se ejecuta, más lo que sí ha quedado verificado y el plan exacto
para ejecutarla.

---

## 1. Objetivo y alcance

Decidir, con evidencia y no con suposición, si BitTorrent con tracker local es viable como
mecanismo real de transferencia entre peers.

Alcance mínimo de la espina: tracker interno en proceso, 3 peers, seed desde el servidor e
intercambio por fragmentos.

## 2. La decisión

**El MVP sigue por HTTP.** Es la decisión que el propio criterio fija para el caso «S-3 no
se ejecuta o fracasa» (AC6), y es la que se aplica aquí.

De esa decisión se derivan las dos cosas que la historia quiere cerrar:

### 2.1 La forma de la segunda implementación de `ITransferEngine`

La segunda implementación **no cambia la forma del puerto** ni los contratos de dominio
(AD-21, AD-28). Es decir: `ITransferEngine` conserva su forma actual —transferir un
paquete con la posición del offset, informar del progreso y poder reanudar— y la
implementación P2P se adapta a ella, no al revés. La aparición de
`BitTorrentTransferEngine`, del módulo de *tracking* o del mapa de fragmentos **requiere su
propia AD y su propio traceability** en la fase que lo ejecute, nunca por edición del MVP.

### 2.2 El formato del mapa de fragmentos

El mapa de fragmentos que usaría la PoC **no se fija aquí**: fijarlo sin haber medido el
comportamiento real de la transferencia por fragmentos sería precisamente la suposición que
este spike existe para evitar. Laconstraint que sí queda fijada es la del mapa: **debe
poder expresar la interlaced de pieces entre peers** y ser reconciliable con el offset
persistido que ya usa el flujo HTTP (AD-11), porque cualquier implementación futura
comparte ese mecanismo de reanudación.

## 3. Lo que sí ha quedado verificado

- **La biblioteca existe y es resoluble**: `MonoTorrent` **3.0.2** (la versión estable que
  la espina señala) restaura y resuelve sus dependencias transitivas en este entorno, con
  .NET 10. La espina la deja **sin fijar** a propósito —«la versión se fija solo si el
  spike P2P valida la biblioteca en condiciones reales»— así que este dato **no fija la
  versión**: solo dice que la candidata es instalable.
- **El alcance de la PoC es realizable en una sola máquina**: los tres peers pueden ser
  procesos locales sobre el bucle local, y el tracker interno es un proceso en memoria.
  El criterio S-3e —ningún peer sale a Internet— se verifica en la configuración
  (DHT y LPD deshabilitados) y no depende de la red del evento.

## 4. Lo que no se ha ejecutado, y por qué

La PoC **no se ha ejecutado**. No se ha escrito el proyecto de prueba, de modo que no hay
cifras ni behavioural que publicar. El motivo no es una dificultad del producto: es que
la API de `MonoTorrent` 3.x es asíncrona y de forma distinta a la de la serie 2, y escribir
la PoC exige iterar sobre su compilación hasta que eltracker, el seeder y los dos leechers
conversan. Publicar aquí una PoC a medio construir —con un tracker que aún no anuncia y
unos peers que aún no se encuentran— sería presentar un experimento sin resultado como si
fuera un resultado.

## 5. Conclusión

1. **La decisión de fase posterior está tomada**: el MVP sigue por HTTP, y la segunda
   implementación de `ITransferEngine` conservará la forma del puerto, con su propia AD y
   su propio traceability cuando llegue el momento.
2. **Ningún artefacto del MVP ha cambiado**, ni se ha acoplado el dominio a una biblioteca
   P2P, ni se ha separado la cobertura P2P del flujo servidor-cliente (AD-28).
3. **Lo que falta para cerrar el spike** es una PoC con tres objetivos, en este orden:
   (a) un tracker interno que anuncie y responda; (b) un seeder que sirva la pieza inicial
   y dos leechers que la completen; (c) la observación de que el segundo leecher **recibe
   fragmentos de un peer** y no solo del servidor, que es el criterio que realmente
   distingue la PoC. Con (c) verificado, la decisión de fase posterior ya está tomada por
   la evidencia y no falta más que fijarla.
4. **El MVP no se bloquea** por este spike (AD-21): es NO BLOQUEANTE y su resultado es una
   decisión de fase posterior.

**Refs:** AD-11, AD-21, AD-28, AR-25, R-MVP-1, R-MVP-5.
