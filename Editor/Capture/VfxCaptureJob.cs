using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Cuvara.VFX.Editor.Capture
{
    /// <summary>
    /// The control flow of the <c>capture</c> verb across play mode.
    /// </summary>
    /// <remarks>
    /// <para>Entering play mode reloads the scripting domain in the Sandbox (Enter Play Mode Options
    /// off), so <see cref="VfxCli.Run"/> cannot wait for a capture. <see cref="Start"/> therefore stores
    /// the job as JSON in <see cref="SessionState"/>, asks for play mode and returns at once with
    /// <c>"pending": true</c> and the <c>result_file</c> to wait for. After the reload this class
    /// (<see cref="InitializeOnLoadAttribute"/>) finds the job, runs a <see cref="VfxCaptureSession"/>,
    /// leaves play mode, and only then writes the final verb result to <c>result_file</c>, so the
    /// editor is back in edit mode, with the scenes it had before, when the result appears.
    /// <c>Tools~/vfxcli.py</c> waits for that file.</para>
    /// <para>Batch mode: <see cref="VfxCli.Batch"/> attaches its <c>-vfxOut</c> file with
    /// <see cref="AttachBatch"/> and does not exit; the job writes the result there and exits the editor
    /// with 0 or 1.</para>
    /// </remarks>
    [InitializeOnLoad]
    public static class VfxCaptureJob
    {
        const string Key = "Cuvara.VFX.Capture.Job";

        /// <summary>Seconds the job may wait for play mode to start (compile errors block it).</summary>
        public const double EnterTimeoutSeconds = 180;

        /// <summary>Default seconds a whole capture job may take.</summary>
        public const double DefaultTimeoutSeconds = 900;

        static VfxCaptureSession s_Session;

        static VfxCaptureJob()
        {
            EditorApplication.update += Poll;
        }

        /// <summary>True when a capture job is pending in this editor session.</summary>
        public static bool IsPending => !string.IsNullOrEmpty(SessionState.GetString(Key, string.Empty));

        /// <summary>The folder of the result files: <c>&lt;project&gt;/Temp/CuvaraVfxCapture</c>.</summary>
        public static string ResultFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "CuvaraVfxCapture")).Replace('\\', '/');

        /// <summary>
        /// Starts a capture job from edit mode: stores it, asks for play mode and sets the pending payload
        /// (<c>pending</c>, <c>job</c>, <c>out</c>, <c>result_file</c>) in <paramref name="result"/>.
        /// </summary>
        public static void Start(VfxCaptureRequest request, double timeoutSeconds, VfxResult result)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                result.Error("capture starts from edit mode; the editor is in play mode (leave it first)");
                return;
            }
            if (IsPending)
            {
                result.Error("another capture job is pending in this editor");
                return;
            }
            if (EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
            {
                result.Error("scripts are compiling or failed to compile: play mode cannot start");
                return;
            }

            var id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            Directory.CreateDirectory(ResultFolder);
            var resultFile = ResultFolder + "/capture-" + id + ".json";
            if (File.Exists(resultFile))
                File.Delete(resultFile);

            var job = new JsonObject
            {
                { "id", id },
                { "phase", "entering" },
                { "request", request.ToJson() },
                { "result_file", resultFile },
                { "batch_out", null },
                { "started_utc", DateTime.UtcNow.Ticks },
                { "timeout_seconds", timeoutSeconds },
                { "result", null },
            };
            Save(job);
            EditorApplication.EnterPlaymode();

            result.Set("pending", true);
            result.Set("job", id);
            result.Set("out", request.Out);
            result.Set("result_file", resultFile);
        }

        /// <summary>
        /// Batch mode: makes the pending job write its final result to <paramref name="outPath"/> and exit
        /// the editor. Returns false when no job is pending.
        /// </summary>
        public static bool AttachBatch(string outPath)
        {
            var job = Load();
            if (job == null)
                return false;
            job.Set("batch_out", string.IsNullOrEmpty(outPath) ? null : Path.GetFullPath(outPath));
            job.Set("batch", true);
            Save(job);
            return true;
        }

        static JsonObject Load()
        {
            var text = SessionState.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(text))
                return null;
            try
            {
                return Json.Parse(text) as JsonObject;
            }
            catch (FormatException)
            {
                SessionState.EraseString(Key);
                return null;
            }
        }

        static void Save(JsonObject job) => SessionState.SetString(Key, Json.Write(job));

        static void Poll()
        {
            JsonObject job;
            try
            {
                job = Load();
            }
            catch (Exception)
            {
                return;
            }
            if (job == null)
                return;

            try
            {
                Advance(job);
            }
            catch (Exception exception)
            {
                Debug.LogError("[vfxcli] capture job failed: " + exception);
                var failed = new VfxResult("capture");
                failed.Error("capture job failed: " + exception.GetType().Name + ": " + exception.Message);
                job.Set("result", failed.ToJsonObject());
                job.Set("phase", "exiting");
                Save(job);
                if (EditorApplication.isPlaying)
                    EditorApplication.ExitPlaymode();
            }
        }

        static void Advance(JsonObject job)
        {
            var phase = job.Get("phase") as string;
            var elapsed = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - Convert.ToInt64(job.Get("started_utc"), CultureInfo.InvariantCulture)).TotalSeconds;
            var timeout = Convert.ToDouble(job.Get("timeout_seconds"), CultureInfo.InvariantCulture);

            switch (phase)
            {
                case "entering":
                    if (EditorApplication.isPlaying && !EditorApplication.isCompiling)
                    {
                        var request = VfxCaptureRequest.FromJson((JsonObject)job.Get("request"));
                        s_Session = VfxCaptureSession.Start(request);
                        job.Set("phase", "running");
                        Save(job);
                    }
                    else if (elapsed > EnterTimeoutSeconds)
                    {
                        Conclude(job, Failure($"play mode did not start within {EnterTimeoutSeconds} s (compile errors, or a modal dialog)"));
                    }
                    return;

                case "running":
                    if (s_Session == null)
                    {
                        // The domain reloaded during the capture (a script changed, or play mode ended).
                        ExitWith(job, Failure("the capture was interrupted: the scripting domain reloaded or play mode ended"));
                        return;
                    }
                    if (!s_Session.IsDone && elapsed > timeout)
                        s_Session.Abort($"capture did not finish within {timeout} s");
                    if (!s_Session.IsDone)
                    {
                        if (!EditorApplication.isPlaying)
                            s_Session.Abort("play mode ended during the capture");
                        else
                            return;
                    }
                    var final = s_Session.Result;
                    s_Session = null;
                    ExitWith(job, final.ToJsonObject());
                    return;

                case "exiting":
                    if (EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        if (EditorApplication.isPlaying && elapsed > timeout + 60)
                            EditorApplication.ExitPlaymode();
                        return;
                    }
                    Conclude(job, (JsonObject)job.Get("result"));
                    return;

                default:
                    SessionState.EraseString(Key);
                    return;
            }
        }

        static JsonObject Failure(string message)
        {
            var result = new VfxResult("capture");
            result.Error(message);
            return result.ToJsonObject();
        }

        static void ExitWith(JsonObject job, JsonObject result)
        {
            job.Set("result", result);
            job.Set("phase", "exiting");
            Save(job);
            if (EditorApplication.isPlaying)
                EditorApplication.ExitPlaymode();
        }

        /// <summary>Edit mode again: writes the result file (and the batch output), clears the job.</summary>
        static void Conclude(JsonObject job, JsonObject result)
        {
            SessionState.EraseString(Key);
            result = result ?? Failure("the capture ended without a result");
            result.Set("pending", false);
            result.Set("job", job.Get("id"));
            var text = Json.Write(result);
            var utf8 = new UTF8Encoding(false);
            var resultFile = job.Get("result_file") as string;
            if (!string.IsNullOrEmpty(resultFile))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(resultFile));
                // Written through a temporary name and moved, so a reader never sees half a file.
                var temporary = resultFile + ".tmp";
                File.WriteAllText(temporary, text, utf8);
                if (File.Exists(resultFile))
                    File.Delete(resultFile);
                File.Move(temporary, resultFile);
            }

            var ok = result.Get("ok") is bool b && b;
            Debug.Log($"[vfxcli] capture job {job.Get("id")} ended, ok {ok}: {resultFile}");
            if (job.Get("batch") is bool batch && batch)
            {
                var batchOut = job.Get("batch_out") as string;
                if (!string.IsNullOrEmpty(batchOut))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(batchOut));
                    File.WriteAllText(batchOut, text, utf8);
                }
                EditorApplication.Exit(ok ? VfxCli.ExitOk : VfxCli.ExitFailed);
            }
        }
    }
}
