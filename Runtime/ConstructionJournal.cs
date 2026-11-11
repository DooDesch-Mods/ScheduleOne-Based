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
    private readonly record struct Notice(string Title, string Subtitle, float Due, bool Work);

    private static readonly Dictionary<(SiteDefinition, string), Seen> Last = new();
    private static readonly Dictionary<(SiteDefinition, string), UpgradeStage> LastFollowUp = new();
    private static readonly List<Notice> Pending = new();
    private static float _next;
    private static bool _waiting;

    internal static void Reset()
    {
        Last.Clear();
        LastFollowUp.Clear();
        Pending.Clear();
        ConstructionSounds.Reset();
        _next = 0;
        _waiting = false;
    }

    internal static void Tick(IEnumerable<SiteDefinition> sites)
    {
        ConstructionSounds.Tick();
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
            foreach (var page in site.Quests)
            {
                Reconcile(site, page);
                Report(site, page);
            }
            foreach (var followUp in site.FollowUps.Where(f => f.QuestType == null)) Report(site, followUp);
        }
        ShowDue();
    }

    internal static string NightsLeft(int nights) =>
        nights == 1 ? Text.T("One night left") : Text.T("{0} nights left", nights);

    // A first upgrade sells its rights before the work, so its page opens with them. A follow-up without rights opens
    // its page with the order, as the construction starts.
    private static void Reconcile(SiteDefinition site, SiteQuest page)
    {
        var state = Mod.Engine.GetUpgrade(site.Base.Id, page.UpgradeId);
        var quest = QuestManager.GetQuestByName(Text.T(page.Title));
        string name = site.Base.Id + "/" + page.UpgradeId;
        switch (state.Stage)
        {
            case UpgradeStage.RightsOwned or UpgradeStage.Constructing:
                if (quest == null)
                {
                    QuestManager.CreateQuest(page.QuestType);
                    Mod.Log("Construction quest of " + name + " started.");
                }
                else Apply(site, page, quest, state);
                break;
            case UpgradeStage.Complete:
                if (quest != null) Finish(page, quest, name);
                break;
            default:
                // Only a save whose two halves disagree or a debug jump gets here.
                if (quest != null)
                {
                    Mod.Warn("Construction quest of " + name + " cancelled: nothing ordered behind it.");
                    quest.Cancel();
                }
                break;
        }
    }

    private static void Apply(SiteDefinition site, SiteQuest page, Quest quest, UpgradeState state)
    {
        var upgrade = site.UpgradeOf(page.UpgradeId);
        var phases = upgrade.Phases;
        var entries = quest.QuestEntries;
        // S1API adds the entries a frame or two after the quest was created.
        if (entries.Count != phases.Count + 1) return;
        string phase = Mod.Engine.CurrentPhase(state.BaseId, state.UpgradeId) ?? "";
        int active = state.Stage == UpgradeStage.RightsOwned ? 0 : 1 + Math.Max(0, phases.ToList().FindIndex(p => p.Id == phase));
        // A restored quest comes back with the saved titles, so every title is written each time.
        SetTitle(entries[0], Text.T(page.OrderEntry));
        for (int i = 0; i < phases.Count; i++) SetTitle(entries[i + 1], Text.T(site.PhaseTitles[phases[i].Id]));
        SetTitle(entries[active], ActiveTitle(site, page, upgrade, state, active));
        // The next step starts before the last one ends, or the game could count all as done.
        SetState(entries[active], QuestState.Active);
        for (int i = 0; i < entries.Count; i++)
            if (i != active) SetState(entries[i], i < active ? QuestState.Completed : QuestState.Inactive);
    }

    private static string ActiveTitle(SiteDefinition site, SiteQuest page, UpgradeDefinition upgrade, UpgradeState state, int active)
    {
        if (Mod.HeldBy.Length > 0 && state.Stage == UpgradeStage.Constructing) return Hold();
        if (active == 0) return Text.T(page.OrderEntry);
        return Progress(Text.T(site.PhaseTitles[upgrade.Phases[active - 1].Id]), upgrade.ConstructionCycles - state.CompletedCycles);
    }

    private static string Hold() => Text.T("On hold: move {0} off the red walls.", Mod.HeldBy);

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

    private static void Finish(SiteQuest page, Quest quest, string name)
    {
        foreach (var entry in quest.QuestEntries)
            if (entry.State != QuestState.Completed) entry.Complete();
        // Completing the last entry ends the quest on its own, unless S1API had not added the entries yet.
        if (QuestManager.GetQuestByName(Text.T(page.Title)) != null) quest.Complete();
        Mod.Log("Construction quest of " + name + " completed.");
    }

    // The first look after a load only takes note: a reload changes nothing the player needs to hear about.
    private static void Report(SiteDefinition site, SiteQuest page)
    {
        var state = Mod.Engine.GetUpgrade(site.Base.Id, page.UpgradeId);
        var upgrade = site.UpgradeOf(page.UpgradeId);
        // A hold belongs to the build under construction.
        var now = new Seen(state.Stage, state.CompletedCycles, state.Stage == UpgradeStage.Constructing ? Mod.HeldBy : "");
        var key = (site, page.UpgradeId);
        if (!Last.TryGetValue(key, out var before))
        {
            Last[key] = now;
            return;
        }
        if (now == before) return;
        Last[key] = now;
        string done = site.FollowUp(page.UpgradeId)?.Done ?? "Ready to move in";
        // The list shrinks while the player clears the walls, and only the start of a hold is news.
        if (now.Held.Length > 0 && before.Held.Length == 0) Queue(Text.T(page.Title), Text.T("Construction on hold"));
        else if (now.Stage == UpgradeStage.Complete && before.Stage != UpgradeStage.Complete) Queue(Text.T(page.Title), Text.T(done), true);
        else if (now.Stage == UpgradeStage.Constructing && now.Cycles > before.Cycles)
        {
            string phase = Mod.Engine.CurrentPhase(state.BaseId, state.UpgradeId) ?? "";
            if (site.PhaseTitles.TryGetValue(phase, out string? title))
                Queue(Text.T(title), NightsLeft(upgrade.ConstructionCycles - now.Cycles), true);
            else Mod.Warn("Morning notice skipped: phase " + phase + " has no title.");
        }
    }

    // A follow-up takes one night and has no quest, so its finish is the only news.
    private static void Report(SiteDefinition site, SiteFollowUp followUp)
    {
        var stage = Mod.Engine.GetUpgrade(site.Base.Id, followUp.UpgradeId).Stage;
        var key = (site, followUp.UpgradeId);
        bool seen = LastFollowUp.TryGetValue(key, out var before);
        LastFollowUp[key] = stage;
        if (!seen || stage == before) return;
        if (stage == UpgradeStage.Complete) Queue(Text.T(followUp.Title), Text.T(followUp.Done), true);
        Mod.Log("Follow-up " + followUp.UpgradeId + " of " + site.Base.Id + " is now " + stage + ".");
    }

    private static void Queue(string title, string subtitle, bool work = false) =>
        Pending.Add(new Notice(title, subtitle, Time.unscaledTime + NoticeDelay, work));

    private static void ShowDue()
    {
        for (int i = Pending.Count - 1; i >= 0; i--)
        {
            if (Pending[i].Due > Time.unscaledTime) continue;
            var notice = Pending[i];
            Pending.RemoveAt(i);
            Notify(notice);
            if (notice.Work) ConstructionSounds.Play();
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
