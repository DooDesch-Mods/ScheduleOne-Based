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
using Il2CppScheduleOne.UI;
using UnityEngine;
using UnityEngine.Events;

namespace Based.Runtime;

// The plan as an item. To the game it is a wall decoration: the definition is a clone of a vanilla artwork, so placing,
// saving, co-op and picking it up by right click are the game's own. The look comes from the item id on every peer.
// Each site has its own item, so a folder knows which plan it opens and carries that property's name.
internal static class ProjectFolder
{
    private sealed class Folder
    {
        public SiteDefinition Site = null!;
        public BuildableItemDefinition? Definition;
        public readonly List<BuildableItem> Hung = new();
        public bool Gift;
    }

    private const string IconResource = "Based.Assets.folder-icon.png";
    private const float Standoff = .002f, BoxDepth = .02f, TalkDepth = .04f, InteractionRange = 4f, ClickCooldown = .35f;
    private const float HeldScale = .173f;
    private const float HeldSearchInterval = .25f;
    private static Transform? _heldModel;
    private static float _nextHeldSearch;
    private static readonly Vector3 HeldOffset = new(.13f, .23f, .013f);
    private static readonly string[] WallDonors = { "artworklines", "artworkmillie", "artworkoffer", "artworkbeachday", "artworkmenace", "artworkrapscallion" };
    private static readonly string[] HandDonors = { "friendlynote1", "friendlynote2", "friendlynote3" };
    private static readonly Dictionary<string, Folder> Folders = new(StringComparer.Ordinal);
    private static Sprite? _icon;
    private static Transform? _holder;
    private static IntPtr _placing;
    private static float _nextClick;
    private static bool _failed;

    private static Folder Of(SiteDefinition site)
    {
        if (!Folders.TryGetValue(site.FolderItemId, out var folder))
            Folders[site.FolderItemId] = folder = new Folder { Site = site };
        return folder;
    }

    // The site whose folder this item is, or nothing for every other item.
    internal static SiteDefinition? SiteOf(string? itemId) =>
        itemId == null ? null : Mod.Sites.FirstOrDefault(site => site.FolderItemId == itemId);

    // The site of the folder in the hand.
    internal static SiteDefinition? HeldSite
    {
        get
        {
            var inventory = PlayerSingleton<PlayerInventory>.Instance;
            return inventory == null || inventory.EquippedItem == null ? null : SiteOf(inventory.EquippedItem.ID);
        }
    }

    private static bool Carried(SiteDefinition site)
    {
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
        return inventory != null && inventory.GetAmountOfItem(site.FolderItemId) > 0;
    }

    private static List<BuildableItem> Hung(SiteDefinition site)
    {
        var hung = Of(site).Hung;
        hung.RemoveAll(item => item == null || item.IsDestroyed);
        return hung;
    }

    // Offered only while nothing else tells the player how the work is going. Doubles would do no harm.
    // A bought permit keeps it on offer, since the folder is where its builds are ordered.
    internal static bool Offered(SiteDefinition site)
    {
        var first = Mod.StageOf(site, site.UpgradeId);
        bool started = first is UpgradeStage.RightsOwned or UpgradeStage.Constructing
            || first == UpgradeStage.Complete && site.Upgrade.IsPermit
            || Mod.Constructing(site) != null;
        return started && Hung(site).Count == 0 && !Carried(site);
    }

    // A folder in the pocket or on a wall: the player still has the plans.
    internal static bool Present(SiteDefinition site) => Hung(site).Count > 0 || Carried(site);

    internal static bool Fits(SiteDefinition site)
    {
        var definition = Of(site).Definition;
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
        return definition != null && inventory != null && inventory.CanItemFitInInventory(definition.GetDefaultInstance(1), 1);
    }

    internal static void GiveAfterPayment(SiteDefinition site) => Of(site).Gift = true;

    internal static void Register()
    {
        if (_failed) return;
        foreach (var site in Mod.Sites)
        {
            var folder = Of(site);
            if (folder.Definition == null && !Build(folder)) return;
            if (!Registry.ItemExists(site.FolderItemId))
            {
                Registry.Instance.AddToRegistry(folder.Definition);
                Mod.Log("Project folder item registered: " + site.FolderItemId + ".");
                continue;
            }
            var found = Registry.GetItem(site.FolderItemId);
            if (found != null && !string.Equals(found.ID, site.FolderItemId, StringComparison.OrdinalIgnoreCase))
                MelonLoader.MelonLogger.Error("[Based] The project folder's id " + site.FolderItemId + " collides with " + found.ID + "; the item is unavailable.");
        }
    }

    internal static void Reset()
    {
        foreach (var folder in Folders.Values)
        {
            folder.Hung.Clear();
            folder.Gift = false;
        }
        _placing = IntPtr.Zero;
        PlanBoard.Reset();
    }

    internal static void Tick()
    {
        foreach (var site in Mod.Sites)
        {
            var folder = Of(site);
            if (folder.Gift && Mod.Engine.GetUpgrade(site.Base.Id, site.UpgradeId).Stage != UpgradeStage.RightsMissing)
            {
                folder.Gift = false;
                Give(site);
            }
            if (folder.Hung.RemoveAll(item => item == null || item.IsDestroyed) > 0) Mod.Log("A project folder left the wall: " + site.Base.Id + ".");
        }
        var held = HeldSite;
        SyncPlacement(held != null);
        foreach (var site in Mod.Sites) PlanBoard.Tick(site, held == site || Of(site).Hung.Count > 0);
        if (held != null) PollClick(held);
    }

    // Returns what the plan shows when the item could not be handed over, or nothing.
    internal static string Give(SiteDefinition site)
    {
        var definition = Of(site).Definition;
        var inventory = PlayerSingleton<PlayerInventory>.Instance;
        if (definition == null || inventory == null)
        {
            Mod.Warn("Project folder for " + site.Base.Id + " not given: the " + (definition == null ? "item" : "inventory") + " is not ready.");
            return Text.T("No room for the project folder.");
        }
        var item = definition.GetDefaultInstance(1);
        if (!inventory.CanItemFitInInventory(item, 1))
        {
            Mod.Log("Project folder for " + site.Base.Id + " not given: the inventory is full.");
            Notify(site, Text.T("No room for the project folder."));
            return Text.T("No room for the project folder.");
        }
        inventory.AddItemToInventory(item);
        Notify(site, Text.T("Left click to read, or hang it on a wall."));
        Mod.Log("Project folder added to the inventory: " + site.Base.Id + ".");
        return "";
    }

    internal static void NoteClosed() => _nextClick = Time.unscaledTime + ClickCooldown;

    // The item carries its property's name.
    private static string DisplayName(SiteDefinition site) => Text.T("{0} - Project folder", Text.T(site.PropertyName));

    private static void Notify(SiteDefinition site, string line)
    {
        var notifications = Singleton<NotificationsManager>.Instance;
        if (notifications == null) { Mod.Warn("No notification shown: the notification manager is not ready."); return; }
        notifications.SendNotification(DisplayName(site), line, _icon, 8f, false);
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

    internal static void DebugOpen(SiteDefinition site) => Open(site, "console");

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

    // Hangs a folder on the wall nearest to the player through the game's own spawn, as a placement would.
    // A co-op join test needs one on a wall: its spawn data carries the item to a joining client.
    internal static string DebugWall(SiteDefinition site)
    {
        if (!Il2CppFishNet.InstanceFinder.IsServer) throw new InvalidOperationException("Only the host can hang a folder by console.");
        var definition = Of(site).Definition;
        if (definition == null) throw new InvalidOperationException("The project folder item of " + site.Base.Id + " is not built.");
        var manager = NetworkSingleton<BuildManager>.Instance;
        if (manager == null) throw new InvalidOperationException("The build manager is not ready.");
        var player = Player.Local;
        if (player == null) throw new InvalidOperationException("No local player.");
        Vector3 from = player.transform.position + Vector3.up * 1.4f;
        Surface? best = null;
        Vector3 point = default;
        float bestDistance = float.MaxValue;
        foreach (var surface in UnityEngine.Object.FindObjectsOfType<Surface>())
        {
            if (surface.SurfaceType != Surface.ESurfaceType.Wall || surface.ParentProperty == null) continue;
            var collider = surface.GetComponentInChildren<Collider>();
            if (collider == null) continue;
            Vector3 closest = collider.ClosestPoint(from);
            float distance = Vector3.Distance(closest, from);
            if (distance >= bestDistance) continue;
            best = surface;
            point = closest;
            bestDistance = distance;
        }
        if (best == null) throw new InvalidOperationException("No wall surface in the loaded world.");
        var item = manager.CreateSurfaceItem(definition.GetDefaultInstance(1), best, best.GetRelativePosition(point), Quaternion.identity);
        string where = best.ParentProperty.PropertyName + " wall " + best.name + " at " + point + " (" + bestDistance.ToString("0.0") + " m away)";
        if (item == null) throw new InvalidOperationException("The game did not create the folder on " + where + ".");
        return where;
    }

    internal static string DebugState(SiteDefinition site)
    {
        var hung = string.Join("; ", Hung(site).Select(i => i.transform.position.ToString()));
        return site.Base.Id + ": holding=" + (HeldSite == site) + ", carried=" + Carried(site) + ", aimedAtWall=" + AimedAtWall() + ", hung=[" + hung + "]";
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
    internal static void Dress(SiteDefinition site, BuildableItem item, bool ghost)
    {
        Transform root = item.transform;
        if (root.Find(PlanBoard.ModelName) != null) return;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        var caption = FindNamed(root, "Name");
        if (caption != null) caption.gameObject.SetActive(false);
        var model = PlanBoard.CreateModel(site, root);
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
        interact.onInteractStart.AddListener((UnityAction)(() => Open(site, "interaction on the wall")));
        Of(site).Hung.Add(item);
        Mod.Log("Project folder hung at " + root.position + ": " + site.Base.Id + ".");
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

    private static bool Build(Folder folder)
    {
        var site = folder.Site;
        var wall = WallDonors.Select(id => Registry.GetItem(id)?.TryCast<BuildableItemDefinition>())
            .FirstOrDefault(d => d != null && d.BuiltItem != null && d.BuiltItem.TryCast<SurfaceItem>() != null);
        var note = HandDonors.Select(id => Registry.GetItem(id)?.TryCast<StorableItemDefinition>()).FirstOrDefault(d => d != null);
        if (wall == null || note == null)
        {
            _failed = true;
            MelonLoader.MelonLogger.Error("[Based] The project folder is unavailable: this game build has no " + (wall == null ? "wall artwork" : "friendly note") + " to clone.");
            return false;
        }
        _icon ??= LoadIcon();
        var definition = UnityEngine.Object.Instantiate(wall);
        definition.hideFlags |= HideFlags.DontUnloadUnusedAsset;
        definition.name = "Based_ProjectFolder_" + site.FolderItemId;
        definition.ID = site.FolderItemId;
        definition.Name = DisplayName(site);
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
        DressHand(site, definition);
        folder.Definition = definition;
        Mod.Log("Project folder item " + site.FolderItemId + " built from " + wall.ID + " and " + note.ID + ".");
        return true;
    }

    // An item takes one of two routes into the hand, chosen by the donor. Both get a private copy wearing the folder.
    private static void DressHand(SiteDefinition site, BuildableItemDefinition definition)
    {
        if (definition.EquippableData != null && definition.EquippableData.FirstPersonEquippedItemPrefab != null)
        {
            var data = UnityEngine.Object.Instantiate(definition.EquippableData);
            data.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            data.name = "Based_FolderEquippableData";
            var prefab = CloneForHand(site, data.FirstPersonEquippedItemPrefab.gameObject).GetComponent<FPEquippedItem>();
            if (prefab == null) Mod.Warn("The cloned first-person prefab lost its component; the hand shows the donor.");
            else
            {
                data.FirstPersonEquippedItemPrefab = prefab;
                definition.EquippableData = data;
            }
        }
        if (definition.Equippable != null)
            definition.Equippable = CloneForHand(site, definition.Equippable.gameObject).GetComponent<Equippable>();
    }

    // The clone sits under an inactive parent so its Awake never runs. Deactivating the clone itself would carry over to every copy.
    private static GameObject CloneForHand(SiteDefinition site, GameObject donor)
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
        var model = PlanBoard.CreateModel(site, clone.transform);
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

// Runs on every peer for a placed folder and for one restored from a save, so the item id is all that has to travel.
[HarmonyPatch(typeof(SurfaceItem), nameof(SurfaceItem.InitializeSurfaceItem))]
internal static class DressHungFolder
{
    private static void Postfix(SurfaceItem __instance, ItemInstance instance)
    {
        var site = ProjectFolder.SiteOf(instance?.ID);
        if (site == null) return;
        try { ProjectFolder.Dress(site, __instance, ghost: false); }
        catch (Exception error) { Mod.Warn("Dressing a hung project folder failed: " + error); }
    }
}

[HarmonyPatch(typeof(BuildStart_Surface), nameof(BuildStart_Surface.CreateGhostModel))]
internal static class DressFolderGhost
{
    private static void Postfix(BuildableItemDefinition itemDefinition, SurfaceItem __result)
    {
        var site = ProjectFolder.SiteOf(itemDefinition?.ID);
        if (__result == null || site == null) return;
        try { ProjectFolder.Dress(site, __result, ghost: true); }
        catch (Exception error) { Mod.Warn("Dressing the project folder ghost failed: " + error); }
    }
}

[HarmonyPatch(typeof(BuildUpdate_Surface), nameof(BuildUpdate_Surface.LateUpdate))]
internal static class SwitchFolderGhostAndHand
{
    private static void Postfix(BuildUpdate_Surface __instance)
    {
        var item = __instance.ItemInstance;
        if (ProjectFolder.SiteOf(item?.ID) == null) return;
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
