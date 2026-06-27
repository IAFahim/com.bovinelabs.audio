# Audio Showcase — using the Vibe audio system (timeline-driven)

A worked example of **playing and shaping sound from a DOTS Timeline** using the
Vibe audio tracks on top of the Bridge audio backend. Open `AudioShowcase.unity`
and press Play the same way you run the other samples; the director loops a 12s
timeline that exercises the whole stack.

## The mental model (two layers)

Vibe makes **no sound of its own**. It only contributes Timeline *tracks* that
read/write ECS components owned by **Bridge**, and Bridge plays the actual audio.

```
Timeline clip (Vibe)  ->  Vibe TrackSystem  ->  Bridge ECS component  ->  Bridge AudioSyncSystem  ->  managed AudioSource
   (authoring)             (per frame)           (data)                    (Burst marshalling)         (you hear it)
```

- **Bridge** (`com.bovinelabs.bridge`) pools managed `AudioSource` GameObjects
  behind `AudioSyncSystem` and exposes them to ECS:
  - `AudioSourceData` — Volume, Pitch
  - `AudioSourceDataExtended` — Clip, PanStereo, SpatialBlend + 3D distance fields
  - `AudioSourceEnabled` — enable = play, disable = stop (an `IEnableableComponent`)
  - `AudioSourceIndex.PoolIndex` — which pooled source this entity got (-1 = none)
  - `MusicSelection.TrackId` — the global music slot (singleton)
  - one `*FilterData` component per audio filter (low/high-pass, echo, reverb, …)
  - A baked `AudioSource` GameObject **becomes** such an entity via `AudioSourceBaker`.
- **Vibe** (`com.bovinelabs.vibe`) adds the Timeline tracks below. Every Vibe audio
  track is `#if BOVINELABS_BRIDGE` (auto-defined here because the Bridge package is
  present), so Vibe audio only exists when Bridge does.

**So "how do I play a sound" =** drop an `AudioSource` in a SubScene → add a Vibe
audio track to a director's TimelineAsset → bind the track to that AudioSource →
add clips. That's exactly what this sample does.

## What this sample contains

- `AudioShowcase.unity` (the main scene) — **self-contained & audible standalone**:
  - **Main Camera** with an `AudioListener` (required for *any* audio to be heard).
  - the **AudioShowcase SubScene** reference.
  - (Volume comes from the game-wide `Vex.Audio` system — see gotcha 3 below — so no
    per-scene volume object is needed.)
- `AudioShowcase_Sub.unity` (the baked SubScene)
  - **AudioSource Cube** — a cube carrying a classic `AudioSource` (2D, no play-on-awake)
    **and** an `AudioLowPassFilter`. The bakers turn these into the ECS audio entity.
  - **Audio Settings** — a `SettingsAuthoring` baking `BridgeSettings`, so the audio pool
    + `MusicSelection` singleton exist when this scene runs on its own (see below).
  - **Director** — a bare `PlayableDirector` (play-on-awake, wrap = Loop) pointing at
    the timeline. No marker component is needed in this project — a director with a
    `TimelineAsset` bakes and activates on its own (matches the other samples).
- `Timelines/AudioShowcase.playable` — the 12s timeline (tracks below).
- `Audio/` — placeholder clips generated with `ffmpeg`
  (`sfx_blip`, `sfx_blip2`, `sfx_thud`, `music_bed_b`) + `MusicTrack_BedB.asset`
  (a 2nd `MusicTrackDefinition`, id 2). Swap these for real assets any time — the
  timeline references clips by object, so nothing else changes.

## Why it was silent (3 standalone-audio gotchas)

The timeline drives the ECS audio data fine, but **hearing** it standalone needs three
things the game's normal bootstrap would otherwise supply — all now baked into this sample:

1. **An `AudioListener`.** No listener → no output, period. Provided by the Main Camera.
2. **The global Bridge audio settings.** `MusicSelection`, the music registry, and the
   managed AudioSource **pool** all come from `BridgeSettings` via a `SettingsAuthoring`.
   Without them the pool is empty and nothing can play. (Don't add a *second* settings
   entity — a duplicate makes every `GetSingleton` across Essence/Reaction/Audio throw.)
3. **Non-zero volume buses.** `AudioVolumeData.MusicVolume` / `AmbianceVolume` /
   `EffectVolume` are `SharedStatic<float>` the game must drive; Bridge never writes
   them, so they sit at **0** (silent) until something does. The game-wide
   **`Vex.Audio`** system (`Assets/Scripts/Audio/`) owns this: `AudioVolume` holds the
   persisted master/music/ambiance/effect levels (PlayerPrefs — bind your options
   sliders to it) and `AudioVolumeApplySystem` pushes them onto the Bridge buses every
   frame. Defaults are 1, so audio just works.

The pool's managed AudioSources are created with hide flags, so they won't show in the
Hierarchy or `FindObjectsByType` — that's normal, not a bug.

### Settings touched (outside this folder)

`Assets/Settings/Settings/BridgeSettings.asset` was edited so the demo is audible:
- `loopedAudioPoolSize` / `oneShotAudioPoolSize` bumped 2 → **8**.
- `musicTracks` now lists **both** music defs (menu = id 1, bed B = id 2) so the
  music track can crossfade between them.

> **Pool-size caveat:** `oneShotAudioPoolSize` must exceed the peak number of
> simultaneous one-shots, or triggers are silently dropped. Music + looped sources
> draw from `loopedAudioPoolSize`.

## The timeline (track cheatsheet)

| Track (Vibe) | Bound to | Clip(s) | Demonstrates | Writes (Bridge) |
|---|---|---|---|---|
| `MusicSelectionTrack` | *(global, no binding)* | Bed A @0–6, Bed B @6–12 | crossfade between two music beds | `MusicSelection.TrackId` |
| `AudioSourceDataTrack` | AudioSource | `AudioSourceVolumeSweepClip` @0–3 | fade in (vol 0→1) | `AudioSourceData.Volume` |
| `AudioSourceDataTrack` | AudioSource | `AudioSourcePitchSweepClip` @7–11 | slow-mo pitch dip (1→0.5) | `AudioSourceData.Pitch` |
| `AudioLowPassFilterTrack` | AudioLowPassFilter | `AudioLowPassFilterSweepClip` @7–11 | "underwater" muffle (22 kHz→600 Hz) | `AudioLowPassFilterData.CutoffFrequency` |
| `AudioSourcePanSweepTrack` | AudioSource | `AudioSourcePanSweepClip` @3–6 | stereo pan (L→R) | `AudioSourceDataExtended.PanStereo` |
| `AudioSourceTriggerTrack` | AudioSource | 4× `AudioSourceTriggerClip` @1/2.5/4/5.5 | one-shot SFX, randomized vol+pitch from a 3-clip list | `AudioSourceData` + `AudioSourceEnabled` |

Sweep clips evaluate `value = lerp(min, max, curve(t))` (linear curve here,
`remapCurveToClipLength = true`). The trigger clips use `action = Play`,
`forceRestart = true`, and a deterministic `seed` so the randomized clip/volume/pitch
pick is repeatable per run.

## Verifying it works

Press Play and **listen** — that's the only check a script can't make for you.
You should hear: music fade in, a crossfade to the second bed at ~6s, a few
randomized blips/thuds in the first half, a stereo sweep, then a pitch-down +
muffle near the end.

To verify the *systems are driving the data* (no ears required), enter Play on this
scene and inspect the audio entity in the ECS world:

- `AudioSourceData.Volume` / `.Pitch` ramp during the sweep clips
- `AudioSourceEnabled` toggles on at each trigger beat
- `AudioSourceIndex.PoolIndex` becomes ≥ 0 (the pool assigned a source)
- `MusicSelection.TrackId` flips 1 → 2 at the 6s crossfade
- `AudioLowPassFilterData.CutoffFrequency` and `AudioSourceDataExtended.PanStereo`
  change over their clips

## Extending it

The other audio tracks follow the **identical pattern** — bind a component, add a
sweep/animated/initial clip:

- More filters: `AudioHighPassFilterTrack`, `AudioEchoFilterTrack`,
  `AudioReverbFilterTrack`, `AudioChorusFilterTrack`, `AudioDistortionFilterTrack`
  (each binds its matching `Audio*Filter` component on the source).
- `AudioSourceClipTrack` to swap the clip itself over the timeline.
- Each filter/data track also has `*InitialClip` (restore captured value) and
  `*Clip` (animated) variants alongside the `*SweepClip` shown here.

## Rebuilding

The whole sample is generated by `Assets/Editor/AudioShowcase/BuildAudioShowcase.cs`
(menu: **Tools ▸ Build Audio Showcase**). It is idempotent — re-running rebuilds the
timeline and scenes from scratch. The `ffmpeg` clips are committed; regenerate them
from that script's history if needed.
