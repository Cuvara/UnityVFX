using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>The envelope of <see cref="VfxCli.Run"/> and <see cref="VfxCli.BatchMain"/>, and the JSON helper.</summary>
    public sealed class VfxCliTests
    {
        [Test]
        public void Registry_HasCatalogAndDescribe()
        {
            Assert.That(VfxVerbRegistry.Names, Does.Contain("catalog").And.Contain("describe"));
            Assert.That(VfxVerbRegistry.Problems, Is.Empty);
            Assert.That(VfxVerbRegistry.Names, Is.Ordered.Using((System.Collections.Generic.IComparer<string>)System.StringComparer.Ordinal));
        }

        [Test]
        public void Envelope_StartsWithOkVerbErrorsWarnings()
        {
            var result = TestJson.Result("no-such-verb", "{}");
            Assert.That(result.Take(4).Select(p => p.Key), Is.EqualTo(new[] { "ok", "verb", "errors", "warnings" }));
        }

        [Test]
        public void UnknownVerb_IsOkFalseAndListsTheValidVerbs()
        {
            var result = TestJson.Result("no-such-verb", "{}");
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            Assert.That(result.Get("verb"), Is.EqualTo("no-such-verb"));
            var errors = TestJson.Strings(result, "errors");
            Assert.That(errors, Has.Count.EqualTo(1));
            StringAssert.Contains("catalog", errors[0]);
            StringAssert.Contains("describe", errors[0]);
            Assert.That(TestJson.Strings(result, "valid_verbs"), Does.Contain("catalog").And.Contain("describe"));
        }

        [Test]
        public void EmptyVerb_IsOkFalse()
        {
            var result = TestJson.Result(null, null);
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            Assert.That(result.Get("verb"), Is.EqualTo(string.Empty));
        }

        [TestCase("{")]
        [TestCase("[1, 2]")]
        [TestCase("\"text\"")]
        [TestCase("{\"filter\": 3}")]
        public void BadArguments_AreOkFalse_NeverAThrow(string args)
        {
            JsonObject result = null;
            Assert.DoesNotThrow(() => result = TestJson.Result("catalog", args));
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            Assert.That(TestJson.Strings(result, "errors"), Has.Count.GreaterThan(0));
        }

        [Test]
        public void MissingRequiredArgument_IsOkFalse()
        {
            var result = TestJson.Result("describe", "{}");
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            StringAssert.Contains("asset", TestJson.Strings(result, "errors")[0]);
        }

        [Test]
        public void UnusedArgument_IsAWarning()
        {
            var result = TestJson.Ok("catalog", "{\"filter\": \"VFXBasicSpawner\", \"bogus\": 1}");
            Assert.That(TestJson.Strings(result, "warnings").Any(w => w.Contains("'bogus'")), Is.True);
        }

        [Test]
        public void BatchMain_WithoutVerb_IsUsageAndWritesTheEnvelope()
        {
            var folder = TempFolder();
            var outPath = Path.Combine(folder, "out.json");
            var code = VfxCli.BatchMain(new[] { "Unity", "-vfxOut", outPath });
            Assert.That(code, Is.EqualTo(VfxCli.ExitUsage));
            var result = (JsonObject)Json.Parse(File.ReadAllText(outPath));
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            StringAssert.Contains("catalog", TestJson.Strings(result, "errors")[0]);
        }

        [Test]
        public void BatchMain_RunsAVerbFromFiles()
        {
            var folder = TempFolder();
            var argsPath = Path.Combine(folder, "args.json");
            var outPath = Path.Combine(folder, "out.json");
            File.WriteAllText(argsPath, "{\"filter\": \"VFXBasicSpawner\"}");
            var code = VfxCli.BatchMain(new[] { "Unity", "-vfxVerb", "catalog", "-vfxArgs", argsPath, "-vfxOut", outPath });
            Assert.That(code, Is.EqualTo(VfxCli.ExitOk));
            var result = (JsonObject)Json.Parse(File.ReadAllText(outPath));
            Assert.That(result.Get("ok"), Is.EqualTo(true));
            Assert.That(File.ReadAllBytes(outPath).Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "no BOM");

            code = VfxCli.BatchMain(new[] { "Unity", "-vfxVerb", "no-such-verb", "-vfxOut", outPath });
            Assert.That(code, Is.EqualTo(VfxCli.ExitFailed));
        }

        [Test]
        public void Json_RoundTripsAndWritesStableText()
        {
            var value = new JsonObject
            {
                { "b", true },
                { "a", new List<object> { 1L, 2.5, "x\"y\n", null } },
                { "f", 0.1f },
                { "nan", float.NaN },
                { "sorted", new Dictionary<string, object> { { "z", 1 }, { "m", 2 } } },
            };
            var text = Json.Write(value);
            Assert.That(text, Is.EqualTo("{\"b\":true,\"a\":[1,2.5,\"x\\\"y\\n\",null],\"f\":0.1,\"nan\":\"NaN\",\"sorted\":{\"m\":2,\"z\":1}}"));
            var parsed = (JsonObject)Json.Parse(text);
            Assert.That(Json.Write(parsed), Is.EqualTo(text));
            Assert.That(Json.Parse("{\"n\": -12, \"d\": 1e3, \"u\": \"\\u00e9\"}") is JsonObject o && (long)o.Get("n") == -12 && (double)o.Get("d") == 1000.0 && (string)o.Get("u") == "\u00e9");
            Assert.Throws<System.FormatException>(() => Json.Parse("{\"a\": 1,}"));
            Assert.Throws<System.FormatException>(() => Json.Parse("{\"a\": 1, \"a\": 2}"));
        }

        static string TempFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "cuvara-vfx-tests", System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        [OneTimeTearDown]
        public void DeleteTempFolders()
        {
            var root = Path.Combine(Path.GetTempPath(), "cuvara-vfx-tests");
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
