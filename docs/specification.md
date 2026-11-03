# SPEC-P2-NOTIFICATIONS — NotificationsAPI

**Estado:** G5 implementado; 47 testes locais aprovados. [Evidências](quality/g5-validation.md). Compose centralizado no G6 e Kubernetes no G7.

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
Confirmado em 30/10/2026: registro técnico durável e único por tipo/usuário ou tipo/pedido, com emissão recuperável. Implementado com EventReceipts, Subjects e Notifications. Worker escreve JSON e faz flush antes de marcar Emitted. Pode haver repetição física do log na janela de falha entre emissão e confirmação; nunca duplicar a notificação lógica. [Formato, retentativas, comandos de recuperação e limites](operations.md).

## Baseline obrigatória para implementação

As decisões dos ADR-P2-001 a ADR-P2-007 estão **Aceitas**. PostgreSQL implementado; Docker Desktop Kubernetes previsto no G7. Contratos V1 em `docs/contracts/v1/`.
