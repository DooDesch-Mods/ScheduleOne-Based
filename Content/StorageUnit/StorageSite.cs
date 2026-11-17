namespace Based.Content.StorageUnit;

// Ray sells the builds of the Storage Unit: one permit, then the upper level in three steps and a second loading bay.
internal static class StorageSite
{
    internal const string VendorContainerId = "based_storage_plan";

    public static SiteDefinition Create() => new()
    {
        Base = StorageDefinition.Create(),
        UpgradeId = StorageDefinition.PermitId,
        Menu = true,
        PropertyName = "Storage Unit",
        VendorNpcId = Runtime.PlanCopies.RayId,
        VendorContainerId = VendorContainerId,
        VendorLine = "Sure. The whole upper floor is empty, nobody rents it.",
        Title = "Expansion: Storage Unit",
        FolderItemId = "based_storage_folder",
        BoardSurfaceId = "based-storage-plan-board",
        BuyFirst = "Buy the Storage Unit first.",
        QuestMarker = new UnityEngine.Vector3(-9.84f, 0f, 98.5f),
        PhaseTitles = new Dictionary<string, string>
        {
            ["loft-stair"] = "Steel stairs",
            ["loft-door"] = "Door and partitions",
            ["east-open"] = "Opening to the walkway",
            ["south-open"] = "Opening to the south"
        },
        Plan = new PlanRoom[]
        {
            new(PlanRoomKind.Existing, -8.8f, 103.8f, -3.8f, 114f, "Storage Unit")
        },
        // Outside in the yard west of the hall, where the loft's stair rises. The loft's pair stands at the fire barrel,
        // the east room's pair on the grass in front of the stair's foot, the south room's one joins the fire. The
        // barrel's ring keeps clear of the lamp post's base (x -12.5 .. -11.5, z 100.5 .. 101.5) and the bush north of
        // it (from z 103.08); the stair's pair leaves the walk to the first step (x -9.84, z 98.4) between them and
        // stands north of Geraldine's evening path (z 93.6 to 96.5) and the bus stop (to z 97.39).
        StaffPlaces = new StaffPlace[]
        {
            new(new(-13.25f, 0.1f, 102.55f), StorageWorld.FireSpot),
            new(new(-11.5f, 0.1f, 102.7f), StorageWorld.FireSpot),
            new(new(-9.25f, 0.1f, 97.6f), new(-10.6f, 0.1f, 97.9f)),
            new(new(-10.6f, 0.1f, 97.9f), new(-9.25f, 0.1f, 97.6f)),
            new(new(-12.95f, 0.1f, 101.6f), StorageWorld.FireSpot)
        },
        RegisterWorld = StorageWorld.Register,
        Crew = StorageCrew.Instance,
        FollowUps = new[]
        {
            new SiteFollowUp(StorageDefinition.LoftId, "Loft: Storage Unit", "Ready to move in",
                new[] { "Two more employees can work here.", "An outside steel stair leads up." })
            {
                Name = "Loft above the unit",
                QuestType = typeof(StorageLoftQuest),
                OrderEntry = "Order the loft from Ray",
                Staff = 2,
                Floor = StorageWorld.UpperFloor,
                Plan = new PlanRoom[]
                {
                    new(PlanRoomKind.Existing, -8.8f, 103.8f, -3.8f, 114f),
                    new(PlanRoomKind.New, -8.75f, 103.8f, -2.2f, 113.8f, "Loft"),
                    new(PlanRoomKind.New, -10.65f, 98.7f, -9.01f, 106.3f)
                }
            },
            new SiteFollowUp(StorageDefinition.EastId, "East units: Storage Unit", "Ready to move in",
                new[] { "Two more employees can work here." })
            {
                Name = "East units",
                QuestType = typeof(StorageEastQuest),
                OrderEntry = "Order the east units from Ray",
                Staff = 2,
                Floor = StorageWorld.UpperFloor,
                Plan = new PlanRoom[]
                {
                    new(PlanRoomKind.Existing, -8.75f, 103.8f, -2.2f, 113.8f, "Loft"),
                    new(PlanRoomKind.New, -2.2f, 103.8f, 3.2f, 113.8f, "Expansion")
                }
            },
            new SiteFollowUp(StorageDefinition.SouthId, "South units: Storage Unit", "Ready to move in",
                new[] { "One more employee can work here." })
            {
                Name = "South units",
                QuestType = typeof(StorageSouthQuest),
                OrderEntry = "Order the south units from Ray",
                Staff = 1,
                Floor = StorageWorld.UpperFloor,
                Plan = new PlanRoom[]
                {
                    new(PlanRoomKind.Existing, -8.75f, 103.8f, 3.2f, 113.8f, "Loft"),
                    new(PlanRoomKind.New, -2.2f, 94f, 3.2f, 103.8f, "Expansion")
                }
            },
            new SiteFollowUp(StorageDefinition.BayId, "Second loading bay: Storage Unit", "Ready for deliveries",
                new[] { "A second delivery spot in the hall." })
            {
                Name = "Second loading bay"
            }
        }
    };
}

public sealed class StorageLoftQuest : Based.Runtime.ConstructionQuest
{
    protected override string BaseId => StorageDefinition.BaseId;
    protected override string? UpgradeId => StorageDefinition.LoftId;
}

public sealed class StorageEastQuest : Based.Runtime.ConstructionQuest
{
    protected override string BaseId => StorageDefinition.BaseId;
    protected override string? UpgradeId => StorageDefinition.EastId;
}

public sealed class StorageSouthQuest : Based.Runtime.ConstructionQuest
{
    protected override string BaseId => StorageDefinition.BaseId;
    protected override string? UpgradeId => StorageDefinition.SouthId;
}
