using FiapCloudGames.Notifications.Application;
using FiapCloudGames.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FiapCloudGames.Notifications.Infrastructure;

public sealed class NotificationStore(NotificationsDbContext db, TimeProvider clock) : INotificationStore
{
    public async Task Record(PreparedNotice n, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"event:" + n.EventId}, 0))", ct);
        var receipt = await db.EventReceipts.FindAsync([n.EventId], ct);
        if (receipt is not null)
        {
            if (receipt.Kind != n.Kind || receipt.SubjectId != n.SubjectId || receipt.Fingerprint != n.EventFingerprint) throw new InvalidEventException("EventId reutilizado com conteúdo divergente.");
            return;
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({n.Kind + ":" + n.SubjectId}, 0))", ct);
        var subject = await db.Subjects.FindAsync([n.Kind, n.SubjectId], ct);
        if (subject is not null && subject.Fingerprint != n.SubjectFingerprint) throw new InvalidEventException("Resultado divergente para identidade já registrada.");
        if (subject is null)
        {
            db.Subjects.Add(new NotificationSubject { Kind = n.Kind, SubjectId = n.SubjectId, PaymentId = n.PaymentId, Fingerprint = n.SubjectFingerprint, ShouldNotify = n.Emit });
            if (n.Emit) db.Notifications.Add(Notification.Create(n.Kind, n.SubjectId, n.UserId, n.EventId, n.PaymentId, n.GameId, clock.GetUtcNow()));
        }
        db.EventReceipts.Add(new EventReceipt { EventId = n.EventId, Kind = n.Kind, SubjectId = n.SubjectId, Fingerprint = n.EventFingerprint, ReceivedAt = clock.GetUtcNow() });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { throw new InvalidEventException("Identidade de pagamento/notificação já utilizada."); }
    }
}
