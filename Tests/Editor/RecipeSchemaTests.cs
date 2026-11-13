using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// <c>Schemas~/vfx-recipe.schema.json</c> against the fixture recipe and against the op list of the
    /// builder (<see cref="VfxRecipeOps"/>), so the schema, the builder and <c>catalog</c> agree.
    /// </summary>
    public sealed class RecipeSchemaTests
    {
        const string SchemaPath = "Packages/com.cuvara.vfx/Schemas~/vfx-recipe.schema.json";
        const string FixtureRecipe = "Packages/com.cuvara.vfx/Tests/Fixtures/BurstQuads/BurstQuads.vfxrecipe.json";

        static JsonObject Load(string path) => (JsonObject)Json.Parse(File.ReadAllText(Path.GetFullPath(path)));

        [Test]
        public void FixtureRecipe_ValidatesAgainstTheSchema()
        {
            var errors = TestJsonSchema.Validate(Load(SchemaPath), Load(FixtureRecipe));
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void BrokenRecipe_FailsTheSchema()
        {
            var recipe = Load(FixtureRecipe);
            var ops = (List<object>)recipe.Get("ops");
            ((JsonObject)ops[3]).Add("typo", 1);
            ((JsonObject)ops[4]).Set("op", "add_contxt");
            recipe.Set("schema", "cuvara.vfx-recipe/0");

            var errors = TestJsonSchema.Validate(Load(SchemaPath), recipe);
            Assert.That(errors, Has.Some.Contains("/ops/3/typo"));
            Assert.That(errors, Has.Some.Contains("/ops/4/op"));
            Assert.That(errors, Has.Some.Contains("/schema"));
        }

        [Test]
        public void Schema_ListsTheBuilderOpsWithTheirFields()
        {
            var schema = Load(SchemaPath);
            Assert.That(((JsonObject)schema.Get("properties")).Get("schema"), Is.Not.Null);
            var defs = (JsonObject)schema.Get("$defs");
            var opEnum = (List<object>)((JsonObject)((JsonObject)((JsonObject)defs.Get("op")).Get("properties")).Get("op")).Get("enum");
            Assert.That(opEnum.Cast<string>(), Is.EqualTo(VfxRecipeOps.Names));

            foreach (var spec in VfxRecipeOps.Specs)
            {
                var opSchema = (JsonObject)defs.Get(spec.Name);
                Assert.That(opSchema, Is.Not.Null, spec.Name);
                var required = ((List<object>)opSchema.Get("required")).Cast<string>().Where(f => f != "op");
                Assert.That(required, Is.EquivalentTo(spec.Required), spec.Name + " required");
                var properties = ((JsonObject)opSchema.Get("properties")).Select(p => p.Key).Where(f => f != "op");
                Assert.That(properties, Is.EquivalentTo(spec.Required.Concat(spec.Optional)), spec.Name + " fields");
            }
        }

        [Test]
        public void Catalog_PrintsTheRecipeOpsAndTheirFields()
        {
            var catalog = TestJson.Ok("catalog", "{\"filter\": \"VFXBasicSpawner\"}");
            Assert.That(TestJson.Strings(catalog, "recipe_ops"), Is.EqualTo(VfxRecipeOps.Names));
            var fields = (JsonObject)catalog.Get("recipe_op_fields");
            Assert.That(fields.Select(p => p.Key), Is.EqualTo(VfxRecipeOps.Names));
            Assert.That(TestJson.Strings((JsonObject)fields.Get("add_block"), "required"), Is.EqualTo(new[] { "id", "context", "type" }));
        }
    }
}
