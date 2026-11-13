namespace Cuvara.VFX.Editor.Verbs
{
    /// <summary>
    /// <c>apply</c>: applies recipe ops to an existing <c>.vfx</c>.
    /// </summary>
    /// <remarks>
    /// <para>Arguments: <c>asset</c> (required): the <c>.vfx</c>, project-relative or absolute;
    /// <c>ops</c> (required): an array of recipe ops (the op set of <c>build</c>). The nodes of the existing
    /// graph are addressed with the ids <c>describe</c> prints (<c>c0</c>, <c>c0.b1</c>, <c>o2</c>,
    /// <c>p0</c>); new nodes get the ids their ops give.</para>
    /// <para>The ops run on a temporary copy that is compiled; the asset is replaced only when the compile
    /// is clean, keeping its GUID. Payload as for <c>build</c>, without the recipe keys.</para>
    /// </remarks>
    public sealed class ApplyVerb : IVfxVerb
    {
        /// <inheritdoc />
        public string Name => "apply";

        /// <inheritdoc />
        public string Summary => "applies recipe ops to an existing .vfx on a temporary copy; replaces it only when the compile is clean";

        /// <inheritdoc />
        public void Execute(VfxArgs args, VfxResult result)
        {
            var asset = args.GetString("asset", required: true);
            if (!args.Has("ops"))
                throw new VfxArgumentException("missing required argument 'ops' (array of recipe ops)");
            VfxRecipeBuilder.Apply(asset, args.GetRaw("ops"), result);
        }
    }
}
