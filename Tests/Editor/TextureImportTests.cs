using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// <c>import-texture</c> on small PNGs the tests write themselves: determinism of the output bytes, the
    /// pixel steps on known pixels, the importer settings, and the failures that leave a target untouched.
    /// </summary>
    public sealed class TextureImportTests
    {
        const string Folder = "Assets/CuvaraVfxTextureTestTemp";
        const string Target = Folder + "/Out.png";

        string m_SourceFolder;

        [SetUp]
        public void CreateFolders()
        {
            DeleteFolders();
            m_SourceFolder = Path.Combine(Path.GetTempPath(), "cuvara-vfx-texture-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(m_SourceFolder);
        }

        [TearDown]
        public void DeleteFolders()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
            if (m_SourceFolder != null && Directory.Exists(m_SourceFolder))
                Directory.Delete(m_SourceFolder, true);
            m_SourceFolder = null;
        }

        // ------------------------------------------------------------ helpers

        /// <summary>Writes a PNG of RGBA pixels given top row first (as an image is read) and returns its absolute path.</summary>
        string WritePng(string name, int width, int height, params byte[][] topDownPixels)
        {
            Assert.That(topDownPixels.Length, Is.EqualTo(width * height));
            var rgba = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixel = topDownPixels[(row * width) + x];
                    var y = height - 1 - row;
                    Array.Copy(pixel, 0, rgba, ((y * width) + x) * 4, 4);
                }
            }
            var path = Path.Combine(m_SourceFolder, name).Replace('\\', '/');
            File.WriteAllBytes(path, new VfxPixels(width, height, rgba).EncodePng());
            return path;
        }

        static byte[] P(int r, int g, int b, int a = 255) => new[] { (byte)r, (byte)g, (byte)b, (byte)a };

        /// <summary>The pixel in column x, row from the top, of a PNG asset.</summary>
        static byte[] Pixel(VfxPixels image, int x, int rowFromTop)
        {
            var i = image.Index(x, image.Height - 1 - rowFromTop, 0);
            return image.Rgba.Skip(i).Take(4).ToArray();
        }

        static VfxPixels ReadTarget(string target = Target) => VfxPixels.DecodePng(File.ReadAllBytes(Path.GetFullPath(target)));

        static string Args(string source, string target, params object[] steps) => Json.Write(new JsonObject
        {
            { "source", source },
            { "target", target },
            { "transform", steps.ToList() },
        });

        static JsonObject Step(string name, params (string key, object value)[] parameters)
        {
            var step = new JsonObject { { "step", name } };
            foreach (var (key, value) in parameters)
                step.Add(key, value);
            return step;
        }

        static string Hash(string file) => VfxRecipeBuilder.Sha256(File.ReadAllBytes(file));

        static TextureImporter Importer(string target = Target) => (TextureImporter)AssetImporter.GetAtPath(target);

        string Gradient(string name, int width, int height)
        {
            var pixels = new List<byte[]>();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels.Add(P((x * 255) / Math.Max(1, width - 1), (y * 255) / Math.Max(1, height - 1), (x * y * 7) % 256));
            return WritePng(name, width, height, pixels.ToArray());
        }

        // ------------------------------------------------------------ codec and determinism

        [Test]
        public void Png_EncodeThenDecode_KeepsEveryPixelAndTheOrientation()
        {
            var source = WritePng("corners.png", 2, 2, P(255, 0, 0), P(0, 255, 0), P(0, 0, 255, 128), P(10, 20, 30, 0));
            var image = VfxPixels.DecodePng(File.ReadAllBytes(source));
            Assert.That(image.Width, Is.EqualTo(2));
            Assert.That(image.Height, Is.EqualTo(2));
            Assert.That(Pixel(image, 0, 0), Is.EqualTo(P(255, 0, 0)), "top left");
            Assert.That(Pixel(image, 1, 0), Is.EqualTo(P(0, 255, 0)), "top right");
            Assert.That(Pixel(image, 0, 1), Is.EqualTo(P(0, 0, 255, 128)), "bottom left");
            Assert.That(Pixel(image, 1, 1), Is.EqualTo(P(10, 20, 30, 0)), "bottom right");
        }

        [Test]
        public void SameSourceAndTransform_Twice_GiveTheSameOutputHash()
        {
            var source = Gradient("gradient.png", 12, 6);
            var args = Args(source, Target,
                Step("luminance_to_alpha", ("color", "unpremultiply")),
                "premultiply",
                Step("resize_pot", ("max_size", 8L)),
                Step("flipbook", ("columns", 2L), ("rows", 1L)),
                Step("wrap", ("u", "repeat")),
                "linear");

            var first = TestJson.Ok("import-texture", args);
            var firstFile = Hash(Path.GetFullPath(Target));
            Assert.That(first.Get("created"), Is.EqualTo(true));
            Assert.That(first.Get("source_sha256"), Is.EqualTo(Hash(source)));
            Assert.That(first.Get("output_sha256"), Is.EqualTo(firstFile));
            Assert.That(first.Get("size"), Is.EqualTo(new List<object> { 8L, 4L }));
            var guid = first.Get("guid");

            var second = TestJson.Ok("import-texture", args);
            Assert.That(second.Get("created"), Is.EqualTo(false));
            Assert.That(second.Get("guid"), Is.EqualTo(guid));
            Assert.That(second.Get("guid_stable"), Is.EqualTo(true));
            Assert.That(second.Get("previous_sha256"), Is.EqualTo(firstFile));
            Assert.That(second.Get("output_sha256"), Is.EqualTo(first.Get("output_sha256")));
            Assert.That(second.Get("source_sha256"), Is.EqualTo(first.Get("source_sha256")));
            Assert.That(Json.Write(second.Get("steps")), Is.EqualTo(Json.Write(first.Get("steps"))));

            var other = TestJson.Ok("import-texture", Args(source, Folder + "/Other.png",
                Step("luminance_to_alpha", ("color", "unpremultiply")), "premultiply", Step("resize_pot", ("max_size", 8L)),
                Step("flipbook", ("columns", 2L), ("rows", 1L)), Step("wrap", ("u", "repeat")), "linear"));
            Assert.That(other.Get("output_sha256"), Is.EqualTo(first.Get("output_sha256")), "a second target gets the same bytes");
        }

        // ------------------------------------------------------------ pixel steps

        [Test]
        public void LuminanceToAlpha_KnownPixels()
        {
            var source = WritePng("lum.png", 4, 1, P(255, 255, 255), P(0, 0, 0), P(255, 0, 0), P(0, 255, 0));
            TestJson.Ok("import-texture", Args(source, Target, "luminance_to_alpha"));
            var image = ReadTarget();
            Assert.That(Pixel(image, 0, 0), Is.EqualTo(P(255, 255, 255, 255)));
            Assert.That(Pixel(image, 1, 0), Is.EqualTo(P(0, 0, 0, 0)));
            Assert.That(Pixel(image, 2, 0), Is.EqualTo(P(255, 0, 0, 54)), "0.2126 * 255 = 54.2");
            Assert.That(Pixel(image, 3, 0), Is.EqualTo(P(0, 255, 0, 182)), "0.7152 * 255 = 182.4");

            var grey = WritePng("grey.png", 2, 1, P(64, 64, 64), P(0, 0, 255));
            TestJson.Ok("import-texture", Args(grey, Target, Step("luminance_to_alpha", ("color", "unpremultiply"))));
            image = ReadTarget();
            Assert.That(Pixel(image, 0, 0), Is.EqualTo(P(255, 255, 255, 64)), "grey on black becomes white with alpha");
            Assert.That(Pixel(image, 1, 0), Is.EqualTo(P(0, 0, 255, 18)), "0.0722 * 255 = 18.4; 255 * 255 / 18 clamps to 255");

            TestJson.Ok("import-texture", Args(grey, Target, Step("luminance_to_alpha", ("color", "white"))));
            image = ReadTarget();
            Assert.That(Pixel(image, 0, 0), Is.EqualTo(P(255, 255, 255, 64)));
            Assert.That(Pixel(image, 1, 0), Is.EqualTo(P(255, 255, 255, 18)));
        }

        [Test]
        public void Premultiply_KnownPixels()
        {
            var source = WritePng("pre.png", 3, 1, P(255, 128, 0, 128), P(200, 100, 50, 255), P(255, 255, 255, 0));
            var result = TestJson.Ok("import-texture", Args(source, Target, "premultiply"));
            var image = ReadTarget();
            Assert.That(Pixel(image, 0, 0), Is.EqualTo(P(128, 64, 0, 128)), "255 * 128 / 255 = 128; 128 * 128 / 255 = 64.25");
            Assert.That(Pixel(image, 1, 0), Is.EqualTo(P(200, 100, 50, 255)));
            Assert.That(Pixel(image, 2, 0), Is.EqualTo(P(0, 0, 0, 0)));
            Assert.That(((JsonObject)result.Get("import_settings")).Get("premultiplied"), Is.EqualTo(true));
        }

        [Test]
        public void PackChannels_KnownPixels()
        {
            var source = WritePng("base.png", 2, 1, P(10, 20, 30), P(200, 100, 50));
            var mask = WritePng("mask.png", 2, 1, P(1, 2, 3, 4), P(5, 6, 7, 8));
            var result = TestJson.Ok("import-texture", Args(source, Target, Step("pack_channels",
                ("r", new JsonObject { { "file", mask }, { "channel", "g" } }),
                ("g", 0.5),
                ("b", new JsonObject { { "channel", "r" } }),
                ("a", new JsonObject { { "channel", "luminance" } }))));
            var image = ReadTarget();
            Assert.That(Pixel(image, 0, 0), Is.EqualTo(P(2, 128, 10, 19)), "luminance of (10, 20, 30) = 19.1");
            Assert.That(Pixel(image, 1, 0), Is.EqualTo(P(6, 128, 200, 118)), "luminance of (200, 100, 50) = 118.2");

            var inputs = TestJson.Objects(result, "inputs");
            Assert.That(inputs, Has.Count.EqualTo(1));
            Assert.That(inputs[0].Get("sha256"), Is.EqualTo(Hash(mask)));

            TestJson.Ok("import-texture", Args(source, Target, Step("pack_channels", ("a", 0L), ("r", new JsonObject { { "channel", "b" } }))));
            image = ReadTarget();
            Assert.That(Pixel(image, 0, 0), Is.EqualTo(P(30, 20, 30, 0)), "unnamed channels are kept, swaps read the image before the step");
        }

        [Test]
        public void ResizePot_KnownPixels()
        {
            var source = WritePng("box.png", 4, 2,
                P(0, 0, 0), P(100, 0, 0), P(10, 0, 0), P(20, 0, 0),
                P(200, 0, 0), P(40, 0, 0), P(30, 0, 0), P(40, 0, 0));
            var result = TestJson.Ok("import-texture", Args(source, Target, Step("resize_pot", ("max_size", 2L))));
            var image = ReadTarget();
            Assert.That(image.Width, Is.EqualTo(2));
            Assert.That(image.Height, Is.EqualTo(1));
            Assert.That(Pixel(image, 0, 0), Is.EqualTo(P(85, 0, 0)), "(0 + 100 + 200 + 40) / 4");
            Assert.That(Pixel(image, 1, 0), Is.EqualTo(P(25, 0, 0)), "(10 + 20 + 30 + 40) / 4");
            var step = TestJson.Objects(result, "steps")[0];
            Assert.That(step.Get("from"), Is.EqualTo(new List<object> { 4L, 2L }));
            Assert.That(step.Get("to"), Is.EqualTo(new List<object> { 2L, 1L }));

            var flat = Enumerable.Repeat(P(90, 60, 30, 200), 15).ToArray();
            var odd = WritePng("odd.png", 5, 3, flat);
            result = TestJson.Ok("import-texture", Args(odd, Target, "resize_pot"));
            image = ReadTarget();
            Assert.That(result.Get("size"), Is.EqualTo(new List<object> { 4L, 4L }), "5 and 3 go to their nearest power of two");
            Assert.That(image.Rgba.Length, Is.EqualTo(64));
            for (int i = 0; i < image.Rgba.Length; i += 4)
                Assert.That(image.Rgba.Skip(i).Take(4).ToArray(), Is.EqualTo(P(90, 60, 30, 200)));
        }

        [TestCase(1, 1)]
        [TestCase(3, 4)]
        [TestCase(5, 4)]
        [TestCase(6, 8)]
        [TestCase(700, 512)]
        [TestCase(1000, 1024)]
        [TestCase(1024, 1024)]
        public void NearestPowerOfTwo_IsGeometric(int n, int expected)
        {
            Assert.That(VfxPixels.NearestPowerOfTwo(n), Is.EqualTo(expected));
        }

        // ------------------------------------------------------------ importer settings

        [Test]
        public void ImporterSettings_FollowLinearAndWrap()
        {
            var source = Gradient("mask.png", 4, 4);
            var result = TestJson.Ok("import-texture", Args(source, Target, Step("wrap", ("u", "repeat"), ("v", "clamp")), "linear"));
            var importer = Importer();
            Assert.That(importer.sRGBTexture, Is.False);
            Assert.That(importer.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.None), "an opaque image has no alpha source");
            Assert.That(importer.mipmapEnabled, Is.True);
            Assert.That(importer.npotScale, Is.EqualTo(TextureImporterNPOTScale.None));
            Assert.That(importer.maxTextureSize, Is.EqualTo(32));
            Assert.That(importer.textureCompression, Is.EqualTo(VfxTextureImport.Compression));
            var settings = (JsonObject)result.Get("import_settings");
            Assert.That(settings.Get("srgb"), Is.EqualTo(false));
            Assert.That(settings.Get("wrap_u"), Is.EqualTo("Repeat"));

            TestJson.Ok("import-texture", Args(source, Target, Step("wrap", ("mode", "repeat")), "srgb", "luminance_to_alpha"));
            importer = Importer();
            Assert.That(importer.sRGBTexture, Is.True);
            Assert.That(importer.wrapModeU, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.wrapModeV, Is.EqualTo(TextureWrapMode.Repeat));
            Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput));
            Assert.That(importer.alphaIsTransparency, Is.True, "straight alpha in sRGB");

            TestJson.Ok("import-texture", Args(source, Target, "luminance_to_alpha", "premultiply"));
            importer = Importer();
            Assert.That(importer.wrapModeU, Is.EqualTo(TextureWrapMode.Clamp), "clamp without a wrap step");
            Assert.That(importer.alphaIsTransparency, Is.False, "premultiplied colour is not dilated");
        }

        [Test]
        public void Flipbook_IsRecorded()
        {
            var source = Gradient("sheet.png", 8, 4);
            var result = TestJson.Ok("import-texture", Args(source, Target, Step("flipbook", ("columns", 4L), ("rows", 2L))));
            var flipbook = (JsonObject)result.Get("flipbook");
            Assert.That(flipbook.Get("frames"), Is.EqualTo(8L));
            Assert.That(flipbook.Get("frame_width"), Is.EqualTo(2L));
            Assert.That(flipbook.Get("frame_height"), Is.EqualTo(2L));
        }

        // ------------------------------------------------------------ failures

        static IEnumerable<TestCaseData> Failures()
        {
            yield return new TestCaseData((object)new object[] { "premultiplied" }, "unknown step 'premultiplied'", "premultiply").SetName("UnknownStep_ListsTheValidSteps");
            yield return new TestCaseData((object)new object[] { Step("resize_pot", ("max", 4L)) }, "unknown parameter 'max'", "max_size").SetName("UnknownParameter_ListsTheValidParameters");
            yield return new TestCaseData((object)new object[] { Step("wrap", ("u", "mirror")) }, "unknown u 'mirror'", null).SetName("UnknownWrapMode_ListsTheValidValues");
            yield return new TestCaseData((object)new object[] { Step("luminance_to_alpha", ("color", "keeep")) }, "unknown color 'keeep'", "keep").SetName("UnknownColorMode_ListsTheValidValues");
            yield return new TestCaseData((object)new object[] { Step("pack_channels", ("r", new JsonObject { { "channel", "red" } })) }, "unknown channel 'red'", "r").SetName("UnknownChannel_ListsTheValidChannels");
            yield return new TestCaseData((object)new object[] { Step("flipbook", ("columns", 3L), ("rows", 1L)) }, "does not divide into 3 columns", null).SetName("FlipbookThatDoesNotDivide_Fails");
            yield return new TestCaseData((object)new object[] { "linear", "srgb" }, "conflicts with transform[0] (linear)", null).SetName("LinearAndSrgb_Conflict");
            yield return new TestCaseData((object)new object[] { Step("resize_pot", ("max_size", 100L)) }, "not a power of two", null).SetName("ResizePotMaxSizeNotPowerOfTwo_Fails");
        }

        [TestCaseSource(nameof(Failures))]
        public void FailingTransform_ReportsTheError_AndLeavesTheTargetUntouched(object[] steps, string expectedError, string expectedNearest)
        {
            var source = Gradient("good.png", 8, 4);
            TestJson.Ok("import-texture", Args(source, Target, "premultiply"));
            var bytes = File.ReadAllBytes(Path.GetFullPath(Target));
            var meta = File.ReadAllBytes(Path.GetFullPath(Target) + ".meta");

            var result = TestJson.Result("import-texture", Args(source, Target, steps));
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            var errors = TestJson.Strings(result, "errors");
            Assert.That(errors, Has.Count.EqualTo(1));
            StringAssert.Contains(expectedError, errors[0]);
            var stepError = (JsonObject)result.Get("step_error");
            Assert.That(stepError, Is.Not.Null);
            if (expectedNearest != null)
                Assert.That(TestJson.Strings(stepError, "valid_names").First(), Is.EqualTo(expectedNearest));

            Assert.That(File.ReadAllBytes(Path.GetFullPath(Target)), Is.EqualTo(bytes), "the PNG is untouched");
            Assert.That(File.ReadAllBytes(Path.GetFullPath(Target) + ".meta"), Is.EqualTo(meta), "the .meta is untouched");
        }

        /// <summary>
        /// A failing import over an EXISTING target, at each stage: the transform check (unknown step),
        /// a pixel step (a pack_channels file of another size), the grid check after the pixel steps
        /// (a flipbook that does not divide the resized image), and the write of the output (a read-only
        /// target). The PNG and its .meta have the same SHA-256 before and after.
        /// </summary>
        [TestCase("unknown_step", "unknown step 'premultiplied'")]
        [TestCase("pack_file_size", "is 3x3, the image is 8x4 at this step")]
        [TestCase("flipbook_after_resize", "does not divide into 3 columns")]
        [TestCase("write_fails", "the target is restored")]
        public void FailedImport_LeavesAnExistingTargetAndItsMetaWithTheSameHashes(string stage, string expectedError)
        {
            var source = Gradient("good.png", 8, 4);
            TestJson.Ok("import-texture", Args(source, Target, "premultiply", Step("wrap", ("mode", "repeat"))));
            var targetFile = Path.GetFullPath(Target);
            var pngBefore = Hash(targetFile);
            var metaBefore = Hash(targetFile + ".meta");

            object[] steps;
            switch (stage)
            {
                case "unknown_step":
                    steps = new object[] { "premultiplied" };
                    break;
                case "pack_file_size":
                    steps = new object[] { "premultiply", Step("pack_channels", ("a", new JsonObject { { "file", Gradient("small.png", 3, 3) }, { "channel", "r" } })) };
                    break;
                case "flipbook_after_resize":
                    steps = new object[] { "premultiply", Step("resize_pot", ("max_size", 4L)), Step("flipbook", ("columns", 3L), ("rows", 1L)) };
                    break;
                default:
                    steps = new object[] { "srgb" };
                    break;
            }

            var attributes = File.GetAttributes(targetFile);
            if (stage == "write_fails")
            {
                File.SetAttributes(targetFile, attributes | FileAttributes.ReadOnly);
                if (CanWrite(targetFile))
                {
                    File.SetAttributes(targetFile, attributes);
                    Assert.Ignore("this user can write a read-only file (root on Linux), so the write cannot be made to fail");
                }
            }

            JsonObject result;
            try
            {
                result = TestJson.Result("import-texture", Args(source, Target, steps));
            }
            finally
            {
                File.SetAttributes(targetFile, attributes);
            }

            Assert.That(result.Get("ok"), Is.EqualTo(false));
            var errors = TestJson.Strings(result, "errors");
            Assert.That(errors, Has.Count.EqualTo(1), string.Join(" | ", errors));
            StringAssert.Contains(expectedError, errors[0]);
            Assert.That(Hash(targetFile), Is.EqualTo(pngBefore), "the PNG keeps its SHA-256");
            Assert.That(Hash(targetFile + ".meta"), Is.EqualTo(metaBefore), "the .meta keeps its SHA-256");
            Assert.That(Importer().wrapModeU, Is.EqualTo(TextureWrapMode.Repeat), "the importer keeps the previous settings");
        }

        static bool CanWrite(string file)
        {
            try
            {
                using (new FileStream(file, FileMode.Open, FileAccess.Write))
                    return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        [Test]
        public void BadPaths_Fail_WithoutWritingTheTarget()
        {
            var source = Gradient("good.png", 4, 4);
            var notPng = Path.Combine(m_SourceFolder, "not.png").Replace('\\', '/');
            File.WriteAllText(notPng, "not a png");
            var missing = Path.Combine(m_SourceFolder, "missing.png").Replace('\\', '/');

            StringAssert.Contains("does not exist", TestJson.Strings(TestJson.Result("import-texture", Args(missing, Target)), "errors")[0]);
            StringAssert.Contains("not a PNG", TestJson.Strings(TestJson.Result("import-texture", Args(notPng, Target)), "errors")[0]);
            StringAssert.Contains("must end with .png", TestJson.Strings(TestJson.Result("import-texture", Args(source, Folder + "/Out.tga")), "errors")[0]);
            StringAssert.Contains("must be project-relative", TestJson.Strings(TestJson.Result("import-texture", Args("relative/x.png", Target)), "errors")[0]);
            StringAssert.Contains("missing required argument 'target'",
                TestJson.Strings(TestJson.Result("import-texture", Json.Write(new JsonObject { { "source", source } })), "errors")[0]);
            Assert.That(File.Exists(Path.GetFullPath(Target)), Is.False);
        }

        [Test]
        public void Catalog_ListsTheTextureSteps()
        {
            var catalog = TestJson.Ok("catalog", "{\"filter\": \"spawner\"}");
            Assert.That(TestJson.Strings(catalog, "texture_steps"), Is.EqualTo(VfxTextureSteps.Names));
            Assert.That(VfxTextureSteps.Names, Is.EqualTo(new[] { "luminance_to_alpha", "premultiply", "pack_channels", "resize_pot", "flipbook", "wrap", "linear", "srgb" }));
            var fields = (JsonObject)catalog.Get("texture_step_fields");
            Assert.That(TestJson.Strings((JsonObject)fields.Get("resize_pot"), "optional"), Is.EqualTo(new[] { "max_size" }));
        }
    }
}
