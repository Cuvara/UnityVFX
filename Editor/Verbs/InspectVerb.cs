namespace Cuvara.VFX.Editor.Verbs
{
    /// <summary>
    /// <c>inspect</c>: the static facts of one effect for a lint (schema <c>Schemas~/vfx-inspect.schema.json</c>).
    /// </summary>
    /// <remarks>
    /// <para>Arguments: <c>effect</c> (required): a <c>.prefab</c> that holds one <c>VisualEffect</c>, or a
    /// <c>.vfx</c> asset; project-relative or absolute inside the project or a local package of it.</para>
    /// <para>Payload: <c>schema</c>, <c>effect</c>, <c>asset</c>, <c>prefab</c>, <c>recipe</c> (path and
    /// SHA-256 of the <c>.vfxrecipe.json</c> next to the <c>.vfx</c>, or null), <c>vfx_graph</c>,
    /// <c>unity</c>, <c>build_target</c>, <c>describe_sha256</c>, <c>systems</c> (name, capacity, outputs),
    /// <c>outputs</c> (type, blend mode, generated shader, Shader Graph, effective features, render
    /// settings), <c>textures</c> (size, format, sRGB, mip maps, compression, estimated bytes, users),
    /// <c>exposed_properties</c> (type, default, prefab overrides), <c>hdr_colors</c> (path, RGBA, peak)
    /// and <c>totals</c>. No timestamp; two calls on an unchanged effect give the same JSON.</para>
    /// </remarks>
    public sealed class InspectVerb : IVfxVerb
    {
        /// <inheritdoc />
        public string Name => "inspect";

        /// <inheritdoc />
        public string Summary => "static facts of an effect (prefab or .vfx) for lint: systems, outputs, shaders, textures, exposed properties, HDR colours";

        /// <inheritdoc />
        public void Execute(VfxArgs args, VfxResult result)
        {
            VfxInspect.Run(args.GetString("effect", required: true), result);
        }
    }
}
