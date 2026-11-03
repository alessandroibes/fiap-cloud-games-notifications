using FiapCloudGames.Notifications.Application;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FiapCloudGames.Notifications.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNotificationsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<NotificationsDbContext>(o => o.UseNpgsql(config.GetConnectionString("Notifications") ?? throw new InvalidOperationException("ConnectionStrings:Notifications obrigatório.")));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<TextWriter>(Console.Out);
        services.AddScoped<INotificationStore, NotificationStore>(); services.AddScoped<NotificationService>();
        services.AddScoped<INotificationSink, ConsoleNotificationSink>(); services.AddScoped<NotificationDispatcher>();
        services.AddHostedService<NotificationWorker>();
        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<UserCreatedConsumer>(); bus.AddConsumer<PaymentProcessedConsumer>();
            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(config["RabbitMq:Host"] ?? "localhost", config.GetValue<ushort>("RabbitMq:Port", 5672), config["RabbitMq:VirtualHost"] ?? "fcg", h =>
                {
                    h.Username(config["RabbitMq:Username"] ?? throw new InvalidOperationException("RabbitMq:Username obrigatório."));
                    h.Password(config["RabbitMq:Password"] ?? throw new InvalidOperationException("RabbitMq:Password obrigatório."));
                });
                cfg.ReceiveEndpoint("notifications-user-created-v1", e =>
                {
                    e.UseMessageRetry(r => { r.Ignore<InvalidEventException>(); r.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)); });
                    e.ConfigureConsumer<UserCreatedConsumer>(context);
                });
                cfg.ReceiveEndpoint("notifications-payment-processed-v1", e =>
                {
                    e.UseMessageRetry(r => { r.Ignore<InvalidEventException>(); r.Intervals(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)); });
                    e.ConfigureConsumer<PaymentProcessedConsumer>(context);
                });
            });
        });
        services.Configure<MassTransitHostOptions>(o => { o.WaitUntilStarted = false; o.StopTimeout = TimeSpan.FromSeconds(10); });
        return services;
    }
}
