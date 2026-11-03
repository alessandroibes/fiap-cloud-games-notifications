# UserCreatedEvent — contrato V1 aceito

- **Produtor:** UsersAPI; **consumidores:** NotificationsAPI.
- **Namespace C#:** `FiapCloudGames.Contracts.V1`.
- **Fonte canônica após criação dos repositórios:** `fiap-cloud-games-infrastructure/docs/contracts/v1/`.

| Campo | Tipo C# | Obrigatório |
|---|---|---|
| `EventId` | `Guid` | Sim |
| `OccurredAt` | `DateTimeOffset UTC` | Sim |
| `UserId` | `Guid` | Sim |
| `Email` | `string` | Sim |
| `Name` | `string` | Sim |

## JSON ilustrativo
```json
{
  "eventId": "11111111-1111-4111-8111-111111111111",
  "occurredAt": "2026-10-29T18:00:00Z",
  "userId": "11111111-1111-4111-8111-111111111111",
  "email": "usuario@exemplo.com",
  "name": "Usuário Exemplo"
}
```

Regras: JSON camelCase; UTC ISO 8601. `EventId` identifica o evento lógico e `UserId` identifica o usuário. Este evento não possui preço, moeda, pedido ou status de pagamento. Testar compatibilidade de identidade MassTransit. Sem senha, hash ou JWT; limitar dados pessoais nos logs. O consumo simula boas-vindas.
