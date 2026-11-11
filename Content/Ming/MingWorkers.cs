using Based.Core;
using Personnel.Model;
using S1API.Entities;
using UnityEngine;

namespace Based.Content.Ming;

public static class MingWorkers
{
    public const string ForemanId = "based_ming_foreman";
    public const string CarpenterId = "based_ming_carpenter";
    public const string FitterId = "based_ming_fitter";
}

// The Sweatshop crew: a foreman with the plan, a carpenter and a fitter, on the three stages of the wing.
internal sealed class MingCrewPlan : CrewPlan
{
    private static readonly string[] IdList = { MingWorkers.ForemanId, MingWorkers.CarpenterId, MingWorkers.FitterId };
    private static readonly string[] NameList = { "Frank Mercer", "Joe Ellis", "Luis Moreno" };

    public override string LogName => "workers";
    public override string[] Ids => IdList;
    public override string[] Names => NameList;
    public override string Region => "Northtown";
    public override Vector3 SpawnPosition => new(-63.949f, MingWorld.FloorY, 140.19f);
    public override bool Owns(NPC npc) => npc is MingWorkerNpc;
    public override bool WorksOn(string upgrade, UpgradeStage stage, string? phase) => stage == UpgradeStage.Constructing && phase != null;

    public override NpcAppearance Appearance(int index)
    {
        var skin = index == 0 ? new Color(0.72f, 0.53f, 0.40f, 1f) : index == 1
            ? new Color(0.79f, 0.59f, 0.43f, 1f) : new Color(0.56f, 0.39f, 0.28f, 1f);
        return new NpcAppearance
        {
            Gender = 0,
            Height = index == 0 ? 1.02f : index == 1 ? 0.98f : 1.04f,
            Weight = index == 0 ? 0.63f : 0.5f,
            SkinColor = skin,
            LeftEyeLidColor = skin,
            RightEyeLidColor = skin,
            HairPath = index == 0 ? "Avatar/Hair/Balding/Balding" : "Avatar/Hair/BuzzCut/BuzzCut",
            HairColor = index == 0 ? new Color(0.24f, 0.20f, 0.17f, 1f) : new Color(0.15f, 0.11f, 0.08f, 1f),
            EyebrowScale = 1.05f,
            EyebrowThickness = 1.1f,
            LeftEyeTop = 0.55f,
            RightEyeTop = 0.55f,
            LeftEyeBottom = 0.08f,
            RightEyeBottom = 0.08f,
            PupilDilation = 0.6f,
            FaceLayers = new List<NpcLayer>
            {
                new() { Path = "Avatar/Layers/Face/Face_Neutral", Tint = Color.black },
                new() { Path = "Avatar/Layers/Face/FacialHair_Stubble", Tint = Color.black }
            },
            BodyLayers = new List<NpcLayer>
            {
                new() { Path = index == 1 ? "Avatar/Layers/Top/Overalls" : "Avatar/Layers/Top/T-Shirt", Tint = index == 0 ? new Color(0.62f, 0.39f, 0.14f, 1f) : index == 1 ? new Color(0.21f, 0.30f, 0.44f, 1f) : new Color(0.51f, 0.23f, 0.17f, 1f) },
                new() { Path = "Avatar/Layers/Bottom/Jeans", Tint = new Color(0.25f, 0.31f, 0.38f, 1f) }
            },
            Accessories = new List<NpcLayer>
            {
                new() { Path = "Avatar/Accessories/Feet/CombatBoots/CombatBoots", Tint = new Color(0.22f, 0.16f, 0.11f, 1f) }
            }
        };
    }

    public override Vector3 WorkPoint(int index, bool secondary, string upgrade, string? phase)
    {
        float y = MingWorld.FloorY;
        // The foreman stays put and only turns from the plan to the timber, so nobody crosses the carpenter between the tables.
        if (index == 0) return new Vector3(-57.5f, y, 143.2f);
        if (index == 1) return !secondary ? new Vector3(-57.8f, y, 137.55f) : new Vector3(-56.4f, y, 143.45f);
        return secondary ? new Vector3(-54.7f, y, 138.4f) : new Vector3(-54.7f, y, 140.1f);
    }

    public override Vector3 ContactPoint(int index, bool secondary, string upgrade, string? phase)
    {
        var point = WorkPoint(index, secondary, upgrade, phase);
        if (index == 0) return secondary ? new Vector3(-58.85f, 0.1f, 144f) : new Vector3(-57.5f, 0.45f, 142.4f);
        if (index == 1)
        {
            if (point.z < 140) return new Vector3(-57.8f, 0.45f, 136.95f);
            return phase == "breakthrough" ? new Vector3(-57.2f, 0.45f, 142.05f) : new Vector3(-56.4f, 0.45f, 144.05f);
        }
        return new Vector3(-54.1f, 0.9f, point.z);
    }

    // On the first day the far spot has no table yet, so the carpenter only looks at the plan there.
    public override bool UsesTool(int index, bool secondary, string upgrade, string? phase) =>
        index == 1 && !(phase == "breakthrough" && secondary) || index == 2 && phase != "breakthrough";

    public override string? Held(int index) => index == 0 ? "tools/managementclipboard/ManagementClipboard_AvatarEquippable" : null;

    public override bool GrabsBetweenSwings(int index) => index != 0;

    public override string Line(int index, string upgrade, string? phase)
    {
        if (index != 0) return phase == "roof" ? "Just the last few fittings. Mind the tools." : "Keep the site clear, please.";
        return phase switch
        {
            "breakthrough" => "We're opening the wall today. Stay behind the barrier.",
            "frame" => "Floor's in. Walls go up today.",
            "roof" => "Roof and finishing today. You can move in tomorrow.",
            _ => "Talk to Mrs. Ming about the expansion."
        };
    }
}

public abstract class MingWorkerNpc : Personnel.PersonnelNpc { }

public sealed class MingForeman : MingWorkerNpc { protected override string DefId => MingWorkers.ForemanId; }
public sealed class MingCarpenter : MingWorkerNpc { protected override string DefId => MingWorkers.CarpenterId; }
public sealed class MingFitter : MingWorkerNpc { protected override string DefId => MingWorkers.FitterId; }
