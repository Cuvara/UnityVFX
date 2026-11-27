using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Cuvara.VFX.Tests
{
    /// <summary>
    /// <see cref="VfxLightingRig"/> sets every value of each lighting preset; <see cref="VfxCameraRig"/>
    /// places the camera at <see cref="VfxCameraPreset.Position"/> with <see cref="VfxCameraPreset.Rotation"/>.
    /// The Showcase and the capture stage share these two, so equal values here mean equal presets there.
    /// </summary>
    public sealed class VfxRigTests
    {
        TestObjects objects;
        AmbientMode ambientMode;
        Color ambientSky, ambientEquator, ambientGround;
        float ambientIntensity;
        Material skybox;
        bool fog;

        [SetUp]
        public void SetUp()
        {
            objects = new TestObjects();
            ambientMode = RenderSettings.ambientMode;
            ambientSky = RenderSettings.ambientSkyColor;
            ambientEquator = RenderSettings.ambientEquatorColor;
            ambientGround = RenderSettings.ambientGroundColor;
            ambientIntensity = RenderSettings.ambientIntensity;
            skybox = RenderSettings.skybox;
            fog = RenderSettings.fog;
        }

        [TearDown]
        public void TearDown()
        {
            objects.DestroyAll();
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.skybox = skybox;
            RenderSettings.fog = fog;
        }

        VfxLightingRig LightingRig(out Light key, out Light fill, out Volume volume, out Camera camera)
        {
            key = objects.GameObject("Key").AddComponent<Light>();
            fill = objects.GameObject("Fill").AddComponent<Light>();
            volume = objects.GameObject("Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            camera = objects.GameObject("Camera").AddComponent<Camera>();
            // Active, so OnDestroy runs when the test destroys it; Start (which applies the default
            // preset) does not run inside a synchronous test.
            GameObject go = objects.GameObject("LightingRig");
            var rig = go.AddComponent<VfxLightingRig>();
            rig.KeyLight = key;
            rig.FillLight = fill;
            rig.Volume = volume;
            rig.TargetCamera = camera;
            return rig;
        }

        static void AssertColor(Color expected, Color actual, string what) =>
            Assert.That(Vector4.Distance(expected, actual), Is.LessThan(1e-5f), $"{what}: expected {expected}, was {actual}");

        static void AssertRotation(Quaternion expected, Quaternion actual, string what) =>
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(0.01f), what);

        static void AssertPreset(VfxLightingPreset p, Light key, Light fill, VolumeProfile profile, Camera camera)
        {
            string n = p.name + ": ";
            Assert.AreEqual(LightType.Directional, key.type, n + "key type");
            AssertColor(p.keyColor, key.color, n + "key colour");
            Assert.AreEqual(p.keyIntensity, key.intensity, 1e-6f, n + "key intensity");
            AssertRotation(VfxLightingPreset.Rotation(p.keyAngles), key.transform.rotation, n + "key rotation");
            Assert.AreEqual(LightShadows.Soft, key.shadows, n + "key shadows");
            Assert.AreEqual(p.keyShadowStrength, key.shadowStrength, 1e-6f, n + "key shadow strength");

            Assert.AreEqual(LightType.Directional, fill.type, n + "fill type");
            AssertColor(p.fillColor, fill.color, n + "fill colour");
            Assert.AreEqual(p.fillIntensity, fill.intensity, 1e-6f, n + "fill intensity");
            AssertRotation(VfxLightingPreset.Rotation(p.fillAngles), fill.transform.rotation, n + "fill rotation");
            Assert.AreEqual(LightShadows.None, fill.shadows, n + "fill shadows");

            Assert.AreEqual(AmbientMode.Trilight, RenderSettings.ambientMode, n + "ambient mode");
            AssertColor(p.ambientSky, RenderSettings.ambientSkyColor, n + "ambient sky");
            AssertColor(p.ambientEquator, RenderSettings.ambientEquatorColor, n + "ambient equator");
            AssertColor(p.ambientGround, RenderSettings.ambientGroundColor, n + "ambient ground");
            Assert.AreEqual(1f, RenderSettings.ambientIntensity, n + "ambient intensity");
            Assert.IsNull(RenderSettings.skybox, n + "no skybox");
            Assert.IsFalse(RenderSettings.fog, n + "no fog");

            Assert.AreEqual(CameraClearFlags.SolidColor, camera.clearFlags, n + "clear flags");
            AssertColor(p.background, camera.backgroundColor, n + "background");

            Assert.AreEqual(4, profile.components.Count, n + "Bloom, Tonemapping, Vignette, Color Adjustments, once each");
            Assert.IsTrue(profile.TryGet(out Bloom bloom), n + "bloom");
            Assert.IsTrue(bloom.active && bloom.intensity.overrideState && bloom.threshold.overrideState && bloom.scatter.overrideState, n + "bloom overrides");
            Assert.AreEqual(p.bloomIntensity, bloom.intensity.value, 1e-6f, n + "bloom intensity");
            Assert.AreEqual(p.bloomThreshold, bloom.threshold.value, 1e-6f, n + "bloom threshold");
            Assert.AreEqual(p.bloomScatter, bloom.scatter.value, 1e-6f, n + "bloom scatter");
            Assert.IsTrue(profile.TryGet(out Tonemapping tonemapping), n + "tonemapping");
            Assert.IsTrue(tonemapping.mode.overrideState, n + "tonemapping override");
            Assert.AreEqual(TonemappingMode.ACES, tonemapping.mode.value, n + "ACES");
            Assert.IsTrue(profile.TryGet(out Vignette vignette), n + "vignette");
            Assert.AreEqual(p.vignetteIntensity, vignette.intensity.value, 1e-6f, n + "vignette intensity");
            Assert.AreEqual(0.4f, vignette.smoothness.value, 1e-6f, n + "vignette smoothness");
            Assert.IsTrue(profile.TryGet(out ColorAdjustments adjustments), n + "colour adjustments");
            Assert.IsTrue(adjustments.postExposure.overrideState, n + "post exposure override");
            Assert.AreEqual(p.postExposure, adjustments.postExposure.value, 1e-6f, n + "post exposure");
        }

        [Test]
        public void LightingRig_SetsEveryValueOfEachPreset_InAnyOrder()
        {
            VfxLightingRig rig = LightingRig(out Light key, out Light fill, out Volume volume, out Camera camera);
            string[] order = { "day", "dusk", "night", "day", "night", "dusk" };
            foreach (string name in order)
            {
                Assert.IsTrue(rig.ApplyPreset(name), name);
                Assert.AreEqual(name, rig.Preset);
                Assert.IsNotNull(rig.RuntimeProfile);
                Assert.AreSame(rig.RuntimeProfile, volume.sharedProfile, name + ": the Volume uses the rig's own profile");
                AssertPreset(VfxLightingPreset.Get(name), key, fill, rig.RuntimeProfile, camera);
            }
        }

        [Test]
        public void LightingRig_StaticApply_SetsTheSameValues()
        {
            var key = objects.GameObject("Key").AddComponent<Light>();
            var fill = objects.GameObject("Fill").AddComponent<Light>();
            var camera = objects.GameObject("Camera").AddComponent<Camera>();
            foreach (string name in VfxLightingPreset.Names)
            {
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                try
                {
                    VfxLightingPreset preset = VfxLightingPreset.Get(name);
                    VfxLightingRig.Apply(preset, key, fill, profile, camera);
                    AssertPreset(preset, key, fill, profile, camera);
                }
                finally
                {
                    foreach (var component in profile.components) Object.DestroyImmediate(component);
                    Object.DestroyImmediate(profile);
                }
            }
        }

        [UnityTest]
        public IEnumerator LightingRig_LeavesTheVolumesOwnProfileUnchanged_AndDestroysItsProfile()
        {
            VfxLightingRig rig = LightingRig(out _, out _, out Volume volume, out _);
            var sceneProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = sceneProfile;
            rig.ApplyPreset("night");
            Assert.AreEqual(0, sceneProfile.components.Count, "the profile the scene gave is not written");
            VolumeProfile own = rig.RuntimeProfile;
            Bloom bloom = null;
            own.TryGet(out bloom);
            Object.Destroy(rig.gameObject);
            yield return null;
            Assert.IsTrue(own == null, "the rig's profile is destroyed with the rig");
            Assert.IsTrue(bloom == null, "and so are the components the preset added to it");
            Object.DestroyImmediate(sceneProfile);
        }

        [Test]
        public void LightingRig_UnknownPreset_ReturnsFalse_AndChangesNothing()
        {
            VfxLightingRig rig = LightingRig(out Light key, out _, out _, out _);
            rig.ApplyPreset("dusk");
            float intensity = key.intensity;
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("unknown lighting preset 'noon'"));
            Assert.IsFalse(rig.ApplyPreset("noon"));
            Assert.AreEqual("dusk", rig.Preset);
            Assert.AreEqual(intensity, key.intensity);
        }

        [Test]
        public void CameraRig_PlacesTheCamera_AtThePresetsPositionAndRotation()
        {
            var camera = objects.GameObject("Camera").AddComponent<Camera>();
            Transform focus = objects.GameObject("Focus").transform;
            focus.position = new Vector3(3f, 0f, -2f);
            GameObject go = objects.GameObject("CameraRig");
            go.SetActive(false);
            var rig = go.AddComponent<VfxCameraRig>();
            rig.TargetCamera = camera;
            rig.Focus = focus;
            rig.FocusOffset = new Vector3(0f, 0.5f, 0f);
            Vector3 origin = focus.position + rig.FocusOffset;
            Assert.AreEqual(origin, rig.FocusPoint);

            foreach (string name in VfxCameraPreset.Names)
            {
                Assert.IsTrue(rig.ApplyPreset(name), name);
                Assert.AreEqual(name, rig.Preset);
                VfxCameraPreset preset = VfxCameraPreset.Get(name);
                Assert.That(Vector3.Distance(preset.Position(origin), camera.transform.position), Is.LessThan(1e-4f), name + " position");
                Assert.That(Quaternion.Angle(preset.Rotation, camera.transform.rotation), Is.LessThan(0.01f), name + " rotation");
                Assert.AreEqual(preset.fieldOfView, camera.fieldOfView, 1e-5f, name + " field of view");
            }
        }

        [Test]
        public void CameraRig_StaticPlace_EqualsThePreset()
        {
            var camera = objects.GameObject("Camera").AddComponent<Camera>();
            var focus = new Vector3(-1f, 0f, 4f);
            foreach (string name in VfxCameraPreset.Names)
            {
                VfxCameraPreset preset = VfxCameraPreset.Get(name);
                VfxCameraRig.Place(camera, preset, focus);
                Assert.That(Vector3.Distance(preset.Position(focus), camera.transform.position), Is.LessThan(1e-4f), name + " position");
                Assert.That(Quaternion.Angle(preset.Rotation, camera.transform.rotation), Is.LessThan(0.01f), name + " rotation");
                Assert.AreEqual(preset.fieldOfView, camera.fieldOfView, 1e-5f, name + " field of view");
                Vector3 toTarget = (preset.Target(focus) - camera.transform.position).normalized;
                Assert.That(Vector3.Dot(camera.transform.forward, toTarget), Is.EqualTo(1f).Within(1e-4f), name + " looks at the target");
            }
        }

        [Test]
        public void CameraRig_WithoutFocus_UsesItsOwnPosition()
        {
            var camera = objects.GameObject("Camera").AddComponent<Camera>();
            GameObject go = objects.GameObject("CameraRig");
            go.transform.position = new Vector3(5f, 0f, 5f);
            var rig = go.AddComponent<VfxCameraRig>();
            rig.TargetCamera = camera;
            rig.ApplyPreset("close");
            VfxCameraPreset preset = VfxCameraPreset.Get("close");
            Assert.That(Vector3.Distance(preset.Position(go.transform.position), camera.transform.position), Is.LessThan(1e-4f));
        }
    }
}
