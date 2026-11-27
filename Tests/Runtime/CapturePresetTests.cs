using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cuvara.VFX.Tests
{
    /// <summary>
    /// The lighting and camera presets shared by the <c>capture</c> verb and the Showcase: names,
    /// defaults, copies, and what applying them sets.
    /// </summary>
    public sealed class CapturePresetTests
    {
        [Test]
        public void LightingPresets_AreDayDuskNight_WithDuskAsDefault()
        {
            Assert.That(VfxLightingPreset.Names, Is.EqualTo(new[] { "day", "dusk", "night" }));
            Assert.That(VfxLightingPreset.DefaultName, Is.EqualTo("dusk"));
            foreach (var name in VfxLightingPreset.Names)
                Assert.That(VfxLightingPreset.Get(name).name, Is.EqualTo(name));
            Assert.That(VfxLightingPreset.Get("noon"), Is.Null);
            Assert.That(VfxLightingPreset.Get(null), Is.Null);
        }

        [Test]
        public void CameraPresets_AreGameplayCloseSide_WithGameplayAsDefault()
        {
            Assert.That(VfxCameraPreset.Names, Is.EqualTo(new[] { "gameplay", "close", "side" }));
            Assert.That(VfxCameraPreset.DefaultName, Is.EqualTo("gameplay"));
            foreach (var name in VfxCameraPreset.Names)
                Assert.That(VfxCameraPreset.Get(name).name, Is.EqualTo(name));
            Assert.That(VfxCameraPreset.Get("top"), Is.Null);
        }

        [Test]
        public void Get_ReturnsACopy_SoChangingItKeepsThePreset()
        {
            var lighting = VfxLightingPreset.Get("dusk");
            lighting.keyIntensity = 99f;
            Assert.That(VfxLightingPreset.Get("dusk").keyIntensity, Is.Not.EqualTo(99f));

            var camera = VfxCameraPreset.Get("gameplay");
            camera.distance = 99f;
            Assert.That(VfxCameraPreset.Get("gameplay").distance, Is.Not.EqualTo(99f));
        }

        [Test]
        public void LightingPresets_GetDarkerFromDayToNight()
        {
            float Brightness(VfxLightingPreset p) => p.keyIntensity * p.keyColor.grayscale + p.ambientSky.grayscale + p.background.grayscale;
            var presets = VfxLightingPreset.Names.Select(VfxLightingPreset.Get).ToList();
            Assert.That(Brightness(presets[0]), Is.GreaterThan(Brightness(presets[1])));
            Assert.That(Brightness(presets[1]), Is.GreaterThan(Brightness(presets[2])));
            foreach (var preset in presets)
            {
                Assert.That(preset.vignetteIntensity, Is.InRange(0.05f, 0.3f), preset.name + ": a light vignette");
                Assert.That(preset.bloomIntensity, Is.GreaterThan(0f), preset.name);
                Assert.That(preset.keyAngles.x, Is.InRange(5f, 85f), preset.name + ": the key light comes from above the horizon");
            }
        }

        [Test]
        public void GameplayCamera_LooksDownAtAbout45Degrees_FromItsDistance()
        {
            var preset = VfxCameraPreset.Get("gameplay");
            var origin = new Vector3(2f, 0f, -1f);
            var position = preset.Position(origin);
            var target = preset.Target(origin);
            Assert.That(Vector3.Distance(position, target), Is.EqualTo(preset.distance).Within(1e-4f));
            var toTarget = (target - position).normalized;
            var pitch = Mathf.Asin(-toTarget.y) * Mathf.Rad2Deg;
            Assert.That(pitch, Is.EqualTo(45f).Within(0.01f));
            Assert.That(Vector3.Dot(preset.Rotation * Vector3.forward, toTarget), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ApplyTo_PlacesACamera_AndSetsLights()
        {
            var cameraObject = new GameObject("PresetCamera");
            var key = new GameObject("Key").AddComponent<Light>();
            var fill = new GameObject("Fill").AddComponent<Light>();
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                var cameraPreset = VfxCameraPreset.Get("side");
                cameraPreset.ApplyTo(camera, Vector3.zero);
                Assert.That(Vector3.Distance(camera.transform.position, cameraPreset.Position(Vector3.zero)), Is.LessThan(1e-4f));
                Assert.That(camera.fieldOfView, Is.EqualTo(cameraPreset.fieldOfView));

                var lighting = VfxLightingPreset.Get("night");
                lighting.ApplyTo(key, fill);
                Assert.That(key.type, Is.EqualTo(LightType.Directional));
                Assert.That(key.intensity, Is.EqualTo(lighting.keyIntensity));
                Assert.That(key.shadows, Is.EqualTo(LightShadows.Soft));
                Assert.That(fill.shadows, Is.EqualTo(LightShadows.None));
                Assert.That(fill.color, Is.EqualTo(lighting.fillColor));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(key.gameObject);
                Object.DestroyImmediate(fill.gameObject);
            }
        }

        [Test]
        public void ApplyTo_VolumeProfile_AddsBloomAcesVignetteAndExposure_Once()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            try
            {
                var preset = VfxLightingPreset.Get("day");
                preset.ApplyTo(profile);
                preset.ApplyTo(profile);
                Assert.That(profile.components.Count, Is.EqualTo(4), "applying twice overrides, it does not add twice");

                Assert.That(profile.TryGet(out Bloom bloom), Is.True);
                Assert.That(bloom.intensity.overrideState, Is.True);
                Assert.That(bloom.intensity.value, Is.EqualTo(preset.bloomIntensity));
                Assert.That(bloom.threshold.value, Is.EqualTo(preset.bloomThreshold));
                Assert.That(profile.TryGet(out Tonemapping tonemapping), Is.True);
                Assert.That(tonemapping.mode.value, Is.EqualTo(TonemappingMode.ACES));
                Assert.That(profile.TryGet(out Vignette vignette), Is.True);
                Assert.That(vignette.intensity.value, Is.EqualTo(preset.vignetteIntensity));
                Assert.That(profile.TryGet(out ColorAdjustments adjustments), Is.True);
                Assert.That(adjustments.postExposure.value, Is.EqualTo(preset.postExposure));
            }
            finally
            {
                foreach (var component in profile.components)
                    Object.DestroyImmediate(component);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
