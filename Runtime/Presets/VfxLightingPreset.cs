using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Cuvara.VFX
{
    /// <summary>
    /// A lighting preset of the capture stage and the Showcase scene: a soft directional key light, a
    /// fill light, a trilight ambient, the background colour, and the post-processing of the Volume
    /// (Bloom, ACES tonemapping, a light vignette, post exposure).
    /// </summary>
    /// <remarks>
    /// <para>The presets are plain data so the <c>capture</c> verb and the Showcase use the same values:
    /// <see cref="Get"/> returns a new copy of <c>day</c>, <c>dusk</c> (the default) or <c>night</c>, which
    /// a caller may change without changing the preset.</para>
    /// <para>Colours are linear. Light intensities are URP directional light intensities.</para>
    /// </remarks>
    [Serializable]
    public sealed class VfxLightingPreset
    {
        /// <summary>The name of the default preset.</summary>
        public const string DefaultName = "dusk";

        /// <summary>Preset name: <c>day</c>, <c>dusk</c> or <c>night</c>.</summary>
        public string name;

        /// <summary>Colour of the key light.</summary>
        public Color keyColor;

        /// <summary>Intensity of the key light.</summary>
        public float keyIntensity;

        /// <summary>Key light direction: x is the elevation in degrees, y the heading in degrees.</summary>
        public Vector2 keyAngles;

        /// <summary>Shadow strength of the key light (soft shadows).</summary>
        public float keyShadowStrength;

        /// <summary>Colour of the fill light (no shadows).</summary>
        public Color fillColor;

        /// <summary>Intensity of the fill light.</summary>
        public float fillIntensity;

        /// <summary>Fill light direction: x is the elevation in degrees, y the heading in degrees.</summary>
        public Vector2 fillAngles;

        /// <summary>Trilight ambient: sky colour.</summary>
        public Color ambientSky;

        /// <summary>Trilight ambient: equator colour.</summary>
        public Color ambientEquator;

        /// <summary>Trilight ambient: ground colour.</summary>
        public Color ambientGround;

        /// <summary>The camera's clear colour.</summary>
        public Color background;

        /// <summary>Bloom intensity.</summary>
        public float bloomIntensity;

        /// <summary>Bloom threshold (linear, in gamma space as URP reads it).</summary>
        public float bloomThreshold;

        /// <summary>Bloom scatter.</summary>
        public float bloomScatter;

        /// <summary>Vignette intensity (light: below 0.3).</summary>
        public float vignetteIntensity;

        /// <summary>Post exposure in EV.</summary>
        public float postExposure;

        static readonly string[] s_Names = { "day", "dusk", "night" };

        /// <summary>The preset names, in a fixed order: day, dusk, night.</summary>
        public static IReadOnlyList<string> Names => s_Names;

        /// <summary>A new copy of the preset with that name, or null when there is none.</summary>
        public static VfxLightingPreset Get(string presetName)
        {
            switch (presetName)
            {
                case "day":
                    return new VfxLightingPreset
                    {
                        name = "day",
                        keyColor = new Color(1.00f, 0.96f, 0.90f), keyIntensity = 2.0f, keyAngles = new Vector2(50f, -30f), keyShadowStrength = 0.8f,
                        fillColor = new Color(0.75f, 0.85f, 1.00f), fillIntensity = 0.45f, fillAngles = new Vector2(20f, 150f),
                        ambientSky = new Color(0.55f, 0.62f, 0.72f), ambientEquator = new Color(0.42f, 0.42f, 0.40f), ambientGround = new Color(0.22f, 0.20f, 0.18f),
                        background = new Color(0.52f, 0.58f, 0.66f),
                        bloomIntensity = 0.6f, bloomThreshold = 1.0f, bloomScatter = 0.6f, vignetteIntensity = 0.18f, postExposure = 1.0f,
                    };
                case "dusk":
                    return new VfxLightingPreset
                    {
                        name = "dusk",
                        keyColor = new Color(1.00f, 0.62f, 0.38f), keyIntensity = 1.6f, keyAngles = new Vector2(20f, -60f), keyShadowStrength = 0.7f,
                        fillColor = new Color(0.45f, 0.55f, 0.85f), fillIntensity = 0.35f, fillAngles = new Vector2(25f, 120f),
                        ambientSky = new Color(0.30f, 0.32f, 0.45f), ambientEquator = new Color(0.28f, 0.22f, 0.22f), ambientGround = new Color(0.10f, 0.09f, 0.09f),
                        background = new Color(0.16f, 0.15f, 0.19f),
                        bloomIntensity = 0.8f, bloomThreshold = 0.9f, bloomScatter = 0.65f, vignetteIntensity = 0.22f, postExposure = 1.0f,
                    };
                case "night":
                    return new VfxLightingPreset
                    {
                        name = "night",
                        keyColor = new Color(0.55f, 0.65f, 1.00f), keyIntensity = 0.5f, keyAngles = new Vector2(40f, 30f), keyShadowStrength = 0.6f,
                        fillColor = new Color(0.30f, 0.35f, 0.50f), fillIntensity = 0.10f, fillAngles = new Vector2(20f, -150f),
                        ambientSky = new Color(0.040f, 0.050f, 0.090f), ambientEquator = new Color(0.030f, 0.035f, 0.050f), ambientGround = new Color(0.015f, 0.015f, 0.020f),
                        background = new Color(0.020f, 0.025f, 0.040f),
                        bloomIntensity = 1.0f, bloomThreshold = 0.8f, bloomScatter = 0.7f, vignetteIntensity = 0.25f, postExposure = 0.75f,
                    };
                default:
                    return null;
            }
        }

        /// <summary>The rotation of a directional light with the given angles (elevation, heading).</summary>
        public static Quaternion Rotation(Vector2 angles) => Quaternion.Euler(angles.x, angles.y, 0f);

        /// <summary>Sets colour, intensity, rotation and shadows of a key and a fill light (either may be null).</summary>
        public void ApplyTo(Light key, Light fill)
        {
            if (key != null)
            {
                key.type = LightType.Directional;
                key.color = keyColor;
                key.intensity = keyIntensity;
                key.transform.rotation = Rotation(keyAngles);
                key.shadows = LightShadows.Soft;
                key.shadowStrength = keyShadowStrength;
            }
            if (fill != null)
            {
                fill.type = LightType.Directional;
                fill.color = fillColor;
                fill.intensity = fillIntensity;
                fill.transform.rotation = Rotation(fillAngles);
                fill.shadows = LightShadows.None;
            }
        }

        /// <summary>Sets the trilight ambient of the active scene's <see cref="RenderSettings"/>, no skybox, no fog.</summary>
        public void ApplyAmbient()
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.ambientIntensity = 1f;
            // URP lights with the ambient probe, not with the colours: rebuild the probe from them.
            DynamicGI.UpdateEnvironment();
        }

        /// <summary>
        /// Adds (or overrides) Bloom, Tonemapping (ACES), Vignette and Color Adjustments (post exposure)
        /// in <paramref name="profile"/> with the values of this preset.
        /// </summary>
        public void ApplyTo(VolumeProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var bloom = Component<Bloom>(profile);
            bloom.intensity.Override(bloomIntensity);
            bloom.threshold.Override(bloomThreshold);
            bloom.scatter.Override(bloomScatter);

            var tonemapping = Component<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);

            var vignette = Component<Vignette>(profile);
            vignette.intensity.Override(vignetteIntensity);
            vignette.smoothness.Override(0.4f);

            var colorAdjustments = Component<ColorAdjustments>(profile);
            colorAdjustments.postExposure.Override(postExposure);
        }

        static T Component<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
                component = profile.Add<T>();
            component.active = true;
            return component;
        }
    }
}
