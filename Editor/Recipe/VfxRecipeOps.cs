using System;
using System.Collections.Generic;
using System.Linq;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// The op names a recipe (<c>*.vfxrecipe.json</c>) may use, with their fields, as printed by
    /// <c>catalog</c> and checked by the recipe builder.
    /// </summary>
    /// <remarks>
    /// The list follows <c>Documentation~/DESIGN.md</c>, section "Recipe". The reference with examples is
    /// in <c>Documentation~/AUTHORING.md</c>, section "Recipe", and the JSON Schema is
    /// <c>Schemas~/vfx-recipe.schema.json</c>; a change here changes both.
    /// </remarks>
    public static class VfxRecipeOps
    {
        /// <summary>The recipe schema id.</summary>
        public const string Schema = "cuvara.vfx-recipe/1";

        /// <summary>The top-level fields of a recipe; <c>schema</c>, <c>id</c> and <c>ops</c> are required.</summary>
        public static readonly IReadOnlyList<string> TopLevelFields = new[] { "schema", "id", "vfx_graph", "meta", "ops" };

        /// <summary>One op: its name and its required and optional fields (besides <c>op</c>).</summary>
        public sealed class Spec
        {
            internal Spec(string name, string[] required, string[] optional, string summary)
            {
                Name = name;
                Required = required;
                Optional = optional;
                Summary = summary;
            }

            /// <summary>The op name.</summary>
            public string Name { get; }

            /// <summary>Fields the op must have.</summary>
            public IReadOnlyList<string> Required { get; }

            /// <summary>Fields the op may have.</summary>
            public IReadOnlyList<string> Optional { get; }

            /// <summary>One line on what the op does.</summary>
            public string Summary { get; }

            /// <summary>Every field the op accepts, <c>op</c> included.</summary>
            public IEnumerable<string> AllFields => new[] { "op" }.Concat(Required).Concat(Optional);
        }

        /// <summary>The ops, in the order a recipe usually uses them.</summary>
        public static readonly IReadOnlyList<Spec> Specs = new[]
        {
            new Spec("add_parameter", new[] { "id", "type", "name" }, new[] { "value", "exposed", "category", "tooltip" },
                "adds a parameter of a catalog parameter type; exposed (default true) makes it a property of the VisualEffect"),
            new Spec("add_context", new[] { "id", "type" }, new[] { "variant", "label", "settings", "slots" },
                "adds a context by catalog type name and optional catalog variant id"),
            new Spec("add_block", new[] { "id", "context", "type" }, new[] { "variant", "settings", "slots" },
                "adds a block at the end of a context"),
            new Spec("add_operator", new[] { "id", "type" }, new[] { "variant", "settings", "slots" },
                "adds an operator"),
            new Spec("set_setting", new[] { "node", "name", "value" }, Array.Empty<string>(),
                "sets one setting of a node"),
            new Spec("set_slot", new[] { "slot" }, new[] { "value", "space" },
                "sets the value (and/or the space) of an input slot, or the value of a parameter"),
            new Spec("link_slots", new[] { "from", "to" }, Array.Empty<string>(),
                "links an output slot to an input slot"),
            new Spec("link_flow", new[] { "from", "to" }, new[] { "from_slot", "to_slot" },
                "links the flow of two contexts (flow slots default to 0)"),
            // No 'settings': the only setting of a subgraph node (VFX Graph 17.6) is its asset, m_Subgraph,
            // which 'asset' sets. 'slots' are the subgraph's exposed parameters by name.
            new Spec("use_subgraph", new[] { "id", "asset" }, new[] { "context", "slots" },
                "adds a subgraph: .vfx (context), .vfxoperator (operator) or .vfxblock (block, needs context); slots are its exposed parameters"),
            new Spec("set_output", new[] { "node" }, new[] { "texture", "blend_mode", "color_mapping", "gradient" },
                "sets the texture, blend mode, colour mapping and mapping gradient of an output context"),
            new Spec("comment", new[] { "text" }, new[] { "title" },
                "adds a sticky note"),
        };

        /// <summary>Op names, in the order a recipe usually uses them.</summary>
        public static readonly IReadOnlyList<string> Names = Specs.Select(s => s.Name).ToList();

        /// <summary>The spec of an op, or null.</summary>
        public static Spec Find(string name) => Specs.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.Ordinal));
    }
}
