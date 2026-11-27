using Cuvara.VFX.Editor.Capture;

namespace Cuvara.VFX.Editor.Verbs
{
    /// <summary>
    /// <c>capture</c>: a deterministic play-test of one effect in a neutral stage; writes one PNG per
    /// sample, <c>contact-sheet.png</c> and <c>stats.json</c> (schema <c>Schemas~/vfx-capture.schema.json</c>).
    /// </summary>
    /// <remarks>
    /// <para>Arguments: <c>effect</c> (required, a <c>.prefab</c> or a <c>.vfx</c>), <c>seed</c> (default
    /// 1234), <c>times</c> (seconds; default 8 times spread over the recipe's <c>meta.duration_seconds</c>,
    /// or over 1 s), <c>camera</c> (<c>gameplay</c>, <c>close</c>, <c>side</c>), <c>lighting</c>
    /// (<c>day</c>, <c>dusk</c>, <c>night</c>), <c>out</c> (a folder outside Assets and the packages;
    /// default <c>&lt;project&gt;/Captures/&lt;effect&gt;/seed-&lt;seed&gt;-&lt;camera&gt;-&lt;lighting&gt;</c>),
    /// and the optional <c>delta_time</c>, <c>width</c>, <c>height</c>, <c>columns</c>,
    /// <c>probe_interval</c>, <c>settle_frames</c>, <c>max_seconds</c>, <c>timeout_seconds</c>.</para>
    /// <para>The verb returns at once with <c>"pending": true</c> and a <c>result_file</c>: the capture
    /// runs in play mode after a domain reload (<see cref="VfxCaptureJob"/>). The final result, written
    /// to <c>result_file</c> when the editor is back in edit mode, holds every field of
    /// <c>stats.json</c>. Documentation~/CAPTURE.md.</para>
    /// </remarks>
    public sealed class CaptureVerb : IVfxVerb
    {
        /// <inheritdoc />
        public string Name => "capture";

        /// <inheritdoc />
        public string Summary => "play an effect with a fixed seed and time step in a neutral stage; frames, contact sheet and stats.json (pending: poll result_file)";

        /// <inheritdoc />
        public void Execute(VfxArgs args, VfxResult result)
        {
            var timeout = args.GetDouble("timeout_seconds", VfxCaptureJob.DefaultTimeoutSeconds);
            if (!(timeout >= 30 && timeout <= 3600))
            {
                result.Error("argument 'timeout_seconds' must be between 30 and 3600");
                return;
            }
            var request = VfxCaptureRequest.Parse(args, result);
            if (request == null)
                return;
            VfxCaptureJob.Start(request, timeout, result);
        }
    }
}
