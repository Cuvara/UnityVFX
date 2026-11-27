using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// <c>inspect</c> on the committed BurstQuads fixture (one burst of additive quads, capacity 32, one
    /// exposed HDR colour <c>Tint</c>) and on a prefab made at test time in a temporary folder that
    /// holds a <c>VisualEffect</c> with that graph and two property overrides.
    /// </summary>
    public sealed class InspectVerbTests
    {
        const string Fixture = "Packages/com.cuvara.vfx/Tests/Fixtures/BurstQuads/BurstQuads.vfx";
        const string FixtureRecipe = "Packages/com.cuvara.vfx/Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json";
        const string SchemaPath = "Packages/com.cuvara.vfx/Schemas~/vfx-inspect.schema.json";
        const string DefaultDot = "Packages/com.unity.visualeffectgraph/Textures/DefaultDot.tga";
        const string OverrideTexture = "Packages/com.unity.visualeffectgraph/Textures/DefaultParticle.tga";
        const string Folder = "Assets/CuvaraVfxInspectTestTemp";
        const string Prefab = Folder + "/InspectFixture.prefab";

        static string Args(string effect) => Json.Write(new JsonObject { { "effect", effect } });

        static JsonObject Inspect(string effect) => TestJson.Ok("inspect", Args(effect));

        static double Number(object value) => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);

        static List<double> Numbers(object list) => ((List<object>)list).Select(v => v == null ? double.NaN : Number(v)).ToList();

        static JsonObject Single(JsonObject result, string key, string field, string value) =>
            TestJson.Objects(result, key).Single(o => (string)o.Get(field) == value);

        [OneTimeSetUp]
        public void CreatePrefab()
        {
            DeleteFolder();
            AssetDatabase.CreateFolder("Assets", "CuvaraVfxInspectTestTemp");

            var root = new GameObject("InspectFixture");
            try
            {
                var child = new GameObject("Fx");
                child.transform.SetParent(root.transform, false);
                var effect = child.AddComponent<VisualEffect>();
                effect.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Fixture);

                // Overrides as the VisualEffect inspector writes them: an entry of the property sheet
                // with m_Overridden on. "Ghost" names no exposed property of the graph.
                using (var serialized = new SerializedObject(effect))
                {
                    var tint = AddEntry(serialized, "m_Vector4f", "Tint");
                    tint.FindPropertyRelative("m_Value").vector4Value = new Vector4(8f, 1f, 0.5f, 1f);
                    var ghost = AddEntry(serialized, "m_NamedObject", "Ghost");
                    ghost.FindPropertyRelative("m_Value").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Texture2D>(OverrideTexture);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(root, Prefab, out var saved);
                Assert.That(saved, Is.True, "save " + Prefab);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static SerializedProperty AddEntry(SerializedObject serialized, string sheet, string name)
        {
            var array = serialized.FindProperty("m_PropertySheet." + sheet + ".m_Array");
            Assert.That(array, Is.Not.Null, sheet);
            array.InsertArrayElementAtIndex(array.arraySize);
            var entry = array.GetArrayElementAtIndex(array.arraySize - 1);
            entry.FindPropertyRelative("m_Name").stringValue = name;
            entry.FindPropertyRelative("m_Overridden").boolValue = true;
            return entry;
        }

        [OneTimeTearDown]
        public void DeleteFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void Inspect_BurstQuads_ReportsTheSystemWithItsCapacity()
        {
            var result = Inspect(Fixture);
            Assert.That(result.Get("schema"), Is.EqualTo("cuvara.vfx-inspect/1"));
            Assert.That(result.Get("prefab"), Is.Null);
            Assert.That(((JsonObject)result.Get("asset")).Get("guid"), Is.EqualTo(AssetDatabase.AssetPathToGUID(Fixture)));

            var particles = Single(result, "systems", "data_type", "Particle");
            Assert.That(particles.Get("capacity"), Is.EqualTo(32L));
            Assert.That(TestJson.Strings(particles, "outputs"), Has.Count.EqualTo(1));
            Assert.That(Single(result, "systems", "data_type", "SpawnEvent").Get("capacity"), Is.Null);
            Assert.That(((JsonObject)result.Get("totals")).Get("capacity"), Is.EqualTo(32L));
        }

        [Test]
        public void Inspect_BurstQuads_ReportsTheAdditiveQuadOutputAndItsGeneratedShader()
        {
            var result = Inspect(Fixture);
            var outputs = TestJson.Objects(result, "outputs");
            Assert.That(outputs, Has.Count.EqualTo(1));
            var output = outputs[0];
            var particles = Single(result, "systems", "data_type", "Particle");
            Assert.That(output.Get("system"), Is.EqualTo(particles.Get("id")));
            Assert.That(output.Get("type"), Is.EqualTo("VFXPlanarPrimitiveOutput"));
            Assert.That(output.Get("blend_mode"), Is.EqualTo("Additive"));

            var shader = (JsonObject)output.Get("shader");
            Assert.That(shader, Is.Not.Null, "warnings: " + string.Join(" | ", TestJson.Strings(result, "warnings")));
            Assert.That(shader.Get("name"), Is.EqualTo($"Hidden/VFX/BurstQuads/{particles.Get("name")}/Output Particle Unlit Quad"));
            Assert.That(shader.Get("guid"), Is.EqualTo(AssetDatabase.AssetPathToGUID(Fixture)));
            Assert.That(output.Get("shader_graph"), Is.Null);

            var effective = (JsonObject)output.Get("effective");
            Assert.That(effective.Get("sorted"), Is.EqualTo(false), "sort Auto does not sort an additive output");
            Assert.That(effective.Get("soft_particles"), Is.EqualTo(false));
            Assert.That(effective.Get("shadow_casting"), Is.EqualTo(false));

            var settings = (JsonObject)output.Get("render_settings");
            Assert.That(settings.Get("primitiveType"), Is.EqualTo("Quad"));
            Assert.That(settings.Get("sort"), Is.EqualTo("Auto"));
            Assert.That(settings.Has("blendMode"), Is.False, "blend mode has its own field");
        }

        [Test]
        public void Inspect_BurstQuads_ReportsTheExposedColourWithItsDefaultAndTheHdrValue()
        {
            var result = Inspect(Fixture);
            var tint = Single(result, "exposed_properties", "name", "Tint");
            Assert.That(tint.Get("type"), Is.EqualTo("Color"));
            Assert.That(Numbers(tint.Get("default")), Is.EqualTo(new[] { 4.0, 2.0, 0.5, 1.0 }));
            Assert.That(tint.Get("overridden"), Is.EqualTo(false));

            var hdr = TestJson.Objects(result, "hdr_colors");
            Assert.That(hdr, Has.Count.EqualTo(1));
            Assert.That(hdr[0].Get("source"), Is.EqualTo("parameter"));
            Assert.That(hdr[0].Get("path"), Is.EqualTo(tint.Get("id")));
            Assert.That(hdr[0].Get("name"), Is.EqualTo("Tint"));
            Assert.That(Numbers(hdr[0].Get("rgba")), Is.EqualTo(new[] { 4.0, 2.0, 0.5, 1.0 }));
            Assert.That(Number(hdr[0].Get("peak")), Is.EqualTo(4.0));
            Assert.That(Number(hdr[0].Get("intensity")), Is.EqualTo(2.0));
        }

        [Test]
        public void Inspect_BurstQuads_ReportsTheOutputTextureWithItsImportFacts()
        {
            var result = Inspect(Fixture);
            var texture = Single(result, "textures", "path", DefaultDot);
            var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultDot);
            Assert.That(texture.Get("guid"), Is.EqualTo(AssetDatabase.AssetPathToGUID(DefaultDot)));
            Assert.That(texture.Get("width"), Is.EqualTo((long)imported.width));
            Assert.That(texture.Get("height"), Is.EqualTo((long)imported.height));
            Assert.That(texture.Get("format"), Is.EqualTo(imported.graphicsFormat.ToString()));
            Assert.That(texture.Get("mip_count"), Is.EqualTo((long)imported.mipmapCount));
            Assert.That(texture.Get("srgb"), Is.EqualTo(((TextureImporter)AssetImporter.GetAtPath(DefaultDot)).sRGBTexture));
            Assert.That(TestJson.Strings(texture, "used_by"), Is.EqualTo(new[] { "c3.mainTexture" }));
            // 128 x 128 RGBA32 with 8 mips: 4 * (128^2 + 64^2 + ... + 1^2) bytes.
            if (imported.width == 128 && imported.height == 128 && imported.mipmapCount == 8 && imported.format == TextureFormat.RGBA32)
                Assert.That(texture.Get("estimated_bytes"), Is.EqualTo(87380L));
        }

        [Test]
        public void Inspect_BurstQuads_ReportsTheRecipeAndTheDescribeHash()
        {
            var result = Inspect(Fixture);
            var recipe = (JsonObject)result.Get("recipe");
            Assert.That(recipe.Get("path"), Is.EqualTo(FixtureRecipe));
            Assert.That(recipe.Get("sha256"), Is.EqualTo(VfxRecipeBuilder.Sha256(File.ReadAllBytes(Path.GetFullPath(FixtureRecipe)))));

            var describe = VfxCli.Run("describe", Json.Write(new JsonObject { { "asset", Fixture } }));
            Assert.That(result.Get("describe_sha256"), Is.EqualTo(VfxRecipeBuilder.Sha256(new UTF8Encoding(false).GetBytes(describe))));
        }

        [Test]
        public void Inspect_IsDeterministic()
        {
            var first = VfxCli.Run("inspect", Args(Fixture));
            var second = VfxCli.Run("inspect", Args(Fixture));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(VfxCli.Run("inspect", Args(Prefab)), Is.EqualTo(VfxCli.Run("inspect", Args(Prefab))));
        }

        [Test]
        public void Inspect_Output_ValidatesAgainstTheSchema()
        {
            var schema = (JsonObject)Json.Parse(File.ReadAllText(Path.GetFullPath(SchemaPath)));
            foreach (var effect in new[] { Fixture, Prefab })
            {
                var errors = TestJsonSchema.Validate(schema, Inspect(effect));
                Assert.That(errors, Is.Empty, effect + ":\n" + string.Join("\n", errors));
            }
        }

        [Test]
        public void Inspect_Prefab_ReportsTheOverridesOfItsVisualEffect()
        {
            var result = Inspect(Prefab);
            var effect = (JsonObject)result.Get("effect");
            Assert.That(effect.Get("kind"), Is.EqualTo("prefab"));
            var prefab = (JsonObject)result.Get("prefab");
            Assert.That(prefab.Get("path"), Is.EqualTo(Prefab));
            Assert.That(prefab.Get("visual_effect"), Is.EqualTo("InspectFixture/Fx"));
            Assert.That(((JsonObject)result.Get("asset")).Get("path"), Is.EqualTo(Fixture));

            var tint = Single(result, "exposed_properties", "name", "Tint");
            Assert.That(tint.Get("overridden"), Is.EqualTo(true));
            var overrides = TestJson.Objects(tint, "overrides");
            Assert.That(overrides, Has.Count.EqualTo(1));
            Assert.That(overrides[0].Get("sheet"), Is.EqualTo("m_Vector4f"));
            Assert.That(Numbers(overrides[0].Get("value")), Is.EqualTo(new[] { 8.0, 1.0, 0.5, 1.0 }));

            var hdr = TestJson.Objects(result, "hdr_colors");
            var fromPrefab = hdr.Single(h => (string)h.Get("source") == "prefab_override");
            Assert.That(fromPrefab.Get("path"), Is.EqualTo("prefab:Tint"));
            Assert.That(Number(fromPrefab.Get("peak")), Is.EqualTo(8.0));
            Assert.That(Number(fromPrefab.Get("intensity")), Is.EqualTo(3.0));
            Assert.That(Number(((JsonObject)result.Get("totals")).Get("hdr_peak")), Is.EqualTo(8.0));

            var texture = Single(result, "textures", "path", OverrideTexture);
            Assert.That(TestJson.Strings(texture, "used_by"), Is.EqualTo(new[] { "prefab:Ghost" }));
            var warnings = TestJson.Strings(result, "warnings");
            Assert.That(warnings.Any(w => w.Contains("'Ghost'") && w.Contains("matches no exposed property")), Is.True, string.Join(" | ", warnings));
        }

        [Test]
        public void Inspect_NotAnEffect_IsOkFalse()
        {
            var recipe = TestJson.Result("inspect", Args(FixtureRecipe));
            Assert.That(recipe.Get("ok"), Is.EqualTo(false));
            StringAssert.Contains("must be a .prefab", TestJson.Strings(recipe, "errors")[0]);

            var missing = TestJson.Result("inspect", Args(Folder + "/Missing.vfx"));
            Assert.That(missing.Get("ok"), Is.EqualTo(false));
            StringAssert.Contains("Missing.vfx", TestJson.Strings(missing, "errors")[0]);

            var noArgument = TestJson.Result("inspect", "{}");
            Assert.That(noArgument.Get("ok"), Is.EqualTo(false));
            StringAssert.Contains("effect", TestJson.Strings(noArgument, "errors")[0]);
        }

        [Test]
        public void Inspect_PrefabWithoutVisualEffect_IsOkFalse()
        {
            const string empty = Folder + "/Empty.prefab";
            var root = new GameObject("Empty");
            try
            {
                PrefabUtility.SaveAsPrefabAsset(root, empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            var result = TestJson.Result("inspect", Args(empty));
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            StringAssert.Contains("has no VisualEffect", TestJson.Strings(result, "errors")[0]);
        }
    }
}
