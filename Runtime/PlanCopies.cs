using Based.Content;
using Based.Core;
using Based.UI;
using Il2CppScheduleOne.Dialogue;
using HarmonyLib;
using S1API.Money;
using UnityEngine;
using UnityEngine.Events;

namespace Based.Runtime;

// Ray keeps a copy of every plan: a lost project folder comes back for cash. He offers it only for owned
// properties whose expansion rights are bought, and only while the player has no folder left.
// Ray talks in two ways. While his schedule has him at the realty office the controller overrides the choice list
// with the estate agent conversation, so the offer is added to that conversation's first menu. Anywhere else the
// controller's own choice list shows, and the offer is a controller choice.
internal static class PlanCopies
{
    internal const string RayId = "ray_hoffman";
    internal const int Price = 200;
    internal const string AgentConversation = "Realtor_SellProperty";
    private const string ContainerId = "based_ray_plans";
    private const string OfferLabel = "based_ray_plans_offer";
    private const string CopyChoice = "based_ray_copy_";
    private const string NeverMind = "based_ray_never_mind";
    private const float WarnAfter = 30f;
    private static S1API.Entities.NPC? _ray;
    private static DialogueController? _controller;
    private static DialogueController.DialogueChoice? _choice;
    private static DialogueHandler? _handler;
    private static bool _missingReported, _offered;

    internal static void Reset()
    {
        _ray = null;
        _controller = null;
        _choice = null;
        _handler = null;
        _missingReported = _offered = false;
    }

    // Runs once a second while a save is loaded.
    internal static void Tick(float sinceLoad)
    {
        if (_controller == null && !Bind(sinceLoad)) return;
        bool offered = Eligible().Length > 0;
        if (offered != _offered) Mod.Log(offered ? "Ray offers copies of the building plans." : "Ray has no building plans to offer.");
        _offered = offered;
        _choice!.Enabled = offered;
    }

    private static SiteDefinition[] Eligible()
    {
        if (ProjectFolder.Present) return Array.Empty<SiteDefinition>();
        return Mod.Sites.Where(site => Mod.Owns(site) &&
            Mod.Engine.GetUpgrade(site.Base.Id, site.UpgradeId).Stage != UpgradeStage.RightsMissing).ToArray();
    }

    private static bool Bind(float sinceLoad)
    {
        var npc = S1API.Entities.NPC.Get(RayId);
        if (npc == null)
        {
            if (sinceLoad > WarnAfter && !_missingReported)
            {
                Mod.Warn("Ray is missing from this save; copies of the building plans stay unavailable.");
                _missingReported = true;
            }
            return false;
        }
        var controller = npc.gameObject.GetComponentInChildren<DialogueController>(true);
        _handler = npc.gameObject.GetComponentInChildren<DialogueHandler>(true);
        if (controller == null || _handler == null) throw new InvalidOperationException("Ray has no dialogue controller or handler.");
        _ray = npc;
        npc.Dialogue.OnChoiceSelected(OfferLabel, Open);
        foreach (var site in Mod.Sites)
            npc.Dialogue.OnChoiceSelected(CopyChoice + site.Base.Id, () => Sell(site));
        npc.Dialogue.OnChoiceSelected(NeverMind, () => Mod.Log("The player left Ray without a copy of the plans."));
        var choice = new DialogueController.DialogueChoice
        {
            ChoiceText = Text.T("I lost my building plans."),
            Conversation = Build(),
            Priority = 55,
            onChoosen = new UnityEvent()
        };
        choice.onChoosen.AddListener((Action)Prepare);
        controller.AddDialogueChoice(choice, 55);
        _choice = choice;
        _controller = controller;
        Mod.Log("Ray's dialogue extended with copies of the building plans.");
        return true;
    }

    // The game starts the choice's conversation right after this runs, so the offer matches the moment of asking.
    private static void Prepare()
    {
        try { _choice!.Conversation = Build(); }
        catch (Exception error) { Mod.Warn("Ray's offer could not be prepared: " + error.Message); }
    }

    // Called for the choice list of every conversation node; only the first menu of Ray's agent conversation gets the entry.
    internal static void AddChoice(DialogueController controller, string nodeLabel, Il2CppSystem.Collections.Generic.List<DialogueChoiceData> choices)
    {
        if (_controller == null || controller.Pointer != _controller.Pointer || nodeLabel != "ENTRY") return;
        if (DialogueHandler.ActiveDialogue?.name != AgentConversation) return;
        if (!_offered) { Mod.Log("Ray's menu stays as it is: no building plans to offer."); return; }
        for (int i = 0; i < choices.Count; i++)
            if (choices[i]?.ChoiceLabel == OfferLabel) return;
        var entry = new DialogueChoiceData { Guid = Guid.NewGuid().ToString(), ChoiceLabel = OfferLabel, ChoiceText = Text.T("I lost my building plans.") };
        // Before the farewell, which vanilla keeps last.
        choices.Insert(Math.Max(0, choices.Count - 1), entry);
        Mod.Log("Ray's menu lists the building plan copies.");
    }

    // The offer is built for the moment of asking and replaces the menu.
    private static void Open()
    {
        try
        {
            _handler!.StartDialogue(Build());
            Mod.Log("Ray opened the building plan offer.");
        }
        catch (Exception error) { Mod.Warn("Ray's offer could not be opened: " + error); }
    }

    private static Conversation Build()
    {
        var sites = Eligible();
        float cash = Money.GetCashBalance();
        string price = "$" + Price;
        _ray!.Dialogue.BuildAndRegisterContainer(ContainerId, b =>
        {
            if (cash < Price)
            {
                Mod.Log("Ray's offer: cash " + cash + " is below $" + Price + "; he asks the player to come back.");
                b.AddNode("ENTRY", Text.T("Copies are {0}, cash. Come back when you have it.", price));
            }
            else if (!ProjectFolder.Fits)
            {
                Mod.Log("Ray's offer: the inventory has no room for a folder; he asks the player to make room.");
                b.AddNode("ENTRY", Text.T("Make some room in your pockets first."));
            }
            else
            {
                Mod.Log("Ray's offer: " + sites.Length + " copies at $" + Price + ", cash " + cash + ".");
                b.AddNode("ENTRY", Text.T("Lost your plans? I keep a copy of every deed. {0} a set, cash.", price), choices =>
                {
                    foreach (var site in sites)
                        choices.Add(CopyChoice + site.Base.Id, Text.T("{0} - {1}", Text.T(site.PropertyName), price), "DONE");
                    choices.Add(NeverMind, Text.T("Never mind."));
                });
                b.AddNode("DONE", Text.T("Here you go. Keep them somewhere safe this time."));
            }
        });
        for (int i = 0; i < _handler!.dialogueContainers.Count; i++)
            if (_handler.dialogueContainers[i].name == ContainerId) return _handler.dialogueContainers[i];
        throw new InvalidOperationException("S1API did not register the conversation " + ContainerId + ".");
    }

#if DEBUG
    // Opens Ray's conversation through the controller's own interact entry, the call the game makes when the
    // player talks to him, so the menu the player sees includes the plan copy entry. "office" and "street" set the
    // controller state his schedule sets (the estate agent conversation as override, or none) before talking.
    internal static void DebugTalk(string where)
    {
        var npc = S1API.Entities.NPC.Get(RayId);
        if (npc == null) { Mod.Warn("baseddiag ray: no NPC '" + RayId + "' in this save."); return; }
        var controller = npc.gameObject.GetComponentInChildren<DialogueController>(true);
        if (controller == null) { Mod.Warn("baseddiag ray: Ray has no DialogueController."); return; }
        if (!controller.DialogueEnabled) { Mod.Warn("baseddiag ray: DialogueEnabled is off on Ray's controller."); return; }
        if (where == "end")
        {
            Mod.Log("baseddiag ray: dialogue in progress " + (_handler?.IsDialogueInProgress ?? false) + "; ending it.");
            _handler?.EndDialogue();
            return;
        }
        if (where == "street") controller.OverrideContainer = null;
        else if (where == "office")
        {
            Conversation? agent = null;
            foreach (var found in Resources.FindObjectsOfTypeAll<Conversation>())
                if (found.name == AgentConversation) { agent = found; break; }
            if (agent == null) { Mod.Warn("baseddiag ray: the conversation " + AgentConversation + " was not found."); return; }
            controller.OverrideContainer = agent;
        }
        else if (where.Length > 0) { Mod.Warn("baseddiag ray: unknown place '" + where + "', use office or street."); return; }
        if (!controller.CanStartDialogue()) Mod.Warn("baseddiag ray: CanStartDialogue is false (pursuit, clipboard or police call); starting anyway.");
        Mod.Log("baseddiag ray: override '" + (controller.OverrideContainer?.name ?? "none") + "', dialogue in progress " + (_handler?.IsDialogueInProgress ?? false)
            + ", active '" + (DialogueHandler.ActiveDialogue?.name ?? "none") + "', behaviour " + controller.UseDialogueBehaviour + ".");
        if (controller.OverrideContainer == null && controller.GetActiveChoices().Count == 0)
        {
            Mod.Warn("baseddiag ray: Ray has no override conversation and no active choices, the game would only show a greeting bubble.");
            return;
        }
        controller.Interacted();
        Mod.Log("baseddiag ray: Interacted() called on Ray's controller.");
    }
#endif

    // The folder goes into the pocket first, so a full inventory never costs money.
    private static void Sell(SiteDefinition site)
    {
        try
        {
            if (!Eligible().Contains(site)) { Mod.Warn("Plan copy for " + site.Base.Id + " refused: the player no longer qualifies."); return; }
            float cash = Money.GetCashBalance();
            if (cash < Price) { Mod.Warn("Plan copy for " + site.Base.Id + " refused: cash " + cash + " is below " + Price + "."); return; }
            string refused = ProjectFolder.Give();
            if (refused.Length > 0) { Mod.Warn("Plan copy for " + site.Base.Id + " not handed over (" + refused + "); no cash taken."); return; }
            Money.ChangeCashBalance(-Price, true, true);
            Mod.Log("Ray sold a copy of the " + site.Base.Id + " plans for $" + Price + "; cash " + cash + " -> " + Money.GetCashBalance() + ".");
        }
        catch (Exception error) { Mod.Warn("Plan copy for " + site.Base.Id + " failed: " + error); }
    }
}

[HarmonyPatch(typeof(DialogueController), nameof(DialogueController.ModifyChoiceList))]
internal static class RayPlanCopiesChoice
{
    // The parameter names must match the target's.
    private static void Postfix(DialogueController __instance, string dialogueLabel, ref Il2CppSystem.Collections.Generic.List<DialogueChoiceData> existingChoices)
    {
        try { if (existingChoices != null) PlanCopies.AddChoice(__instance, dialogueLabel, existingChoices); }
        catch (Exception error) { Mod.Warn("Ray's menu could not list the plan copies: " + error); }
    }
}
