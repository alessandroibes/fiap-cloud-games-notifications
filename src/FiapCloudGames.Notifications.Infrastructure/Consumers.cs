using FiapCloudGames.Contracts.V1;
using FiapCloudGames.Notifications.Application;
using MassTransit;

namespace FiapCloudGames.Notifications.Infrastructure;

public sealed class UserCreatedConsumer(NotificationService service) : IConsumer<UserCreatedEvent>
{
    public Task Consume(ConsumeContext<UserCreatedEvent> context)
    {
        var m = context.Message;
        return service.UserCreated(new(m.EventId, m.OccurredAt, m.UserId, m.Email, m.Name), context.CancellationToken);
    }
}
public sealed class PaymentProcessedConsumer(NotificationService service) : IConsumer<PaymentProcessedEvent>
{
    public Task Consume(ConsumeContext<PaymentProcessedEvent> context)
    {
        var m = context.Message;
        return service.PaymentProcessed(new(m.EventId, m.OccurredAt, m.OrderId, m.PaymentId, m.UserId, m.GameId, m.Status, m.Price, m.Currency), context.CancellationToken);
    }
}
