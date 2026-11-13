namespace Cuvara.VFX.Editor.Verbs
{
    /// <summary>
    /// <c>build</c>: builds the <c>.vfx</c> of a recipe (schema <c>Schemas~/vfx-recipe.schema.json</c>).
    /// </summary>
    /// <remarks>
    /// <para>Arguments: <c>recipe</c> (required): the path of a <c>&lt;Name&gt;.vfxrecipe.json</c>,
    /// project-relative (<c>Assets/...</c>, <c>Packages/&lt;package&gt;/...</c>) or absolute (inside the
    /// project or a local package, the repository path included).</para>
    /// <para>The graph is built on a temporary copy and compiled; <c>&lt;Name&gt;.vfx</c> next to the recipe
    /// is created or replaced only when the compile is clean, keeping its GUID. On failure the previous
    /// asset is untouched.</para>
    /// <para>Payload: <c>recipe</c>, <c>recipe_sha256</c>, <c>asset</c> (path and GUID), <c>recipe_id</c>,
    /// <c>ops_applied</c>, <c>compile_errors</c>, <c>created</c>, <c>guid_stable</c>, <c>previous_sha256</c>,
    /// <c>sha256</c>, <c>describe_sha256</c> (SHA-256 of the exact <c>describe</c> output for the asset) and
    /// <c>systems</c> (id, name, data type, capacity). A failing op adds <c>op_error</c> (index, op, message,
    /// valid names nearest first).</para>
    /// </remarks>
    public sealed class BuildVerb : IVfxVerb
    {
        /// <inheritdoc />
        public string Name => "build";

        /// <inheritdoc />
        public string Summary => "builds the .vfx next to a recipe on a temporary copy; replaces it only when the compile is clean";

        /// <inheritdoc />
        public void Execute(VfxArgs args, VfxResult result)
        {
            VfxRecipeBuilder.Build(args.GetString("recipe", required: true), result);
        }
    }
}
