using System.Collections.Generic;
using DooDesch.Requirements;
using MelonLoader;

namespace Based.Runtime;

// MelonLoader only logs a missing dependency and loads Based anyway, so the player finds an expansion that never
// shows up. The shared Require (Workspace/requirements) tells the install mistakes apart and names the file; the
// vendor says that line. Require reads MelonLoader's registered mods only: touching a type of a missing mod throws
// when the calling method is compiled, not at the line that uses it.
internal static class MissingMods
{
    // Below these Based stays off.
    private static readonly (string Name, string Minimum)[] Required =
    {
        ("MapExpansions", "1.0.0"),
        ("Personnel", "2.4.6"),
        ("Sideload", "1.36.1"),
    };

    // Below these Based runs and warns. Both READMEs and thunderstore/manifest.json name these versions, so a mod
    // manager installs them; a manual install can still carry an older one.
    private static readonly (string Name, string Minimum, string Effect)[] Recommended =
    {
        ("MapExpansions", "1.0.4", "employees can ignore storage and drying racks in the new part of the Sweatshop"),
    };

    internal static string Line { get; private set; } = "";
    internal static bool Any => Line.Length > 0;

    internal static void Check(MelonLogger.Instance log)
    {
        var problems = new List<string>();
        foreach (var (name, minimum) in Required)
        {
            string problem = Require.Installed(name, minimum);
            if (problem != null) problems.Add(problem);
        }
        foreach (var (name, minimum, effect) in Recommended)
            Require.Recommended(name, minimum, effect);
        Line = string.Join(" ", problems);
        if (problems.Count == 0) log.Msg("Required mods are installed: " + string.Join(", ", System.Array.ConvertAll(Required, r => r.Name + " " + r.Minimum + "+")) + ".");
    }
}
