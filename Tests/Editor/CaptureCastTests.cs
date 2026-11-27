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
using Object = UnityEngine.Object;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// The optional characters of <c>capture</c>: the inputs <c>character</c>, <c>target_count</c>,
    /// <c>target_spacing</c>, <c>source_socket</c>, <c>target_socket</c>, the lane, the binding of a
    /// <see cref="ChainLightningBinder"/> and the <c>characters</c> and <c>binders</c> fields of
    /// <c>stats.json</c>, on the ChainTargets fixture (it exposes the binder's default properties).
    /// </summary>
    /// <remarks>
    /// The character is a stand-in built by the test (a cube with the five sockets) and saved as a
    /// temporary prefab, so the tests pass in a project where the Showcase sample is not imported. The
    /// play-mode test follows the pattern of <see cref="CaptureTests"/>: it enters play mode with
    /// <see cref="EnterPlayMode"/> and creates everything it needs after that yield.
    /// </remarks>
    public sealed class CaptureCastTests
    {
        const string Fixture = "Packages/com.cuvara.vfx/Tests/Fixtures/ChainTargets/ChainTargets.vfx";
        const string SchemaPath = "Packages/com.cuvara.vfx/Schemas~/vfx-capture.schema.json";
        const string TempFolder = "Assets/CuvaraVfxCaptureCastTests";
        const string StandInPath = TempFolder + "/StandIn.prefab";
        const string MissingPath = TempFolder + "/DoesNotExist.prefab";
        const double TimeoutSeconds = 300;
        const int BurstCount = 48;

        static readonly Vector3 HandRight = new Vector3(0.3f, 0.9f, 0f);
        static readonly Vector3 Chest = new Vector3(0f, 1.4f, 0.15f);

        static string OutFolder(string name) =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "CuvaraVfxCaptureCastTests", name)).Replace('\\', '/');

        static VfxCaptureRequest Request(JsonObject args, out VfxResult parse)
        {
            parse = new VfxResult("capture");
            return VfxCaptureRequest.Parse(VfxArgs.Parse(Json.Write(args)), parse);
        }

        static JsonObject Args(string character, long targets, string outName) => new JsonObject
        {
            { "effect", Fixture },
            { "seed", 1234L },
            { "times", new List<object> { 0.0, 0.3 } },
            { "camera", "gameplay" },
            { "lighting", "dusk" },
            { "out", OutFolder(outName) },
            { "width", 256L },
            { "height", 144L },
            { "character", character },
            { "target_count", targets },
        };

        /// <summary>A stand-in character: a 1.8 m cube body with the five sockets, saved as a prefab.</summary>
        static void CreateStandIn()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", TempFolder.Substring("Assets/".Length));
            var root = new GameObject("StandIn");
            try
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.4f, 1.8f, 0.3f);
                Socket(root.transform, VfxSocket.Head, new Vector3(0f, 1.7f, 0f));
                Socket(root.transform, VfxSocket.Chest, Chest);
                Socket(root.transform, VfxSocket.HandLeft, new Vector3(-0.3f, 0.9f, 0f));
                Socket(root.transform, VfxSocket.HandRight, HandRight);
                Socket(root.transform, VfxSocket.Feet, Vector3.zero);
                PrefabUtility.SaveAsPrefabAsset(root, StandInPath, out var saved);
                Assert.That(saved, Is.True, "stand-in prefab saved");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void Socket(Transform root, string name, Vector3 position)
        {
            var socket = new GameObject("VfxSocket_" + name);
            socket.transform.SetParent(root, false);
            socket.transform.localPosition = position;
            socket.AddComponent<VfxSocket>().SocketName = name;
        }

        [UnityTearDown]
        public IEnumerator LeavePlayModeAndDeleteTheStandIn()
        {
            VfxCaptureSession.Active?.Abort("test teardown");
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();
            if (AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.DeleteAsset(TempFolder);
        }

        // ------------------------------------------------------------------ capture (play mode)

        [UnityTest]
        public IEnumerator StandInWithThreeTargets_BindsTheChain_TwiceTheSame_AndAMissingPrefabPlacesThePlaceholder()
        {
            GraphicsRequirement.IgnoreUnlessGpuSimulationRuns();
            CreateStandIn();
            yield return new EnterPlayMode();

            // Everything below runs after the domain reload of entering play mode.
            var requests = new[]
            {
                Request(Args(StandInPath, 3, "stand-in-a"), out var parseA),
                Request(Args(StandInPath, 3, "stand-in-b"), out var parseB),
                Request(Args(MissingPath, 1, "missing"), out var parseMissing),
            };
            Assert.That(requests[0], Is.Not.Null, "parse: " + string.Join(" | ", parseA.Errors));
            Assert.That(requests[1], Is.Not.Null, "parse: " + string.Join(" | ", parseB.Errors));
            Assert.That(requests[2], Is.Not.Null, "parse: " + string.Join(" | ", parseMissing.Errors));
            Assert.That(requests[0].CharacterPlaced, Is.EqualTo(StandInPath));
            Assert.That(requests[2].CharacterPlaced, Is.EqualTo(VfxCaptureRequest.Placeholder));

            var results = new List<VfxResult>();
            foreach (var request in requests)
            {
                var session = VfxCaptureSession.Start(request);
                var started = EditorApplication.timeSinceStartup;
                while (!session.IsDone && EditorApplication.timeSinceStartup - started < TimeoutSeconds)
                    yield return null;
                Assert.That(session.IsDone, Is.True, request.Out + ": capture did not finish");
                Assert.That(session.Result.Ok, Is.True, request.Out + ": " + string.Join(" | ", session.Result.Errors));
                results.Add(session.Result);
                yield return null;
            }

            var a = ReadStats(requests[0].Out);
            var b = ReadStats(requests[1].Out);
            var missing = ReadStats(requests[2].Out);
            foreach (var (stats, folder) in new[] { (a, requests[0].Out), (b, requests[1].Out), (missing, requests[2].Out) })
            {
                var problems = TestJsonSchema.Validate((JsonObject)Json.Parse(File.ReadAllText(VfxProjectPaths.ToFullPath(SchemaPath))), stats);
                Assert.That(problems, Is.Empty, folder + ": stats.json against Schemas~/vfx-capture.schema.json");
                foreach (var frame in ((List<object>)stats.Get("frames")).Cast<JsonObject>())
                {
                    var file = Path.Combine(folder, (string)frame.Get("file"));
                    Assert.That(File.Exists(file), Is.True, file);
                    Assert.That(VfxRecipeBuilder.Sha256(File.ReadAllBytes(file)), Is.EqualTo(frame.Get("sha256")), file);
                }
                Assert.That(File.Exists(Path.Combine(folder, VfxCaptureSession.ContactSheetFile)), Is.True);
            }

            // Determinism: one seed twice, the same counts.
            Assert.That(Peak(a), Is.EqualTo(BurstCount), "one burst of 48 quads");
            Assert.That(Peak(b), Is.EqualTo(Peak(a)), "same seed, same peak");
            Assert.That(Counts(b), Is.EqualTo(Counts(a)), "same seed, same counts at every sample");
            Assert.That(Counts(a), Has.All.GreaterThanOrEqualTo(0), "no -1 count");

            // The lane: caster at x = -n*s/2 facing +X, targets s apart facing the caster.
            var characters = (JsonObject)a.Get("characters");
            Assert.That(characters.Get("character"), Is.EqualTo(StandInPath));
            Assert.That(characters.Get("placed"), Is.EqualTo(StandInPath));
            Assert.That(characters.Get("source_socket"), Is.EqualTo("hand_r"));
            Assert.That(characters.Get("target_socket"), Is.EqualTo("chest"));
            Assert.That(characters.Get("target_count"), Is.EqualTo(3L));
            Assert.That(Convert.ToDouble(characters.Get("target_spacing"), CultureInfo.InvariantCulture), Is.EqualTo(3.5));
            var caster = (JsonObject)characters.Get("caster");
            var casterPosition = new Vector3(-5.25f, 0f, 0f);
            var expectedSource = casterPosition + Quaternion.Euler(0f, 90f, 0f) * HandRight;
            AssertVector(caster.Get("position"), casterPosition, "caster position");
            AssertVector(caster.Get("socket"), expectedSource, "caster hand_r");
            Assert.That(caster.Get("socket_found"), Is.EqualTo(true));
            Assert.That(caster.Get("in_frame"), Is.EqualTo(true), "caster inside the gameplay frame");
            var targets = ((List<object>)characters.Get("targets")).Cast<JsonObject>().ToList();
            Assert.That(targets, Has.Count.EqualTo(3));
            var expectedTargets = new List<Vector3>();
            for (int i = 0; i < 3; i++)
            {
                var position = new Vector3(-1.75f + 3.5f * i, 0f, 0f);
                var chest = position + Quaternion.Euler(0f, -90f, 0f) * Chest;
                expectedTargets.Add(chest);
                AssertVector(targets[i].Get("position"), position, "target " + (i + 1) + " position");
                AssertVector(targets[i].Get("socket"), chest, "target " + (i + 1) + " chest");
                Assert.That(targets[i].Get("socket_found"), Is.EqualTo(true));
                Assert.That(targets[i].Get("in_frame"), Is.EqualTo(true), "target " + (i + 1) + " inside the gameplay frame");
            }

            // The binder the capture added to the .vfx, and what the graph holds after binding.
            var binders = ((List<object>)a.Get("binders")).Cast<JsonObject>().ToList();
            Assert.That(binders, Has.Count.EqualTo(1));
            Assert.That(binders[0].Get("added"), Is.EqualTo(true));
            Assert.That(binders[0].Get("hop_count"), Is.EqualTo(3L));
            Assert.That(Convert.ToInt64(binders[0].Get("graph_hop_count"), CultureInfo.InvariantCulture), Is.EqualTo(3L), "HopCount in the graph");
            AssertVector(binders[0].Get("source"), expectedSource, "SourcePosition in the graph");
            var bound = ((List<object>)binders[0].Get("targets")).ToList();
            Assert.That(bound, Has.Count.EqualTo(3));
            for (int i = 0; i < 3; i++)
                AssertVector(bound[i], expectedTargets[i], "TargetPosition" + i + " in the graph");
            // The particles lie on the line from the source to the last target (TargetPosition7).
            Assert.That(a.Get("bounds"), Is.Not.Null);
            Assert.That(Json.Write(b.Get("binders")), Is.EqualTo(Json.Write(a.Get("binders"))), "same binding twice");
            Assert.That(results[0].Warnings, Is.Empty, string.Join(" | ", results[0].Warnings));

            // A prefab path that does not exist: the placeholder, a warning, and stats.json says so.
            var fallback = (JsonObject)missing.Get("characters");
            Assert.That(fallback.Get("character"), Is.EqualTo(MissingPath));
            Assert.That(fallback.Get("placed"), Is.EqualTo(VfxCaptureRequest.Placeholder));
            Assert.That(((List<object>)fallback.Get("targets")), Has.Count.EqualTo(1));
            Assert.That(((JsonObject)fallback.Get("caster")).Get("socket_found"), Is.EqualTo(true), "the placeholder has hand_r");
            Assert.That(results[2].Warnings, Has.Some.Contains("does not exist; the capture places the built-in placeholder"));
            var fallbackBinders = ((List<object>)missing.Get("binders")).Cast<JsonObject>().ToList();
            Assert.That(fallbackBinders, Has.Count.EqualTo(1));
            Assert.That(fallbackBinders[0].Get("hop_count"), Is.EqualTo(1L));
        }

        static JsonObject ReadStats(string folder)
        {
            var path = Path.Combine(folder, VfxCaptureSession.StatsFile);
            Assert.That(File.Exists(path), Is.True, path);
            return (JsonObject)Json.Parse(File.ReadAllText(path));
        }

        static long Peak(JsonObject stats) => Convert.ToInt64(((JsonObject)stats.Get("alive")).Get("peak"), CultureInfo.InvariantCulture);

        static List<long> Counts(JsonObject stats) =>
            ((List<object>)((JsonObject)stats.Get("alive")).Get("samples")).Cast<JsonObject>()
            .Select(s => Convert.ToInt64(s.Get("count"), CultureInfo.InvariantCulture)).ToList();

        static void AssertVector(object value, Vector3 expected, string what)
        {
            Assert.That(value, Is.InstanceOf<List<object>>(), what);
            var list = ((List<object>)value).Select(v => Convert.ToDouble(v, CultureInfo.InvariantCulture)).ToList();
            Assert.That(list, Has.Count.EqualTo(3), what);
            Assert.That(list[0], Is.EqualTo(expected.x).Within(1e-3), what + " x");
            Assert.That(list[1], Is.EqualTo(expected.y).Within(1e-3), what + " y");
            Assert.That(list[2], Is.EqualTo(expected.z).Within(1e-3), what + " z");
        }

        // ------------------------------------------------------------------ arguments (edit mode)

        [Test]
        public void Parse_WithoutCharacterInputs_PlacesNoCharacter()
        {
            var request = Request(new JsonObject { { "effect", Fixture } }, out var result);
            Assert.That(request, Is.Not.Null, string.Join(" | ", result.Errors));
            Assert.That(request.Character, Is.Null);
            Assert.That(request.CharacterPlaced, Is.Null);
            Assert.That(request.TargetCount, Is.EqualTo(0));
            Assert.That(VfxCaptureCast.IsRequested(request), Is.False);
            Assert.That(result.Warnings, Is.Empty);
        }

        [Test]
        public void Parse_Placeholder_HasTheDefaults()
        {
            var request = Request(new JsonObject { { "effect", Fixture }, { "character", "placeholder" }, { "target_count", 3L } }, out var result);
            Assert.That(request, Is.Not.Null, string.Join(" | ", result.Errors));
            Assert.That(request.Character, Is.EqualTo(VfxCaptureRequest.Placeholder));
            Assert.That(request.CharacterPlaced, Is.EqualTo(VfxCaptureRequest.Placeholder));
            Assert.That(request.CharacterFallback, Is.Null);
            Assert.That(request.TargetCount, Is.EqualTo(3));
            Assert.That(request.TargetSpacing, Is.EqualTo(3.5));
            Assert.That(request.SourceSocket, Is.EqualTo("hand_r"));
            Assert.That(request.TargetSocket, Is.EqualTo("chest"));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(VfxCaptureCast.CasterX(3, 3.5), Is.EqualTo(-5.25f));
            Assert.That(VfxCaptureCast.CasterX(0, 3.5), Is.EqualTo(0f));
        }

        [Test]
        public void Parse_AMissingPrefab_PlacesThePlaceholder_WithAWarning()
        {
            var request = Request(new JsonObject { { "effect", Fixture }, { "character", MissingPath } }, out var result);
            Assert.That(request, Is.Not.Null, string.Join(" | ", result.Errors));
            Assert.That(request.Character, Is.EqualTo(MissingPath));
            Assert.That(request.CharacterPlaced, Is.EqualTo(VfxCaptureRequest.Placeholder));
            Assert.That(request.CharacterFallback, Does.Contain("does not exist"));
            Assert.That(result.Warnings, Has.Some.Contains("'" + MissingPath + "' does not exist; the capture places the built-in placeholder (VfxMannequin) instead"));
        }

        [TestCase(-1L, 3.5, "hand_r", "chest", "argument 'target_count' must be between 0 and 8")]
        [TestCase(9L, 3.5, "hand_r", "chest", "argument 'target_count' must be between 0 and 8")]
        [TestCase(3L, 0.4, "hand_r", "chest", "argument 'target_spacing' must be between 0.5 and 10 m")]
        [TestCase(3L, 10.5, "hand_r", "chest", "argument 'target_spacing' must be between 0.5 and 10 m")]
        [TestCase(3L, 3.5, "", "chest", "argument 'source_socket' must be a socket name")]
        [TestCase(3L, 3.5, "hand_r", " ", "argument 'target_socket' must be a socket name")]
        public void Parse_RejectsABadCharacterInput(long targets, double spacing, string sourceSocket, string targetSocket, string error)
        {
            var request = Request(new JsonObject
            {
                { "effect", Fixture },
                { "character", "placeholder" },
                { "target_count", targets },
                { "target_spacing", spacing },
                { "source_socket", sourceSocket },
                { "target_socket", targetSocket },
            }, out var result);
            Assert.That(request, Is.Null);
            Assert.That(result.Errors, Has.Some.Contains(error));
        }

        [Test]
        public void Parse_TargetsOrSocketsWithoutACharacter_AreAnError()
        {
            var request = Request(new JsonObject { { "effect", Fixture }, { "target_count", 2L }, { "source_socket", "hand_l" } }, out var result);
            Assert.That(request, Is.Null);
            Assert.That(result.Errors, Has.Some.Contains("argument(s) 'target_count', 'source_socket' need 'character'"));
        }

        [Test]
        public void Parse_ACharacterThatIsNotAPrefab_IsAnError()
        {
            var vfx = Request(new JsonObject { { "effect", Fixture }, { "character", Fixture } }, out var vfxResult);
            Assert.That(vfx, Is.Null);
            Assert.That(vfxResult.Errors, Has.Some.Contains("must be a .prefab path or \"placeholder\""));

            var empty = Request(new JsonObject { { "effect", Fixture }, { "character", "" } }, out var emptyResult);
            Assert.That(empty, Is.Null);
            Assert.That(emptyResult.Errors, Has.Some.Contains("argument 'character' must be a .prefab path or \"placeholder\""));
        }

        [Test]
        public void Request_WithCharacters_SurvivesTheJsonRoundTripOfTheJob()
        {
            var request = Request(new JsonObject
            {
                { "effect", Fixture },
                { "character", MissingPath },
                { "target_count", 4L },
                { "target_spacing", 2.5 },
                { "source_socket", "hand_l" },
                { "target_socket", "head" },
            }, out var result);
            Assert.That(request, Is.Not.Null, string.Join(" | ", result.Errors));
            var copy = VfxCaptureRequest.FromJson((JsonObject)Json.Parse(Json.Write(request.ToJson())));
            Assert.That(Json.Write(copy.ToJson()), Is.EqualTo(Json.Write(request.ToJson())));
            Assert.That(copy.TargetCount, Is.EqualTo(4));
            Assert.That(copy.TargetSpacing, Is.EqualTo(2.5));
            Assert.That(copy.SourceSocket, Is.EqualTo("hand_l"));
            Assert.That(copy.TargetSocket, Is.EqualTo("head"));
            Assert.That(copy.CharacterPlaced, Is.EqualTo(VfxCaptureRequest.Placeholder));
            Assert.That(copy.CharacterFallback, Is.Not.Null);
        }
    }
}
