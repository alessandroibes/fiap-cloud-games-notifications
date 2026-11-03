param(
    [string]$UsersEnv = (Join-Path $PSScriptRoot '../../fiap-cloud-games-users/.env'),
    [string]$UsersUrl = 'http://127.0.0.1:5081',
    [string]$CatalogUrl = 'http://127.0.0.1:5082',
    [string]$NotificationsContainer = 'fcg-notifications-local-notifications-1'
)
$ErrorActionPreference = 'Stop'
$started = [DateTime]::UtcNow.ToString('o')
$settings = @{}
Get-Content -LiteralPath $UsersEnv | Where-Object { $_ -and -not $_.StartsWith('#') } | ForEach-Object { $pair=$_.Split('=',2); $settings[$pair[0]]=$pair[1] }
$admin = Invoke-RestMethod "$UsersUrl/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{email=$settings['AdminBootstrap__Email'];password=$settings['AdminBootstrap__Password']} | ConvertTo-Json)
$adminHeaders = @{Authorization="Bearer $($admin.accessToken)"}
$suffix = [Guid]::NewGuid().ToString('N')
$email = "g5-$suffix@example.com"
$password = 'Aa1!' + [Guid]::NewGuid().ToString('N')
$response = Invoke-WebRequest "$UsersUrl/api/users" -Method Post -ContentType 'application/json' -Body (@{name='Demonstracao G5';email=$email;password=$password} | ConvertTo-Json) -UseBasicParsing
if ($response.StatusCode -ne 201) { throw 'Cadastro não retornou 201.' }
$user = $response.Content | ConvertFrom-Json
$login = Invoke-RestMethod "$UsersUrl/api/auth/login" -Method Post -ContentType 'application/json' -Body (@{email=$email;password=$password} | ConvertTo-Json)
$headers = @{Authorization="Bearer $($login.accessToken)"}
$gameResponse = Invoke-WebRequest "$CatalogUrl/api/games" -Headers $adminHeaders -Method Post -ContentType 'application/json' -Body (@{name="Demonstracao G5 $suffix";price=19.90} | ConvertTo-Json) -UseBasicParsing
if ($gameResponse.StatusCode -ne 201) { throw 'Criação de jogo falhou.' }
$game = $gameResponse.Content | ConvertFrom-Json
$orderResponse = Invoke-WebRequest "$CatalogUrl/api/orders" -Headers $headers -Method Post -ContentType 'application/json' -Body (@{gameId=$game.id} | ConvertTo-Json) -UseBasicParsing
$order = $orderResponse.Content | ConvertFrom-Json
if ($orderResponse.StatusCode -ne 202 -or $order.status -ne 'Pending') { throw 'Compra não retornou 202/Pending.' }
$deadline = [DateTime]::UtcNow.AddSeconds(60)
do {
    $detail = Invoke-RestMethod "$CatalogUrl/api/orders/$($order.orderId)" -Headers $headers
    if ($detail.status -eq 'Approved') { break }
    if ($detail.status -eq 'Rejected') { throw 'Demonstração exige Payments em Approved; pedido foi rejeitado corretamente.' }
    Start-Sleep -Milliseconds 300
} while ([DateTime]::UtcNow -lt $deadline)
if ($detail.status -ne 'Approved') { throw 'Pagamento não concluiu no prazo.' }
$library = Invoke-RestMethod "$CatalogUrl/api/library" -Headers $headers
if (@($library | Where-Object { $_.gameId -eq $game.id }).Count -ne 1) { throw 'Biblioteca não recebeu uma entrada.' }
$deadline = [DateTime]::UtcNow.AddSeconds(60)
do {
    $lines = docker logs --since $started $NotificationsContainer
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao ler logs locais de Notifications.' }
    $records = @($lines | ForEach-Object { try { $record = $_ | ConvertFrom-Json; if ($record.notificationId) { $record } } catch { } })
    $welcome = @($records | Where-Object { $_.kind -eq 'Welcome' -and $_.userId -eq $user.id })
    $purchase = @($records | Where-Object { $_.kind -eq 'PurchaseConfirmation' -and $_.subjectId -eq $order.orderId })
    if ($welcome.Count -gt 0 -and $purchase.Count -gt 0) { break }
    Start-Sleep -Milliseconds 500
} while ([DateTime]::UtcNow -lt $deadline)
if ($welcome.Count -eq 0 -or $purchase.Count -eq 0) { throw 'Logs esperados não encontrados.' }
if (@($welcome.notificationId | Select-Object -Unique).Count -ne 1 -or @($purchase.notificationId | Select-Object -Unique).Count -ne 1) { throw 'Mais de uma identidade lógica encontrada.' }
[pscustomobject]@{UserId=$user.id;OrderId=$order.orderId;Status=$detail.status;WelcomeNotificationId=$welcome[0].notificationId;PurchaseNotificationId=$purchase[0].notificationId;WelcomeLogs=$welcome.Count;PurchaseLogs=$purchase.Count;LibraryGranted=$true} | ConvertTo-Json
