using System.Collections.Generic;
using DooDesch.Localization;

namespace Based.UI;

internal static class Text
{
    internal static string T(string value, params object[] arguments) => L10n.T(value, arguments);
    internal static void Register() => L10n.Register("de", new Dictionary<string, string>
    {
        ["More room above Ming"] = "Mehr Platz über Ming",
        ["Here is the plan for the rest of the floor."] = "Hier ist der Plan für den Rest der Etage.",
        ["I would like more space upstairs."] = "Ich möchte oben mehr Platz schaffen.",
        ["Buy the existing room from Ming first."] = "Kaufe zuerst das bestehende Zimmer bei Ming.",
        ["Waiting for the host's construction plan."] = "Warte auf den Bauplan des Hosts.",
        ["Clear the marked doorway area before ordering construction."] = "Räume den markierten Bereich am Durchbruch vor dem Bauauftrag frei.",
        ["Move {0} away from the doorway before ordering construction."] = "Räume {0} am Durchbruch vor dem Bauauftrag weg.",
        ["Purchase requested."] = "Kauf angefragt.",
        ["Expansion rights"] = "Ausbaurechte",
        ["Order construction"] = "Bau beauftragen",
        ["Four nights"] = "Vier Nächte",
        ["Breakthrough"] = "Durchbruch",
        ["Frame"] = "Rohbau",
        ["Roof and interior"] = "Dach und Innenausbau",
        ["Ready to use"] = "Bezugsfertig",
        ["Construction confirmed"] = "Bauauftrag bestätigt",
        ["Rights acquired"] = "Ausbaurechte erworben",
        ["Plan your expansion"] = "Plane deinen Ausbau",
        ["Existing room"] = "Bestehendes Zimmer",
        ["New production space"] = "Neue Produktionsfläche",
        ["Your current room stays usable. Keep the marked doorway clear during construction."] = "Dein jetziges Zimmer bleibt nutzbar. Halte den markierten Durchbruch während der Bauarbeiten frei.",
        ["Ming sells the rest of the floor and the expansion rights. Construction is a separate order."] = "Ming verkauft dir den übrigen Bereich und die Ausbaurechte. Den Bau beauftragst du separat.",
        ["Materials and the three-person crew are included. Sleep anywhere to advance the work."] = "Material und drei Bauarbeiter sind enthalten. Jede abgeschlossene Nacht bringt den Bau voran, egal wo du schläfst.",
        ["No delivery dock is blocked at this property."] = "An dieser Immobilie wird kein Lieferplatz gesperrt.",
        ["Buy rights"] = "Rechte kaufen",
        ["Confirm construction"] = "Bau verbindlich beauftragen",
        ["Close"] = "Schließen",
        ["Work in progress"] = "Bauarbeiten laufen",
        ["Complete"] = "Fertig",
        ["Construction paused because the expansion could not be applied."] = "Bau angehalten, weil die Erweiterung nicht angewendet werden konnte.",
        ["Purchase the existing room first"] = "Zuerst das bestehende Zimmer kaufen",
        ["A payment needs attention"] = "Eine Zahlung muss geklärt werden",
    });
}
