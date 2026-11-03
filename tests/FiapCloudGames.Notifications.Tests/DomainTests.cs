using FiapCloudGames.Notifications.Application;
using FiapCloudGames.Notifications.Domain;
using Xunit;

namespace FiapCloudGames.Notifications.Tests;

public sealed class RecordingStore : INotificationStore
{
    public PreparedNotice? Last { get; private set; }
    public Task Record(PreparedNotice n, CancellationToken ct) { Last = n; return Task.CompletedTask; }
}
public sealed class DomainTests
{
    [Fact]
    public void EmissionFailuresAreLimitedAndRecoveryPreservesIdentity()
    {
        var now = DateTimeOffset.UtcNow;
        var n = Notification.Create(NotificationKind.Welcome, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, now);
        var id = n.Id;
        foreach (var delay in new[] { 1, 5, 15 }) { n.RecordFailure(now); Assert.Equal(now.AddSeconds(delay), n.NextAttemptAt); }
        n.RecordFailure(now); Assert.Equal(DeliveryState.Failed, n.State); Assert.Null(n.EmittedAt);
        n.Retry(now); Assert.Equal(DeliveryState.Pending, n.State); Assert.Equal(0, n.Failures); Assert.Equal(id, n.Id);
        n.MarkEmitted(now); n.Retry(now); n.RecordFailure(now); Assert.Equal(DeliveryState.Emitted, n.State); Assert.Equal(now, n.EmittedAt);
    }
    [Fact] public void ConfirmationRequiresPaymentAndGame() => Assert.Throws<ArgumentException>(() => Notification.Create(NotificationKind.PurchaseConfirmation, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, null, DateTimeOffset.UtcNow));
    [Theory]
    [InlineData("Approved", true)]
    [InlineData("Rejected", false)]
    public async Task OnlyApprovedRequestsEmission(string status, bool expected)
    {
        var store = new RecordingStore(); var service = new NotificationService(store);
        await service.PaymentProcessed(ValidPayment() with { Status = status }, default);
        Assert.Equal(expected, store.Last!.Emit);
    }
    private static PaymentNotice ValidPayment() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Approved", 10.50m, "BRL");
    [Theory]
    [InlineData("event")]
    [InlineData("user")]
    [InlineData("order")]
    [InlineData("payment")]
    [InlineData("game")]
    [InlineData("date")]
    [InlineData("utc")]
    [InlineData("status")]
    [InlineData("currency")]
    [InlineData("negative")]
    [InlineData("scale")]
    [InlineData("overflow")]
    public async Task InvalidPaymentCannotReachPersistence(string field)
    {
        var p = ValidPayment(); p = field switch
        {
            "event" => p with { EventId = Guid.Empty },
            "user" => p with { UserId = Guid.Empty },
            "order" => p with { OrderId = Guid.Empty },
            "payment" => p with { PaymentId = Guid.Empty },
            "game" => p with { GameId = Guid.Empty },
            "date" => p with { OccurredAt = default },
            "utc" => p with { OccurredAt = p.OccurredAt.ToOffset(TimeSpan.FromHours(-3)) },
            "status" => p with { Status = "approved" },
            "currency" => p with { Currency = "USD" },
            "negative" => p with { Price = -1 },
            "scale" => p with { Price = 1.001m },
            _ => p with { Price = 10000000000000000m }
        };
        var store = new RecordingStore(); await Assert.ThrowsAsync<InvalidEventException>(() => new NotificationService(store).PaymentProcessed(p, default)); Assert.Null(store.Last);
    }
    [Theory]
    [InlineData("", "user@example.com")]
    [InlineData("Nome", "")]
    [InlineData("Nome", "invalid")]
    [InlineData("Nome", "Display <user@example.com>")]
    public async Task InvalidWelcomeIsRejected(string name, string email)
    {
        var store = new RecordingStore(); await Assert.ThrowsAsync<InvalidEventException>(() => new NotificationService(store).UserCreated(new(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid(), email, name), default)); Assert.Null(store.Last);
    }
}
