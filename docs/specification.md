# SPEC-P2-NOTIFICATIONS — NotificationsAPI

**Estado:** Especificação de planejamento, alinhada aos ADRs aceitos; implementação ainda não iniciada.

## Responsabilidade
Simular notificações por logs no console, sem integração com provedor real de e-mail.

## Fluxo e aceite
1. Consumir `UserCreatedEvent` e registrar simulação de boas-vindas.
2. Consumir `PaymentProcessedEvent` e registrar confirmação apenas para `Approved`.
3. Para `Rejected`, não registrar confirmação de compra.
4. Duplicatas não repetem a notificação lógica (controle idempotente conforme decisão de persistência).
5. Logs estruturados incluem identificadores de correlação, evitando senhas, tokens e exposição desnecessária de e-mails.
6. Testes cobrem ambos os eventos, aprovação/rejeição e duplicatas.

## Decisões confirmadas e detalhamento técnico
Confirmado em 30/10/2026: registro técnico durável e único por tipo/usuário ou tipo/pedido, com emissão recuperável. Pode haver repetição física do log na janela de falha entre emissão e confirmação; nunca duplicar a notificação lógica. Detalhar formato dos logs e configuração técnica.

## Baseline obrigatória para implementação

As decisões dos ADR-P2-001 a ADR-P2-007 estão **Aceitas**. Implementar com PostgreSQL e Docker Desktop Kubernetes; contratos V1 conforme `docs/contracts/`.
