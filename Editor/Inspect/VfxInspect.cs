using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cuvara.VFX.Editor.Bridge;
using Cuvara.VFX.Editor.Verbs;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.VFX;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// <c>inspect</c>: the static facts of one effect that a lint needs, read from the graph (through
    /// <see cref="VfxGraphBridge.Describe"/> and <see cref="VfxGraphBridge.InspectOutputs"/>), the
    /// importers of the textures it references and, for a prefab, the property overrides of its
    /// <see cref="VisualEffect"/>. Schema <c>Schemas~/vfx-inspect.schema.json</c>.
    /// </summary>
    /// <remarks>
    /// Nothing here changes an asset. The output has no timestamp and a fixed order (describe order for
    /// nodes, ordinal order for textures), so two calls on an unchanged effect give the same JSON.
    /// </remarks>
    public static class VfxInspect
    {
        /// <summary>Schema id of the inspect output.</summary>
        public const string Schema = "cuvara.vfx-inspect/1";

        /// <summary>The sheets of <c>VisualEffect.m_PropertySheet</c>, in the order they are read.</summary>
        static readonly string[] Sheets =
        {
            "m_Float", "m_Vector2f", "m_Vector3f", "m_Vector4f", "m_Uint", "m_Int", "m_Matrix4x4f",
            "m_AnimationCurve", "m_Gradient", "m_NamedObject", "m_Bool",
        };

        /// <summary>Output settings that inspect reports elsewhere or that are not a value.</summary>
        static readonly HashSet<string> NotRenderSettings = new HashSet<string>(StringComparer.Ordinal)
        {
            "blendMode", "shaderGraph", "materialSettings", "shader",
        };

        sealed class Override
        {
            public string Sheet;
            public string Name;
            public object Value;
            public UnityEngine.Object Asset;
        }

        sealed class TextureUse
        {
            public string Guid;
            public long FileId;
            public string Path;
            public readonly SortedSet<string> UsedBy = new SortedSet<string>(StringComparer.Ordinal);
        }

        /// <summary>Runs <c>inspect</c> on <paramref name="effectArgument"/> (a <c>.prefab</c> or a <c>.vfx</c>) into <paramref name="result"/>.</summary>
        public static void Run(string effectArgument, VfxResult result)
        {
            var effect = DescribeVerb.ToProjectPath(effectArgument);
            string assetPath;
            JsonObject prefabInfo = null;
            var overrides = new List<Override>();

            if (effect.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(effect);
                if (root == null)
                {
                    result.Error($"effect '{effect}' is not a prefab inside the project or a package of it");
                    return;
                }
                var effects = root.GetComponentsInChildren<VisualEffect>(true);
                if (effects.Length != 1)
                {
                    result.Error(effects.Length == 0
                        ? $"prefab '{effect}' has no VisualEffect component"
                        : $"prefab '{effect}' has {effects.Length} VisualEffect components ({string.Join(", ", effects.Select(e => HierarchyPath(e.transform)))}); inspect reads one: give the .vfx of each instead");
                    return;
                }
                var visualEffect = effects[0];
                if (visualEffect.visualEffectAsset == null)
                {
                    result.Error($"the VisualEffect of prefab '{effect}' ({HierarchyPath(visualEffect.transform)}) has no asset");
                    return;
                }
                assetPath = AssetDatabase.GetAssetPath(visualEffect.visualEffectAsset);
                prefabInfo = new JsonObject
                {
                    { "path", effect },
                    { "guid", AssetDatabase.AssetPathToGUID(effect) },
                    { "visual_effect", HierarchyPath(visualEffect.transform) },
                };
                overrides = ReadOverrides(visualEffect);
            }
            else if (effect.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase))
            {
                if (AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(effect) == null)
                {
                    result.Error($"effect '{effect}' is not a VisualEffectAsset inside the project or a package of it");
                    return;
                }
                assetPath = effect;
            }
            else
            {
                result.Error($"effect '{effect}' must be a .prefab that holds a VisualEffect or a .vfx asset");
                return;
            }

            var describeWarnings = new List<string>();
            var graph = VfxGraphBridge.Describe(assetPath, describeWarnings);
            var describeText = DescribeVerb.ToResult(graph, describeWarnings).ToJson();
            var factWarnings = new List<string>();
            var facts = VfxGraphBridge.InspectOutputs(assetPath, factWarnings);
            foreach (var warning in describeWarnings.Concat(factWarnings).Distinct())
                result.Warning(warning);

            var systemNames = (VfxGraphBridge.Map)facts.Get("system_names");
            var outputFacts = ((List<object>)facts.Get("outputs")).Cast<VfxGraphBridge.Map>().ToDictionary(m => (string)m.Get("id"), StringComparer.Ordinal);
            var contexts = Maps(graph.Get("contexts"));
            var parameters = Maps(graph.Get("parameters"));
            var operators = Maps(graph.Get("operators"));

            result.Set("schema", Schema);
            result.Set("effect", new JsonObject
            {
                { "path", effect },
                { "guid", AssetDatabase.AssetPathToGUID(effect) },
                { "kind", prefabInfo != null ? "prefab" : "vfx" },
            });
            result.Set("asset", new JsonObject { { "path", assetPath }, { "guid", AssetDatabase.AssetPathToGUID(assetPath) } });
            result.Set("prefab", prefabInfo);
            result.Set("recipe", Recipe(assetPath));
            result.Set("vfx_graph", VfxGraphBridge.InstalledVersion);
            result.Set("unity", Application.unityVersion);
            result.Set("build_target", EditorUserBuildSettings.activeBuildTarget.ToString());
            result.Set("describe_sha256", VfxRecipeBuilder.Sha256(new UTF8Encoding(false).GetBytes(describeText)));

            var systems = Systems(graph, contexts, systemNames);
            var outputs = Outputs(contexts, outputFacts, result);
            var exposed = ExposedProperties(parameters, overrides, result);
            var textures = Textures(contexts, operators, parameters, overrides, result);
            var colors = HdrColors(contexts, operators, parameters, overrides, exposed);

            result.Set("systems", systems);
            result.Set("outputs", outputs);
            result.Set("textures", textures);
            result.Set("exposed_properties", exposed);
            result.Set("hdr_colors", colors);
            result.Set("totals", new JsonObject
            {
                { "systems", systems.Count },
                { "capacity", systems.Sum(s => ((JsonObject)s).Get("capacity") is long c ? c : 0L) },
                { "outputs", outputs.Count },
                { "textures", textures.Count },
                { "texture_bytes", textures.Sum(t => ((JsonObject)t).Get("estimated_bytes") is long b ? b : 0L) },
                { "exposed_properties", exposed.Count },
                { "hdr_colors", colors.Count },
                { "hdr_peak", colors.Count == 0 ? null : (object)colors.Max(c => (float)((JsonObject)c).Get("peak")) },
            });
        }

        // ------------------------------------------------------------------ graph

        static JsonObject Recipe(string assetPath)
        {
            if (!assetPath.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase))
                return null;
            var recipePath = assetPath.Substring(0, assetPath.Length - ".vfx".Length) + VfxRecipeBuilder.RecipeSuffix;
            var file = VfxProjectPaths.ToFullPath(recipePath);
            if (!File.Exists(file))
                return null;
            return new JsonObject { { "path", recipePath }, { "sha256", VfxRecipeBuilder.Sha256(File.ReadAllBytes(file)) } };
        }

        static List<object> Systems(VfxGraphBridge.Map graph, List<VfxGraphBridge.Map> contexts, VfxGraphBridge.Map systemNames)
        {
            var list = new List<object>();
            foreach (var system in Maps(graph.Get("systems")))
            {
                var id = (string)system.Get("id");
                var name = systemNames.Get(id) as string;
                var members = ((List<object>)system.Get("contexts")).Cast<string>().ToList();
                list.Add(new JsonObject
                {
                    { "id", id },
                    { "name", string.IsNullOrEmpty(name) ? system.Get("name") : name },
                    { "data_type", system.Get("data_type") },
                    { "capacity", ToLong(system.Get("capacity")) },
                    { "contexts", members.Cast<object>().ToList() },
                    {
                        "outputs", contexts.Where(c => (string)c.Get("context_type") == "Output" && members.Contains((string)c.Get("id")))
                            .Select(c => c.Get("id")).ToList()
                    },
                });
            }
            return list;
        }

        static List<object> Outputs(List<VfxGraphBridge.Map> contexts, Dictionary<string, VfxGraphBridge.Map> facts, VfxResult result)
        {
            var list = new List<object>();
            foreach (var context in contexts.Where(c => (string)c.Get("context_type") == "Output"))
            {
                var id = (string)context.Get("id");
                facts.TryGetValue(id, out var fact);
                var visible = new HashSet<string>(((List<object>)fact?.Get("visible_settings") ?? new List<object>()).Cast<string>(), StringComparer.Ordinal);
                var settings = Maps(context.Get("settings"));

                var renderSettings = new JsonObject();
                foreach (var setting in settings)
                {
                    var name = (string)setting.Get("name");
                    if (!NotRenderSettings.Contains(name) && visible.Contains(name))
                        renderSettings.Add(name, setting.Get("value"));
                }

                var shaders = ((List<object>)fact?.Get("shaders") ?? new List<object>()).Cast<VfxGraphBridge.Map>().ToList();
                if (shaders.Count > 1)
                    result.Warning($"{id}: {shaders.Count} generated shaders match '{fact.Get("expected_shader")}'; the first is reported");
                var shader = shaders.FirstOrDefault();

                list.Add(new JsonObject
                {
                    { "id", id },
                    { "system", context.Get("system") },
                    { "type", context.Get("type") },
                    { "name", context.Get("name") },
                    { "label", context.Get("label") },
                    { "blend_mode", settings.FirstOrDefault(s => (string)s.Get("name") == "blendMode")?.Get("value") },
                    {
                        "shader", shader == null ? null : new JsonObject
                        {
                            { "name", shader.Get("name") }, { "path", shader.Get("path") }, { "guid", shader.Get("guid") },
                        }
                    },
                    { "shader_graph", AssetRef(fact?.Get("shader_graph") as VfxGraphBridge.Map) },
                    { "effective", fact?.Get("effective") },
                    { "render_settings", renderSettings },
                });
            }
            return list;
        }

        static List<object> ExposedProperties(List<VfxGraphBridge.Map> parameters, List<Override> overrides, VfxResult result)
        {
            var list = new List<object>();
            var matched = new HashSet<Override>();
            foreach (var parameter in parameters)
            {
                if (!(parameter.Get("exposed") is bool exposed) || !exposed || parameter.Get("is_output") is bool output && output)
                    continue;
                var name = (string)parameter.Get("exposed_name") ?? string.Empty;
                var mine = overrides.Where(o => o.Name == name || o.Name.StartsWith(name + "_", StringComparison.Ordinal)).ToList();
                foreach (var o in mine)
                    matched.Add(o);
                list.Add(new JsonObject
                {
                    { "id", parameter.Get("id") },
                    { "name", name },
                    { "type", parameter.Get("type") },
                    { "category", parameter.Get("category") },
                    { "default", parameter.Get("value") },
                    { "overridden", mine.Count > 0 },
                    { "overrides", mine.Select(o => (object)new JsonObject { { "name", o.Name }, { "sheet", o.Sheet }, { "value", o.Value } }).ToList() },
                });
            }
            foreach (var o in overrides.Where(o => !matched.Contains(o)))
                result.Warning($"prefab override '{o.Name}' ({o.Sheet}) matches no exposed property of the graph");
            return list;
        }

        // ------------------------------------------------------------------ textures

        static List<object> Textures(List<VfxGraphBridge.Map> contexts, List<VfxGraphBridge.Map> operators, List<VfxGraphBridge.Map> parameters,
            List<Override> overrides, VfxResult result)
        {
            var uses = new Dictionary<string, TextureUse>(StringComparer.Ordinal);

            void Use(object value, string where)
            {
                if (!(value is VfxGraphBridge.Map reference) || !(reference.Get("guid") is string guid) || string.IsNullOrEmpty(guid))
                    return;
                var fileId = reference.Get("file_id") is long id ? id : 0L;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(LoadAsset(path, fileId) is Texture))
                    return;
                var key = guid + ":" + fileId;
                if (!uses.TryGetValue(key, out var use))
                    uses[key] = use = new TextureUse { Guid = guid, FileId = fileId, Path = path };
                use.UsedBy.Add(where);
            }

            foreach (var parameter in parameters)
                Use(parameter.Get("value"), (string)parameter.Get("id"));
            foreach (var (address, input) in UnlinkedInputs(contexts, operators))
                Use(input.Get("value"), address);
            foreach (var o in overrides)
            {
                if (!(o.Asset is Texture texture))
                    continue;
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture, out string guid, out long fileId))
                {
                    var key = guid + ":" + fileId;
                    if (!uses.TryGetValue(key, out var use))
                        uses[key] = use = new TextureUse { Guid = guid, FileId = fileId, Path = AssetDatabase.GetAssetPath(texture) };
                    use.UsedBy.Add("prefab:" + o.Name);
                }
                else
                {
                    result.Warning($"prefab override '{o.Name}' references texture '{texture.name}', which is not a project asset");
                }
            }

            return uses.Values
                .OrderBy(u => u.Path, StringComparer.Ordinal)
                .ThenBy(u => u.Guid, StringComparer.Ordinal)
                .ThenBy(u => u.FileId)
                .Select(u => (object)TextureFacts(u))
                .ToList();
        }

        static UnityEngine.Object LoadAsset(string path, long fileId)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            if (fileId != 0)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string _, out long id) && id == fileId)
                        return asset;
            }
            return AssetDatabase.LoadMainAssetAtPath(path);
        }

        static JsonObject TextureFacts(TextureUse use)
        {
            var texture = (Texture)LoadAsset(use.Path, use.FileId);
            var importer = AssetImporter.GetAtPath(use.Path);
            var textureImporter = importer as TextureImporter;
            int depth = texture is Texture2DArray array ? array.depth
                : texture is Texture3D volume ? volume.depth
                : texture is CubemapArray cubes ? cubes.cubemapCount
                : 1;
            string textureFormat = texture is Texture2D t2 ? t2.format.ToString()
                : texture is Cubemap cube ? cube.format.ToString()
                : texture is Texture2DArray a2 ? a2.format.ToString()
                : texture is Texture3D t3 ? t3.format.ToString()
                : texture is CubemapArray ca ? ca.format.ToString()
                : null;

            return new JsonObject
            {
                { "path", use.Path },
                { "guid", use.Guid },
                { "name", texture.name },
                { "type", texture.GetType().Name },
                { "dimension", texture.dimension.ToString() },
                { "width", texture.width },
                { "height", texture.height },
                { "depth", depth },
                { "format", texture.graphicsFormat.ToString() },
                { "texture_format", textureFormat },
                { "srgb", textureImporter != null ? textureImporter.sRGBTexture : texture.isDataSRGB },
                { "mip_maps", textureImporter != null ? textureImporter.mipmapEnabled : texture.mipmapCount > 1 },
                { "mip_count", texture.mipmapCount },
                { "compression", textureImporter != null ? textureImporter.textureCompression.ToString() : null },
                { "crunched", textureImporter != null ? (object)textureImporter.crunchedCompression : null },
                { "compressed_format", GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat) },
                { "max_size", textureImporter != null ? (object)textureImporter.maxTextureSize : null },
                { "readable", texture.isReadable },
                { "importer", importer != null ? importer.GetType().Name : null },
                { "estimated_bytes", EstimatedBytes(texture) },
                { "used_by", use.UsedBy.Select(u => (object)u).ToList() },
            };
        }

        /// <summary>
        /// The GPU size of all mip levels, from the size, the mip count and the graphics format
        /// (<see cref="GraphicsFormatUtility.ComputeMipmapSize(int, int, GraphicsFormat)"/>); null for
        /// other textures (render textures). A readable texture keeps a CPU copy as well; see <c>readable</c>.
        /// </summary>
        static object EstimatedBytes(Texture texture)
        {
            var format = texture.graphicsFormat;
            if (format == GraphicsFormat.None)
                return null;
            int mips = Math.Max(1, texture.mipmapCount);
            long total = 0;
            for (int mip = 0; mip < mips; mip++)
            {
                int width = Math.Max(1, texture.width >> mip);
                int height = Math.Max(1, texture.height >> mip);
                switch (texture)
                {
                    case Texture3D volume:
                        total += GraphicsFormatUtility.ComputeMipmapSize(width, height, Math.Max(1, volume.depth >> mip), format);
                        break;
                    case Texture2DArray array:
                        total += (long)GraphicsFormatUtility.ComputeMipmapSize(width, height, format) * array.depth;
                        break;
                    case CubemapArray cubes:
                        total += (long)GraphicsFormatUtility.ComputeMipmapSize(width, height, format) * 6 * cubes.cubemapCount;
                        break;
                    case Cubemap _:
                        total += (long)GraphicsFormatUtility.ComputeMipmapSize(width, height, format) * 6;
                        break;
                    case Texture2D _:
                        total += GraphicsFormatUtility.ComputeMipmapSize(width, height, format);
                        break;
                    default:
                        return null;
                }
            }
            return total;
        }

        // ------------------------------------------------------------------ HDR colours

        static List<object> HdrColors(List<VfxGraphBridge.Map> contexts, List<VfxGraphBridge.Map> operators, List<VfxGraphBridge.Map> parameters,
            List<Override> overrides, List<object> exposed)
        {
            var list = new List<object>();

            void Add(string source, string path, string name, string type, object value)
            {
                if (type == "Gradient")
                {
                    if (!(value is IEnumerable<KeyValuePair<string, object>> gradient))
                        return;
                    var keys = Get(gradient, "color_keys") as List<object> ?? new List<object>();
                    for (int i = 0; i < keys.Count; i++)
                        if (keys[i] is IEnumerable<KeyValuePair<string, object>> key)
                            Add(source, path + ".color_keys." + i, name, "Gradient", Get(key, "color"));
                    return;
                }
                if (!(value is List<object> components) || components.Count < 3 || components.Count > 4 || !components.All(IsNumber))
                    return;
                var rgb = components.Take(3).Select(c => Convert.ToSingle(c, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var peak = Math.Max(rgb[0], Math.Max(rgb[1], rgb[2]));
                if (!(peak > 1f))
                    return;
                var entry = new JsonObject
                {
                    { "source", source },
                    { "path", path },
                    { "name", name },
                    { "type", type },
                    { "rgba", new List<object> { rgb[0], rgb[1], rgb[2], components.Count == 4 ? (object)Convert.ToSingle(components[3], System.Globalization.CultureInfo.InvariantCulture) : null } },
                    { "peak", peak },
                    { "intensity", Math.Round(Math.Log(peak, 2.0), 4) },
                };
                list.Add(entry);
            }

            foreach (var parameter in parameters)
            {
                if (parameter.Get("is_output") is bool output && output)
                    continue;
                var type = (string)parameter.Get("type");
                var name = (string)parameter.Get("exposed_name");
                if (IsColorType(type, name))
                    Add("parameter", (string)parameter.Get("id"), name, type, parameter.Get("value"));
            }
            foreach (var (address, input) in UnlinkedInputs(contexts, operators))
            {
                var type = (string)input.Get("type");
                var name = (string)input.Get("name");
                if (IsColorType(type, name))
                    Add("slot", address, name, type, input.Get("value"));
            }
            foreach (var o in overrides)
            {
                var property = exposed.Cast<JsonObject>().FirstOrDefault(p => (string)p.Get("name") == o.Name);
                var type = property?.Get("type") as string;
                if (type != null && IsColorType(type, o.Name))
                    Add("prefab_override", "prefab:" + o.Name, o.Name, type, o.Value);
            }
            return list;
        }

        /// <summary>Colour, Gradient, or a Vector3/Vector4 whose name says colour (<c>_Color</c>, <c>BaseColor</c>).</summary>
        static bool IsColorType(string type, string name)
        {
            if (type == "Color" || type == "Gradient")
                return true;
            if (type != "Vector3" && type != "Vector4" || string.IsNullOrEmpty(name))
                return false;
            return name.IndexOf("color", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("colour", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------------ prefab overrides

        static List<Override> ReadOverrides(VisualEffect visualEffect)
        {
            var list = new List<Override>();
            using (var serialized = new SerializedObject(visualEffect))
            {
                var sheet = serialized.FindProperty("m_PropertySheet");
                if (sheet == null)
                    return list;
                foreach (var sheetName in Sheets)
                {
                    var array = sheet.FindPropertyRelative(sheetName + ".m_Array");
                    if (array == null || !array.isArray)
                        continue;
                    for (int i = 0; i < array.arraySize; i++)
                    {
                        var element = array.GetArrayElementAtIndex(i);
                        var overridden = element.FindPropertyRelative("m_Overridden");
                        if (overridden == null || !overridden.boolValue)
                            continue;
                        var value = element.FindPropertyRelative("m_Value");
                        var entry = new Override
                        {
                            Sheet = sheetName,
                            Name = element.FindPropertyRelative("m_Name")?.stringValue ?? string.Empty,
                        };
                        entry.Value = ReadValue(value, out entry.Asset);
                        list.Add(entry);
                    }
                }
            }
            return list
                .OrderBy(o => o.Name, StringComparer.Ordinal)
                .ThenBy(o => o.Sheet, StringComparer.Ordinal)
                .ToList();
        }

        static object ReadValue(SerializedProperty property, out UnityEngine.Object asset)
        {
            asset = null;
            if (property == null)
                return null;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Float: return property.floatValue;
                case SerializedPropertyType.Integer: return property.longValue;
                case SerializedPropertyType.Boolean: return property.boolValue;
                case SerializedPropertyType.Vector2: return VfxGraphBridge.PlainValue(property.vector2Value);
                case SerializedPropertyType.Vector3: return VfxGraphBridge.PlainValue(property.vector3Value);
                case SerializedPropertyType.Vector4: return VfxGraphBridge.PlainValue(property.vector4Value);
                case SerializedPropertyType.Color: return VfxGraphBridge.PlainValue(property.colorValue);
                case SerializedPropertyType.AnimationCurve: return VfxGraphBridge.PlainValue(property.animationCurveValue);
                case SerializedPropertyType.Gradient: return VfxGraphBridge.PlainValue(property.gradientValue);
                case SerializedPropertyType.ObjectReference:
                    asset = property.objectReferenceValue;
                    return asset == null ? null : VfxGraphBridge.PlainValue(asset);
                default:
                {
                    // Matrix4x4 is serialised as the fields e00 .. e33.
                    var matrix = new Matrix4x4();
                    bool any = false;
                    for (int row = 0; row < 4; row++)
                    {
                        for (int column = 0; column < 4; column++)
                        {
                            var cell = property.FindPropertyRelative($"e{row}{column}");
                            if (cell == null)
                                continue;
                            matrix[row, column] = cell.floatValue;
                            any = true;
                        }
                    }
                    return any ? VfxGraphBridge.PlainValue(matrix) : property.propertyType.ToString();
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Every input slot of the contexts, their blocks and the operators that has no link, with its describe address.</summary>
        static IEnumerable<(string address, VfxGraphBridge.Map input)> UnlinkedInputs(List<VfxGraphBridge.Map> contexts, List<VfxGraphBridge.Map> operators)
        {
            IEnumerable<(string, VfxGraphBridge.Map)> Of(VfxGraphBridge.Map node)
            {
                var id = (string)node.Get("id");
                foreach (var input in Maps(node.Get("inputs")))
                    if (!(input.Get("linked") is bool linked && linked))
                        yield return (id + "." + (string)input.Get("ref"), input);
            }

            foreach (var context in contexts)
            {
                foreach (var item in Of(context))
                    yield return item;
                foreach (var block in Maps(context.Get("blocks")))
                    foreach (var item in Of(block))
                        yield return item;
            }
            foreach (var op in operators)
                foreach (var item in Of(op))
                    yield return item;
        }

        static JsonObject AssetRef(VfxGraphBridge.Map reference)
        {
            if (reference == null)
                return null;
            return new JsonObject { { "name", reference.Get("name") }, { "path", reference.Get("path") }, { "guid", reference.Get("guid") } };
        }

        static List<VfxGraphBridge.Map> Maps(object value) =>
            (value as List<object> ?? new List<object>()).OfType<VfxGraphBridge.Map>().ToList();

        static object Get(IEnumerable<KeyValuePair<string, object>> map, string key)
        {
            foreach (var pair in map)
                if (pair.Key == key)
                    return pair.Value;
            return null;
        }

        static bool IsNumber(object value) => value is float || value is double || value is int || value is long;

        static object ToLong(object value)
        {
            switch (value)
            {
                case null: return null;
                case int i: return (long)i;
                case long l: return l;
                case uint u: return (long)u;
                default: return value;
            }
        }

        static string HierarchyPath(Transform transform)
        {
            var names = new List<string>();
            for (var t = transform; t != null; t = t.parent)
                names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
