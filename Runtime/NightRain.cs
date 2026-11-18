using System;
using System.Collections.Generic;
using Based.Core;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Weather;

namespace Based.Runtime;

// Tells whether it rains during the night a player is about to sleep. The sleep jumps the clock, so nothing is
// watched: the game lays out the day's weather as a list of slots counted from 07:00, and the host reads that list
// when the sleep starts, before the game rolls the next day's weather.
internal static class NightRain
{
    // The result for the sleep in progress; null when no sleep start was seen.
    private static bool? _sampled;

    internal static string Describe() =>
        "sampled=" + (_sampled?.ToString() ?? "none")
#if DEBUG
        + ", forced=" + (Forced?.ToString() ?? "off")
#endif
        ;

#if DEBUG
    // A test decides the weather of the next nights; null leaves it to the game.
    internal static bool? Forced;
#endif

    internal static void SleepStarted()
    {
        _sampled = null;
        if (!NativeBus.IsHost) { Based.Mod.Log("Rain check skipped at sleep start: only the host decides."); return; }
        try
        {
            _sampled = Sample(out string detail);
            Based.Mod.Log("Rain check at sleep start: " + (_sampled == true ? "it rains tonight" : "it stays dry tonight") + "; " + detail + ".");
        }
        catch (Exception error) { Based.Mod.Warn("Rain check at sleep start failed; the night counts as dry: " + error.Message); }
    }

    // The weather of the night that just passed, or false when it could not be read. The sample is used once.
    internal static bool Take(out string source)
    {
        var sampled = _sampled;
        _sampled = null;
#if DEBUG
        if (Forced != null) { source = "forced by baseddiag rain"; return Forced.Value; }
#endif
        if (sampled == null) { source = "no sample from the sleep start"; return false; }
        source = "sampled at sleep start";
        return sampled.Value;
    }

    private static bool Sample(out string detail)
    {
        if (!NetworkSingleton<EnvironmentManager>.InstanceExists) { detail = "no environment manager"; return false; }
        if (!NetworkSingleton<TimeManager>.InstanceExists) { detail = "no time manager"; return false; }
        var sequence = NetworkSingleton<EnvironmentManager>.Instance._currentWeatherSequence;
        if (sequence == null) { detail = "no current weather sequence"; return false; }
        var items = sequence.WeatherProfiles;
        if (items == null) { detail = "sequence " + sequence.Id + " has no slots"; return false; }
        var slots = new List<WeatherSlot>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            // A slot's rain effect exists only when its profile switches rain on.
            bool rains = item?.Profile?.RainSettings?.IsActive == true;
            slots.Add(new WeatherSlot(item?.ActiveTime ?? 0, item?.TransitionInTime ?? 0, rains));
        }
        int time = NetworkSingleton<TimeManager>.Instance.CurrentTime;
        int from = RainWindow.MinuteOfWeatherDay(time);
        detail = "sequence " + sequence.Id + ", " + slots.Count + " slots, from " + time.ToString("0000") + " to 0700";
        return RainWindow.Rains(slots, from, RainWindow.DayMinutes);
    }
}
