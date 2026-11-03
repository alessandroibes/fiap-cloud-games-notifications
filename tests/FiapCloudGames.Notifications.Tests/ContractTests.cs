extern alias Consumer;
using System.Text.Json;
using FiapCloudGames.Contracts.V1;
using MassTransit;
using Xunit;
using RemoteUser = Consumer::FiapCloudGames.Contracts.V1.UserCreatedEvent;
using RemotePayment = Consumer::FiapCloudGames.Contracts.V1.PaymentProcessedEvent;

namespace FiapCloudGames.Notifications.Tests;

public sealed class ContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public void UserMatchesIndependentProducerAndCanonicalFields()
    {
        var remote = new RemoteUser { EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, UserId = Guid.NewGuid(), Email = "user@example.com", Name = "Nome" };
        var text = JsonSerializer.Serialize(remote, Json); var local = JsonSerializer.Deserialize<UserCreatedEvent>(text, Json)!;
        Assert.Equal(remote.UserId, local.UserId); Assert.Equal(remote.Email, local.Email);
        Assert.Equal(MessageUrn.ForTypeString<RemoteUser>(), MessageUrn.ForTypeString<UserCreatedEvent>());
        Assert.Equal("urn:message:FiapCloudGames.Contracts.V1:UserCreatedEvent", MessageUrn.ForTypeString<UserCreatedEvent>());
        Schema(text, "user-created-v1.md", ["eventId", "occurredAt", "userId", "email", "name"]);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UserCreatedEvent>("{}", Json));
    }
    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public void PaymentMatchesIndependentProducerAndCanonicalFields(string status)
    {
        var remote = new RemotePayment { EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid(), PaymentId = Guid.NewGuid(), UserId = Guid.NewGuid(), GameId = Guid.NewGuid(), Status = status, Price = 12.34m, Currency = "BRL" };
        var text = JsonSerializer.Serialize(remote, Json); var local = JsonSerializer.Deserialize<PaymentProcessedEvent>(text, Json)!;
        Assert.Equal(remote.PaymentId, local.PaymentId); Assert.Equal(remote.Price, local.Price); Assert.Equal(status, local.Status);
        Assert.Equal(MessageUrn.ForTypeString<RemotePayment>(), MessageUrn.ForTypeString<PaymentProcessedEvent>());
        Assert.Equal("urn:message:FiapCloudGames.Contracts.V1:PaymentProcessedEvent", MessageUrn.ForTypeString<PaymentProcessedEvent>());
        Schema(text, "payment-processed-v1.md", ["eventId", "occurredAt", "orderId", "paymentId", "userId", "gameId", "status", "price", "currency"]);
        using var doc = JsonDocument.Parse(text); Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("price").ValueKind);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PaymentProcessedEvent>("{}", Json));
    }
    private static void Schema(string text, string doc, string[] fields)
    {
        using var json = JsonDocument.Parse(text); Assert.Equal(fields.Order(), json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(TimeSpan.Zero, json.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Offset);
        var markdown = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, doc));
        foreach (var field in fields) Assert.Contains($"`{char.ToUpperInvariant(field[0])}{field[1..]}`", markdown);
    }
}
