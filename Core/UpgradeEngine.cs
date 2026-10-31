namespace Based.Core;

public sealed class UpgradeEngine
{
    private readonly object _gate = new();
    private readonly IAuthority _authority;
    private readonly Dictionary<string, BaseDefinition> _definitions;
    private BasedState _state = new();

    public UpgradeEngine(IEnumerable<BaseDefinition> definitions, IAuthority authority)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _definitions = DefinitionRegistry.Create(definitions);
        Reset();
    }

    public IReadOnlyList<BaseDefinition> Definitions => Array.AsReadOnly(_definitions.Values.ToArray());
    public long Revision { get { lock (_gate) return _state.Revision; } }

    public UpgradeState GetUpgrade(string baseId, string upgradeId)
    {
        lock (_gate) return FindState(baseId, upgradeId).Copy();
    }

    public string? CurrentPhase(string baseId, string upgradeId)
    {
        lock (_gate)
        {
            var state = FindState(baseId, upgradeId);
            if (state.Stage != UpgradeStage.Constructing) return null;
            return FindDefinition(baseId, upgradeId).Phases.LastOrDefault(p => p.AfterSleepCycles <= state.CompletedCycles)?.Id;
        }
    }

    public IReadOnlyList<AppliedEffect> CompletedEffects(string baseId)
    {
        lock (_gate)
        {
            if (!_definitions.ContainsKey(baseId)) throw new KeyNotFoundException("Unknown base: " + baseId);
            return _state.Upgrades.Where(s => s.BaseId == baseId && s.Stage == UpgradeStage.Complete)
                .SelectMany(s => FindDefinition(s.BaseId, s.UpgradeId).Effects.Select(e => new AppliedEffect(s.BaseId, s.UpgradeId, e))).ToArray();
        }
    }

    public PurchaseResult BeginPurchase(PurchaseCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        lock (_gate)
        {
            if (!_authority.CanMutate) return Reject("host-required");
            if (string.IsNullOrWhiteSpace(command.CommandId) || string.IsNullOrWhiteSpace(command.PayerId)) return Reject("invalid-command");
            var existing = _state.Purchases.Find(p => p.Command.CommandId == command.CommandId);
            if (existing != null) return existing.Command == command ? existing.Result : Reject("command-id-conflict");
            string? reason = CanPurchase(command, checkReservation: true);
            if (reason == null && command.ExpectedRevision.HasValue && command.ExpectedRevision != _state.Revision) reason = "stale-revision";
            PurchaseResult result;
            if (reason != null) result = Reject(reason);
            else
            {
                var definition = FindDefinition(command.BaseId, command.UpgradeId);
                int amount = command.Kind == PurchaseKind.Rights ? definition.RightsPrice : definition.ConstructionPrice;
                result = new(PurchaseStatus.PaymentPending, "payment-required", _state.Revision,
                    new PaymentRequest(command.CommandId, command.PayerId, amount));
            }
            _state.Purchases.Add(new PurchaseRecord { Command = command, Result = result });
            return result;
        }
    }

    public PurchaseResult AcknowledgePayment(string commandId, PaymentAcknowledgement acknowledgement)
    {
        ArgumentNullException.ThrowIfNull(acknowledgement);
        lock (_gate)
        {
            if (!_authority.CanMutate) return Reject("host-required");
            var record = _state.Purchases.Find(p => p.Command.CommandId == commandId);
            if (record == null) return Reject("unknown-command");
            if (record.Result.Status != PurchaseStatus.PaymentPending) return record.Result;
            var request = record.Result.Payment!;
            if (!acknowledgement.Succeeded)
            {
                record.Result = Reject(record.Result.Reason == "cancellation-requested" ? "cancelled-before-payment" : "payment-declined");
                return record.Result;
            }
            if (string.IsNullOrWhiteSpace(acknowledgement.ReceiptId)) return Reject("receipt-required");
            if (acknowledgement.Amount < 0 || string.IsNullOrWhiteSpace(acknowledgement.PayerId)) return Reject("invalid-payment-acknowledgement");
            // A receipt is consumed once across commands; transport must authenticate its payer.
            if (_state.Purchases.Any(p => p != record && p.Result.ReceiptId == acknowledgement.ReceiptId)) return Reject("receipt-already-used");
            string? reason = record.Result.Reason == "cancellation-requested" ? "cancelled-after-payment" :
                acknowledgement.PayerId != request.PayerId ? "payer-mismatch" :
                acknowledgement.Amount != request.Amount ? "amount-mismatch" : CanPurchase(record.Command, checkReservation: false);
            if (reason != null)
            {
                record.Result = new(PurchaseStatus.RefundRequired, reason, _state.Revision,
                    new PaymentRequest(commandId, acknowledgement.PayerId, acknowledgement.Amount), acknowledgement.ReceiptId);
                return record.Result;
            }
            var state = FindState(record.Command.BaseId, record.Command.UpgradeId);
            if (record.Command.Kind == PurchaseKind.Rights)
            {
                state.RightsPaid = request.Amount;
                state.Stage = UpgradeStage.RightsOwned;
            }
            else
            {
                state.ConstructionPaid = request.Amount;
                state.Stage = UpgradeStage.Constructing;
                state.LastSleepCycle = _state.LastSleepCycle;
            }
            _state.Revision++;
            record.Result = new(PurchaseStatus.Applied, "applied", _state.Revision, request, acknowledgement.ReceiptId);
            return record.Result;
        }
    }

    public PurchaseResult CancelPurchase(string commandId)
    {
        lock (_gate)
        {
            if (!_authority.CanMutate) return Reject("host-required");
            var record = _state.Purchases.Find(p => p.Command.CommandId == commandId);
            if (record == null) return Reject("unknown-command");
            if (record.Result.Status != PurchaseStatus.PaymentPending) return record.Result;
            // Keep the reservation until the debit adapter confirms no charge or a refund.
            record.Result = record.Result with { Reason = "cancellation-requested" };
            return record.Result;
        }
    }

    public PurchaseResult AcknowledgeRefund(string commandId, string receiptId)
    {
        lock (_gate)
        {
            if (!_authority.CanMutate) return Reject("host-required");
            var record = _state.Purchases.Find(p => p.Command.CommandId == commandId);
            if (record == null) return Reject("unknown-command");
            if (record.Result.Status != PurchaseStatus.RefundRequired) return record.Result;
            if (record.Result.ReceiptId != receiptId) return Reject("receipt-mismatch");
            record.Result = record.Result with { Status = PurchaseStatus.Rejected, Reason = "refunded" };
            return record.Result;
        }
    }

    public TransitionResult AdvanceSleep(long cycle)
    {
        lock (_gate)
        {
            if (!_authority.CanMutate) return new(false, "host-required", _state.Revision);
            if (cycle < 0) return new(false, "invalid-cycle", _state.Revision);
            if (cycle <= _state.LastSleepCycle) return new(false, "cycle-already-processed", _state.Revision);
            _state.LastSleepCycle = cycle;
            foreach (var state in _state.Upgrades.Where(s => s.Stage == UpgradeStage.Constructing))
            {
                state.CompletedCycles++;
                state.LastSleepCycle = cycle;
                if (state.CompletedCycles >= FindDefinition(state.BaseId, state.UpgradeId).ConstructionCycles) state.Stage = UpgradeStage.Complete;
            }
            _state.Revision++;
            return new(true, "sleep-processed", _state.Revision);
        }
    }

    public BasedState ExportState()
    {
        lock (_gate) return Copy(_state);
    }

    // Clients may restore a validated host snapshot without becoming an authority.
    public void RestoreState(BasedState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            var copy = Copy(state);
            ValidateState(copy);
            _state = copy;
        }
    }

    public void Reset()
    {
        lock (_gate)
            _state = new BasedState
            {
                Upgrades = _definitions.Values.SelectMany(b => b.Upgrades.Select(u => new UpgradeState { BaseId = b.Id, UpgradeId = u.Id })).ToList()
            };
    }

    private string? CanPurchase(PurchaseCommand command, bool checkReservation)
    {
        if (!_definitions.TryGetValue(command.BaseId, out var baseDefinition)) return "unknown-base";
        var definition = baseDefinition.Upgrades.FirstOrDefault(u => u.Id == command.UpgradeId);
        if (definition == null) return "unknown-upgrade";
        if (!Enum.IsDefined(typeof(PurchaseKind), command.Kind)) return "invalid-purchase-kind";
        var state = FindState(command.BaseId, command.UpgradeId);
        if (definition.Prerequisites.Any(p => FindState(command.BaseId, p).Stage != UpgradeStage.Complete)) return "prerequisite-incomplete";
        if (command.Kind == PurchaseKind.Rights && state.Stage != UpgradeStage.RightsMissing) return "rights-already-owned";
        if (command.Kind == PurchaseKind.Construction && state.Stage != UpgradeStage.RightsOwned) return "construction-unavailable";
        if (command.Kind == PurchaseKind.Construction && _state.Upgrades.Any(s => s.Stage == UpgradeStage.Constructing)) return "construction-active";
        if (checkReservation && _state.Purchases.Any(p => p.Result.Status == PurchaseStatus.PaymentPending &&
            (p.Command.BaseId == command.BaseId && p.Command.UpgradeId == command.UpgradeId ||
             command.Kind == PurchaseKind.Construction && p.Command.Kind == PurchaseKind.Construction))) return "purchase-pending";
        return null;
    }

    private PurchaseResult Reject(string reason) => new(PurchaseStatus.Rejected, reason, _state.Revision);
    private UpgradeDefinition FindDefinition(string baseId, string upgradeId) => _definitions[baseId].Upgrades.First(u => u.Id == upgradeId);
    private UpgradeState FindState(string baseId, string upgradeId) => _state.Upgrades.FirstOrDefault(s => s.BaseId == baseId && s.UpgradeId == upgradeId)
        ?? throw new KeyNotFoundException("Unknown upgrade: " + baseId + "/" + upgradeId);

    private static BasedState Copy(BasedState state)
    {
        if (state.Upgrades == null || state.Purchases == null || state.Upgrades.Any(s => s == null) || state.Purchases.Any(p => p == null))
            throw new ArgumentException("State lists must not contain null.");
        return new BasedState
        {
            SchemaVersion = state.SchemaVersion, Revision = state.Revision, LastSleepCycle = state.LastSleepCycle,
            Upgrades = state.Upgrades.Select(s => s.Copy()).ToList(), Purchases = state.Purchases.Select(p => p.Copy()).ToList()
        };
    }

    private void ValidateState(BasedState state)
    {
        if (state.SchemaVersion != 1 || state.Revision < 0 || state.LastSleepCycle < -1) throw new ArgumentException("Unsupported or invalid state header.");
        var keys = new HashSet<(string, string)>();
        foreach (var upgrade in state.Upgrades)
        {
            if (!keys.Add((upgrade.BaseId, upgrade.UpgradeId))) throw new ArgumentException("Duplicate saved upgrade.");
            if (!_definitions.TryGetValue(upgrade.BaseId, out var b) || !b.Upgrades.Any(u => u.Id == upgrade.UpgradeId)) throw new ArgumentException("Unknown saved upgrade.");
            var definition = FindDefinition(upgrade.BaseId, upgrade.UpgradeId);
            if (!Enum.IsDefined(typeof(UpgradeStage), upgrade.Stage) || upgrade.CompletedCycles < 0 || upgrade.CompletedCycles > definition.ConstructionCycles ||
                upgrade.RightsPaid < 0 || upgrade.ConstructionPaid < 0 || upgrade.LastSleepCycle < -1 || upgrade.LastSleepCycle > state.LastSleepCycle)
                throw new ArgumentException("Invalid saved upgrade values.");
            if (upgrade.Stage == UpgradeStage.Complete && upgrade.CompletedCycles != definition.ConstructionCycles ||
                upgrade.Stage == UpgradeStage.Constructing && upgrade.CompletedCycles >= definition.ConstructionCycles ||
                upgrade.Stage < UpgradeStage.Constructing && upgrade.CompletedCycles != 0)
                throw new ArgumentException("Saved phase and completion disagree.");
            if (upgrade.Stage == UpgradeStage.RightsMissing && upgrade.RightsPaid != 0 ||
                upgrade.Stage < UpgradeStage.Constructing && upgrade.ConstructionPaid != 0)
                throw new ArgumentException("Saved payments and ownership disagree.");
        }
        foreach (var b in _definitions.Values)
            foreach (var definition in b.Upgrades)
                if (!keys.Contains((b.Id, definition.Id)))
                    state.Upgrades.Add(new UpgradeState { BaseId = b.Id, UpgradeId = definition.Id });
        if (state.Upgrades.Count(s => s.Stage == UpgradeStage.Constructing) > 1) throw new ArgumentException("Saved state has simultaneous construction.");
        foreach (var upgrade in state.Upgrades.Where(u => u.Stage != UpgradeStage.RightsMissing))
            foreach (string prerequisite in FindDefinition(upgrade.BaseId, upgrade.UpgradeId).Prerequisites)
                if (!state.Upgrades.Any(u => u.BaseId == upgrade.BaseId && u.UpgradeId == prerequisite && u.Stage == UpgradeStage.Complete))
                    throw new ArgumentException("Saved upgrade has an incomplete prerequisite.");
        var commands = new HashSet<string>(StringComparer.Ordinal);
        var receipts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in state.Purchases)
        {
            if (record.Command == null || record.Result == null || string.IsNullOrWhiteSpace(record.Command.CommandId) ||
                string.IsNullOrWhiteSpace(record.Command.PayerId) || !commands.Add(record.Command.CommandId) || !Enum.IsDefined(typeof(PurchaseStatus), record.Result.Status))
                throw new ArgumentException("Invalid saved purchase.");
            if (record.Result.Revision < 0 || record.Result.Revision > state.Revision) throw new ArgumentException("Invalid saved purchase revision.");
            if (record.Result.ReceiptId != null && !receipts.Add(record.Result.ReceiptId)) throw new ArgumentException("Duplicate saved payment receipt.");
            if (record.Result.Status == PurchaseStatus.PaymentPending || record.Result.Status == PurchaseStatus.RefundRequired)
            {
                var payment = record.Result.Payment;
                if (payment == null || payment.CommandId != record.Command.CommandId || payment.Amount < 0 || string.IsNullOrWhiteSpace(payment.PayerId))
                    throw new ArgumentException("Invalid saved pending payment.");
                if (!_definitions.TryGetValue(record.Command.BaseId, out var b) || !b.Upgrades.Any(u => u.Id == record.Command.UpgradeId))
                    throw new ArgumentException("Unknown saved payment target.");
            }
        }
        var pending = state.Purchases.Where(p => p.Result.Status == PurchaseStatus.PaymentPending).ToArray();
        if (pending.Count(p => p.Command.Kind == PurchaseKind.Construction) > 1 ||
            pending.GroupBy(p => (p.Command.BaseId, p.Command.UpgradeId)).Any(g => g.Count() > 1))
            throw new ArgumentException("Conflicting saved reservations.");
    }
}
