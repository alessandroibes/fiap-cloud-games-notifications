using FiapCloudGames.Notifications.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args.Where(a => a is not "--migrate" and not "--retry-failed").ToArray());
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.Services.AddNotificationsInfrastructure(builder.Configuration);
await using var app = builder.Build();
if (args.Contains("--migrate") || args.Contains("--retry-failed"))
{
    await using var scope = app.Services.CreateAsyncScope();
    if (args.Contains("--migrate")) await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
    if (args.Contains("--retry-failed"))
    {
        var count = await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().RetryFailed(CancellationToken.None);
        app.Logger.LogInformation("Notificações com falha recolocadas em Pending: {Count}", count);
    }
    return;
}
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/health/ready", async (NotificationsDbContext db, CancellationToken ct) =>
{
    try { return Results.StatusCode(await db.Database.CanConnectAsync(ct) && !(await db.Database.GetPendingMigrationsAsync(ct)).Any() ? 200 : 503); }
    catch { return Results.StatusCode(503); }
});
app.Run();
public partial class Program;
