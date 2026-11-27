// Agent-owned Sandbox project setup for com.cuvara.vfx.
//
// Creates and checks the project settings the package needs (URP with HDR, Forward+, depth and
// opaque textures, linear colour space, the Input System as the active input handling, VFX Graph
// runtime resources) through editor APIs, never by writing YAML. Both entry points are idempotent
// and return JSON, so an agent can call them through `unity-mcp-cli run-tool script-execute`:
//
//   Cuvara.VFX.Sandbox.Editor.SandboxSetup.Apply()   // creates or repairs, then returns the state
//   Cuvara.VFX.Sandbox.Editor.SandboxSetup.Report()  // returns the state, changes nothing
//
// See Documentation~/AUTHORING.md, section "Sandbox".

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Cuvara.VFX.Sandbox.Editor
{
    public static class SandboxSetup
    {
        public const string SettingsFolder = "Assets/Settings";
        public const string RendererPath = SettingsFolder + "/Sandbox_Renderer.asset";
        public const string PipelinePath = SettingsFolder + "/Sandbox_URP.asset";

        // URP's own default location; URP creates the asset here when none is registered.
        public const string GlobalSettingsPath = "Assets/UniversalRenderPipelineGlobalSettings.asset";

        const string UrpPackage = "Packages/com.unity.render-pipelines.universal";
        const string PostProcessDataPath = UrpPackage + "/Runtime/Data/PostProcessData.asset";
        const string VfxRuntimeResourcesPath = "Packages/com.unity.visualeffectgraph/Runtime/Utilities/RuntimeResources.asset";
        const string VfxEditorResourcesPath = "Packages/com.unity.visualeffectgraph/Editor/Utilities/EditorResources.asset";

        // PlayerSettings.activeInputHandler: 0 Input Manager (old), 1 Input System package, 2 both.
        const int InputSystemOnly = 1;

        [Serializable]
        public class PackageVersion
        {
            public string name;
            public string version;
            public string source;
            public string resolved_path;
        }

        [Serializable]
        public class State
        {
            public bool ok;
            public List<string> changes = new List<string>();
            public List<string> problems = new List<string>();
            public string unity;
            public string project;
            public string render_pipeline;
            public string render_pipeline_path;
            public string graphics_default_pipeline;
            public int quality_levels;
            public int quality_levels_using_pipeline;
            public bool hdr;
            public bool depth_texture;
            public bool opaque_texture;
            public string rendering_path;
            public string color_space;
            public int active_input_handler;
            public string global_settings_path;
            public int global_settings_assets;
            public bool vfx_runtime_resources;
            public bool vfx_editor_resources;
            public List<PackageVersion> packages = new List<PackageVersion>();
        }

        [MenuItem("Tools/Cuvara VFX Sandbox/Apply Settings")]
        static void ApplyMenu() => Debug.Log(Apply());

        [MenuItem("Tools/Cuvara VFX Sandbox/Report Settings")]
        static void ReportMenu() => Debug.Log(Report());

        /// <summary>Creates or repairs every setting, saves the assets and returns the state as JSON.</summary>
        public static string Apply()
        {
            var changes = new List<string>();

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
                changes.Add("created " + SettingsFolder);
            }

            var renderer = EnsureRenderer(changes);
            var pipeline = EnsurePipeline(renderer, changes);
            EnsureGraphicsAndQuality(pipeline, changes);
            EnsureGlobalSettings(changes);

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                changes.Add("color space set to Linear");
            }

            EnsureInputHandler(changes);
            EnsureVfxResources(changes);

            AssetDatabase.SaveAssets();
            return JsonUtility.ToJson(Collect(changes), true);
        }

        /// <summary>Returns the current state as JSON; changes nothing.</summary>
        public static string Report() => JsonUtility.ToJson(Collect(new List<string>()), true);

        static UniversalRendererData EnsureRenderer(List<string> changes)
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                // Same steps as UniversalRenderPipelineAsset.CreateRendererAsset (internal in URP 17.6).
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
                AssetDatabase.CreateAsset(renderer, RendererPath);
                ResourceReloader.TryReloadAllNullIn(renderer, UrpPackage);
                changes.Add("created " + RendererPath);
            }

            if (renderer.postProcessData == null)
            {
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
                EditorUtility.SetDirty(renderer);
                changes.Add("renderer post-process data assigned");
            }

            if (renderer.renderingMode != RenderingMode.ForwardPlus)
            {
                renderer.renderingMode = RenderingMode.ForwardPlus;
                EditorUtility.SetDirty(renderer);
                changes.Add("rendering path set to Forward+");
            }

            return renderer;
        }

        static UniversalRenderPipelineAsset EnsurePipeline(UniversalRendererData renderer, List<string> changes)
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
                changes.Add("created " + PipelinePath);
            }

            var list = pipeline.rendererDataList;
            if (list.Length == 0 || list[0] != renderer)
            {
                var serialized = new SerializedObject(pipeline);
                var property = serialized.FindProperty("m_RendererDataList");
                if (property.arraySize == 0)
                    property.arraySize = 1;
                property.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                changes.Add("pipeline renderer 0 set to " + RendererPath);
            }

            if (!pipeline.supportsHDR)
            {
                pipeline.supportsHDR = true;
                changes.Add("HDR enabled");
            }

            if (!pipeline.supportsCameraDepthTexture)
            {
                pipeline.supportsCameraDepthTexture = true;
                changes.Add("depth texture enabled");
            }

            if (!pipeline.supportsCameraOpaqueTexture)
            {
                pipeline.supportsCameraOpaqueTexture = true;
                changes.Add("opaque texture enabled");
            }

            EditorUtility.SetDirty(pipeline);
            return pipeline;
        }

        static void EnsureGraphicsAndQuality(UniversalRenderPipelineAsset pipeline, List<string> changes)
        {
            if (GraphicsSettings.defaultRenderPipeline != pipeline)
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                changes.Add("Graphics default render pipeline set");
            }

            var current = QualitySettings.GetQualityLevel();
            var levels = QualitySettings.names.Length;
            for (var i = 0; i < levels; i++)
            {
                if (QualitySettings.GetRenderPipelineAssetAt(i) == pipeline)
                    continue;
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
                changes.Add("Quality level " + QualitySettings.names[i] + " render pipeline set");
            }

            if (QualitySettings.GetQualityLevel() != current)
                QualitySettings.SetQualityLevel(current, false);
        }

        static void EnsureGlobalSettings(List<string> changes)
        {
            var registered = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>() as RenderPipelineGlobalSettings;
            var atDefault = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(GlobalSettingsPath);

            // URP creates and registers the asset itself once its pipeline is active (EnsureGlobalSettings);
            // its Ensure method is internal, so nothing is created here.
            var keep = atDefault != null ? atDefault : registered;
            if (keep == null)
                return;

            if (registered != keep)
            {
                EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<UniversalRenderPipeline>(keep);
                changes.Add("URP global settings registered: " + AssetDatabase.GetAssetPath(keep));
            }

            // A failed first import can leave numbered copies ("... 1.asset"); only the registered one is used.
            foreach (var path in FindGlobalSettingsPaths())
            {
                if (path == AssetDatabase.GetAssetPath(keep))
                    continue;
                if (AssetDatabase.DeleteAsset(path))
                    changes.Add("deleted unused URP global settings " + path);
            }
        }

        static void EnsureInputHandler(List<string> changes)
        {
            var playerSettings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var serialized = new SerializedObject(playerSettings);
            var property = serialized.FindProperty("activeInputHandler");
            if (property != null && property.intValue != InputSystemOnly)
            {
                property.intValue = InputSystemOnly;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                changes.Add("active input handling set to Input System package (takes effect after an editor restart)");
            }
        }

        static void EnsureVfxResources(List<string> changes)
        {
            // Same assignment as VFXManagerEditor.CheckVFXManager in com.unity.visualeffectgraph 17.6.
            var manager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/VFXManager.asset").FirstOrDefault();
            if (manager == null)
                return;

            var serialized = new SerializedObject(manager);
            var modified = AssignIfNull(serialized, "m_RuntimeResources", VfxRuntimeResourcesPath, changes);
            modified |= AssignIfNull(serialized, "m_EditorResources", VfxEditorResourcesPath, changes);
            if (modified)
                serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool AssignIfNull(SerializedObject serialized, string propertyName, string assetPath, List<string> changes)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue != null)
                return false;
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (asset == null)
                return false;
            property.objectReferenceValue = asset;
            changes.Add("VFXManager " + propertyName + " assigned");
            return true;
        }

        static IEnumerable<string> FindGlobalSettingsPaths() =>
            AssetDatabase.FindAssets("t:UniversalRenderPipelineGlobalSettings", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct();

        static State Collect(List<string> changes)
        {
            var state = new State { changes = changes, unity = Application.unityVersion, project = Application.dataPath };

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            var active = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var defaultPipeline = GraphicsSettings.defaultRenderPipeline;

            state.render_pipeline = active != null ? active.name : null;
            state.render_pipeline_path = active != null ? AssetDatabase.GetAssetPath(active) : null;
            state.graphics_default_pipeline = defaultPipeline != null ? AssetDatabase.GetAssetPath(defaultPipeline) : null;
            state.quality_levels = QualitySettings.names.Length;
            for (var i = 0; i < state.quality_levels; i++)
            {
                if (pipeline != null && QualitySettings.GetRenderPipelineAssetAt(i) == pipeline)
                    state.quality_levels_using_pipeline++;
            }

            if (active != null)
            {
                state.hdr = active.supportsHDR;
                state.depth_texture = active.supportsCameraDepthTexture;
                state.opaque_texture = active.supportsCameraOpaqueTexture;
                var list = active.rendererDataList;
                var renderer = list.Length > 0 ? list[0] as UniversalRendererData : null;
                state.rendering_path = renderer != null ? renderer.renderingMode.ToString() : null;
            }

            state.color_space = PlayerSettings.colorSpace.ToString();
            var playerSettings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var input = playerSettings.FindProperty("activeInputHandler");
            state.active_input_handler = input != null ? input.intValue : -1;

            var registered = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
            state.global_settings_path = registered != null ? AssetDatabase.GetAssetPath(registered) : null;
            state.global_settings_assets = FindGlobalSettingsPaths().Count();

            var manager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/VFXManager.asset").FirstOrDefault();
            if (manager != null)
            {
                var serialized = new SerializedObject(manager);
                state.vfx_runtime_resources = serialized.FindProperty("m_RuntimeResources")?.objectReferenceValue != null;
                state.vfx_editor_resources = serialized.FindProperty("m_EditorResources")?.objectReferenceValue != null;
            }

            var wanted = new[]
            {
                "com.cuvara.vfx", "com.unity.render-pipelines.universal", "com.unity.visualeffectgraph",
                "com.ivanmurzak.unity.mcp", "com.unity.inputsystem", "com.unity.test-framework",
            };
            var registeredPackages = PackageInfo.GetAllRegisteredPackages();
            foreach (var name in wanted)
            {
                var info = registeredPackages.FirstOrDefault(p => p.name == name);
                state.packages.Add(new PackageVersion
                {
                    name = name,
                    version = info != null ? info.version : null,
                    source = info != null ? info.source.ToString() : null,
                    resolved_path = info != null ? info.resolvedPath : null,
                });
            }

            Check(state, active != null && state.render_pipeline_path == PipelinePath, "active render pipeline is not " + PipelinePath);
            Check(state, state.graphics_default_pipeline == PipelinePath, "Graphics default render pipeline is not " + PipelinePath);
            Check(state, state.quality_levels_using_pipeline == state.quality_levels, "a quality level does not use " + PipelinePath);
            Check(state, state.hdr, "HDR is off");
            Check(state, state.depth_texture, "depth texture is off");
            Check(state, state.opaque_texture, "opaque texture is off");
            Check(state, state.rendering_path == nameof(RenderingMode.ForwardPlus), "rendering path is not Forward+");
            Check(state, state.color_space == nameof(ColorSpace.Linear), "colour space is not Linear");
            Check(state, state.active_input_handler == InputSystemOnly, "active input handling is not the Input System package");
            Check(state, state.global_settings_path == GlobalSettingsPath, "URP global settings are not registered at " + GlobalSettingsPath);
            Check(state, state.global_settings_assets == 1, "expected one URP global settings asset, found " + state.global_settings_assets);
            Check(state, state.vfx_runtime_resources && state.vfx_editor_resources, "VFXManager resources are not assigned");
            foreach (var package in state.packages)
                Check(state, package.version != null, "package not resolved: " + package.name);
            // The package is referenced as file:../../package: <sandbox home>/package is a link
            // to the repository root created by Tools~/setup-sandbox.py (Documentation~/AUTHORING.md, "Sandbox").
            Check(state, state.packages[0].source == "Local", "com.cuvara.vfx is not the local package file:../../package (run Tools~/setup-sandbox.py)");

            state.ok = state.problems.Count == 0;
            return state;
        }

        static void Check(State state, bool condition, string problem)
        {
            if (!condition)
                state.problems.Add(problem);
        }
    }
}
