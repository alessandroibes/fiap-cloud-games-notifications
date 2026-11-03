param([string]$InfrastructureEnv = (Join-Path $PSScriptRoot '../../fiap-cloud-games-infrastructure/.env'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root '.env'
if (Test-Path -LiteralPath $target) { Write-Output '.env existente preservado.'; exit 0 }
if (-not (Test-Path -LiteralPath $InfrastructureEnv)) { throw 'Informe o .env local da Infraestrutura.' }
$infra = @{}
Get-Content -LiteralPath $InfrastructureEnv | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { $parts = $_.Split('=', 2); $infra[$parts[0]] = $parts[1] }
foreach ($required in @('NOTIFICATIONS_DB_PASSWORD','POSTGRES_PORT','RABBITMQ_PORT','RABBITMQ_PASSWORD')) { if (-not $infra[$required]) { throw "Configuração obrigatória ausente: $required" } }
$random = [Security.Cryptography.RandomNumberGenerator]::Create()
function New-LocalSecret { $bytes = New-Object byte[] 32; $random.GetBytes($bytes); [BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant() }
try {
    $lines = @(
        "ConnectionStrings__Notifications=Host=127.0.0.1;Port=$($infra.POSTGRES_PORT);Database=fcg_notifications;Username=fcg_notifications;Password=$($infra.NOTIFICATIONS_DB_PASSWORD)",
        "NOTIFICATIONS_DOCKER_CONNECTION=Host=postgres;Port=5432;Database=fcg_notifications;Username=fcg_notifications;Password=$($infra.NOTIFICATIONS_DB_PASSWORD)",
        'RabbitMq__Host=127.0.0.1', "RabbitMq__Port=$($infra.RABBITMQ_PORT)", 'RabbitMq__VirtualHost=fcg', 'RabbitMq__Username=fcg_local', "RabbitMq__Password=$($infra.RABBITMQ_PASSWORD)",
        "TEST_DB_PASSWORD=$(New-LocalSecret)", "TEST_RABBIT_PASSWORD=$(New-LocalSecret)", 'TEST_DB_PORT=55436', 'TEST_RABBIT_PORT=5676',
        'ASPNETCORE_URLS=http://127.0.0.1:5084'
    )
    [IO.File]::WriteAllLines($target, $lines, (New-Object Text.UTF8Encoding($false)))
} finally { $random.Dispose() }
Write-Output '.env local criado sem imprimir credenciais. Não versionar.'
