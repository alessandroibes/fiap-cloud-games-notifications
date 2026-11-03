using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FiapCloudGames.Notifications.Infrastructure;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Notifications") ?? throw new InvalidOperationException("Configure ConnectionStrings__Notifications para executar EF.")).Options);
}
