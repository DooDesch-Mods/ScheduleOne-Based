namespace Based.Core;

// One entry of the game's daily weather sequence, in game minutes.
public readonly record struct WeatherSlot(int ActiveTime, int TransitionInTime, bool Rains);

// The game lays a day's weather out as a list of slots counted from 07:00. The sleep skips the clock, so the
// weather of a night is read from that list instead of being watched.
public static class RainWindow
{
    public const int DayMinutes = 1440;
    private const int DayStartMinute = 7 * 60;

    // Minutes since 07:00 for a 24-hour clock value such as 2230 or 130.
    public static int MinuteOfWeatherDay(int time24)
    {
        int minute = time24 / 100 * 60 + time24 % 100 - DayStartMinute;
        return time24 < 700 ? minute + DayMinutes : minute;
    }

    // True when a rainy slot is fully in place for any part of [fromMinute, toMinute). A slot starts with its
    // transition, while the weather is still moving in, so only the time after it counts.
    public static bool Rains(IReadOnlyList<WeatherSlot> slots, int fromMinute, int toMinute)
    {
        int start = 0;
        foreach (var slot in slots)
        {
            int end = start + slot.ActiveTime + slot.TransitionInTime;
            if (slot.Rains && fromMinute < end && toMinute > start + slot.TransitionInTime) return true;
            start = end;
        }
        return false;
    }
}
