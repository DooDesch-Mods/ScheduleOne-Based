using Based.Content;
using Based.Core;
using Based.UI;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Newtonsoft.Json;
using S1API.Entities.Dialogue;
using S1API.Money;
using UnityEngine;

namespace Based.Runtime;

// The foreman takes a tip while a build runs. Tips up to a tenth of the build price leave less of the crew's trash
// behind, none at all from that tenth on. Cash counts at face value, product at one and a half times its market price.
// The host counts every tip; a client pays first, sends the tip until the host answers and gets it back on a refusal.
internal static class Tips
{
    private const string Wire = "BASED-TIP1:";
    internal const string Choice = "based_tip", ProductChoice = "based_tip_product", Never = "based_tip_never";
    private const string CashChoice = "based_tip_cash_", ItemChoice = "based_tip_item_";
    private const string TipNode = "TIP", ProductNode = "PRODUCT", PartNode = "THANKS_PART", FullNode = "THANKS_FULL";
    private const float ProductFactor = 1.5f, CapShare = 0.1f, ResendInterval = 3f;
    private static readonly float[] CashShares = { .25f, .5f, 1f };
    private const int MaxProducts = 4;
    private static readonly HashSet<S1API.Entities.NPC> Bound = new();
    private static readonly Dictionary<string, Offer> Offers = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Pending> Waiting = new(StringComparer.Ordinal);
    private static bool _listening;

    private sealed record ProductOffer(HotbarSlot Slot, string ItemId, string Name, int Units, int Value, float Price, int Pack);

    private sealed class Offer
    {
        internal SiteDefinition Site = null!;
        internal string UpgradeId = "";
        internal int Cap, Total;
        internal int[] Amounts = Array.Empty<int>();
        internal ProductOffer[] Products = Array.Empty<ProductOffer>();
        internal string Label => Site.Base.Id + "/" + UpgradeId;
    }

    private sealed class Pending
    {
        internal TipPacket Packet = null!;
        internal Action Refund = null!;
        internal string What = "";
        internal float NextSend;
        internal int Sends;
    }

    internal sealed class TipPacket
    {
        public string Kind { get; set; } = "";
        public string Id { get; set; } = "";
        public string BaseId { get; set; } = "";
        public string UpgradeId { get; set; } = "";
        public int Amount { get; set; }
        public bool Accepted { get; set; }
        public string Reason { get; set; } = "";
    }

    internal static void Listen()
    {
        if (_listening) return;
        NativeBus.HostReceived += ReceiveHost;
        NativeBus.ClientReceived += ReceiveClient;
        _listening = true;
    }

    internal static void Reset()
    {
        foreach (var pending in Waiting.Values)
            Mod.Warn("Tip " + pending.Packet.Id + " (" + pending.What + ") had no answer from the host when the save closed; it is not refunded.");
        Waiting.Clear();
        Offers.Clear();
        Bound.Clear();
    }

    // The build a tip at this base goes to: the one under construction, if it has a price.
    private static (SiteDefinition Site, string UpgradeId)? Target(string baseId)
    {
        foreach (var site in Mod.Sites.Where(s => s.Base.Id == baseId))
        {
            string? building = Mod.Constructing(site);
            if (building != null && site.UpgradeOf(building).ConstructionPrice > 0) return (site, building);
        }
        return null;
    }

    internal static int Cap(SiteDefinition site, string upgradeId) =>
        Mathf.RoundToInt(site.UpgradeOf(upgradeId).ConstructionPrice * CapShare);

    // A full tenth of the build price leaves no trash; less leaves the share that is missing.
    internal static int LitterCount(SiteDefinition site, string upgradeId, int full, out int tips, out int cap)
    {
        tips = Mod.Engine.GetUpgrade(site.Base.Id, upgradeId).Tips;
        cap = Cap(site, upgradeId);
        float share = cap > 0 ? Mathf.Clamp01((float)tips / cap) : 0f;
        return Mathf.RoundToInt(full * (1f - share));
    }

    // Binds the choices once per foreman; the dialogue itself is rebuilt each time a conversation starts.
    internal static void Bind(string baseId, S1API.Entities.NPC foreman)
    {
        if (!Bound.Add(foreman)) return;
        foreman.Dialogue.OnChoiceSelected(Choice, () => Mod.Log("The player opened the tip choice at " + baseId + "."));
        foreman.Dialogue.OnChoiceSelected(ProductChoice, () => Mod.Log("The player looks for product to tip at " + baseId + "."));
        foreman.Dialogue.OnChoiceSelected(Never, () => Mod.Log("The player left the foreman at " + baseId + " without a tip."));
        for (int i = 0; i < CashShares.Length; i++)
        {
            int index = i;
            foreman.Dialogue.OnChoiceSelected(CashChoice + i, () => Contained("cash tip", () => GiveCash(baseId, index)));
        }
        for (int i = 0; i < MaxProducts; i++)
        {
            int index = i;
            foreman.Dialogue.OnChoiceSelected(ItemChoice + i, () => Contained("product tip", () => GiveProduct(baseId, index)));
        }
        Mod.Log("Tip choices bound to the foreman at " + baseId + ".");
    }

    // Adds the foreman's line and, while a build runs below the tip limit, the tip conversation.
    internal static void Build(string baseId, DialogueContainerBuilder builder, string line)
    {
        var offer = MakeOffer(baseId, out string why);
        if (offer == null)
        {
            Offers.Remove(baseId);
            builder.AddNode("ENTRY", line);
            Mod.Log("Foreman at " + baseId + " offers no tip choice: " + why + ".");
            return;
        }
        Offers[baseId] = offer;
        builder.AddNode("ENTRY", line, choices => choices
            .Add(Choice, Text.T("Give a tip"), TipNode)
            .Add(Never, Text.T("Never mind.")));
        bool any = offer.Amounts.Length > 0 || offer.Products.Length > 0;
        builder.AddNode(TipNode, any ? Text.T("What did you have in mind?") : Text.T("Come back when you have some cash."), choices =>
        {
            for (int i = 0; i < offer.Amounts.Length; i++)
                choices.Add(CashChoice + i, Text.T("${0} in cash", offer.Amounts[i]), Thanks(offer, offer.Amounts[i]));
            if (offer.Products.Length > 0) choices.Add(ProductChoice, Text.T("Some product"), ProductNode);
            choices.Add(Never, Text.T("Never mind."));
        });
        if (offer.Products.Length > 0)
            builder.AddNode(ProductNode, Text.T("What have you got?"), choices =>
            {
                for (int i = 0; i < offer.Products.Length; i++)
                {
                    var product = offer.Products[i];
                    choices.Add(ItemChoice + i, Text.T("{0}x {1} (worth ${2})", product.Units, product.Name, product.Value), Thanks(offer, product.Value));
                }
                choices.Add(Never, Text.T("Never mind."));
            });
        builder.AddNode(PartNode, Text.T("Thanks. We'll tidy up a bit."));
        builder.AddNode(FullNode, Text.T("Thanks. We'll leave the place clean."));
        Mod.Log("Foreman at " + baseId + " offers a tip for " + offer.Label + ": $" + offer.Total + " of $" + offer.Cap + " given, cash "
                + (offer.Amounts.Length > 0 ? string.Join("/", offer.Amounts.Select(a => "$" + a)) : "none") + ", product "
                + (offer.Products.Length > 0 ? string.Join(", ", offer.Products.Select(p => p.Units + "x " + p.ItemId + " $" + p.Value)) : "none") + ".");
    }

    private static string Thanks(Offer offer, int amount) => offer.Total + amount >= offer.Cap ? FullNode : PartNode;

    private static Offer? MakeOffer(string baseId, out string why)
    {
        var target = Target(baseId);
        if (target == null) { why = "no paid build runs"; return null; }
        var (site, upgradeId) = target.Value;
        int cap = Cap(site, upgradeId);
        int total = Mod.Engine.GetUpgrade(site.Base.Id, upgradeId).Tips;
        if (cap <= 0) { why = "the build has no price"; return null; }
        if (total >= cap) { why = "the crew already got $" + total + " of $" + cap + " for " + upgradeId; return null; }
        int left = cap - total;
        float cash = Money.GetCashBalance();
        var amounts = CashShares.Select(s => Math.Min(left, Mathf.RoundToInt(cap * s))).Where(a => a > 0 && a <= cash).Distinct().ToArray();
        why = "";
        return new Offer { Site = site, UpgradeId = upgradeId, Cap = cap, Total = total, Amounts = amounts, Products = ProductsFor(left) };
    }

    // Each product stack gives as many units as it takes to reach the limit, at least one.
    private static ProductOffer[] ProductsFor(int left)
    {
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
        var result = new List<ProductOffer>();
        if (inventory == null) { Mod.Warn("Tip product list empty: the player inventory is not ready."); return result.ToArray(); }
        for (int i = 0; i < inventory.hotbarSlots.Count && result.Count < MaxProducts; i++)
        {
            var slot = inventory.hotbarSlots[i];
            var item = slot?.ItemInstance;
            if (item == null) continue;
            var product = item.TryCast<ProductItemInstance>();
            var definition = item.Definition?.TryCast<ProductDefinition>();
            if (product == null || definition == null || item.Quantity <= 0) continue;
            int pack = Math.Max(1, product.Amount);
            float unit = definition.MarketValue * pack * ProductFactor;
            if (unit <= 0) continue;
            int units = Math.Clamp(Mathf.CeilToInt(left / unit), 1, item.Quantity);
            result.Add(new ProductOffer(slot!, item.ID, definition.Name, units, Mathf.RoundToInt(units * unit), definition.MarketValue, pack));
        }
        return result.ToArray();
    }

    private static void GiveCash(string baseId, int index)
    {
        if (!Offers.TryGetValue(baseId, out var offer) || index >= offer.Amounts.Length)
        {
            Mod.Warn("Cash tip " + index + " at " + baseId + " chosen without a matching offer; nothing paid.");
            return;
        }
        PayCash(offer, offer.Amounts[index]);
    }

    private static void PayCash(Offer offer, int amount)
    {
        float cash = Money.GetCashBalance();
        if (cash < amount)
        {
            Mod.Warn("Cash tip of $" + amount + " for " + offer.Label + " not paid: the player has $" + cash + ".");
            return;
        }
        Money.ChangeCashBalance(-amount, true, true);
        Mod.Log("Cash tip of $" + amount + " for " + offer.Label + " paid; cash " + cash + " -> " + Money.GetCashBalance() + ".");
        Submit(offer, amount, "$" + amount + " cash", () =>
        {
            Money.ChangeCashBalance(amount, true, true);
            Mod.Log("Cash tip of $" + amount + " refunded; cash " + Money.GetCashBalance() + ".");
        });
    }

    private static void GiveProduct(string baseId, int index)
    {
        if (!Offers.TryGetValue(baseId, out var offer) || index >= offer.Products.Length)
        {
            Mod.Warn("Product tip " + index + " at " + baseId + " chosen without a matching offer; nothing taken.");
            return;
        }
        PayProduct(offer, offer.Products[index]);
    }

    private static void PayProduct(Offer offer, ProductOffer product)
    {
        var item = product.Slot.ItemInstance;
        if (item == null || item.ID != product.ItemId || item.Quantity < product.Units)
        {
            Mod.Warn("Product tip of " + product.Units + "x " + product.ItemId + " for " + offer.Label + " not taken: the slot changed since the offer.");
            return;
        }
        var refund = item.GetCopy(product.Units);
        product.Slot.ChangeQuantity(-product.Units);
        Mod.Log("Product tip of " + product.Units + "x " + product.ItemId + " worth $" + product.Value + " (market price $"
                + product.Price.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " x " + product.Pack + " per unit x " + ProductFactor.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ") for " + offer.Label + " taken from the inventory.");
        Submit(offer, product.Value, product.Units + "x " + product.ItemId, () =>
        {
            var inventory = PlayerSingleton<PlayerInventory>.Instance;
            if (inventory != null && inventory.CanItemFitInInventory(refund, refund.Quantity))
            {
                inventory.AddItemToInventory(refund);
                Mod.Log("Product tip of " + product.Units + "x " + product.ItemId + " refunded to the inventory.");
            }
            else Mod.Error("Product tip of " + product.Units + "x " + product.ItemId + " could not be refunded: no room in the inventory.");
        });
    }

    private static void Submit(Offer offer, int amount, string what, Action refund)
    {
        var packet = new TipPacket { Kind = "tip", Id = Guid.NewGuid().ToString("N"), BaseId = offer.Site.Base.Id, UpgradeId = offer.UpgradeId, Amount = amount };
        if (NativeBus.IsHost)
        {
            if (!Apply(-1, packet)) refund();
            return;
        }
        Waiting[packet.Id] = new Pending { Packet = packet, Refund = refund, What = what };
        Mod.Log("Tip " + packet.Id + " of $" + amount + " (" + what + ") for " + offer.Label + " goes to the host.");
        Tick();
    }

    // Sends each open tip again until the host answers, since a packet can be lost during a join or a hitch.
    internal static void Tick()
    {
        if (NativeBus.IsHost || Waiting.Count == 0) return;
        float now = Time.unscaledTime;
        foreach (var pending in Waiting.Values.Where(p => now >= p.NextSend).ToArray())
        {
            pending.NextSend = now + ResendInterval;
            pending.Sends++;
            NativeBus.SendToHost(Wire + JsonConvert.SerializeObject(pending.Packet));
            if (pending.Sends > 1) Mod.Log("Tip " + pending.Packet.Id + " sent to the host again (" + pending.Sends + ").");
        }
    }

    // Counts the tip on the host. True means the tip counts, now or from an earlier send.
    private static bool Apply(int peer, TipPacket packet)
    {
        string who = peer < 0 ? "the host" : "peer " + peer;
        string label = packet.BaseId + "/" + packet.UpgradeId;
        var result = Mod.Engine.AddTip(packet.Id, packet.BaseId, packet.UpgradeId, packet.Amount);
        if (result.Reason == "duplicate")
        {
            Mod.Log("Tip " + packet.Id + " from " + who + " arrived again; it counted already.");
            return true;
        }
        if (!result.Applied)
        {
            Mod.Warn("Tip " + packet.Id + " of $" + packet.Amount + " from " + who + " for " + label + " refused: " + result.Reason + ".");
            return false;
        }
        Mod.Changed();
        var site = Mod.Sites.FirstOrDefault(s => s.Base.Id == packet.BaseId);
        int total = Mod.Engine.GetUpgrade(packet.BaseId, packet.UpgradeId).Tips;
        Mod.Log("Tip " + packet.Id + " of $" + packet.Amount + " from " + who + " counted for " + label + ": $" + total
                + (site != null ? " of $" + Cap(site, packet.UpgradeId) : "") + " in tips; revision " + result.Revision + ".");
        return true;
    }

    private static void ReceiveHost(int peer, string payload)
    {
        if (!payload.StartsWith(Wire, StringComparison.Ordinal)) return;
        try
        {
            var packet = JsonConvert.DeserializeObject<TipPacket>(payload.Substring(Wire.Length)) ?? throw new InvalidOperationException("Empty tip packet.");
            if (packet.Kind != "tip") throw new InvalidOperationException("Unexpected tip packet kind '" + packet.Kind + "'.");
            bool counted = Apply(peer, packet);
            string reason = counted ? "counted" : "refused";
            NativeBus.SendToPeer(peer, Wire + JsonConvert.SerializeObject(new TipPacket { Kind = "ack", Id = packet.Id, Accepted = counted, Reason = reason }));
        }
        catch (Exception error) { Mod.Warn("Rejected tip message from peer " + peer + ": " + error.Message); }
    }

    private static void ReceiveClient(string payload)
    {
        if (NativeBus.IsHost || !payload.StartsWith(Wire, StringComparison.Ordinal)) return;
        try
        {
            var packet = JsonConvert.DeserializeObject<TipPacket>(payload.Substring(Wire.Length)) ?? throw new InvalidOperationException("Empty tip answer.");
            if (packet.Kind != "ack") throw new InvalidOperationException("Unexpected tip answer kind '" + packet.Kind + "'.");
            if (!Waiting.Remove(packet.Id, out var pending))
            {
                Mod.Log("Tip answer " + packet.Id + " ignored: no open tip with that id.");
                return;
            }
            if (packet.Accepted)
            {
                Mod.Log("Tip " + packet.Id + " (" + pending.What + ") counted by the host after " + pending.Sends + " send(s).");
                return;
            }
            Mod.Warn("Tip " + packet.Id + " (" + pending.What + ") refused by the host; it goes back to the player.");
            pending.Refund();
        }
        catch (Exception error) { Mod.Warn("Rejected tip answer from the host: " + error.Message); }
    }

    private static void Contained(string what, Action action)
    {
        try { action(); }
        catch (Exception error) { Mod.Error("The " + what + " failed: " + error); }
    }

#if DEBUG
    internal static string Describe()
    {
        var lines = new List<string>();
        foreach (var site in Mod.Sites)
        foreach (var upgrade in site.Base.Upgrades.Where(u => u.ConstructionPrice > 0))
        {
            var state = Mod.Engine.GetUpgrade(site.Base.Id, upgrade.Id);
            var rooms = ConstructionLitter.RoomsOf(site, upgrade.Id);
            int full = rooms.Count > 0 ? ConstructionLitter.CountFor(rooms) : 0;
            int count = LitterCount(site, upgrade.Id, full, out int tips, out int cap);
            string line = upgrade.Id + " stage=" + state.Stage + " tips=" + tips + " cap=" + cap + " litter=" + count + "/" + full;
            if (!lines.Contains(line)) lines.Add(line);
        }
        return string.Join("; ", lines) + "; open=" + Waiting.Count;
    }

    // Runs the dialogue's own paths: a tier amount pays like its choice, any other amount the same way.
    internal static void DebugCash(string baseId, int amount)
    {
        var offer = MakeOffer(baseId, out string why) ?? throw new InvalidOperationException("No tip offer at " + baseId + ": " + why + ".");
        Offers[baseId] = offer;
        int index = Array.IndexOf(offer.Amounts, amount);
        if (index >= 0) GiveCash(baseId, index); else PayCash(offer, amount);
    }

    internal static void DebugProduct(string baseId, int index)
    {
        var offer = MakeOffer(baseId, out string why) ?? throw new InvalidOperationException("No tip offer at " + baseId + ": " + why + ".");
        Offers[baseId] = offer;
        if (index >= offer.Products.Length) throw new InvalidOperationException("Only " + offer.Products.Length + " product choice(s) at " + baseId + ".");
        GiveProduct(baseId, index);
    }

    internal static void DebugSet(string baseId, int total)
    {
        var target = Target(baseId) ?? throw new InvalidOperationException("No paid build runs at " + baseId + ".");
        var result = Mod.Engine.SetTips(baseId, target.UpgradeId, total);
        if (!result.Applied) throw new InvalidOperationException("Tips not set: " + result.Reason + ".");
        Mod.Changed();
    }

    // The game's changecash runs only on the host; this gives this peer's player cash, so a client can tip.
    internal static void DebugWallet(int amount)
    {
        float before = Money.GetCashBalance();
        Money.ChangeCashBalance(amount, true, true);
        Mod.Log("baseddiag: wallet " + before + " -> " + Money.GetCashBalance() + ".");
    }
#endif
}
