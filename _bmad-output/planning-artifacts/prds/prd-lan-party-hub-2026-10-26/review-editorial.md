# Revisión editorial — PRD LAN Party Hub

Este documento existe para ayudar a los equipos de producto, UX, arquitectura e ingeniería a
comprender exactamente qué debe resolver LAN Party Hub en el MVP, con requisitos trazables y sin
presentar como decididas las cuestiones técnicas abiertas. Modelo de estructura: Estratégica /
Contexto (pirámide).

| Pass | Texto original | Texto revisado | Cambios |
| --- | --- | --- | --- |
| structure | §6 No objetivos del MVP y §7.2 Excluido del MVP | MERGE §7.2 dentro de §6; mantener §7 sólo con “Incluido” | Elimina duplicación de exclusiones (~25 palabras) sin perder alcance. |
| structure | §11 Requisitos excluidos del MVP | CUT; renumerar §12-§14 como §11-§13 | Elimina una tercera referencia a las mismas exclusiones (~26 palabras). |
| prose | “coordinator” (§4.3) | “coordinador” | Corrige mezcla de idioma sin cambiar el significado. |
| prose | “descargas simultáneas de varios clientes avanzan” (§4.3 FR-5) | “las descargas simultáneas de varios clientes avanzan” | Corrige concordancia y deja explícito el sujeto. |
| prose | “Un paquete alterado, no confiable o manipulado” (§8 SM-3) | “Un paquete alterado, manipulado o que no supera la validación de confianza” | Evita ambigüedad entre “no confiable” y manipulación. |
| prose | “posible acoplamiento o Validación tardía” (§10 P2P) | “posible acoplamiento o validación tardía” | Corrige capitalización errónea. |

**Resumen:** 5 recomendaciones; reducción estimada de 51 palabras (1,4% del original de 3.532).
No se propone recorte de recorridos, glosario ni trazabilidad porque sostienen la comprensión y
el consumo posterior. La longitud es adecuada para un PRD de plataforma que alimenta UX,
arquitectura e historias.

## Nota de vigencia — Enmienda ENM-001-01

Esta revisión editorial no describe el contrato de acceso de clientes y no requiere corrección por
la enmienda. El contrato vigente es LAN administrada con ejecutable distribuido por la
organización: **sin autenticación ni autorización de clientes**, con FR-4 sustituida por
compatibilidad de versión.

Las cinco recomendaciones de la tabla se aplicaron sobre el texto que sigue vigente: los passages
citados ("coordinador", "las descargas simultáneas de varios clientes", "Un paquete alterado,
manipulado o que no supera la validación de confianza" y "posible acoplamiento o validación
tardía") siguen presentes en el PRD actual, y la numeración de secciones §0..§14 no varió con la
enmienda, por lo que las referencias de esta tabla siguen resolviendo.

Lo que sí cambió y afecta a la lectura de este artefacto es la **renumeración de preguntas
abiertas**: al eliminarse la pregunta de autenticación de clientes (OQ-1 anterior), las
preguntas y supuestos pasan de OQ-1..OQ-5 a **OQ-1..OQ-4** (R5 → OQ-1, R3 → OQ-2, P2P → OQ-3,
R-01/NFR-6 → OQ-4).
