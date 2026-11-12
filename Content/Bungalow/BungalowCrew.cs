using Based.Content.SiteCrews;
using Based.Core;
using Personnel.Model;
using UnityEngine;

namespace Based.Content.Bungalow;

public static class BungalowWorkers
{
    public const string LumberjackId = "based_bungalow_lumberjack";
    public const string CarpenterId = "based_bungalow_carpenter";
}

// The day the carport is ordered, a worker clears the pines on the spot; the night after, they are gone. The game has
// no axe or saw, so he swings the machete. On the day the deck's stilts stand, a carpenter hammers at its beams from the
// gravel. The sunroom takes both: the day it is ordered, the woodcutter fells the pines in its footprint and before its door; on the day
// its frames stand, the carpenter works at the west front.
internal sealed class BungalowCrew : ConstructionCrew
{
    private const int Lumberjack = 0, Carpenter = 1;
    private static readonly string[] IdList = { BungalowWorkers.LumberjackId, BungalowWorkers.CarpenterId };
    private static readonly string[] NameList = { "Walt Becker", "Ron Mercer" };

    // On the gravel east of the deck, facing the outer beam.
    private static readonly Vector3 DeckSouthStand = new(-187.45f, -4.03f, 111.3f);
    private static readonly Vector3 DeckSouthBeam = new(-188.2f, -3.85f, 111.3f);
    private static readonly Vector3 DeckNorthStand = new(-187.45f, -4.0f, 114.6f);
    private static readonly Vector3 DeckNorthBeam = new(-188.2f, -3.85f, 114.6f);

    // North of two of the pines, on the ground measured there; the contact is the bark at chest height.
    private static readonly Vector3 WestStand = new(-165.9f, -3.74f, 104.95f);
    private static readonly Vector3 WestBark = new(-165.9f, -2.84f, 104.35f);
    private static readonly Vector3 EastStand = new(-161.5f, -3.91f, 105.05f);
    private static readonly Vector3 EastBark = new(-161.5f, -3.01f, 104.45f);

    // North of the two pines in the sunroom's footprint, on the flat gravel at -4.0.
    private static readonly Vector3 SunroomEastStand = new(-178.56f, -4f, 118.36f);
    private static readonly Vector3 SunroomEastBark = new(-178.56f, -3.1f, 117.76f);
    private static readonly Vector3 SunroomWestStand = new(-182.53f, -4f, 118.85f);
    private static readonly Vector3 SunroomWestBark = new(-182.53f, -3.1f, 118.25f);

    // Outside the sunroom's west front, facing its frames.
    private static readonly Vector3 FrameSouthStand = new(-183.75f, -4f, 112.5f);
    private static readonly Vector3 FrameSouthPost = new(-183.05f, -3f, 112.5f);
    private static readonly Vector3 FrameNorthStand = new(-183.75f, -4f, 117.8f);
    private static readonly Vector3 FrameNorthPost = new(-183.05f, -3f, 117.8f);

    protected override string Tag => "bungalow workers";
    protected override string BaseId => BungalowDefinition.BaseId;
    // No foreman: nobody carries the plan or takes tips.
    protected override int ForemanIndex => -1;
    protected override string[] Ids => IdList;
    protected override string[] Names => NameList;
    protected override string Region => "Westville";
    protected override Vector3 SpawnPosition => WestStand;
    protected override bool Owns(Personnel.PersonnelNpc npc) => npc is BungalowWorkerNpc;
    protected override bool WorksOn(UpgradeStage stage, string? phase) =>
        stage == UpgradeStage.Constructing && (Upgrade == BungalowDefinition.CarportId
            || Upgrade == BungalowDefinition.DeckId && phase == "deck-stilts"
            || Upgrade == BungalowDefinition.SunroomId && (phase == null || phase == "sunroom-glass"));

    protected override bool OnJob(int index, string? phase) => index == (IsCarpentry(phase) ? Carpenter : Lumberjack);

    private bool IsCarpentry(string? phase) =>
        Upgrade == BungalowDefinition.DeckId || Upgrade == BungalowDefinition.SunroomId && phase != null;

    protected override NpcAppearance CreateAppearance(int index)
    {
        var skin = new Color(0.76f, 0.57f, 0.44f, 1f);
        return new NpcAppearance
        {
            Gender = 0,
            Height = 1.05f,
            Weight = 0.7f,
            SkinColor = skin,
            LeftEyeLidColor = skin,
            RightEyeLidColor = skin,
            HairPath = "Avatar/Hair/BuzzCut/BuzzCut",
            HairColor = new Color(0.33f, 0.22f, 0.13f, 1f),
            EyebrowScale = 1.1f,
            EyebrowThickness = 1.2f,
            LeftEyeTop = 0.5f,
            RightEyeTop = 0.5f,
            LeftEyeBottom = 0.1f,
            RightEyeBottom = 0.1f,
            PupilDilation = 0.6f,
            FaceLayers = new List<NpcLayer>
            {
                new() { Path = "Avatar/Layers/Face/Face_Neutral", Tint = Color.black },
                new() { Path = "Avatar/Layers/Face/FacialHair_Stubble", Tint = Color.black }
            },
            BodyLayers = new List<NpcLayer>
            {
                new() { Path = "Avatar/Layers/Top/T-Shirt", Tint = index == Carpenter ? new Color(0.20f, 0.36f, 0.24f, 1f) : new Color(0.55f, 0.12f, 0.10f, 1f) },
                new() { Path = "Avatar/Layers/Bottom/Jeans", Tint = new Color(0.22f, 0.27f, 0.33f, 1f) }
            },
            Accessories = new List<NpcLayer>
            {
                new() { Path = "Avatar/Accessories/Feet/CombatBoots/CombatBoots", Tint = new Color(0.22f, 0.16f, 0.11f, 1f) }
            }
        };
    }

    protected override Vector3 WorkPoint(int index, bool secondary, string? phase)
    {
        bool sunroom = Upgrade == BungalowDefinition.SunroomId;
        if (index == Carpenter)
            return sunroom ? secondary ? FrameNorthStand : FrameSouthStand : secondary ? DeckNorthStand : DeckSouthStand;
        return sunroom ? secondary ? SunroomWestStand : SunroomEastStand : secondary ? EastStand : WestStand;
    }

    protected override Vector3 ContactPoint(int index, bool secondary, string? phase)
    {
        bool sunroom = Upgrade == BungalowDefinition.SunroomId;
        if (index == Carpenter)
            return sunroom ? secondary ? FrameNorthPost : FrameSouthPost : secondary ? DeckNorthBeam : DeckSouthBeam;
        return sunroom ? secondary ? SunroomWestBark : SunroomEastBark : secondary ? EastBark : WestBark;
    }

    protected override bool WantsHammer(int index, bool secondary, string? phase) => true;
    protected override string Tool(int index) => index == Carpenter ? base.Tool(index) : "weapons/machete/Machete_AvatarEquippable";
    protected override bool GrabsBetweenSwings(int index) => index == Carpenter;
    protected override string Line(int index, string? phase) => Upgrade == BungalowDefinition.SunroomId
        ? index == Carpenter ? "Frames are up. The glass goes in tonight." : "Three pines in the way of the sunroom. Not for long."
        : index == Carpenter ? "Stilts are in. The boards go on tonight." : "These pines have to go before the roof goes up.";
}

public abstract class BungalowWorkerNpc : Personnel.PersonnelNpc { }

public sealed class BungalowLumberjack : BungalowWorkerNpc { protected override string DefId => BungalowWorkers.LumberjackId; }

public sealed class BungalowCarpenter : BungalowWorkerNpc { protected override string DefId => BungalowWorkers.CarpenterId; }
