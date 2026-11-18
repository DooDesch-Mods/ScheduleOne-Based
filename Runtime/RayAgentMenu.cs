using Based.Core;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Schedules;

namespace Based.Runtime;

// Decides whether a choice list is the first menu of Ray's estate agent conversation, the menu his work schedule
// shows at the realty office, where the controller's own choices stay hidden. Every reason to refuse a list of
// Ray's is logged once per load, so a player log shows which check stopped the entry.
// The menu is matched by name or as the conversation one of Ray's schedule events opens, so a schedule or another
// mod that swaps in a copy of the estate agent conversation still gets the entry.
internal static class RayAgentMenu
{
    private static readonly HashSet<string> Reported = new();

    internal static void Reset() => Reported.Clear();

    internal static bool Accepts(string owner, DialogueController? bound, DialogueController controller, string nodeLabel)
    {
        if (controller == null) return false;
        if (bound == null || controller.Pointer != bound.Pointer)
        {
            // Every NPC's menu passes through here; only Ray's is worth a line.
            if (!IsRay(controller)) return false;
            if (bound == null)
            {
                Once(owner + ":unbound", owner + ": Ray's menu opened before his dialogue was extended; no entry this time.", false);
                return false;
            }
            Once(owner + ":controller", owner + ": Ray talks through another dialogue controller than the one bound at load; the entry follows it.", true);
        }
        // Follow-up nodes of the conversation keep their own choices.
        if (nodeLabel != "ENTRY") return false;
        var active = DialogueHandler.ActiveDialogue;
        if (active == null)
        {
            Once(owner + ":noactive", owner + ": Ray's menu opened without an active conversation; no entry added.", true);
            return false;
        }
        if (active.name == PlanCopies.AgentConversation) return true;
        if (IsScheduleOverride(controller, active))
        {
            Once(owner + ":copy:" + active.name, owner + ": Ray's office menu is the conversation '" + active.name + "' instead of "
                + PlanCopies.AgentConversation + "; the entry goes there.", false);
            return true;
        }
        Once(owner + ":conv:" + active.name, owner + ": Ray's conversation '" + active.name + "' is not his office menu; there the entry is one of the controller's choices.", false);
        return false;
    }

    private static bool IsRay(DialogueController controller)
    {
        var npc = controller.GetComponentInParent<NPC>();
        if (npc == null) return false;
        try { return npc.ID == PlanCopies.RayId; }
        catch (Exception error)
        {
            Once("ray-id", "An NPC's id could not be read while checking for Ray's menu: " + error.Message, true);
            return false;
        }
    }

    // The controller's override is no help here: starting the conversation enables the dialogue behaviour, which
    // interrupts the schedule event and clears the override before the first menu is built. The event keeps the
    // conversation it sets, so the active conversation is compared with that.
    private static bool IsScheduleOverride(DialogueController controller, Conversation active)
    {
        var npc = controller.GetComponentInParent<NPC>();
        if (npc == null) return false;
        foreach (var location in npc.GetComponentsInChildren<NPCEvent_LocationDialogue>(true))
            if (location.DialogueOverride != null && location.DialogueOverride.Pointer == active.Pointer) return true;
        return false;
    }

    private static void Once(string key, string line, bool warn)
    {
        if (!Reported.Add(key)) return;
        if (warn) Mod.Warn(line);
        else Mod.Log(line);
    }
}
