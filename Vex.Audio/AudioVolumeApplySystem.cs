// Pushes the persisted AudioVolume settings onto Bridge's audio volume buses every frame.
// Bridge declares the buses (AudioVolumeData.* SharedStatics) and reads them in its own
// AudioVolumeSystem, but deliberately never writes them — that is the game's job, this is it.
namespace Vex.Audio
{
    using BovineLabs.Bridge.Data.Audio;
    using Unity.Entities;
    using UnityEngine;

    /// <summary>
    /// Applies <see cref="AudioVolume"/> (master + per-bus + focus mute) to the Bridge audio buses.
    /// Runs automatically in the default game world; no scene setup required.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.Default)]
    public partial class AudioVolumeApplySystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var focus = AudioVolume.MuteWhenUnfocused && !Application.isFocused ? 0f : 1f;
            var master = AudioVolume.Master * focus;

            AudioVolumeData.MusicVolume.Data = AudioVolume.Music * master;
            AudioVolumeData.AmbianceVolume.Data = AudioVolume.Ambiance * master;
            AudioVolumeData.EffectVolume.Data = AudioVolume.Effect * master;
            AudioVolumeData.MuteWhenUnfocused.Data = AudioVolume.MuteWhenUnfocused;
        }
    }
}
