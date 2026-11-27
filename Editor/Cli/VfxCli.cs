using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// The agent entry point of com.cuvara.vfx: one verb, JSON in, JSON out.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Run"/> is called through Unity-MCP (<c>script-execute</c>) when an editor has the
    /// project open; <see cref="Batch"/> is the <c>-executeMethod</c> entry of a batch-mode editor. Both give
    /// <c>{"ok", "verb", "errors", "warnings", ...payload}</c>. <c>Tools~/vfxcli.py</c> picks the path.</para>
    /// <para>Verbs and formats: <c>Documentation~/DESIGN.md</c>, section "Agent interface".</para>
    /// </remarks>
    public static class VfxCli
    {
        /// <summary>Exit code of <see cref="Batch"/>: the verb reported <c>"ok": true</c>.</summary>
        public const int ExitOk = 0;

        /// <summary>Exit code of <see cref="Batch"/>: the verb reported <c>"ok": false</c>.</summary>
        public const int ExitFailed = 1;

        /// <summary>Exit code of <see cref="Batch"/>: the command line was not usable (no verb, unreadable args file).</summary>
        public const int ExitUsage = 2;

        /// <summary>
        /// Return value of <see cref="BatchMain"/> when the verb left a pending job (<c>capture</c>): the
        /// editor does not exit; the job writes <c>-vfxOut</c> and exits with 0 or 1 when it ends.
        /// </summary>
        internal const int ExitPending = -1;

        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Runs one verb and returns its result as compact JSON. Never throws: an exception becomes
        /// <c>"ok": false</c> with the message in <c>errors</c>; an unknown verb lists the valid verbs.
        /// </summary>
        /// <param name="verb">Verb name, for example <c>catalog</c>.</param>
        /// <param name="jsonArgs">A JSON object, or null or empty for no arguments.</param>
        public static string Run(string verb, string jsonArgs)
        {
            VfxResult result;
            try
            {
                result = Execute(verb, jsonArgs);
            }
            catch (Exception exception)
            {
                result = new VfxResult(verb);
                result.Error(Describe(exception));
            }

            try
            {
                return result.ToJson();
            }
            catch (Exception exception)
            {
                var fallback = new VfxResult(verb);
                fallback.Error("the result could not be written as JSON: " + Describe(exception));
                return fallback.ToJson();
            }
        }

        /// <summary>Runs one verb and returns the result object (used by <see cref="Run"/> and the tests).</summary>
        internal static VfxResult Execute(string verb, string jsonArgs)
        {
            var result = new VfxResult(verb);
            IVfxVerb handler;
            try
            {
                handler = VfxVerbRegistry.Find(verb);
                foreach (var problem in VfxVerbRegistry.Problems)
                    result.Warning(problem);
            }
            catch (Exception exception)
            {
                result.Error("the verb registry could not be built: " + Describe(exception));
                return result;
            }

            if (handler == null)
            {
                var names = VfxVerbRegistry.Names;
                result.Error(string.IsNullOrEmpty(verb)
                    ? "no verb given; valid verbs: " + string.Join(", ", names)
                    : $"unknown verb '{verb}'; valid verbs: " + string.Join(", ", names));
                result.Set("valid_verbs", names.ToList());
                return result;
            }

            try
            {
                var args = VfxArgs.Parse(jsonArgs);
                handler.Execute(args, result);
                foreach (var key in args.UnreadKeys)
                    result.Warning($"argument '{key}' is not used by '{handler.Name}' and was ignored");
            }
            catch (VfxArgumentException exception)
            {
                result.Error(exception.Message);
            }
            catch (Exception exception)
            {
                result.Error(Describe(exception));
            }
            return result;
        }

        /// <summary>
        /// <c>-executeMethod Cuvara.VFX.Editor.VfxCli.Batch -vfxVerb &lt;verb&gt; [-vfxArgs &lt;file.json&gt;] [-vfxOut &lt;file.json&gt;]</c>.
        /// Writes the result to <c>-vfxOut</c> (or the log when absent) and exits the editor with
        /// 0 (ok), 1 (ok false) or 2 (usage). A verb that leaves a pending job (<c>capture</c>) does not
        /// exit here: the job writes <c>-vfxOut</c> and exits the editor when it ends.
        /// </summary>
        public static void Batch()
        {
            int code;
            try
            {
                code = BatchMain(Environment.GetCommandLineArgs());
            }
            catch (Exception exception)
            {
                Debug.LogError("[vfxcli] " + Describe(exception));
                code = ExitFailed;
            }
            if (code == ExitPending)
                return;
            EditorApplication.Exit(code);
        }

        /// <summary>The body of <see cref="Batch"/> without the exit, for tests.</summary>
        internal static int BatchMain(IReadOnlyList<string> commandLine)
        {
            var verb = Option(commandLine, "-vfxVerb");
            var argsPath = Option(commandLine, "-vfxArgs");
            var outPath = Option(commandLine, "-vfxOut");

            string jsonArgs = null;
            string usageError = null;
            if (string.IsNullOrEmpty(verb))
            {
                usageError = "usage: -executeMethod Cuvara.VFX.Editor.VfxCli.Batch -vfxVerb <verb> [-vfxArgs <file.json>] [-vfxOut <file.json>]; valid verbs: " +
                             string.Join(", ", VfxVerbRegistry.Names);
            }
            else if (!string.IsNullOrEmpty(argsPath))
            {
                try
                {
                    jsonArgs = File.ReadAllText(argsPath, Encoding.UTF8);
                }
                catch (Exception exception)
                {
                    usageError = $"cannot read -vfxArgs '{argsPath}': {exception.Message}";
                }
            }

            string output;
            int code;
            if (usageError != null)
            {
                var usage = new VfxResult(verb);
                usage.Error(usageError);
                output = usage.ToJson();
                code = ExitUsage;
            }
            else
            {
                output = Run(verb, jsonArgs);
                var parsed = Json.Parse(output) as JsonObject;
                code = parsed != null && parsed.Get("ok") is bool ok && ok ? ExitOk : ExitFailed;
                if (code == ExitOk && parsed.Get("pending") is bool pending && pending && Capture.VfxCaptureJob.AttachBatch(outPath))
                {
                    Debug.Log($"[vfxcli] {verb} is pending: the job writes {outPath} and exits the editor when it ends");
                    return ExitPending;
                }
            }

            if (!string.IsNullOrEmpty(outPath))
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(outPath));
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(outPath, output, Utf8NoBom);
                Debug.Log($"[vfxcli] {verb} exit {code}: wrote {outPath}");
            }
            else
            {
                Debug.Log($"[vfxcli] {verb} exit {code}: {output}");
            }
            return code;
        }

        static string Option(IReadOnlyList<string> commandLine, string name)
        {
            for (int i = 0; i < commandLine.Count - 1; i++)
                if (string.Equals(commandLine[i], name, StringComparison.OrdinalIgnoreCase))
                    return commandLine[i + 1];
            return null;
        }

        static string Describe(Exception exception)
        {
            while (exception is System.Reflection.TargetInvocationException && exception.InnerException != null)
                exception = exception.InnerException;
            return $"{exception.GetType().Name}: {exception.Message}";
        }
    }
}
