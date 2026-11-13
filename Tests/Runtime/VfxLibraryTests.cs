using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Cuvara.VFX.Tests
{
    public sealed class VfxLibraryTests
    {
        TestObjects objects;

        [SetUp]
        public void SetUp() => objects = new TestObjects();

        [TearDown]
        public void TearDown() => objects.DestroyAll();

        [Test]
        public void TryGet_FindsEntryById()
        {
            GameObject spark = objects.EffectPrefab("Spark");
            GameObject bolt = objects.EffectPrefab("Bolt");
            VfxLibrary library = objects.Library(
                new VfxLibrary.Entry("spark", spark, 0.5f, "impact"),
                new VfxLibrary.Entry("bolt", bolt, 0.9f, "lightning", "chain"));

            Assert.IsTrue(library.TryGet("bolt", out VfxLibrary.Entry entry));
            Assert.AreSame(bolt, entry.Prefab);
            Assert.AreEqual(0.9f, entry.DurationSeconds);
            CollectionAssert.AreEqual(new[] { "lightning", "chain" }, entry.Tags);
            Assert.IsTrue(entry.HasTag("chain"));
            Assert.IsFalse(entry.HasTag("Chain"));
            Assert.IsTrue(library.Contains("spark"));
        }

        [Test]
        public void TryGet_UnknownNullOrEmptyId_ReturnsFalse()
        {
            VfxLibrary library = objects.Library(new VfxLibrary.Entry("spark", objects.EffectPrefab("Spark"), 0.5f));

            Assert.IsFalse(library.TryGet("Spark", out VfxLibrary.Entry entry));
            Assert.IsNull(entry);
            Assert.IsFalse(library.TryGet(null, out _));
            Assert.IsFalse(library.TryGet(string.Empty, out _));
        }

        [Test]
        public void TryGet_DuplicateId_FirstEntryWins()
        {
            GameObject first = objects.EffectPrefab("First");
            GameObject second = objects.EffectPrefab("Second");
            VfxLibrary library = objects.Library(
                new VfxLibrary.Entry("spark", first, 0.5f),
                new VfxLibrary.Entry("spark", second, 0.7f));

            Assert.IsTrue(library.TryGet("spark", out VfxLibrary.Entry entry));
            Assert.AreSame(first, entry.Prefab);
        }

        [Test]
        public void SetEntries_RebuildsLookup()
        {
            VfxLibrary library = objects.Library(new VfxLibrary.Entry("spark", objects.EffectPrefab("Spark"), 0.5f));
            Assert.IsTrue(library.Contains("spark"));

            library.SetEntries(new[] { new VfxLibrary.Entry("bolt", objects.EffectPrefab("Bolt"), 0.5f) });

            Assert.IsFalse(library.Contains("spark"));
            Assert.IsTrue(library.Contains("bolt"));
        }

        [Test]
        public void Validate_CleanLibrary_ReportsNothing()
        {
            VfxLibrary library = objects.Library(
                new VfxLibrary.Entry("spark", objects.EffectPrefab("Spark"), 0.5f),
                new VfxLibrary.Entry("bolt", objects.EffectPrefab("Bolt"), 0f));

            CollectionAssert.IsEmpty(library.Validate());
        }

        [Test]
        public void Validate_ReportsEmptyDuplicateMissingPrefabNoEffectAndNegativeDuration()
        {
            GameObject noEffect = objects.GameObject("NoEffect");
            VfxLibrary library = objects.Library(
                new VfxLibrary.Entry("spark", objects.EffectPrefab("Spark"), 0.5f),
                new VfxLibrary.Entry("", objects.EffectPrefab("Empty"), 0.5f),
                new VfxLibrary.Entry("spark", objects.EffectPrefab("Spark2"), 0.5f),
                new VfxLibrary.Entry("missing", null, 0.5f),
                new VfxLibrary.Entry("plain", noEffect, 0.5f),
                new VfxLibrary.Entry("negative", objects.EffectPrefab("Negative"), -1f));

            var problems = new List<string>();
            int count = library.Validate(problems);

            Assert.AreEqual(5, count, string.Join("\n", problems));
            Assert.AreEqual(5, problems.Count);
            StringAssert.Contains("entry 1: empty id", problems[0]);
            StringAssert.Contains("duplicate id 'spark'", problems[1]);
            StringAssert.Contains("'missing': no prefab", problems[2]);
            StringAssert.Contains("has no VisualEffect", problems[3]);
            StringAssert.Contains("negative duration", problems[4]);
        }

        [Test]
        public void Validate_AppendsToExistingList()
        {
            VfxLibrary library = objects.Library(new VfxLibrary.Entry("", objects.EffectPrefab("Empty"), 0.5f));
            var problems = new List<string> { "earlier" };

            Assert.AreEqual(1, library.Validate(problems));
            Assert.AreEqual(2, problems.Count);
        }
    }
}
