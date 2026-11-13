using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

namespace Cuvara.VFX.Editor.Bridge
{
    // The write half of the bridge: create a graph asset, add nodes by the catalog's names, set
    // settings and slot values, link, save and compile. Every change goes through the VFX Graph model
    // API (VFXModel.AddChild, SetSettingValue, VFXSlot.value and Link, VFXContext.LinkTo) and the asset
    // is written by VisualEffectResource.WriteAsset; no .vfx text is written or patched here.
    public static partial class VfxGraphBridge
    {
        /// <summary>
        /// An edit that cannot be done: an unknown type, variant, setting, slot or id, a value of the
        /// wrong shape, or a link the graph refuses. <see cref="ValidNames"/> lists the names that would
        /// have been accepted, nearest to the requested one first (empty when no list applies).
        /// </summary>
        public sealed class EditException : Exception
        {
            /// <summary>Creates the exception.</summary>
            public EditException(string message, IReadOnlyList<string> validNames = null)
                : base(message)
            {
                ValidNames = validNames ?? Array.Empty<string>();
            }

            /// <summary>The accepted names, nearest first.</summary>
            public IReadOnlyList<string> ValidNames { get; }

            internal static EditException Unknown(string what, string wanted, IEnumerable<string> valid)
            {
                var ordered = NearestFirst(wanted, valid);
                var list = ordered.Count == 0 ? "(none)" : string.Join(", ", ordered);
                return new EditException($"unknown {what} '{wanted}'; valid {what}s (nearest first): {list}", ordered);
            }
        }

        /// <summary>
        /// The names ordered by edit distance to <paramref name="wanted"/> (case-insensitive; a name that
        /// contains the wanted text counts as close), then ordinally. Duplicates are removed.
        /// </summary>
        public static IReadOnlyList<string> NearestFirst(string wanted, IEnumerable<string> names)
        {
            var key = (wanted ?? string.Empty).ToLowerInvariant();
            return names
                .Where(n => n != null)
                .Distinct(StringComparer.Ordinal)
                .Select(n => (name: n, distance: Distance(key, n.ToLowerInvariant())))
                .OrderBy(p => p.distance)
                .ThenBy(p => p.name, StringComparer.Ordinal)
                .Select(p => p.name)
                .ToList();
        }

        static int Distance(string a, string b)
        {
            if (a.Length > 0 && b.Length > 0 && (b.Contains(a) || a.Contains(b)))
                return Math.Abs(a.Length - b.Length) / 4;
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
                previous[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                (previous, current) = (current, previous);
            }
            return previous[b.Length];
        }

        /// <summary>Drops the cached resource of an asset so the next read loads it from disk.</summary>
        public static void ForgetResource(string assetPath)
        {
            if (!string.IsNullOrEmpty(assetPath))
                VisualEffectResource.ForgetAtPath(assetPath);
        }

        // ------------------------------------------------------------------ settings shared with catalog

        /// <summary>
        /// The user-editable settings of a model: the fields marked <c>VFXSetting</c> and visible in the
        /// inspector or the graph, including those a model forwards to its data (Initialize: capacity,
        /// bounds mode) or to its render-pipeline sub-output. Falls back to the model's own fields when
        /// the model cannot list them (a library instance without a parent context).
        /// </summary>
        static List<(FieldInfo field, VFXSettingAttribute attribute, object instance)> UserSettings(VFXModel model)
        {
            var list = new List<(FieldInfo, VFXSettingAttribute, object)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var setting in model.GetSettings(true))
                {
                    if (!setting.valid)
                        continue;
                    var attribute = setting.field.GetCustomAttributes(typeof(VFXSettingAttribute), true).OfType<VFXSettingAttribute>().FirstOrDefault();
                    if (!IsUserSetting(attribute) || !seen.Add(setting.field.Name))
                        continue;
                    list.Add((setting.field, attribute, setting.instance));
                }
                return list;
            }
            catch (Exception)
            {
                list.Clear();
                seen.Clear();
            }

            foreach (var (field, attribute) in VFXModel.GetFields(model.GetType()))
            {
                if (IsUserSetting(attribute) && seen.Add(field.Name))
                    list.Add((field, attribute, model));
            }
            return list;
        }

        // ------------------------------------------------------------------ library lookup

        enum NodeKind
        {
            Context,
            Block,
            Operator,
        }

        sealed class LibraryEntry
        {
            public Type type;
            public string variantId;
            public IVFXModelDescriptor descriptor;
            public bool isSubVariant;
        }

        static List<LibraryEntry> LibraryEntries(NodeKind kind)
        {
            IEnumerable<IVFXModelDescriptor> descriptors;
            switch (kind)
            {
                case NodeKind.Context:
                    descriptors = VFXLibrary.GetContexts().Where(d => d.infoAttribute != null && !d.infoAttribute.experimental);
                    break;
                case NodeKind.Block:
                    descriptors = VFXLibrary.GetBlocks().Where(d => d.infoAttribute != null && !d.infoAttribute.experimental);
                    break;
                default:
                    descriptors = VFXLibrary.GetOperators().Where(d => d.infoAttribute != null && !d.infoAttribute.experimental);
                    break;
            }

            var entries = new List<LibraryEntry>();
            foreach (var descriptor in descriptors)
            {
                entries.Add(new LibraryEntry { type = descriptor.modelType, variantId = descriptor.variant.GetUniqueIdentifier(), descriptor = descriptor });
                IVFXModelDescriptor[] subs;
                try
                {
                    subs = descriptor.subVariantDescriptors ?? Array.Empty<IVFXModelDescriptor>();
                }
                catch (Exception)
                {
                    subs = Array.Empty<IVFXModelDescriptor>();
                }
                foreach (var sub in subs)
                    entries.Add(new LibraryEntry { type = sub.modelType, variantId = sub.variant.GetUniqueIdentifier(), descriptor = sub, isSubVariant = true });
            }
            return entries;
        }

        static string KindName(NodeKind kind) => kind == NodeKind.Context ? "context" : kind == NodeKind.Block ? "block" : "operator";

        /// <summary>
        /// Creates a library model by its catalog type name (or full name) and, optionally, its catalog
        /// variant id. Without a variant, a type with exactly one catalog entry uses that entry's variant;
        /// a type with several entries is created with its field defaults.
        /// </summary>
        static VFXModel CreateLibraryModel(NodeKind kind, string type, string variant)
        {
            if (string.IsNullOrEmpty(type))
                throw new EditException($"{KindName(kind)} type is empty");
            var entries = LibraryEntries(kind);
            var byType = entries.Where(e => e.type.Name == type || e.type.FullName == type).ToList();
            if (byType.Count == 0)
                throw EditException.Unknown(KindName(kind) + " type", type, entries.Select(e => e.type.Name));

            var types = byType.Select(e => e.type).Distinct().ToList();
            if (types.Count > 1)
                throw new EditException($"{KindName(kind)} type '{type}' is ambiguous; use the full name: {string.Join(", ", types.Select(t => t.FullName).OrderBy(n => n, StringComparer.Ordinal))}",
                    types.Select(t => t.FullName).ToList());

            LibraryEntry chosen = null;
            if (!string.IsNullOrEmpty(variant))
            {
                chosen = byType.FirstOrDefault(e => e.variantId == variant);
                if (chosen == null)
                    throw EditException.Unknown($"variant of {type}", variant, byType.Select(e => e.variantId));
            }
            else
            {
                var mainEntries = byType.Where(e => !e.isSubVariant).ToList();
                if (mainEntries.Count == 1)
                    chosen = mainEntries[0];
            }

            var model = chosen != null ? chosen.descriptor.CreateInstance() : (VFXModel)ScriptableObject.CreateInstance(types[0]);
            if (model == null)
                throw new EditException($"{KindName(kind)} type '{type}' could not be created");
            return model;
        }

        // ------------------------------------------------------------------ value conversion

        /// <summary>
        /// Converts a plain JSON value (null, bool, long, double, string, lists, ordered maps) to
        /// <paramref name="type"/>. <paramref name="current"/> is the value a struct starts from, so a
        /// map can change some fields and keep the others.
        /// </summary>
        static object ConvertTo(Type type, object json, object current, string where)
        {
            if (type == null)
                throw new EditException($"{where}: the target has no type");

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return LoadAsset(type, json, where);

            if (json == null)
            {
                if (!type.IsValueType)
                    return null;
                throw new EditException($"{where}: null is not a {ValueTypeName(type)}");
            }

            if (type == typeof(float)) return (float)Number(json, where);
            if (type == typeof(double)) return Number(json, where);
            if (type == typeof(int)) return (int)Integer(json, where, int.MinValue, int.MaxValue);
            if (type == typeof(uint)) return (uint)Integer(json, where, 0, uint.MaxValue);
            if (type == typeof(long)) return Integer(json, where, long.MinValue, long.MaxValue);
            if (type == typeof(bool))
            {
                if (json is bool b)
                    return b;
                throw new EditException($"{where}: expected true or false, got {Show(json)}");
            }
            if (type == typeof(string))
            {
                if (json is string s)
                    return s;
                throw new EditException($"{where}: expected a string, got {Show(json)}");
            }
            if (type.IsEnum)
                return ParseEnum(type, json, where);
            if (type == typeof(Vector2)) { var f = Floats(json, 2, 2, where); return new Vector2(f[0], f[1]); }
            if (type == typeof(Vector3)) { var f = Floats(json, 3, 3, where); return new Vector3(f[0], f[1], f[2]); }
            if (type == typeof(Vector4)) { var f = Floats(json, 4, 4, where); return new Vector4(f[0], f[1], f[2], f[3]); }
            if (type == typeof(Color)) { var f = Floats(json, 3, 4, where); return new Color(f[0], f[1], f[2], f.Length > 3 ? f[3] : 1f); }
            if (type == typeof(Quaternion)) { var f = Floats(json, 4, 4, where); return new Quaternion(f[0], f[1], f[2], f[3]); }
            if (type == typeof(Matrix4x4))
            {
                var f = Floats(json, 16, 16, where);
                var m = new Matrix4x4();
                for (int i = 0; i < 16; i++)
                    m[i] = f[i];
                return m;
            }
            if (type == typeof(Gradient))
                return GradientFrom(json, where);
            if (type == typeof(AnimationCurve))
                return CurveFrom(json, where);

            // A struct or class with public fields (Position, DirectionType, Vector, AABox, Sphere ...).
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            if (fields.Length == 0)
                throw new EditException($"{where}: values of type {ValueTypeName(type)} cannot be written from JSON");
            object instance = current != null && type.IsInstanceOfType(current) ? current : SafeCreate(type, where);
            if (json is IEnumerable<KeyValuePair<string, object>> map)
            {
                foreach (var pair in map)
                {
                    var field = fields.FirstOrDefault(f => f.Name == pair.Key);
                    if (field == null)
                        throw EditException.Unknown($"field of {ValueTypeName(type)}", pair.Key, fields.Select(f => f.Name));
                    field.SetValue(instance, ConvertTo(field.FieldType, pair.Value, SafeGet(field, instance), where + "." + pair.Key));
                }
                return instance;
            }
            if (fields.Length == 1)
            {
                // Shorthand: a struct with one field (Position.position, Vector.vector) takes that field's value.
                fields[0].SetValue(instance, ConvertTo(fields[0].FieldType, json, SafeGet(fields[0], instance), where + "." + fields[0].Name));
                return instance;
            }
            throw new EditException($"{where}: a {ValueTypeName(type)} is written as an object with the fields {string.Join(", ", fields.Select(f => f.Name))}");
        }

        static object SafeCreate(Type type, string where)
        {
            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception exception)
            {
                throw new EditException($"{where}: a {ValueTypeName(type)} cannot be created: {exception.Message}");
            }
        }

        static string Show(object json)
        {
            switch (json)
            {
                case null: return "null";
                case string s: return "\"" + s + "\"";
                case bool b: return b ? "true" : "false";
                case IEnumerable<KeyValuePair<string, object>> _: return "an object";
                case IList _: return "an array";
                default: return Convert.ToString(json, CultureInfo.InvariantCulture);
            }
        }

        static double Number(object json, string where)
        {
            switch (json)
            {
                case long l: return l;
                case int i: return i;
                case double d: return d;
                case float f: return f;
                default: throw new EditException($"{where}: expected a number, got {Show(json)}");
            }
        }

        static long Integer(object json, string where, long min, long max)
        {
            var value = Number(json, where);
            if (Math.Abs(value - Math.Round(value)) > 1e-9)
                throw new EditException($"{where}: expected an integer, got {Show(json)}");
            if (value < min || value > max)
                throw new EditException($"{where}: {Show(json)} is out of range [{min}, {max}]");
            return (long)Math.Round(value);
        }

        static float[] Floats(object json, int min, int max, string where)
        {
            if (!(json is IList list) || json is string)
                throw new EditException($"{where}: expected an array of {(min == max ? min.ToString(CultureInfo.InvariantCulture) : min + " to " + max)} numbers, got {Show(json)}");
            if (list.Count < min || list.Count > max)
                throw new EditException($"{where}: expected {(min == max ? min.ToString(CultureInfo.InvariantCulture) : min + " to " + max)} numbers, got {list.Count}");
            var result = new float[list.Count];
            for (int i = 0; i < list.Count; i++)
                result[i] = (float)Number(list[i], where + "[" + i + "]");
            return result;
        }

        static object ParseEnum(Type type, object json, string where)
        {
            var names = Enum.GetNames(type);
            IEnumerable<string> parts;
            if (json is string s)
                parts = s.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0);
            else if (json is IList list && type.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0)
                parts = list.Cast<object>().Select(o => o as string ?? throw new EditException($"{where}: flag names must be strings"));
            else
                throw new EditException($"{where}: expected one of {string.Join(", ", names)}, got {Show(json)}", names);

            long bits = 0;
            int count = 0;
            foreach (var part in parts)
            {
                if (!names.Contains(part))
                    throw EditException.Unknown($"value of {type.Name}", part, names);
                bits |= Convert.ToInt64(Enum.Parse(type, part), CultureInfo.InvariantCulture);
                count++;
            }
            if (count == 0)
                throw new EditException($"{where}: expected one of {string.Join(", ", names)}", names);
            return Enum.ToObject(type, bits);
        }

        static UnityEngine.Object LoadAsset(Type type, object json, string where)
        {
            if (json == null)
                return null;
            string path;
            switch (json)
            {
                case string s:
                    path = s;
                    break;
                case IEnumerable<KeyValuePair<string, object>> map:
                {
                    var guid = map.Where(p => p.Key == "guid").Select(p => p.Value as string).FirstOrDefault();
                    path = map.Where(p => p.Key == "path").Select(p => p.Value as string).FirstOrDefault();
                    if (!string.IsNullOrEmpty(guid))
                    {
                        var fromGuid = AssetDatabase.GUIDToAssetPath(guid);
                        if (string.IsNullOrEmpty(fromGuid))
                            throw new EditException($"{where}: no asset with GUID {guid}");
                        path = fromGuid;
                    }
                    break;
                }
                default:
                    throw new EditException($"{where}: an asset is written as a project-relative path (Assets/... or Packages/...), got {Show(json)}");
            }

            if (string.IsNullOrEmpty(path))
                throw new EditException($"{where}: the asset path is empty");
            path = path.Replace('\\', '/');
            var asset = AssetDatabase.LoadAssetAtPath(path, type);
            if (asset == null)
                asset = AssetDatabase.LoadAllAssetsAtPath(path).FirstOrDefault(type.IsInstanceOfType);
            if (asset == null)
            {
                var exists = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path));
                throw new EditException(exists
                    ? $"{where}: the asset at '{path}' is not a {type.Name}"
                    : $"{where}: no asset at '{path}' (paths are project-relative: Assets/... or Packages/<package>/...)");
            }
            return asset;
        }

        static Gradient GradientFrom(object json, string where)
        {
            if (!(json is IEnumerable<KeyValuePair<string, object>> map))
                throw new EditException($"{where}: a Gradient is written as {{\"mode\", \"color_keys\": [{{\"t\", \"color\"}}], \"alpha_keys\": [{{\"t\", \"alpha\"}}]}}, got {Show(json)}");
            var valid = new[] { "mode", "color_space", "color_keys", "alpha_keys" };
            var gradient = new Gradient();
            var colors = new List<GradientColorKey>();
            var alphas = new List<GradientAlphaKey>();
            foreach (var pair in map)
            {
                switch (pair.Key)
                {
                    case "mode":
                        gradient.mode = (GradientMode)ParseEnum(typeof(GradientMode), pair.Value, where + ".mode");
                        break;
                    case "color_space":
                        gradient.colorSpace = (ColorSpace)ParseEnum(typeof(ColorSpace), pair.Value, where + ".color_space");
                        break;
                    case "color_keys":
                        foreach (var (key, i) in Items(pair.Value, where + ".color_keys"))
                        {
                            var at = $"{where}.color_keys[{i}]";
                            var t = (float)Number(Field(key, "t", at), at + ".t");
                            var c = Floats(Field(key, "color", at), 3, 4, at + ".color");
                            colors.Add(new GradientColorKey(new Color(c[0], c[1], c[2], c.Length > 3 ? c[3] : 1f), t));
                        }
                        break;
                    case "alpha_keys":
                        foreach (var (key, i) in Items(pair.Value, where + ".alpha_keys"))
                        {
                            var at = $"{where}.alpha_keys[{i}]";
                            alphas.Add(new GradientAlphaKey((float)Number(Field(key, "alpha", at), at + ".alpha"), (float)Number(Field(key, "t", at), at + ".t")));
                        }
                        break;
                    default:
                        throw EditException.Unknown("Gradient field", pair.Key, valid);
                }
            }
            if (colors.Count == 0)
                colors.Add(new GradientColorKey(Color.white, 0f));
            if (alphas.Count == 0)
                alphas.Add(new GradientAlphaKey(1f, 0f));
            if (colors.Count > 8 || alphas.Count > 8)
                throw new EditException($"{where}: a Gradient has at most 8 colour keys and 8 alpha keys");
            gradient.SetKeys(colors.ToArray(), alphas.ToArray());
            return gradient;
        }

        static AnimationCurve CurveFrom(object json, string where)
        {
            // Shorthand: [[t, v], ...] with linear tangents.
            if (json is IList points && !(json is string))
            {
                var keys = new List<Keyframe>();
                for (int i = 0; i < points.Count; i++)
                {
                    var p = Floats(points[i], 2, 2, $"{where}[{i}]");
                    keys.Add(new Keyframe(p[0], p[1]));
                }
                for (int i = 0; i < keys.Count; i++)
                {
                    var key = keys[i];
                    float inSlope = i > 0 ? Slope(keys[i - 1], key) : (i + 1 < keys.Count ? Slope(key, keys[i + 1]) : 0f);
                    float outSlope = i + 1 < keys.Count ? Slope(key, keys[i + 1]) : inSlope;
                    key.inTangent = inSlope;
                    key.outTangent = outSlope;
                    keys[i] = key;
                }
                return new AnimationCurve(keys.ToArray());
            }

            if (!(json is IEnumerable<KeyValuePair<string, object>> map))
                throw new EditException($"{where}: an AnimationCurve is written as [[t, v], ...] or {{\"keys\": [{{\"time\", \"value\", \"in_tangent\", \"out_tangent\"}}]}}, got {Show(json)}");
            var curve = new AnimationCurve();
            var frames = new List<Keyframe>();
            foreach (var pair in map)
            {
                switch (pair.Key)
                {
                    case "keys":
                        foreach (var (key, i) in Items(pair.Value, where + ".keys"))
                        {
                            var at = $"{where}.keys[{i}]";
                            if (!(key is IEnumerable<KeyValuePair<string, object>> keyMap))
                                throw new EditException($"{at}: expected an object");
                            var frame = new Keyframe();
                            foreach (var field in keyMap)
                            {
                                switch (field.Key)
                                {
                                    case "time": frame.time = (float)Number(field.Value, at + ".time"); break;
                                    case "value": frame.value = (float)Number(field.Value, at + ".value"); break;
                                    case "in_tangent": frame.inTangent = (float)Number(field.Value, at + ".in_tangent"); break;
                                    case "out_tangent": frame.outTangent = (float)Number(field.Value, at + ".out_tangent"); break;
                                    case "in_weight": frame.inWeight = (float)Number(field.Value, at + ".in_weight"); break;
                                    case "out_weight": frame.outWeight = (float)Number(field.Value, at + ".out_weight"); break;
                                    case "weighted_mode": frame.weightedMode = (WeightedMode)ParseEnum(typeof(WeightedMode), field.Value, at + ".weighted_mode"); break;
                                    default:
                                        throw EditException.Unknown("Keyframe field", field.Key,
                                            new[] { "time", "value", "in_tangent", "out_tangent", "in_weight", "out_weight", "weighted_mode" });
                                }
                            }
                            frames.Add(frame);
                        }
                        break;
                    case "pre_wrap":
                        curve.preWrapMode = (WrapMode)ParseEnum(typeof(WrapMode), pair.Value, where + ".pre_wrap");
                        break;
                    case "post_wrap":
                        curve.postWrapMode = (WrapMode)ParseEnum(typeof(WrapMode), pair.Value, where + ".post_wrap");
                        break;
                    default:
                        throw EditException.Unknown("AnimationCurve field", pair.Key, new[] { "keys", "pre_wrap", "post_wrap" });
                }
            }
            curve.keys = frames.ToArray();
            return curve;
        }

        static float Slope(Keyframe a, Keyframe b) => Mathf.Approximately(b.time, a.time) ? 0f : (b.value - a.value) / (b.time - a.time);

        static IEnumerable<(object item, int index)> Items(object json, string where)
        {
            if (!(json is IList list) || json is string)
                throw new EditException($"{where}: expected an array, got {Show(json)}");
            for (int i = 0; i < list.Count; i++)
                yield return (list[i], i);
        }

        static object Field(object json, string key, string where)
        {
            if (!(json is IEnumerable<KeyValuePair<string, object>> map))
                throw new EditException($"{where}: expected an object, got {Show(json)}");
            foreach (var pair in map)
                if (pair.Key == key)
                    return pair.Value;
            throw new EditException($"{where}: missing '{key}'");
        }

        // ------------------------------------------------------------------ the edit session

        /// <summary>
        /// One edit of a graph asset: nodes are created and addressed by recipe-local ids, slots by
        /// <c>&lt;id&gt;.&lt;slot path&gt;</c>. Call <see cref="Save"/> to write the asset, then
        /// <see cref="CompileReport"/> to compile it.
        /// </summary>
        /// <remarks>
        /// Ids are case-sensitive. An existing graph opened with <see cref="Open"/> starts with the ids
        /// <c>describe</c> prints (<c>c0</c>, <c>c0.b1</c>, <c>o2</c>, <c>p0</c>); a new id may not reuse one.
        /// Slot paths: master slot name, then child slot names, joined with dots
        /// (<c>init.bounds.center</c>); a slot without a name is <c>#&lt;index&gt;</c>; <c>out</c> is the
        /// only output of a node that has exactly one (a parameter's <c>o</c>, an operator's <c>#0</c>).
        /// </remarks>
        public sealed class GraphEdit
        {
            const float ContextColumnX = 0f;
            const float OperatorColumnX = -520f;
            const float NoteColumnX = 480f;

            readonly VisualEffectResource m_Resource;
            readonly VFXGraph m_Graph;
            readonly Dictionary<string, VFXModel> m_Nodes = new Dictionary<string, VFXModel>(StringComparer.Ordinal);
            readonly List<VFXModel> m_Added = new List<VFXModel>();

            GraphEdit(string assetPath, VisualEffectResource resource, VFXGraph graph)
            {
                AssetPath = assetPath;
                m_Resource = resource;
                m_Graph = graph;
            }

            /// <summary>The asset this edit writes.</summary>
            public string AssetPath { get; }

            /// <summary>The ids known to this edit, ordered.</summary>
            public IReadOnlyList<string> Ids => m_Nodes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

            /// <summary>True when the id names a node.</summary>
            public bool Has(string id) => id != null && m_Nodes.ContainsKey(id);

            /// <summary>
            /// Creates an empty graph asset at <paramref name="assetPath"/> (under <c>Assets/</c>) with
            /// VFX Graph's own <c>VisualEffectAssetEditorUtility.CreateNewAsset</c>; an asset already there
            /// is deleted first.
            /// </summary>
            public static GraphEdit Create(string assetPath)
            {
                RequireSupportedVersion();
                if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase))
                    throw new EditException($"a new graph is created under Assets/ with the extension .vfx, not at '{assetPath}'");
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath, AssetPathToGUIDOptions.OnlyExistingAssets)))
                    AssetDatabase.DeleteAsset(assetPath);
                VisualEffectAssetEditorUtility.CreateNewAsset(assetPath);
                var resource = VisualEffectResource.GetResourceAtPath(assetPath);
                if (resource == null)
                    throw new EditException($"VFX Graph did not create a resource at '{assetPath}'");
                var graph = resource.GetGraph() ?? resource.CreateGraph();
                return new GraphEdit(assetPath, resource, graph);
            }

            /// <summary>Opens an existing <c>.vfx</c> asset; its nodes get the ids <c>describe</c> prints.</summary>
            public static GraphEdit Open(string assetPath)
            {
                RequireSupportedVersion();
                var resource = VisualEffectResource.GetResourceAtPath(assetPath);
                if (resource == null)
                    throw new EditException($"no VFX Graph resource at '{assetPath}'");
                if (resource.isSubgraph)
                    throw new EditException($"'{assetPath}' is a subgraph; apply edits .vfx graphs only");
                var graph = resource.GetGraph();
                if (graph == null)
                    throw new EditException($"the resource at '{assetPath}' has no graph");
                var edit = new GraphEdit(assetPath, resource, graph);
                foreach (var pair in AssignIds(graph, out _, out _, out _, out _))
                    edit.m_Nodes[pair.Value] = pair.Key;
                return edit;
            }

            // -------------------------------------------------------------- nodes

            /// <summary>Adds a context by catalog type name and optional variant id.</summary>
            public void AddContext(string id, string type, string variant, string label)
            {
                CheckNewId(id);
                var model = CreateLibraryModel(NodeKind.Context, type, variant);
                var context = model as VFXContext ?? throw new EditException($"'{type}' is not a context");
                if (!string.IsNullOrEmpty(label))
                    context.label = label;
                m_Graph.AddChild(context);
                Register(id, context);
            }

            /// <summary>Adds a block at the end of a context.</summary>
            public void AddBlock(string contextId, string id, string type, string variant)
            {
                CheckNewId(id);
                var context = Node<VFXContext>(contextId, "context");
                var model = CreateLibraryModel(NodeKind.Block, type, variant);
                var block = model as VFXBlock ?? throw new EditException($"'{type}' is not a block");
                AttachBlock(context, contextId, block, type);
                Register(id, block);
            }

            /// <summary>Adds an operator.</summary>
            public void AddOperator(string id, string type, string variant)
            {
                CheckNewId(id);
                var model = CreateLibraryModel(NodeKind.Operator, type, variant);
                m_Graph.AddChild(model);
                Register(id, model);
            }

            /// <summary>
            /// Adds a parameter of a catalog parameter type (its <c>name</c>, like <c>Color</c> or
            /// <c>Vector 3</c>, or its <c>type</c>, like <c>Vector3</c>) with an exposed name and a value.
            /// </summary>
            public void AddParameter(string id, string type, string exposedName, object value, bool exposed, string category, string tooltip)
            {
                CheckNewId(id);
                if (string.IsNullOrEmpty(exposedName))
                    throw new EditException("a parameter needs a name");
                var descriptors = VFXLibrary.GetParameters().ToList();
                var descriptor = descriptors.FirstOrDefault(d => d.name == type)
                                 ?? descriptors.FirstOrDefault(d => d.modelType != null && (ValueTypeName(d.modelType) == type || d.modelType.FullName == type));
                if (descriptor == null)
                    throw EditException.Unknown("parameter type", type, descriptors.Select(d => d.name).Concat(descriptors.Where(d => d.modelType != null).Select(d => ValueTypeName(d.modelType))));

                var duplicate = m_Graph.children.OfType<VFXParameter>().FirstOrDefault(p => p.exposedName == exposedName);
                if (duplicate != null)
                    throw new EditException($"a parameter named '{exposedName}' already exists");

                var parameter = (VFXParameter)descriptor.CreateInstance();
                m_Graph.AddChild(parameter);
                parameter.collapsed = true;
                parameter.order = m_Graph.children.OfType<VFXParameter>().Count() - 1;
                parameter.SetSettingValue("m_ExposedName", exposedName);
                parameter.SetSettingValue("m_Exposed", exposed);
                if (!string.IsNullOrEmpty(category))
                    parameter.category = category;
                if (!string.IsNullOrEmpty(tooltip))
                    parameter.tooltip = tooltip;
                if (value != null || typeof(UnityEngine.Object).IsAssignableFrom(parameter.type))
                    parameter.value = ConvertTo(parameter.type, value, parameter.value, id + ".value");
                Register(id, parameter);
            }

            /// <summary>
            /// Adds a subgraph node: <c>.vfx</c> gives a subgraph context, <c>.vfxoperator</c> an operator,
            /// <c>.vfxblock</c> a block in <paramref name="contextId"/>.
            /// </summary>
            public void UseSubgraph(string id, string subgraphPath, string contextId)
            {
                CheckNewId(id);
                if (string.IsNullOrEmpty(subgraphPath))
                    throw new EditException("use_subgraph needs an asset path");
                var path = subgraphPath.Replace('\\', '/');
                var extension = Path.GetExtension(path).ToLowerInvariant();
                switch (extension)
                {
                    case ".vfxoperator":
                    {
                        if (!string.IsNullOrEmpty(contextId))
                            throw new EditException("a subgraph operator is not placed in a context; remove 'context'");
                        var asset = (VisualEffectSubgraphOperator)LoadAsset(typeof(VisualEffectSubgraphOperator), path, id + ".asset");
                        var op = ScriptableObject.CreateInstance<VFXSubgraphOperator>();
                        m_Graph.AddChild(op);
                        op.SetSettingValue("m_Subgraph", asset);
                        Register(id, op);
                        break;
                    }
                    case ".vfxblock":
                    {
                        if (string.IsNullOrEmpty(contextId))
                            throw new EditException("a subgraph block needs 'context'");
                        var context = Node<VFXContext>(contextId, "context");
                        var asset = (VisualEffectSubgraphBlock)LoadAsset(typeof(VisualEffectSubgraphBlock), path, id + ".asset");
                        var block = ScriptableObject.CreateInstance<VFXSubgraphBlock>();
                        block.SetSettingValue("m_Subgraph", asset);
                        AttachBlock(context, contextId, block, nameof(VFXSubgraphBlock));
                        Register(id, block);
                        break;
                    }
                    case ".vfx":
                    {
                        if (!string.IsNullOrEmpty(contextId))
                            throw new EditException("a subgraph context is not placed in a context; remove 'context'");
                        var asset = (VisualEffectAsset)LoadAsset(typeof(VisualEffectAsset), path, id + ".asset");
                        var context = ScriptableObject.CreateInstance<VFXSubgraphContext>();
                        m_Graph.AddChild(context);
                        context.SetSettingValue("m_Subgraph", asset);
                        Register(id, context);
                        break;
                    }
                    default:
                        throw EditException.Unknown("subgraph extension", extension, new[] { ".vfx", ".vfxoperator", ".vfxblock" });
                }
            }

            /// <summary>Adds a sticky note.</summary>
            public void AddComment(string title, string text)
            {
                var ui = m_Graph.UIInfos;
                var existing = ui.stickyNoteInfos ?? Array.Empty<VFXUI.StickyNoteInfo>();
                var note = new VFXUI.StickyNoteInfo
                {
                    title = title ?? string.Empty,
                    contents = text ?? string.Empty,
                    position = new Rect(NoteColumnX, 100f + 220f * existing.Length, 320f, 180f),
                    theme = "Classic",
                    textSize = "Small",
                };
                ui.stickyNoteInfos = existing.Concat(new[] { note }).ToArray();
                m_Graph.Invalidate(VFXModel.InvalidationCause.kUIChanged);
            }

            void AttachBlock(VFXContext context, string contextId, VFXBlock block, string type)
            {
                if (!context.AcceptChild(block))
                {
                    throw new EditException(
                        $"block '{type}' (compatible contexts: {block.compatibleContexts}, data: {block.compatibleData}) cannot be added to context '{contextId}' " +
                        $"({context.GetType().Name}, context type {context.contextType}, data {context.ownedType})");
                }
                context.AddChild(block);
            }

            void CheckNewId(string id)
            {
                if (string.IsNullOrEmpty(id))
                    throw new EditException("the node needs an id");
                if (m_Nodes.ContainsKey(id))
                    throw new EditException($"id '{id}' is already used");
            }

            void Register(string id, VFXModel model)
            {
                m_Nodes[id] = model;
                m_Added.Add(model);
            }

            T Node<T>(string id, string what) where T : class
            {
                if (string.IsNullOrEmpty(id) || !m_Nodes.TryGetValue(id, out var model))
                    throw EditException.Unknown("id", id ?? string.Empty, m_Nodes.Keys);
                return model as T ?? throw new EditException($"'{id}' is a {KindOf(model)}, not a {what}");
            }

            static string KindOf(VFXModel model)
            {
                switch (model)
                {
                    case VFXContext _: return "context";
                    case VFXBlock _: return "block";
                    case VFXParameter _: return "parameter";
                    case VFXOperator _: return "operator";
                    default: return model.GetType().Name;
                }
            }

            /// <summary>"context", "block", "operator" or "parameter" for an id.</summary>
            public string Kind(string id) => KindOf(Node<VFXModel>(id, "node"));

            /// <summary>The input slot paths of a node (master slots and their members).</summary>
            public IReadOnlyList<string> InputSlots(string id) => SlotPaths(Container(id).inputSlots);

            /// <summary>The CLR type name of an input slot (<c>Texture2D</c>, <c>Gradient</c>), or null.</summary>
            public string InputSlotType(string address)
            {
                try
                {
                    return ValueTypeName(ResolveSlot(address, true).property.type);
                }
                catch (EditException)
                {
                    return null;
                }
            }

            IVFXSlotContainer Container(string id)
            {
                var model = Node<VFXModel>(id, "node");
                return model as IVFXSlotContainer ?? throw new EditException($"'{id}' has no slots");
            }

            // -------------------------------------------------------------- settings and slots

            /// <summary>The user-editable setting names of a node (as <c>catalog</c> lists them).</summary>
            public IReadOnlyList<string> SettingNames(string id) => UserSettings(Node<VFXModel>(id, "node")).Select(s => s.field.Name).ToList();

            /// <summary>Sets a setting by name; the value is converted to the setting's type.</summary>
            public void SetSetting(string id, string name, object value)
            {
                var model = Node<VFXModel>(id, "node");
                var settings = UserSettings(model);
                var match = settings.FirstOrDefault(s => s.field.Name == name);
                if (match.field == null)
                    throw EditException.Unknown($"setting of {model.GetType().Name}", name ?? string.Empty, settings.Select(s => s.field.Name));
                if ((match.attribute.visibleFlags & VFXSettingAttribute.VisibleFlags.ReadOnly) != 0)
                    throw new EditException($"setting '{name}' of '{id}' is read-only");

                if (match.field.FieldType.IsEnum && value is string s)
                {
                    IEnumerable<int> filtered = null;
                    try
                    {
                        filtered = (match.instance as VFXModel ?? model).GetFilteredOutEnumerators(name);
                    }
                    catch (Exception)
                    {
                        // A model that cannot filter its enumerators accepts them all.
                    }
                    var parsed = ParseEnum(match.field.FieldType, s, $"{id}.{name}");
                    if (filtered != null && filtered.Contains(Convert.ToInt32(parsed, CultureInfo.InvariantCulture)))
                    {
                        var allowed = Enum.GetValues(match.field.FieldType).Cast<object>()
                            .Where(v => !filtered.Contains(Convert.ToInt32(v, CultureInfo.InvariantCulture)))
                            .Select(v => v.ToString());
                        throw EditException.Unknown($"value of {name} for this node", s, allowed);
                    }
                }

                var converted = ConvertTo(match.field.FieldType, value, SafeGet(match.field, match.instance), $"{id}.{name}");
                try
                {
                    model.SetSettingValue(name, converted);
                }
                catch (ArgumentException exception)
                {
                    throw new EditException($"{id}.{name}: {exception.Message}");
                }
            }

            /// <summary>Sets the value (and optionally the space: None, Local, World) of an input slot, or the value of a parameter.</summary>
            public void SetSlot(string address, object value, bool hasValue, string space)
            {
                var slot = ResolveSlot(address, true);
                if (hasValue)
                {
                    slot.value = ConvertTo(slot.property.type, value, SafeSlotValue(slot), address);
                }
                if (!string.IsNullOrEmpty(space))
                {
                    if (!slot.spaceable)
                        throw new EditException($"{address}: the slot has no space");
                    slot.space = (VFXSpace)ParseEnum(typeof(VFXSpace), space, address + ".space");
                }
            }

            static object SafeSlotValue(VFXSlot slot)
            {
                try
                {
                    return slot.value;
                }
                catch (Exception)
                {
                    return null;
                }
            }

            /// <summary>Links an output slot to an input slot.</summary>
            public void LinkSlots(string from, string to)
            {
                var output = ResolveSlot(from, false);
                var input = ResolveSlot(to, true);
                if (!input.Link(output))
                {
                    throw new EditException(
                        $"cannot link {from} ({ValueTypeName(output.property.type)}) to {to} ({ValueTypeName(input.property.type)}): " +
                        "the types do not convert, or the link would make a cycle");
                }
            }

            /// <summary>Links the flow of two contexts (output flow slot <paramref name="fromSlot"/> to input flow slot <paramref name="toSlot"/>).</summary>
            public void LinkFlow(string from, string to, int fromSlot, int toSlot)
            {
                var source = Node<VFXContext>(from, "context");
                var target = Node<VFXContext>(to, "context");
                if (!VFXContext.CanLink(source, target, fromSlot, toSlot))
                {
                    throw new EditException(
                        $"cannot link the flow {from}[{fromSlot}] -> {to}[{toSlot}]: {from} ({source.GetType().Name}) gives {source.outputType} on " +
                        $"{source.outputFlowSlot.Length} flow output(s), {to} ({target.GetType().Name}) takes {target.inputType} on " +
                        $"{target.inputFlowSlot.Length} flow input(s), or the link exists already");
                }
                source.LinkTo(target, fromSlot, toSlot);
            }

            VFXSlot ResolveSlot(string address, bool input)
            {
                if (string.IsNullOrEmpty(address))
                    throw new EditException("the slot address is empty (expected <id>.<slot>)");

                string id = null;
                foreach (var key in m_Nodes.Keys)
                {
                    if (address.Length > key.Length && address[key.Length] == '.' && address.StartsWith(key, StringComparison.Ordinal) && (id == null || key.Length > id.Length))
                        id = key;
                }
                if (id == null)
                {
                    var head = address.Contains('.') ? address.Substring(0, address.IndexOf('.')) : address;
                    if (!address.Contains('.') && m_Nodes.ContainsKey(address))
                        throw new EditException($"'{address}' is a node, not a slot; address a slot as <id>.<slot>");
                    throw EditException.Unknown("id", head, m_Nodes.Keys);
                }

                var model = m_Nodes[id];
                var container = model as IVFXSlotContainer ?? throw new EditException($"'{id}' has no slots");
                var parameterValue = input && model is VFXParameter parameter && !parameter.isOutput;
                var useInputs = input && !parameterValue;
                var masters = useInputs ? container.inputSlots : container.outputSlots;
                var direction = useInputs ? "input" : "output";
                var path = address.Substring(id.Length + 1);
                var segments = path.Split('.');

                VFXSlot slot = masters.FirstOrDefault(s => s.name == segments[0]);
                if (slot == null && segments[0].StartsWith("#", StringComparison.Ordinal) &&
                    int.TryParse(segments[0].Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index < masters.Count &&
                    string.IsNullOrEmpty(masters[index].name))
                    slot = masters[index];
                if (slot == null && segments[0] == "out" && !useInputs && masters.Count == 1)
                    slot = masters[0];
                if (slot == null)
                {
                    var valid = SlotPaths(masters);
                    if (!useInputs && masters.Count == 1)
                        valid = valid.Concat(new[] { "out" }).ToList();
                    throw EditException.Unknown($"{direction} slot of '{id}' ({model.GetType().Name})", path, valid);
                }

                for (int i = 1; i < segments.Length; i++)
                {
                    var children = slot.children.ToList();
                    VFXSlot child = children.FirstOrDefault(c => c.name == segments[i]);
                    if (child == null && segments[i].StartsWith("#", StringComparison.Ordinal) &&
                        int.TryParse(segments[i].Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out var childIndex) && childIndex < children.Count)
                        child = children[childIndex];
                    if (child == null)
                        throw EditException.Unknown($"{direction} slot of '{id}' ({model.GetType().Name})", path, SlotPaths(masters));
                    slot = child;
                }
                return slot;
            }

            static List<string> SlotPaths(IEnumerable<VFXSlot> masters)
            {
                var list = new List<string>();
                foreach (var master in masters)
                    AddPaths(list, master, SlotRef(master));
                return list;
            }

            static void AddPaths(List<string> list, VFXSlot slot, string path)
            {
                list.Add(path);
                int index = 0;
                foreach (var child in slot.children)
                {
                    var name = string.IsNullOrEmpty(child.name) ? "#" + index : child.name;
                    AddPaths(list, child, path + "." + name);
                    index++;
                }
            }

            // -------------------------------------------------------------- save

            /// <summary>
            /// Places the new nodes, gives each parameter its graph node, and writes the asset with
            /// <c>VisualEffectResource.WriteAsset</c>. The asset is not imported here.
            /// </summary>
            public void Save()
            {
                Layout();
                foreach (var parameter in m_Graph.children.OfType<VFXParameter>().ToList())
                {
                    if (parameter.nodes.Count == 0)
                        parameter.CreateDefaultNode(parameter.position);
                    else
                        parameter.ValidateNodes();
                }
                m_Resource.WriteAssetWithSubAssets();
            }

            void Layout()
            {
                var existing = m_Graph.children.Where(c => !m_Added.Contains(c)).ToList();
                float contextY = existing.OfType<VFXContext>().Select(c => c.position.y + 400f).DefaultIfEmpty(0f).Max();
                float operatorY = existing.Where(c => !(c is VFXContext)).Select(c => c.position.y + 160f).DefaultIfEmpty(100f).Max();
                foreach (var model in m_Added)
                {
                    switch (model)
                    {
                        case VFXContext context:
                            context.position = new Vector2(ContextColumnX, contextY);
                            contextY += 220f + 110f * context.GetNbChildren();
                            break;
                        case VFXBlock _:
                            break;
                        default:
                            model.position = new Vector2(OperatorColumnX, operatorY);
                            operatorY += 160f;
                            break;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ compile

        /// <summary>
        /// Compiles a saved graph asset and reports what went wrong. Returns a map with <c>compiled</c>
        /// (bool), <c>errors</c> and <c>warnings</c> (strings), and <c>systems</c> (id, name, data type,
        /// capacity per system, as <c>describe</c> lists them).
        /// </summary>
        /// <remarks>
        /// Three sources: the in-memory compile of the graph (<c>VFXGraph.Compile</c>, the same call the
        /// graph window makes) with the errors VFX Graph registers per model; the asset import (the
        /// importer compiles again and generates the shaders), with every error logged meanwhile; and the
        /// generated shaders and compute shaders, which must have no error message.
        /// </remarks>
        public static Map CompileReport(string assetPath)
        {
            RequireSupportedVersion();
            var errors = new List<string>();
            var warnings = new List<string>();
            var logged = new List<string>();

            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    logged.Add(condition);
            }

            bool compiled = false;
            Application.logMessageReceived += OnLog;
            try
            {
                var resource = VisualEffectResource.GetResourceAtPath(assetPath);
                var graph = resource?.GetGraph();
                if (graph == null)
                {
                    errors.Add($"no VFX Graph at '{assetPath}'");
                }
                else
                {
                    graph.PrepareGraph();
                    graph.errorManager.RefreshCompilationReport();
                    var output = graph.Compile();
                    compiled = output.success;
                    if (!compiled)
                        errors.Add("VFX Graph could not compile the graph");
                    CollectModelErrors(graph, errors, warnings);
                }

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                CollectShaderErrors(assetPath, errors, warnings);
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            foreach (var line in logged.Distinct())
                errors.Add("log: " + FirstLines(line, 6));

            var systems = errors.Count == 0 ? Systems(assetPath) : new List<object>();
            return new Map
            {
                { "compiled", compiled && errors.Count == 0 },
                { "errors", errors.Select(e => (object)e).ToList() },
                { "warnings", warnings.Select(w => (object)w).ToList() },
                { "systems", systems },
            };
        }

        /// <summary>The systems of a graph asset as <c>describe</c> lists them: id, name, data type and capacity.</summary>
        public static List<object> Systems(string assetPath)
        {
            var systems = new List<object>();
            var description = Describe(assetPath, new List<string>());
            foreach (var system in (List<object>)description.Get("systems"))
            {
                var map = (Map)system;
                systems.Add(new Map
                {
                    { "id", map.Get("id") },
                    { "name", map.Get("name") },
                    { "data_type", map.Get("data_type") },
                    { "capacity", map.Get("capacity") },
                });
            }
            return systems;
        }

        static string FirstLines(string text, int count)
        {
            var lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            return string.Join(" | ", lines.Take(count).Where(l => l.Length > 0));
        }

        static void CollectModelErrors(VFXGraph graph, List<string> errors, List<string> warnings)
        {
            var ids = AssignIds(graph, out _, out _, out _, out _);
            var reporter = new VFXErrorReporter(VFXErrorOrigin.Invalidate);
            var compileReporter = graph.errorManager.compileReporter;
            foreach (var pair in ids.OrderBy(p => p.Value, StringComparer.Ordinal))
            {
                var model = pair.Key;
                try
                {
                    model.GenerateErrors(reporter);
                }
                catch (Exception exception)
                {
                    warnings.Add($"{pair.Value} ({model.GetType().Name}): its checks threw {exception.GetType().Name}: {exception.Message}");
                }

                var reports = reporter.GetDirtyModelErrors(model).ToList();
                if (compileReporter != null)
                    reports.AddRange(compileReporter.GetDirtyModelErrors(model));
                foreach (var report in reports)
                {
                    var line = $"{pair.Value} ({model.GetType().Name}): {report.description ?? report.error}";
                    if (report.type == VFXErrorType.Error)
                        errors.Add(line);
                    else
                        warnings.Add(line);
                }
            }
        }

        static void CollectShaderErrors(string assetPath, List<string> errors, List<string> warnings)
        {
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                switch (asset)
                {
                    case Shader shader:
                        foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        {
                            var line = $"shader {shader.name}: {message.message} (line {message.line})";
                            if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                                errors.Add(line);
                            else
                                warnings.Add(line);
                        }
                        break;
                    case ComputeShader compute:
                        foreach (var message in ShaderUtil.GetComputeShaderMessages(compute))
                        {
                            var line = $"compute shader {compute.name}: {message.message} (line {message.line})";
                            if (message.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error)
                                errors.Add(line);
                            else
                                warnings.Add(line);
                        }
                        break;
                }
            }
        }
    }
}
