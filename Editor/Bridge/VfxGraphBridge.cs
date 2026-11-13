using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.VFX;
using UnityEngine;
using UnityEngine.VFX;

namespace Cuvara.VFX.Editor.Bridge
{
    /// <summary>
    /// The only door into the internals of <c>Unity.VisualEffectGraph.Editor</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This assembly is named <c>Unity.Testing.VisualEffectGraph.Editor</c> because that name is in the
    /// <c>InternalsVisibleTo</c> list of <c>com.unity.visualeffectgraph/Editor/PackageInfo.cs</c>, so it
    /// compiles against <c>VFXGraph</c>, <c>VFXContext</c>, <c>VFXBlock</c>, <c>VFXOperator</c>,
    /// <c>VFXSlot</c>, <c>VFXParameter</c> and <c>VFXLibrary</c>. An upstream change of those types breaks
    /// the compile of this file instead of failing silently at run time.
    /// </para>
    /// <para>
    /// The public API exposes no Unity-internal type: only strings, booleans, numbers, arrays and the
    /// nested <see cref="Map"/> (an ordered list of key/value pairs) holding the same plain values,
    /// <c>List&lt;object&gt;</c>, and asset references as maps of path, GUID and file id. Callers
    /// (the <c>Cuvara.VFX.Editor</c> verbs) turn those trees into JSON.
    /// </para>
    /// <para>
    /// Every entry point calls <see cref="RequireSupportedVersion"/> first: on another major.minor of the
    /// VFX Graph package it throws with a message that names this file.
    /// </para>
    /// </remarks>
    public static partial class VfxGraphBridge
    {
        /// <summary>The VFX Graph package version this file was written and verified against.</summary>
        public const string WrittenForVersion = "17.6.0";

        /// <summary>The major.minor that <see cref="IsSupportedVersion"/> accepts.</summary>
        public const string WrittenForMajorMinor = "17.6";

        /// <summary>Package name of VFX Graph.</summary>
        public const string PackageName = "com.unity.visualeffectgraph";

        const int MaxValueDepth = 8;

        /// <summary>An ordered map of plain values; the order is the order of insertion.</summary>
        public sealed class Map : List<KeyValuePair<string, object>>
        {
            /// <summary>Appends a key and its value.</summary>
            public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));

            /// <summary>The value of the first entry with that key, or null.</summary>
            public object Get(string key)
            {
                foreach (var pair in this)
                    if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                        return pair.Value;
                return null;
            }
        }

        // ------------------------------------------------------------------ version guard

        /// <summary>The installed VFX Graph package version, for example <c>17.6.0</c>, or null.</summary>
        public static string InstalledVersion
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VFXGraph).Assembly);
                return info?.version;
            }
        }

        /// <summary>True when the installed VFX Graph has the major.minor this bridge was written for.</summary>
        public static bool IsSupportedVersion(out string message)
        {
            var installed = InstalledVersion;
            if (string.IsNullOrEmpty(installed))
            {
                message = $"VfxGraphBridge: cannot read the version of {PackageName} (no package info for the assembly of VFXGraph).";
                return false;
            }

            if (MajorMinor(installed) != WrittenForMajorMinor)
            {
                message = $"VfxGraphBridge was written for {PackageName} {WrittenForMajorMinor}.x ({WrittenForVersion}); the project has {installed}. " +
                          "Verify every internal type and member used in Editor/Bridge/VfxGraphBridge.cs against the installed package source, " +
                          "then update WrittenForVersion.";
                return false;
            }

            message = $"{PackageName} {installed} matches {WrittenForMajorMinor}.x";
            return true;
        }

        /// <summary>Throws <see cref="InvalidOperationException"/> when <see cref="IsSupportedVersion"/> is false.</summary>
        public static void RequireSupportedVersion()
        {
            if (!IsSupportedVersion(out var message))
                throw new InvalidOperationException(message);
        }

        /// <summary>"17.6" for "17.6.0" or "17.6.0-pre.1".</summary>
        public static string MajorMinor(string version)
        {
            if (string.IsNullOrEmpty(version))
                return string.Empty;
            var parts = version.Split('.', '-', '+');
            return parts.Length >= 2 ? parts[0] + "." + parts[1] : version;
        }

        // ------------------------------------------------------------------ catalog

        /// <summary>
        /// What the VFX Graph library offers: <c>contexts</c>, <c>blocks</c>, <c>operators</c> and
        /// <c>parameter_types</c>, each a list of maps, sorted by type name and then variant id.
        /// </summary>
        /// <param name="filter">Case-insensitive substring of a type name, variant name or category; null or empty for all.</param>
        /// <param name="includeSubVariants">List the sub-variants of each entry (name and settings).</param>
        /// <param name="warnings">Receives one line for each library entry that could not be read.</param>
        /// <remarks>
        /// Experimental entries are never listed, whatever the editor preference "Experimental Operators/Blocks"
        /// says, so the output does not depend on a user preference.
        /// </remarks>
        public static Map Catalog(string filter, bool includeSubVariants, List<string> warnings)
        {
            RequireSupportedVersion();
            warnings ??= new List<string>();

            var catalog = new Map
            {
                { "contexts", CatalogModels(VFXLibrary.GetContexts(), "context", filter, includeSubVariants, warnings) },
                { "blocks", CatalogModels(VFXLibrary.GetBlocks(), "block", filter, includeSubVariants, warnings) },
                { "operators", CatalogModels(VFXLibrary.GetOperators(), "operator", filter, includeSubVariants, warnings) },
                { "parameter_types", CatalogParameters(filter, warnings) },
            };
            return catalog;
        }

        static List<object> CatalogModels<T>(IEnumerable<VFXModelDescriptor<T>> descriptors, string kind, string filter,
            bool includeSubVariants, List<string> warnings) where T : VFXModel
        {
            var rows = new List<(string sortType, string sortVariant, Map entry)>();
            foreach (var descriptor in descriptors)
            {
                if (descriptor == null || descriptor.infoAttribute == null || descriptor.infoAttribute.experimental)
                    continue;
                var variant = descriptor.variant;
                var modelType = descriptor.modelType;
                if (!Matches(filter, modelType.Name, modelType.FullName, descriptor.name, Title(descriptor.name), descriptor.category))
                    continue;

                var variantId = variant.GetUniqueIdentifier();
                try
                {
                    var model = descriptor.unTypedModel;
                    var entry = new Map
                    {
                        { "kind", kind },
                        { "type", modelType.Name },
                        { "type_full", modelType.FullName },
                        { "name", descriptor.name },
                        { "title", Title(descriptor.name) },
                        { "category", descriptor.category ?? string.Empty },
                        { "variant", new Map { { "id", variantId }, { "settings", SettingPairs(variant.settings) } } },
                        { "settings", CatalogSettings(model) },
                    };

                    if (model is IVFXSlotContainer container)
                    {
                        entry.Add("inputs", CatalogSlots(container.inputSlots));
                        entry.Add("outputs", CatalogSlots(container.outputSlots));
                    }
                    else
                    {
                        entry.Add("inputs", new List<object>());
                        entry.Add("outputs", new List<object>());
                    }

                    if (model is VFXContext context)
                    {
                        entry.Add("context_type", context.contextType.ToString());
                        entry.Add("input_data", context.inputType.ToString());
                        entry.Add("output_data", context.outputType.ToString());
                        entry.Add("flow_inputs", context.inputFlowSlot.Length);
                        entry.Add("flow_outputs", context.outputFlowSlot.Length);
                    }
                    else if (model is VFXBlock block)
                    {
                        entry.Add("compatible_contexts", block.compatibleContexts.ToString());
                        entry.Add("compatible_data", block.compatibleData.ToString());
                    }

                    var subVariants = descriptor.subVariantDescriptors ?? Array.Empty<IVFXModelDescriptor>();
                    entry.Add("sub_variant_count", subVariants.Length);
                    if (includeSubVariants)
                    {
                        var subs = new List<(string id, Map map)>();
                        foreach (var sub in subVariants)
                        {
                            var subId = sub.variant.GetUniqueIdentifier();
                            subs.Add((subId, new Map
                            {
                                { "id", subId },
                                { "name", sub.name },
                                { "settings", SettingPairs(sub.variant.settings) },
                            }));
                        }
                        entry.Add("sub_variants", subs.OrderBy(s => s.id, StringComparer.Ordinal).Select(s => (object)s.map).ToList());
                    }

                    rows.Add((modelType.Name, variantId, entry));
                }
                catch (Exception exception)
                {
                    warnings.Add($"{kind} {modelType.FullName} ({variantId}) could not be read: {exception.GetType().Name}: {exception.Message}");
                }
            }

            return rows
                .OrderBy(r => r.sortType, StringComparer.Ordinal)
                .ThenBy(r => r.sortVariant, StringComparer.Ordinal)
                .Select(r => (object)r.entry)
                .ToList();
        }

        static List<object> CatalogParameters(string filter, List<string> warnings)
        {
            var rows = new List<(string name, Map entry)>();
            foreach (var descriptor in VFXLibrary.GetParameters())
            {
                try
                {
                    var type = descriptor.modelType;
                    var name = descriptor.name;
                    if (!Matches(filter, type?.Name, type?.FullName, name, null))
                        continue;
                    rows.Add((name, new Map
                    {
                        { "kind", "parameter" },
                        { "name", name },
                        { "type", SlotTypeName(type) },
                        { "type_full", type?.FullName },
                    }));
                }
                catch (Exception exception)
                {
                    warnings.Add($"parameter type {descriptor?.name} could not be read: {exception.GetType().Name}: {exception.Message}");
                }
            }

            return rows
                .OrderBy(r => r.name, StringComparer.Ordinal)
                .ThenBy(r => (string)r.entry.Get("type_full") ?? string.Empty, StringComparer.Ordinal)
                .Select(r => (object)r.entry)
                .ToList();
        }

        /// <summary>A library name without the markup of VFXLibraryStringHelper: "|Set|_Age|By Speed" gives "Set Age By Speed".</summary>
        static string Title(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;
            var parts = name.Split('|').Select(p => p.TrimStart('_').Trim()).Where(p => p.Length > 0);
            return string.Join(" ", parts);
        }

        static bool Matches(string filter, params string[] candidates)
        {
            if (string.IsNullOrEmpty(filter))
                return true;
            foreach (var candidate in candidates)
                if (!string.IsNullOrEmpty(candidate) && candidate.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static Map SettingPairs(KeyValuePair<string, object>[] pairs)
        {
            var map = new Map();
            if (pairs == null)
                return map;
            foreach (var pair in pairs.OrderBy(p => p.Key, StringComparer.Ordinal))
                map.Add(pair.Key, ToPlain(pair.Value, 0, null));
            return map;
        }

        static List<object> CatalogSettings(VFXModel model)
        {
            // Which settings the default instance shows. Some models decide that from the data of their
            // parent context (ScreenSpaceSize reads GetData()), which a library instance does not have:
            // then the visibility is unknown (null), not an error.
            HashSet<string> visible = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var setting in model.GetSettings(false, VFXSettingAttribute.VisibleFlags.InInspector))
                    visible.Add(setting.name);
                foreach (var setting in model.GetSettings(false, VFXSettingAttribute.VisibleFlags.InGraph))
                    visible.Add(setting.name);
            }
            catch (Exception)
            {
                visible = null;
            }

            var list = new List<object>();
            foreach (var (field, attribute, instance) in UserSettings(model))
            {
                var entry = new Map
                {
                    { "name", field.Name },
                    { "type", ValueTypeName(field.FieldType) },
                    { "kind", ValueKind(field.FieldType) },
                };
                var allowed = AllowedValues(instance as VFXModel ?? model, field);
                if (allowed != null)
                    entry.Add("allowed_values", allowed);
                entry.Add("default", ToPlain(SafeGet(field, instance), 0, null));
                entry.Add("visible_by_default", visible == null ? (object)null : visible.Contains(field.Name));
                entry.Add("read_only", (attribute.visibleFlags & VFXSettingAttribute.VisibleFlags.ReadOnly) != 0);
                list.Add(entry);
            }
            return list;
        }

        static bool IsUserSetting(VFXSettingAttribute attribute)
        {
            const VFXSettingAttribute.VisibleFlags shown = VFXSettingAttribute.VisibleFlags.InInspector | VFXSettingAttribute.VisibleFlags.InGraph;
            return attribute != null && (attribute.visibleFlags & shown) != 0;
        }

        static List<object> AllowedValues(VFXModel model, FieldInfo field)
        {
            var type = field.FieldType;
            if (type == typeof(bool))
                return new List<object> { false, true };
            if (!type.IsEnum)
                return null;

            HashSet<int> filteredOut = null;
            try
            {
                var filtered = model.GetFilteredOutEnumerators(field.Name);
                if (filtered != null)
                    filteredOut = new HashSet<int>(filtered);
            }
            catch (Exception)
            {
                // A model that cannot filter its enumerators lists them all.
            }

            var values = new List<object>();
            foreach (var value in Enum.GetValues(type))
            {
                if (filteredOut != null && filteredOut.Contains(Convert.ToInt32(value, CultureInfo.InvariantCulture)))
                    continue;
                var name = Enum.GetName(type, value);
                if (!values.Contains(name))
                    values.Add(name);
            }
            return values;
        }

        static List<object> CatalogSlots(IEnumerable<VFXSlot> slots)
        {
            var list = new List<object>();
            foreach (var slot in slots)
            {
                var entry = new Map
                {
                    { "name", slot.name },
                    { "ref", SlotRef(slot) },
                    { "type", SlotTypeName(slot.property.type) },
                    { "type_full", slot.property.type?.FullName },
                    { "direction", slot.direction == VFXSlot.Direction.kInput ? "in" : "out" },
                };
                if (slot.direction == VFXSlot.Direction.kInput)
                    entry.Add("default", SlotValue(slot));
                if (slot.spaceable)
                    entry.Add("space", slot.space.ToString());
                list.Add(entry);
            }
            return list;
        }

        // ------------------------------------------------------------------ describe

        /// <summary>
        /// The graph of a <c>.vfx</c> (or subgraph) asset as plain values with stable ids and ordering.
        /// </summary>
        /// <param name="assetPath">Project-relative path, for example <c>Assets/Effects/Spark.vfx</c>.</param>
        /// <param name="warnings">Receives one line for each node or value that could not be read.</param>
        /// <remarks>
        /// Ids follow the order of the graph's children, which is the serialised order: contexts <c>c0</c>,
        /// <c>c1</c>; their blocks <c>c0.b0</c>; operators <c>o0</c>; parameters <c>p0</c>; any other model
        /// <c>x0</c>; systems <c>s0</c>. Slots are addressed as <c>&lt;id&gt;.&lt;slot path&gt;</c> where the
        /// path joins slot names with dots (<c>c1.b0.Position.x</c>). Two calls on the same asset give the
        /// same tree.
        /// </remarks>
        public static Map Describe(string assetPath, List<string> warnings)
        {
            RequireSupportedVersion();
            warnings ??= new List<string>();
            if (string.IsNullOrEmpty(assetPath))
                throw new ArgumentException("asset is empty");

            var resource = VisualEffectResource.GetResourceAtPath(assetPath);
            if (resource == null)
                throw new ArgumentException($"no VFX Graph resource at '{assetPath}' (expected a .vfx, .vfxoperator or .vfxblock asset inside the project)");
            var graph = resource.GetGraph();
            if (graph == null)
                throw new ArgumentException($"the resource at '{assetPath}' has no graph");

            var ids = AssignIds(graph, out var contexts, out var operators, out var parameters, out var others);

            var systemIds = SystemIds(contexts);
            var systems = new List<object>();
            foreach (var pair in systemIds.OrderBy(p => int.Parse(p.Value.Substring(1), CultureInfo.InvariantCulture)))
                systems.Add(DescribeSystem(pair.Key, pair.Value, contexts, ids, warnings));

            var links = new List<(string to, string from)>();
            var result = new Map
            {
                { "asset", AssetRef(resource.asset != null ? (UnityEngine.Object)resource.asset : resource.subgraph, assetPath) },
                { "asset_kind", resource.asset != null ? "vfx" : resource.subgraph != null ? resource.subgraph.GetType().Name : "unknown" },
                { "vfx_graph", InstalledVersion },
                { "graph_version", graph.version },
            };

            var contextList = new List<object>();
            foreach (var context in contexts)
                contextList.Add(DescribeContext(context, ids, systemIds, links, warnings));
            var operatorList = new List<object>();
            foreach (var op in operators)
                operatorList.Add(DescribeOperator(op, ids, links, warnings));
            var parameterList = new List<object>();
            foreach (var parameter in parameters)
                parameterList.Add(DescribeParameter(parameter, ids, links, warnings));
            var otherList = new List<object>();
            foreach (var other in others)
                otherList.Add(new Map { { "id", ids[other] }, { "type", other.GetType().Name }, { "name", other.name } });

            result.Add("contexts", contextList);
            result.Add("operators", operatorList);
            result.Add("parameters", parameterList);
            result.Add("other_models", otherList);
            result.Add("links", links
                .Distinct()
                .OrderBy(l => l.to, StringComparer.Ordinal)
                .ThenBy(l => l.from, StringComparer.Ordinal)
                .Select(l => (object)new Map { { "from", l.from }, { "to", l.to } })
                .ToList());
            result.Add("flow_links", DescribeFlowLinks(contexts, ids));
            result.Add("systems", systems);
            result.Add("sticky_notes", DescribeStickyNotes(graph));
            result.Add("groups", DescribeGroups(graph, ids));
            return result;
        }

        /// <summary>
        /// The describe ids of a graph, in the order of its children (the serialised order): contexts
        /// <c>c0</c>, their blocks <c>c0.b0</c>, operators <c>o0</c>, parameters <c>p0</c>, other models
        /// <c>x0</c>. <c>apply</c> addresses the nodes of an existing graph with the same ids.
        /// </summary>
        static Dictionary<VFXModel, string> AssignIds(VFXGraph graph, out List<VFXContext> contexts, out List<VFXModel> operators,
            out List<VFXParameter> parameters, out List<VFXModel> others)
        {
            var ids = new Dictionary<VFXModel, string>();
            contexts = new List<VFXContext>();
            operators = new List<VFXModel>();
            parameters = new List<VFXParameter>();
            others = new List<VFXModel>();
            foreach (var child in graph.children)
            {
                switch (child)
                {
                    case VFXContext context:
                        ids[context] = "c" + contexts.Count;
                        contexts.Add(context);
                        int blockIndex = 0;
                        foreach (var block in context.children)
                            ids[block] = ids[context] + ".b" + blockIndex++;
                        break;
                    case VFXParameter parameter:
                        ids[parameter] = "p" + parameters.Count;
                        parameters.Add(parameter);
                        break;
                    case VFXOperator op:
                        ids[op] = "o" + operators.Count;
                        operators.Add(op);
                        break;
                    case null:
                        break;
                    default:
                        ids[child] = "x" + others.Count;
                        others.Add(child);
                        break;
                }
            }
            return ids;
        }

        /// <summary>The describe system ids: one per <c>VFXData</c>, <c>s0</c>, <c>s1</c>, in the order of their first context.</summary>
        static Dictionary<VFXData, string> SystemIds(List<VFXContext> contexts)
        {
            var systemIds = new Dictionary<VFXData, string>();
            foreach (var context in contexts)
            {
                var data = SafeData(context);
                if (data == null || systemIds.ContainsKey(data))
                    continue;
                systemIds[data] = "s" + systemIds.Count;
            }
            return systemIds;
        }

        static VFXData SafeData(VFXContext context)
        {
            try
            {
                return context.GetData();
            }
            catch (Exception)
            {
                return null;
            }
        }

        static Map DescribeSystem(VFXData data, string id, List<VFXContext> contexts, Dictionary<VFXModel, string> ids, List<string> warnings)
        {
            var members = contexts.Where(c => SafeData(c) == data).Select(c => (object)ids[c]).ToList();
            var first = contexts.First(c => SafeData(c) == data);
            string name = null;
            try
            {
                name = VFXSystemNames.GetSystemName(first);
            }
            catch (Exception exception)
            {
                warnings.Add($"system {id}: name not readable: {exception.Message}");
            }

            var system = new Map
            {
                { "id", id },
                { "name", name ?? string.Empty },
                { "data_type", data.type.ToString() },
                { "data_class", data.GetType().Name },
                { "contexts", members },
            };
            object capacity = null;
            try
            {
                capacity = data.GetSettings(true).Any(s => s.name == "capacity") ? ToPlain(data.GetSettingValue("capacity"), 0, null) : null;
            }
            catch (Exception exception)
            {
                warnings.Add($"system {id}: capacity not readable: {exception.Message}");
            }
            system.Add("capacity", capacity);
            system.Add("settings", DescribeSettings(data, id, warnings));
            return system;
        }

        static Map DescribeContext(VFXContext context, Dictionary<VFXModel, string> ids, Dictionary<VFXData, string> systemIds,
            List<(string to, string from)> links, List<string> warnings)
        {
            var id = ids[context];
            var data = SafeData(context);
            var entry = new Map
            {
                { "id", id },
                { "type", context.GetType().Name },
                { "name", context.name },
                { "label", context.label ?? string.Empty },
                { "context_type", context.contextType.ToString() },
                { "input_data", context.inputType.ToString() },
                { "output_data", context.outputType.ToString() },
                { "system", data != null && systemIds.TryGetValue(data, out var systemId) ? systemId : null },
                { "settings", DescribeSettings(context, id, warnings) },
                { "inputs", DescribeInputs(context, id, ids, links, warnings) },
                { "outputs", DescribeOutputs(context) },
            };

            var blocks = new List<object>();
            foreach (var block in context.children)
            {
                var blockId = ids[block];
                blocks.Add(new Map
                {
                    { "id", blockId },
                    { "type", block.GetType().Name },
                    { "name", block.name },
                    { "enabled", block.enabled },
                    { "settings", DescribeSettings(block, blockId, warnings) },
                    { "inputs", DescribeInputs(block, blockId, ids, links, warnings) },
                    { "outputs", DescribeOutputs(block) },
                });
            }
            entry.Add("blocks", blocks);
            return entry;
        }

        static Map DescribeOperator(VFXModel op, Dictionary<VFXModel, string> ids, List<(string to, string from)> links, List<string> warnings)
        {
            var id = ids[op];
            var entry = new Map
            {
                { "id", id },
                { "type", op.GetType().Name },
                { "name", op.name },
                { "settings", DescribeSettings(op, id, warnings) },
            };
            if (op is IVFXSlotContainer container)
            {
                entry.Add("inputs", DescribeInputs(container, id, ids, links, warnings));
                entry.Add("outputs", DescribeOutputs(container));
            }
            return entry;
        }

        static Map DescribeParameter(VFXParameter parameter, Dictionary<VFXModel, string> ids, List<(string to, string from)> links, List<string> warnings)
        {
            var id = ids[parameter];
            object value = null;
            try
            {
                value = parameter.isOutput ? null : ToPlain(parameter.value, 0, null);
            }
            catch (Exception exception)
            {
                warnings.Add($"{id}: value not readable: {exception.Message}");
            }

            var entry = new Map
            {
                { "id", id },
                { "type", SlotTypeName(parameter.type) },
                { "exposed_name", parameter.exposedName ?? string.Empty },
                { "exposed", parameter.exposed },
                { "is_output", parameter.isOutput },
                { "category", parameter.category ?? string.Empty },
                { "value", value },
                { "node_count", parameter.nodes?.Count ?? 0 },
                { "settings", DescribeSettings(parameter, id, warnings) },
            };
            // Only when set, so graphs without tooltips describe as before.
            if (!string.IsNullOrEmpty(parameter.tooltip))
                entry.Add("tooltip", parameter.tooltip);
            if (parameter.isOutput)
                entry.Add("inputs", DescribeInputs(parameter, id, ids, links, warnings));
            entry.Add("outputs", DescribeOutputs(parameter));
            return entry;
        }

        static List<object> DescribeSettings(VFXModel model, string id, List<string> warnings)
        {
            var list = new List<object>();
            foreach (var (field, attribute) in VFXModel.GetFields(model.GetType()))
            {
                if (!IsUserSetting(attribute))
                    continue;
                object value;
                try
                {
                    value = ToPlain(field.GetValue(model), 0, null);
                }
                catch (Exception exception)
                {
                    warnings.Add($"{id}: setting {field.Name} not readable: {exception.Message}");
                    value = null;
                }
                list.Add(new Map { { "name", field.Name }, { "type", ValueTypeName(field.FieldType) }, { "value", value } });
            }
            return list;
        }

        static List<object> DescribeInputs(IVFXSlotContainer container, string id, Dictionary<VFXModel, string> ids,
            List<(string to, string from)> links, List<string> warnings)
        {
            var list = new List<object>();
            foreach (var slot in container.inputSlots)
            {
                var entry = new Map
                {
                    { "name", slot.name },
                    { "ref", SlotRef(slot) },
                    { "type", SlotTypeName(slot.property.type) },
                    { "linked", slot.HasLink(true) },
                };
                if (slot.spaceable)
                    entry.Add("space", slot.space.ToString());
                try
                {
                    entry.Add("value", SlotValue(slot));
                }
                catch (Exception exception)
                {
                    warnings.Add($"{id}.{slot.name}: value not readable: {exception.Message}");
                    entry.Add("value", null);
                }
                list.Add(entry);
                CollectLinks(slot, id, ids, links, warnings);
            }
            return list;
        }

        static List<object> DescribeOutputs(IVFXSlotContainer container)
        {
            var list = new List<object>();
            foreach (var slot in container.outputSlots)
                list.Add(new Map { { "name", slot.name }, { "ref", SlotRef(slot) }, { "type", SlotTypeName(slot.property.type) } });
            return list;
        }

        static void CollectLinks(VFXSlot slot, string ownerId, Dictionary<VFXModel, string> ids, List<(string to, string from)> links, List<string> warnings)
        {
            foreach (var linked in slot.LinkedSlots)
            {
                var from = SlotAddress(linked, ids);
                if (from == null)
                {
                    warnings.Add($"{ownerId}.{SlotPath(slot)}: linked to a slot whose owner is not in the graph");
                    continue;
                }
                links.Add((ownerId + "." + SlotPath(slot), from));
            }
            foreach (var child in slot.children)
                CollectLinks(child, ownerId, ids, links, warnings);
        }

        static string SlotAddress(VFXSlot slot, Dictionary<VFXModel, string> ids)
        {
            var owner = slot.owner as VFXModel;
            if (owner == null || !ids.TryGetValue(owner, out var ownerId))
                return null;
            return ownerId + "." + SlotPath(slot);
        }

        /// <summary>
        /// Slot names joined with dots from the master slot down. A slot without a name (the output of
        /// Add, Multiply and other math operators) is written <c>#&lt;index&gt;</c>: its index among the
        /// owner's slots of the same direction, or among its parent's children.
        /// </summary>
        static string SlotPath(VFXSlot slot)
        {
            var parent = slot.GetParent();
            if (parent == null)
                return SlotRef(slot);
            var name = string.IsNullOrEmpty(slot.name) ? "#" + parent.GetIndex(slot) : slot.name;
            return SlotPath(parent) + "." + name;
        }

        /// <summary>The address component of a master slot: its name, or <c>#&lt;index&gt;</c> when it has none.</summary>
        static string SlotRef(VFXSlot slot)
        {
            if (!string.IsNullOrEmpty(slot.name))
                return slot.name;
            var owner = slot.owner;
            if (owner == null)
                return "#?";
            var list = slot.direction == VFXSlot.Direction.kInput ? owner.inputSlots : owner.outputSlots;
            return "#" + list.IndexOf(slot);
        }

        static object SlotValue(VFXSlot slot)
        {
            return ToPlain(slot.value, 0, null);
        }

        static List<object> DescribeFlowLinks(List<VFXContext> contexts, Dictionary<VFXModel, string> ids)
        {
            var rows = new List<(string to, int toSlot, string from, int fromSlot)>();
            foreach (var context in contexts)
            {
                var flowInputs = context.inputFlowSlot;
                for (int i = 0; i < flowInputs.Length; i++)
                {
                    foreach (var link in flowInputs[i].link)
                    {
                        if (link.context == null || !ids.TryGetValue(link.context, out var fromId))
                            continue;
                        rows.Add((ids[context], i, fromId, link.slotIndex));
                    }
                }
            }
            return rows
                .Distinct()
                .OrderBy(r => r.to, StringComparer.Ordinal)
                .ThenBy(r => r.toSlot)
                .ThenBy(r => r.from, StringComparer.Ordinal)
                .ThenBy(r => r.fromSlot)
                .Select(r => (object)new Map { { "from", r.from }, { "from_slot", r.fromSlot }, { "to", r.to }, { "to_slot", r.toSlot } })
                .ToList();
        }

        static List<object> DescribeStickyNotes(VFXGraph graph)
        {
            var list = new List<object>();
            var notes = graph.UIInfos?.stickyNoteInfos;
            if (notes == null)
                return list;
            foreach (var note in notes)
                if (note != null)
                    list.Add(new Map { { "title", note.title ?? string.Empty }, { "contents", note.contents ?? string.Empty } });
            return list;
        }

        static List<object> DescribeGroups(VFXGraph graph, Dictionary<VFXModel, string> ids)
        {
            var list = new List<object>();
            var groups = graph.UIInfos?.groupInfos;
            if (groups == null)
                return list;
            foreach (var group in groups)
            {
                if (group == null)
                    continue;
                var members = new List<string>();
                int notes = 0;
                if (group.contents != null)
                {
                    foreach (var node in group.contents)
                    {
                        if (node.isStickyNote)
                            notes++;
                        else if (node.model != null && ids.TryGetValue(node.model, out var memberId))
                            members.Add(memberId);
                    }
                }
                list.Add(new Map
                {
                    { "title", group.title ?? string.Empty },
                    { "members", members.Distinct().OrderBy(m => m, StringComparer.Ordinal).Select(m => (object)m).ToList() },
                    { "sticky_notes", notes },
                });
            }
            return list;
        }

        // ------------------------------------------------------------------ values

        /// <summary>A project asset as a map: name, type, project-relative path, GUID and local file id.</summary>
        static Map AssetRef(UnityEngine.Object asset, string fallbackPath = null)
        {
            if (asset == null)
            {
                if (fallbackPath == null)
                    return null;
                return new Map { { "path", fallbackPath }, { "guid", AssetDatabase.AssetPathToGUID(fallbackPath) } };
            }

            var path = AssetDatabase.GetAssetPath(asset);
            var map = new Map
            {
                { "name", asset.name },
                { "type", asset.GetType().Name },
            };
            if (!string.IsNullOrEmpty(path) && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long fileId))
            {
                map.Add("path", path);
                map.Add("guid", guid);
                map.Add("file_id", fileId);
            }
            else
            {
                map.Add("path", fallbackPath);
                map.Add("guid", fallbackPath != null ? AssetDatabase.AssetPathToGUID(fallbackPath) : null);
            }
            return map;
        }

        static object SafeGet(FieldInfo field, object instance)
        {
            try
            {
                return field.GetValue(instance);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A value as a plain tree: null, bool, string (enums by name), int/long/float/double, lists,
        /// <see cref="Map"/> for structs and classes (public instance fields in declaration order), and
        /// asset references for Unity objects.
        /// </summary>
        static object ToPlain(object value, int depth, HashSet<object> visiting)
        {
            switch (value)
            {
                case null:
                    return null;
                case string s:
                    return s;
                case bool b:
                    return b;
                case Enum e:
                    return e.ToString();
                case float f:
                    return f;
                case double d:
                    return d;
                case int i:
                    return i;
                case uint u:
                    return (long)u;
                case long l:
                    return l;
                case ulong ul:
                    return ul <= long.MaxValue ? (object)(long)ul : ul.ToString(CultureInfo.InvariantCulture);
                case short sh:
                    return (int)sh;
                case ushort us:
                    return (int)us;
                case byte by:
                    return (int)by;
                case sbyte sb:
                    return (int)sb;
                case char c:
                    return c.ToString();
                case Vector2 v2:
                    return new List<object> { v2.x, v2.y };
                case Vector3 v3:
                    return new List<object> { v3.x, v3.y, v3.z };
                case Vector4 v4:
                    return new List<object> { v4.x, v4.y, v4.z, v4.w };
                case Vector2Int v2i:
                    return new List<object> { v2i.x, v2i.y };
                case Vector3Int v3i:
                    return new List<object> { v3i.x, v3i.y, v3i.z };
                case Color color:
                    return new List<object> { color.r, color.g, color.b, color.a };
                case Color32 color32:
                    return new List<object> { (int)color32.r, (int)color32.g, (int)color32.b, (int)color32.a };
                case Quaternion q:
                    return new List<object> { q.x, q.y, q.z, q.w };
                case Matrix4x4 m:
                {
                    var list = new List<object>(16);
                    for (int i = 0; i < 16; i++)
                        list.Add(m[i]);
                    return list;
                }
                case Rect rect:
                    return new List<object> { rect.x, rect.y, rect.width, rect.height };
                case Bounds bounds:
                    return new Map { { "center", ToPlain(bounds.center, depth + 1, visiting) }, { "size", ToPlain(bounds.size, depth + 1, visiting) } };
                case Gradient gradient:
                    return GradientToPlain(gradient);
                case AnimationCurve curve:
                    return CurveToPlain(curve);
                case UnityEngine.Object unityObject:
                    return unityObject == null ? null : AssetRef(unityObject);
                case Type type:
                    return type.FullName;
            }

            if (depth >= MaxValueDepth)
                return value.ToString();

            var valueType = value.GetType();
            if (!valueType.IsValueType)
            {
                visiting ??= new HashSet<object>(IdentityComparer.Instance);
                if (!visiting.Add(value))
                    return "<cycle>";
            }

            try
            {
                if (value is IDictionary dictionary)
                {
                    var rows = new List<(string key, object value)>();
                    foreach (DictionaryEntry pair in dictionary)
                        rows.Add((Convert.ToString(pair.Key, CultureInfo.InvariantCulture), ToPlain(pair.Value, depth + 1, visiting)));
                    var map = new Map();
                    foreach (var row in rows.OrderBy(r => r.key, StringComparer.Ordinal))
                        map.Add(row.key, row.value);
                    return map;
                }

                if (value is IEnumerable enumerable)
                {
                    var list = new List<object>();
                    foreach (var item in enumerable)
                        list.Add(ToPlain(item, depth + 1, visiting));
                    return list;
                }

                var fields = valueType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                if (fields.Length == 0)
                    return value.ToString();
                var fieldMap = new Map();
                foreach (var field in fields)
                    fieldMap.Add(field.Name, ToPlain(SafeGet(field, value), depth + 1, visiting));
                return fieldMap;
            }
            finally
            {
                if (!valueType.IsValueType)
                    visiting.Remove(value);
            }
        }

        static Map GradientToPlain(Gradient gradient)
        {
            var colors = new List<object>();
            foreach (var key in gradient.colorKeys)
                colors.Add(new Map { { "t", key.time }, { "color", new List<object> { key.color.r, key.color.g, key.color.b, key.color.a } } });
            var alphas = new List<object>();
            foreach (var key in gradient.alphaKeys)
                alphas.Add(new Map { { "t", key.time }, { "alpha", key.alpha } });
            return new Map
            {
                { "mode", gradient.mode.ToString() },
                { "color_space", gradient.colorSpace.ToString() },
                { "color_keys", colors },
                { "alpha_keys", alphas },
            };
        }

        static Map CurveToPlain(AnimationCurve curve)
        {
            var keys = new List<object>();
            foreach (var key in curve.keys)
            {
                keys.Add(new Map
                {
                    { "time", key.time },
                    { "value", key.value },
                    { "in_tangent", key.inTangent },
                    { "out_tangent", key.outTangent },
                    { "in_weight", key.inWeight },
                    { "out_weight", key.outWeight },
                    { "weighted_mode", key.weightedMode.ToString() },
                });
            }
            return new Map
            {
                { "keys", keys },
                { "pre_wrap", curve.preWrapMode.ToString() },
                { "post_wrap", curve.postWrapMode.ToString() },
            };
        }

        /// <summary>Short slot type name: float, int, uint, bool, string, or the CLR type name (Vector3, Position, Texture2D).</summary>
        static string SlotTypeName(Type type) => ValueTypeName(type);

        static string ValueTypeName(Type type)
        {
            if (type == null)
                return null;
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(int)) return "int";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(long)) return "long";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type.IsGenericType)
            {
                var name = type.Name;
                var tick = name.IndexOf('`');
                if (tick >= 0)
                    name = name.Substring(0, tick);
                return name + "<" + string.Join(",", type.GetGenericArguments().Select(ValueTypeName)) + ">";
            }
            return type.Name;
        }

        static string ValueKind(Type type)
        {
            if (type.IsEnum) return "enum";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(int) || type == typeof(uint) || type == typeof(long)) return "integer";
            if (type == typeof(float) || type == typeof(double)) return "number";
            if (type == typeof(string)) return "string";
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return "asset";
            return "object";
        }

        sealed class IdentityComparer : IEqualityComparer<object>
        {
            public static readonly IdentityComparer Instance = new IdentityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
