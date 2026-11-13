using System.Collections.Generic;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// The op names a recipe (<c>*.vfxrecipe.json</c>) may use, as printed by <c>catalog</c>.
    /// </summary>
    /// <remarks>
    /// The list follows <c>Documentation~/DESIGN.md</c>, section "Recipe". The recipe builder owns the
    /// final set: when it adds, renames or drops an op, it changes this list, so <c>catalog</c> and the
    /// schema stay in step with the builder.
    /// </remarks>
    public static class VfxRecipeOps
    {
        /// <summary>The recipe schema id.</summary>
        public const string Schema = "cuvara.vfx-recipe/1";

        /// <summary>Op names, in the order a recipe usually uses them.</summary>
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "add_parameter",
            "add_context",
            "add_block",
            "add_operator",
            "set_setting",
            "set_slot",
            "link_slots",
            "link_flow",
            "use_subgraph",
            "set_output",
            "comment",
        };
    }
}
