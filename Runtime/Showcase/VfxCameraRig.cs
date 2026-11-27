using System;
using UnityEngine;

namespace Cuvara.VFX
{
    /// <summary>
    /// Places a camera from a <see cref="VfxCameraPreset"/> around a focus point. The Showcase overlay
    /// and the capture stage both place their camera through
    /// <see cref="Place(Camera, VfxCameraPreset, Vector3)"/>, so a preset frames the same in both by
    /// construction.
    /// </summary>
    /// <remarks>
    /// As a component it holds the camera and the focus (a transform plus an offset, or the rig's own
    /// position) and applies a preset by name (<see cref="ApplyPreset(string)"/>); <see cref="Preset"/>
    /// is applied on <c>Start</c>. The focus is the effect's origin, the point the preset's
    /// <see cref="VfxCameraPreset.targetHeight"/> is measured from: for a station on the ground, its
    /// ground point.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cuvara/VFX/VFX Camera Rig")]
    public sealed class VfxCameraRig : MonoBehaviour
    {
        [SerializeField, Tooltip("Camera to place; empty: the camera on this object, else Camera.main.")]
        Camera targetCamera;

        [SerializeField, Tooltip("Focus point (the effect's origin on the ground); empty: this transform.")]
        Transform focus;

        [SerializeField, Tooltip("World-space offset added to the focus position.")]
        Vector3 focusOffset;

        [SerializeField, Tooltip("Preset applied on Start: gameplay, close or side.")]
        string preset = VfxCameraPreset.DefaultName;

        /// <summary>The camera placed; when null, the camera on this object or <see cref="Camera.main"/> at the next apply.</summary>
        public Camera TargetCamera
        {
            get => targetCamera;
            set => targetCamera = value;
        }

        /// <summary>The focus transform; null means this transform.</summary>
        public Transform Focus
        {
            get => focus;
            set => focus = value;
        }

        /// <summary>World-space offset added to the focus position.</summary>
        public Vector3 FocusOffset
        {
            get => focusOffset;
            set => focusOffset = value;
        }

        /// <summary>Name of the preset applied last (or on <c>Start</c>).</summary>
        public string Preset => preset;

        /// <summary>The focus point in world space: the focus transform's position (or this one's) plus the offset.</summary>
        public Vector3 FocusPoint => (focus != null ? focus.position : transform.position) + focusOffset;

        /// <summary>Applies the preset named <paramref name="presetName"/> (gameplay, close or side).</summary>
        /// <returns>False (with a logged error) when there is no preset of that name or no camera.</returns>
        public bool ApplyPreset(string presetName)
        {
            VfxCameraPreset cameraPreset = VfxCameraPreset.Get(presetName);
            if (cameraPreset == null)
            {
                Debug.LogError($"VfxCameraRig: unknown camera preset '{presetName}'; valid: {string.Join(", ", VfxCameraPreset.Names)}", this);
                return false;
            }
            return ApplyPreset(cameraPreset);
        }

        /// <summary>Places the rig's camera with <paramref name="cameraPreset"/> around <see cref="FocusPoint"/>.</summary>
        /// <returns>False (with a logged error) when there is no camera.</returns>
        public bool ApplyPreset(VfxCameraPreset cameraPreset)
        {
            if (cameraPreset == null)
                throw new ArgumentNullException(nameof(cameraPreset));
            if (targetCamera == null) targetCamera = GetComponent<Camera>();
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null)
            {
                Debug.LogError("VfxCameraRig: no camera to place", this);
                return false;
            }
            Place(targetCamera, cameraPreset, FocusPoint);
            preset = cameraPreset.name;
            return true;
        }

        /// <summary>
        /// The shared implementation: puts <paramref name="camera"/> at
        /// <see cref="VfxCameraPreset.Position(Vector3)"/> with <see cref="VfxCameraPreset.Rotation"/> and
        /// the preset's field of view, for an effect at <paramref name="focus"/>.
        /// </summary>
        public static void Place(Camera camera, VfxCameraPreset cameraPreset, Vector3 focus)
        {
            if (cameraPreset == null)
                throw new ArgumentNullException(nameof(cameraPreset));
            cameraPreset.ApplyTo(camera, focus);
        }

        void Start()
        {
            if (!string.IsNullOrEmpty(preset)) ApplyPreset(preset);
        }
    }
}
