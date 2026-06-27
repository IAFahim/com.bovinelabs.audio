// Game-facing audio volume settings, persisted to PlayerPrefs. Bind options-menu sliders to
// these properties; AudioVolumeApplySystem pushes them onto the Bridge audio buses each frame.
namespace Vex.Audio
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Persisted master/music/ambiance/effect volumes (0..1) and the mute-when-unfocused toggle.
    /// Single source of truth for game audio volume. Setting a property clamps, saves, and raises
    /// <see cref="Changed"/> so UI can stay in sync.
    /// </summary>
    public static class AudioVolume
    {
        private const string Prefix = "audio.volume.";

        private static float master = Get("master", 1f);
        private static float music = Get("music", 1f);
        private static float ambiance = Get("ambiance", 1f);
        private static float effect = Get("effect", 1f);
        private static bool muteWhenUnfocused = Get("muteUnfocused", 0f) > 0.5f;

        /// <summary>Raised whenever any value changes (e.g. so an options menu can refresh).</summary>
        public static event Action Changed;

        /// <summary>Master multiplier applied on top of every bus (0..1).</summary>
        public static float Master { get => master; set => Set(ref master, "master", value); }

        public static float Music { get => music; set => Set(ref music, "music", value); }

        public static float Ambiance { get => ambiance; set => Set(ref ambiance, "ambiance", value); }

        public static float Effect { get => effect; set => Set(ref effect, "effect", value); }

        /// <summary>When true, all audio is silenced while the application is not focused. Default off.</summary>
        public static bool MuteWhenUnfocused
        {
            get => muteWhenUnfocused;
            set
            {
                if (muteWhenUnfocused == value)
                {
                    return;
                }

                muteWhenUnfocused = value;
                PlayerPrefs.SetFloat(Prefix + "muteUnfocused", value ? 1f : 0f);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        private static void Set(ref float field, string key, float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value))
            {
                return;
            }

            field = value;
            PlayerPrefs.SetFloat(Prefix + key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        private static float Get(string key, float def) => PlayerPrefs.GetFloat(Prefix + key, def);
    }
}
