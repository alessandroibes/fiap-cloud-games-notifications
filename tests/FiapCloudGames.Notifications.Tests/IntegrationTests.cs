extern alias Consumer;
using System.Net;
using FiapCloudGames.Notifications.Domain;
using FiapCloudGames.Notifications.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FiapCloudGames.Notifications.Tests;

[Trait("Category", "Integration")]
public sealed class IntegrationTests(NotificationsFixture f) : IClassFixture<NotificationsFixture>
{
    [Fact]
    public async Task WelcomeIsDurableAndCommonReplaysDoNotEmitAgain()
    {
        var user = NotificationsFixture.User(); var messageId = Guid.NewGuid();
        await f.Publish(user, messageId); await f.Receipt(user.EventId); await f.Drain();
        await f.Publish(user, messageId);
        var equivalent = user with { EventId = Guid.NewGuid() }; await f.Publish(equivalent); await f.Receipt(equivalent.EventId); await f.Drain();
        Assert.Single(f.Sink.Emissions, x => x.SubjectId == user.UserId);
        await using var db = f.Db(); var notification = Assert.Single(await db.Notifications.Where(x => x.SubjectId == user.UserId).ToListAsync());
        Assert.Equal(DeliveryState.Emitted, notification.State); Assert.NotNull(notification.EmittedAt); Assert.Equal(NotificationKind.Welcome, notification.Kind);
        Assert.Equal(2, await db.EventReceipts.CountAsync(x => x.SubjectId == user.UserId));
    }
    [Theory]
    [InlineData("Approved", 1)]
    [InlineData("Rejected", 0)]
    public async Task PaymentOnlyConfirmsApprovalAndCatalogHasIndependentCopy(string status, int expected)
    {
        var p = NotificationsFixture.Payment(status); await f.Publish(p); await f.Receipt(p.EventId); await f.Drain();
        await NotificationsFixture.Wait(() => Task.FromResult(f.CatalogCopies.Any(x => x.EventId == p.EventId)));
        Assert.Equal(expected, f.Sink.Emissions.Count(x => x.SubjectId == p.OrderId));
        await using var db = f.Db(); Assert.Equal(expected, await db.Notifications.CountAsync(x => x.SubjectId == p.OrderId));
        Assert.Equal(status == "Approved", (await db.Subjects.FindAsync(NotificationKind.PurchaseConfirmation, p.OrderId))!.ShouldNotify);
    }
    [Fact]
    public async Task ConcurrentDuplicatesAndDifferentEventIdsCreateOneLogicalNotification()
    {
        var p = NotificationsFixture.Payment(); var duplicates = Enumerable.Range(0, 6).Select(_ => p with { EventId = Guid.NewGuid() }).ToArray();
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => f.Publish(p)).Concat(duplicates.Select(x => f.Publish(x))));
        await f.Receipt(p.EventId); foreach (var n in duplicates) await f.Receipt(n.EventId);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = f.Factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchOne(default);
        }));
        await f.Drain(); Assert.Single(f.Sink.Emissions, x => x.SubjectId == p.OrderId);
        await using var db = f.Db(); Assert.Equal(1, await db.Notifications.CountAsync(x => x.SubjectId == p.OrderId));
        Assert.Equal(7, await db.EventReceipts.CountAsync(x => x.SubjectId == p.OrderId));
    }
    [Theory]
    [InlineData("Rejected", "Approved", 0)]
    [InlineData("Approved", "Rejected", 1)]
    public async Task ConflictingTerminalResultCannotChangeNotification(string first, string next, int expected)
    {
        var p = NotificationsFixture.Payment(first); await f.Publish(p); await f.Receipt(p.EventId);
        var conflict = p with { EventId = Guid.NewGuid(), Status = next }; await f.Publish(conflict); await f.Error(conflict.EventId); await f.Drain();
        Assert.Equal(expected, f.Sink.Emissions.Count(x => x.SubjectId == p.OrderId));
        await using var db = f.Db(); Assert.False(await db.EventReceipts.AnyAsync(x => x.EventId == conflict.EventId));
    }
    [Fact]
    public async Task EventIdAndPaymentIdCannotBeReusedForAnotherIdentity()
    {
        var p = NotificationsFixture.Payment(); await f.Publish(p); await f.Receipt(p.EventId);
        await f.Publish(p with { UserId = Guid.NewGuid() }); await f.Error(p.EventId);
        var reused = p with { EventId = Guid.NewGuid(), OrderId = Guid.NewGuid() }; await f.Publish(reused); await f.Error(reused.EventId);
        await f.Drain(); Assert.Single(f.Sink.Emissions, x => x.SubjectId == p.OrderId); Assert.DoesNotContain(f.Sink.Emissions, x => x.SubjectId == reused.OrderId);
    }
    [Fact]
    public async Task SeparateOrdersForSameUserHaveSeparateConfirmations()
    {
        var first = NotificationsFixture.Payment(); var second = NotificationsFixture.Payment() with { UserId = first.UserId };
        await f.Publish(first); await f.Publish(second); await f.Receipt(first.EventId); await f.Receipt(second.EventId); await f.Drain();
        Assert.Equal(2, f.Sink.Emissions.Count(x => x.UserId == first.UserId));
    }
    [Fact]
    public async Task InvalidWelcomeGoesToErrorWithoutNotification()
    {
        var n = NotificationsFixture.User() with { Email = "invalid" }; await f.Publish(n); await f.Error(n.EventId);
        await using var db = f.Db(); Assert.False(await db.EventReceipts.AnyAsync(x => x.EventId == n.EventId)); Assert.False(await db.Notifications.AnyAsync(x => x.SubjectId == n.UserId));
    }
    [Theory]
    [InlineData("status")]
    [InlineData("price")]
    [InlineData("currency")]
    [InlineData("id")]
    [InlineData("utc")]
    public async Task InvalidPaymentsGoToErrorWithoutConfirmation(string field)
    {
        var p = NotificationsFixture.Payment(); p = field switch
        {
            "status" => p with { Status = "approved" },
            "price" => p with { Price = 0.001m },
            "currency" => p with { Currency = "USD" },
            "id" => p with { PaymentId = Guid.Empty },
            _ => p with { OccurredAt = p.OccurredAt.ToOffset(TimeSpan.FromHours(-3)) }
        };
        await f.Publish(p); await f.Error(p.EventId); await using var db = f.Db(); Assert.False(await db.Notifications.AnyAsync(x => x.SubjectId == p.OrderId)); Assert.False(await db.EventReceipts.AnyAsync(x => x.EventId == p.EventId));
    }
    [Fact]
    public async Task TransientFailureBeforeCommitRetriesAtomically()
    {
        var n = NotificationsFixture.User(); f.Failures.BeforeCommit[n.EventId] = 1;
        await f.Publish(n); await f.Receipt(n.EventId); await f.Drain(); Assert.Equal(2, f.Failures.Attempts[n.EventId]);
        await using var db = f.Db(); Assert.Equal(1, await db.Notifications.CountAsync(x => x.SubjectId == n.UserId)); Assert.Single(f.Sink.Emissions, x => x.SubjectId == n.UserId);
    }
    [Fact]
    public async Task ExhaustedConsumerRetriesLeaveNoPartialRecordAndReplayRecovers()
    {
        var n = NotificationsFixture.User(); f.Failures.BeforeCommit[n.EventId] = 4; await f.Publish(n); await f.Error(n.EventId);
        Assert.Equal(4, f.Failures.Attempts[n.EventId]); await using (var db = f.Db())
        {
            Assert.False(await db.Notifications.AnyAsync(x => x.SubjectId == n.UserId)); Assert.False(await db.EventReceipts.AnyAsync(x => x.EventId == n.EventId)); Assert.False(await db.Subjects.AnyAsync(x => x.SubjectId == n.UserId));
        }
        f.Failures.BeforeCommit.TryRemove(n.EventId, out _); f.Errors.TryRemove(n.EventId, out _); await f.Publish(n); await f.Receipt(n.EventId); await f.Drain();
        Assert.Single(f.Sink.Emissions, x => x.SubjectId == n.UserId);
    }
    [Fact]
    public async Task FailureAfterPhysicalLogMayRepeatLogButNeverLogicalNotification()
    {
        var n = NotificationsFixture.User(); await f.Publish(n); await f.Receipt(n.EventId); f.Failures.AfterEmission[n.UserId] = 1;
        await f.Drain(); Assert.Single(f.Sink.Emissions, x => x.SubjectId == n.UserId);
        Guid logicalId;
        await using (var db = f.Db())
        {
            var row = await db.Notifications.SingleAsync(x => x.SubjectId == n.UserId); logicalId = row.Id;
            Assert.Equal(DeliveryState.Pending, row.State); Assert.Null(row.EmittedAt); Assert.Equal(1, row.Failures);
        }
        await f.Restart(); f.Clock.Advance(2); await f.Drain();
        Assert.Equal(2, f.Sink.Emissions.Count(x => x.SubjectId == n.UserId)); Assert.All(f.Sink.Emissions.Where(x => x.SubjectId == n.UserId), x => Assert.Equal(logicalId, x.Id));
        await using var verify = f.Db(); Assert.Equal(DeliveryState.Emitted, (await verify.Notifications.SingleAsync(x => x.SubjectId == n.UserId)).State);
        var replay = n with { EventId = Guid.NewGuid() }; await f.Publish(replay); await f.Receipt(replay.EventId); await f.Drain(); Assert.Equal(2, f.Sink.Emissions.Count(x => x.SubjectId == n.UserId));
    }
    [Fact]
    public async Task EmissionFailuresAreBoundedAndExplicitRetryRecoversSameRow()
    {
        var n = NotificationsFixture.User(); await f.Publish(n); await f.Receipt(n.EventId); f.Sink.FailBefore[n.UserId] = 4;
        for (var i = 0; i < 4; i++) { await f.Drain(); f.Clock.Advance(20); }
        Guid id;
        await using (var db = f.Db()) { var row = await db.Notifications.SingleAsync(x => x.SubjectId == n.UserId); id = row.Id; Assert.Equal(DeliveryState.Failed, row.State); Assert.Equal(4, row.Failures); }
        Assert.DoesNotContain(f.Sink.Emissions, x => x.SubjectId == n.UserId);
        f.Sink.FailBefore.TryRemove(n.UserId, out _); Assert.True(await f.RetryFailed() >= 1); await f.Drain();
        var emitted = Assert.Single(f.Sink.Emissions, x => x.SubjectId == n.UserId); Assert.Equal(id, emitted.Id); Assert.Equal(0, await f.RetryFailed());
    }
    [Fact]
    public async Task PendingDeliverySurvivesRestartAndDoesNotNeedBroker()
    {
        var n = NotificationsFixture.User(); await f.Publish(n); await f.Receipt(n.EventId);
        await f.Docker("stop", "rabbitmq");
        try
        {
            await f.Restart(worker: true, waitForBroker: false);
            await NotificationsFixture.Wait(async () => { await using var db = f.Db(); return await db.Notifications.AnyAsync(x => x.SubjectId == n.UserId && x.State == DeliveryState.Emitted); });
            Assert.Single(f.Sink.Emissions, x => x.SubjectId == n.UserId);
        }
        finally { await f.Docker("start", "rabbitmq"); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)); await f.Factory.Services.GetRequiredService<IBusControl>().StartAsync(timeout.Token);
        await f.Restart(); var replay = n with { EventId = Guid.NewGuid() }; await f.Publish(replay); await f.Receipt(replay.EventId); await f.Drain();
        Assert.Single(f.Sink.Emissions, x => x.SubjectId == n.UserId);
    }
    [Fact]
    public async Task UniqueDatabaseConstraintProtectsLogicalNotification()
    {
        var n = NotificationsFixture.User(); await f.Publish(n); await f.Receipt(n.EventId);
        await using var db = f.Db(); db.Notifications.Add(Notification.Create(NotificationKind.Welcome, n.UserId, n.UserId, Guid.NewGuid(), null, null, f.Clock.GetUtcNow()));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal("UX_Notifications_Kind_Subject", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
        await f.Drain();
    }
    [Fact]
    public async Task NoPersonalPayloadIsPersistedAndNoNotificationHttpEndpointExists()
    {
        Assert.Equal(HttpStatusCode.OK, (await f.Client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Client.GetAsync("/api/notifications")).StatusCode);
        await using var db = f.Db(); var properties = db.Model.GetEntityTypes().SelectMany(t => t.GetProperties()).Select(p => p.Name).ToArray();
        Assert.DoesNotContain("Email", properties); Assert.DoesNotContain("Name", properties); Assert.DoesNotContain("Password", properties);
    }
    [Fact]
    public async Task StartupDoesNotApplyMigrations()
    {
        var schema = "g5_" + Guid.NewGuid().ToString("N"); await using var connection = new NpgsqlConnection(f.Config["ConnectionStrings:Notifications"]); await connection.OpenAsync();
        var quoted = new NpgsqlCommandBuilder().QuoteIdentifier(schema); await using (var create = new NpgsqlCommand("CREATE SCHEMA " + quoted, connection)) await create.ExecuteNonQueryAsync();
        try
        {
            var config = new Dictionary<string, string?>(f.Config) { ["ConnectionStrings:Notifications"] = new NpgsqlConnectionStringBuilder(f.Config["ConnectionStrings:Notifications"]) { SearchPath = schema }.ConnectionString };
            await using var isolated = new NotificationsFactory(config, f.Clock, f.Sink, new FailurePlan()); using var client = isolated.CreateClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45)); await isolated.Services.GetRequiredService<IBusControl>().StartAsync(timeout.Token);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode); Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
            await using var query = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema = @schema", connection); query.Parameters.AddWithValue("schema", schema); Assert.Equal(0L, await query.ExecuteScalarAsync());
        }
        finally { await using var drop = new NpgsqlCommand("DROP SCHEMA " + quoted, connection); await drop.ExecuteNonQueryAsync(); }
    }
}
