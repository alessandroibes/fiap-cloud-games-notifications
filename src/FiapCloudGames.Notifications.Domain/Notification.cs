namespace FiapCloudGames.Notifications.Domain;

public enum NotificationKind { Welcome, PurchaseConfirmation }
public enum DeliveryState { Pending, Emitted, Failed }

public sealed class Notification
{
    public Guid Id { get; private set; }
    public NotificationKind Kind { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public Guid? GameId { get; private set; }
    public Guid SourceEventId { get; private set; }
    public DeliveryState State { get; private set; }
    public int Failures { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? EmittedAt { get; private set; }
    private Notification() { }
    public static Notification Create(NotificationKind kind, Guid subject, Guid user, Guid sourceEvent, Guid? payment, Guid? game, DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind) || subject == Guid.Empty || user == Guid.Empty || sourceEvent == Guid.Empty)
            throw new ArgumentException("Identificadores da notificação inválidos.");
        if (kind == NotificationKind.PurchaseConfirmation && (payment is null || payment == Guid.Empty || game is null || game == Guid.Empty))
            throw new ArgumentException("Confirmação exige pagamento e jogo.");
        return new Notification { Id = Guid.NewGuid(), Kind = kind, SubjectId = subject, UserId = user, SourceEventId = sourceEvent, PaymentId = payment, GameId = game, CreatedAt = now.ToUniversalTime(), NextAttemptAt = now.ToUniversalTime() };
    }
    public void MarkEmitted(DateTimeOffset now)
    {
        if (State != DeliveryState.Pending) throw new InvalidOperationException("Notificação não está pendente.");
        State = DeliveryState.Emitted; EmittedAt = now.ToUniversalTime();
    }
    public void RecordFailure(DateTimeOffset now)
    {
        if (State != DeliveryState.Pending) return;
        Failures++;
        if (Failures >= 4) State = DeliveryState.Failed;
        else NextAttemptAt = now.ToUniversalTime().AddSeconds(Failures switch { 1 => 1, 2 => 5, _ => 15 });
    }
    public void Retry(DateTimeOffset now)
    {
        if (State != DeliveryState.Failed) return;
        State = DeliveryState.Pending; Failures = 0; NextAttemptAt = now.ToUniversalTime();
    }
}
