using FiapCloudGames.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace FiapCloudGames.Notifications.Infrastructure;

public sealed class EventReceipt
{
    public Guid EventId { get; set; }
    public NotificationKind Kind { get; set; }
    public Guid SubjectId { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset ReceivedAt { get; set; }
}
public sealed class NotificationSubject
{
    public NotificationKind Kind { get; set; }
    public Guid SubjectId { get; set; }
    public Guid? PaymentId { get; set; }
    public string Fingerprint { get; set; } = "";
    public bool ShouldNotify { get; set; }
}
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<EventReceipt> EventReceipts => Set<EventReceipt>();
    public DbSet<NotificationSubject> Subjects => Set<NotificationSubject>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<NotificationSubject>(e =>
        {
            e.HasKey(x => new { x.Kind, x.SubjectId }); e.Property(x => x.Kind).HasConversion<string>();
            e.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired(); e.HasIndex(x => x.PaymentId).IsUnique();
        });
        model.Entity<EventReceipt>(e =>
        {
            e.HasKey(x => x.EventId); e.Property(x => x.Kind).HasConversion<string>(); e.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired();
            e.HasOne<NotificationSubject>().WithMany().HasForeignKey(x => new { x.Kind, x.SubjectId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Notification>(e =>
        {
            e.HasKey(x => x.Id); e.Property(x => x.Kind).HasConversion<string>(); e.Property(x => x.State).HasConversion<string>();
            e.HasIndex(x => new { x.Kind, x.SubjectId }).IsUnique().HasDatabaseName("UX_Notifications_Kind_Subject");
            e.HasIndex(x => new { x.State, x.NextAttemptAt });
            e.HasOne<NotificationSubject>().WithMany().HasForeignKey(x => new { x.Kind, x.SubjectId }).OnDelete(DeleteBehavior.Restrict);
            e.ToTable("Notifications", t => t.HasCheckConstraint("CK_Notifications_State", "(\"State\" IN ('Pending','Failed') AND \"EmittedAt\" IS NULL) OR (\"State\" = 'Emitted' AND \"EmittedAt\" IS NOT NULL)"));
        });
    }
}
