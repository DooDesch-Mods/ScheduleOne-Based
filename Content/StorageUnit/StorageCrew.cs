using Based.Content.SiteCrews;
using Personnel.Model;
using UnityEngine;

namespace Based.Content.StorageUnit;

// Ray's crew at the Storage Unit: a foreman with the plan, a carpenter at the timber and a fitter who cuts and welds
// where StorageWorld.SparkSpot puts the sparks. Every phase stands where that night's site stands.
internal sealed class StorageCrew : ConstructionCrew
{
    internal const string ForemanId = "based_storage_foreman";
    internal const string CarpenterId = "based_storage_carpenter";
    internal const string FitterId = "based_storage_fitter";
    internal static readonly StorageCrew Instance = new();

    // True from the first work hour of a build night to the last.
    internal static bool Working => Instance.WorkingHours;
    // True while the fitter is on site, standing at the spark spot of the phase.
    internal static bool WelderAtSpot => Instance.WorkingHours && Instance.IsVisible(FitterId);

    private const float Ground = 0f, Loft = StorageWorld.UpperFloor, Landing = StorageWorld.LandingFloor;
    // Heights of the work surface above the floor that a hammer strike reaches.
    private const float Bench = .7f;
    private static readonly HashSet<string> WarnedPhases = new(StringComparer.Ordinal);

    // Per phase and worker the two stations the worker walks between, and what it faces at each. The fitter keeps one
    // station: it is the spot under or in front of the sparks. Coordinates follow the site props of StorageWorld.SiteProps.
    private sealed record Station(Vector3 At, Vector3 Face);

    private static readonly Dictionary<string, (int SparkLevel, Station[][] Stations)> Phases = new(StringComparer.Ordinal)
    {
        // The stair stands; the door is cut today. Foreman and carpenter keep to the stair's foot on the grass, clear
        // of the cones (-10.85, 95.3), (-9.55, 95.3), (-10.75, 97.9), the bricks (-9.75, 96.5) and the sign
        // (-11.25, 99.6); the carpenter works the barrel stack at (-12.3, 98.6). The fitter stands on the landing at
        // the window that becomes the door.
        ["loft-stair"] = (1, new[]
        {
            new[] { St(-10.4f, Ground, 97.3f, -10.0f, Bench, 98.9f), St(-10.4f, Ground, 97.3f, -10.2f, Bench, 99.6f) },
            new[] { St(-11.1f, Ground, 98.6f, -12.0f, Bench, 98.6f), St(-10.4f, Ground, 98.7f, -10.4f, Bench, 99.4f) },
            new[] { St(-9.9f, Landing, 105.475f, -9.4f, Landing + 1.1f, 105.475f) }
        }),
        // The door is in; the loft's south wall opens to the hall today. Foreman and carpenter work inside the loft
        // at the table (-5.5, 108.8) and the pallet (-3.3, 112.7); the fitter stands under the top of the south wall.
        ["loft-door"] = (2, new[]
        {
            new[] { St(-5.5f, Loft, 107.9f, -5.5f, Loft + Bench, 108.8f), St(-4.7f, Loft, 107.9f, -5.2f, Loft + Bench, 108.8f) },
            new[] { St(-3.3f, Loft, 111.3f, -3.3f, Loft + Bench, 111.9f), St(-7.2f, Loft, 108.8f, -6.6f, Loft + Bench, 108.8f) },
            new[] { St(-6.5f, Loft, 104.75f, -6.5f, Loft + 2.3f, 104.0f) }
        }),
        // The walkway is open; the east room's front opens today. Foreman and carpenter work on the walkway at the
        // table (-0.35, 108.8) and the pallet (-1, 112.9); the fitter stands north of the table, facing Unit5's front.
        ["east-open"] = (4, new[]
        {
            new[] { St(-0.85f, Loft, 107.15f, -0.35f, Loft + Bench, 108.8f), St(-1.4f, Loft, 107.3f, -0.6f, Loft + Bench, 108.3f) },
            new[] { St(-1.0f, Loft, 111.6f, -1.0f, Loft + Bench, 112.2f), St(-1.15f, Loft, 108.8f, -0.35f, Loft + Bench, 108.8f) },
            new[] { St(-0.35f, Loft, 110.6f, 0.15f, Loft + 1.5f, 110.6f) }
        }),
        // The south room is open; it is finished today. Foreman and carpenter work in the south room at the table
        // (2.6, 98.3, west edge x 2.05) and the pallet (2.4, 95.15); the fitter stands on the walkway beside the
        // elevator. The barrel stack (west face x 1.62, z 99.67 .. 101.13) and the cinderblocks at (1.63, 97.42) and
        // (1.6, 98.2) take the walking map off the strip west of them, so the stations there keep an agent's radius off.
        ["south-open"] = (6, new[]
        {
            new[] { St(1.6f, Loft, 99.5f, 2.5f, Loft + Bench, 98.9f), St(1.2f, Loft, 100.4f, 1.9f, Loft + Bench, 100.4f) },
            new[] { St(2.0f, Loft, 96.5f, 2.0f, Loft + Bench, 95.9f), St(1.6f, Loft, 98.8f, 2.3f, Loft + Bench, 98.8f) },
            new[] { St(-0.3f, Loft, 102.6f, 0.56f, Loft + 1.4f, 102.6f) }
        })
    };

    private static Station St(float x, float y, float z, float faceX, float faceY, float faceZ) =>
        new(new Vector3(x, y, z), new Vector3(faceX, faceY, faceZ));

    protected override string Tag => "storage workers";
    protected override string BaseId => StorageDefinition.BaseId;
    protected override string[] Ids { get; } = { ForemanId, CarpenterId, FitterId };
    protected override string[] Names { get; } = { "Walt Kessler", "Dale Prescott", "Marco Vance" };
    // Inside Unit 1, away from its door.
    protected override Vector3 SpawnPosition => new(-6.6f, .1f, 112f);
    protected override bool Owns(Personnel.PersonnelNpc npc) => npc is StorageWorkerNpc;

    protected override NpcAppearance CreateAppearance(int index)
    {
        var skin = index == 0 ? new Color(0.86f, 0.68f, 0.55f, 1f) : index == 1
            ? new Color(0.45f, 0.30f, 0.21f, 1f) : new Color(0.68f, 0.48f, 0.36f, 1f);
        return new NpcAppearance
        {
            Gender = 0,
            Height = index == 0 ? 0.96f : index == 1 ? 1.06f : 1.0f,
            Weight = index == 0 ? 0.42f : index == 1 ? 0.68f : 0.55f,
            SkinColor = skin,
            LeftEyeLidColor = skin,
            RightEyeLidColor = skin,
            HairPath = index == 1 ? "Avatar/Hair/Balding/Balding" : "Avatar/Hair/BuzzCut/BuzzCut",
            HairColor = index == 0 ? new Color(0.58f, 0.57f, 0.55f, 1f) : index == 1 ? new Color(0.10f, 0.08f, 0.07f, 1f) : new Color(0.36f, 0.24f, 0.12f, 1f),
            EyebrowScale = 1.0f,
            EyebrowThickness = 0.9f,
            LeftEyeTop = 0.5f,
            RightEyeTop = 0.5f,
            LeftEyeBottom = 0.1f,
            RightEyeBottom = 0.1f,
            PupilDilation = 0.55f,
            FaceLayers = new List<NpcLayer>
            {
                new() { Path = "Avatar/Layers/Face/Face_Neutral", Tint = Color.black },
                new() { Path = "Avatar/Layers/Face/FacialHair_Stubble", Tint = Color.black }
            },
            BodyLayers = new List<NpcLayer>
            {
                new() { Path = index == 2 ? "Avatar/Layers/Top/Overalls" : "Avatar/Layers/Top/T-Shirt",
                    Tint = index == 0 ? new Color(0.78f, 0.74f, 0.62f, 1f) : index == 1 ? new Color(0.17f, 0.36f, 0.27f, 1f) : new Color(0.58f, 0.18f, 0.16f, 1f) },
                new() { Path = "Avatar/Layers/Bottom/Jeans", Tint = index == 0 ? new Color(0.33f, 0.30f, 0.26f, 1f) : new Color(0.18f, 0.22f, 0.30f, 1f) }
            },
            Accessories = new List<NpcLayer>
            {
                new() { Path = "Avatar/Accessories/Feet/CombatBoots/CombatBoots", Tint = new Color(0.14f, 0.12f, 0.10f, 1f) }
            }
        };
    }

    protected override string Line(int index, string? phase) => index == 0
        ? Based.UI.Text.T(phase switch
        {
            "loft-stair" => "We're cutting the door today.",
            "loft-door" => "We're opening the loft to the hall today.",
            "east-open" => "We're opening the east room today.",
            "south-open" => "We're finishing the south room today.",
            _ => "Talk to Ray about the expansion."
        })
        : Based.UI.Text.T("Keep the site clear, please.");

    // Only the carpenter hammers; the foreman reads the plan and the fitter cuts.
    protected override bool WantsHammer(int index, bool secondary, string? phase) => index == 1;

    protected override Vector3 WorkPoint(int index, bool secondary, string? phase) => StationOf(index, secondary, phase).At;

    // The fitter faces the spark spot itself, wherever StorageWorld puts it.
    protected override Vector3 ContactPoint(int index, bool secondary, string? phase)
    {
        var station = StationOf(index, secondary, phase);
        if (index != 2 || phase == null || !Phases.TryGetValue(phase, out var entry)) return station.Face;
        var spark = StorageWorld.SparkSpot(entry.SparkLevel);
        return spark?.At ?? station.Face;
    }

    private static Station StationOf(int index, bool secondary, string? phase)
    {
        if (phase == null || !Phases.TryGetValue(phase, out var entry))
        {
            // The first phase's stations stand in for a phase this crew has no stations for.
            if (WarnedPhases.Add(phase ?? "none"))
                MelonLoader.MelonLogger.Warning("storage workers: no stations for phase " + (phase ?? "none") + "; using the loft-stair stations");
            entry = Phases["loft-stair"];
        }
        var worker = entry.Stations[index];
        return worker[secondary && worker.Length > 1 ? 1 : 0];
    }
}

public abstract class StorageWorkerNpc : Personnel.PersonnelNpc { }

public sealed class StorageForeman : StorageWorkerNpc { protected override string DefId => StorageCrew.ForemanId; }
public sealed class StorageCarpenter : StorageWorkerNpc { protected override string DefId => StorageCrew.CarpenterId; }
public sealed class StorageFitter : StorageWorkerNpc { protected override string DefId => StorageCrew.FitterId; }
