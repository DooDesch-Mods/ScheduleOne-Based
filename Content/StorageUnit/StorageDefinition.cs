using Based.Core;

namespace Based.Content.StorageUnit;

public static class StorageDefinition
{
    public const string BaseId = "ray.storageunit";
    public const string PermitId = "ray.storageunit-permit";
    // A, B and C open the upper level in three steps, D adds the second loading bay.
    public const string LoftId = "ray.storageunit-loft";
    public const string EastId = "ray.storageunit-east";
    public const string SouthId = "ray.storageunit-south";
    public const string BayId = "ray.storageunit-bay";
    // A, B and C change one expansion, each state holding everything built before it; D has its own.
    public const string ExpansionId = "based_storage_upper";
    public const string BayExpansionId = "based_storage_bay";
    public const int PermitPrice = 1000;
    // $2,000 per new employee place, the bay at the price of the Sweatshop's dock.
    public const int LoftPrice = 4000;
    public const int EastPrice = 4000;
    public const int SouthPrice = 2000;
    public const int BayPrice = 1500;

    public static BaseDefinition Create() =>
        new(BaseId, "storageunit", new[]
        {
            new UpgradeDefinition(PermitId, PermitPrice, 0, 0, Array.Empty<string>(),
                Array.Empty<ConstructionPhase>(), Array.Empty<UpgradeEffect>()),
            new UpgradeDefinition(LoftId, 0, LoftPrice, 3, new[] { PermitId }, new[]
            {
                new ConstructionPhase("loft-stair", 1, "a-stair"),
                new ConstructionPhase("loft-door", 2, "a-door")
            }, new[] { new UpgradeEffect("world-state", ExpansionId, "a-complete") }),
            new UpgradeDefinition(EastId, 0, EastPrice, 2, new[] { LoftId }, new[]
            {
                new ConstructionPhase("east-open", 1, "b-open")
            }, new[] { new UpgradeEffect("world-state", ExpansionId, "b-complete") }),
            new UpgradeDefinition(SouthId, 0, SouthPrice, 2, new[] { EastId }, new[]
            {
                new ConstructionPhase("south-open", 1, "c-open")
            }, new[] { new UpgradeEffect("world-state", ExpansionId, "c-complete") }),
            new UpgradeDefinition(BayId, 0, BayPrice, 1, new[] { SouthId },
                Array.Empty<ConstructionPhase>(), new[] { new UpgradeEffect("world-state", BayExpansionId, "complete") })
        });
}
