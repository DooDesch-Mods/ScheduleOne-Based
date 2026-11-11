using System.Linq;
using System.Reflection;
using Based.Content;
using Based.UI;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using S1API.Quests;
using UnityEngine;

namespace Based.Runtime;

// The journal page of one site. Each client keeps its own and derives it from the host's construction state,
// so the quest holds no state of its own. S1API rebuilds it from the save by its class name.
public abstract class ConstructionQuest : Quest
{
    private const string IconResource = "Based.Assets.quest-icon.png";
    private static Sprite? _icon;
    private static bool _iconAbsent;

    protected abstract string BaseId { get; }
    // The upgrade this page follows; the site's first upgrade unless a follow-up has a page of its own.
    protected virtual string? UpgradeId => null;

    private SiteDefinition Site => Mod.Sites.First(s => s.Base.Id == BaseId);
    private SiteQuest Page => Site.Quest(UpgradeId ?? Site.UpgradeId);

    protected override string Title => Text.T(Page.Title);
    protected override string Description => Text.T("The crew finishes one stage each night you sleep.");
    protected override bool AutoBegin => true;

    // Unity destroys runtime sprites when a save is left for the menu, so a destroyed one is built again.
    protected override Sprite? QuestIcon
    {
        get
        {
            if (_icon != null) return _icon;
            if (_iconAbsent) return null;
            _icon = LoadIcon();
            _iconAbsent = _icon == null;
            return _icon;
        }
    }

    internal static Sprite? Icon => _icon;

    // The layout stays fixed because the game restores entry states by index: the order, then one entry per phase.
    protected override void OnCreated()
    {
        base.OnCreated();
        var site = Site;
        var page = Page;
        AddEntry(Text.T(page.OrderEntry), site.QuestMarker);
        foreach (var phase in site.UpgradeOf(page.UpgradeId).Phases) AddEntry(Text.T(site.PhaseTitles[phase.Id]), site.QuestMarker);
    }

    private static Sprite? LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(IconResource);
        if (stream == null)
        {
            Mod.Warn("Quest icon resource is missing; the quest uses the default icon.");
            return null;
        }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] bytes = buffer.ToArray();
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        if (!ImageConversion.LoadImage(texture, (Il2CppStructArray<byte>)bytes, false))
        {
            Mod.Warn("Quest icon could not be decoded; the quest uses the default icon.");
            UnityEngine.Object.Destroy(texture);
            return null;
        }
        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
    }
}
