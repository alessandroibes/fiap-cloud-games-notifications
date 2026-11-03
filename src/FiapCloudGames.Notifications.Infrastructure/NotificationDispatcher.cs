using FiapCloudGames.Notifications.Application;
using FiapCloudGames.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FiapCloudGames.Notifications.Infrastructure;

public sealed class ConsoleNotificationSink(TextWriter output, TimeProvider clock) : INotificationSink
{
    public async Task Emit(Notification n, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var message = n.Kind == NotificationKind.Welcome ? "Boas-vindas simuladas" : "Confirmação de compra simulada";
        var json = JsonSerializer.Serialize(new
        {
            timestamp = clock.GetUtcNow(),
            level = "Information",
            message,
            notificationId = n.Id,
            kind = n.Kind.ToString(),
            subjectId = n.SubjectId,
            userId = n.UserId,
            paymentId = n.PaymentId,
            gameId = n.GameId,
            sourceEventId = n.SourceEventId
        });
        // Escrever antes de confirmar a emissão no banco, sem a fila assíncrona do ConsoleLogger.
        await output.WriteLineAsync(json.AsMemory(), ct);
        await output.FlushAsync(ct);
    }
}

public sealed class NotificationDispatcher(NotificationsDbContext db, INotificationSink sink, TimeProvider clock, ILogger<NotificationDispatcher> logger)
{
    public async Task<bool> DispatchOne(CancellationToken ct)
    {
        Guid id;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var now = clock.GetUtcNow();
            var items = await db.Notifications.FromSqlInterpolated($"SELECT * FROM \"Notifications\" WHERE \"State\" = 'Pending' AND \"NextAttemptAt\" <= {now} ORDER BY \"CreatedAt\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToListAsync(ct);
            var notice = items.SingleOrDefault();
            if (notice is null) return false;
            id = notice.Id;
            try
            {
                await sink.Emit(notice, ct);
                notice.MarkEmitted(clock.GetUtcNow());
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                logger.LogWarning("Falha de emissão/registro. NotificationId {NotificationId}, ErrorType {ErrorType}", id, error.GetType().Name);
            }
        }
        // Registro de falha separado, pois a tentativa anterior foi revertida.
        await using var retryTx = await db.Database.BeginTransactionAsync(ct);
        var current = await db.Notifications.FromSqlInterpolated($"SELECT * FROM \"Notifications\" WHERE \"Id\" = {id} FOR UPDATE").SingleAsync(ct);
        current.RecordFailure(clock.GetUtcNow());
        await db.SaveChangesAsync(ct); await retryTx.CommitAsync(ct);
        return true;
    }
    public Task<int> RetryFailed(CancellationToken ct) => db.Notifications.Where(n => n.State == DeliveryState.Failed)
        .ExecuteUpdateAsync(s => s.SetProperty(n => n.State, DeliveryState.Pending).SetProperty(n => n.Failures, 0).SetProperty(n => n.NextAttemptAt, clock.GetUtcNow()), ct);
}

public sealed class NotificationWorker(IServiceScopeFactory scopes, ILogger<NotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                if (await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchOne(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogWarning("Worker indisponível temporariamente. ErrorType {ErrorType}", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
