using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Cuvara.VFX.Editor.Capture;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// <c>capture</c> on the BurstQuads fixture (one burst of 24 additive quads, capacity 32).
    /// </summary>
    /// <remarks>
    /// <para>The determinism test is an EditMode <see cref="UnityTestAttribute"/> that enters play mode
    /// with <see cref="EnterPlayMode"/> and runs <see cref="VfxCaptureSession"/> itself. The capture code
    /// is editor code (asset database, package versions) and needs play mode, so a PlayMode test
    /// assembly, which may not reference the editor assembly, cannot run it. Entering play mode reloads
    /// the domain: the test framework then resumes the enumerator at the instruction after the yield
    /// with its locals reset, so everything the test needs is created after that yield.</para>
    /// <para>The verb's own control flow across the reload (<see cref="VfxCaptureJob"/>, the pending
    /// result, the return to edit mode) is not run here: it would reload the domain under the test.
    /// It is checked through <c>Tools~/vfxcli.py capture</c> (Documentation~/CAPTURE.md).</para>
    /// </remarks>
    public sealed class CaptureTests
    {
        const string Fixture = "Packages/com.cuvara.vfx/Tests/Fixtures/BurstQuads/BurstQuads.vfx";
        const string FixtureRecipe = "Packages/com.cuvara.vfx/Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json";
        const string SchemaPath = "Packages/com.cuvara.vfx/Schemas~/vfx-capture.schema.json";
        const double TimeoutSeconds = 300;

        static string OutFolder(string name) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "CuvaraVfxCaptureTests", name)).Replace('\\', '/');

        static VfxCaptureRequest Request(string json, out VfxResult parse)
        {
            parse = new VfxResult("capture");
            return VfxCaptureRequest.Parse(VfxArgs.Parse(json), parse);
        }

        static string Args(long seed, string outName, string camera = "gameplay", string lighting = "dusk") => Json.Write(new JsonObject
        {
            { "effect", Fixture },
            { "seed", seed },
            { "times", new List<object> { 0.0, 0.1, 0.3, 0.6, 0.85 } },
            { "camera", camera },
            { "lighting", lighting },
            { "out", OutFolder(outName) },
            { "width", 256L },
            { "height", 144L },
        });

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            VfxCaptureSession.Active?.Abort("test teardown");
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator SameSeedTwice_GivesTheSamePeak_NoMinusOne_AndAllFiles()
        {
            // The capture simulates on the GPU and reads the render target back: ignored, with the
            // reason, on a runner without a graphics device or compute shaders (Documentation~/CI.md).
            GraphicsRequirement.IgnoreUnlessGpuSimulationRuns();
            yield return new EnterPlayMode();

            // Everything below runs after the domain reload of entering play mode.
            var first = Request(Args(1234, "seed-a"), out var parseFirst);
            Assert.That(first, Is.Not.Null, "parse: " + string.Join(" | ", parseFirst.Errors));
            var second = Request(Args(1234, "seed-b"), out var parseSecond);
            Assert.That(second, Is.Not.Null, "parse: " + string.Join(" | ", parseSecond.Errors));

            var sessionA = VfxCaptureSession.Start(first);
            var started = EditorApplication.timeSinceStartup;
            while (!sessionA.IsDone && EditorApplication.timeSinceStartup - started < TimeoutSeconds)
                yield return null;
            Assert.That(sessionA.IsDone, Is.True, "first capture did not finish");
            Assert.That(sessionA.Result.Ok, Is.True, "first capture: " + string.Join(" | ", sessionA.Result.Errors));

            yield return null;
            var sessionB = VfxCaptureSession.Start(second);
            started = EditorApplication.timeSinceStartup;
            while (!sessionB.IsDone && EditorApplication.timeSinceStartup - started < TimeoutSeconds)
                yield return null;
            Assert.That(sessionB.IsDone, Is.True, "second capture did not finish");
            Assert.That(sessionB.Result.Ok, Is.True, "second capture: " + string.Join(" | ", sessionB.Result.Errors));

            var a = ReadStats(first.Out);
            var b = ReadStats(second.Out);
            var peakA = Convert.ToInt64(((JsonObject)a.Get("alive")).Get("peak"), CultureInfo.InvariantCulture);
            var peakB = Convert.ToInt64(((JsonObject)b.Get("alive")).Get("peak"), CultureInfo.InvariantCulture);
            Assert.That(peakA, Is.GreaterThan(0), "the burst has particles");
            Assert.That(peakB, Is.EqualTo(peakA), "same seed, same peak");
            Assert.That(peakA, Is.LessThanOrEqualTo(32), "capacity of BurstQuads");
            Assert.That(Counts(b), Is.EqualTo(Counts(a)), "same seed, same counts at every sample");
            Assert.That(Counts(a).Concat(Counts(b)), Has.All.GreaterThanOrEqualTo(0), "no -1 count");

            foreach (var (stats, folder) in new[] { (a, first.Out), (b, second.Out) })
            {
                AssertFiles(stats, folder);
                AssertFields(stats);
            }
        }

        static JsonObject ReadStats(string folder)
        {
            var path = Path.Combine(folder, VfxCaptureSession.StatsFile);
            Assert.That(File.Exists(path), Is.True, path);
            return (JsonObject)Json.Parse(File.ReadAllText(path));
        }

        static List<long> Counts(JsonObject stats) =>
            ((List<object>)((JsonObject)stats.Get("alive")).Get("samples")).Cast<JsonObject>()
            .Select(s => Convert.ToInt64(s.Get("count"), CultureInfo.InvariantCulture)).ToList();

        static void AssertFiles(JsonObject stats, string folder)
        {
            var frames = ((List<object>)stats.Get("frames")).Cast<JsonObject>().ToList();
            Assert.That(frames.Select(f => (string)f.Get("file")), Is.EqualTo(new[] { "frame-0.00.png", "frame-0.10.png", "frame-0.30.png", "frame-0.60.png", "frame-0.85.png" }));
            foreach (var frame in frames)
            {
                var file = Path.Combine(folder, (string)frame.Get("file"));
                Assert.That(File.Exists(file), Is.True, file);
                Assert.That(VfxRecipeBuilder.Sha256(File.ReadAllBytes(file)), Is.EqualTo(frame.Get("sha256")), file);
            }
            var sheet = (JsonObject)stats.Get("contact_sheet");
            var sheetFile = Path.Combine(folder, VfxCaptureSession.ContactSheetFile);
            Assert.That(File.Exists(sheetFile), Is.True, sheetFile);
            Assert.That(VfxRecipeBuilder.Sha256(File.ReadAllBytes(sheetFile)), Is.EqualTo(sheet.Get("sha256")));
            Assert.That(Directory.GetFiles(folder, "frame-*.png").Length, Is.EqualTo(frames.Count), "one PNG per sample");
        }

        static void AssertFields(JsonObject stats)
        {
            var schema = (JsonObject)Json.Parse(File.ReadAllText(VfxProjectPaths.ToFullPath(SchemaPath)));
            var problems = TestJsonSchema.Validate(schema, stats);
            Assert.That(problems, Is.Empty, "stats.json against Schemas~/vfx-capture.schema.json");

            Assert.That(stats.Get("schema"), Is.EqualTo("cuvara.vfx-capture/1"));
            Assert.That(stats.Get("effect"), Is.EqualTo(Fixture));
            Assert.That(stats.Get("recipe_sha256"), Is.EqualTo(VfxRecipeBuilder.Sha256(File.ReadAllBytes(VfxProjectPaths.ToFullPath(FixtureRecipe)))));
            Assert.That(stats.Get("seed"), Is.EqualTo(1234L));
            Assert.That(Convert.ToDouble(stats.Get("delta_time"), CultureInfo.InvariantCulture), Is.EqualTo(1.0 / 60.0).Within(1e-6));
            Assert.That(stats.Get("camera"), Is.EqualTo("gameplay"));
            Assert.That(stats.Get("lighting"), Is.EqualTo("dusk"));
            Assert.That(stats.Get("unity"), Is.EqualTo(Application.unityVersion));
            Assert.That(stats.Get("vfx_graph"), Is.EqualTo(Bridge.VfxGraphBridge.InstalledVersion));
            // Lifetimes of BurstQuads are 0.6 to 0.9 s; the end is seen at a probe (0.05 s apart).
            Assert.That(Convert.ToDouble(stats.Get("duration_seconds"), CultureInfo.InvariantCulture), Is.InRange(0.6, 1.0));
            Assert.That(stats.Get("bounds"), Is.Not.Null);
            var systems = ((List<object>)stats.Get("systems")).Cast<JsonObject>().ToList();
            Assert.That(systems, Has.Count.EqualTo(1));
            Assert.That(systems[0].Get("capacity"), Is.EqualTo(32L));
            Assert.That(systems[0].Get("peak"), Is.EqualTo(((JsonObject)stats.Get("alive")).Get("peak")));
        }

        // ------------------------------------------------------------------ arguments (edit mode)

        [Test]
        public void Parse_UnknownCameraOrLighting_ListsTheValidNames()
        {
            var request = Request(Json.Write(new JsonObject { { "effect", Fixture }, { "camera", "top" }, { "lighting", "noon" } }), out var result);
            Assert.That(request, Is.Null);
            Assert.That(result.Errors, Has.Some.Contains("unknown camera 'top'; valid cameras: gameplay, close, side"));
            Assert.That(result.Errors, Has.Some.Contains("unknown lighting 'noon'; valid lightings: day, dusk, night"));
        }

        [Test]
        public void Parse_Defaults_SpreadEightTimesOverTheRecipeDuration_AndUseTheSandboxCapturesFolder()
        {
            var request = Request(Json.Write(new JsonObject { { "effect", Fixture } }), out var result);
            Assert.That(request, Is.Not.Null, string.Join(" | ", result.Errors));
            Assert.That(request.Seed, Is.EqualTo(VfxCaptureRequest.DefaultSeed));
            Assert.That(request.Camera, Is.EqualTo("gameplay"));
            Assert.That(request.Lighting, Is.EqualTo("dusk"));
            Assert.That(request.Times, Has.Count.EqualTo(8));
            Assert.That(request.Times.First(), Is.EqualTo(0.0));
            Assert.That(request.Times.Last(), Is.EqualTo(1.0), "meta.duration_seconds of BurstQuads");
            var project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            Assert.That(request.Out, Is.EqualTo(project + "/Captures/BurstQuads/seed-1234-gameplay-dusk"));
            Assert.That(request.RecipeSha256, Has.Length.EqualTo(64));
        }

        [Test]
        public void Parse_RejectsAnOutFolderInsideAssets_TimesTooClose_AndAShortSettle()
        {
            var inside = Path.GetFullPath(Path.Combine(Application.dataPath, "CaptureOut")).Replace('\\', '/');
            var request = Request(Json.Write(new JsonObject
            {
                { "effect", Fixture },
                { "out", inside },
                { "times", new List<object> { 0.1, 0.101 } },
                { "settle_frames", 10L },
            }), out var result);
            Assert.That(request, Is.Null);
            Assert.That(result.Errors, Has.Some.Contains("inside the project's Assets or a package"));
            Assert.That(result.Errors, Has.Some.Contains("at least 0.01 s apart"));
            Assert.That(result.Errors, Has.Some.Contains("'settle_frames' must be between 62"));
        }

        [Test]
        public void Parse_RejectsAnEffectThatIsNotAPrefabOrAVfx()
        {
            var request = Request(Json.Write(new JsonObject { { "effect", FixtureRecipe } }), out var result);
            Assert.That(request, Is.Null);
            Assert.That(result.Errors, Has.Some.Contains("must be a .prefab that holds a VisualEffect or a .vfx asset"));
        }

        [Test]
        public void Request_SurvivesTheJsonRoundTripOfTheJob()
        {
            var request = Request(Args(77, "roundtrip", "side", "night"), out var result);
            Assert.That(request, Is.Not.Null, string.Join(" | ", result.Errors));
            var copy = VfxCaptureRequest.FromJson((JsonObject)Json.Parse(Json.Write(request.ToJson())));
            Assert.That(Json.Write(copy.ToJson()), Is.EqualTo(Json.Write(request.ToJson())));
        }

        [Test]
        public void CaptureVerb_IsRegistered_AndRefusesBadArgumentsWithoutEnteringPlayMode()
        {
            Assert.That(VfxVerbRegistry.Names, Has.Member("capture"));
            var result = TestJson.Result("capture", Json.Write(new JsonObject { { "effect", Fixture }, { "camera", "top" } }));
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            Assert.That(EditorApplication.isPlayingOrWillChangePlaymode, Is.False);
            Assert.That(VfxCaptureJob.IsPending, Is.False);
        }
    }
}
