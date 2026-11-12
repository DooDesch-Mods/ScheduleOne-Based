using MapExpansions;
using UnityEngine;

namespace Based.Content.StorageUnit;

// The Storage Unit's second loading bay. Its world state "complete" is the effect of the bay upgrade, so the expansion
// must be registered from the start.
//
// The hall is 6.5 m wide between its west wall (x -8.75) and the front of Unit 3 (x -2.2). Bay 1 moves 0.45 m west and
// 1.85 m north, bay 2, a copy of the Barn's second bay, stands beside it, both 3 m wide. Vanilla sets the van onto the
// dock instead of driving it in, so the 5.3 m entrance stays as it is. The van parks with its centre about 2 m south of
// the dock root, so a root at z 102.35 puts it on z 97.5 to 103.0: inside the hall's north end (z 103.9) and north of
// Geraldine's door (z 95.97 to 97.03).
public static class StorageDock
{
    public const string ExtensionId = "storage-second-bay";
    // The IDs and the template decide the dock's saved GUIDs; changing them strands deliveries in old saves.
    public const string DockId = "hall-bay-2";
    // Barn dock 1 is a vanilla second bay: same layout as the Storage Unit dock, its floor decal reads "2".
    private const string TemplateProperty = "barn";
    private const int TemplateDock = 1;
    private const string BayOnePath = "Region_Northtown/Storage warehouse/Storage Unit/Loading Dock";
    // The vanilla dock root stands at (-6.75, 0.1, 100.5) with yaw 180; its marking runs from x -8.25 to -5.25.
    private static readonly Vector3 BayOneShift = new(-.45f, 0f, 1.85f);
    private static readonly Vector3 BayTwo = new(-3.95f, .1f, 102.35f);
    private const float Yaw = 180f;
    // Vanilla's employee idle point (2) stands at (-8.29, 0.1, 102.79), where the moved bay's van now parks. It goes to
    // the wall south of the bay, outside the van and Geraldine's evening path (z 93.6 to 96.5).
    private const string IdlePointPath = "@Properties/StorageUnit/EmployeeIdlePoints/GameObject (2)";
    private static readonly Vector3 IdlePointShift = new(-.05f, 0f, -6.2f);
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        if (!MapExpansionsApi.Register(Create()))
            throw new InvalidOperationException("Based could not register the Storage Unit loading bay; inspect the MapExpansions validation errors.");
        _registered = true;
    }

    public static ExpansionDefinition Create()
    {
        var complete = new WorldStateDefinition { Id = "complete" };
        complete.Moves.Add(new ObjectMoveDefinition { Id = "bay-1", Path = BayOnePath, Offset = BayOneShift });
        complete.Moves.Add(new ObjectMoveDefinition { Id = "idle-point-2", Path = IdlePointPath, Offset = IdlePointShift, Optional = true });
        var extension = new PropertyExtensionDefinition { Id = ExtensionId, PropertyCode = StorageCode };
        extension.Docks.Add(new PropertyDockDefinition
        {
            Id = DockId, Index = 1, TemplatePropertyCode = TemplateProperty, TemplateDockIndex = TemplateDock,
            At = new LocalPoint(BayTwo, Yaw)
        });
        complete.PropertyExtensions.Add(extension);
        var definition = new ExpansionDefinition
        {
            Id = StorageDefinition.BayExpansionId, DisplayName = "Storage Unit second loading bay", Version = "0.1.0", Source = null
        };
        definition.WorldStates.Add(complete);
        return definition;
    }

    private const string StorageCode = "storageunit";
}
