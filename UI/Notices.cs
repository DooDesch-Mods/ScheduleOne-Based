using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI;
using Il2CppTMPro;
using UnityEngine;

namespace Based.UI;

// The game's notification box is 200 px wide and lets a longer title run past its background. After the game builds
// the entry, the box grows to the right until the title fits; the icon and the slide-in from the left stay as they are.
internal static class Notices
{
    // The title's rect leaves this much of the box free: the icon on the left, a margin on the right.
    private const float TitleInset = 52.5f, Padding = 12f;

    internal static bool Send(string title, string subtitle, Sprite? icon, float seconds, bool sound)
    {
        var notifications = Singleton<NotificationsManager>.Instance;
        if (notifications == null)
        {
            Mod.Warn("Notification not shown, the notification manager is not ready: " + title);
            return false;
        }
        notifications.SendNotification(title, subtitle, icon, seconds, sound);
        Fit(notifications, title);
        return true;
    }

    private static void Fit(NotificationsManager notifications, string title)
    {
        var entries = notifications.EntryContainer;
        if (entries == null || entries.childCount == 0)
        {
            Mod.Warn("Notification box not fitted: the game's entry list is empty after sending " + title);
            return;
        }
        var entry = entries.GetChild(entries.childCount - 1);
        var container = entry.Find("Container")?.GetComponent<RectTransform>();
        var text = container?.Find("Title")?.GetComponent<TextMeshProUGUI>();
        var box = entry.GetComponent<RectTransform>();
        if (container == null || text == null || box == null)
        {
            Mod.Warn("Notification box not fitted: the entry has no Container/Title, the game's layout changed: " + title);
            return;
        }
        float needed = text.GetPreferredValues(title).x + TitleInset + Padding;
        float extra = needed - box.rect.width;
        if (extra <= 0f) return;
        // With the pivot on the left edge the slide-in, which sets the x position, keeps the box's left edge in place.
        container.pivot = new Vector2(0f, container.pivot.y);
        container.sizeDelta = new Vector2(extra, container.sizeDelta.y);
        Mod.Log("Notification box widened by " + extra.ToString("F0") + " px for the title: " + title);
    }
}
