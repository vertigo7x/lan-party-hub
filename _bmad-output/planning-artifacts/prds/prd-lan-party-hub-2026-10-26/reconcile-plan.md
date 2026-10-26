# Reconciliación de entrada — docs/plan.md

## Resumen

- **Entrada:** `docs/plan.md`
- **Artefactos comparados:** `prd.md` y `.memlog.md`
- **Resultado:** 8 discrepancias, todas resueltas o preservadas como restricciones.

## Discrepancias

1. RF-01 se trazó a FR-1 y FR-2, separando administración de sincronización/búsqueda.
2. RF-02 se trazó a FR-3; mDNS permanece candidato y la dirección manual queda obligatoria.
3. RF-03 se trazó a FR-5 y FR-7; BitTorrent/MonoTorrent no se elevan a decisión.
4. RF-04 se trazó a FR-6 y FR-8; Installer/External quedan excluidos del MVP.
5. RF-05 se trazó a FR-9; launchers externos y conexión a servidores de juego quedan fuera.
6. RF-06 se trazó a FR-10 sin elegir SignalR como decisión definitiva.
7. RF-07 se trazó a FR-11 con actualizaciones diferenciales avanzadas fuera del MVP.
8. RF-08 modo gestionado queda excluido y explícitamente enlazado en la matriz.

## Suplementos no incorporados

Stack, límites de módulos, contratos, ADRs, estructura de solución y estrategia de pruebas se
conservan como dependencias de Fase D o posteriores. No se incorporaron al PRD para respetar la
separación entre requisitos de producto y decisiones técnicas.

## Nota de vigencia — Enmienda ENM-001-01

`docs/plan.md` no fija un contrato de autenticación ni de autorización de clientes, por lo que
esta reconciliación no requirió corrección de fondo: los ocho ítems de trazabilidad RF-01..RF-08
siguen vigentes tal como se describen arriba.

Lo que sí cambió con la enmienda es el destino de **RF-04** dentro del PRD, que no aparece en
esta tabla porque `docs/plan.md` lo expresa en el ámbito de integridad y autenticidad de
paquetes (FR-6, FR-8 en el PRD) y no en el de control de acceso de clientes. El contrato de
acceso vigente es LAN administrada con ejecutable distribuido por la organización, sin
autenticación ni autorización de clientes, con FR-4 sustituida por compatibilidad de versión.
