using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.VFX;
using UnityEngine;

namespace Cuvara.VFX.Editor.Bridge
{
    public static partial class VfxGraphBridge
    {
        /// <summary>
        /// The facts of a graph that <c>inspect</c> needs and <see cref="Describe"/> does not print:
        /// the name VFX Graph gives each system, and per output context its generated shader, its
        /// Shader Graph, the settings it shows and its effective render features.
        /// </summary>
        /// <param name="assetPath">Project-relative path of a <c>.vfx</c> asset.</param>
        /// <param name="warnings">Receives one line for each fact that could not be read.</param>
        /// <returns>
        /// <c>system_names</c>: a map from the describe system id (<c>s0</c>) to the unique system name
        /// (<c>System</c>, <c>System (1)</c>, or the title the author gave). <c>outputs</c>: one map per
        /// context of type <c>Output</c>, in describe order, with <c>id</c> (the describe id),
        /// <c>expected_shader</c> (the shader name VFX Graph generates for it; null for a static mesh
        /// output), <c>shaders</c> (name, path, GUID of each generated <c>Shader</c> sub-asset with that
        /// name, or of the shader a static mesh output renders with), <c>shader_graph</c> (an asset
        /// reference, or null), <c>visible_settings</c> (the setting names the inspector or the graph
        /// shows in this configuration) and <c>effective</c> (render features after VFX Graph's own
        /// rules: <c>sorted</c>, <c>indirect_draw</c>, <c>shadow_casting</c>, <c>soft_particles</c>,
        /// <c>motion_vector</c>, <c>alpha_clipping</c>, <c>flipbook</c>, <c>strips</c>, <c>ray_traced</c>;
        /// null where the output type has no such feature).
        /// </returns>
        /// <remarks>
        /// The shader name follows <c>VFXGraphCompiledData.SaveShaderFiles</c> in VFX Graph 17.6:
        /// <c>Hidden/VFX/&lt;asset name&gt;/&lt;system name&gt;/[&lt;letter&gt;/]&lt;context label or name&gt;</c>, with
        /// <c>|</c> and line breaks of the name replaced by spaces. The system names come from
        /// <c>VFXGraph.systemNames</c> and the letters from <c>VFXGraph.SyncContextLetters</c>, both
        /// synchronised first as <c>VFXGraph.PrepareGraph</c> does before a compile (the letters are not
        /// serialised, so a graph loaded from disk has none). Nothing is written to the asset.
        /// </remarks>
        public static Map InspectOutputs(string assetPath, List<string> warnings)
        {
            RequireSupportedVersion();
            warnings ??= new List<string>();
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset is empty");

            var resource = VisualEffectResource.GetResourceAtPath(assetPath);
            if (resource == null)
                throw new ArgumentException($"no VFX Graph resource at '{assetPath}'");
            var graph = resource.GetGraph();
            if (graph == null)
                throw new ArgumentException($"the resource at '{assetPath}' has no graph");

            var ids = AssignIds(graph, out var contexts, out _, out _, out _);
            var systemIds = SystemIds(contexts);

            var uniqueNames = new Dictionary<VFXData, string>();
            try
            {
                graph.systemNames.Sync(graph);
                // Context letters (A, B for several outputs of one system) are not serialised: a graph
                // loaded from disk has none until PrepareGraph assigns them.
                graph.SyncContextLetters();
                foreach (var data in systemIds.Keys)
                    uniqueNames[data] = graph.systemNames.GetUniqueSystemName(data);
            }
            catch (Exception exception)
            {
                warnings.Add($"system names or context letters not readable: {exception.GetType().Name}: {exception.Message}");
            }

            var systemNames = new Map();
            foreach (var pair in systemIds.OrderBy(p => int.Parse(p.Value.Substring(1), System.Globalization.CultureInfo.InvariantCulture)))
                systemNames.Add(pair.Value, uniqueNames.TryGetValue(pair.Key, out var unique) ? unique : null);

            var shaders = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Shader>().Where(s => s != null).ToList();
            var assetName = Path.GetFileNameWithoutExtension(assetPath);
            var guid = AssetDatabase.AssetPathToGUID(assetPath);

            var outputs = new List<object>();
            foreach (var context in contexts)
            {
                if (context.contextType != VFXContextType.Output)
                    continue;
                var id = ids[context];
                var data = SafeData(context);
                string expected = null;
                if (data != null && uniqueNames.TryGetValue(data, out var systemName))
                {
                    var contextName = string.IsNullOrEmpty(context.label) ? context.name : context.label;
                    contextName = (contextName ?? string.Empty).Replace('\n', ' ').Replace('|', ' ');
                    expected = context.letter == '\0'
                        ? $"Hidden/VFX/{assetName}/{systemName}/{contextName}"
                        : $"Hidden/VFX/{assetName}/{systemName}/{context.letter}/{contextName}";
                }

                var matched = new List<object>();
                if (data is VFXDataMesh mesh)
                {
                    // A static mesh output renders with the shader the author picked (kept on its data),
                    // not with a generated one.
                    expected = null;
                    Shader meshShader = null;
                    try
                    {
                        meshShader = mesh.shader;
                    }
                    catch (Exception exception)
                    {
                        warnings.Add($"{id}: mesh shader not readable: {exception.GetType().Name}: {exception.Message}");
                    }
                    var reference = meshShader != null ? AssetRef(meshShader) : null;
                    if (reference != null)
                        matched.Add(new Map { { "name", meshShader.name }, { "path", reference.Get("path") }, { "guid", reference.Get("guid") } });
                    else
                        warnings.Add($"{id}: the mesh output has no shader");
                }
                else if (expected != null)
                {
                    foreach (var shader in shaders
                                 .Where(s => s.name == expected || s.name.StartsWith(expected + "/", StringComparison.Ordinal))
                                 .OrderBy(s => s.name, StringComparer.Ordinal))
                        matched.Add(new Map { { "name", shader.name }, { "path", assetPath }, { "guid", guid } });
                }
                if (matched.Count == 0 && !(data is VFXDataMesh))
                    warnings.Add($"{id}: no generated shader named '{expected ?? "?"}' in '{assetPath}'; the graph may not be compiled");

                Map shaderGraph = null;
                try
                {
                    var asset = VFXShaderGraphHelpers.GetShaderGraph(context);
                    if (asset != null)
                        shaderGraph = AssetRef(asset);
                }
                catch (Exception exception)
                {
                    warnings.Add($"{id}: shader graph not readable: {exception.GetType().Name}: {exception.Message}");
                }

                outputs.Add(new Map
                {
                    { "id", id },
                    { "expected_shader", expected },
                    { "shaders", matched },
                    { "shader_graph", shaderGraph },
                    { "visible_settings", VisibleSettings(context, id, warnings) },
                    { "effective", EffectiveFeatures(context, id, warnings) },
                });
            }

            return new Map
            {
                { "system_names", systemNames },
                { "outputs", outputs },
            };
        }

        /// <summary>A value as the plain tree <see cref="Describe"/> prints (colours as lists, gradients and curves as maps, assets as references).</summary>
        public static object PlainValue(object value) => ToPlain(value, 0, null);

        static List<object> VisibleSettings(VFXModel model, string id, List<string> warnings)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var setting in model.GetSettings(false, VFXSettingAttribute.VisibleFlags.InInspector))
                    names.Add(setting.name);
                foreach (var setting in model.GetSettings(false, VFXSettingAttribute.VisibleFlags.InGraph))
                    names.Add(setting.name);
            }
            catch (Exception exception)
            {
                warnings.Add($"{id}: visible settings not readable: {exception.GetType().Name}: {exception.Message}");
            }
            return names.OrderBy(n => n, StringComparer.Ordinal).Select(n => (object)n).ToList();
        }

        static Map EffectiveFeatures(VFXContext context, string id, List<string> warnings)
        {
            object Read(string name, Func<object> read)
            {
                try
                {
                    return read();
                }
                catch (Exception exception)
                {
                    warnings.Add($"{id}: {name} not readable: {exception.GetType().Name}: {exception.Message}");
                    return null;
                }
            }

            var particle = context as VFXAbstractParticleOutput;
            var rendered = context as VFXAbstractRenderedOutput;
            var renderer = context as IVFXSubRenderer;
            return new Map
            {
                { "sorted", particle == null ? null : Read("sorted", () => particle.HasSorting()) },
                { "indirect_draw", particle == null ? null : Read("indirect_draw", () => particle.HasIndirectDraw()) },
                { "shadow_casting", renderer == null ? null : Read("shadow_casting", () => renderer.hasShadowCasting) },
                {
                    "soft_particles", particle == null ? null : Read("soft_particles",
                        () => particle.supportSoftParticles && particle.GetSettingValue("useSoftParticle") is bool soft && soft)
                },
                { "motion_vector", renderer == null ? null : Read("motion_vector", () => renderer.hasMotionVector) },
                { "alpha_clipping", rendered == null ? null : Read("alpha_clipping", () => rendered.hasAlphaClipping) },
                { "flipbook", particle == null ? null : Read("flipbook", () => particle.usesFlipbook) },
                { "strips", particle == null ? null : Read("strips", () => particle.HasStrips(false)) },
                { "ray_traced", renderer == null ? null : Read("ray_traced", () => renderer.isRayTraced) },
            };
        }
    }
}
