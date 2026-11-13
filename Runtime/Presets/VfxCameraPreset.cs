using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cuvara.VFX
{
    /// <summary>
    /// A camera preset of the capture stage and the Showcase scene: an orbit around a target point.
    /// </summary>
    /// <remarks>
    /// <c>gameplay</c> (the default) looks down at about 45 degrees from an MMO third-person distance,
    /// <c>close</c> is a low close-up, <c>side</c> looks across the effect from its right.
    /// <see cref="Get"/> returns a new copy, so a caller may change it without changing the preset.
    /// </remarks>
    [Serializable]
    public sealed class VfxCameraPreset
    {
        /// <summary>The name of the default preset.</summary>
        public const string DefaultName = "gameplay";

        /// <summary>Preset name: <c>gameplay</c>, <c>close</c> or <c>side</c>.</summary>
        public string name;

        /// <summary>Elevation of the camera above the target, in degrees (0 is level, 90 straight down).</summary>
        public float pitch;

        /// <summary>Heading of the camera's view direction, in degrees (0 looks along +Z).</summary>
        public float yaw;

        /// <summary>Distance from the target, in metres.</summary>
        public float distance;

        /// <summary>Vertical field of view, in degrees.</summary>
        public float fieldOfView;

        /// <summary>Height of the target point above the effect's origin, in metres.</summary>
        public float targetHeight;

        static readonly string[] s_Names = { "gameplay", "close", "side" };

        /// <summary>The preset names, in a fixed order: gameplay, close, side.</summary>
        public static IReadOnlyList<string> Names => s_Names;

        /// <summary>A new copy of the preset with that name, or null when there is none.</summary>
        public static VfxCameraPreset Get(string presetName)
        {
            switch (presetName)
            {
                case "gameplay":
                    return new VfxCameraPreset { name = "gameplay", pitch = 45f, yaw = 0f, distance = 10f, fieldOfView = 40f, targetHeight = 1f };
                case "close":
                    return new VfxCameraPreset { name = "close", pitch = 15f, yaw = 0f, distance = 4f, fieldOfView = 40f, targetHeight = 1f };
                case "side":
                    return new VfxCameraPreset { name = "side", pitch = 5f, yaw = -90f, distance = 7f, fieldOfView = 40f, targetHeight = 1f };
                default:
                    return null;
            }
        }

        /// <summary>The camera rotation of this preset.</summary>
        public Quaternion Rotation => Quaternion.Euler(pitch, yaw, 0f);

        /// <summary>The point the camera looks at, for an effect at <paramref name="origin"/>.</summary>
        public Vector3 Target(Vector3 origin) => origin + new Vector3(0f, targetHeight, 0f);

        /// <summary>The camera position for an effect at <paramref name="origin"/>.</summary>
        public Vector3 Position(Vector3 origin) => Target(origin) - Rotation * new Vector3(0f, 0f, distance);

        /// <summary>Places <paramref name="camera"/> (position, rotation, field of view) for an effect at <paramref name="origin"/>.</summary>
        public void ApplyTo(Camera camera, Vector3 origin)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));
            camera.transform.SetPositionAndRotation(Position(origin), Rotation);
            camera.fieldOfView = fieldOfView;
        }
    }
}
