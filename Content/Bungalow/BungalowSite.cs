using Based.Core;

namespace Based.Content.Bungalow;

public static class BungalowDefinition
{
    public const string BaseId = "ray.bungalow";
    public const string PermitId = "ray.bungalow-permit";
    public const string StoreyId = "ray.bungalow-storey";
    public const string SunroomId = "ray.bungalow-sunroom";
    public const string CarportId = "ray.bungalow-carport";
    public const string DeckId = "ray.bungalow-deck";
    public const int PermitPrice = 1500;
    public const int StoreyPrice = 6000;
    public const int SunroomPrice = 4000;
    public const int CarportPrice = 1500;
    public const int DeckPrice = 2000;

    public static BaseDefinition Create() =>
        new(BaseId, "bungalow", new[]
        {
            new UpgradeDefinition(PermitId, PermitPrice, 0, 0, Array.Empty<string>(),
                Array.Empty<ConstructionPhase>(), Array.Empty<UpgradeEffect>()),
            new UpgradeDefinition(StoreyId, 0, StoreyPrice, 5, new[] { PermitId }, new[]
            {
                new ConstructionPhase("storey-floor", 1, "floor"),
                new ConstructionPhase("storey-walls", 2, "walls"),
                new ConstructionPhase("storey-roof", 4, "roof")
            }, Array.Empty<UpgradeEffect>()),
            new UpgradeDefinition(SunroomId, 0, SunroomPrice, 3, new[] { PermitId }, new[]
            {
                new ConstructionPhase("sunroom-base", 1, "base"),
                new ConstructionPhase("sunroom-glass", 2, "glass")
            }, Array.Empty<UpgradeEffect>()),
            new UpgradeDefinition(CarportId, 0, CarportPrice, 1, new[] { PermitId },
                Array.Empty<ConstructionPhase>(), Array.Empty<UpgradeEffect>()),
            new UpgradeDefinition(DeckId, 0, DeckPrice, 2, new[] { PermitId },
                Array.Empty<ConstructionPhase>(), Array.Empty<UpgradeEffect>())
        });
}

// Ray sells the builds of the Bungalow: one permit, then one of four builds at a time.
internal static class BungalowSite
{
    internal const string VendorContainerId = "based_bungalow_plan";

    public static SiteDefinition Create() => new()
    {
        Base = BungalowDefinition.Create(),
        UpgradeId = BungalowDefinition.PermitId,
        Menu = true,
        PropertyName = "Bungalow",
        VendorNpcId = Runtime.PlanCopies.RayId,
        VendorContainerId = VendorContainerId,
        VendorLine = "Sure thing. Here's what the lot allows.",
        Title = "Expansion: Bungalow",
        FolderItemId = "based_bungalow_folder",
        BoardSurfaceId = "based-bungalow-plan-board",
        BuyFirst = "Buy the Bungalow first.",
        QuestMarker = new UnityEngine.Vector3(-170f, -3.7f, 115.5f),
        PhaseTitles = new Dictionary<string, string>
        {
            ["storey-floor"] = "Floor",
            ["storey-walls"] = "Walls",
            ["storey-roof"] = "Roof and interior",
            ["sunroom-base"] = "Foundation",
            ["sunroom-glass"] = "Glazing"
        },
        Plan = new PlanRoom[]
        {
            new(PlanRoomKind.Existing, -176f, 109.35f, -170f, 115.35f),
            new(PlanRoomKind.Existing, -176f, 115.5f, -170f, 121.5f),
            new(PlanRoomKind.Existing, -170f, 115.5f, -163.85f, 121.5f, "Bungalow")
        },
        StaffPlaces = new UnityEngine.Vector3[]
        {
            new(-168.2f, -3.7f, 106.6f),
            new(-167.2f, -3.7f, 106.6f),
            new(-168.2f, -3.7f, 105.6f),
            new(-167.2f, -3.7f, 105.6f),
            new(-166.2f, -3.7f, 105.6f)
        },
        FollowUps = new[]
        {
            new SiteFollowUp(BungalowDefinition.StoreyId, "Second storey: Bungalow", "Ready to move in",
                new[] { "Three more employees can work here." })
            {
                Name = "Second storey",
                QuestType = typeof(BungalowStoreyQuest),
                OrderEntry = "Order the second storey from Ray",
                Staff = 3
            },
            new SiteFollowUp(BungalowDefinition.SunroomId, "Sunroom: Bungalow", "Ready to move in",
                new[] { "Two more employees can work here." })
            {
                Name = "Sunroom",
                QuestType = typeof(BungalowSunroomQuest),
                OrderEntry = "Order the sunroom from Ray",
                Staff = 2
            },
            new SiteFollowUp(BungalowDefinition.CarportId, "Double carport: Bungalow", "Ready for deliveries",
                new[] { "A second delivery spot under the roof." })
            {
                Name = "Double carport"
            },
            new SiteFollowUp(BungalowDefinition.DeckId, "Slope deck: Bungalow", "Finished",
                new[] { "A deck over the slope behind the house." })
            {
                Name = "Slope deck"
            }
        }
    };
}

public sealed class BungalowStoreyQuest : Based.Runtime.ConstructionQuest
{
    protected override string BaseId => BungalowDefinition.BaseId;
    protected override string? UpgradeId => BungalowDefinition.StoreyId;
}

public sealed class BungalowSunroomQuest : Based.Runtime.ConstructionQuest
{
    protected override string BaseId => BungalowDefinition.BaseId;
    protected override string? UpgradeId => BungalowDefinition.SunroomId;
}
