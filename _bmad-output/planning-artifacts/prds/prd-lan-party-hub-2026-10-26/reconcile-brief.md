# Reconciliación de entrada — Product Brief

## Resumen

- **Entrada:** Product Brief `brief-lan-party-hub-2026-10-26/brief.md`
- **Artefactos comparados:** `prd.md` y `.memlog.md`
- **Resultado:** 4 discrepancias, todas resueltas en el PRD o explícitamente limitadas al workflow.

## Discrepancias

1. **Estado del brief:** el brief pasó de `ready-for-review` a aprobación humana explícita del
   Checkpoint A. Resuelto en frontmatter y §Sobre este documento; registrado también en su memlog.
2. **R3:** el brief exigía resolver o asignar antes de Fase B. El PRD lo asigna a Fase D y conserva
   la obligatoriedad del descubrimiento automático y de la dirección manual.
3. **R4:** el brief exigía detalle funcional en Fase B. El PRD lo concreta como **compatibilidad
   de versión de clientes**: comportamiento observable y determinista para cliente compatible
   (sincroniza, descarga, instala y reporta estado) y para cliente incompatible (no completa
   sincronización, descarga ni instalación, y recibe un diagnóstico explícito con la versión
   requerida o disponible). El modelo operativo es LAN administrada con ejecutable distribuido
   por la organización: **no hay autenticación ni autorización de clientes**, por lo que no
   existen credenciales, alta, revocación ni expiración que decidir. La matriz de versiones
   compatibles y el mecanismo de negociación quedan para Fase D, sin que el PRD selecte
   tecnología.
4. **R5 y P2P:** el brief exigía responsable/fase sin fijarlos. El PRD mantiene ambos abiertos,
   conserva restricciones y asigna raíz/firma y PoC a Fase D; no propone mecanismo definitivo.

## Suplementos no incorporados

No se detectaron elementos cualitativos del brief que el PRD pierda sin duplicación: problema,
usuarios, visión, alcance, riesgos y criterios de éxito están trazados en §1, §2, §7-§12.

## Nota de vigencia — Enmienda ENM-001-01

La discrepancia 3 se reescribe para reflejar el contrato vigente del brief aprobado: R4 es
compatibilidad de versión de clientes sobre LAN administrada, no autenticación/autorización. El
brief aprobado ya no contiene preguntas de credenciales, alta, revocación ni expiración, por lo
que la discrepancia ya no es de asignación sino de concreción funcional en Fase B.

Consecuencia sobre la numeración: al eliminarse la pregunta de autenticación de clientes, las
preguntas abiertas y supuestos del PRD pasan de OQ-1..OQ-5 a **OQ-1..OQ-4** (R5 → OQ-1,
R3 → OQ-2, P2P → OQ-3, R-01/NFR-6 → OQ-4).
