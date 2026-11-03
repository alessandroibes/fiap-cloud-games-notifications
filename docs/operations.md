# Operação local — NotificationsAPI

## Preparação e execução

Pré-requisitos: SDK .NET 8.0.414 ou patch compatível, Docker Desktop Linux e infraestrutura G1 em execução. Na raiz deste repositório:

```powershell
./scripts/Initialize-Local.ps1
docker compose config --quiet
docker compose build notifications
docker compose run --rm notifications --migrate
docker compose up -d notifications
```

Initialize-Local lê somente o .env local de Infraestrutura para preparar a credencial exclusiva fcg_notifications e a conexão RabbitMQ. Aceita -InfrastructureEnv para outro caminho. Gera segredos de testes, sem imprimir valores, e preserva .env existente. Nenhum projeto .NET referencia fontes ou bancos de serviços irmãos.

Compose usa a rede externa fiap-cloud-games_default criada no G1. Imagem local fiap-cloud-games-notifications:local, sem publicação. Dockerfile multi-stage, bases fixadas por digest e runtime não privilegiado. Porta 5084 publicada somente no localhost. `docker compose stop notifications` interrompe o serviço sem remover dados.

Alternativa com SDK:

```powershell
./scripts/Invoke-Notifications.ps1 -Action migrate
./scripts/Invoke-Notifications.ps1 -Action run
```

O script importa .env apenas no processo e restaura o ambiente ao terminar. Se necessário, usar -ExecutionPolicy Bypass somente no processo, sem alterar a política global.

## Configuração e HTTP

| Chave | Uso |
|---|---|
| ConnectionStrings__Notifications | PostgreSQL exclusivo fcg_notifications; localhost:55432 |
| NOTIFICATIONS_DOCKER_CONNECTION | Mesmo banco/credencial via postgres:5432 no Compose |
| RabbitMq__Host / Port / VirtualHost | Broker externo; no Compose rabbitmq:5672, vhost fcg |
| RabbitMq__Username / Password | Credenciais externas; não usar guest |
| TEST_DB_PASSWORD / TEST_RABBIT_PASSWORD | Credenciais exclusivas de testes |
| TEST_DB_PORT / TEST_RABBIT_PORT | 55436 / 5676 por padrão |
| ASPNETCORE_URLS | Endereço SDK; container usa 8080 |

HTTP expõe somente GET /health/live (200) e GET /health/ready (200 se banco/schema disponíveis, 503 caso contrário). Readiness não mede broker, backlog, worker ou notificações Failed. Não há endpoint público de envio, consulta ou replay. O serviço não recebe chave JWT; consumo é restrito pelas credenciais e permissões do broker. Nunca versionar .env, cookies ou tokens. `docker compose config` sem --quiet expande segredos; não compartilhar essa saída.

## Persistência, deduplicação e mensagens

NotificationsDbContext contém apenas persistência técnica: Subjects, EventReceipts e Notifications. Não possui usuários, catálogo ou pagamentos de negócio; não consulta bancos externos. GUIDs externos não têm FK entre serviços. Nomes/e-mails não são armazenados em texto claro; fingerprints SHA-256 participam da detecção de conflitos, não são um mecanismo de anonimização absoluta.

Migration InitialNotificationsInbox é própria do serviço. Executar --migrate antes da API; o comando encerra sem iniciar bus/worker. Startup normal não chama Migrate/EnsureCreated. Ferramenta dotnet-ef 9.0.10 fixada em .config/dotnet-tools.json; para futuras migrations, configurar ConnectionStrings__Notifications e usar Infrastructure como project/Api como startup-project. Revisar e testar antes de aplicar, sem reset/drop do banco.

| Entrada | Fila | Efeito técnico |
|---|---|---|
| UserCreatedEvent | notifications-user-created-v1 | Welcome, uma notificação por UserId |
| PaymentProcessedEvent Approved | notifications-payment-processed-v1 | PurchaseConfirmation, uma notificação por OrderId |
| PaymentProcessedEvent Rejected | notifications-payment-processed-v1 | Receipt/subject duráveis; nenhuma notificação |

Catalog recebe pagamentos em sua própria fila, sem competir com Notifications. Contratos V1 locais conforme docs/contracts/v1/. GUIDs, UTC, nome/e-mail válido, status literal, BRL e preço não negativo de duas casas são validados antes da persistência.

EventReceipts.EventId é único e registra fingerprint do evento. Subjects tem PK (Kind, SubjectId), fingerprint de negócio e PaymentId único quando presente. Notifications tem índice único (Kind, SubjectId). Advisory locks PostgreSQL por evento e identidade de negócio serializam mensagens concorrentes; receipt, subject e nova notificação Pending são confirmados na mesma transação antes do ACK.

Reentrega do mesmo EventId com dados iguais não cria outro registro. Outro EventId com a mesma identidade/snapshot adiciona receipt sem nova notificação. EventId divergente ou identidade já registrada com outro resultado/snapshot vai para erro, preservando o primeiro resultado. Registrar Rejected impede que um Approved posterior conflitante gere confirmação indevida. Para nova tentativa legítima, Catalog cria outro OrderId. Preços numericamente equivalentes, como 29.9 e 29.90, têm o mesmo fingerprint de negócio.

Erros permanentes não são retentados. Falhas transitórias de consumo têm três retentativas em 1, 5 e 15 segundos, totalizando quatro tentativas. Após esgotamento seguem para notifications-user-created-v1_error ou notifications-payment-processed-v1_error, sem consumer automático de produção. Investigar a causa e reenviar a mensagem original preservando EventId e dados; não alterar resultados para forçar confirmação. Receipts e subjects não são limpos automaticamente.

## Emissão recuperável no console

O worker consulta notificações Pending e elegíveis por NextAttemptAt. Usa transação e `FOR UPDATE SKIP LOCKED`, permitindo workers concorrentes sem emitir a mesma linha simultaneamente. A escrita de um registro JSON e o flush do stdout antecedem a atualização para Emitted. Não usa a fila assíncrona do ConsoleLogger para esse registro; logs operacionais continuam pelo logger JSON do ASP.NET.

O registro contém timestamp UTC, level, message, notificationId, kind, subjectId, userId, paymentId, gameId e sourceEventId. Texto: "Boas-vindas simuladas" ou "Confirmação de compra simulada". Não inclui nome, e-mail, senha, JWT ou string de conexão. Não há provedor de e-mail real.

Uma falha após a escrita e antes do commit deixa a notificação recuperável e pode repetir o log físico com o mesmo NotificationId. Não há transação atômica entre stdout e PostgreSQL. Emitted registra a conclusão observada pela aplicação; não garante retenção do log pelo Docker/host nem leitura por destinatário real. A garantia aprovada é uma notificação lógica durável por identidade, com emissão recuperável e possível repetição física.

Falhas de emissão/gravação capturadas são registradas após rollback, com novas tentativas em 1, 5 e 15 segundos. Na quarta falha registrada, a linha passa a Failed. Cancelamento/queda do processo ou indisponibilidade que impeça gravar o contador podem deixar Pending, com nova tentativa após recuperação. O contador representa falhas persistidas, não quantidade exata de escritas físicas. Falhas de acesso ao banco pelo worker são registradas por tipo técnico e consultadas novamente após um segundo.

Após corrigir a causa, recuperar explicitamente notificações Failed:

```powershell
docker compose run --rm notifications --retry-failed
```

Alternativa SDK: `./scripts/Invoke-Notifications.ps1 -Action retry-failed`. O comando recoloca somente Failed em Pending, zera o contador e preserva NotificationId, receipts e subjects. Não altera Emitted e não reenfileira eventos rejeitados. O worker em execução concluirá a emissão. Em cenários com falha após escrita, o replay pode repetir o log físico; isso não duplica a identidade lógica.

## Observação e testes

Consultar logs com `docker compose logs notifications` e acompanhar contagens por Kind/State em Notifications, NextAttemptAt, Failures e filas _error. O aumento de Failed exige investigação mesmo com readiness 200. Consultas de evidência devem usar identificadores/contagens, sem expor segredos.

```powershell
./scripts/Validate-G5.ps1
```

O script sobe fcg-notifications-g5-tests, restaura com lockfiles, compila e testa. PostgreSQL fcg_notifications_test e broker/vhost fcg_test são exclusivos, portas 55436/5676. Testes interrompem somente seu broker e injetam falhas em fixtures, sem alterar bancos do G1. Mantêm volumes/dados e usam GUIDs novos; não executar duas suítes simultâneas. A fixture consome suas filas _error para comprovar roteamento; produção não o faz.

A maioria dos testes drena o dispatcher explicitamente e usa relógio controlado para verificar prazos sem esperas artificiais longas. Um teste inicia o worker real, reinicia o host com broker indisponível e verifica recuperação. O sink de console tem testes próprios de formato e flush. `dotnet test FiapCloudGames.Notifications.sln --filter 'Category!=Integration'` executa domínio/contratos/sink sem containers. TRX permanece em TestResults ignorado.

Com os quatro serviços locais em execução e Payments em Approved:

```powershell
./scripts/Validate-LocalFlow.ps1
```

O roteiro autentica o administrador local de Users para criar um jogo, cadastra um usuário demonstrativo, autentica esse usuário e inicia a compra. Verifica aprovação/biblioteca e busca os logs Welcome/PurchaseConfirmation pelos identificadores técnicos. Não imprime nem grava senha/JWT. Os dados demonstrativos permanecem nos serviços; não há limpeza automática. É possível informar -UsersEnv, -UsersUrl, -CatalogUrl e -NotificationsContainer. Se o modo de Payments for Rejected, o roteiro falha explicitamente, sem forçar aprovação.

Compose centralizado dos quatro serviços pertence ao G6; /k8s e Docker Desktop Kubernetes ao G7. .NET 8, EF Core 9.0.10, Npgsql EF 9.0.4 e MassTransit 8.5.8 mantêm versões adotadas nos gates anteriores, fixadas em lockfiles. Notifications não publica eventos; sua Inbox lógica e fila local de emissão não exigem a Consumer Outbox de Payments.
