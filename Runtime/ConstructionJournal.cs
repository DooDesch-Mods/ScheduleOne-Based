using System;
using System.Collections.Generic;
using System.Linq;
using Based.Content;
using Based.Core;
using Based.UI;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Phone.ContactsApp;
using S1API.Quests;
using S1API.Quests.Constants;
using UnityEngine;

namespace Based.Runtime;

// Keeps every site's quest on the state of its construction and tells the player when a night changed it.
internal static class ConstructionJournal
{
    private const float Interval = 1f;
    private const float NoticeSeconds = 8f;
    // The morning starts behind a black fade, and the messages that arrive with it push an earlier notice off the screen.
    private const float NoticeDelay = 4f;

    private readonly record struct Seen(UpgradeStage Stage, int Cycles, string Held);
    private readonly record struct Notice(string Title, string Subtitle, float Due);

    private static readonly Dictionary<SiteDefinition, Seen> Last = new();
    private static readonly List<Notice> Pending = new();
    private static float _next;
    private static bool _waiting;

    internal static void Reset()
    {
        Last.Clear();
        Pending.Clear();
        _next = 0;
        _waiting = false;
    }

    internal static void Tick(IEnumerable<SiteDefinition> sites)
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + Interval;
        // S1API's quest constructor reads the contacts app and the game's quest start reads the journal.
        if (Il2CppScheduleOne.PlayerScripts.Player.Local == null || !PlayerSingleton<JournalApp>.InstanceExists || !PlayerSingleton<ContactsApp>.InstanceExists)
        {
            if (!_waiting) Mod.Log("Construction quest waits for the player's phone.");
            _waiting = true;
            return;
        }
        _waiting = false;
        foreach (var site in sites)
        {
            Reconcile(site);
            Report(site);
        }
        ShowDue();
    }

    internal static string NightsLeft(int nights) =>
        nights == 1 ? Text.T("One night left") : Text.T("{0} nights left", nights);

    private static void Reconcile(SiteDefinition site)
    {
        var state = Mod.Engine.GetUpgrade(site.Base.Id, site.UpgradeId);
        var quest = QuestManager.GetQuestByName(Text.T(site.Title));
        switch (state.Stage)
        {
            case UpgradeStage.RightsOwned or UpgradeStage.Constructing:
                if (quest == null)
                {
                    QuestManager.CreateQuest(site.QuestType);
                    Mod.Log("Construction quest of " + site.Base.Id + " started.");
                }
                else Apply(site, quest, state);
                break;
            case UpgradeStage.Complete:
                if (quest != null) Finish(site, quest);
                break;
            default:
                // Only a save whose two halves disagree or a debug jump gets here.
                if (quest != null)
                {
                    Mod.Warn("Construction quest of " + site.Base.Id + " cancelled: no rights behind it.");
                    quest.Cancel();
                }
                break;
        }
    }

    private static void Apply(SiteDefinition site, Quest quest, UpgradeState state)
    {
        var phases = site.Upgrade.Phases;
        var entries = quest.QuestEntries;
        // S1API adds the entries a frame or two after the quest was created.
        if (entries.Count != phases.Count + 1) return;
        string phase = Mod.Engine.CurrentPhase(state.BaseId, state.UpgradeId) ?? "";
        int active = state.Stage == UpgradeStage.RightsOwned ? 0 : 1 + Math.Max(0, phases.ToList().FindIndex(p => p.Id == phase));
        // A restored quest comes back with the saved titles, so every title is written each time.
        SetTitle(entries[0], Text.T(site.OrderEntry));
        for (int i = 0; i < phases.Count; i++) SetTitle(entries[i + 1], Text.T(site.PhaseTitles[phases[i].Id]));
        SetTitle(entries[active], ActiveTitle(site, state, active));
        // The next step starts before the last one ends, or the game could count all as done.
        SetState(entries[active], QuestState.Active);
        for (int i = 0; i < entries.Count; i++)
            if (i != active) SetState(entries[i], i < active ? QuestState.Completed : QuestState.Inactive);
    }

    private static string ActiveTitle(SiteDefinition site, UpgradeState state, int active)
    {
        if (Mod.HeldBy.Length > 0) return Hold();
        if (active == 0) return Text.T(site.OrderEntry);
        return Progress(Text.T(site.PhaseTitles[site.Upgrade.Phases[active - 1].Id]), site.Upgrade.ConstructionCycles - state.CompletedCycles);
    }

    private static string Hold() => Text.T("Construction is on hold: move {0} away from the marked walls.", Mod.HeldBy);

    private static string Progress(string title, int nights) =>
        nights == 1 ? Text.T("{0}, one night left", title) : Text.T("{0}, {1} nights left", title, nights);

    private static void SetTitle(QuestEntry entry, string title)
    {
        if (entry.Title != title) entry.Title = title;
    }

    private static void SetState(QuestEntry entry, QuestState state)
    {
        if (entry.State != state) entry.SetState(state);
    }

    private static void Finish(SiteDefinition site, Quest quest)
    {
        foreach (var entry in quest.QuestEntries)
            if (entry.State != QuestState.Completed) entry.Complete();
        // Completing the last entry ends the quest on its own, unless S1API had not added the entries yet.
        if (QuestManager.GetQuestByName(Text.T(site.Title)) != null) quest.Complete();
        Mod.Log("Construction quest of " + site.Base.Id + " completed.");
    }

    // The first look after a load only takes note: a reload changes nothing the player needs to hear about.
    private static void Report(SiteDefinition site)
    {
        var state = Mod.Engine.GetUpgrade(site.Base.Id, site.UpgradeId);
        var now = new Seen(state.Stage, state.CompletedCycles, Mod.HeldBy);
        if (!Last.TryGetValue(site, out var before))
        {
            Last[site] = now;
            return;
        }
        if (now == before) return;
        Last[site] = now;
        if (now.Held.Length > 0 && now.Held != before.Held) Queue(Text.T(site.Title), Text.T("Construction on hold"));
        else if (now.Stage == UpgradeStage.Complete && before.Stage != UpgradeStage.Complete) Queue(Text.T(site.Title), Text.T("Ready to use"));
        else if (now.Stage == UpgradeStage.Constructing && now.Cycles > before.Cycles)
        {
            string phase = Mod.Engine.CurrentPhase(state.BaseId, state.UpgradeId) ?? "";
            if (site.PhaseTitles.TryGetValue(phase, out string? title))
                Queue(Text.T(title), NightsLeft(site.Upgrade.ConstructionCycles - now.Cycles));
            else Mod.Warn("Morning notice skipped: phase " + phase + " has no title.");
        }
    }

    private static void Queue(string title, string subtitle) =>
        Pending.Add(new Notice(title, subtitle, Time.unscaledTime + NoticeDelay));

    private static void ShowDue()
    {
        for (int i = Pending.Count - 1; i >= 0; i--)
        {
            if (Pending[i].Due > Time.unscaledTime) continue;
            var notice = Pending[i];
            Pending.RemoveAt(i);
            Notify(notice);
        }
    }

    private static void Notify(Notice notice)
    {
        var notifications = Singleton<NotificationsManager>.Instance;
        if (notifications == null)
        {
            Mod.Warn("Construction notice not shown, the notification manager is not ready: " + notice.Title);
            return;
        }
        var icon = ConstructionQuest.Icon != null ? ConstructionQuest.Icon : PlayerSingleton<JournalApp>.Instance.AppIcon;
        notifications.SendNotification(notice.Title, notice.Subtitle, icon, NoticeSeconds, true);
        Mod.Log("Construction notice shown: " + notice.Title + " / " + notice.Subtitle);
    }
}
