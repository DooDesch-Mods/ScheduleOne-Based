using System;
using System.Collections.Generic;
using System.Linq;
using Based.UI;
using MelonLoader;

namespace Based.Runtime;

// MelonLoader only logs a missing dependency and loads Based anyway, so the player finds an expansion that never
// shows up. This names what is missing at the vendor instead. It reads assembly names only: touching a type of a
// missing mod throws when the calling method is compiled, not at the line that uses it.
internal static class MissingMods
{
    // The same versions as the Requirements list in both READMEs.
    private static readonly (string Name, Version Minimum)[] Required =
    {
        ("MapExpansions", new Version(1, 0, 0)),
        ("Personnel", new Version(2, 4, 6)),
        ("Sideload", new Version(1, 36, 1)),
    };

    // A build without a release tag reports 0.0.0; it is built from current source, not an old release.
    private static readonly Version DevelopmentBuild = new(0, 0, 0, 0);

    internal static string Line { get; private set; } = "";
    internal static bool Any => Line.Length > 0;

    internal static void Check(MelonLogger.Instance log)
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName()).ToList();
        var missing = new List<string>();
        foreach (var (name, minimum) in Required)
        {
            var found = loaded.FirstOrDefault(a => a.Name == name);
            string need = name + " " + minimum + "+";
            if (found?.Version == null)
            {
                log.Error(name + " is not installed; " + minimum + " or newer is required.");
                missing.Add(need);
            }
            else if (found.Version == DevelopmentBuild)
                log.Msg(name + " is a development build; its version is not checked.");
            else if (found.Version < minimum)
            {
                log.Error(name + " " + found.Version + " is too old; " + minimum + " or newer is required.");
                missing.Add(need);
            }
            else log.Msg(name + " " + found.Version + " meets the minimum " + minimum + ".");
        }
        Line = missing.Count == 0 ? "" : Text.T("Based needs these mods: {0}.", string.Join(", ", missing));
    }
}
