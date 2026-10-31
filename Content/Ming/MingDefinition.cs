using Based.Core;

namespace Based.Content.Ming;

public static class MingDefinition
{
    public const string BaseId = "ming.sweatshop";
    public const string UpgradeId = "ming.restaurant-wing";
    public const string ExpansionId = "based_ming_upper";
    public const int ProvisionalRightsPrice = 1000;
    public const int ProvisionalConstructionPrice = 2500;

    public static BaseDefinition Create(int rightsPrice = ProvisionalRightsPrice,
        int constructionPrice = ProvisionalConstructionPrice) =>
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
