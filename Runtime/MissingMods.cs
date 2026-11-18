using System;
using System.Linq;
using DooDesch.Requirements;

namespace Based.Runtime;

// MelonLoader only logs a missing dependency and loads Based anyway, so the player finds an expansion that never
// shows up. The shared Require (Workspace/requirements) tells the install mistakes apart and names the file; the
// vendor says that line. Require reads MelonLoader's registered mods only: touching a type of a missing mod throws
// when the calling method is compiled, not at the line that uses it.
//
// The minimums are the versions in thunderstore/manifest.json; below them Based stays off. That includes
// MapExpansions: the Storage Unit sets CopyDefinition.Tilt and the 1.3.0 cuts and grids, the Bungalow the 1.4.0 weather
// cover, grass clearing and tree removal, all while the sites register, so an older MapExpansions would stop the
// Sweatshop as well. The lamps of both need the copied room switches of 1.4.0; on 1.3.0 they would hang without a switch.
// The Bungalow storey's lifted shell needs the copy colliders of 1.5.0, its garden the ground paint; before it the shell lets rain and the player through.
internal static class MissingMods
{
    internal static string Line { get; private set; } = "";
    internal static bool Any => Line.Length > 0;
    // Whether the S1API assembly is in the process. Without it nothing that touches an S1API type may run.
    internal static bool S1ApiLoaded { get; private set; }

    internal static void Check()
    {
        // The file is S1API.Il2Cpp.MelonLoader.dll, but the assembly inside it is named S1API.
        S1ApiLoaded = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name is "S1API" or "S1API.Il2Cpp.MelonLoader");
        if (!S1ApiLoaded) MelonLoader.MelonLogger.Warning("The S1API assembly is not loaded; only the missing-mods notice runs.");
        Line = Require.Check() ?? "";
    }
}
