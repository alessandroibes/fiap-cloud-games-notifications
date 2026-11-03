# fiap-cloud-games-notifications

Boas-vindas e confirmação Approved no console, com deduplicação lógica durável.

## Estado

G5 implementado em .NET 8, PostgreSQL/EF Core e MassTransit/RabbitMQ: boas-vindas, confirmação somente após Approved, deduplicação durável e emissão recuperável no console. Suíte local: 47 testes aprovados. [Evidências e limites](docs/quality/g5-validation.md).

Compose centralizado pertence ao G6; manifests /k8s e Docker Desktop Kubernetes ao G7. Não há envio de e-mail real.

## Documentação

- [ADRs aceitos](docs/architecture/decisions/README.md).
- [Decisões funcionais confirmadas](docs/architecture/functional-decisions.md).
- [Especificação de referência](docs/specification.md).
- [Operação, configuração e recuperação](docs/operations.md).
- [UserCreatedEvent V1](docs/contracts/v1/user-created-v1.md) e [PaymentProcessedEvent V1](docs/contracts/v1/payment-processed-v1.md).
- Contratos canônicos e roadmap: repositório fiap-cloud-games-infrastructure, docs/contracts/v1/ e docs/implementation/roadmap.md. Tipos C# locais, sem dependência de projetos irmãos.

## Execução e configuração

Com infraestrutura G1 em execução, na raiz deste repositório:

```powershell
./scripts/Initialize-Local.ps1
docker compose build notifications
docker compose run --rm notifications --migrate
docker compose up -d notifications
```

Saúde em http://localhost:5084/health/live e /health/ready. Não há endpoint público de envio/consulta. Configuração em .env ignorado, com credencial exclusiva fcg_notifications. Startup não aplica migrations automaticamente.

```powershell
./scripts/Validate-G5.ps1
```

Testes usam PostgreSQL/RabbitMQ exclusivos, portas 55436/5676, incluindo interrupção do broker de testes. Não executar suítes simultâneas. Com os quatro serviços reais, `scripts/Validate-LocalFlow.ps1` demonstra cadastro, compra aprovada e os dois logs; consulte as condições em [operação](docs/operations.md).

## Garantias e organização

Cada evento é persistido com receipt e identidade de negócio. Uma notificação Welcome por usuário e PurchaseConfirmation por pedido aprovado. Rejected é registrado para deduplicação/conflitos, sem gerar confirmação. Um worker escreve JSON no stdout, faz flush e marca Emitted. Falha entre escrita e commit pode repetir o log físico com o mesmo NotificationId; não duplica a notificação lógica.

Falhas de consumo têm retentativas limitadas e filas _error. Falhas registradas de emissão passam a Failed após quatro tentativas; `docker compose run --rm notifications --retry-failed` recupera essas linhas explicitamente após corrigir a causa.

- Domain: identidade da notificação, estados e política de tentativas.
- Application: validação de eventos, fingerprints e portas de persistência/emissão.
- Infrastructure: Inbox lógica PostgreSQL, consumers, worker e sink de console.
- Api: health checks e comandos explícitos de migration/recuperação.
- Contracts e tests: V1 local, assembly independente e integração real com falhas controladas.

## Git e segurança

Clone independente fornecido pelo responsável; manter o origin existente. Commits separados, sem push ou publicação de imagens sem autorização. Documentação técnica é versionada. AGENTS.md, specs/, .codex/ e docs/course/ são locais e ignorados; segredos fora do Git.
