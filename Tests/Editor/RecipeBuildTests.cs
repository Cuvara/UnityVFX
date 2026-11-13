using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// <c>build</c> and <c>apply</c> on a copy of the BurstQuads fixture recipe in a temporary project
    /// folder: round trip, determinism, unknown names, and the rule that a failed build leaves the
    /// previous asset byte-identical.
    /// </summary>
    public sealed class RecipeBuildTests
    {
        const string FixtureRecipe = "Packages/com.cuvara.vfx/Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json";
        const string Folder = "Assets/CuvaraVfxRecipeTestTemp";
        const string Recipe = Folder + "/Burst.vfxrecipe.json";
        const string Asset = Folder + "/Burst.vfx";

        static string RecipeFile => Path.GetFullPath(Recipe);
        static string AssetFile => Path.GetFullPath(Asset);

        [SetUp]
        public void CreateFolder()
        {
            DeleteFolder();
            AssetDatabase.CreateFolder("Assets", "CuvaraVfxRecipeTestTemp");
            WriteRecipe(FixtureDocument());
        }

        [TearDown]
        public void DeleteFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
            if (AssetDatabase.IsValidFolder(VfxRecipeBuilder.TempFolder))
                AssetDatabase.DeleteAsset(VfxRecipeBuilder.TempFolder);
        }

        static JsonObject FixtureDocument() => (JsonObject)Json.Parse(File.ReadAllText(Path.GetFullPath(FixtureRecipe)));

        static void WriteRecipe(JsonObject document) => File.WriteAllText(RecipeFile, Json.Write(document, true));

        static JsonObject Op(JsonObject document, string id) =>
            ((List<object>)document.Get("ops")).Cast<JsonObject>().First(o => (o.Get("id") as string) == id);

        static string BuildArgs => Json.Write(new JsonObject { { "recipe", Recipe } });

        static string DescribeArgs => Json.Write(new JsonObject { { "asset", Asset } });

        static JsonObject Build() => TestJson.Ok("build", BuildArgs);

        static string Hash(string file) => VfxRecipeBuilder.Sha256(File.ReadAllBytes(file));

        [Test]
        public void Build_Twice_DescribesToTheSameJson()
        {
            var first = Build();
            Assert.That(first.Get("created"), Is.EqualTo(true));
            Assert.That(TestJson.Strings(first, "compile_errors"), Is.Empty);
            var describeFirst = VfxCli.Run("describe", DescribeArgs);
            var guid = AssetDatabase.AssetPathToGUID(Asset);
            Assert.That(guid, Is.Not.Empty);

            var second = Build();
            Assert.That(second.Get("created"), Is.EqualTo(false));
            Assert.That(second.Get("guid_stable"), Is.EqualTo(true));
            Assert.That(AssetDatabase.AssetPathToGUID(Asset), Is.EqualTo(guid));
            var describeSecond = VfxCli.Run("describe", DescribeArgs);

            Assert.That(describeSecond, Is.EqualTo(describeFirst));
            Assert.That(second.Get("describe_sha256"), Is.EqualTo(first.Get("describe_sha256")));
        }

        [Test]
        public void Build_DescribeShowsWhatTheRecipeDeclared()
        {
            var built = Build();
            var systems = TestJson.Objects(built, "systems");
            Assert.That(systems.Select(s => s.Get("capacity")), Does.Contain(32L));

            var graph = TestJson.Ok("describe", DescribeArgs);
            var contexts = TestJson.Objects(graph, "contexts");
            Assert.That(contexts.Select(c => (string)c.Get("type")),
                Is.EqualTo(new[] { "VFXBasicSpawner", "VFXBasicInitialize", "VFXBasicUpdate", "VFXPlanarPrimitiveOutput" }));

            var output = contexts[3];
            var outputSettings = TestJson.Objects(output, "settings").ToDictionary(s => (string)s.Get("name"), s => s.Get("value"));
            Assert.That(outputSettings["blendMode"], Is.EqualTo("Additive"));
            Assert.That(outputSettings["primitiveType"], Is.EqualTo("Quad"));
            var outputBlocks = TestJson.Objects(output, "blocks").Select(b => (string)b.Get("type")).ToList();
            Assert.That(outputBlocks, Is.EqualTo(new[] { "Orient", "SetAttribute", "AttributeFromCurve" }));

            var burst = TestJson.Objects(contexts[0], "blocks").Single();
            Assert.That(burst.Get("type"), Is.EqualTo("VFXSpawnerBurst"));
            var count = TestJson.Objects(burst, "inputs").Single(i => (string)i.Get("name") == "Count");
            Assert.That(count.Get("value"), Is.EqualTo(24L));

            var parameter = TestJson.Objects(graph, "parameters").Single();
            Assert.That(parameter.Get("exposed_name"), Is.EqualTo("Tint"));
            Assert.That(parameter.Get("exposed"), Is.EqualTo(true));
            Assert.That(parameter.Get("type"), Is.EqualTo("Color"));

            var links = TestJson.Objects(graph, "links").Select(l => l.Get("from") + " -> " + l.Get("to")).ToList();
            Assert.That(links, Does.Contain("p0.o -> c3.b1._Color"));
            var flows = TestJson.Objects(graph, "flow_links").Select(l => l.Get("from") + " -> " + l.Get("to")).ToList();
            Assert.That(flows, Is.EqualTo(new[] { "c0 -> c1", "c1 -> c2", "c2 -> c3" }));
            Assert.That(TestJson.Objects(graph, "sticky_notes").Select(n => (string)n.Get("title")), Does.Contain("BurstQuads"));
        }

        static IEnumerable<TestCaseData> UnknownNames()
        {
            yield return new TestCaseData(new Action<JsonObject>(d => Op(d, "burst").Set("type", "VFXSpawnerBurstt")), "VFXSpawnerBurst")
                .SetName("UnknownName_BlockType");
            yield return new TestCaseData(new Action<JsonObject>(d => ((JsonObject)Op(d, "init").Get("settings")).Set("capacityy", 8)), "capacity")
                .SetName("UnknownName_Setting");
            yield return new TestCaseData(new Action<JsonObject>(d => ((JsonObject)Op(d, "burst").Get("slots")).Set("Countt", 3)), "Count")
                .SetName("UnknownName_Slot");
            yield return new TestCaseData(new Action<JsonObject>(d => Op(d, "update").Set("op", "add_contxt")), "add_context")
                .SetName("UnknownName_Op");
            yield return new TestCaseData(new Action<JsonObject>(d =>
                    ((List<object>)d.Get("ops")).Cast<JsonObject>().First(o => (string)o.Get("op") == "link_flow").Set("to", "inti")), "init")
                .SetName("UnknownName_Id");
            yield return new TestCaseData(new Action<JsonObject>(d => Op(d, "init").Set("variant", "Context/#0Common/Initialize Particles")),
                    "Context/#0Common/Initialize Particle")
                .SetName("UnknownName_Variant");
        }

        [TestCaseSource(nameof(UnknownNames))]
        public void UnknownName_FailsWithTheValidNames_AndLeavesTheAssetUntouched(Action<JsonObject> breakRecipe, string expectedNearest)
        {
            Build();
            var before = Hash(AssetFile);
            var guid = AssetDatabase.AssetPathToGUID(Asset);

            var document = FixtureDocument();
            breakRecipe(document);
            WriteRecipe(document);
            var result = TestJson.Result("build", BuildArgs);

            Assert.That(result.Get("ok"), Is.EqualTo(false));
            var errors = TestJson.Strings(result, "errors");
            Assert.That(errors, Has.Count.EqualTo(1));
            StringAssert.StartsWith("op ", errors[0]);
            StringAssert.Contains("unknown", errors[0]);
            StringAssert.Contains(expectedNearest, errors[0]);
            var opError = (JsonObject)result.Get("op_error");
            Assert.That(TestJson.Strings(opError, "valid_names").First(), Is.EqualTo(expectedNearest));

            Assert.That(Hash(AssetFile), Is.EqualTo(before), "the previous asset must stay byte-identical");
            Assert.That(AssetDatabase.AssetPathToGUID(Asset), Is.EqualTo(guid));
            Assert.That(AssetDatabase.IsValidFolder(VfxRecipeBuilder.TempFolder), Is.False, "the temporary copy is deleted");
        }

        [Test]
        public void Apply_SetSlot_ChangesTheValueThatDescribeShows()
        {
            Build();
            var guid = AssetDatabase.AssetPathToGUID(Asset);
            var args = Json.Write(new JsonObject
            {
                { "asset", Asset },
                { "ops", new List<object> { new JsonObject { { "op", "set_slot" }, { "slot", "c0.b0.Count" }, { "value", 7 } } } },
            });

            var applied = TestJson.Ok("apply", args);
            Assert.That(applied.Get("guid_stable"), Is.EqualTo(true));
            Assert.That(AssetDatabase.AssetPathToGUID(Asset), Is.EqualTo(guid));

            var graph = TestJson.Ok("describe", DescribeArgs);
            var burst = TestJson.Objects(TestJson.Objects(graph, "contexts")[0], "blocks").Single();
            var count = TestJson.Objects(burst, "inputs").Single(i => (string)i.Get("name") == "Count");
            Assert.That(count.Get("value"), Is.EqualTo(7L));
        }

        [Test]
        public void Apply_AddBlockToAnExistingContext_UsesTheDescribeIds()
        {
            Build();
            var args = Json.Write(new JsonObject
            {
                { "asset", Asset },
                {
                    "ops", new List<object>
                    {
                        new JsonObject
                        {
                            { "op", "add_block" }, { "id", "fade" }, { "context", "c3" }, { "type", "AttributeFromCurve" },
                            { "settings", new JsonObject { { "attribute", "size" }, { "SampleMode", "OverLife" }, { "Composition", "Multiply" } } },
                            { "slots", new JsonObject { { "Size", new List<object> { new List<object> { 0, 1 }, new List<object> { 1, 0.2 } } } } },
                        },
                    }
                },
            });
            TestJson.Ok("apply", args);

            var graph = TestJson.Ok("describe", DescribeArgs);
            var blocks = TestJson.Objects(TestJson.Objects(graph, "contexts")[3], "blocks");
            Assert.That(blocks.Select(b => (string)b.Get("type")), Is.EqualTo(new[] { "Orient", "SetAttribute", "AttributeFromCurve", "AttributeFromCurve" }));
            Assert.That(blocks[3].Get("id"), Is.EqualTo("c3.b3"));
        }

        [Test]
        public void Apply_UnknownSlot_FailsAndLeavesTheAssetUntouched()
        {
            Build();
            var before = Hash(AssetFile);
            var args = Json.Write(new JsonObject
            {
                { "asset", Asset },
                { "ops", new List<object> { new JsonObject { { "op", "set_slot" }, { "slot", "c0.b0.Cout" }, { "value", 7 } } } },
            });

            var result = TestJson.Result("apply", args);
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            StringAssert.Contains("Count", TestJson.Strings(result, "errors")[0]);
            Assert.That(Hash(AssetFile), Is.EqualTo(before));
        }

        [Test]
        public void Build_RecipeWithoutSchema_IsRejectedBeforeAnyOp()
        {
            var document = FixtureDocument();
            document.Set("schema", "cuvara.vfx-recipe/0");
            WriteRecipe(document);
            var result = TestJson.Result("build", BuildArgs);
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            StringAssert.Contains("cuvara.vfx-recipe/1", TestJson.Strings(result, "errors")[0]);
            Assert.That(File.Exists(AssetFile), Is.False);
        }
    }
}
