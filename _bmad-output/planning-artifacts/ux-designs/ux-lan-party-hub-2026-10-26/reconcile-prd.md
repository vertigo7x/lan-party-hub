# Reconciliación del PRD — LAN Party Hub

## Alcance

Esta entrada reconcilia el PRD aprobado (`prds/prd-lan-party-hub-2026-10-26/prd.md`) con las espinas de UX `DESIGN.md` y `EXPERIENCE.md`. Se conserva el PRD como fuente de requisitos; la UX define comportamiento observable sin cerrar decisiones técnicas de Fase D.

## Cobertura explícita de requisitos

| Requisito | Cobertura UX | Evidencia / validación |
|---|---|---|
| FR-1 Administración del catálogo | Superficie Catálogo del servidor; alta y modificación de metadatos; publicación condicionada a licencia y restricciones | `EXPERIENCE.md` IA servidor, Patrones de estado, flujo de Marta |
| FR-2 Sincronización, búsqueda y filtrado | Catálogo del cliente; sincronización, búsqueda, categorías; último catálogo válido offline | IA cliente, Patrones de estado, flujo de Ana |
| FR-3 Descubrimiento y conexión | Conexión con descubrimiento y dirección manual; reconexión y sincronización pendiente | Patrones de estado, primitivas de interacción, flujo de Ana |
| FR-4 Compatibilidad de versiones | Diagnóstico explícito; operaciones bloqueadas para cliente incompatible; ausencia de login | Fundación, Patrones de estado, flujo de Ana |
| FR-5 Descarga desde servidor | Progreso, pausa, reanudación y persistencia; descargas independientes | Patrones de componente, Patrones de estado, flujos de Ana y recuperación |
| FR-6 Integridad y autenticidad | Paquete dañado o no confiable; instalación bloqueada; diagnóstico disponible | Patrones de estado, microcopia crítica, flujo de Ana |
| FR-7 Abstracción de transferencia y P2P | UI MVP limitada a servidor; ausencia de peer exchange hasta PoC | Inspiration y anti-patrones; preguntas abiertas OQ-3 |
| FR-8 Instalación portable | Manifiesto, operaciones tipadas, confirmación privilegiada, directorio autorizado, reintento | Patrones de estado, flujo de Marta, flujo de recuperación |
| FR-9 Ejecución | Ejecutable detectado, argumentos, directorio de trabajo, proceso en ejecución, error identificable | Patrones de estado, flujo de Ana |
| FR-10 Panel de estado de clientes | Tabla densa con conexión, versiones, descargas, instalaciones y errores | IA servidor, flujo de Luis |
| FR-11 Versiones completas | Estado desactualizado y acción Actualizar; sin actualización diferencial | Patrones de estado |
| NFR-1 Red local autónoma | Sin dependencia de Internet; catálogo y operaciones locales | Escritorio y plataforma |
| NFR-2 Escala inicial | Sin promesa de velocidad; microcopia sin tiempo restante | Voz y tono; OQ-4 |
| NFR-3 Paquetes varios GB | Persistencia de progreso y reanudación | Flujo de recuperación |
| NFR-4 Windows 10/11 | Fundición; evolución multiplataforma fuera de alcance | Fundación |
| NFR-5 Seguridad de contenido | Validación previa, bloqueo de instalación, confirmación privilegiada | Patrones de estado; flujo de Marta |
| NFR-6 Uso de recursos | Sin umbrales inventados; sin promesa de rendimiento | Voz y tono; preguntas abiertas OQ-4 |
| NFR-7 Diagnóstico | Errores persistidos, nombrados y asociados al equipo u operación | Piso de accesibilidad; flujo de Luis |
| NFR-8 Licencias | Campos obligatorios y bloqueo de publicación | Flujo de Marta |
| NFR-9 Resiliencia | Reconexión, instalación incompleta, recuperación y error explícito | Patrones de estado; flujo de recuperación |
| NFR-10 Sin cloud obligatorio | Operación LAN local; sin trackers/DHT en la UI | Fundación; escritura de plataforma |
| SM-1 a SM-5, SM-C1 y SM-C2 | Flujos reproducibles, estado observable, sin promesas prematuras | Flujos clave y validación de cobertura |

## Restricciones y preguntas abiertas preservadas

| ID | Tratamiento en UX | Estado |
|---|---|---|
| R3 / OQ-2 | Descubrimiento y dirección manual visibles; no se elige mDNS ni otro mecanismo | Abierta para Fase D |
| R5 / OQ-1 | Se distinguen corrupción y falta de confianza, pero no se fija raíz, distribución, algoritmo ni firma | Abierta para Fase D |
| P2P / OQ-3 | No se presenta intercambio entre clientes; MVP descarga desde servidor | Abierta para Fase D |
| R-01 / OQ-4 / NFR-2 | No se promete velocidad, tasa ni tiempo restante | Hipótesis para Fase D |
| R-02 | Persistencia y reanudación exigidas como comportamiento | Validación técnica para Fase D/F |
| R-03 | Dirección manual y reconexión degradada definidas | Validación técnica para Fase D |
| R-05 | Restricciones de licencia como condición de publicación | Restricción cerrada; mecanismo en Fase D |

## Restricciones de alcance preservadas

- Sin autenticación o autorización de clientes.
- Instalación únicamente portable.
- Sin gestión centralizada de sesiones, launcher externo ni conexión directa del juego a servidores.
- Sin soporte multiplataforma adicional en el MVP.
- Sin actualización diferencial avanzada.
- Sin salida de Fase C: no se generan arquitectura, ADR, backlog, historias, scaffold ni código.

## Elementos cualitativos omitidos

No se trasladan al contrato de UX las afirmaciones de marketing ovisionarias del Brief sobre convertirse en la forma estándar de preparar una LAN. Se conserva la útil: reducir la logística manual, devolver tiempo al jugador y mantener el estado visible. La estructura visual, la microcopia y los flujos sí materializan esa promesa.
