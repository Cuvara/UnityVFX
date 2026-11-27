using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace Cuvara.VFX.Editor.Capture
{
    /// <summary>
    /// One capture in play mode: builds the <see cref="VfxCaptureStage"/>, plays the effect with a fixed
    /// seed and a fixed <c>Time.captureDeltaTime</c>, reads the alive count and the frames at fixed
    /// simulation frames, and writes the frames, <c>contact-sheet.png</c> and <c>stats.json</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Clock.</b> A player-loop step at the start of each frame (before <c>EarlyUpdate</c>)
    /// drives the capture; it sees the frame before it fully simulated and rendered. The effect's own
    /// clock advances only while its <c>playRate</c> is 1: one simulation frame of
    /// <see cref="VfxCaptureRequest.DeltaTime"/> per player frame.</para>
    /// <para><b>Checkpoints.</b> At every sample frame and every probe frame the effect is held still
    /// (<c>playRate</c> 0) for <see cref="VfxCaptureRequest.SettleFrames"/> frames, because VFX Graph
    /// refreshes <c>aliveParticleCount</c> from the GPU only every 60 frames. The count read after that
    /// is the count of the held state; a count of -1 is never recorded (the hold is extended, then the
    /// capture fails). At a sample frame the camera's render target is read at the end of the hold.</para>
    /// <para>Two captures with the same effect, seed and arguments therefore read the same counts.</para>
    /// </remarks>
    public sealed class VfxCaptureSession
    {
        /// <summary>Schema id of <c>stats.json</c>.</summary>
        public const string Schema = "cuvara.vfx-capture/1";

        /// <summary>File name of the statistics.</summary>
        public const string StatsFile = "stats.json";

        /// <summary>File name of the contact sheet.</summary>
        public const string ContactSheetFile = "contact-sheet.png";

        /// <summary>Gap between contact sheet cells, in pixels.</summary>
        public const int SheetGap = 4;

        static readonly Color32 SheetBackground = new Color32(24, 24, 24, 255);

        sealed class Sample
        {
            public double Time;
            public int Frame;
            public int Count = -1;
            public string File;
            public string Sha256;
            public double MeanLuminance;
            public double MaxLuminance;
            public Color32[] Pixels;
        }

        sealed class SystemStats
        {
            public string Name;
            public long Capacity;
            public long Peak;
        }

        enum Phase
        {
            Running,
            Holding,
            Done,
        }

        static VfxCaptureSession s_Active;

        readonly VfxCaptureRequest m_Request;
        readonly VfxResult m_Result = new VfxResult("capture");
        readonly List<Sample> m_Samples;
        readonly Dictionary<int, Sample> m_SampleByFrame;
        readonly List<SystemStats> m_Systems = new List<SystemStats>();
        readonly List<string> m_SystemNames = new List<string>();
        readonly int m_ProbeFrames;
        readonly int m_LastSampleFrame;
        readonly int m_MaxFrame;
        readonly int m_FrameBudget;

        VfxCaptureStage m_Stage;
        GameObject m_EffectRoot;
        VisualEffect[] m_Effects = new VisualEffect[0];
        Texture2D m_Readback;
        float m_PreviousCaptureDeltaTime;
        bool m_PreviousRunInBackground;
        bool m_Hooked;

        Phase m_Phase = Phase.Holding;
        int m_SimFrame;
        int m_Held;
        int m_Ticks;
        long m_Peak;
        int m_Probes;
        double? m_Duration;
        bool m_HasBounds;
        Bounds m_Bounds;
        string m_VfxGraphVersion;
        JsonObject m_Characters;
        List<object> m_Binders = new List<object>();

        VfxCaptureSession(VfxCaptureRequest request)
        {
            m_Request = request;
            m_Samples = request.Times.Select(t => new Sample { Time = t, Frame = request.FrameOf(t), File = VfxCaptureRequest.FrameFile(t) }).ToList();
            m_SampleByFrame = m_Samples.ToDictionary(s => s.Frame);
            m_ProbeFrames = Math.Max(1, (int)Math.Round(request.ProbeInterval / request.DeltaTime));
            m_LastSampleFrame = m_Samples.Max(s => s.Frame);
            m_MaxFrame = Math.Max(m_LastSampleFrame, (int)Math.Ceiling(request.MaxSeconds / request.DeltaTime));
            var checkpoints = m_MaxFrame / m_ProbeFrames + m_Samples.Count + 2;
            m_FrameBudget = m_MaxFrame + checkpoints * request.SettleFrames * 4 + 600;
        }

        /// <summary>True when the capture ended (with or without errors); then <see cref="Result"/> is final.</summary>
        public bool IsDone => m_Phase == Phase.Done;

        /// <summary>The verb result: <c>ok</c>, errors, warnings and, when ok, every field of <c>stats.json</c>.</summary>
        public VfxResult Result => m_Result;

        /// <summary>The capture that runs now, or null.</summary>
        public static VfxCaptureSession Active => s_Active;

        /// <summary>
        /// Starts a capture. Needs play mode and no other running capture; a failure to start gives a
        /// session that is already done with the error in its result.
        /// </summary>
        public static VfxCaptureSession Start(VfxCaptureRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var session = new VfxCaptureSession(request);
            if (!Application.isPlaying)
            {
                session.Fail("capture needs play mode");
                return session;
            }
            if (s_Active != null)
            {
                session.Fail("another capture is running");
                return session;
            }
            try
            {
                session.Begin();
            }
            catch (Exception exception)
            {
                session.Fail("capture could not start: " + exception.GetType().Name + ": " + exception.Message);
            }
            return session;
        }

        /// <summary>Stops the capture with an error (for example on a timeout of the job).</summary>
        public void Abort(string reason)
        {
            if (!IsDone)
                Fail(reason);
        }

        void Begin()
        {
            s_Active = this;
            m_VfxGraphVersion = Bridge.VfxGraphBridge.InstalledVersion;
            PrepareOutFolder();

            m_PreviousCaptureDeltaTime = Time.captureDeltaTime;
            m_PreviousRunInBackground = Application.runInBackground;
            // An unfocused editor does not advance play mode unless the player runs in the background
            // (Documentation~/AUTHORING.md, Pitfalls).
            Application.runInBackground = true;
            EditorApplication.isPaused = false;
            Time.captureDeltaTime = (float)m_Request.DeltaTime;

            m_Stage = new VfxCaptureStage(VfxCameraPreset.Get(m_Request.Camera), VfxLightingPreset.Get(m_Request.Lighting), m_Request.Width, m_Request.Height);
            m_Readback = new Texture2D(m_Request.Width, m_Request.Height, TextureFormat.RGBAFloat, false, true) { name = "CuvaraVfxCaptureReadback" };

            if (m_Request.Kind == "prefab")
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(m_Request.Effect);
                if (prefab == null)
                    throw new InvalidOperationException($"prefab '{m_Request.Effect}' could not be loaded");
                m_EffectRoot = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
                m_EffectRoot.name = prefab.name;
            }
            else
            {
                var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(m_Request.Effect);
                if (asset == null)
                    throw new InvalidOperationException($"VisualEffectAsset '{m_Request.Effect}' could not be loaded");
                m_EffectRoot = new GameObject(asset.name);
                m_EffectRoot.AddComponent<VisualEffect>().visualEffectAsset = asset;
            }
            m_Stage.Own(m_EffectRoot);

            m_Effects = m_EffectRoot.GetComponentsInChildren<VisualEffect>(true);
            if (m_Effects.Length == 0)
                throw new InvalidOperationException($"'{m_Request.Effect}' has no VisualEffect");

            // Characters and binding (optional): placed and bound before the effect's first step, so a
            // spawn at t = 0 reads the bound positions.
            var cast = VfxCaptureCast.Place(m_Request, m_Stage, m_EffectRoot, m_Result);
            if (cast != null)
            {
                var outside = cast.CountOutsideFrame(m_Stage.Camera);
                if (outside > 0)
                    m_Result.Warning($"{outside} character(s) are not fully inside the {m_Request.Camera} frame (stats.json characters[].in_frame)");
                m_Characters = cast.CharactersJson(m_Stage.Camera);
                m_Binders = cast.BindersJson();
            }
            foreach (var effect in m_Effects)
            {
                effect.resetSeedOnPlay = false;
                effect.startSeed = (uint)m_Request.Seed;
                effect.playRate = 0f;
                effect.Reinit();
            }

            Hook(true);
            m_Phase = Phase.Holding;
            m_SimFrame = 0;
            m_Held = 0;
        }

        void PrepareOutFolder()
        {
            Directory.CreateDirectory(m_Request.Out);
            foreach (var file in Directory.GetFiles(m_Request.Out))
            {
                var name = Path.GetFileName(file);
                if (name == StatsFile || name == ContactSheetFile || (name.StartsWith("frame-", StringComparison.Ordinal) && name.EndsWith(".png", StringComparison.Ordinal)))
                    File.Delete(file);
            }
        }

        // ------------------------------------------------------------------ player loop

        struct CaptureStep
        {
        }

        void Hook(bool on)
        {
            if (on == m_Hooked)
                return;
            m_Hooked = on;
            SetStep(Step, on);
        }

        /// <summary>
        /// Puts <paramref name="step"/> first in <c>EarlyUpdate</c>, or removes it (and only it). At that
        /// point of a frame the previous frame has simulated, rendered and finished.
        /// </summary>
        static void SetStep(PlayerLoopSystem.UpdateFunction step, bool add)
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (loop.subSystemList == null)
                return;
            for (int i = 0; i < loop.subSystemList.Length; i++)
            {
                if (loop.subSystemList[i].type != typeof(UnityEngine.PlayerLoop.EarlyUpdate))
                    continue;
                var systems = new List<PlayerLoopSystem>(loop.subSystemList[i].subSystemList ?? new PlayerLoopSystem[0]);
                systems.RemoveAll(s => s.type == typeof(CaptureStep) && s.updateDelegate == step);
                if (add)
                    systems.Insert(0, new PlayerLoopSystem { type = typeof(CaptureStep), updateDelegate = step });
                loop.subSystemList[i].subSystemList = systems.ToArray();
            }
            PlayerLoop.SetPlayerLoop(loop);
        }

        void Step()
        {
            if (IsDone)
                return;
            try
            {
                StepCore();
            }
            catch (Exception exception)
            {
                Fail("capture failed: " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        void StepCore()
        {
            if (!Application.isPlaying)
            {
                Fail("play mode ended during the capture");
                return;
            }
            if (++m_Ticks > m_FrameBudget)
            {
                Fail($"capture did not finish within {m_FrameBudget} frames");
                return;
            }
            if (m_Effects.Any(e => e == null))
            {
                Fail("the effect was destroyed during the capture");
                return;
            }

            if (m_Phase == Phase.Running)
            {
                // The frame before this step simulated one frame of the effect.
                m_SimFrame++;
                if (IsCheckpoint(m_SimFrame))
                {
                    SetPlayRate(0f);
                    m_Phase = Phase.Holding;
                    m_Held = 0;
                }
                return;
            }

            // Holding: the effect has not moved since the checkpoint frame.
            m_Held++;
            if (m_Held < m_Request.SettleFrames)
                return;
            var counts = m_Effects.Select(e => e.aliveParticleCount).ToList();
            if (counts.Any(c => c < 0))
            {
                if (m_Held < m_Request.SettleFrames * 4)
                    return;
                Fail($"aliveParticleCount stayed -1 for {m_Held} frames at t={Seconds(m_SimFrame)} s");
                return;
            }
            Record(m_SimFrame, counts.Sum());
            if (IsDone)
                return;
            if (ShouldStop())
            {
                Finish();
                return;
            }
            SetPlayRate(1f);
            m_Phase = Phase.Running;
        }

        bool IsCheckpoint(int frame) => frame % m_ProbeFrames == 0 || m_SampleByFrame.ContainsKey(frame) || frame >= m_MaxFrame;

        bool ShouldStop()
        {
            if (m_SimFrame >= m_MaxFrame)
                return true;
            if (m_SimFrame < m_LastSampleFrame)
                return false;
            return m_Duration.HasValue || m_Peak == 0;
        }

        void SetPlayRate(float rate)
        {
            foreach (var effect in m_Effects)
                effect.playRate = rate;
        }

        double Seconds(int frame) => Math.Round(frame * m_Request.DeltaTime, 6);

        void Record(int frame, long alive)
        {
            m_Probes++;
            m_Peak = Math.Max(m_Peak, alive);
            var awake = m_Effects.Any(e => e.HasAnySystemAwake());
            if (!m_Duration.HasValue && m_Peak > 0 && alive == 0 && !awake)
                m_Duration = Seconds(frame);

            foreach (var effect in m_Effects)
            {
                effect.GetParticleSystemNames(m_SystemNames);
                foreach (var systemName in m_SystemNames)
                {
                    var info = effect.GetParticleSystemInfo(systemName);
                    var name = m_Effects.Length == 1 ? systemName : HierarchyPath(effect.transform, m_EffectRoot.transform) + "/" + systemName;
                    var stats = m_Systems.FirstOrDefault(s => s.Name == name);
                    if (stats == null)
                    {
                        stats = new SystemStats { Name = name };
                        m_Systems.Add(stats);
                    }
                    stats.Capacity = info.capacity;
                    if (info.aliveCount > 0)
                    {
                        stats.Peak = Math.Max(stats.Peak, info.aliveCount);
                        Encapsulate(info.bounds, effect.transform.localToWorldMatrix);
                    }
                }
            }

            if (m_SampleByFrame.TryGetValue(frame, out var sample))
            {
                sample.Count = (int)alive;
                ReadFrame(sample);
            }
        }

        void Encapsulate(Bounds local, Matrix4x4 toWorld)
        {
            var min = local.min;
            var max = local.max;
            for (int i = 0; i < 8; i++)
            {
                var corner = toWorld.MultiplyPoint3x4(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
                if (!m_HasBounds)
                {
                    m_Bounds = new Bounds(corner, Vector3.zero);
                    m_HasBounds = true;
                }
                else
                {
                    m_Bounds.Encapsulate(corner);
                }
            }
        }

        static string HierarchyPath(Transform transform, Transform root)
        {
            var names = new List<string>();
            for (var t = transform; t != null && t != root; t = t.parent)
                names.Add(t.name);
            names.Reverse();
            return names.Count == 0 ? root.name : string.Join("/", names);
        }

        // ------------------------------------------------------------------ frames

        void ReadFrame(Sample sample)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = m_Stage.Target;
            m_Readback.ReadPixels(new Rect(0, 0, m_Request.Width, m_Request.Height), 0, 0, false);
            RenderTexture.active = previous;

            var pixels = m_Readback.GetPixels();
            var encoded = new Color32[pixels.Length];
            double sum = 0.0, max = 0.0;
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                double luminance = 0.2126 * p.r + 0.7152 * p.g + 0.0722 * p.b;
                if (double.IsNaN(luminance))
                    luminance = 0.0;
                sum += luminance;
                if (luminance > max)
                    max = luminance;
                encoded[i] = new Color32(ToSrgbByte(p.r), ToSrgbByte(p.g), ToSrgbByte(p.b), 255);
            }
            sample.MeanLuminance = Math.Round(sum / pixels.Length, 6);
            sample.MaxLuminance = Math.Round(max, 6);
            sample.Pixels = encoded;
            var bytes = EncodePng(encoded, m_Request.Width, m_Request.Height);
            File.WriteAllBytes(Path.Combine(m_Request.Out, sample.File), bytes);
            sample.Sha256 = VfxRecipeBuilder.Sha256(bytes);
        }

        static byte ToSrgbByte(float linear)
        {
            if (float.IsNaN(linear) || linear <= 0f)
                return 0;
            var srgb = Mathf.LinearToGammaSpace(Mathf.Min(linear, 1f));
            return (byte)Mathf.Clamp(Mathf.RoundToInt(srgb * 255f), 0, 255);
        }

        static byte[] EncodePng(Color32[] pixels, int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            try
            {
                texture.SetPixels32(pixels);
                return texture.EncodeToPNG();
            }
            finally
            {
                Object.Destroy(texture);
            }
        }

        byte[] ContactSheet(out int columns)
        {
            int w = m_Request.Width, h = m_Request.Height;
            columns = Math.Min(m_Request.Columns, m_Samples.Count);
            var rows = (m_Samples.Count + columns - 1) / columns;
            var sheetWidth = columns * w + (columns + 1) * SheetGap;
            var sheetHeight = rows * h + (rows + 1) * SheetGap;
            var sheet = new Color32[sheetWidth * sheetHeight];
            for (int i = 0; i < sheet.Length; i++)
                sheet[i] = SheetBackground;
            for (int index = 0; index < m_Samples.Count; index++)
            {
                int column = index % columns, row = index / columns;
                // Row 0 is the top row; texture rows count from the bottom.
                int x0 = SheetGap + column * (w + SheetGap);
                int y0 = SheetGap + (rows - 1 - row) * (h + SheetGap);
                var pixels = m_Samples[index].Pixels;
                for (int y = 0; y < h; y++)
                    Array.Copy(pixels, y * w, sheet, (y0 + y) * sheetWidth + x0, w);
            }
            return EncodePng(sheet, sheetWidth, sheetHeight);
        }

        // ------------------------------------------------------------------ end

        void Finish()
        {
            try
            {
                var sheetBytes = ContactSheet(out var columns);
                File.WriteAllBytes(Path.Combine(m_Request.Out, ContactSheetFile), sheetBytes);
                var stats = Stats(VfxRecipeBuilder.Sha256(sheetBytes), columns);
                File.WriteAllText(Path.Combine(m_Request.Out, StatsFile), Json.Write(stats, true) + "\n", new System.Text.UTF8Encoding(false));
                foreach (var pair in stats)
                    m_Result.Set(pair.Key, pair.Value);
                m_Result.Set("out", m_Request.Out);
                m_Result.Set("stats_file", Path.Combine(m_Request.Out, StatsFile).Replace('\\', '/'));
                m_Result.Set("probes", (long)m_Probes);
                if (m_Peak == 0)
                    m_Result.Warning("no particle was alive at any checkpoint: check the effect, its seed and the sample times");
                if (!m_Duration.HasValue)
                    m_Result.Warning($"the effect did not end within {Seconds(m_SimFrame)} s (looping, or max_seconds too short): duration_seconds is null");
            }
            catch (Exception exception)
            {
                m_Result.Error("capture could not write its output: " + exception.GetType().Name + ": " + exception.Message);
            }
            End();
        }

        JsonObject Stats(string sheetSha256, int columns)
        {
            List<object> Vector(Vector3 v) => new List<object> { Round(v.x), Round(v.y), Round(v.z) };
            return new JsonObject
            {
                { "schema", Schema },
                { "effect", m_Request.Effect },
                { "recipe_sha256", m_Request.RecipeSha256 },
                { "seed", m_Request.Seed },
                { "delta_time", Math.Round(m_Request.DeltaTime, 6) },
                { "camera", m_Request.Camera },
                { "lighting", m_Request.Lighting },
                { "unity", Application.unityVersion },
                { "vfx_graph", m_VfxGraphVersion },
                { "duration_seconds", m_Duration.HasValue ? (object)m_Duration.Value : null },
                { "alive", new JsonObject
                    {
                        { "peak", m_Peak },
                        { "samples", m_Samples.Select(s => (object)new JsonObject { { "t", s.Time }, { "count", (long)s.Count } }).ToList() },
                    }
                },
                { "bounds", m_HasBounds ? new JsonObject { { "center", Vector(m_Bounds.center) }, { "size", Vector(m_Bounds.size) } } : null },
                { "systems", m_Systems.Select(s => (object)new JsonObject { { "name", s.Name }, { "capacity", s.Capacity }, { "peak", s.Peak } }).ToList() },
                { "frames", m_Samples.Select(s => (object)new JsonObject
                    {
                        { "t", s.Time },
                        { "file", s.File },
                        { "sha256", s.Sha256 },
                        { "mean_luminance", s.MeanLuminance },
                        { "max_luminance", s.MaxLuminance },
                    }).ToList()
                },
                { "contact_sheet", new JsonObject { { "file", ContactSheetFile }, { "sha256", sheetSha256 }, { "columns", (long)columns } } },
                { "resolution", new List<object> { (long)m_Request.Width, (long)m_Request.Height } },
                { "probe_interval", Math.Round(m_ProbeFrames * m_Request.DeltaTime, 6) },
                { "settle_frames", (long)m_Request.SettleFrames },
                { "characters", m_Characters },
                { "binders", m_Binders },
            };
        }

        static double Round(float value) => Math.Round(value, 4);

        void Fail(string message)
        {
            m_Result.Error(message);
            End();
        }

        void End()
        {
            if (m_Phase == Phase.Done)
                return;
            m_Phase = Phase.Done;
            // The step may be running inside the player loop now: take it out after this frame.
            if (m_Hooked)
                EditorApplication.delayCall += () => Hook(false);
            if (m_Stage != null)
            {
                try
                {
                    m_Stage.Dispose();
                }
                catch (Exception exception)
                {
                    m_Result.Warning("the capture stage could not be removed: " + exception.Message);
                }
                m_Stage = null;
            }
            if (m_Readback != null)
                Object.Destroy(m_Readback);
            m_Readback = null;
            foreach (var sample in m_Samples)
                sample.Pixels = null;
            if (s_Active == this)
            {
                Time.captureDeltaTime = m_PreviousCaptureDeltaTime;
                Application.runInBackground = m_PreviousRunInBackground;
                s_Active = null;
            }
        }

        /// <summary>For messages: the frames of the request, by time.</summary>
        public override string ToString() => string.Join(", ", m_Samples.Select(s => s.Time.ToString(CultureInfo.InvariantCulture) + "s@" + s.Frame));
    }
}
