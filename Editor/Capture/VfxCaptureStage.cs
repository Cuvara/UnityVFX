using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Cuvara.VFX.Editor.Capture
{
    /// <summary>
    /// The neutral capture stage, made from code in a scene of its own so a capture depends on no
    /// scene: a grey ground and backdrop, a key and a fill light, the trilight ambient and a global
    /// Volume (Bloom, ACES tonemapping, light vignette) from a <see cref="VfxLightingPreset"/>, and a
    /// camera placed by a <see cref="VfxCameraPreset"/> that renders into an HDR render texture with
    /// post-processing on. Play mode only.
    /// </summary>
    /// <remarks>
    /// Cameras, lights and Volumes of the other loaded scenes are disabled while the stage exists and
    /// enabled again by <see cref="Dispose"/>, which also destroys every object the stage made.
    /// </remarks>
    public sealed class VfxCaptureStage : IDisposable
    {
        /// <summary>Linear albedo of the ground.</summary>
        public static readonly Color GroundAlbedo = new Color(0.30f, 0.30f, 0.30f);

        /// <summary>Linear albedo of the backdrop.</summary>
        public static readonly Color BackdropAlbedo = new Color(0.36f, 0.36f, 0.36f);

        /// <summary>Distance of the backdrop behind the effect, seen from the camera, in metres.</summary>
        public const float BackdropDistance = 8f;

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

        /// <summary>Builds the stage. Throws when not in play mode or when URP's Lit shader is missing.</summary>
        public VfxCaptureStage(VfxCameraPreset cameraPreset, VfxLightingPreset lightingPreset, int width, int height)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("the capture stage needs play mode");
            if (cameraPreset == null)
                throw new ArgumentNullException(nameof(cameraPreset));
            if (lightingPreset == null)
                throw new ArgumentNullException(nameof(lightingPreset));
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
                throw new InvalidOperationException("shader 'Universal Render Pipeline/Lit' not found: the capture stage needs URP");

            m_PreviousActive = SceneManager.GetActiveScene();
            Scene = SceneManager.CreateScene("CuvaraVfxCapture" + (++s_Count));
            DisableOtherScenes();
            SceneManager.SetActiveScene(Scene);
            lightingPreset.ApplyAmbient();

            var origin = Vector3.zero;
            var heading = Quaternion.Euler(0f, cameraPreset.yaw, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            Dress(ground, lit, GroundAlbedo);

            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "Backdrop";
            backdrop.transform.SetPositionAndRotation(origin + heading * new Vector3(0f, 0f, BackdropDistance) + new Vector3(0f, 7f, 0f), heading);
            backdrop.transform.localScale = new Vector3(40f, 14f, 1f);
            Dress(backdrop, lit, BackdropAlbedo);

            var key = new GameObject("Key Light").AddComponent<Light>();
            var fill = new GameObject("Fill Light").AddComponent<Light>();
            lightingPreset.ApplyTo(key, fill);
            Own(key.gameObject);
            Own(fill.gameObject);

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CuvaraVfxCaptureProfile";
            lightingPreset.ApplyTo(profile);
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
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = lightingPreset.background;
            Camera.nearClipPlane = 0.1f;
            Camera.farClipPlane = 300f;
            Camera.targetTexture = Target;
            cameraPreset.ApplyTo(Camera, origin);
            var data = Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;
            data.volumeLayerMask = ~0;
            Own(cameraObject);
        }

        /// <summary>Moves a root object into the stage's scene and destroys it with the stage.</summary>
        public void Own(GameObject gameObject)
        {
            if (gameObject.transform.parent == null && gameObject.scene != Scene)
                SceneManager.MoveGameObjectToScene(gameObject, Scene);
            m_Owned.Add(gameObject);
        }

        void Dress(GameObject primitive, Shader lit, Color albedo)
        {
            var collider = primitive.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);
            var material = new Material(lit) { name = primitive.name };
            material.SetColor("_BaseColor", albedo);
            material.SetFloat("_Smoothness", 0.15f);
            primitive.GetComponent<MeshRenderer>().sharedMaterial = material;
            m_Owned.Add(material);
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
