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
        QuestMarker = new UnityEngine.Vector3(-9.84f, 0f, 97.5f),
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
        // Along the north wall of Unit 1, away from the door and the roller door.
        StaffPlaces = new UnityEngine.Vector3[]
        {
            new(-8.2f, 0.1f, 113.4f),
            new(-7.4f, 0.1f, 113.4f),
            new(-6.6f, 0.1f, 113.4f),
            new(-5.8f, 0.1f, 113.4f),
            new(-5.0f, 0.1f, 113.4f)
        },
        RegisterWorld = StorageWorld.Register,
        FollowUps = new[]
        {
            new SiteFollowUp(StorageDefinition.LoftId, "Loft: Storage Unit", "Ready to move in",
                new[] { "Two more employees can work here.", "An outside steel stair leads up." })
            {
                Name = "Loft above the unit",
                QuestType = typeof(StorageLoftQuest),
                OrderEntry = "Order the loft from Ray",
                Staff = 2,
                Plan = new PlanRoom[]
                {
                    new(PlanRoomKind.Existing, -8.8f, 103.8f, -3.8f, 114f),
                    new(PlanRoomKind.New, -8.75f, 103.8f, -2.2f, 113.8f, "Loft"),
                    new(PlanRoomKind.New, -10.65f, 97.7f, -9.01f, 105.3f)
                }
            },
            new SiteFollowUp(StorageDefinition.EastId, "East units: Storage Unit", "Ready to move in",
                new[] { "Two more employees can work here." })
            {
                Name = "East units",
                QuestType = typeof(StorageEastQuest),
                OrderEntry = "Order the east units from Ray",
                Staff = 2,
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
