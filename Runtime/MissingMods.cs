using DooDesch.Requirements;

namespace Based.Runtime;

// MelonLoader only logs a missing dependency and loads Based anyway, so the player finds an expansion that never
// shows up. The shared Require (Workspace/requirements) tells the install mistakes apart and names the file; the
// vendor says that line. Require reads MelonLoader's registered mods only: touching a type of a missing mod throws
// when the calling method is compiled, not at the line that uses it.
//
// The minimums are the versions in thunderstore/manifest.json; below them Based stays off. MapExpansions is the
// exception: Based runs from 1.0.0 and only warns below the manifest version.
internal static class MissingMods
{
    internal static string Line { get; private set; } = "";
    internal static bool Any => Line.Length > 0;

    internal static void Check() =>
        Line = Require.Check(new Need("MapExpansions")
        {
            Minimum = "1.0.0",
            Effect = "employees can ignore storage, drying racks and the loading dock in the new part of the Sweatshop",
        }) ?? "";
}
