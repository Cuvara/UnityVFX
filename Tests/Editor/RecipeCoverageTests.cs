using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Cuvara.VFX.Editor.Bridge;
using NUnit.Framework;
using UnityEditor;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// The recipe ops and op fields the BurstQuads fixture does not use, each built in a temporary
    /// folder and checked through <c>describe</c>: <c>set_output</c> (texture, blend mode, colour
    /// mapping, gradient), asset values by path and by <c>{"guid"}</c>, the space of a spaceable slot,
    /// <c>use_subgraph</c> with a <c>.vfxoperator</c>, a <c>.vfxblock</c> and a <c>.vfx</c> subgraph
    /// (created through the bridge in the same folder), <c>add_operator</c>, <c>set_setting</c>,
    /// <c>link_flow</c> slots, a context label and a parameter's category and tooltip. Textures come
    /// from <c>import-texture</c> on PNGs the tests write.
    /// </summary>
    public sealed class RecipeCoverageTests
    {
        const string FolderName = "CuvaraVfxRecipeCoverageTemp";
        const string Folder = "Assets/" + FolderName;
        const string QuadVariant = "Context/#2Output Basic/Output Particle|Unlit|Quad";
        const ImportAssetOptions Reimport = ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport;

        string m_SourceFolder;

        [SetUp]
        public void CreateFolders()
        {
            DeleteFolders();
            AssetDatabase.CreateFolder("Assets", FolderName);
            m_SourceFolder = Path.Combine(Path.GetTempPath(), "cuvara-vfx-coverage-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(m_SourceFolder);
        }

        [TearDown]
        public void DeleteFolders()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
            if (AssetDatabase.IsValidFolder(VfxRecipeBuilder.TempFolder))
                AssetDatabase.DeleteAsset(VfxRecipeBuilder.TempFolder);
            if (m_SourceFolder != null && Directory.Exists(m_SourceFolder))
                Directory.Delete(m_SourceFolder, true);
            m_SourceFolder = null;
        }

        // ------------------------------------------------------------ recipes

        static JsonObject Op(string op, params (string key, object value)[] fields)
        {
            var result = new JsonObject { { "op", op } };
            foreach (var (key, value) in fields)
                result.Add(key, value);
            return result;
        }

        static JsonObject Map(params (string key, object value)[] fields)
        {
            var result = new JsonObject();
            foreach (var (key, value) in fields)
                result.Add(key, value);
            return result;
        }

        static List<object> List(params object[] items) => items.ToList();

        /// <summary>
        /// One particle system: spawner <c>spawn</c> with burst <c>burst</c>, Initialize <c>init</c> with
        /// a <c>life</c> lifetime block, Update <c>update</c>, quad output <c>output</c>, and their flow.
        /// <paramref name="inInit"/> runs after the Initialize blocks; <paramref name="atEnd"/> after the flow.
        /// </summary>
        static List<object> OneSystem(IEnumerable<JsonObject> inInit = null, IEnumerable<JsonObject> atEnd = null)
        {
            var ops = new List<object>
            {
                Op("add_context", ("id", "spawn"), ("type", "VFXBasicSpawner")),
                Op("add_block", ("id", "burst"), ("context", "spawn"), ("type", "VFXSpawnerBurst"), ("variant", "Spawn/Single Burst"), ("slots", Map(("Count", 4L)))),
                Op("add_context", ("id", "init"), ("type", "VFXBasicInitialize"), ("variant", "Context/#0Common/Initialize Particle"),
                    ("settings", Map(("capacity", 8L), ("boundsMode", "Manual")))),
                Op("add_block", ("id", "life"), ("context", "init"), ("type", "SetAttribute"), ("settings", Map(("attribute", "lifetime"))), ("slots", Map(("_Lifetime", 0.5)))),
            };
            if (inInit != null)
                ops.AddRange(inInit);
            ops.Add(Op("add_context", ("id", "update"), ("type", "VFXBasicUpdate")));
            ops.Add(Op("add_context", ("id", "output"), ("type", "VFXPlanarPrimitiveOutput"), ("variant", QuadVariant)));
            ops.Add(Op("link_flow", ("from", "spawn"), ("to", "init")));
            ops.Add(Op("link_flow", ("from", "init"), ("to", "update")));
            ops.Add(Op("link_flow", ("from", "update"), ("to", "output")));
            if (atEnd != null)
                ops.AddRange(atEnd);
            return ops;
        }

        static JsonObject GradientValue() => Map(
            ("mode", "Blend"),
            ("color_keys", List(
                Map(("t", 0.0), ("color", List(0.0, 0.0, 0.0))),
                Map(("t", 0.25), ("color", List(1.0, 0.5, 0.25))),
                Map(("t", 1.0), ("color", List(2.0, 2.0, 2.0))))),
            ("alpha_keys", List(
                Map(("t", 0.0), ("alpha", 0.0)),
                Map(("t", 1.0), ("alpha", 1.0)))));

        /// <summary>set_output with every field; the mask texture by path, the parameter texture by GUID.</summary>
        static List<object> OutputRecipe(string maskPath, string rampGuid) => OneSystem(atEnd: new[]
        {
            Op("add_parameter", ("id", "p_ramp"), ("type", "Texture2D"), ("name", "Ramp"), ("value", Map(("guid", rampGuid)))),
            Op("set_output", ("node", "output"), ("texture", maskPath), ("blend_mode", "AlphaPremultiplied"),
                ("color_mapping", "GradientMapped"), ("gradient", GradientValue())),
        });

        /// <summary>The remaining fields: category, tooltip, label, add_operator, set_setting, set_slot space, link_flow slots.</summary>
        static List<object> FieldsRecipe() => new List<object>
        {
            Op("add_parameter", ("id", "p_speed"), ("type", "float"), ("name", "Speed"), ("value", 1.5), ("exposed", false), ("category", "Motion"), ("tooltip", "metres per second")),
            Op("add_context", ("id", "kill"), ("type", "VFXBasicEvent"), ("settings", Map(("eventName", "Kill")))),
            Op("add_context", ("id", "spawn"), ("type", "VFXBasicSpawner"), ("label", "Main spawn")),
            Op("add_block", ("id", "burst"), ("context", "spawn"), ("type", "VFXSpawnerBurst"), ("variant", "Spawn/Single Burst"), ("slots", Map(("Count", 4L)))),
            Op("add_context", ("id", "init"), ("type", "VFXBasicInitialize"), ("variant", "Context/#0Common/Initialize Particle"),
                ("settings", Map(("capacity", 8L), ("boundsMode", "Manual")))),
            Op("set_setting", ("node", "init"), ("name", "capacity"), ("value", 12L)),
            Op("set_slot", ("slot", "init.bounds"), ("space", "World")),
            Op("add_block", ("id", "life"), ("context", "init"), ("type", "SetAttribute"), ("settings", Map(("attribute", "lifetime")))),
            Op("add_block", ("id", "pos"), ("context", "init"), ("type", "SetAttribute"), ("settings", Map(("attribute", "position")))),
            Op("set_slot", ("slot", "pos._Position"), ("value", List(1.0, 2.0, 3.0)), ("space", "World")),
            Op("add_operator", ("id", "rnd"), ("type", "Random"), ("variant", "Operator/Random/Random|Float"),
                ("settings", Map(("constant", false))), ("slots", Map(("Min", 0.4), ("Max", 0.8)))),
            Op("add_context", ("id", "update"), ("type", "VFXBasicUpdate")),
            Op("add_context", ("id", "output"), ("type", "VFXPlanarPrimitiveOutput"), ("variant", QuadVariant)),
            Op("link_flow", ("from", "kill"), ("to", "spawn"), ("from_slot", 0L), ("to_slot", 1L)),
            Op("link_flow", ("from", "spawn"), ("to", "init")),
            Op("link_flow", ("from", "init"), ("to", "update")),
            Op("link_flow", ("from", "update"), ("to", "output")),
            Op("link_slots", ("from", "rnd.out"), ("to", "life._Lifetime")),
            Op("comment", ("title", "Fields"), ("text", "every op field BurstQuads does not use")),
        };

        /// <summary>The three subgraph kinds, each with a slot value and a link from a parameter of the main graph.</summary>
        static List<object> SubgraphRecipe(string operatorPath, string blockPath, string vfxPath) => OneSystem(
            inInit: new[]
            {
                Op("use_subgraph", ("id", "grow"), ("asset", blockPath), ("context", "init"), ("slots", Map(("Size", 0.7)))),
            },
            atEnd: new[]
            {
                Op("add_parameter", ("id", "p_x"), ("type", "float"), ("name", "X"), ("value", 0.4)),
                Op("add_parameter", ("id", "p_s"), ("type", "float"), ("name", "S"), ("value", 0.2)),
                Op("add_parameter", ("id", "p_n"), ("type", "float"), ("name", "N"), ("value", 3L)),
                Op("use_subgraph", ("id", "twice"), ("asset", operatorPath), ("slots", Map(("In", 0.3)))),
                Op("use_subgraph", ("id", "sub"), ("asset", vfxPath), ("slots", Map(("Count", 9L)))),
                Op("link_slots", ("from", "p_x.out"), ("to", "twice.In")),
                Op("link_slots", ("from", "twice.Out"), ("to", "life._Lifetime")),
                Op("link_slots", ("from", "p_s.out"), ("to", "grow.Size")),
                Op("link_slots", ("from", "p_n.out"), ("to", "sub.Count")),
            });

        /// <summary>The graph a .vfx subgraph holds: one burst whose count is the exposed float Count.</summary>
        static List<object> SubRecipe() => OneSystem(atEnd: new[]
        {
            Op("add_parameter", ("id", "p_count"), ("type", "float"), ("name", "Count"), ("value", 6L)),
            Op("link_slots", ("from", "p_count.out"), ("to", "burst.Count")),
        });

        static string WriteRecipe(string name, List<object> ops)
        {
            var path = Folder + "/" + name + VfxRecipeBuilder.RecipeSuffix;
            var document = new JsonObject { { "schema", VfxRecipeOps.Schema }, { "id", name.ToLowerInvariant() }, { "vfx_graph", "17.6" }, { "ops", ops } };
            File.WriteAllText(Path.GetFullPath(path), Json.Write(document, true));
            return path;
        }

        static string BuildArgs(string recipe) => Json.Write(new JsonObject { { "recipe", recipe } });

        static JsonObject Build(string name, List<object> ops) => TestJson.Ok("build", BuildArgs(WriteRecipe(name, ops)));

        static JsonObject BuildFails(string name, List<object> ops)
        {
            var result = TestJson.Result("build", BuildArgs(WriteRecipe(name, ops)));
            Assert.That(result.Get("ok"), Is.EqualTo(false), "the build must fail");
            Assert.That(File.Exists(Path.GetFullPath(Folder + "/" + name + ".vfx")), Is.False, "a failed build creates no asset");
            Assert.That(AssetDatabase.IsValidFolder(VfxRecipeBuilder.TempFolder), Is.False, "the temporary copy is deleted");
            return result;
        }

        static JsonObject Describe(string asset) => TestJson.Ok("describe", Json.Write(new JsonObject { { "asset", asset } }));

        // ------------------------------------------------------------ describe lookups

        static JsonObject ContextOfType(JsonObject graph, string type) =>
            TestJson.Objects(graph, "contexts").Single(c => (string)c.Get("type") == type);

        static JsonObject Input(JsonObject node, string name)
        {
            var input = TestJson.Objects(node, "inputs").FirstOrDefault(i => (string)i.Get("name") == name);
            Assert.That(input, Is.Not.Null, $"{node.Get("id")} has no input '{name}'");
            return input;
        }

        static object Setting(JsonObject node, string name) =>
            TestJson.Objects(node, "settings").Single(s => (string)s.Get("name") == name).Get("value");

        static string GuidOf(object assetValue) => (string)((JsonObject)assetValue).Get("guid");

        static double Number(object value) => Convert.ToDouble(value, CultureInfo.InvariantCulture);

        static List<string> Links(JsonObject graph) => TestJson.Objects(graph, "links").Select(l => l.Get("from") + " -> " + l.Get("to")).ToList();

        // ------------------------------------------------------------ test assets

        /// <summary>Writes an opaque 8x8 PNG outside the project and imports it with import-texture; returns the asset GUID.</summary>
        string ImportTexture(string name, byte red)
        {
            var rgba = new byte[8 * 8 * 4];
            for (int i = 0; i < 64; i++)
            {
                rgba[(i * 4) + 0] = red;
                rgba[(i * 4) + 1] = (byte)(i * 4);
                rgba[(i * 4) + 2] = 64;
                rgba[(i * 4) + 3] = 255;
            }
            var source = Path.Combine(m_SourceFolder, name + ".png").Replace('\\', '/');
            File.WriteAllBytes(source, new VfxPixels(8, 8, rgba).EncodePng());
            var target = Folder + "/" + name + ".png";
            var result = TestJson.Ok("import-texture", Json.Write(new JsonObject
            {
                { "source", source },
                { "target", target },
                { "transform", List("luminance_to_alpha", "premultiply") },
            }));
            var guid = (string)result.Get("guid");
            Assert.That(guid, Is.EqualTo(AssetDatabase.AssetPathToGUID(target)).And.Not.Empty);
            return guid;
        }

        /// <summary>A subgraph operator: Out = In * 2.</summary>
        static string CreateOperatorSubgraph(string path)
        {
            var edit = VfxGraphBridge.GraphEdit.CreateSubgraph(path);
            edit.AddParameter("p_in", "float", "In", 0.5, true, null, "the value to double");
            edit.AddOperator("mul", "Multiply", null);
            edit.SetSlot("mul.b", 2.0, true, null);
            edit.AddOutputParameter("p_out", "float", "Out", null);
            edit.LinkSlots("p_in.out", "mul.a");
            edit.LinkSlots("mul.out", "p_out.i");
            edit.Save();
            AssetDatabase.ImportAsset(path, Reimport);
            return AssetDatabase.AssetPathToGUID(path);
        }

        /// <summary>A subgraph block for <paramref name="suitable"/> contexts: sets the size from its input Size.</summary>
        static string CreateBlockSubgraph(string path, string suitable = "InitAndUpdateAndOutput")
        {
            var edit = VfxGraphBridge.GraphEdit.CreateSubgraph(path, "blocks");
            edit.SetSetting("blocks", "m_SuitableContexts", suitable);
            edit.AddParameter("p_size", "float", "Size", 0.25, true, null, null);
            edit.AddBlock("blocks", "set_size", "SetAttribute", null);
            edit.SetSetting("set_size", "attribute", "size");
            edit.LinkSlots("p_size.out", "set_size._Size");
            edit.Save();
            AssetDatabase.ImportAsset(path, Reimport);
            return AssetDatabase.AssetPathToGUID(path);
        }

        // ------------------------------------------------------------ set_output and asset values

        [Test]
        public void SetOutput_TextureBlendColorMappingGradient_DescribeShowsThem()
        {
            var maskGuid = ImportTexture("Mask", 200);
            var rampGuid = ImportTexture("Ramp", 40);
            var built = Build("Output", OutputRecipe(Folder + "/Mask.png", rampGuid));
            Assert.That(TestJson.Strings(built, "compile_errors"), Is.Empty);

            var graph = Describe(Folder + "/Output.vfx");
            var output = ContextOfType(graph, "VFXPlanarPrimitiveOutput");
            Assert.That(Setting(output, "blendMode"), Is.EqualTo("AlphaPremultiplied"));
            Assert.That(Setting(output, "colorMapping"), Is.EqualTo("GradientMapped"));

            var texture = Input(output, "mainTexture").Get("value");
            Assert.That(GuidOf(texture), Is.EqualTo(maskGuid), "the texture given by path is stored by its GUID");
            Assert.That(((JsonObject)texture).Get("path"), Is.EqualTo(Folder + "/Mask.png"));

            var gradient = (JsonObject)Input(output, "gradient").Get("value");
            Assert.That(gradient.Get("mode"), Is.EqualTo("Blend"));
            var colorKeys = TestJson.Objects(gradient, "color_keys");
            Assert.That(colorKeys.Select(k => Number(k.Get("t"))), Is.EqualTo(new[] { 0.0, 0.25, 1.0 }).Within(1e-3));
            var middle = ((List<object>)colorKeys[1].Get("color")).Select(Number).ToList();
            Assert.That(middle, Is.EqualTo(new[] { 1.0, 0.5, 0.25, 1.0 }).Within(1e-5));
            var last = ((List<object>)colorKeys[2].Get("color")).Select(Number).ToList();
            Assert.That(last.Take(3), Is.EqualTo(new[] { 2.0, 2.0, 2.0 }).Within(1e-5), "HDR gradient colours survive");
            var alphaKeys = TestJson.Objects(gradient, "alpha_keys");
            Assert.That(alphaKeys.Select(k => Number(k.Get("alpha"))), Is.EqualTo(new[] { 0.0, 1.0 }).Within(1e-5));

            var ramp = TestJson.Objects(graph, "parameters").Single(p => (string)p.Get("exposed_name") == "Ramp");
            Assert.That(ramp.Get("type"), Is.EqualTo("Texture2D"));
            Assert.That(GuidOf(ramp.Get("value")), Is.EqualTo(rampGuid), "the texture given as {guid} is stored");
        }

        [Test]
        public void SetOutputTextureByGuid_AndSetSlotTextureByPath_ThroughApply()
        {
            var maskGuid = ImportTexture("Mask", 200);
            var rampGuid = ImportTexture("Ramp", 40);
            Build("Output", OneSystem(atEnd: new[] { Op("set_output", ("node", "output"), ("texture", Map(("guid", maskGuid)))) }));
            var asset = Folder + "/Output.vfx";
            var output = ContextOfType(Describe(asset), "VFXPlanarPrimitiveOutput");
            Assert.That(GuidOf(Input(output, "mainTexture").Get("value")), Is.EqualTo(maskGuid));
            var outputId = (string)output.Get("id");

            TestJson.Ok("apply", Json.Write(new JsonObject
            {
                { "asset", asset },
                { "ops", List(Op("set_slot", ("slot", outputId + ".mainTexture"), ("value", Folder + "/Ramp.png"))) },
            }));
            output = ContextOfType(Describe(asset), "VFXPlanarPrimitiveOutput");
            Assert.That(GuidOf(Input(output, "mainTexture").Get("value")), Is.EqualTo(rampGuid));

            TestJson.Ok("apply", Json.Write(new JsonObject
            {
                { "asset", asset },
                { "ops", List(Op("set_output", ("node", outputId), ("texture", Map(("path", Folder + "/Mask.png"), ("guid", maskGuid))))) },
            }));
            output = ContextOfType(Describe(asset), "VFXPlanarPrimitiveOutput");
            Assert.That(GuidOf(Input(output, "mainTexture").Get("value")), Is.EqualTo(maskGuid), "path and guid that agree are accepted");
        }

        static IEnumerable<TestCaseData> BadAssetValues()
        {
            yield return new TestCaseData((object)Map(("guid", "0123456789abcdef0123456789abcdef")), "no asset with GUID 0123456789abcdef0123456789abcdef")
                .SetName("AssetValue_UnknownGuid_Fails");
            yield return new TestCaseData((object)(Folder + "/Missing.png"), "no asset at '" + Folder + "/Missing.png'")
                .SetName("AssetValue_MissingPath_Fails");
            yield return new TestCaseData((object)Map(("gid", "x")), "unknown field of an asset reference")
                .SetName("AssetValue_UnknownField_ListsGuidAndPath");
            yield return new TestCaseData((object)42L, "an asset is written as a project-relative path")
                .SetName("AssetValue_Number_Fails");
            yield return new TestCaseData((object)(Folder + "/Mask.vfxrecipe.json"), "is not a Texture2D")
                .SetName("AssetValue_WrongAssetType_Fails");
        }

        [TestCaseSource(nameof(BadAssetValues))]
        public void BadAssetValue_FailsTheBuild_WithAClearError(object texture, string expected)
        {
            File.WriteAllText(Path.GetFullPath(Folder + "/Mask.vfxrecipe.json"), "{}");
            AssetDatabase.ImportAsset(Folder + "/Mask.vfxrecipe.json", Reimport);
            var result = BuildFails("Bad", OneSystem(atEnd: new[] { Op("set_output", ("node", "output"), ("texture", texture)) }));
            var errors = TestJson.Strings(result, "errors");
            Assert.That(errors, Has.Count.EqualTo(1));
            StringAssert.Contains(expected, errors[0]);
            StringAssert.Contains("(set_output)", errors[0]);
        }

        [Test]
        public void SetOutput_GradientWithoutGradientMapping_FailsWithTheReason()
        {
            var result = BuildFails("Bad", OneSystem(atEnd: new[] { Op("set_output", ("node", "output"), ("gradient", GradientValue())) }));
            StringAssert.Contains("set color_mapping to GradientMapped first", TestJson.Strings(result, "errors")[0]);
        }

        [Test]
        public void SetOutput_OnABlock_FailsWithTheReason()
        {
            var result = BuildFails("Bad", OneSystem(atEnd: new[] { Op("set_output", ("node", "life"), ("blend_mode", "Additive")) }));
            StringAssert.Contains("set_output needs an output context, and 'life' is a block", TestJson.Strings(result, "errors")[0]);
        }

        // ------------------------------------------------------------ the other fields

        [Test]
        public void RemainingFields_DescribeShowsWhatTheRecipeDeclared()
        {
            var built = Build("Fields", FieldsRecipe());
            Assert.That(TestJson.Strings(built, "compile_errors"), Is.Empty);
            var graph = Describe(Folder + "/Fields.vfx");

            // add_parameter category, tooltip and exposed false
            var speed = TestJson.Objects(graph, "parameters").Single();
            Assert.That(speed.Get("category"), Is.EqualTo("Motion"));
            Assert.That(speed.Get("tooltip"), Is.EqualTo("metres per second"));
            Assert.That(speed.Get("exposed"), Is.EqualTo(false));

            // add_context label
            var spawn = ContextOfType(graph, "VFXBasicSpawner");
            Assert.That(spawn.Get("label"), Is.EqualTo("Main spawn"));

            // set_setting after add_context: the system holds the capacity
            var init = ContextOfType(graph, "VFXBasicInitialize");
            var system = TestJson.Objects(graph, "systems").Single(s => (string)s.Get("id") == (string)init.Get("system"));
            Assert.That(system.Get("capacity"), Is.EqualTo(12L));

            // set_slot space: alone on a slot, and together with a value
            Assert.That(Input(init, "bounds").Get("space"), Is.EqualTo("World"));
            var position = TestJson.Objects(init, "blocks").Single(b => (string)b.Get("name") == "|Set|_Position");
            var positionSlot = Input(position, "_Position");
            Assert.That(positionSlot.Get("space"), Is.EqualTo("World"));
            Assert.That(((List<object>)((JsonObject)positionSlot.Get("value")).Get("position")).Select(Number), Is.EqualTo(new[] { 1.0, 2.0, 3.0 }));

            // add_operator with variant, settings and slots; its single output linked through 'out'
            var random = TestJson.Objects(graph, "operators").Single();
            Assert.That(random.Get("type"), Is.EqualTo("Random"));
            Assert.That(Setting(random, "constant"), Is.EqualTo(false));
            Assert.That(Number(Input(random, "Min").Get("value")), Is.EqualTo(0.4).Within(1e-6));
            Assert.That(Number(Input(random, "Max").Get("value")), Is.EqualTo(0.8).Within(1e-6));
            var life = TestJson.Objects(init, "blocks").Single(b => (string)b.Get("name") == "|Set|_Lifetime");
            Assert.That(Links(graph), Does.Contain(random.Get("id") + ".r -> " + life.Get("id") + "._Lifetime"));

            // link_flow from_slot and to_slot: the event stops the spawner (flow input 1)
            var kill = ContextOfType(graph, "VFXBasicEvent");
            var flows = TestJson.Objects(graph, "flow_links").Select(f => $"{f.Get("from")}[{f.Get("from_slot")}] -> {f.Get("to")}[{f.Get("to_slot")}]").ToList();
            Assert.That(flows, Does.Contain($"{kill.Get("id")}[0] -> {spawn.Get("id")}[1]"));
        }

        static IEnumerable<TestCaseData> BadFields()
        {
            yield return new TestCaseData(Op("set_slot", ("slot", "burst.Count"), ("space", "World")), "burst.Count: the slot has no space")
                .SetName("SetSlot_SpaceOnASlotWithoutSpace_Fails");
            yield return new TestCaseData(Op("set_slot", ("slot", "init.bounds"), ("space", "Wrld")), "unknown value of VFXSpace 'Wrld'")
                .SetName("SetSlot_UnknownSpace_ListsTheSpaces");
            yield return new TestCaseData(Op("link_flow", ("from", "update"), ("to", "output"), ("to_slot", 3L)), "cannot link the flow update[0] -> output[3]")
                .SetName("LinkFlow_FlowSlotThatDoesNotExist_Fails");
            yield return new TestCaseData(Op("set_setting", ("node", "init"), ("name", "capacty"), ("value", 4L)), "unknown setting of VFXBasicInitialize 'capacty'")
                .SetName("SetSetting_UnknownName_ListsTheSettings");
        }

        [TestCaseSource(nameof(BadFields))]
        public void BadField_FailsTheBuild_WithAClearError(JsonObject op, string expected)
        {
            var result = BuildFails("Bad", OneSystem(atEnd: new[] { op }));
            StringAssert.Contains(expected, TestJson.Strings(result, "errors")[0]);
        }

        // ------------------------------------------------------------ use_subgraph

        [Test]
        public void UseSubgraph_OperatorBlockAndVfx_DescribeShowsTheReferencesAndLinkedSlots()
        {
            var operatorGuid = CreateOperatorSubgraph(Folder + "/Twice.vfxoperator");
            var blockGuid = CreateBlockSubgraph(Folder + "/Grow.vfxblock");
            var subBuilt = Build("Sub", SubRecipe());
            var vfxGuid = (string)((JsonObject)subBuilt.Get("asset")).Get("guid");

            // The subgraphs themselves, as the bridge made them.
            var twice = Describe(Folder + "/Twice.vfxoperator");
            Assert.That(twice.Get("asset_kind"), Is.EqualTo("VisualEffectSubgraphOperator"));
            var twiceParameters = TestJson.Objects(twice, "parameters");
            Assert.That(twiceParameters.Select(p => $"{p.Get("exposed_name")}:{p.Get("is_output")}"), Is.EqualTo(new[] { "In:False", "Out:True" }));
            var grow = Describe(Folder + "/Grow.vfxblock");
            Assert.That(grow.Get("asset_kind"), Is.EqualTo("VisualEffectSubgraphBlock"));
            Assert.That(TestJson.Objects(grow, "contexts").Single().Get("type"), Is.EqualTo("VFXBlockSubgraphContext"));

            var built = Build("Main", SubgraphRecipe(Folder + "/Twice.vfxoperator", Folder + "/Grow.vfxblock", Folder + "/Sub.vfx"));
            Assert.That(TestJson.Strings(built, "compile_errors"), Is.Empty);
            var graph = Describe(Folder + "/Main.vfx");
            var links = Links(graph);
            string ParameterId(string name) => (string)TestJson.Objects(graph, "parameters").Single(p => (string)p.Get("exposed_name") == name).Get("id");

            // .vfxoperator: an operator holding the subgraph, its input In and output Out
            var subOperator = TestJson.Objects(graph, "operators").Single(o => (string)o.Get("type") == "VFXSubgraphOperator");
            Assert.That(GuidOf(Setting(subOperator, "m_Subgraph")), Is.EqualTo(operatorGuid));
            var inSlot = Input(subOperator, "In");
            Assert.That(Number(inSlot.Get("value")), Is.EqualTo(0.3).Within(1e-6));
            Assert.That(inSlot.Get("linked"), Is.EqualTo(true));
            Assert.That(TestJson.Objects(subOperator, "outputs").Select(o => (string)o.Get("name")), Is.EqualTo(new[] { "Out" }));
            var init = ContextOfType(graph, "VFXBasicInitialize");
            var life = TestJson.Objects(init, "blocks").Single(b => (string)b.Get("type") == "SetAttribute");
            Assert.That(links, Does.Contain($"{ParameterId("X")}.o -> {subOperator.Get("id")}.In"));
            Assert.That(links, Does.Contain($"{subOperator.Get("id")}.Out -> {life.Get("id")}._Lifetime"));

            // .vfxblock: a block in Initialize holding the subgraph, its input Size
            var subBlock = TestJson.Objects(init, "blocks").Single(b => (string)b.Get("type") == "VFXSubgraphBlock");
            Assert.That(GuidOf(Setting(subBlock, "m_Subgraph")), Is.EqualTo(blockGuid));
            Assert.That(Number(Input(subBlock, "Size").Get("value")), Is.EqualTo(0.7).Within(1e-6));
            Assert.That(links, Does.Contain($"{ParameterId("S")}.o -> {subBlock.Get("id")}.Size"));

            // .vfx: a subgraph context holding the graph, its input Count
            var subContext = ContextOfType(graph, "VFXSubgraphContext");
            Assert.That(GuidOf(Setting(subContext, "m_Subgraph")), Is.EqualTo(vfxGuid));
            Assert.That(Number(Input(subContext, "Count").Get("value")), Is.EqualTo(9.0));
            Assert.That(links, Does.Contain($"{ParameterId("N")}.o -> {subContext.Get("id")}.Count"));

            // Deterministic: a second build describes the same.
            var again = Build("Main", SubgraphRecipe(Folder + "/Twice.vfxoperator", Folder + "/Grow.vfxblock", Folder + "/Sub.vfx"));
            Assert.That(again.Get("guid_stable"), Is.EqualTo(true));
            Assert.That(again.Get("describe_sha256"), Is.EqualTo(built.Get("describe_sha256")));
        }

        static IEnumerable<TestCaseData> BadSubgraphs()
        {
            yield return new TestCaseData(Op("use_subgraph", ("id", "g"), ("asset", Folder + "/Grow.vfxblock")), "a subgraph block needs 'context'")
                .SetName("UseSubgraph_BlockWithoutContext_Fails");
            yield return new TestCaseData(Op("use_subgraph", ("id", "g"), ("asset", Folder + "/Twice.vfxoperator"), ("context", "init")), "a subgraph operator is not placed in a context")
                .SetName("UseSubgraph_OperatorWithContext_Fails");
            yield return new TestCaseData(Op("use_subgraph", ("id", "g"), ("asset", Folder + "/Twice.vfxop")), "unknown subgraph extension '.vfxop'")
                .SetName("UseSubgraph_UnknownExtension_ListsTheExtensions");
            yield return new TestCaseData(Op("use_subgraph", ("id", "g"), ("asset", Folder + "/Missing.vfxoperator")), "no asset at '" + Folder + "/Missing.vfxoperator'")
                .SetName("UseSubgraph_MissingAsset_Fails");
            yield return new TestCaseData(Op("use_subgraph", ("id", "g"), ("asset", Folder + "/Twice.vfxoperator"), ("settings", Map())), "unknown field of use_subgraph 'settings'")
                .SetName("UseSubgraph_Settings_IsNotAField");
            yield return new TestCaseData(Op("use_subgraph", ("id", "g"), ("asset", Folder + "/Twice.vfxoperator"), ("slots", Map(("Inn", 1.0)))), "unknown input slot of 'g'")
                .SetName("UseSubgraph_UnknownSlot_ListsTheInputs");
            yield return new TestCaseData(Op("use_subgraph", ("id", "g"), ("asset", Folder + "/InitOnly.vfxblock"), ("context", "update")), "cannot be added to context 'update'")
                .SetName("UseSubgraph_BlockInAContextItDoesNotSuit_Fails");
        }

        [TestCaseSource(nameof(BadSubgraphs))]
        public void BadSubgraph_FailsTheBuild_WithAClearError(JsonObject op, string expected)
        {
            CreateOperatorSubgraph(Folder + "/Twice.vfxoperator");
            CreateBlockSubgraph(Folder + "/Grow.vfxblock");
            CreateBlockSubgraph(Folder + "/InitOnly.vfxblock", "Init");
            var result = BuildFails("Bad", OneSystem(atEnd: new[] { op }));
            var errors = TestJson.Strings(result, "errors");
            Assert.That(errors, Has.Count.EqualTo(1));
            StringAssert.Contains(expected, errors[0]);
        }

        [Test]
        public void Bridge_NewSubgraphs_AreSavedAtTheCurrentGraphAndResourceVersions()
        {
            var operatorPath = Folder + "/Twice.vfxoperator";
            var blockPath = Folder + "/Grow.vfxblock";
            CreateOperatorSubgraph(operatorPath);
            CreateBlockSubgraph(blockPath);
            foreach (var path in new[] { operatorPath, blockPath })
            {
                VfxGraphBridge.ForgetResource(path);
                var versions = VfxGraphBridge.GraphVersions(path);
                Assert.That(versions.Get("resource_version"), Is.EqualTo(VfxGraphBridge.CurrentResourceVersion), path + " resource version");
                Assert.That(versions.Get("graph_version"), Is.EqualTo(VfxGraphBridge.CurrentGraphVersion), path + " graph version");
                Assert.That(File.ReadAllText(Path.GetFullPath(path)), Does.Contain("m_ResourceVersion: " + VfxGraphBridge.CurrentResourceVersion), path);
            }
        }

        [Test]
        public void Bridge_OutputParameterOutsideASubgraph_AndAWrongSubgraphExtension_Fail()
        {
            var edit = VfxGraphBridge.GraphEdit.Create(Folder + "/Plain.vfx");
            var exception = Assert.Throws<VfxGraphBridge.EditException>(() => edit.AddOutputParameter("p", "float", "Out", null));
            StringAssert.Contains("only in a subgraph operator", exception.Message);
            exception = Assert.Throws<VfxGraphBridge.EditException>(() => VfxGraphBridge.GraphEdit.CreateSubgraph(Folder + "/Plain.vfx"));
            StringAssert.Contains(".vfxoperator or .vfxblock", exception.Message);
        }

        // ------------------------------------------------------------ coverage of the op list

        [Test]
        public void EveryOpAndField_IsUsedByARecipeThatBuilds()
        {
            var fixture = (List<object>)((JsonObject)Json.Parse(File.ReadAllText(Path.GetFullPath(
                "Packages/com.cuvara.vfx/Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json")))).Get("ops");
            var recipes = new[]
            {
                fixture,
                OutputRecipe(Folder + "/Mask.png", "0123456789abcdef0123456789abcdef"),
                FieldsRecipe(),
                SubgraphRecipe(Folder + "/Twice.vfxoperator", Folder + "/Grow.vfxblock", Folder + "/Sub.vfx"),
                SubRecipe(),
            };
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var op in recipes.SelectMany(r => r).Cast<JsonObject>())
            {
                var name = (string)op.Get("op");
                foreach (var pair in op)
                    used.Add(name + "." + pair.Key);
            }

            var missing = VfxRecipeOps.Specs
                .SelectMany(spec => spec.Required.Concat(spec.Optional).Select(field => spec.Name + "." + field))
                .Where(field => !used.Contains(field))
                .ToList();
            Assert.That(missing, Is.Empty, "op fields no built recipe uses: " + string.Join(", ", missing));
        }
    }
}
