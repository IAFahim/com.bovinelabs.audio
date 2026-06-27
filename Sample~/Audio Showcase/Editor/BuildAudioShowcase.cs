// Builds the Vibe Audio Showcase: a SubScene (AudioSource + low-pass filter + director),
// a DOTS Timeline driving the Vibe audio tracks, and a main scene referencing the SubScene.
// Idempotent: re-running rebuilds the timeline + scenes from scratch.
namespace AudioShowcase.Editor
{
    using System;
    using System.Reflection;
    using BovineLabs.Bridge.Authoring.Audio;
    using BovineLabs.Vibe.Authoring.Audio;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.Playables;
    using UnityEngine.SceneManagement;
    using UnityEngine.Timeline;

    public static class BuildAudioShowcase
    {
        private const string Dir = "Assets/Samples/AudioShowcase";
        private const string SubPath = Dir + "/AudioShowcase_Sub.unity";
        private const string MainPath = Dir + "/AudioShowcase.unity";
        private const string TimelinePath = Dir + "/Timelines/AudioShowcase.playable";
        private const string AudioDir = Dir + "/Audio";
        private const string BedBDefPath = AudioDir + "/MusicTrack_BedB.asset";
        private const string BgmDefPath = "Assets/Settings/Audio/MusicTrackDefinition.asset";
        private const string BridgeSettingsPath = "Assets/Settings/Settings/BridgeSettings.asset";

        // com.vex.preload stage prefabs: the host carries Main Camera + AudioListener + lights/inputs;
        // the content carries the full Settings (incl. BridgeSettings -> audio pool + MusicSelection),
        // a camera, and AudioSource Cubes. Building on these = no duplicate listeners/settings.
        private const string HostPrefabPath = "Assets/Prefabs/Required In Scene.prefab";
        private const string ContentPrefabPath = "Assets/Prefabs/Required In Subscene.prefab";

        [MenuItem("Tools/Build Audio Showcase")]
        public static void Menu() => Debug.Log(Run());

        public static string Run()
        {
            try { return Build(); }
            catch (Exception e) { return "ERROR|" + e.Message + "\n" + e.StackTrace; }
        }

        private static string Build()
        {
            if (EditorApplication.isPlaying) return "BLOCKED|editor in play mode";

            // Suppress com.vex.preload's auto-inject hooks while we build, so the prefabs we instantiate
            // here are the only Required In Scene / Required In Subscene copies (no duplicates).
            SetPreloadBuilding(true);
            try
            {
                return BuildInner();
            }
            finally
            {
                SetPreloadBuilding(false);
            }
        }

        private static string BuildInner()
        {
            var originalScene = EditorSceneManager.GetActiveScene().path;
            AssetDatabase.Refresh();

            // --- folders ---
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Samples", "AudioShowcase");
            if (!AssetDatabase.IsValidFolder(Dir + "/Timelines")) AssetDatabase.CreateFolder(Dir, "Timelines");
            if (!AssetDatabase.IsValidFolder(AudioDir)) AssetDatabase.CreateFolder(Dir, "Audio");

            // --- clips ---
            var blip = Load<AudioClip>(AudioDir + "/sfx_blip.wav");
            var blip2 = Load<AudioClip>(AudioDir + "/sfx_blip2.wav");
            var thud = Load<AudioClip>(AudioDir + "/sfx_thud.wav");
            var bedB = Load<AudioClip>(AudioDir + "/music_bed_b.wav");
            if (blip == null || blip2 == null || thud == null || bedB == null)
                return "ERROR|missing generated audio clips under " + AudioDir;

            // --- 2nd music track definition (id 2) ---
            var bgmDef = AssetDatabase.LoadAssetAtPath<MusicTrackDefinition>(BgmDefPath);
            if (bgmDef == null) return "ERROR|missing existing MusicTrackDefinition at " + BgmDefPath;

            var bedBDef = AssetDatabase.LoadAssetAtPath<MusicTrackDefinition>(BedBDefPath);
            if (bedBDef == null)
            {
                bedBDef = ScriptableObject.CreateInstance<MusicTrackDefinition>();
                AssetDatabase.CreateAsset(bedBDef, BedBDefPath);
            }
            var defSo = new SerializedObject(bedBDef);
            defSo.FindProperty("id").intValue = 2;
            defSo.FindProperty("clip").objectReferenceValue = bedB;
            defSo.FindProperty("baseVolume").floatValue = 1f;
            defSo.FindProperty("blendOverrideSeconds").floatValue = 0f;
            defSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bedBDef);

            // --- BridgeSettings: pool 8/8 + register both music tracks ---
            var bridge = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(BridgeSettingsPath);
            if (bridge == null) return "ERROR|missing BridgeSettings at " + BridgeSettingsPath;
            var bso = new SerializedObject(bridge);
            bso.FindProperty("loopedAudioPoolSize").intValue = 8;
            bso.FindProperty("oneShotAudioPoolSize").intValue = 8;
            var tracks = bso.FindProperty("musicTracks");
            EnsureInArray(tracks, bgmDef);
            EnsureInArray(tracks, bedBDef);
            bso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bridge);

            // --- timeline asset (rebuild fresh) ---
            AssetDatabase.DeleteAsset(TimelinePath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);

            var musicTrack = timeline.CreateTrack<MusicSelectionTrack>(null, "Music");
            MusicClip(musicTrack, 0, 6, "Bed A (menu)", bgmDef);
            MusicClip(musicTrack, 6, 6, "Bed B (crossfade)", bedBDef);

            var dataTrack = timeline.CreateTrack<AudioSourceDataTrack>(null, "Volume / Pitch");
            Sweep<AudioSourceVolumeSweepClip>(dataTrack, 0, 3, "Fade In", "minVolume", 0f, "maxVolume", 1f);
            Sweep<AudioSourcePitchSweepClip>(dataTrack, 7, 4, "Slow-mo Pitch Dip", "minPitch", 1f, "maxPitch", 0.5f);

            var lpfTrack = timeline.CreateTrack<AudioLowPassFilterTrack>(null, "Low-Pass (muffle)");
            Sweep<AudioLowPassFilterSweepClip>(lpfTrack, 7, 4, "Underwater", "minCutoffFrequency", 22000f, "maxCutoffFrequency", 600f);

            var panTrack = timeline.CreateTrack<AudioSourcePanSweepTrack>(null, "Stereo Pan");
            Sweep<AudioSourcePanSweepClip>(panTrack, 3, 3, "Pan L>R", "minPan", -1f, "maxPan", 1f);

            var trigTrack = timeline.CreateTrack<AudioSourceTriggerTrack>(null, "One-Shot Triggers");
            double[] beats = { 1.0, 2.5, 4.0, 5.5 };
            for (int i = 0; i < beats.Length; i++)
                TriggerClip(trigTrack, beats[i], i, blip, blip2, thud);

            EditorUtility.SetDirty(timeline);

            // --- SubScene (rebuild fresh) ---
            var sub = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(sub);

            // Build on the project's stage prefab: it bakes the full Settings (incl. BridgeSettings ->
            // audio pool + MusicSelection) and provides AudioSource Cubes. preload sees this prefab as
            // content, so it won't inject a second copy -> no duplicate settings singletons.
            var contentPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContentPrefabPath);
            if (contentPrefab == null) return "ERROR|missing stage content prefab " + ContentPrefabPath;
            var content = (GameObject)PrefabUtility.InstantiatePrefab(contentPrefab, sub);

            // Bind the timeline to the stage's existing AudioSource Cube (it already has AudioLowPassFilter).
            AudioSource src = null;
            foreach (var s in content.GetComponentsInChildren<AudioSource>(true)) { src = s; break; }
            if (src == null) return "ERROR|stage content prefab has no AudioSource";
            src.playOnAwake = false;
            src.loop = false;
            src.spatialBlend = 0f;     // 2D so it is audible regardless of listener distance; pan sweep still works
            src.clip = blip;           // default clip; trigger clips swap this at runtime
            src.volume = 1f;

            var lpf = src.GetComponent<AudioLowPassFilter>();
            if (lpf == null) lpf = src.gameObject.AddComponent<AudioLowPassFilter>();
            lpf.cutoffFrequency = 22000f;

            var directorGo = new GameObject("Director");
            var director = directorGo.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            director.playOnAwake = true;
            director.extrapolationMode = DirectorWrapMode.Loop;

            // scene-side generic bindings (MusicSelectionTrack is global -> no binding)
            director.SetGenericBinding(dataTrack, src);
            director.SetGenericBinding(panTrack, src);
            director.SetGenericBinding(trigTrack, src);
            director.SetGenericBinding(lpfTrack, lpf);

            EditorSceneManager.SaveScene(sub, SubPath);

            // --- Main scene (rebuild fresh) ---
            var main = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(main);

            // Host stage prefab provides Main Camera + AudioListener (the "ears") + lights + inputs.
            // preload recognizes it as the host, so it won't inject a duplicate. Volume buses are driven
            // game-wide by Vex.Audio.AudioVolumeApplySystem.
            var hostPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HostPrefabPath);
            if (hostPrefab == null) return "ERROR|missing stage host prefab " + HostPrefabPath;
            PrefabUtility.InstantiatePrefab(hostPrefab, main);

            var subGo = new GameObject("AudioShowcase SubScene");
            var subComp = subGo.AddComponent<Unity.Scenes.SubScene>();
            subComp.SceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubPath);
            subComp.AutoLoadScene = true;

            EditorSceneManager.SaveScene(main, MainPath);

            AssetDatabase.SaveAssets();

            // --- restore editor ---
            EditorSceneManager.CloseScene(sub, true);
            EditorSceneManager.CloseScene(main, true);
            if (!string.IsNullOrEmpty(originalScene))
                EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);

            return "OK|built " + MainPath + " + " + SubPath + " + " + TimelinePath +
                   " | music tracks=" + tracks.arraySize + " | trigger beats=" + beats.Length;
        }

        // Toggle com.vex.preload's IsBuilding guard (private setter) via reflection, the same flag the
        // package sets around its own programmatic scene building. No-op if the package isn't present.
        private static void SetPreloadBuilding(bool value)
        {
            Type guard = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                guard = a.GetType("Vex.Preload.Editor.PreloadPlayGuard");
                if (guard != null) break;
            }
            if (guard == null) return;

            var setter = guard.GetProperty("IsBuilding", BindingFlags.Public | BindingFlags.Static)?.GetSetMethod(true);
            if (setter != null) { setter.Invoke(null, new object[] { value }); return; }
            guard.GetField("<IsBuilding>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, value);
        }

        private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path);

        private static void EnsureInArray(SerializedProperty arr, UnityEngine.Object obj)
        {
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == obj) return;
            arr.arraySize++;
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = obj;
        }

        private static void MusicClip(TrackAsset track, double start, double dur, string name, UnityEngine.Object def)
        {
            var clip = track.CreateClip<MusicSelectionClip>();
            clip.start = start;
            clip.duration = dur;
            clip.displayName = name;
            var so = new SerializedObject(clip.asset);
            so.FindProperty("track").objectReferenceValue = def;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Sweep<T>(TrackAsset track, double start, double dur, string name,
            string minField, float min, string maxField, float max) where T : PlayableAsset, ITimelineClipAsset, new()
        {
            var clip = track.CreateClip<T>();
            clip.start = start;
            clip.duration = dur;
            clip.displayName = name;
            var so = new SerializedObject(clip.asset);
            so.FindProperty(minField).floatValue = min;
            so.FindProperty(maxField).floatValue = max;
            so.FindProperty("relative").boolValue = false;
            so.FindProperty("remapCurveToClipLength").boolValue = true;
            so.FindProperty("curve").animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void TriggerClip(TrackAsset track, double start, int index,
            AudioClip a, AudioClip b, AudioClip c)
        {
            var clip = track.CreateClip<AudioSourceTriggerClip>();
            clip.start = start;
            clip.duration = 0.3;
            clip.displayName = "SFX " + (index + 1);
            var so = new SerializedObject(clip.asset);
            so.FindProperty("action").enumValueIndex = 0; // Play
            var clips = so.FindProperty("clips");
            clips.arraySize = 3;
            clips.GetArrayElementAtIndex(0).objectReferenceValue = a;
            clips.GetArrayElementAtIndex(1).objectReferenceValue = b;
            clips.GetArrayElementAtIndex(2).objectReferenceValue = c;
            so.FindProperty("minVolume").floatValue = 0.6f;
            so.FindProperty("maxVolume").floatValue = 1f;
            so.FindProperty("minPitch").floatValue = 0.8f;
            so.FindProperty("maxPitch").floatValue = 1.2f;
            so.FindProperty("seed").intValue = (index * 7) + 1;
            so.FindProperty("forceRestart").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
