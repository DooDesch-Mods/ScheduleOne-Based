using Based.Content;
using Based.Core;
using Based.UI;
using Il2CppScheduleOne.Dialogue;
using UnityEngine.Events;

namespace Based.Runtime;

// One entry at Ray for every property he sells builds for. With one property to expand he hands over its plan
// right away; with several he asks which one first. Like the plan copies, the entry is a controller choice on the
// street and an extra line in the estate agent menu at his office, where the controller's choices stay hidden.
internal static class RayMenu
{
    private const string ContainerId = "based_ray_expand";
    private const string OfferLabel = "based_ray_expand_offer";
    private const string SiteChoice = "based_ray_expand_";
    private const string NeverMind = "based_ray_expand_never_mind";
    private const string Entry = "I want to expand one of my properties.";
    private const float WarnAfter = 30f;
    private static S1API.Entities.NPC? _ray;
    private static DialogueController? _controller;
    private static DialogueController.DialogueChoice? _choice;
    private static DialogueHandler? _handler;
    private static bool _missingReported, _offered;

    private static IEnumerable<SiteDefinition> MenuSites => Mod.Sites.Where(s => s.Menu);

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
        if (!MenuSites.Any()) return;
        if (_controller == null && !Bind(sinceLoad)) return;
        bool offered = Eligible().Length > 0;
        if (offered != _offered) Mod.Log(offered ? "Ray offers to expand a property." : "Ray has no property to expand.");
        _offered = offered;
        _choice!.Enabled = offered;
    }

    // Owned, with something left to build. A property not yet bought is Ray's own business in his estate agent menu.
    private static SiteDefinition[] Eligible() => MenuSites
        .Where(site => Mod.Owns(site) && site.BuildIds.Any(id => Mod.StageOf(site, id) != UpgradeStage.Complete))
        .ToArray();

    private static bool Bind(float sinceLoad)
    {
        var npc = S1API.Entities.NPC.Get(PlanCopies.RayId);
        if (npc == null)
        {
            if (sinceLoad > WarnAfter && !_missingReported)
            {
                Mod.Warn("Ray is missing from this save; his properties cannot be expanded.");
                _missingReported = true;
            }
            return false;
        }
        var controller = npc.gameObject.GetComponentInChildren<DialogueController>(true);
        _handler = npc.gameObject.GetComponentInChildren<DialogueHandler>(true);
        if (controller == null || _handler == null) throw new InvalidOperationException("Ray has no dialogue controller or handler.");
        _ray = npc;
        foreach (var site in MenuSites)
        {
            // Each site keeps a conversation of its own with its line, which the plan holds open.
            npc.Dialogue.BuildAndRegisterContainer(site.VendorContainerId, b => b.AddNode("ENTRY", Text.T(site.VendorLine)));
            Mod.RegisterMenuVendor(site, npc, _handler);
            var chosen = site;
            npc.Dialogue.OnChoiceSelected(SiteChoice + site.Base.Id, () => Request(chosen));
        }
        npc.Dialogue.OnChoiceSelected(OfferLabel, Open);
        npc.Dialogue.OnChoiceSelected(NeverMind, () => Mod.Log("The player left Ray without expanding a property."));
        var choice = new DialogueController.DialogueChoice
        {
            ChoiceText = Text.T(Entry),
            Conversation = Build(),
            Priority = 55,
            onChoosen = new UnityEvent()
        };
        choice.onChoosen.AddListener((Action)Prepare);
        controller.AddDialogueChoice(choice, 55);
        _choice = choice;
        _controller = controller;
        Mod.Log("Ray's dialogue extended with the expansion of " + string.Join(", ", MenuSites.Select(s => s.Base.Id)) + ".");
        return true;
    }

    // The game starts the choice's conversation right after this runs, so the answer matches the moment of asking.
    private static void Prepare()
    {
        try
        {
            _choice!.Conversation = Build();
            RequestSingle();
        }
        catch (Exception error) { Mod.Warn("Ray's expansion entry could not be prepared: " + error.Message); }
    }

    // Called for the choice list of every conversation node; only the first menu of Ray's agent conversation gets the entry.
    internal static void AddChoice(DialogueController controller, string nodeLabel, Il2CppSystem.Collections.Generic.List<DialogueChoiceData> choices)
    {
        if (_controller == null || controller.Pointer != _controller.Pointer || nodeLabel != "ENTRY") return;
        if (DialogueHandler.ActiveDialogue?.name != PlanCopies.AgentConversation) return;
        if (!_offered) { Mod.Log("Ray's menu stays as it is: no property to expand."); return; }
        for (int i = 0; i < choices.Count; i++)
            if (choices[i]?.ChoiceLabel == OfferLabel) return;
        var entry = new DialogueChoiceData { Guid = Guid.NewGuid().ToString(), ChoiceLabel = OfferLabel, ChoiceText = Text.T(Entry) };
        // Before the farewell, which vanilla keeps last.
        choices.Insert(Math.Max(0, choices.Count - 1), entry);
        Mod.Log("Ray's menu lists the expansion entry.");
    }

    // The answer is built for the moment of asking and replaces the menu.
    private static void Open()
    {
        try
        {
            _handler!.StartDialogue(Build());
            RequestSingle();
            Mod.Log("Ray opened the expansion entry.");
        }
        catch (Exception error) { Mod.Warn("Ray's expansion entry could not be opened: " + error); }
    }

    // With one property his answer is its own line, and the plan opens on it.
    private static void RequestSingle()
    {
        var sites = Eligible();
        if (sites.Length == 1) Request(sites[0]);
    }

    private static void Request(SiteDefinition site)
    {
        if (!Eligible().Contains(site)) { Mod.Warn("Plan of " + site.Base.Id + " not opened at Ray: nothing left to build or not owned."); return; }
        Mod.RequestPlan(site);
        Mod.Log("Ray hands over the plan of " + site.Base.Id + ".");
    }

    private static Conversation Build()
    {
        var sites = Eligible();
        if (sites.Length == 1) return Find(sites[0].VendorContainerId);
        _ray!.Dialogue.BuildAndRegisterContainer(ContainerId, b =>
        {
            b.AddNode("ENTRY", Text.T("Which one?"), choices =>
            {
                foreach (var site in sites)
                    choices.Add(SiteChoice + site.Base.Id, Text.T(site.PropertyName), "PLAN_" + site.Base.Id);
                choices.Add(NeverMind, Text.T("Never mind."));
            });
            foreach (var site in sites) b.AddNode("PLAN_" + site.Base.Id, Text.T(site.VendorLine));
        });
        Mod.Log("Ray asks which of " + sites.Length + " properties to expand.");
        return Find(ContainerId);
    }

    private static Conversation Find(string containerId)
    {
        for (int i = 0; i < _handler!.dialogueContainers.Count; i++)
            if (_handler.dialogueContainers[i].name == containerId) return _handler.dialogueContainers[i];
        throw new InvalidOperationException("S1API did not register the conversation " + containerId + ".");
    }
}
