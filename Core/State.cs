namespace Based.Core;

public interface IAuthority
{
    bool CanMutate { get; }
}

public enum UpgradeStage { RightsMissing, RightsOwned, Constructing, Complete }
public enum PurchaseKind { Rights, Construction }
public enum PurchaseStatus { PaymentPending, Applied, Rejected, RefundRequired }

public sealed record PurchaseCommand(string CommandId, string BaseId, string UpgradeId,
    PurchaseKind Kind, string PayerId);
public sealed record PaymentRequest(string CommandId, string PayerId, int Amount);
public sealed record PaymentAcknowledgement(bool Succeeded, string ReceiptId, string PayerId, int Amount);
public sealed record PurchaseResult(PurchaseStatus Status, string Reason, long Revision,
    PaymentRequest? Payment = null, string? ReceiptId = null);
public sealed record TransitionResult(bool Applied, string Reason, long Revision);

// These mutable DTOs carry only managed data for the existing Saveable serializer.
public sealed class BasedState
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public long LastSleepCycle { get; set; } = -1;
    public List<UpgradeState> Upgrades { get; set; } = new();
    public List<PurchaseRecord> Purchases { get; set; } = new();
}

public sealed class UpgradeState
{
    public string BaseId { get; set; } = "";
    public string UpgradeId { get; set; } = "";
    public UpgradeStage Stage { get; set; }
    public int CompletedCycles { get; set; }
    public int RightsPaid { get; set; }
    public int ConstructionPaid { get; set; }
    public long LastSleepCycle { get; set; } = -1;
    public UpgradeState Copy() => (UpgradeState)MemberwiseClone();
}

public sealed class PurchaseRecord
{
    public PurchaseCommand Command { get; set; } = null!;
    public PurchaseResult Result { get; set; } = null!;
    public PurchaseRecord Copy() => new() { Command = Command, Result = Result };
}
