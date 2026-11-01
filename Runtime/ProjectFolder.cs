using System.Reflection;
using Based.Content;
using Based.Core;
using Based.UI;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Building;
using Il2CppScheduleOne.Core.Equipping.Framework;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Equipping;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI;
using UnityEngine;
using UnityEngine.Events;

namespace Based.Runtime;

// The plan as an item. To the game it is a wall decoration: the definition is a clone of a vanilla artwork, so placing,
// saving, co-op and picking it up by right click are the game's own. The look comes from the item id on every peer.
internal static class ProjectFolder
{
    internal const string ItemId = "based_project_folder";
    private const string IconResource = "Based.Assets.folder-icon.png";
    private const float Standoff = .002f, BoxDepth = .02f, TalkDepth = .04f, InteractionRange = 4f, ClickCooldown = .35f;
    private const float HeldScale = .173f;
    private const float HeldSearchInterval = .25f;
    private static Transform? _heldModel;
    private static float _nextHeldSearch;
    private static readonly Vector3 HeldOffset = new(.13f, .23f, .013f);
    private static readonly string[] WallDonors = { "artworklines", "artworkmillie", "artworkoffer", "artworkbeachday", "artworkmenace", "artworkrapscallion" };
    private static readonly string[] HandDonors = { "friendlynote1", "friendlynote2", "friendlynote3" };
    private static readonly List<BuildableItem> Hung = new();
    private static BuildableItemDefinition? _definition;
    private static Sprite? _icon;
    private static Transform? _holder;
    private static SiteDefinition? _giftFor;
    private static IntPtr _placing;
    private static float _nextClick;
    private static bool _failed;

    internal static bool Holding
    {
        get
        {
            var inventory = PlayerSingleton<PlayerInventory>.Instance;
            return inventory != null && inventory.EquippedItem != null && inventory.EquippedItem.ID == ItemId;
        }
    }

    private static bool Carried
    {
        get
        {
            var inventory = PlayerSingleton<PlayerInventory>.Instance;
            return inventory != null && inventory.GetAmountOfItem(ItemId) > 0;
        }
    }

    // Offered only while nothing else tells the player how the work is going. Doubles would do no harm.
    internal static bool Offered(UpgradeStage stage)
    {
        Hung.RemoveAll(item => item == null || item.IsDestroyed);
        return stage is UpgradeStage.RightsOwned or UpgradeStage.Constructing && Hung.Count == 0 && !Carried;
    }

    internal static void GiveAfterPayment(SiteDefinition site) => _giftFor = site;

    internal static void Register()
    {
        if (_failed) return;
        if (_definition == null && !Build()) return;
        if (!Registry.ItemExists(ItemId))
        {
            Registry.Instance.AddToRegistry(_definition);
            Mod.Log("Project folder item registered.");
            return;
        }
        var found = Registry.GetItem(ItemId);
        if (found != null && !string.Equals(found.ID, ItemId, StringComparison.OrdinalIgnoreCase))
            MelonLoader.MelonLogger.Error("[Based] The project folder's id collides with " + found.ID + "; the item is unavailable.");
    }

    internal static void Reset()
    {
        Hung.Clear();
        _giftFor = null;
        _placing = IntPtr.Zero;
        PlanBoard.Reset();
    }

    internal static void Tick(SiteDefinition site)
    {
        if (_giftFor != null && Mod.Engine.GetUpgrade(_giftFor.Base.Id, _giftFor.UpgradeId).Stage != UpgradeStage.RightsMissing)
        {
            _giftFor = null;
            Give();
        }
        if (Hung.RemoveAll(item => item == null || item.IsDestroyed) > 0) Mod.Log("A project folder left the wall.");
        bool holding = Holding;
        SyncPlacement(holding);
        PlanBoard.Tick(site, holding || Hung.Count > 0);
        if (holding) PollClick(site);
    }

    // Returns what the plan shows when the item could not be handed over, or nothing.
    internal static string Give()
    {
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
        if (_definition == null || inventory == null)
        {
            Mod.Warn("Project folder not given: the " + (_definition == null ? "item" : "inventory") + " is not ready.");
            return Text.T("No room for the project folder.");
        }
        var item = _definition.GetDefaultInstance(1);
        if (!inventory.CanItemFitInInventory(item, 1))
        {
            Mod.Log("Project folder not given: the inventory is full.");
            Notify(Text.T("No room for the project folder."));
            return Text.T("No room for the project folder.");
        }
        inventory.AddItemToInventory(item);
        Notify(Text.T("Left click to read, or hang it on a wall."));
        Mod.Log("Project folder added to the inventory.");
        return "";
    }

    internal static void NoteClosed() => _nextClick = Time.unscaledTime + ClickCooldown;

    private static void Notify(string line)
    {
        var notifications = Singleton<NotificationsManager>.Instance;
        if (notifications == null) { Mod.Warn("No notification shown: the notification manager is not ready."); return; }
        notifications.SendNotification(Text.T("Project folder"), line, _icon, 8f, false);
    }

    // The ghost shows on a wall that takes the item, where the game's own click hangs it. Anywhere else the click reads it.
    private static void PollClick(SiteDefinition site)
    {
        if (BuildPlan.Site != null || Time.unscaledTime < _nextClick) return;
        if (!GameInput.GetButtonDown(GameInput.ButtonCode.PrimaryClick) || GameInput.IsTyping) return;
        var camera = PlayerSingleton<PlayerCamera>.Instance;
        if (camera == null || camera.activeUIElements.Count > 0) return;
        if (AimedAtWall()) { Mod.Log("Left click hangs the project folder."); return; }
        Open(site, "left click in hand");
    }

    private static void Open(SiteDefinition site, string how)
    {
        if (BuildPlan.Site != null) { Mod.Log("Project folder ignored (" + how + "): the plan is already open."); return; }
        Mod.Log("Project folder opened by " + how + ".");
        BuildPlan.Open(site, standalone: true);
    }

    internal static void DebugOpen() => Open(Mod.Sites[0], "console");

    private static bool AimedAtWall()
    {
        var manager = NetworkSingleton<BuildManager>.Instance;
        if (manager == null || !manager.isBuilding || manager.currentBuildHandler == null) return false;
        var update = manager.currentBuildHandler.GetComponent<BuildUpdate_Surface>();
        return update != null && update.validPosition;
    }

#if DEBUG
    internal static void DebugHang()
    {
        var manager = NetworkSingleton<BuildManager>.Instance;
        var update = manager?.currentBuildHandler?.GetComponent<BuildUpdate_Surface>();
        if (update == null) throw new InvalidOperationException("No wall placement is running.");
        if (!update.validPosition) throw new InvalidOperationException("The aim is not on a wall that takes the folder.");
        update.Place();
        Mod.Log("baseddiag: project folder placed through the game's placement.");
    }

    internal static string DebugState()
    {
        var hung = string.Join("; ", Hung.Where(i => i != null).Select(i => i.transform.position.ToString()));
        return "holding=" + Holding + ", carried=" + Carried + ", aimedAtWall=" + AimedAtWall() + ", hung=[" + hung + "]";
    }
#endif

    // Placing runs while the folder is in the hand, as for a painting. It only stops a placement it started itself.
    private static void SyncPlacement(bool holding)
    {
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
        IntPtr wanted = holding && inventory.Equippable != null ? inventory.EquippedItem.Pointer : IntPtr.Zero;
        if (_placing != IntPtr.Zero && _placing != wanted) StopPlacing();
        if (wanted == IntPtr.Zero || _placing != IntPtr.Zero) return;
        _placing = wanted;
        var manager = NetworkSingleton<BuildManager>.Instance;
        if (manager == null) { Mod.Warn("Project folder cannot be hung: the build manager is not ready."); return; }
        manager.StartBuilding(inventory.EquippedItem);
        Mod.Log("Project folder placement started.");
    }

    private static void StopPlacing()
    {
        IntPtr was = _placing;
        _placing = IntPtr.Zero;
        var manager = NetworkSingleton<BuildManager>.Instance;
        if (manager == null || !manager.isBuilding || manager.currentBuildHandler == null)
        {
            Mod.Log("Project folder placement was already over.");
            return;
        }
        var update = manager.currentBuildHandler.GetComponent<BuildUpdate_Surface>();
        if (update == null || update.ItemInstance == null || update.ItemInstance.Pointer != was)
        {
            Mod.Log("The running placement is not the project folder's; left alone.");
            return;
        }
        manager.StopBuilding();
        Mod.Log("Project folder placement stopped.");
    }

    // Hides the artwork the prefab brought and puts the folder where it hung.
    internal static void Dress(BuildableItem item, bool ghost)
    {
        Transform root = item.transform;
        if (root.Find(PlanBoard.ModelName) != null) return;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        var caption = FindNamed(root, "Name");
        if (caption != null) caption.gameObject.SetActive(false);
        var model = PlanBoard.CreateModel(root);
        model.transform.localPosition = new Vector3(0f, 0f, Standoff);
        FitBox(item);
        if (ghost)
        {
            model.AddComponent<OverrideGhostMaterial>();
            Mod.Log("Project folder ghost dressed.");
            return;
        }
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            if (collider != item.BoundingCollider) collider.enabled = false;
        var box = model.AddComponent<BoxCollider>();
        box.size = new Vector3(PlanBoard.Width, PlanBoard.Height, TalkDepth);
        box.center = new Vector3(0f, 0f, TalkDepth / 2f);
        var interact = model.AddComponent<InteractableObject>();
        interact.SetMessage(Text.T("Read plan"));
        interact.MaxInteractionRange = InteractionRange;
        interact.RequiresUniqueClick = true;
        interact.onInteractStart.AddListener((UnityAction)(() => Open(Mod.Sites[0], "interaction on the wall")));
        Hung.Add(item);
        Mod.Log("Project folder hung at " + root.position + ".");
    }

    // The placement code measures wall penetration with this box. The prefab's box is turned about z, which that code ignores.
    private static void FitBox(BuildableItem item)
    {
        var box = item.BoundingCollider;
        if (box == null) { Mod.Warn("The project folder's wall item has no bounding box to resize."); return; }
        box.transform.localRotation = Quaternion.identity;
        box.size = new Vector3(PlanBoard.Width, PlanBoard.Height, BoxDepth);
        box.center = new Vector3(0f, 0f, BoxDepth / 2f);
    }

    // On a wall that takes the folder the ghost stands in for the one in the hand.
    internal static void SwitchHeld(bool onWall)
    {
        if (_heldModel == null && Time.unscaledTime >= _nextHeldSearch)
        {
            _nextHeldSearch = Time.unscaledTime + HeldSearchInterval;
            _heldModel = FindHeldModel();
        }
        if (_heldModel != null && _heldModel.gameObject.activeSelf == onWall) _heldModel.gameObject.SetActive(!onWall);
    }

    // The older route spawns the item under the equip container, the first-person route without a parent.
    private static Transform? FindHeldModel()
    {
        var container = PlayerSingleton<PlayerInventory>.Instance?.EquipContainer;
        var held = container == null ? null : FindNamed(container, PlanBoard.ModelName);
        if (held != null) return held;
        foreach (var item in UnityEngine.Object.FindObjectsOfType<FPEquippedItem>())
        {
            held = FindNamed(item.transform, PlanBoard.ModelName);
            if (held != null) return held;
        }
        return null;
    }

    private static Transform? FindNamed(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindNamed(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static bool Build()
    {
        var wall = WallDonors.Select(id => Registry.GetItem(id)?.TryCast<BuildableItemDefinition>())
            .FirstOrDefault(d => d != null && d.BuiltItem != null && d.BuiltItem.TryCast<SurfaceItem>() != null);
        var note = HandDonors.Select(id => Registry.GetItem(id)?.TryCast<StorableItemDefinition>()).FirstOrDefault(d => d != null);
        if (wall == null || note == null)
        {
            _failed = true;
            MelonLoader.MelonLogger.Error("[Based] The project folder is unavailable: this game build has no " + (wall == null ? "wall artwork" : "friendly note") + " to clone.");
            return false;
        }
        _icon = LoadIcon();
        var definition = UnityEngine.Object.Instantiate(wall);
        definition.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        definition.name = "Based_ProjectFolder";
        definition.ID = ItemId;
        definition.Name = Text.T("Project folder");
        definition.Description = Text.T("Left click to read, or hang it on a wall.");
        definition.Icon = _icon != null ? _icon : note.Icon;
        definition.Category = note.Category;
        definition.StackLimit = 1;
        definition.UsableInFilters = note.UsableInFilters;
        definition.legalStatus = note.legalStatus;
        definition.AvailableInDemo = note.AvailableInDemo;
        definition.EquipMode = note.EquipMode;
        definition.Equippable = note.Equippable;
        definition.EquippableData = note.EquippableData;
        definition.CustomItemUI = note.CustomItemUI;
        definition.CustomInfoContent = note.CustomInfoContent;
        definition.BasePurchasePrice = 0f;
        definition.ShopCategories = note.ShopCategories;
        definition.RequiresLevelToPurchase = note.RequiresLevelToPurchase;
        definition.RequiredRank = note.RequiredRank;
        definition.ResellMultiplier = 0f;
        definition.StoredItem = note.StoredItem;
        definition.PickpocketDifficultyMultiplier = note.PickpocketDifficultyMultiplier;
        definition.StationItem = note.StationItem;
        definition.CombatUtility = note.CombatUtility;
        definition.BuildSoundType = BuildableItemDefinition.EBuildSoundType.Cardboard;
        DressHand(definition);
        _definition = definition;
        Mod.Log("Project folder item built from " + wall.ID + " and " + note.ID + ".");
        return true;
    }

    // An item takes one of two routes into the hand, chosen by the donor. Both get a private copy wearing the folder.
    private static void DressHand(BuildableItemDefinition definition)
    {
        if (definition.EquippableData != null && definition.EquippableData.FirstPersonEquippedItemPrefab != null)
        {
            var data = UnityEngine.Object.Instantiate(definition.EquippableData);
            data.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            data.name = "Based_FolderEquippableData";
            var prefab = CloneForHand(data.FirstPersonEquippedItemPrefab.gameObject).GetComponent<FPEquippedItem>();
            if (prefab == null) Mod.Warn("The cloned first-person prefab lost its component; the hand shows the donor.");
            else
            {
                data.FirstPersonEquippedItemPrefab = prefab;
                definition.EquippableData = data;
            }
        }
        if (definition.Equippable != null)
            definition.Equippable = CloneForHand(definition.Equippable.gameObject).GetComponent<Equippable>();
    }

    // The clone sits under an inactive parent so its Awake never runs. Deactivating the clone itself would carry over to every copy.
    private static GameObject CloneForHand(GameObject donor)
    {
        if (_holder == null)
        {
            var holder = new GameObject("Based_FolderHeldDonor");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            _holder = holder.transform;
        }
        var clone = UnityEngine.Object.Instantiate(donor, _holder);
        clone.name = "Based_FolderHeld";
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        var model = PlanBoard.CreateModel(clone.transform);
        model.transform.localPosition = HeldOffset;
        // The note's sheet is turned about its height; the folder's face points the other way round than the sheet's.
        model.transform.localRotation = new Quaternion(3.700832E-08f, -0.99805033f, 0.06241523f, 2.9098615E-07f) * Quaternion.Euler(0f, 180f, 0f);
        model.transform.localScale = Vector3.one * HeldScale;
        model.layer = clone.layer;
        return clone;
    }

    private static Sprite? LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResource);
        if (stream == null) { Mod.Warn("Project folder icon resource is missing; the note's icon stays."); return null; }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        if (!ImageConversion.LoadImage(texture, (Il2CppStructArray<byte>)buffer.ToArray(), false))
        {
            Mod.Warn("Project folder icon could not be decoded; the note's icon stays.");
            return null;
        }
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}

[HarmonyPatch(typeof(ProductManager), "Start")]
internal static class RegisterProjectFolder
{
    private static void Prefix()
    {
        try { ProjectFolder.Register(); }
        catch (Exception error) { MelonLoader.MelonLogger.Error("[Based] Registering the project folder failed: " + error); }
    }
}

// Runs on every peer for a placed folder and for one restored from a save, so the item id is all that has to travel.
[HarmonyPatch(typeof(SurfaceItem), nameof(SurfaceItem.InitializeSurfaceItem))]
internal static class DressHungFolder
{
    private static void Postfix(SurfaceItem __instance, ItemInstance instance)
    {
        if (instance == null || instance.ID != ProjectFolder.ItemId) return;
        try { ProjectFolder.Dress(__instance, ghost: false); }
        catch (Exception error) { Mod.Warn("Dressing a hung project folder failed: " + error); }
    }
}

[HarmonyPatch(typeof(BuildStart_Surface), nameof(BuildStart_Surface.CreateGhostModel))]
internal static class DressFolderGhost
{
    private static void Postfix(BuildableItemDefinition itemDefinition, SurfaceItem __result)
    {
        if (__result == null || itemDefinition == null || itemDefinition.ID != ProjectFolder.ItemId) return;
        try { ProjectFolder.Dress(__result, ghost: true); }
        catch (Exception error) { Mod.Warn("Dressing the project folder ghost failed: " + error); }
    }
}

[HarmonyPatch(typeof(BuildUpdate_Surface), nameof(BuildUpdate_Surface.LateUpdate))]
internal static class SwitchFolderGhostAndHand
{
    private static void Postfix(BuildUpdate_Surface __instance)
    {
        var item = __instance.ItemInstance;
        if (item == null || item.ID != ProjectFolder.ItemId) return;
        try
        {
            var ghost = __instance.GhostModel;
            var model = ghost == null ? null : ghost.transform.Find(PlanBoard.ModelName);
            if (model != null && model.gameObject.activeSelf != __instance.validPosition) model.gameObject.SetActive(__instance.validPosition);
            ProjectFolder.SwitchHeld(__instance.validPosition);
        }
        catch (Exception error) { Mod.Warn("Switching the project folder between ghost and hand failed: " + error); }
    }
}
