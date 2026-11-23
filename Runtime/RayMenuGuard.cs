using System.Reflection;
using Based.Core;
using HarmonyLib;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.UI;
using MelonLoader;

namespace Based.Runtime;

// Based adds its entries to Ray's office menu in a postfix of DialogueController.ModifyChoiceList. Another mod that
// patches the same choice list directly can drop them again before the menu shows (Based#80): the menu then lacks
// both entries and nothing is logged. This checks the list the canvas is about to show. When an entry Based added in
// this menu is missing, Based finds the patch of another mod on the choice list of the controller or the handler that
// drops it, removes exactly that patch, names it in a warning and puts its entries back. Patches that keep the
// entries stay, and so do S1API's (the shared API) and our own.
internal static class RayMenuGuard
{
    private static readonly List<DialogueChoiceData> Pending = new();
    private static IntPtr _pendingController;
    private static int _pendingFrame = -1;
    private static bool _cleared;

    internal static void Reset()
    {
        Pending.Clear();
        _pendingController = IntPtr.Zero;
        _pendingFrame = -1;
    }

    // Called right after an entry went into the list of Ray's office menu.
    internal static void Added(DialogueController controller, DialogueChoiceData entry)
    {
        int frame = UnityEngine.Time.frameCount;
        if (frame != _pendingFrame || controller.Pointer != _pendingController) Pending.Clear();
        _pendingFrame = frame;
        _pendingController = controller.Pointer;
        Pending.Add(entry);
    }

    internal static void BeforeDisplay(DialogueHandler diag, Il2CppSystem.Collections.Generic.List<DialogueChoiceData> choices)
    {
        if (Pending.Count == 0) return;
        if (diag == null || choices == null)
        {
            Mod.Warn("Ray's menu could not be checked for Based's entries: the canvas got no handler or no choice list.");
            Reset();
            return;
        }
        var controller = diag.GetComponent<DialogueController>();
        if (controller == null || controller.Pointer != _pendingController)
        {
            Mod.Log("Ray's menu check skipped: the canvas shows the menu of another dialogue controller.");
            Reset();
            return;
        }
        var missing = new List<DialogueChoiceData>();
        foreach (var entry in Pending)
        {
            bool found = false;
            for (int i = 0; i < choices.Count && !found; i++) found = choices[i]?.ChoiceLabel == entry.ChoiceLabel;
            if (!found) missing.Add(entry);
        }
        Reset();
        if (missing.Count == 0) return;

        string labels = string.Join(", ", missing.Select(e => e.ChoiceLabel));
        if (_cleared) Mod.Warn("Ray's menu lost Based's entries (" + labels + ") again after the other mods' patches were removed; they are put back.");
        else
        {
            RemoveDroppingPatches(diag, controller, missing, labels);
            _cleared = true;
        }
        // The canvas shows the handler's copies (its finalChoices), but checks and follows a choice by the same index in
        // the handler's CurrentChoices; an entry missing there shows greyed out and cannot be picked. Both get it.
        var current = diag.CurrentChoices;
        if (current == null || current.Count != choices.Count)
            Mod.Warn("Ray's menu: the handler's choice list has " + (current?.Count.ToString() ?? "no") + " entries, the shown one "
                + choices.Count + "; Based's entries may not be selectable.");
        foreach (var entry in missing)
        {
            // Before the farewell, which vanilla keeps last.
            choices.Insert(Math.Max(0, choices.Count - 1), entry.GetCopy());
            current?.Insert(Math.Max(0, current.Count - 1), entry);
        }
        Mod.Log("Ray's menu: put back " + missing.Count + " entr" + (missing.Count == 1 ? "y" : "ies") + " of Based (" + labels + ").");
    }

    // Each prefix and postfix of another mod on the two choice-list methods is called once on a copy of Based's missing
    // entries. Only a patch that drops one of them from that copy is removed. Patches that cannot be tested this way
    // (transpilers, patches on the node display) are named, not removed.
    private static void RemoveDroppingPatches(DialogueHandler diag, DialogueController controller, List<DialogueChoiceData> missing, string labels)
    {
        string own = Mod.Instance.HarmonyInstance.Id;
        int removed = 0;
        var untested = new List<string>();
        foreach (var original in PathMethods())
        {
            Patches info;
            try { info = HarmonyLib.Harmony.GetPatchInfo(original); }
            catch (Exception error)
            {
                Mod.Warn("The patches on " + Name(original) + " could not be read: " + error.Message);
                continue;
            }
            if (info == null) continue;
            bool choiceList = original.Name == "ModifyChoiceList";
            foreach (var (patch, kind) in Everything(info))
            {
                if (patch.owner == own || IsS1Api(patch)) continue;
                string mod = ModName(patch);
                string method = (patch.PatchMethod?.DeclaringType?.FullName ?? "?") + "." + (patch.PatchMethod?.Name ?? "?");
                string where = method + " of " + mod + " on " + Name(original);
                if (!choiceList || (kind != "prefix" && kind != "postfix"))
                {
                    untested.Add(where + " (" + kind + ")");
                    continue;
                }
                bool? drops = Drops(patch.PatchMethod!, original, diag, controller, missing, out string why);
                if (drops == null)
                {
                    untested.Add(where + " (" + why + ")");
                    continue;
                }
                if (drops == false)
                {
                    Mod.Log("Ray's menu: the patch " + where + " keeps Based's entries; it stays.");
                    continue;
                }
                try
                {
                    new HarmonyLib.Harmony(patch.owner).Unpatch(original, patch.PatchMethod);
                    removed++;
                    Mod.Warn("Removed the patch " + where + ": it dropped Based's entries from Ray's menu. Please report this to the author of " + mod + ".");
                }
                catch (Exception error)
                {
                    Mod.Error("The patch " + where + " drops Based's entries from Ray's menu and could not be removed: " + error.Message);
                }
            }
        }
        if (removed > 0) return;
        Mod.Warn("Ray's menu lost Based's entries (" + labels + "), and no other mod's patch on the choice lists drops them; they are put back. "
            + (untested.Count > 0 ? "Untested patches on the menu's path: " + string.Join("; ", untested) + "." : "No other mod patches the menu's path."));
    }

    // Calls one patch method on a list that holds only Based's entries. True when an entry is gone afterwards, null
    // when the method cannot be called with the arguments a choice-list patch gets.
    private static bool? Drops(MethodInfo method, MethodBase original, DialogueHandler diag, DialogueController controller,
        List<DialogueChoiceData> entries, out string why)
    {
        why = "";
        var list = new Il2CppSystem.Collections.Generic.List<DialogueChoiceData>();
        foreach (var entry in entries) list.Add(entry.GetCopy());
        object? instance = original.DeclaringType == typeof(DialogueController) ? controller : diag.TryCast<ControlledDialogueHandler>();
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        int listAt = -1;
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var type = p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType;
            if (p.Name == "__instance")
            {
                if (instance == null || !type.IsInstanceOfType(instance)) { why = "its __instance is a " + type.Name; return null; }
                args[i] = instance;
            }
            else if (type == typeof(Il2CppSystem.Collections.Generic.List<DialogueChoiceData>)) { args[i] = list; listAt = i; }
            else if (type == typeof(string)) args[i] = "ENTRY";
            else if (p.Name == "__originalMethod") args[i] = original;
            else if (p.Name == "__runOriginal") args[i] = true;
            else if (type.IsValueType) args[i] = Activator.CreateInstance(type);
        }
        if (listAt < 0) { why = "it takes no choice list"; return null; }
        try { method.Invoke(null, args); }
        catch (Exception error)
        {
            why = "the test call threw " + (error.InnerException ?? error).GetType().Name;
            return null;
        }
        if (args[listAt] is not Il2CppSystem.Collections.Generic.List<DialogueChoiceData> after) return true;
        foreach (var entry in entries)
        {
            bool found = false;
            for (int i = 0; i < after.Count && !found; i++) found = after[i]?.ChoiceLabel == entry.ChoiceLabel;
            if (!found) return true;
        }
        return false;
    }

    // Every method a choice of Ray's office menu passes between Based's postfix and the canvas.
    private static IEnumerable<MethodBase> PathMethods()
    {
        var methods = new MethodBase?[]
        {
            AccessTools.DeclaredMethod(typeof(DialogueController), nameof(DialogueController.ModifyChoiceList)),
            AccessTools.DeclaredMethod(typeof(ControlledDialogueHandler), "ModifyChoiceList"),
            AccessTools.DeclaredMethod(typeof(DialogueHandler), nameof(DialogueHandler.ShowNode)),
            AccessTools.DeclaredMethod(typeof(DialogueCanvas), nameof(DialogueCanvas.DisplayDialogueNode)),
        };
        foreach (var method in methods)
        {
            if (method == null) Mod.Warn("A method on the path of Ray's menu was not found; its patches stay unchecked.");
            else yield return method;
        }
    }

    private static IEnumerable<(Patch patch, string kind)> Everything(Patches info)
    {
        foreach (var patch in info.Prefixes) yield return (patch, "prefix");
        foreach (var patch in info.Postfixes) yield return (patch, "postfix");
        foreach (var patch in info.Transpilers) yield return (patch, "transpiler");
        foreach (var patch in info.Finalizers) yield return (patch, "finalizer");
    }

    private static bool IsS1Api(Patch patch)
    {
        string assembly = patch.PatchMethod?.DeclaringType?.Assembly.GetName().Name ?? "";
        return assembly.StartsWith("S1API", StringComparison.OrdinalIgnoreCase);
    }

    private static string ModName(Patch patch)
    {
        foreach (var melon in MelonBase.RegisteredMelons)
            if (melon.HarmonyInstance?.Id == patch.owner) return melon.Info.Name + " " + melon.Info.Version;
        // A mod that patches through a Harmony instance of its own is found by the assembly of its patch method.
        var assembly = patch.PatchMethod?.DeclaringType?.Assembly;
        foreach (var melon in MelonBase.RegisteredMelons)
            if (melon != Mod.Instance && assembly != null && melon.MelonAssembly?.Assembly == assembly) return melon.Info.Name + " " + melon.Info.Version;
        return "'" + patch.owner + "'";
    }

#if DEBUG
    // baseddiag raythief: a stand-in for another mod that drops Based's entries from Ray's menu in a later postfix.
    internal const string ThiefOwner = "based.test.foreign-menu";

    internal static void DebugThief()
    {
        var target = AccessTools.DeclaredMethod(typeof(DialogueController), nameof(DialogueController.ModifyChoiceList));
        var postfix = new HarmonyMethod(AccessTools.DeclaredMethod(typeof(RayMenuThief), nameof(RayMenuThief.Postfix))) { priority = Priority.Last };
        new HarmonyLib.Harmony(ThiefOwner).Patch(target, postfix: postfix);
        Mod.Log("baseddiag raythief: '" + ThiefOwner + "' now drops Based's entries from every choice list.");
    }

    internal static void DebugThiefStatus()
    {
        var target = AccessTools.DeclaredMethod(typeof(DialogueController), nameof(DialogueController.ModifyChoiceList));
        var info = HarmonyLib.Harmony.GetPatchInfo(target);
        string owners = info == null ? "none" : string.Join(", ", Everything(info).Select(p => p.patch.owner).Distinct());
        Mod.Log("baseddiag raythief: owners on DialogueController.ModifyChoiceList: " + owners + ".");
    }
#endif

    private static string Name(MethodBase method) => method.DeclaringType?.Name + "." + method.Name;
}

#if DEBUG
internal static class RayMenuThief
{
    internal static void Postfix(string dialogueLabel, ref Il2CppSystem.Collections.Generic.List<DialogueChoiceData> existingChoices)
    {
        if (existingChoices == null) return;
        for (int i = existingChoices.Count - 1; i >= 0; i--)
            if (existingChoices[i]?.ChoiceLabel?.StartsWith("based_ray_") == true) existingChoices.RemoveAt(i);
    }
}
#endif

// Runs after every other prefix, so the list is the one the canvas shows.
[HarmonyPatch(typeof(DialogueCanvas), nameof(DialogueCanvas.DisplayDialogueNode))]
internal static class RayMenuGuardPatch
{
    // The parameter names must match the target's.
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(DialogueHandler diag, Il2CppSystem.Collections.Generic.List<DialogueChoiceData> choices)
    {
        try { RayMenuGuard.BeforeDisplay(diag, choices); }
        catch (Exception error) { Mod.Warn("Ray's menu could not be checked for Based's entries: " + error); }
    }
}
