using UnityEngine;

namespace Galilego.Universe
{
    public enum AudioChannel { Master, Music, Effects, Ambient, Interface }

    /// <summary>
    /// Volume channels for the future sound. Master is applied through
    /// AudioListener.volume by SettingsApplier; every other channel is applied by
    /// <see cref="GameAudioSource"/> — put it next to any AudioSource, pick the
    /// channel, and the source follows the sliders in the menu. No mixer asset needed.
    /// </summary>
    public static class GameAudio
    {
        /// <summary>Slider value of a channel (0..1). For Master this is the master slider.</summary>
        public static float Volume(AudioChannel channel)
        {
            SettingsData d = GameSettings.Data;
            switch (channel)
            {
                case AudioChannel.Music: return d.volMusic;
                case AudioChannel.Effects: return d.volEffects;
                case AudioChannel.Ambient: return d.volAmbient;
                case AudioChannel.Interface: return d.volInterface;
                default: return d.volMaster;
            }
        }
    }

    /// <summary>
    /// Scales an AudioSource by its channel slider. The volume set on the AudioSource
    /// in the inspector stays the "base" volume; this component multiplies it.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [AddComponentMenu("Audio/Game Audio Source")]
    public sealed class GameAudioSource : MonoBehaviour
    {
        [Tooltip("Канал громкости из меню настроек. Master применяется ко всему звуку отдельно.")]
        public AudioChannel Channel = AudioChannel.Effects;

        private AudioSource source;
        private float baseVolume = 1f;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            baseVolume = source.volume;
        }

        private void OnEnable()
        {
            if (source == null)
            {
                source = GetComponent<AudioSource>();
                baseVolume = source.volume;
            }

            GameSettings.Changed += Apply;
            Apply();
        }

        private void OnDisable()
        {
            GameSettings.Changed -= Apply;
            if (source != null)
            {
                source.volume = baseVolume;
            }
        }

        /// <summary>Call if the script changes AudioSource.volume at runtime: sets a new base volume.</summary>
        public void SetBaseVolume(float volume)
        {
            baseVolume = volume;
            Apply();
        }

        private void Apply()
        {
            if (source != null)
            {
                source.volume = baseVolume * (Channel == AudioChannel.Master ? 1f : GameAudio.Volume(Channel));
            }
        }
    }
}
