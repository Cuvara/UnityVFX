using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace Cuvara.VFX.Editor.Capture
{
    /// <summary>
    /// The validated arguments of one capture, kept as plain data so a capture job survives the domain
    /// reload of entering play mode (it is stored as JSON in <see cref="SessionState"/>).
    /// </summary>
    public sealed class VfxCaptureRequest
    {
        /// <summary>Default seed when the arguments give none.</summary>
        public const long DefaultSeed = 1234;

        /// <summary>Default delta time: 1/60 s.</summary>
        public const double DefaultDeltaTime = 1.0 / 60.0;

        /// <summary>Default frame size.</summary>
        public const int DefaultWidth = 640, DefaultHeight = 360;

        /// <summary>Default interval of the alive-count probes between samples, in seconds.</summary>
        public const double DefaultProbeInterval = 0.05;

        /// <summary>
        /// Default number of frames the effect is held still before its alive count is read. VFX Graph
        /// refreshes <c>aliveParticleCount</c> every 60 frames (Documentation~/AUTHORING.md, Pitfalls), so
        /// a value read after more than 60 frames is the count of the held state.
        /// </summary>
        public const int DefaultSettleFrames = 70;

        /// <summary>Smallest accepted settle: one full readback period of 60 frames and a frame of latency.</summary>
        public const int MinSettleFrames = 62;

        /// <summary>Default upper bound of the simulated time, in seconds.</summary>
        public const double DefaultMaxSeconds = 10.0;

        /// <summary>Default number of columns of the contact sheet.</summary>
        public const int DefaultColumns = 4;

        /// <summary>Most samples one capture takes.</summary>
        public const int MaxSamples = 64;

        /// <summary>Number of samples when <c>times</c> is not given.</summary>
        public const int DefaultSampleCount = 8;

        /// <summary>Project path of the effect (<c>Assets/...</c> or <c>Packages/...</c>).</summary>
        public string Effect;

        /// <summary><c>prefab</c> or <c>vfx</c>.</summary>
        public string Kind;

        /// <summary>The <c>startSeed</c> of every <see cref="VisualEffect"/> of the effect.</summary>
        public long Seed = DefaultSeed;

        /// <summary>Sample times in seconds, ascending, at least 0.01 s apart.</summary>
        public List<double> Times = new List<double>();

        /// <summary>Camera preset name.</summary>
        public string Camera = VfxCameraPreset.DefaultName;

        /// <summary>Lighting preset name.</summary>
        public string Lighting = VfxLightingPreset.DefaultName;

        /// <summary>Absolute output folder.</summary>
        public string Out;

        /// <summary><c>Time.captureDeltaTime</c> during the capture.</summary>
        public double DeltaTime = DefaultDeltaTime;

        /// <summary>Frame width in pixels.</summary>
        public int Width = DefaultWidth;

        /// <summary>Frame height in pixels.</summary>
        public int Height = DefaultHeight;

        /// <summary>Interval of the alive-count probes, in seconds.</summary>
        public double ProbeInterval = DefaultProbeInterval;

        /// <summary>Frames the effect is held still before a count is read.</summary>
        public int SettleFrames = DefaultSettleFrames;

        /// <summary>Upper bound of the simulated time, in seconds.</summary>
        public double MaxSeconds = DefaultMaxSeconds;

        /// <summary>Columns of the contact sheet.</summary>
        public int Columns = DefaultColumns;

        /// <summary>Project path of the <c>.vfxrecipe.json</c> next to the effect's <c>.vfx</c>, or null.</summary>
        public string RecipePath;

        /// <summary>SHA-256 of that recipe file, or null.</summary>
        public string RecipeSha256;

        /// <summary>The sample frame of a time: <c>round(t / delta_time)</c>.</summary>
        public int FrameOf(double seconds) => (int)Math.Round(seconds / DeltaTime, MidpointRounding.AwayFromZero);

        /// <summary>The file name of the frame sampled at <paramref name="seconds"/>, for example <c>frame-0.10.png</c>.</summary>
        public static string FrameFile(double seconds) => "frame-" + seconds.ToString("0.00", CultureInfo.InvariantCulture) + ".png";

        /// <summary>
        /// Reads and checks the arguments of the <c>capture</c> verb. Returns null and adds errors to
        /// <paramref name="result"/> when they are not usable. Must run in the editor (asset database).
        /// </summary>
        public static VfxCaptureRequest Parse(VfxArgs args, VfxResult result)
        {
            var request = new VfxCaptureRequest();
            var effectArgument = args.GetString("effect", required: true);
            request.Effect = Verbs.DescribeVerb.ToProjectPath(effectArgument);
            string assetPath = null;
            if (request.Effect.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                request.Kind = "prefab";
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(request.Effect);
                if (root == null)
                    result.Error($"effect '{effectArgument}' is not a prefab inside the project or a package of it");
                else
                {
                    var effects = root.GetComponentsInChildren<VisualEffect>(true);
                    if (effects.Length == 0)
                        result.Error($"prefab '{request.Effect}' has no VisualEffect component");
                    else if (effects.Any(e => e.visualEffectAsset == null))
                        result.Error($"a VisualEffect of prefab '{request.Effect}' has no asset");
                    else
                        assetPath = AssetDatabase.GetAssetPath(effects[0].visualEffectAsset);
                }
            }
            else if (request.Effect.EndsWith(".vfx", StringComparison.OrdinalIgnoreCase))
            {
                request.Kind = "vfx";
                if (AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(request.Effect) == null)
                    result.Error($"effect '{effectArgument}' is not a VisualEffectAsset inside the project or a package of it");
                else
                    assetPath = request.Effect;
            }
            else
            {
                result.Error($"effect '{effectArgument}' must be a .prefab that holds a VisualEffect or a .vfx asset");
            }

            double recipeDuration = 1.0;
            if (assetPath != null)
            {
                var recipePath = assetPath.Substring(0, assetPath.Length - ".vfx".Length) + VfxRecipeBuilder.RecipeSuffix;
                var file = VfxProjectPaths.ToFullPath(recipePath);
                if (File.Exists(file))
                {
                    request.RecipePath = recipePath;
                    var bytes = File.ReadAllBytes(file);
                    request.RecipeSha256 = VfxRecipeBuilder.Sha256(bytes);
                    try
                    {
                        if (Json.Parse(System.Text.Encoding.UTF8.GetString(bytes)) is JsonObject recipe &&
                            recipe.Get("meta") is JsonObject meta && meta.Get("duration_seconds") is object duration &&
                            (duration is double || duration is long))
                            recipeDuration = Convert.ToDouble(duration, CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        result.Warning($"recipe '{recipePath}' is not valid JSON; its duration is not used for the default times");
                    }
                }
            }

            request.Seed = args.GetLong("seed", DefaultSeed);
            if (request.Seed < 0 || request.Seed > uint.MaxValue)
                result.Error($"argument 'seed' must be between 0 and {uint.MaxValue} (VisualEffect.startSeed is a uint)");

            request.DeltaTime = args.GetDouble("delta_time", DefaultDeltaTime);
            if (!(request.DeltaTime > 0.0 && request.DeltaTime <= 0.1))
                result.Error("argument 'delta_time' must be above 0 and at most 0.1 s");

            request.MaxSeconds = args.GetDouble("max_seconds", DefaultMaxSeconds);
            if (!(request.MaxSeconds > 0.0 && request.MaxSeconds <= 60.0))
                result.Error("argument 'max_seconds' must be above 0 and at most 60");

            request.Camera = args.GetString("camera") ?? VfxCameraPreset.DefaultName;
            if (VfxCameraPreset.Get(request.Camera) == null)
                result.Error($"unknown camera '{request.Camera}'; valid cameras: {string.Join(", ", VfxCameraPreset.Names)}");

            request.Lighting = args.GetString("lighting") ?? VfxLightingPreset.DefaultName;
            if (VfxLightingPreset.Get(request.Lighting) == null)
                result.Error($"unknown lighting '{request.Lighting}'; valid lightings: {string.Join(", ", VfxLightingPreset.Names)}");

            request.Width = (int)args.GetLong("width", DefaultWidth);
            request.Height = (int)args.GetLong("height", DefaultHeight);
            if (request.Width < 64 || request.Width > 2048 || request.Height < 64 || request.Height > 2048)
                result.Error("arguments 'width' and 'height' must be between 64 and 2048");

            request.Columns = (int)args.GetLong("columns", DefaultColumns);
            if (request.Columns < 1 || request.Columns > 16)
                result.Error("argument 'columns' must be between 1 and 16");

            request.ProbeInterval = args.GetDouble("probe_interval", DefaultProbeInterval);
            if (!(request.ProbeInterval >= request.DeltaTime && request.ProbeInterval <= 1.0))
                result.Error("argument 'probe_interval' must be between delta_time and 1 s");

            request.SettleFrames = (int)args.GetLong("settle_frames", DefaultSettleFrames);
            if (request.SettleFrames < MinSettleFrames || request.SettleFrames > 600)
                result.Error($"argument 'settle_frames' must be between {MinSettleFrames} and 600 (VFX Graph refreshes the alive count every 60 frames)");

            var rawTimes = args.GetRaw("times");
            if (rawTimes == null)
            {
                var duration = Math.Max(recipeDuration, request.DeltaTime);
                for (int i = 0; i < DefaultSampleCount; i++)
                    request.Times.Add(Math.Round(duration * i / (DefaultSampleCount - 1), 2));
            }
            else if (rawTimes is List<object> list)
            {
                foreach (var item in list)
                {
                    if (item is double || item is long)
                        request.Times.Add(Convert.ToDouble(item, CultureInfo.InvariantCulture));
                    else
                        result.Error("argument 'times' must be an array of numbers (seconds)");
                }
            }
            else
            {
                result.Error("argument 'times' must be an array of numbers (seconds)");
            }

            request.Times = request.Times.Distinct().OrderBy(t => t).ToList();
            if (request.Times.Count == 0)
                result.Error("argument 'times' must hold at least one time");
            if (request.Times.Count > MaxSamples)
                result.Error($"argument 'times' holds {request.Times.Count} times; at most {MaxSamples}");
            if (request.Times.Any(t => t < 0.0 || t > request.MaxSeconds))
                result.Error($"every time must be between 0 and max_seconds ({request.MaxSeconds.ToString(CultureInfo.InvariantCulture)} s)");
            var files = request.Times.Select(FrameFile).ToList();
            var duplicate = files.GroupBy(f => f).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                result.Error($"two times give the same frame file '{duplicate.Key}'; times must be at least 0.01 s apart");
            if (request.DeltaTime > 0.0)
            {
                var frames = request.Times.Select(request.FrameOf).ToList();
                if (frames.Distinct().Count() != frames.Count)
                    result.Error("two times fall on the same frame at this delta_time; space them further apart");
            }

            var outArgument = args.GetString("out");
            request.Out = string.IsNullOrEmpty(outArgument) ? DefaultOut(request) : Path.GetFullPath(outArgument);
            request.Out = request.Out.Replace('\\', '/').TrimEnd('/');
            if (IsImported(request.Out))
                result.Error($"out '{request.Out}' is inside the project's Assets or a package: Unity would import the PNGs; use a folder such as <project>/Captures");

            return result.Ok ? request : null;
        }

        /// <summary>
        /// True when Unity would import files written to <paramref name="folder"/>: it maps to a path under
        /// <c>Assets/</c> or a package with no component that Unity skips (one ending in <c>~</c> or
        /// starting with <c>.</c>, such as the repository's <c>Sandbox~/Captures</c>).
        /// </summary>
        static bool IsImported(string folder)
        {
            var mapped = VfxProjectPaths.ToProjectPath(folder + "/stats.json");
            if (mapped == null)
                return false;
            var parts = mapped.Split('/');
            return !parts.Take(parts.Length - 1).Any(p => p.EndsWith("~", StringComparison.Ordinal) || p.StartsWith(".", StringComparison.Ordinal));
        }

        /// <summary><c>&lt;project&gt;/Captures/&lt;effect name&gt;/seed-&lt;seed&gt;-&lt;camera&gt;-&lt;lighting&gt;</c>.</summary>
        static string DefaultOut(VfxCaptureRequest request)
        {
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var name = Path.GetFileNameWithoutExtension(request.Effect ?? "effect");
            return Path.Combine(project, "Captures", name, $"seed-{request.Seed}-{request.Camera}-{request.Lighting}");
        }

        /// <summary>The request as JSON (for <see cref="SessionState"/>).</summary>
        public JsonObject ToJson() => new JsonObject
        {
            { "effect", Effect },
            { "kind", Kind },
            { "seed", Seed },
            { "times", Times.Cast<object>().ToList() },
            { "camera", Camera },
            { "lighting", Lighting },
            { "out", Out },
            { "delta_time", DeltaTime },
            { "width", (long)Width },
            { "height", (long)Height },
            { "probe_interval", ProbeInterval },
            { "settle_frames", (long)SettleFrames },
            { "max_seconds", MaxSeconds },
            { "columns", (long)Columns },
            { "recipe_path", RecipePath },
            { "recipe_sha256", RecipeSha256 },
        };

        /// <summary>The request written by <see cref="ToJson"/>.</summary>
        public static VfxCaptureRequest FromJson(JsonObject json)
        {
            double Number(string key) => Convert.ToDouble(json.Get(key), CultureInfo.InvariantCulture);
            return new VfxCaptureRequest
            {
                Effect = json.Get("effect") as string,
                Kind = json.Get("kind") as string,
                Seed = Convert.ToInt64(json.Get("seed"), CultureInfo.InvariantCulture),
                Times = ((List<object>)json.Get("times")).Select(t => Convert.ToDouble(t, CultureInfo.InvariantCulture)).ToList(),
                Camera = json.Get("camera") as string,
                Lighting = json.Get("lighting") as string,
                Out = json.Get("out") as string,
                DeltaTime = Number("delta_time"),
                Width = (int)Number("width"),
                Height = (int)Number("height"),
                ProbeInterval = Number("probe_interval"),
                SettleFrames = (int)Number("settle_frames"),
                MaxSeconds = Number("max_seconds"),
                Columns = (int)Number("columns"),
                RecipePath = json.Get("recipe_path") as string,
                RecipeSha256 = json.Get("recipe_sha256") as string,
            };
        }
    }
}
