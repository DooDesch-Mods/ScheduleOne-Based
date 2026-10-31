using Based.Core;

namespace Based.Content.Ming;

public static class MingDefinition
{
    public const string BaseId = "ming.sweatshop";
    public const string UpgradeId = "ming.restaurant-wing";
    public const string ExpansionId = "based_ming_upper";
    // Together the price of the game's Storage Unit: the wing doubles the sweatshop's floor but adds no employee beds.
    public const int RightsPrice = 1000;
    public const int ConstructionPrice = 3000;

    public static BaseDefinition Create(int rightsPrice = RightsPrice,
        int constructionPrice = ConstructionPrice) =>
        new(BaseId, "sweatshop", new[]
        {
            new UpgradeDefinition(UpgradeId, rightsPrice, constructionPrice, 4,
                Array.Empty<string>(), new[]
                {
                    new ConstructionPhase("breakthrough", 1, "breakthrough"),
                    new ConstructionPhase("frame", 2, "frame"),
                    new ConstructionPhase("roof", 3, "roof")
                }, new[] { new UpgradeEffect("world-state", ExpansionId, "complete") })
        });
}
