using System.Collections.Generic;
using System.IO;
using Cuvara.VFX.Editor.Bridge;
using UnityEngine;

namespace Cuvara.VFX.Editor.Verbs
{
    /// <summary>
    /// <c>describe</c>: a VFX Graph asset as JSON (schema <c>Schemas~/vfx-describe.schema.json</c>).
    /// </summary>
    /// <remarks>
    /// <para>Arguments: <c>asset</c> (required): a project-relative path (<c>Assets/...</c> or
    /// <c>Packages/...</c>) or an absolute path inside the project's <c>Assets</c> folder.</para>
    /// <para>Payload: <c>schema</c>, <c>asset</c> (path and GUID), <c>asset_kind</c>, <c>vfx_graph</c>,
    /// <c>graph_version</c>, <c>contexts</c> (with their <c>blocks</c>), <c>operators</c>,
    /// <c>parameters</c>, <c>other_models</c>, <c>links</c>, <c>flow_links</c>, <c>systems</c>,
    /// <c>sticky_notes</c>, <c>groups</c>. Ids are stable (<c>c0</c>, <c>c0.b1</c>, <c>o2</c>, <c>p0</c>,
    /// <c>s0</c>) and two calls on the same asset return the same JSON.</para>
    /// </remarks>
    public sealed class DescribeVerb : IVfxVerb
    {
        /// <summary>Schema id of the describe output.</summary>
        public const string Schema = "cuvara.vfx-describe/1";

        /// <inheritdoc />
        public string Name => "describe";

        /// <inheritdoc />
        public string Summary => "a .vfx graph as JSON: contexts, blocks, operators, parameters, settings, slot values, links, flow links, systems";

        /// <inheritdoc />
        public void Execute(VfxArgs args, VfxResult result)
        {
            var asset = ToProjectPath(args.GetString("asset", required: true));
            var warnings = new List<string>();
            var graph = VfxGraphBridge.Describe(asset, warnings);
            foreach (var warning in warnings)
                result.Warning(warning);

            result.Set("schema", Schema);
            foreach (var pair in graph)
                result.Set(pair.Key, pair.Value);
        }

        /// <summary>Maps an absolute path inside the project to a project-relative one; other paths are returned with forward slashes.</summary>
        internal static string ToProjectPath(string path)
        {
            var normalized = path.Replace('\\', '/');
            if (!Path.IsPathRooted(path))
                return normalized;
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            var full = Path.GetFullPath(path).Replace('\\', '/');
            return full.StartsWith(projectRoot, System.StringComparison.OrdinalIgnoreCase) ? full.Substring(projectRoot.Length) : normalized;
        }
    }
}
