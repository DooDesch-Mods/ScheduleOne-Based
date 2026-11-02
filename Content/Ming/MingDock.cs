using MapExpansions;
using UnityEngine;

namespace Based.Content.Ming;

// The Sweatshop's loading dock: a copy of another property's dock in the yard behind the restaurant. It is the
// Sweatshop's first dock, so the delivery app starts to list the property once it stands.
public static class MingDock
{
    public const string ExtensionId = "ming-loading-dock";
    public const string DockId = "street-dock";
    // The IDs and the template decide the dock's saved GUIDs; changing them strands deliveries in old saves.
    private const string TemplateProperty = "bungalow";
    private const int TemplateDock = 0;
    // The yard behind the restaurant, beside its back door; vanilla parks the delivery van on the dock root.
    private static readonly Vector3 Position = new(-58f, -4f, 148.5f);
    private const float Yaw = 90f;
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        if (!MapExpansionsApi.Register(Create()))
            throw new InvalidOperationException("Based could not register the Sweatshop loading dock; inspect the MapExpansions validation errors.");
        _registered = true;
    }

    public static ExpansionDefinition Create()
    {
        var extension = new PropertyExtensionDefinition { Id = ExtensionId, PropertyCode = "sweatshop" };
        extension.Docks.Add(new PropertyDockDefinition
        {
            Id = DockId, Index = 0, TemplatePropertyCode = TemplateProperty, TemplateDockIndex = TemplateDock,
            At = new LocalPoint(Position, Yaw)
        });
        var complete = new WorldStateDefinition { Id = "complete" };
        complete.PropertyExtensions.Add(extension);
        var definition = new ExpansionDefinition
        {
            Id = MingDefinition.DockExpansionId, DisplayName = "Sweatshop loading dock", Version = "1.0.0", Source = null
        };
        definition.WorldStates.Add(complete);
        return definition;
    }
}
