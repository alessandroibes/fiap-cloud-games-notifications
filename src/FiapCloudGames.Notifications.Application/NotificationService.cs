using System.Globalization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FiapCloudGames.Notifications.Domain;

namespace FiapCloudGames.Notifications.Application;

public sealed class InvalidEventException(string message) : Exception(message);
public record UserNotice(Guid EventId, DateTimeOffset OccurredAt, Guid UserId, string Email, string Name);
public record PaymentNotice(Guid EventId, DateTimeOffset OccurredAt, Guid OrderId, Guid PaymentId, Guid UserId, Guid GameId, string Status, decimal Price, string Currency);
public record PreparedNotice(Guid EventId, NotificationKind Kind, Guid SubjectId, Guid UserId, Guid? PaymentId, Guid? GameId, string EventFingerprint, string SubjectFingerprint, bool Emit);
public interface INotificationStore { Task Record(PreparedNotice notice, CancellationToken ct); }
public interface INotificationSink { Task Emit(Notification notification, CancellationToken ct); }

public sealed class NotificationService(INotificationStore store)
{
    private static void ValidateHeader(Guid id, DateTimeOffset date, Guid user)
    {
        if (id == Guid.Empty || user == Guid.Empty || date == default || date.Offset != TimeSpan.Zero)
            throw new InvalidEventException("Identificadores ou data UTC inválidos.");
    }
    private static string Hash(object data) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data))));
    public Task UserCreated(UserNotice n, CancellationToken ct)
    {
        ValidateHeader(n.EventId, n.OccurredAt, n.UserId);
        if (string.IsNullOrWhiteSpace(n.Name) || string.IsNullOrWhiteSpace(n.Email) || !MailAddress.TryCreate(n.Email, out var email) || email.Address != n.Email)
            throw new InvalidEventException("Nome ou e-mail inválidos.");
        var business = Hash(new { n.UserId, n.Email, n.Name });
        return store.Record(new(n.EventId, NotificationKind.Welcome, n.UserId, n.UserId, null, null, Hash(new { n.EventId, n.OccurredAt, business }), business, true), ct);
    }
    public Task PaymentProcessed(PaymentNotice n, CancellationToken ct)
    {
        ValidateHeader(n.EventId, n.OccurredAt, n.UserId);
        if (n.OrderId == Guid.Empty || n.PaymentId == Guid.Empty || n.GameId == Guid.Empty || n.Status is not ("Approved" or "Rejected") || n.Currency != "BRL" || n.Price < 0 || n.Price > 9999999999999999.99m || decimal.Round(n.Price, 2) != n.Price)
            throw new InvalidEventException("Dados do pagamento inválidos.");
        var business = Hash(new { n.OrderId, n.PaymentId, n.UserId, n.GameId, n.Status, Price = n.Price.ToString("0.00", CultureInfo.InvariantCulture), n.Currency });
        return store.Record(new(n.EventId, NotificationKind.PurchaseConfirmation, n.OrderId, n.UserId, n.PaymentId, n.GameId, Hash(new { n.EventId, n.OccurredAt, business }), business, n.Status == "Approved"), ct);
    }
}
