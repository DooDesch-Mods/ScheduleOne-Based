using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.NPCs;
using UnityEngine;
using UnityEngine.Events;

namespace Based.Runtime;

// The vendor's line about missing mods, built from game types only. S1API can be the missing mod, so nothing here
// may touch it: the NPC comes from the game's registry and the conversation is assembled the way S1API would.
internal static class MissingModsNotice
{
    private const int ChoicePriority = 55;

    // Adds the choice once per call; false while the vendor is not spawned yet. The caller keeps track of bound vendors.
    internal static bool Bind(string npcId, string containerId, string choiceText, string line, Action<string> chosen)
    {
        var npc = NPCManager.GetNPC(npcId);
        if (npc == null) return false;
        var controller = npc.gameObject.GetComponentInChildren<DialogueController>(true);
        var handler = npc.gameObject.GetComponentInChildren<DialogueHandler>(true);
        if (controller == null || handler == null) throw new InvalidOperationException(npcId + " has no dialogue controller or handler.");

        var container = ScriptableObject.CreateInstance<Conversation>();
        container.name = containerId;
        var nodes = new Il2CppSystem.Collections.Generic.List<DialogueNodeData>();
        nodes.Add(new DialogueNodeData
        {
            Guid = Guid.NewGuid().ToString(),
            DialogueNodeLabel = "ENTRY",
            DialogueText = line,
            Position = Vector2.zero,
            choices = new Il2CppReferenceArray<DialogueChoiceData>(0)
        });
        container.DialogueNodeData = nodes;
        container.NodeLinks = new Il2CppSystem.Collections.Generic.List<NodeLinkData>();
        container.SetAllowExit(true);
        handler.dialogueContainers.Add(container);

        var choice = new DialogueController.DialogueChoice
        {
            ChoiceText = choiceText,
            Enabled = true,
            Conversation = container,
            Priority = ChoicePriority,
            onChoosen = new UnityEvent()
        };
        choice.onChoosen.AddListener((Action)(() => chosen(line)));
        controller.AddDialogueChoice(choice, ChoicePriority);
        return true;
    }
}
