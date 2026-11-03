using System.Text.Json;
using FiapCloudGames.Notifications.Domain;
using FiapCloudGames.Notifications.Infrastructure;
using Xunit;

namespace FiapCloudGames.Notifications.Tests;

public sealed class ObservedWriter : StringWriter
{
    public bool Flushed { get; private set; }
    public override Task FlushAsync(CancellationToken ct) { Flushed = true; return base.FlushAsync(ct); }
}
public sealed class ConsoleSinkTests
{
    [Theory]
    [InlineData(NotificationKind.Welcome, "Boas-vindas simuladas")]
    [InlineData(NotificationKind.PurchaseConfirmation, "Confirmação de compra simulada")]
    public async Task WritesStructuredIdentifiersAndFlushesBeforeCompletion(NotificationKind kind, string message)
    {
        using var writer = new ObservedWriter();
        var n = Notification.Create(kind, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), kind == NotificationKind.Welcome ? null : Guid.NewGuid(), kind == NotificationKind.Welcome ? null : Guid.NewGuid(), DateTimeOffset.UtcNow);
        await new ConsoleNotificationSink(writer, TimeProvider.System).Emit(n, default);
        Assert.True(writer.Flushed); using var json = JsonDocument.Parse(writer.ToString()); var root = json.RootElement;
        Assert.Equal(message, root.GetProperty("message").GetString()); Assert.Equal(n.Id, root.GetProperty("notificationId").GetGuid());
        Assert.Equal(n.UserId, root.GetProperty("userId").GetGuid()); Assert.Equal(n.SourceEventId, root.GetProperty("sourceEventId").GetGuid());
        Assert.False(root.TryGetProperty("email", out _)); Assert.False(root.TryGetProperty("name", out _)); Assert.False(root.TryGetProperty("token", out _));
    }
}
