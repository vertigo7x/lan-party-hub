# Spike S-2 · Raíz de confianza — resultado

**Historia:** 12.1 — S-2 · Raíz de confianza, validación del bootstrap out-of-band
**Decisión que cierra:** R5 / OQ-1 — raíz de confianza y firma (AD-2, AD-8, AD-27)
**Carácter:** **BLOQUEANTE** — su resultado es requisito previo de FR-6 y SM-3 (AD-21)
**Fecha:** 2026-11-12
**Resultado:** **POSITIVO** — la cadena firma → manifiesto → paquete se comporta como la
espina fija, con el rechazo **antes de persistir**. El cierre del MVP no queda bloqueado por
S-2.

---

## 1. Objetivo y alcance

Validar experimentalmente la cadena de confianza de AD-2, AD-8 y AD-27: un ancla fijada
**fuera de banda** (fingerprint), un manifiesto firmado con Ed25519, la verificación de esa
firma, la comprobación de integridad del paquete y —el caso decisivo— el **rechazo previo a
persistir** cuando la confianza falla.

El spike **no introduce ninguna capacidad nueva** en el cliente ni en el servidor: no se
escribió ni una línea de producción. Lo que se hizo fue **ejercitar la cadena ya
construida** con los criterios de éxito que la espina declara, y publicar la evidencia.

### 1.1 Configuración del experimento

| Elemento | Valor |
|---|---|
| Runtime | .NET 10 |
| Interfaz | Avalonia 12.1.4 |
| Firma | Ed25519 (BouncyCastle, pinneado) |
| Bootstrap | fingerprint del ancla obtained **out-of-band** (QR o contraste en la pantalla del organizador) |
| Canonicalización | JSON canónico sin espacios superfluos, claves ordenadas (AD-2) |
| Nivel de la prueba | unitario e integración; **nunca** E2E de LAN real (la espina lo fija: *«no hace falta una LAN para demostrar que la comprobación precede a la escritura»*) |

---

## 2. Criterios y evidencia

Cada criterio de la espina, con la prueba que lo demuestra y el resultado real de esa
prueba en esta ejecución.

### (a) Un manifiesto alterado **se rechaza** y no se ejecuta

*Evidencia:* `tests/UnitTests/Ed25519ManifestVerifierTests.cs` →
`An_altered_manifest_does_not_verify`.

El manifiesto se firma sobre su texto canónico; alterar un solo byte del manifiesto hace
que la firma ya no verifique sobre él. **Resultado: rechazado** ✔

### (b) Un manifiesto firmado por otra clave **se rechaza** contra el ancla fijada

*Evidencia:* `tests/UnitTests/Ed25519ManifestVerifierTests.cs` →
`A_signature_of_another_key_does_not_verify`, `A_signature_of_the_wrong_length_does_not_verify`,
`A_signature_that_cannot_be_read_does_not_verify`.

Este es el caso de la **suplantación**: el manifiesto llega firmado con una clave que no es
la del ancla. **Resultado: rechazado** ✔

### (c) Un paquete cuyo `sha256` no coincide **se rechaza**

*Evidencia:* `tests/UnitTests/PackageIntegrityTests.cs` (el hash que declara el manifiesto es
el que calcula el framework, en minúsculas hexadecimales, sobre los bytes leídos) e
`tests/IntegrationTests/DownloadIntegrityTests.cs` → el fallo se publica con el código
`ManifestSignatureInvalidCode` / el de integridad, según el caso.

El hash se calcula sobre los bytes **reales** que se leen, no sobre lo que el manifiesto
declara. **Resultado: rechazado** ✔

### (d) El fingerprint se muestra y se confirma en la UI **antes** de fijar el ancla

*Evidencia:* `tests/UnitTests/FixTrustAnchorTests.cs` →
`A_matching_fingerprint_fixes_the_anchor`, `A_mismatching_fingerprint_is_rejected`.

El orden de AD-27 es normativo y lo implementa el caso de uso: (1) el cliente obtiene el
fingerprint esperado fuera de banda; (2) obtiene del servidor la clave pública; (3)
**compara**; (4) solo entonces persiste. La comparación ocurre **antes de tocar el store**.
**Resultado: el fingerprint se confirma antes de fijar** ✔

### (e) Prueba MITM de sustitución del ancla: el cliente rechaza la conexión, **no persiste el ancla sustituta** y **no escribe nada**

*Evidencia:* `tests/UnitTests/FixTrustAnchorTests.cs` →
`A_mismatching_fingerprint_is_rejected` y, sobre todo,
`A_mismatching_fingerprint_writes_nothing`.

El store es un doble que **cuenta las escrituras**. Con un fingerprint distinto del
esperado: el resultado es `Rejected` y el contador de escrituras es **cero** — ni el ancla,
ni un manifiesto en la caché `manifests/`, ni un archivo parcial, ni una operación
registrada. El diagnóstico además lleva un código **propio**, distinto del de «firma
inválida» (`ServerConnectionServiceTests`), de modo que el operador puede distinguir una
suplantación de una firma corrupta. **Resultado: rechazado sin escribir nada** ✔

### (f) Rechazo previo a persistir: con la firma inválida contra el ancla ya fijada no queda ninguna escritura ni operación iniciada

*Evidencia:* `tests/IntegrationTests/DownloadIntegrityTests.cs` →
`A_manifest_that_does_not_validate_leaves_no_write_on_disk` (el manifiesto que no valida
**no deja ninguna escritura en disco** y **cero operaciones** registradas),
`A_manifest_signed_with_another_key_is_not_trustworthy` (el manifiesto con firma inválida se
rechaza con `ManifestSignatureInvalidCode` e `IsUntrusted` **antes** de la transferencia),
`The_update_writes_nothing_when_the_manifest_does_not_validate` (ni la ACTUALIZACIÓN escribe
nada) y `The_corruption_and_the_lack_of_authenticity_are_different_failures` (la corrupción y
la falta de autenticidad son fallos DISTINTOS, con códigos distintos).

La comprobación de la firma ocurre **antes** de escribir el manifiesto, antes de tocar el
árbol de la instalación y antes de registrar la operación. **Resultado: nada escrito, ninguna
operación iniciada** ✔

---

## 3. Resultado de la ejecución

| Suite | Pruebas | Resultado |
|---|---|---|
| `Ed25519ManifestVerifierTests`, `FixTrustAnchorTests`, `PackageIntegrityTests`, `AnchorFingerprintTests`, `ManifestSignerTests`, `ManifestCanonicalizerTests` | 67 | **todas verdes** |
| `DownloadIntegrityTests`, `InstallationOrderTests`, `InstallationRepairTests`, `InstallationPersistenceTests` | 22 | **todas verdes** |
| **Total de la cadena de confianza** | **89** | **verde** |

Los seis criterios de la espina quedan **cubiertos por pruebas que pasan** en esta
ejecución.

## 4. Conclusión

1. **La cadena se comporta como la espina la fija.** No se ha encontrado ninguna vía por la
   que un manifiesto alterado, firmado por otra clave, un paquete con el hash incorrecto o
   un ancla sustituida lleguen a aceptarse o a persistirse.
2. **El rechazo precede a persistir**, y eso es observable, no una promesa: el doble del
   store cuenta cero escrituras.
3. **El spike no ha introducido capacidad nueva.** No se ha tocado código de producción: la
   evidencia procede de pruebas sobre las piezas que ya existían.
4. **El carácter bloqueante se satisface.** FR-6 y SM-3 tenían como requisito previo el
   resultado de S-2; con resultado positivo, **el cierre del MVP deja de estar bloqueado por
   la confianza de paquetes** (AD-21, R-MVP-5).

## 5. Límites declarados

- La evidencia es **de integración y unitaria**, no de E2E en una LAN real. Es lo que la
  propia espina fija para estos criterios: *«es unitario o de integración, nunca de E2E de LAN
  real»*.
- El spike **no valida** el intercambio P2P: es el S-3, que es **no bloqueante** y queda
  fuera del MVP (AD-21, AD-28).
- El caso «cliente compartiendo fragmentos con otros» de `docs/plan.md` §10 **no es un caso
  del MVP**: presupone P2P, que no existe en el MVP.

**Refs:** AD-2, AD-8, AD-21, AD-27, AD-28, FR-5, FR-6, FR-8, FR-11, NFR-1, SM-3, R-MVP-5,
R5/OQ-1, UX-DR-38.
