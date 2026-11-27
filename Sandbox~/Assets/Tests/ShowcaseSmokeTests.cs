using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.VFX;

namespace Cuvara.VFX.Sandbox.ShowcaseTests
{
    /// <summary>
    /// Play-mode smoke tests of the Showcase sample (<c>Samples~/Showcase</c>, seen by the Sandbox
    /// through the link <c>Assets/Showcase</c>): the scene loads cleanly, has its four stations and
    /// characters, the overlay's preset paths leave the shared preset data in place, and the fixture
    /// station plays particles.
    /// </summary>
    /// <remarks>
    /// The package's own tests must not depend on the sample, and the sample is only present when
    /// <c>Tools~/setup-sandbox.py</c> created the link, so this assembly has no compile reference to
    /// <c>Cuvara.VFX.Samples.Showcase</c>: it reaches the sample's components by type name and calls
    /// their public methods by reflection. Every test is ignored, with the reason, when
    /// <c>Assets/Showcase</c> is absent (a Sandbox without the link). The CI project gets a copy of the
    /// sample from <c>.github/scripts/ci_sandbox.py</c>, so the tests run there; the particle test is
    /// ignored without a graphics device or compute shaders (Documentation~/CI.md, GPU tests).
    /// </remarks>
    public sealed class ShowcaseSmokeTests
    {
        const string ScenePath = "Assets/Showcase/Showcase.unity";
        const string SampleAssembly = "Cuvara.VFX.Samples.Showcase";
        const string OverlayType = "Cuvara.VFX.Samples.Showcase.ShowcaseOverlay";
        const string SpawnType = "Cuvara.VFX.Samples.Showcase.ShowcaseCharacterSpawn";
        const string FixtureEffect = "burst-quads";

        static readonly string[] StationLabels = { "Chain lightning lane", "Single target", "Ground area", "Empty pedestal" };

        readonly List<string> errors = new List<string>();
        bool previousRunInBackground;

        static bool SamplePresent => File.Exists(Path.Combine(Application.dataPath, "Showcase", "Showcase.unity"));

        static Type SampleType(string fullName) => Type.GetType(fullName + ", " + SampleAssembly);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!SamplePresent)
                Assert.Ignore("Assets/Showcase is absent: run python Tools~/setup-sandbox.py to link Samples~/Showcase into the Sandbox");
            Assert.IsNotNull(SampleType(OverlayType), "the sample assembly " + SampleAssembly + " is compiled");

            previousRunInBackground = Application.runInBackground;
            // An unfocused editor does not advance play mode otherwise (AUTHORING.md, Pitfalls).
            Application.runInBackground = true;
            errors.Clear();
            Application.logMessageReceived += OnLog;
#if UNITY_EDITOR
            AsyncOperation load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
#else
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
#endif
            // Awake and Start of every object, then a few updates.
            for (int i = 0; i < 5; i++) yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            Application.runInBackground = previousRunInBackground;
            Time.timeScale = 1f;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(type + ": " + condition);
        }

        [UnityTest]
        public IEnumerator Scene_LoadsAndPlays_WithNoErrorOrException()
        {
            Assert.AreEqual("Showcase", SceneManager.GetActiveScene().name);
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 1.5f) yield return null;
            Assert.IsEmpty(errors, "errors or exceptions logged while the scene loaded and played");
            Assert.IsNotNull(Overlay(), "the scene has the overlay");
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<VfxLightingRig>(), "the scene has a lighting rig");
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<VfxCameraRig>(), "the scene has a camera rig");
        }

        [UnityTest]
        public IEnumerator Scene_HasTheFourStations_AndTheLaneHasACasterAndThreeTargets3To4MetresApart()
        {
            List<VfxStation> stations = OverlayStations();
            CollectionAssert.AreEqual(StationLabels, stations.Select(s => s.Label).ToArray(), "the overlay's stations, in key order");
            Assert.AreEqual(4, UnityEngine.Object.FindObjectsByType<VfxStation>(FindObjectsSortMode.None).Length, "the scene has exactly four stations");

            VfxStation lane = stations[0];
            Assert.IsNotNull(lane.Anchor, "the lane has a caster");
            Assert.AreEqual(VfxSocket.HandRight, lane.AnchorSocket);
            Assert.AreEqual(VfxSocket.Chest, lane.TargetSocket);
            Assert.AreEqual(3, lane.Targets.Count, "the lane has three targets");
            Assert.IsTrue(lane.Targets.All(t => t != null), "every target is set");
            Assert.IsNotNull(VfxSocket.Find(lane.Anchor, VfxSocket.HandRight), "the caster's hand_r socket exists");
            Assert.AreEqual(VfxSocket.Find(lane.Anchor, VfxSocket.HandRight).transform, lane.ResolvedAnchor, "the lane plays on the caster's hand_r");

            Vector3 previous = Ground(lane.Anchor.position);
            for (int i = 0; i < lane.Targets.Count; i++)
            {
                Assert.IsNotNull(VfxSocket.Find(lane.Targets[i], VfxSocket.Chest), $"target {i} has a chest socket");
                Vector3 here = Ground(lane.Targets[i].position);
                Assert.That(Vector3.Distance(previous, here), Is.InRange(3f, 4f), $"distance to target {i}");
                previous = here;
            }

            VfxStation single = stations[1];
            Assert.IsNotNull(single.Anchor, "the single-target station has a caster");
            Assert.AreEqual(1, single.Targets.Count, "the single-target station has one target");
            yield return null;
        }

        [UnityTest]
        public IEnumerator EveryCharacter_HasTheFiveSockets()
        {
            Type spawnType = SampleType(SpawnType);
            Assert.IsNotNull(spawnType, SpawnType);
            UnityEngine.Object[] spawns = UnityEngine.Object.FindObjectsByType(spawnType, FindObjectsSortMode.None);
            Assert.AreEqual(6, spawns.Length, "four characters at the lane, two at the single-target station");
            foreach (UnityEngine.Object spawn in spawns)
            {
                var instance = (GameObject)Get(spawn, "Instance");
                Assert.IsNotNull(instance, spawn.name + " spawned a character");
                foreach (string socket in VfxSocket.WellKnownNames)
                    Assert.IsNotNull(VfxSocket.Find(instance.transform, socket), $"{spawn.name}: socket {socket}");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpawnPoint_WithoutAPrefab_PlacesThePlaceholderWithTheFiveSockets()
        {
            Type spawnType = SampleType(SpawnType);
            var go = new GameObject("NullSpawn");
            go.SetActive(false);
            Component spawn = go.AddComponent(spawnType);
            LogAssert.Expect(LogType.Log, new Regex("'NullSpawn' has no character prefab.*placeholder mannequin"));
            go.SetActive(true); // Awake spawns

            var instance = (GameObject)Get(spawn, "Instance");
            Assert.IsNotNull(instance);
            Assert.IsTrue((bool)Get(spawn, "IsPlaceholder"));
            Assert.IsNotNull(instance.GetComponent<VfxMannequin>());
            Assert.AreEqual(go.transform, instance.transform.parent);
            foreach (string socket in VfxSocket.WellKnownNames)
                Assert.IsNotNull(VfxSocket.Find(go.transform, socket), "placeholder socket " + socket);
            UnityEngine.Object.Destroy(go);
            yield return null;
            Assert.IsEmpty(errors);
        }

        [UnityTest]
        public IEnumerator LightingPresets_ThroughTheOverlay_EqualTheSharedPresetData()
        {
            Component overlay = Overlay();
            var rig = (VfxLightingRig)Get(overlay, "LightingRig");
            Assert.IsNotNull(rig);
            Assert.AreEqual(VfxLightingPreset.DefaultName, rig.Preset, "dusk is the default");
            foreach (string name in VfxLightingPreset.Names)
            {
                Assert.IsTrue((bool)Call(overlay, "SetLightingPreset", name), name);
                yield return null;
                VfxLightingPreset expected = VfxLightingPreset.Get(name);
                Assert.AreEqual(name, rig.Preset);
                AssertLight(rig.KeyLight, expected.keyColor, expected.keyIntensity, expected.keyAngles, name + " key");
                Assert.AreEqual(LightShadows.Soft, rig.KeyLight.shadows, name + " key shadows");
                Assert.AreEqual(expected.keyShadowStrength, rig.KeyLight.shadowStrength, 1e-5f, name + " key shadow strength");
                AssertLight(rig.FillLight, expected.fillColor, expected.fillIntensity, expected.fillAngles, name + " fill");
                Assert.AreEqual(LightShadows.None, rig.FillLight.shadows, name + " fill shadows");

                Assert.AreEqual(AmbientMode.Trilight, RenderSettings.ambientMode, name);
                AssertColor(expected.ambientSky, RenderSettings.ambientSkyColor, name + " ambient sky");
                AssertColor(expected.ambientEquator, RenderSettings.ambientEquatorColor, name + " ambient equator");
                AssertColor(expected.ambientGround, RenderSettings.ambientGroundColor, name + " ambient ground");
                AssertColor(expected.background, rig.TargetCamera.backgroundColor, name + " background");

                Assert.IsNotNull(rig.Volume, "the rig has a Volume");
                Assert.IsTrue(rig.Volume.isGlobal, "the Volume is global");
                VolumeProfile profile = rig.Volume.sharedProfile;
                Assert.IsNotNull(profile);
                Assert.IsTrue(profile.TryGet(out Bloom bloom), "Bloom");
                Assert.AreEqual(expected.bloomIntensity, bloom.intensity.value, 1e-5f, name + " bloom intensity");
                Assert.AreEqual(expected.bloomThreshold, bloom.threshold.value, 1e-5f, name + " bloom threshold");
                Assert.AreEqual(expected.bloomScatter, bloom.scatter.value, 1e-5f, name + " bloom scatter");
                Assert.IsTrue(profile.TryGet(out Tonemapping tonemapping), "Tonemapping");
                Assert.AreEqual(TonemappingMode.ACES, tonemapping.mode.value, name);
                Assert.IsTrue(profile.TryGet(out Vignette vignette), "Vignette");
                Assert.AreEqual(expected.vignetteIntensity, vignette.intensity.value, 1e-5f, name + " vignette");
                Assert.IsTrue(profile.TryGet(out ColorAdjustments adjustments), "ColorAdjustments");
                Assert.AreEqual(expected.postExposure, adjustments.postExposure.value, 1e-5f, name + " post exposure");
            }
            Assert.IsEmpty(errors);
        }

        [UnityTest]
        public IEnumerator CameraPresets_ThroughTheOverlay_PlaceTheCameraAtTheSharedPresetForTheSelectedStation()
        {
            Component overlay = Overlay();
            Assert.AreEqual(VfxCameraPreset.DefaultName, (string)Get(overlay, "CameraPreset"), "gameplay is the default");
            var rig = (VfxCameraRig)Get(overlay, "CameraRig");
            Assert.IsNotNull(rig);
            List<VfxStation> stations = OverlayStations();
            for (int index = 0; index < stations.Count; index++)
            {
                Assert.IsTrue((bool)Call(overlay, "SelectStation", index), "select " + index);
                foreach (string name in VfxCameraPreset.Names)
                {
                    Assert.IsTrue((bool)Call(overlay, "SetCameraPreset", name), name);
                    yield return null;
                    VfxCameraPreset expected = VfxCameraPreset.Get(name);
                    Camera cam = rig.TargetCamera;
                    Vector3 origin = stations[index].transform.position;
                    string what = $"{stations[index].Label} / {name}";
                    Assert.That(Vector3.Distance(expected.Position(origin), cam.transform.position), Is.LessThan(1e-3f), what + " position");
                    Assert.That(Quaternion.Angle(expected.Rotation, cam.transform.rotation), Is.LessThan(0.01f), what + " rotation");
                    Assert.AreEqual(expected.fieldOfView, cam.fieldOfView, 1e-4f, what + " field of view");
                    Assert.AreEqual(name, (string)Get(overlay, "CameraPreset"));
                }
            }
            Assert.IsEmpty(errors);
        }

        [UnityTest]
        public IEnumerator FixtureStation_HasAliveParticles_AfterItsTrigger()
        {
            // VFX Graph simulates particles with compute shaders (the guard of Tests/Editor/GraphicsRequirement.cs).
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("needs a GPU: the editor has no graphics device (SystemInfo.graphicsDeviceType is Null, for example under -nographics)");
            if (!SystemInfo.supportsComputeShaders)
                Assert.Ignore($"needs compute shaders: the graphics device {SystemInfo.graphicsDeviceType} does not support them");
            VfxStation fixture = OverlayStations().FirstOrDefault(s => s.EffectId == FixtureEffect);
            Assert.IsNotNull(fixture, "a station plays the fixture effect " + FixtureEffect);
            float start = Time.realtimeSinceStartup;
            while (fixture.PlayCount == 0 && Time.realtimeSinceStartup - start < 5f) yield return null;
            Assert.Greater(fixture.PlayCount, 0, "the station's trigger played the effect");

            // aliveParticleCount is a GPU readback that lags by up to 60 frames (AUTHORING.md, Pitfalls).
            int alive = 0;
            start = Time.realtimeSinceStartup;
            while (alive == 0 && Time.realtimeSinceStartup - start < 5f)
            {
                yield return null;
                foreach (VisualEffect effect in UnityEngine.Object.FindObjectsByType<VisualEffect>(FindObjectsSortMode.None))
                    if (effect.visualEffectAsset != null && effect.visualEffectAsset.name == "BurstQuads")
                        alive = Mathf.Max(alive, effect.aliveParticleCount);
            }
            Assert.Greater(alive, 0, "BurstQuads has alive particles after the station's trigger");

            VfxStation pedestal = OverlayStations().Last();
            Assert.IsTrue(string.IsNullOrEmpty(pedestal.EffectId), "the empty pedestal has no effect");
            Assert.AreEqual(0, pedestal.PlayCount, "a station without an effect stays silent");
            Assert.IsEmpty(errors);
        }

        // ------------------------------------------------------------------ helpers

        static Vector3 Ground(Vector3 p) => new Vector3(p.x, 0f, p.z);

        static Component Overlay()
        {
            Type type = SampleType(OverlayType);
            Assert.IsNotNull(type, OverlayType);
            var overlay = (Component)UnityEngine.Object.FindAnyObjectByType(type);
            Assert.IsNotNull(overlay, "the scene has a ShowcaseOverlay");
            return overlay;
        }

        static List<VfxStation> OverlayStations()
        {
            var list = ((IEnumerable<VfxStation>)Get(Overlay(), "Stations")).ToList();
            Assert.IsTrue(list.All(s => s != null), "every overlay station is set");
            return list;
        }

        static object Get(object target, string property)
        {
            PropertyInfo info = target.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(info, target.GetType().Name + "." + property);
            return info.GetValue(target);
        }

        static object Call(object target, string method, params object[] arguments)
        {
            MethodInfo info = target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(info, target.GetType().Name + "." + method);
            return info.Invoke(target, arguments);
        }

        static void AssertLight(Light light, Color color, float intensity, Vector2 angles, string what)
        {
            Assert.IsNotNull(light, what);
            Assert.AreEqual(LightType.Directional, light.type, what);
            AssertColor(color, light.color, what + " colour");
            Assert.AreEqual(intensity, light.intensity, 1e-5f, what + " intensity");
            Assert.That(Quaternion.Angle(VfxLightingPreset.Rotation(angles), light.transform.rotation), Is.LessThan(0.01f), what + " rotation");
        }

        static void AssertColor(Color expected, Color actual, string what)
        {
            Assert.That(Mathf.Abs(expected.r - actual.r) + Mathf.Abs(expected.g - actual.g) + Mathf.Abs(expected.b - actual.b),
                Is.LessThan(1e-4f), $"{what}: expected {expected}, was {actual}");
        }
    }
}
