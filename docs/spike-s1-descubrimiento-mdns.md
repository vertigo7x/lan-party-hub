# Spike S-1 · Descubrimiento por mDNS — resultado

**Historia:** 12.2 — S-1 · Descubrimiento, validación de mDNS junto a la dirección manual
**Decisión que cierra:** la política de descubrimiento (R-03)
**Carácter:** **NO BLOQUEANTE** — su resultado es una decisión de fase posterior y no es
requisito previo de ninguna capacidad del MVP (AD-21, R-MVP-5)
**Fecha:** 2026-11-12
**Resultado:** **POSITIVO CON UNA RESERVA** — la vía de descubrimiento funciona en la red
con *multicast* y la vía manual funciona siempre; el criterio de «dos redes con políticas
distintas» **no ha podido completarse en esta máquina**, y por qué está aquí escrito.

---

## 1. Objetivo y alcance

Medir el comportamiento real de mDNS en redes con políticas distintas, para que el riesgo
R-03 quede documentado **antes** de confiar en él.

Alcance mínimo de la espina: un servidor que anuncia `_lanpartyhub._tcp`, y un cliente que
descubre y compara con la dirección manual.

El spike **no cambia ningún artefacto del MVP**: si no se ejecuta o fracasa, el MVP sigue
igual porque la **vía manual basta** (AD-3, AD-21).

## 2. Configuración del experimento

| Elemento | Valor |
|---|---|
| Red del experimento | `enp2s0`, `192.168.1.127/24`, con `MULTICAST` activo |
| Mecanismo | mDNS sobre UDP, servicio `_lanpartyhub._tcp` (AD-3) |
| Implementación | `MdnsServiceAdvertiser` + `HaukcodeMdnsPublisher` (servidor), `MdnsServerDiscovery` (cliente) |
| Nivel de la prueba | integración sobre la **red real** de la máquina (no un doble de red) |

## 3. Criterios y evidencia

### (a) El cliente descubre el servidor **sin** introducir la dirección — S-1a

*Evidencia:* `tests/IntegrationTests/MdnsDiscoveryTests.cs` →
`The_client_discovers_the_server_without_the_manual_address`.

El servidor anuncia `_lanpartyhub._tcp` de verdad y el cliente lo busca de verdad: el
descubrimiento devuelve el endpoint con el puerto anunciado. **Resultado: descubierto** ✔

*Prueba de que el criterio no se cumple por accidente:*
`The_discovery_of_a_service_that_is_not_published_returns_nothing` — sin anuncio, el
descubrimiento **no inventa** servidores.

### (b) En la misma red, la dirección manual conecta igualmente — S-1b

*Evidencia:* `tests/IntegrationTests/ManualConnectionTests.cs` →
`The_client_connects_with_the_address_that_the_player_types` y
`The_address_that_the_player_types_is_the_one_that_the_client_uses`.

La vía manual no depende de mDNS en absoluto: el jugador escribe la dirección y el cliente
conecta. **Resultado: conecta** ✔

### (c) El comportamiento queda registrado en al menos dos redes con políticas distintas — S-1c

**RESULTADO PARCIAL: una red medida, la segunda no ha podido medirse en esta máquina.**

Lo que sí está medido, con evidencia y en la red real:

| Red | Política | Resultado | Evidencia |
|---|---|---|---|
| `enp2s0` (192.168.1.0/24) | **con** *multicast* | el descubrimiento **funciona**: el servidor aparece sin dirección | `MdnsDiscoveryTests.The_client_discovers_the_server_without_the_manual_address` |
| la misma red, **vía manual** | irrelevante para mDNS | la conexión **funciona** sin depender del descubrimiento | `ManualConnectionTests.The_client_connects_with_the_address_that_the_player_types` |

Lo que **no** ha podido medirse, y por qué:

- La segunda red con política «sin *multicast*» se iba a construir con un **namespace de
  red** (`unshare --user --net`) con una pareja de `veth` y el bit *multicast* desactivado
  en la interfaz, para reproducir una red que no reenvía *multicast*.
- **Este host no lo permite**: el contenedor bloquea la creación de namespaces de red
  (`unshare -n` → *«error al dejar de compartir: Operación no permitida»*) y también la
  escritura de `/proc/self/uid_map` (`unshare --map-root-user --net` → *«Operación no
  permitida»*). La cuenta no es root, así que tampoco se puede cambiar la política de la
  interfaz (`ip link set dev enp2s0 multicast off`) ni filtrar con el cortafuegos.
- Queda **una sola** red con *multicast* disponible para medir.

**Consecuencia, y es la que importa:** como S-1 es **NO BLOQUEANTE**, esta reserva **no
cambia ningún artefacto del MVP** ni bloquea su cierre (AD-21). La razón de fondo es la que
ya fija AD-3: **la vía manual basta**. Si la red del evento no reenvía *multicast*, el
descubrimiento no encuentra el servidor y el jugador escribe la dirección; el producto
funciona igual.

**Riesgo R-03, registrado.** R-03 sigue **ABIERTO** y con la evidencia de este spike: el
descubrimiento depende de una capacidad de la red que la organización no controla. La
mitigación ya es de producto (la vía manual siempre está) y la consecuencia de la reserva
es, precisamente, la que justifica que S-1 no bloquee.

## 4. Resultado de la ejecución

| Prueba | Resultado |
|---|---|
| `MdnsDiscoveryTests.The_client_discovers_the_server_without_the_manual_address` | **verde** (red real, 13 s) |
| `MdnsDiscoveryTests.The_discovery_of_a_service_that_is_not_published_returns_nothing` | **verde** |
| `ManualConnectionTests` (vía manual) | **verde** (suite existente) |

## 5. Conclusión

1. **El descubrimiento funciona** en una red con *multicast*, y lo hace con la
   implementación real, no con un doble.
2. **La vía manual funciona siempre** e es independiente del descubrimiento: es la razón por
   la que S-1 no es bloqueante (AD-3, AD-21).
3. **La segunda red no ha podido medirse en esta máquina** por una limitación del entorno
   (namespaces de red bloqueados, sin root), no por un fallo del producto. Queda registrado
   como **reserva** y como riesgo R-03 abierto.
4. **El MVP no cambia**: ningún artefacto del producto se ha tocado para este spike. Solo se
   ha añadido la evidencia de la red real.

**Qué haría falta para cerrar la reserva:** ejecutar el mismo experimento en una red de
verdad sin *multicast* (por ejemplo, un punto de acceso con *multicast* filtrado, o una
máquina con permisos de administrador) y anexar la salida al informe. La prueba ya está
escrita y es la misma: `MdnsDiscoveryTests`.

**Refs:** AD-3, AD-21, AD-28, R-03, R-MVP-5, UX-DR-30, UX-DR-42.
