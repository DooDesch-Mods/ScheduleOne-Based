using System.Collections.Generic;
using System.Linq;
using Il2CppScheduleOne.Audio;
using Il2CppScheduleOne.Core.Audio;
using Il2CppScheduleOne.DevUtilities;
using UnityEngine;

namespace Based.Runtime;

// A short burst of site noise the morning a night moved the construction on. It plays once, for the player's ears only.
internal static class ConstructionSounds
{
    private const float BaseVolume = .55f;
    private static readonly (string Clip, float At)[] Burst =
    {
        ("Generic smooth hammer impact 1", 0f),
        ("Generic smooth hammer impact 2", .32f),
        ("Generic smooth hammer impact 3", .62f),
        ("Wood impact 1", 1.15f),
        ("Generic smooth hammer impact 2", 1.6f),
        ("Generic smooth hammer impact 1", 1.88f),
        ("Wood impact 2", 2.35f)
    };

    private static readonly Queue<(AudioClip Clip, float Due)> Pending = new();
    private static AudioSource? _source;

    internal static void Reset() => Pending.Clear();

    internal static void Play()
    {
        var names = Burst.Select(b => b.Clip).Distinct().ToArray();
        var clips = Resources.FindObjectsOfTypeAll<AudioClip>().Where(c => c != null && names.Contains(c.name)).GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
        var missing = names.Where(n => !clips.ContainsKey(n)).ToArray();
        if (missing.Length > 0) Mod.Warn("Morning construction sound plays without: " + string.Join(", ", missing));
        Pending.Clear();
        foreach (var (clip, at) in Burst)
            if (clips.TryGetValue(clip, out var found)) Pending.Enqueue((found, Time.unscaledTime + at));
        Mod.Log("Morning construction sound started with " + Pending.Count + " hits.");
    }

    internal static void Tick()
    {
        while (Pending.Count > 0 && Pending.Peek().Due <= Time.unscaledTime)
        {
            var clip = Pending.Dequeue().Clip;
            var source = Source();
            source.volume = BaseVolume * Singleton<AudioManager>.Instance.GetVolume(EAudioType.FX);
            source.PlayOneShot(clip);
        }
    }

    private static AudioSource Source()
    {
        if (_source != null) return _source;
        var holder = new GameObject("Based.MorningWork");
        UnityEngine.Object.DontDestroyOnLoad(holder);
        _source = holder.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0;
        _source.outputAudioMixerGroup = Singleton<AudioManager>.Instance.MainGameMixer;
        return _source;
    }
}
