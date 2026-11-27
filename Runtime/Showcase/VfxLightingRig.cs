using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cuvara.VFX
{
    /// <summary>
    /// Applies a <see cref="VfxLightingPreset"/> to a lighting rig: a key light, a fill light, a
    /// Volume, the ambient of the active scene and the camera background. The Showcase overlay and
    /// the capture stage both apply presets through <see cref="Apply(VfxLightingPreset, Light, Light, VolumeProfile, Camera)"/>,
    /// so a preset looks the same in both by construction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// As a component it holds the rig's references and applies a preset by name
    /// (<see cref="ApplyPreset(string)"/>); <see cref="Preset"/> is applied on <c>Start</c>. The
    /// component never writes into a profile asset: on the first apply it gives the Volume a profile
    /// of its own (made at run time, not saved) and destroys it with the component, so a scene's
    /// profile asset stays unchanged.
    /// </para>
    /// <para>
    /// The static <see cref="Apply(VfxLightingPreset, Light, Light, VolumeProfile, Camera)"/> is the
    /// shared implementation for a caller that owns its objects, such as the capture stage.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cuvara/VFX/VFX Lighting Rig")]
    public sealed class VfxLightingRig : MonoBehaviour
    {
        [SerializeField, Tooltip("Directional key light (soft shadows).")]
        Light keyLight;

        [SerializeField, Tooltip("Directional fill light (no shadows).")]
        Light fillLight;

        [SerializeField, Tooltip("Global Volume that gets Bloom, ACES tonemapping, vignette and post exposure.")]
        Volume volume;

        [SerializeField, Tooltip("Camera whose background colour the preset sets (solid colour clear).")]
        Camera targetCamera;

        [SerializeField, Tooltip("Preset applied on Start: day, dusk or night.")]
        string preset = VfxLightingPreset.DefaultName;

        VolumeProfile runtimeProfile;

        /// <summary>The key light.</summary>
        public Light KeyLight
        {
            get => keyLight;
            set => keyLight = value;
        }

        /// <summary>The fill light.</summary>
        public Light FillLight
        {
            get => fillLight;
            set => fillLight = value;
        }

        /// <summary>The Volume that receives the post-processing of the preset.</summary>
        public Volume Volume
        {
            get => volume;
            set => volume = value;
        }

        /// <summary>The camera whose background the preset sets.</summary>
        public Camera TargetCamera
        {
            get => targetCamera;
            set => targetCamera = value;
        }

        /// <summary>Name of the preset applied last (or on <c>Start</c>).</summary>
        public string Preset => preset;

        /// <summary>The profile the rig made for its Volume, or null before the first apply with a Volume.</summary>
        public VolumeProfile RuntimeProfile => runtimeProfile;

        /// <summary>Applies the preset named <paramref name="presetName"/> (day, dusk or night).</summary>
        /// <returns>False (with a logged error) when there is no preset of that name.</returns>
        public bool ApplyPreset(string presetName)
        {
            VfxLightingPreset lighting = VfxLightingPreset.Get(presetName);
            if (lighting == null)
            {
                Debug.LogError($"VfxLightingRig: unknown lighting preset '{presetName}'; valid: {string.Join(", ", VfxLightingPreset.Names)}", this);
                return false;
            }
            ApplyPreset(lighting);
            return true;
        }

        /// <summary>Applies <paramref name="lighting"/> to the rig's lights, Volume, the scene ambient and the camera.</summary>
        public void ApplyPreset(VfxLightingPreset lighting)
        {
            if (lighting == null)
                throw new ArgumentNullException(nameof(lighting));
            VolumeProfile profile = null;
            if (volume != null)
            {
                if (runtimeProfile == null)
                {
                    runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    runtimeProfile.name = "VfxLightingRig Profile";
                    runtimeProfile.hideFlags = HideFlags.DontSave;
                }
                if (volume.sharedProfile != runtimeProfile) volume.sharedProfile = runtimeProfile;
                profile = runtimeProfile;
            }
            Apply(lighting, keyLight, fillLight, profile, targetCamera);
            preset = lighting.name;
        }

        /// <summary>
        /// The shared implementation: sets the key and fill light (<see cref="VfxLightingPreset.ApplyTo(Light, Light)"/>),
        /// the trilight ambient of the active scene (<see cref="VfxLightingPreset.ApplyAmbient"/>), Bloom,
        /// ACES tonemapping, vignette and post exposure in <paramref name="profile"/>
        /// (<see cref="VfxLightingPreset.ApplyTo(VolumeProfile)"/>) and the solid-colour background of
        /// <paramref name="camera"/>. Any of the objects may be null; it is then skipped.
        /// </summary>
        public static void Apply(VfxLightingPreset lighting, Light key, Light fill, VolumeProfile profile, Camera camera)
        {
            if (lighting == null)
                throw new ArgumentNullException(nameof(lighting));
            lighting.ApplyTo(key, fill);
            lighting.ApplyAmbient();
            if (profile != null) lighting.ApplyTo(profile);
            ApplyBackground(lighting, camera);
        }

        /// <summary>Clears <paramref name="camera"/> with the preset's background colour (no skybox).</summary>
        public static void ApplyBackground(VfxLightingPreset lighting, Camera camera)
        {
            if (lighting == null)
                throw new ArgumentNullException(nameof(lighting));
            if (camera == null) return;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = lighting.background;
        }

        void Start()
        {
            if (!string.IsNullOrEmpty(preset)) ApplyPreset(preset);
        }

        void OnDestroy()
        {
            if (runtimeProfile == null) return;
            if (volume != null && volume.sharedProfile == runtimeProfile) volume.sharedProfile = null;
            for (int i = 0; i < runtimeProfile.components.Count; i++)
                DestroyObject(runtimeProfile.components[i]);
            DestroyObject(runtimeProfile);
            runtimeProfile = null;
        }

        static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
