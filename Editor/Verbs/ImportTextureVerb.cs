namespace Cuvara.VFX.Editor.Verbs
{
    /// <summary>
    /// <c>import-texture</c>: writes a transformed PNG and its import settings; returns the SHA-256 of the
    /// source and of the output.
    /// </summary>
    /// <remarks>
    /// <para>Arguments: <c>source</c> (required): a PNG, project-relative (<c>Assets/...</c>,
    /// <c>Packages/&lt;package&gt;/...</c>) or absolute (anywhere, for example a generator's output folder);
    /// <c>target</c> (required): a project-relative <c>.png</c> asset path, or an absolute path inside the
    /// project or a local package; <c>transform</c> (optional): an ordered array of steps, each a step name
    /// or <c>{"step": name, ...parameters}</c> (<see cref="VfxTextureSteps"/>).</para>
    /// <para>Payload: <c>source</c>, <c>source_sha256</c>, <c>source_size</c>, <c>target</c>, <c>steps</c>
    /// (each applied step with its parameters, defaults filled in), <c>inputs</c> (files read by
    /// <c>pack_channels</c>, with SHA-256), <c>size</c>, <c>flipbook</c>, <c>created</c>,
    /// <c>previous_sha256</c>, <c>guid</c>, <c>guid_stable</c>, <c>output_sha256</c> and
    /// <c>import_settings</c>. An unknown step or parameter adds <c>step_error</c> (index, step, message,
    /// valid names nearest first). A failure leaves an existing target untouched.</para>
    /// </remarks>
    public sealed class ImportTextureVerb : IVfxVerb
    {
        /// <inheritdoc />
        public string Name => "import-texture";

        /// <inheritdoc />
        public string Summary => "writes a PNG transformed by deterministic steps and its import settings; returns the SHA-256 of source and output";

        /// <inheritdoc />
        public void Execute(VfxArgs args, VfxResult result)
        {
            var source = args.GetString("source", required: true);
            var target = args.GetString("target", required: true);
            VfxTextureImport.Import(source, target, args.GetRaw("transform"), result);
        }
    }
}
