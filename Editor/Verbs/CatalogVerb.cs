using System;
using System.Collections.Generic;
using System.Linq;
using Cuvara.VFX.Editor.Bridge;
using UnityEngine;

namespace Cuvara.VFX.Editor.Verbs
{
    /// <summary>
    /// <c>catalog</c>: the contexts, blocks, operators and parameter types the VFX Graph library offers.
    /// </summary>
    /// <remarks>
    /// <para>Arguments: <c>filter</c> (optional, case-insensitive substring of a type name, variant name or
    /// category), <c>include_sub_variants</c> (optional, default false).</para>
    /// <para>Payload: <c>vfx_graph</c>, <c>vfx_graph_bridge</c>, <c>unity</c>, <c>recipe_schema</c>,
    /// <c>recipe_ops</c>, <c>recipe_op_fields</c> (required and optional fields per op), <c>texture_steps</c>,
    /// <c>texture_step_fields</c> (the <c>import-texture</c> steps with their parameters), <c>filter</c>, <c>counts</c>, <c>contexts</c>, <c>blocks</c>, <c>operators</c>,
    /// <c>parameter_types</c>. Each node entry has <c>type</c> (the name a recipe uses), <c>type_full</c>,
    /// <c>name</c>, <c>category</c>, <c>variant</c> (<c>id</c> and the <c>settings</c> that make it),
    /// <c>settings</c> (name, type, kind, allowed values, default) and <c>inputs</c> / <c>outputs</c> slots
    /// (name, type, direction, default). Ordered by type, then variant id; experimental entries are left
    /// out so the output does not depend on an editor preference.</para>
    /// </remarks>
    public sealed class CatalogVerb : IVfxVerb
    {
        /// <inheritdoc />
        public string Name => "catalog";

        /// <inheritdoc />
        public string Summary => "contexts, blocks, operators and parameter types of the VFX Graph library, with settings and slots";

        /// <inheritdoc />
        public void Execute(VfxArgs args, VfxResult result)
        {
            var filter = args.GetString("filter");
            var includeSubVariants = args.GetBool("include_sub_variants", false);

            var warnings = new List<string>();
            var catalog = VfxGraphBridge.Catalog(filter, includeSubVariants, warnings);
            foreach (var warning in warnings)
                result.Warning(warning);

            var contexts = (List<object>)catalog.Get("contexts");
            var blocks = (List<object>)catalog.Get("blocks");
            var operators = (List<object>)catalog.Get("operators");
            var parameters = (List<object>)catalog.Get("parameter_types");

            foreach (var line in AmbiguousShortNames(contexts.Concat(blocks).Concat(operators)))
                result.Warning(line);

            result.Set("vfx_graph", VfxGraphBridge.InstalledVersion);
            result.Set("vfx_graph_bridge", VfxGraphBridge.WrittenForVersion);
            result.Set("unity", Application.unityVersion);
            result.Set("recipe_schema", VfxRecipeOps.Schema);
            result.Set("recipe_ops", VfxRecipeOps.Names.ToList());
            var opFields = new JsonObject();
            foreach (var spec in VfxRecipeOps.Specs)
            {
                opFields.Add(spec.Name, new JsonObject
                {
                    { "required", spec.Required.Cast<object>().ToList() },
                    { "optional", spec.Optional.Cast<object>().ToList() },
                    { "summary", spec.Summary },
                });
            }
            result.Set("recipe_op_fields", opFields);
            result.Set("texture_steps", VfxTextureSteps.Names.ToList());
            var stepFields = new JsonObject();
            foreach (var spec in VfxTextureSteps.Specs)
            {
                stepFields.Add(spec.Name, new JsonObject
                {
                    { "required", spec.Required.Cast<object>().ToList() },
                    { "optional", spec.Optional.Cast<object>().ToList() },
                    { "summary", spec.Summary },
                });
            }
            result.Set("texture_step_fields", stepFields);
            result.Set("filter", filter);
            result.Set("counts", new JsonObject
            {
                { "contexts", contexts.Count },
                { "blocks", blocks.Count },
                { "operators", operators.Count },
                { "parameter_types", parameters.Count },
            });
            result.Set("contexts", contexts);
            result.Set("blocks", blocks);
            result.Set("operators", operators);
            result.Set("parameter_types", parameters);
        }

        /// <summary>One warning per short type name that two different CLR types share (a recipe must then use type_full).</summary>
        static IEnumerable<string> AmbiguousShortNames(IEnumerable<object> entries)
        {
            var byShort = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (var entry in entries.OfType<VfxGraphBridge.Map>())
            {
                var shortName = entry.Get("type") as string;
                var fullName = entry.Get("type_full") as string;
                if (shortName == null || fullName == null)
                    continue;
                if (!byShort.TryGetValue(shortName, out var set))
                    byShort[shortName] = set = new SortedSet<string>(StringComparer.Ordinal);
                set.Add(fullName);
            }
            foreach (var pair in byShort.Where(p => p.Value.Count > 1))
                yield return $"type name '{pair.Key}' is shared by {string.Join(", ", pair.Value)}; use type_full to tell them apart";
        }
    }
}
