using System.Linq;
using NUnit.Framework;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>The catalog lists contexts, blocks and operators with settings and slots, and is deterministic.</summary>
    public sealed class CatalogVerbTests
    {
        [Test]
        public void Catalog_ListsContextsBlocksOperatorsAndParameterTypes()
        {
            var result = TestJson.Ok("catalog", "{}");
            StringAssert.StartsWith("17.6", (string)result.Get("vfx_graph"));
            Assert.That(TestJson.Strings(result, "recipe_ops"), Does.Contain("add_context").And.Contain("link_slots"));

            var contexts = TestJson.Objects(result, "contexts");
            var blocks = TestJson.Objects(result, "blocks");
            var operators = TestJson.Objects(result, "operators");
            var parameters = TestJson.Objects(result, "parameter_types");
            Assert.That(contexts, Is.Not.Empty);
            Assert.That(blocks, Is.Not.Empty);
            Assert.That(operators, Is.Not.Empty);
            Assert.That(parameters, Is.Not.Empty);

            var counts = (JsonObject)result.Get("counts");
            Assert.That(counts.Get("blocks"), Is.EqualTo((long)blocks.Count));

            var types = contexts.Select(c => (string)c.Get("type")).ToList();
            Assert.That(types, Does.Contain("VFXBasicSpawner").And.Contain("VFXBasicInitialize").And.Contain("VFXBasicUpdate"));

            var burst = blocks.First(b => (string)b.Get("type") == "VFXSpawnerBurst");
            Assert.That(TestJson.Objects(burst, "inputs").Select(s => (string)s.Get("name")), Does.Contain("Count"));
            var repeat = TestJson.Objects(burst, "settings").First(s => (string)s.Get("name") == "repeat");
            Assert.That(repeat.Get("kind"), Is.EqualTo("enum"));
            Assert.That(TestJson.Strings(repeat, "allowed_values"), Does.Contain("Single").And.Contain("Periodic"));
            Assert.That(((JsonObject)burst.Get("variant")).Get("id"), Is.Not.Null);

            var add = operators.First(o => (string)o.Get("type") == "Add");
            Assert.That(TestJson.Objects(add, "outputs"), Is.Not.Empty);
            Assert.That(parameters.Select(p => (string)p.Get("type")), Does.Contain("Color").Or.Contain("Vector4"));
        }

        [Test]
        public void Catalog_IsDeterministic()
        {
            var first = VfxCli.Run("catalog", "{\"include_sub_variants\": true}");
            var second = VfxCli.Run("catalog", "{\"include_sub_variants\": true}");
            Assert.That(second, Is.EqualTo(first));
            Assert.That(((JsonObject)Json.Parse(first)).Get("ok"), Is.EqualTo(true));
        }

        [Test]
        public void Catalog_FilterNarrowsTheList()
        {
            var all = TestJson.Ok("catalog", "{}");
            var filtered = TestJson.Ok("catalog", "{\"filter\": \"spawner\"}");
            var allCount = TestJson.Objects(all, "contexts").Count + TestJson.Objects(all, "blocks").Count + TestJson.Objects(all, "operators").Count;
            var filteredCount = TestJson.Objects(filtered, "contexts").Count + TestJson.Objects(filtered, "blocks").Count + TestJson.Objects(filtered, "operators").Count;
            Assert.That(filteredCount, Is.GreaterThan(0).And.LessThan(allCount));
            Assert.That(TestJson.Objects(filtered, "contexts").Select(c => (string)c.Get("type")), Does.Contain("VFXBasicSpawner"));
        }
    }
}
