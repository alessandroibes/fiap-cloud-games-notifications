# Evidências do G5 — NotificationsAPI

Validação local em 02/11/2026, Windows/PowerShell, SDK .NET 8 e Docker Desktop Linux. Código/documentação alterados somente neste repositório. Repositórios irmãos e Fase 1 permaneceram sem alterações de arquivos. O roteiro integrado criou dados demonstrativos via APIs/eventos dos serviços reais, sem dependência entre bancos pela aplicação.

## Comandos e resultados

| Verificação | Resultado observado |
|---|---|
| dotnet restore FiapCloudGames.Notifications.sln --locked-mode | Sucesso sem avisos |
| dotnet build FiapCloudGames.Notifications.sln --no-restore -m:1 | Sucesso, zero avisos e erros |
| dotnet test FiapCloudGames.Notifications.sln --no-build --logger 'trx;LogFileName=g5-tests.trx' | 47 executados, 47 aprovados, zero falhas/ignorados; execução final de 21:45:53 a 21:46:59, horário de Brasília |
| dotnet format whitespace FiapCloudGames.Notifications.sln --verify-no-changes --no-restore | Código de saída zero |
| dotnet ef migrations has-pending-model-changes, Infrastructure/Api, --no-build | Nenhuma alteração pendente no modelo |
| docker compose config --quiet e docker compose build notifications | Sucesso; restore locked e publish Release no Dockerfile |
| docker compose run --rm notifications --migrate, duas execuções | Ambas concluídas; uma migration em fcg_notifications |
| docker compose run --rm notifications --retry-failed | Comando concluído; zero linhas Failed no ambiente naquele momento |
| Container Notifications | Em execução com UID 1654; porta 5084 somente no localhost |
| GET /health/live e /health/ready | HTTP 200 |
| scripts/Validate-LocalFlow.ps1 | Cadastro/login/compra reais; pagamento Approved, biblioteca concedida e logs Welcome/PurchaseConfirmation encontrados |
| Estado persistido local | Duas Welcome e quatro PurchaseConfirmation, todas Emitted; sete EventReceipts |
| Rejected | Um subject com ShouldNotify=false; zero notificações associadas e zero logs para o pedido rejeitado verificado |
| Filas | notifications-user-created-v1 e notifications-payment-processed-v1 vazias, cada uma com um consumer; filas de Catalog/Payments também vazias |

Imagem validada: `sha256:1f2e08b625441d79bd374ec13edea99d0254d46f16532b44a46a4acef61c59ed`. Build local, sem publicação em registry. Contagens refletem a captura e podem mudar com uso posterior.

## Fluxo com os quatro serviços reais

O roteiro cadastrou e autenticou um usuário em Users, criou um jogo com token administrativo, iniciou compra com o JWT do novo usuário, aguardou Payments/Catalog e encontrou os logs de Notifications:

| Identificador | Valor |
|---|---|
| UserId | ed466c16-27c0-499b-a77d-3cae81c1cc0b |
| OrderId aprovado | 097eaf0d-9f65-4d3b-8e3f-ce39afd4480b |
| NotificationId Welcome | 89ca478f-8b61-4ec7-bca3-f389b4ff8b28 |
| NotificationId PurchaseConfirmation | 124ecff0-55a7-40b1-9c34-8550c8d5df2a |

Foi encontrado um log físico de cada tipo nessa execução e uma entrada na biblioteca. Não foram registrados na evidência e-mail, senha ou JWT.

Os eventos retidos desde G2/G4 foram consumidos na primeira implantação de Notifications, explicando as demais linhas. O pedido rejeitado `91e61bc0-6101-47d4-999c-0db543489655` permaneceu sem confirmação, embora seu evento tenha sido recebido e registrado. A existência do receipt não representa notificação enviada.

## Cobertura automatizada

- Domínio/aplicação: estados, limites de tentativas, recuperação preservando identidade, validação de GUIDs/UTC/status/BRL/preço/nome/e-mail e geração exclusiva de confirmação para Approved.
- Contratos V1: campos obrigatórios/canônicos, JSON camelCase, decimal numérico, UTC, identidade MassTransit e assembly independente de produtores; entrega real pelo RabbitMQ.
- Boas-vindas persistidas; reentrega comum com mesmo MessageId e novo EventId equivalente não repete log.
- Duplicatas concorrentes de pagamento com MessageIds/EventIds diferentes geram uma notificação lógica; dispatchers concorrentes em contextos separados não duplicam emissão normal.
- Catalog recebe cópia independente do mesmo PaymentProcessedEvent; Notifications não disputa sua fila.
- Rejected não cria notificação. Resultado terminal conflitante, EventId divergente e PaymentId reutilizado em outro pedido seguem para _error sem mudar o resultado anterior.
- Pedidos distintos do mesmo usuário geram confirmações distintas.
- Eventos inválidos chegam à fila de erro sem receipt/notificação parcial.
- Falha antes do commit reverte receipt/subject/notificação; retentativa transitória recupera. Quatro tentativas esgotadas deixam estado ausente e permitem replay após corrigir a causa.
- Falha após escrever o log e antes de registrar Emitted deixa Pending; reinício e nova tentativa geram dois logs físicos com o mesmo NotificationId, mas uma única linha lógica. Reentrega posterior não gera terceiro log.
- Falhas persistentes antes da escrita atingem Failed após quatro tentativas registradas; recuperação explícita mantém a identidade e emite uma vez.
- Pending sobrevive ao reinício; worker real conclui a emissão com broker indisponível, pois o registro já está no PostgreSQL.
- Índice único protege (Kind, SubjectId) mesmo sem lógica da aplicação; modelo não possui campos de nome/e-mail/senha em texto claro.
- Console sink escreve JSON com identificadores e faz flush antes de concluir; não inclui nome/e-mail/token.
- HTTP sem endpoint público de notificações; startup com schema vazio não aplica migrations, mantendo liveness 200 e readiness 503.

## Evolução da validação

A primeira suíte aprovou 45 testes. A emissão foi ajustada para escrita JSON direta e flush no stdout antes de confirmar Emitted, evitando depender da fila assíncrona do ConsoleLogger. Foram acrescentados dois testes de formato/flush, e a suíte final aprovou os 47 casos. Logs operacionais continuam estruturados pelo logger do ASP.NET.

## Reprodução e limites

Executar Initialize-Local.ps1 e Validate-G5.ps1 conforme [operação](../operations.md). TRX fica em TestResults ignorado; não versionar segredos. Testes usam banco/broker exclusivos e injetam falhas em fixtures. O comando de recuperação com linhas Failed foi exercitado pelo dispatcher em integração; no container local, a execução CLI encontrou zero Failed.

Validate-LocalFlow.ps1 utiliza os quatro serviços locais e cria dados demonstrativos, sem removê-los ao terminar. A captura demonstra fluxo integrado com Composes individuais; não substitui a entrega do Compose centralizado G6. Kubernetes permanece no G7; testes de carga e múltiplas réplicas em containers não foram executados.

A garantia é deduplicação lógica e emissão recuperável. Não há atomicidade entre stdout e PostgreSQL nem garantia de retenção dos logs pelo host; falha entre escrita e confirmação pode repetir fisicamente o mesmo NotificationId, conforme decisão funcional aprovada.
