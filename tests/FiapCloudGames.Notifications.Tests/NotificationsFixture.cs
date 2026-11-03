extern alias Consumer;
using System.Collections.Concurrent;
using System.Diagnostics;
using FiapCloudGames.Notifications.Application;
using FiapCloudGames.Notifications.Domain;
using FiapCloudGames.Notifications.Infrastructure;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;
using UserEvent = Consumer::FiapCloudGames.Contracts.V1.UserCreatedEvent;
using PaymentEvent = Consumer::FiapCloudGames.Contracts.V1.PaymentProcessedEvent;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace FiapCloudGames.Notifications.Tests;

public sealed class TestClock : TimeProvider
{
    private long offset;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Interlocked.Read(ref offset));
    public void Advance(int seconds) => Interlocked.Add(ref offset, TimeSpan.FromSeconds(seconds).Ticks);
}
public record Emission(Guid Id, NotificationKind Kind, Guid SubjectId, Guid UserId);
public sealed class TestSink : INotificationSink
{
    public ConcurrentQueue<Emission> Emissions { get; } = new();
    public ConcurrentDictionary<Guid, int> FailBefore { get; } = new();
    public Task Emit(Notification n, CancellationToken ct)
    {
        if (FailBefore.ContainsKey(n.SubjectId) && FailBefore.AddOrUpdate(n.SubjectId, 0, (_, left) => left - 1) >= 0) throw new IOException("Falha controlada antes do log.");
        Emissions.Enqueue(new(n.Id, n.Kind, n.SubjectId, n.UserId)); return Task.CompletedTask;
    }
}
public sealed class FailurePlan : SaveChangesInterceptor
{
    public ConcurrentDictionary<Guid, int> BeforeCommit { get; } = new();
    public ConcurrentDictionary<Guid, int> Attempts { get; } = new();
    public ConcurrentDictionary<Guid, int> AfterEmission { get; } = new();
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    {
        foreach (var receipt in data.Context!.ChangeTracker.Entries<EventReceipt>().Where(e => e.State == EntityState.Added))
        {
            var id = receipt.Entity.EventId;
            if (BeforeCommit.ContainsKey(id))
            {
                Attempts.AddOrUpdate(id, 1, (_, count) => count + 1);
                if (BeforeCommit.AddOrUpdate(id, 0, (_, left) => left - 1) >= 0) throw new IOException("Falha controlada antes do commit do evento.");
            }
        }
        foreach (var n in data.Context.ChangeTracker.Entries<Notification>().Where(e => e.State == EntityState.Modified && e.Entity.State == DeliveryState.Emitted))
            if (AfterEmission.ContainsKey(n.Entity.SubjectId) && AfterEmission.AddOrUpdate(n.Entity.SubjectId, 0, (_, left) => left - 1) >= 0)
                throw new IOException("Falha controlada após log e antes de confirmar emissão.");
        return base.SavingChangesAsync(data, result, ct);
    }
}
public sealed class NotificationsFactory(Dictionary<string, string?> config, TestClock clock, TestSink sink, FailurePlan failures, bool worker = false) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing"); foreach (var pair in config) builder.UseSetting(pair.Key, pair.Value);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<INotificationSink>(); services.AddSingleton<INotificationSink>(sink);
            services.AddDbContext<NotificationsDbContext>(o => o.AddInterceptors(failures));
            if (!worker) foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(NotificationWorker)).ToArray()) services.Remove(descriptor);
        });
    }
}
public sealed class NotificationsFixture : IAsyncLifetime
{
    public string Root { get; }
    public Dictionary<string, string> Env { get; }
    public Dictionary<string, string?> Config { get; }
    public TestClock Clock { get; } = new();
    public TestSink Sink { get; } = new();
    public FailurePlan Failures { get; } = new();
    public NotificationsFactory Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    private IBusControl bus = null!;
    public ConcurrentDictionary<Guid, string> Errors { get; } = new();
    public ConcurrentQueue<PaymentEvent> CatalogCopies { get; } = new();
    public NotificationsFixture()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FiapCloudGames.Notifications.sln"))) directory = directory.Parent;
        Root = directory?.FullName ?? throw new InvalidOperationException("Raiz Notifications não encontrada.");
        Env = File.ReadAllLines(Path.Combine(Root, ".env")).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
        var connection = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = int.Parse(Env["TEST_DB_PORT"]), Database = "fcg_notifications_test", Username = "fcg_notifications_test", Password = Env["TEST_DB_PASSWORD"] }.ConnectionString;
        Config = new() { ["ConnectionStrings:Notifications"] = connection, ["RabbitMq:Host"] = "127.0.0.1", ["RabbitMq:Port"] = Env["TEST_RABBIT_PORT"], ["RabbitMq:VirtualHost"] = "fcg_test", ["RabbitMq:Username"] = "notifications_test", ["RabbitMq:Password"] = Env["TEST_RABBIT_PASSWORD"] };
    }
    public NotificationsDbContext Db() => new(new DbContextOptionsBuilder<NotificationsDbContext>().UseNpgsql(Config["ConnectionStrings:Notifications"]).Options);
    public static UserEvent User() => new() { EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, UserId = Guid.NewGuid(), Email = "user@example.com", Name = "Pessoa teste" };
    public static PaymentEvent Payment(string status = "Approved") => new() { EventId = Guid.NewGuid(), OccurredAt = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid(), PaymentId = Guid.NewGuid(), UserId = Guid.NewGuid(), GameId = Guid.NewGuid(), Status = status, Price = 29.90m, Currency = "BRL" };
    public async Task InitializeAsync()
    {
        await using (var db = Db()) await db.Database.MigrateAsync();
        bus = Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host("127.0.0.1", ushort.Parse(Env["TEST_RABBIT_PORT"]), "fcg_test", h => { h.Username("notifications_test"); h.Password(Env["TEST_RABBIT_PASSWORD"]); });
            cfg.ReceiveEndpoint("catalog-payment-processed-v1", e => e.Handler<PaymentEvent>(ctx => { CatalogCopies.Enqueue(ctx.Message); return Task.CompletedTask; }));
            cfg.ReceiveEndpoint("notifications-user-created-v1_error", e => { e.ConfigureConsumeTopology = false; e.Handler<UserEvent>(ctx => { Errors[ctx.Message.EventId] = ctx.ReceiveContext.TransportHeaders.Get<string>("MT-Fault-Message") ?? "Erro de consumer"; return Task.CompletedTask; }); });
            cfg.ReceiveEndpoint("notifications-payment-processed-v1_error", e => { e.ConfigureConsumeTopology = false; e.Handler<PaymentEvent>(ctx => { Errors[ctx.Message.EventId] = ctx.ReceiveContext.TransportHeaders.Get<string>("MT-Fault-Message") ?? "Erro de consumer"; return Task.CompletedTask; }); });
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); await bus.StartAsync(timeout.Token);
        StartApi(); await Factory.Services.GetRequiredService<IBusControl>().StartAsync(timeout.Token);
    }
    private void StartApi(bool worker = false) { Factory = new NotificationsFactory(Config, Clock, Sink, Failures, worker); Client = Factory.CreateClient(); }
    public async Task Restart(bool worker = false, bool waitForBroker = true)
    {
        Client.Dispose(); await Factory.DisposeAsync(); StartApi(worker);
        if (waitForBroker) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); await Factory.Services.GetRequiredService<IBusControl>().StartAsync(timeout.Token); }
    }
    public Task Publish<T>(T message, Guid? id = null) where T : class => bus.Publish(message, ctx => ctx.MessageId = id ?? Guid.NewGuid());
    public static async Task Wait(Func<Task<bool>> predicate, int seconds = 60)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)); while (!await predicate()) await Task.Delay(100, timeout.Token);
    }
    public Task Receipt(Guid id) => Wait(async () =>
    {
        if (Errors.TryGetValue(id, out var error)) throw new Xunit.Sdk.XunitException("Evento esperado foi para _error: " + error);
        await using var db = Db(); return await db.EventReceipts.AnyAsync(x => x.EventId == id);
    });
    public Task Error(Guid id) => Wait(() => Task.FromResult(Errors.ContainsKey(id)));
    public async Task Drain()
    {
        for (var i = 0; i < 200; i++)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            if (!await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchOne(default)) return;
        }
        throw new InvalidOperationException("Limite de drenagem de testes excedido.");
    }
    public async Task<int> RetryFailed()
    {
        await using var scope = Factory.Services.CreateAsyncScope(); return await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().RetryFailed(default);
    }
    public async Task Docker(params string[] command)
    {
        var start = new ProcessStartInfo("docker") { WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in new[] { "compose", "--env-file", ".env", "-f", "tests/compose.yaml" }.Concat(command)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(150)); await process.WaitForExitAsync(timeout.Token); await Task.WhenAll(stdout, stderr);
        Assert.True(process.ExitCode == 0, "Docker do ambiente Notifications de testes falhou; saída omitida para preservar credenciais.");
    }
    public async Task DisposeAsync()
    {
        Client?.Dispose(); if (Factory is not null) await Factory.DisposeAsync();
        if (bus is not null) { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await bus.StopAsync(timeout.Token); }
    }
}
