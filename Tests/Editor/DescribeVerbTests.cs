using System.Linq;
using NUnit.Framework;
using UnityEditor;

namespace Cuvara.VFX.Editor.Tests
{
    /// <summary>
    /// describe of a graph created in a temporary folder of the project. The graph is a copy of VFX
    /// Graph's own "Simple Burst" template, made at test time and deleted afterwards; no Unity sample
    /// asset is committed to this repository.
    /// </summary>
    public sealed class DescribeVerbTests
    {
        const string Folder = "Assets/CuvaraVfxTestTemp";
        const string Template = "Packages/com.unity.visualeffectgraph/Editor/Templates/Simple_Burst.vfx";
        const string AssetPath = Folder + "/DescribeFixture.vfx";

        [OneTimeSetUp]
        public void CreateGraph()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder("Assets", "CuvaraVfxTestTemp");
            Assert.That(AssetDatabase.CopyAsset(Template, AssetPath), Is.True, "copy of " + Template);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        [OneTimeTearDown]
        public void DeleteGraph()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
        }

        static string Args => "{\"asset\": \"" + AssetPath + "\"}";

        [Test]
        public void Describe_ListsContextsFlowLinksAndSystems()
        {
            var result = TestJson.Ok("describe", Args);
            Assert.That(result.Get("schema"), Is.EqualTo("cuvara.vfx-describe/1"));
            var asset = (JsonObject)result.Get("asset");
            Assert.That(asset.Get("path"), Is.EqualTo(AssetPath));
            Assert.That(asset.Get("guid"), Is.EqualTo(AssetDatabase.AssetPathToGUID(AssetPath)));

            var contexts = TestJson.Objects(result, "contexts");
            Assert.That(contexts.Select(c => (string)c.Get("context_type")), Does.Contain("Spawner").And.Contain("Init").And.Contain("Output"));
            Assert.That(contexts.Select(c => (string)c.Get("id")), Is.EqualTo(Enumerable.Range(0, contexts.Count).Select(i => "c" + i)));
            Assert.That(TestJson.Objects(result, "flow_links"), Has.Count.GreaterThanOrEqualTo(2));

            var systems = TestJson.Objects(result, "systems");
            Assert.That(systems.Any(s => s.Get("capacity") is long capacity && capacity > 0), Is.True);
            foreach (var context in contexts)
                foreach (var block in TestJson.Objects(context, "blocks"))
                    StringAssert.StartsWith((string)context.Get("id") + ".b", (string)block.Get("id"));
        }

        [Test]
        public void Describe_IsDeterministic_AlsoAfterAReimport()
        {
            var first = VfxCli.Run("describe", Args);
            var second = VfxCli.Run("describe", Args);
            Assert.That(second, Is.EqualTo(first));
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            var third = VfxCli.Run("describe", Args);
            Assert.That(third, Is.EqualTo(first));
        }

        [Test]
        public void Describe_MissingAsset_IsOkFalse()
        {
            var result = TestJson.Result("describe", "{\"asset\": \"Assets/CuvaraVfxTestTemp/Missing.vfx\"}");
            Assert.That(result.Get("ok"), Is.EqualTo(false));
            StringAssert.Contains("Missing.vfx", TestJson.Strings(result, "errors")[0]);
        }
    }
}
