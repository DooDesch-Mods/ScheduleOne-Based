#if SNITCH
using System.Linq;
using Snitch.Api;

namespace Based.Profiling
{
    internal static class SnitchProbe
    {
        public static void Register()
        {
            Panel panel = Profiler.RegisterPanel("Based", "Based");
            panel.Text(() => string.Join("\n", Mod.Sites.Select(site =>
            {
                var state = Mod.Engine.GetUpgrade(site.Base.Id, site.UpgradeId);
                return site.Base.Id + ": " + state.Stage + ", night " + state.CompletedCycles + "/" + site.Upgrade.ConstructionCycles;
            })));
            panel.Text(() => Mod.Failure.Length > 0 ? "Failure: " + Mod.Failure : Mod.HeldBy.Length > 0 ? "Held by " + Mod.HeldBy : "No failure");
            panel.Counter("Workers visible", () => Mod.Sites.Sum(site => site.Crew?.ActiveCount ?? 0));
            panel.Counter("Revision", () => Mod.Engine.Revision);
            panel.Log();
        }
    }
}
#endif
