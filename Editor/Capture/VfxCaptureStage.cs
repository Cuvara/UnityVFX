using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Cuvara.VFX.Editor.Capture
{
    /// <summary>
    /// The neutral capture stage, made from code in a scene of its own so a capture depends on no
    /// scene: a grid ground and a grid backdrop (the materials of <c>Library/Grid</c>, which the
    /// Showcase room uses too), a key and a fill light, the trilight ambient and a global Volume
    /// (Bloom, ACES tonemapping, light vignette) from a <see cref="VfxLightingPreset"/>, and a camera
    /// placed by a <see cref="VfxCameraPreset"/> that renders into an HDR render texture with
    /// post-processing on. Play mode only.
    /// </summary>
    /// <remarks>
    /// <para>Lighting and camera go through the shared code of the Showcase,
    /// <see cref="VfxLightingRig.Apply(VfxLightingPreset, Light, Light, VolumeProfile, UnityEngine.Camera)"/>
    /// and <see cref="VfxCameraRig.Place(UnityEngine.Camera, VfxCameraPreset, Vector3)"/>, so a preset
    /// lights and frames the same in both.</para>
    /// <para>Geometry: the ground is <see cref="GroundSize"/> metres square at y = 0, centred on the
    /// effect's origin. The backdrop is a wall <see cref="BackdropWidth"/> by
    /// <see cref="BackdropHeight"/> metres standing on the ground <see cref="BackdropDistance"/> metres
    /// behind the origin as seen from the camera (turned to the camera's heading). Every camera preset
    /// sees the ground up to the foot of the wall and the wall above it, and no preset sees past an
    /// edge of either (Documentation~/CAPTURE.md, "Stage").</para>
    /// <para>Cameras, lights and Volumes of the other loaded scenes are disabled while the stage exists
    /// and enabled again by <see cref="Dispose"/>, which also destroys every object the stage made.</para>
    /// </remarks>
    public sealed class VfxCaptureStage : IDisposable
    {
        /// <summary>Project path of the ground material (shader <c>Cuvara/VFX/Grid</c>).</summary>
        public const string GroundMaterialPath = "Packages/com.cuvara.vfx/Library/Grid/VfxGridGround.mat";

        /// <summary>Project path of the backdrop material (shader <c>Cuvara/VFX/Grid</c>).</summary>
        public const string BackdropMaterialPath = "Packages/com.cuvara.vfx/Library/Grid/VfxGridBackdrop.mat";

        /// <summary>
        /// Distance of the backdrop behind the effect's origin, seen from the camera, in metres. At 6 m
        /// the foot of the wall is in the top sixth of the gameplay frame, so the frame reads as a floor
        /// with a wall behind it. At the first value, 8 m, the gameplay frame showed only a 1 m strip
        /// of the wall, which read as a band (Documentation~/AUTHORING.md, Pitfalls).
        /// </summary>
        public const float BackdropDistance = 6f;

        /// <summary>Width of the backdrop, in metres.</summary>
        public const float BackdropWidth = 80f;

        /// <summary>Height of the backdrop, in metres.</summary>
        public const float BackdropHeight = 20f;

        /// <summary>Side of the square ground, in metres.</summary>
        public const float GroundSize = 80f;

        static int s_Count;

        readonly List<Behaviour> m_Disabled = new List<Behaviour>();
        readonly List<Object> m_Owned = new List<Object>();
        readonly Scene m_PreviousActive;

        /// <summary>The stage's scene; the effect is placed in it.</summary>
        public Scene Scene { get; }

        /// <summary>The capture camera (disabled other cameras do not render).</summary>
        public UnityEngine.Camera Camera { get; }

        /// <summary>The camera's target: <c>ARGBHalf</c>, linear.</summary>
        public RenderTexture Target { get; }

        /// <summary>
        /// Builds the stage. Throws when not in play mode or when a grid material of <c>Library/Grid</c>
        /// is missing.
        /// </summary>
        public VfxCaptureStage(VfxCameraPreset cameraPreset, VfxLightingPreset lightingPreset, int width, int height)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("the capture stage needs play mode");
            if (cameraPreset == null)
                throw new ArgumentNullException(nameof(cameraPreset));
            if (lightingPreset == null)
                throw new ArgumentNullException(nameof(lightingPreset));
            var groundMaterial = LoadMaterial(GroundMaterialPath);
            var backdropMaterial = LoadMaterial(BackdropMaterialPath);

            m_PreviousActive = SceneManager.GetActiveScene();
            Scene = SceneManager.CreateScene("CuvaraVfxCapture" + (++s_Count));
            DisableOtherScenes();
            SceneManager.SetActiveScene(Scene);

            var origin = Vector3.zero;
            var heading = Quaternion.Euler(0f, cameraPreset.yaw, 0f);

            // Unity's Plane is 10 m square, normal +Y.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(GroundSize / 10f, 1f, GroundSize / 10f);
            Dress(ground, groundMaterial);

            // Unity's Quad is 1 m square and faces -Z: turned to the heading, it faces the camera.
            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "Backdrop";
            var backdropPosition = origin + heading * new Vector3(0f, 0f, BackdropDistance) + new Vector3(0f, BackdropHeight * 0.5f, 0f);
            backdrop.transform.SetPositionAndRotation(backdropPosition, heading);
            backdrop.transform.localScale = new Vector3(BackdropWidth, BackdropHeight, 1f);
            Dress(backdrop, backdropMaterial);

            var key = new GameObject("Key Light").AddComponent<Light>();
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            Own(key.gameObject);
            Own(fill.gameObject);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CuvaraVfxCaptureProfile";
            m_Owned.Add(profile);
            var volumeObject = new GameObject("Post Volume");
            var volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            volume.sharedProfile = profile;
            Own(volumeObject);

            Target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = "CuvaraVfxCaptureTarget",
                antiAliasing = 1,
            };
            Target.Create();
            m_Owned.Add(Target);

            var cameraObject = new GameObject("Capture Camera");
            Camera = cameraObject.AddComponent<UnityEngine.Camera>();
            Camera.allowHDR = true;
            Camera.allowMSAA = false;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 300f;
            Camera.targetTexture = Target;
            var data = Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;
            data.volumeLayerMask = ~0;
            Own(cameraObject);

            // The shared code of the Showcase: key and fill light, the ambient of the active scene
            // (the stage's), the Volume profile and the camera background; then the camera placement.
            VfxLightingRig.Apply(lightingPreset, key, fill, profile, Camera);
            VfxCameraRig.Place(Camera, cameraPreset, origin);
        }

        static Material LoadMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                throw new InvalidOperationException($"material '{path}' not found: the capture stage needs the grid materials of Library/Grid");
            return material;
        }

        /// <summary>Moves a root object into the stage's scene and destroys it with the stage.</summary>
        public void Own(GameObject gameObject)
        {
            if (gameObject.transform.parent == null && gameObject.scene != Scene)
                SceneManager.MoveGameObjectToScene(gameObject, Scene);
            m_Owned.Add(gameObject);
        }

        // The grid material asset is shared, not copied: the stage never changes it.
        void Dress(GameObject primitive, Material material)
        {
            var collider = primitive.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);
            primitive.GetComponent<MeshRenderer>().sharedMaterial = material;
            Own(primitive);
        }

        void DisableOtherScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene == Scene)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(false))
                    {
                        if (behaviour.enabled && (behaviour is UnityEngine.Camera || behaviour is Light || behaviour is Volume))
                        {
                            behaviour.enabled = false;
                            m_Disabled.Add(behaviour);
                        }
                    }
                }
            }
        }

        /// <summary>Destroys the stage's objects, unloads its scene and enables what it disabled.</summary>
        public void Dispose()
        {
            if (Camera != null)
                Camera.targetTexture = null;
            foreach (var owned in m_Owned)
            {
                if (owned != null)
                    Object.Destroy(owned);
            }
            m_Owned.Clear();
            foreach (var behaviour in m_Disabled)
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }
            m_Disabled.Clear();
            if (m_PreviousActive.IsValid() && m_PreviousActive.isLoaded)
                SceneManager.SetActiveScene(m_PreviousActive);
            if (Scene.IsValid() && Scene.isLoaded && SceneManager.sceneCount > 1)
                SceneManager.UnloadSceneAsync(Scene);
        }
    }
}
