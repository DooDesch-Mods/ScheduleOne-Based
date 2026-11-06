using System;
using MelonLoader;

namespace Based.Runtime;

// Based runs from MapExpansions 1.0.0, so a member added later may only be touched by a method that is called once the
// loaded version has it. The check reads the version from MapExpansions' MelonInfo, like Require does, and compares
// numbers only: "1.2.0-beta.1" counts as 1.2.0 and a development build (0.0.0) counts as current.
internal static class MapExpansionsFeatures
{
    // The release that brought PropertyFixtureDefinition, PropertyExtensionDefinition.Fixtures and ObjectMoveDefinition.Optional.
    internal const string FixturesVersion = "1.2.0";

    private static bool? _fixtures;

    /// <summary>The loaded MapExpansions can hold tiles closed under a main-world object. Decided once and logged once.</summary>
    internal static bool Fixtures => _fixtures ??= DetectFixtures();

    private static bool DetectFixtures()
    {
        string? installed = null;
        foreach (MelonBase melon in MelonBase.RegisteredMelons)
            if (melon?.Info != null && string.Equals(melon.Info.Name, "MapExpansions", StringComparison.Ordinal)) { installed = melon.Info.Version; break; }

        if (!TryParse(installed, out Version have))
        {
            Mod.Warn($"The sink corner needs MapExpansions {FixturesVersion}; its version '{installed ?? "not loaded"}' could not be read, so the corner keeps the old tiles.");
            return false;
        }
        if (have != new Version(0, 0, 0) && have < new Version(FixturesVersion))
        {
            Mod.Log($"The sink corner needs MapExpansions {FixturesVersion}; {installed} is loaded, so the corner under the moved sink stays out of the grid.");
            return false;
        }
        // A development build reports 0.0.0 whatever it contains, so the type has to be there as well.
        if (typeof(MapExpansions.MapExpansionsApi).Assembly.GetType("MapExpansions.PropertyFixtureDefinition") == null)
        {
            Mod.Log($"The sink corner needs MapExpansions {FixturesVersion}; {installed} has no fixtures, so the corner under the moved sink stays out of the grid.");
            return false;
        }
        Mod.Log($"MapExpansions {installed} holds the tiles under the moved sink while it stands.");
        return true;
    }

    private static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrEmpty(text)) return false;
        int cut = text.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) text = text.Substring(0, cut);
        return Version.TryParse(text, out version!);
    }
}
