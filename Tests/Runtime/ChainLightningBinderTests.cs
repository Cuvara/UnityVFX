using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.VFX;

namespace Cuvara.VFX.Tests
{
    /// <summary>
    /// The binder is asserted through its internal seam <c>Apply(IVfxPropertySink)</c> with a fake sink,
    /// because the package has no VisualEffectAsset with these exposed properties yet; a real
    /// <see cref="VisualEffect"/> without an asset proves that missing properties are skipped silently.
    /// </summary>
    public sealed class ChainLightningBinderTests
    {
        sealed class FakeSink : IVfxPropertySink
        {
            public readonly HashSet<int> Vector3Ids = new HashSet<int>();
            public readonly HashSet<int> IntIds = new HashSet<int>();
            public readonly HashSet<int> UIntIds = new HashSet<int>();
            public readonly HashSet<int> FloatIds = new HashSet<int>();
            public readonly Dictionary<int, Vector3> Vectors = new Dictionary<int, Vector3>();
            public readonly Dictionary<int, int> Ints = new Dictionary<int, int>();
            public readonly Dictionary<int, uint> UInts = new Dictionary<int, uint>();
            public readonly Dictionary<int, float> Floats = new Dictionary<int, float>();
            public int Writes;

            public static FakeSink WithDefaults()
            {
                var sink = new FakeSink();
                sink.Vector3Ids.Add(Shader.PropertyToID(ChainLightningBinder.DefaultSourceProperty));
                for (int i = 0; i < ChainLightningBinder.MaxTargets; i++)
                    sink.Vector3Ids.Add(Target(i));
                sink.IntIds.Add(Shader.PropertyToID(ChainLightningBinder.DefaultHopCountProperty));
                return sink;
            }

            public bool HasVector3(int id) => Vector3Ids.Contains(id);
            public void SetVector3(int id, Vector3 value) { Vectors[id] = value; Writes++; }
            public bool HasInt(int id) => IntIds.Contains(id);
            public void SetInt(int id, int value) { Ints[id] = value; Writes++; }
            public bool HasUInt(int id) => UIntIds.Contains(id);
            public void SetUInt(int id, uint value) { UInts[id] = value; Writes++; }
            public bool HasFloat(int id) => FloatIds.Contains(id);
            public void SetFloat(int id, float value) { Floats[id] = value; Writes++; }
        }

        static readonly int SourceId = Shader.PropertyToID(ChainLightningBinder.DefaultSourceProperty);
        static readonly int HopId = Shader.PropertyToID(ChainLightningBinder.DefaultHopCountProperty);

        static int Target(int index) => Shader.PropertyToID(ChainLightningBinder.DefaultTargetPropertyPrefix + index);

        TestObjects objects;
        ChainLightningBinder binder;

        [SetUp]
        public void SetUp()
        {
            objects = new TestObjects();
            binder = objects.GameObject("Chain").AddComponent<ChainLightningBinder>();
            binder.Source = Point("Caster", new Vector3(0f, 1f, 0f));
        }

        [TearDown]
        public void TearDown() => objects.DestroyAll();

        Transform Point(string name, Vector3 position)
        {
            Transform t = objects.GameObject(name).transform;
            t.position = position;
            return t;
        }

        Transform[] Row(int count)
        {
            var row = new Transform[count];
            for (int i = 0; i < count; i++) row[i] = Point("Target" + i, new Vector3(2f * (i + 1), 1f, 0f));
            return row;
        }

        [Test]
        public void DefaultPropertyNames_AreDocumentedValues()
        {
            Assert.AreEqual(8, ChainLightningBinder.MaxTargets);
            Assert.AreEqual("SourcePosition", binder.SourceProperty);
            Assert.AreEqual("TargetPosition", binder.TargetPropertyPrefix);
            Assert.AreEqual("HopCount", binder.HopCountProperty);
        }

        [Test]
        public void Apply_WritesSourceTargetsAndHopCount()
        {
            Transform[] row = Row(3);
            binder.SetTargets(row);
            FakeSink sink = FakeSink.WithDefaults();

            Assert.AreEqual(3, binder.Apply(sink));

            Assert.AreEqual(3, binder.HopCount);
            Assert.AreEqual(3, sink.Ints[HopId]);
            Assert.AreEqual(new Vector3(0f, 1f, 0f), sink.Vectors[SourceId]);
            for (int i = 0; i < 3; i++) Assert.AreEqual(row[i].position, sink.Vectors[Target(i)], "slot " + i);
            for (int i = 3; i < ChainLightningBinder.MaxTargets; i++)
                Assert.AreEqual(row[2].position, sink.Vectors[Target(i)], "unused slot " + i + " repeats the last target");
        }

        [Test]
        public void Apply_SkipsNullDestroyedAndInactiveTargets_AndHopCountFollows()
        {
            Transform a = Point("A", new Vector3(1f, 0f, 0f));
            Transform destroyed = Point("Destroyed", new Vector3(2f, 0f, 0f));
            Transform inactive = Point("Inactive", new Vector3(3f, 0f, 0f));
            Transform b = Point("B", new Vector3(4f, 0f, 0f));
            inactive.gameObject.SetActive(false);
            binder.SetTargets(new[] { a, null, destroyed, inactive, b });
            Object.DestroyImmediate(destroyed.gameObject);
            FakeSink sink = FakeSink.WithDefaults();

            Assert.AreEqual(2, binder.Apply(sink));

            Assert.AreEqual(2, sink.Ints[HopId]);
            Assert.AreEqual(a.position, sink.Vectors[Target(0)]);
            Assert.AreEqual(b.position, sink.Vectors[Target(1)]);
            Assert.AreEqual(b.position, sink.Vectors[Target(2)]);
        }

        [Test]
        public void Apply_NoValidTarget_HopCountZero_SlotsRepeatSource()
        {
            binder.SetTargets(new Transform[] { null });
            FakeSink sink = FakeSink.WithDefaults();

            Assert.AreEqual(0, binder.Apply(sink));

            Assert.AreEqual(0, sink.Ints[HopId]);
            Assert.AreEqual(binder.Source.position, sink.Vectors[Target(0)]);
        }

        [Test]
        public void Apply_MoreTargetsThanMax_WritesTheFirstMax()
        {
            Transform[] row = Row(ChainLightningBinder.MaxTargets + 3);
            binder.SetTargets(row);
            FakeSink sink = FakeSink.WithDefaults();

            Assert.AreEqual(ChainLightningBinder.MaxTargets, binder.Apply(sink));

            Assert.AreEqual(row[ChainLightningBinder.MaxTargets - 1].position, sink.Vectors[Target(ChainLightningBinder.MaxTargets - 1)]);
            Assert.IsFalse(sink.Vectors.ContainsKey(Target(ChainLightningBinder.MaxTargets)));
        }

        [Test]
        public void Apply_GraphWithoutProperties_WritesNothing()
        {
            binder.SetTargets(Row(2));
            var sink = new FakeSink();

            Assert.AreEqual(2, binder.Apply(sink));

            Assert.AreEqual(0, sink.Writes);
        }

        [Test]
        public void Apply_HopCountExposedAsUIntOrFloat_IsWritten()
        {
            binder.SetTargets(Row(2));
            var uintSink = new FakeSink();
            uintSink.UIntIds.Add(HopId);
            var floatSink = new FakeSink();
            floatSink.FloatIds.Add(HopId);

            binder.Apply(uintSink);
            binder.Apply(floatSink);

            Assert.AreEqual(2u, uintSink.UInts[HopId]);
            Assert.AreEqual(2f, floatSink.Floats[HopId]);
        }

        [Test]
        public void Apply_CustomPropertyNames_AreUsed()
        {
            binder.SourceProperty = "From";
            binder.TargetPropertyPrefix = "Hop";
            binder.HopCountProperty = "Hops";
            Transform[] row = Row(1);
            binder.SetTargets(row);
            var sink = new FakeSink();
            sink.Vector3Ids.Add(Shader.PropertyToID("From"));
            sink.Vector3Ids.Add(Shader.PropertyToID("Hop0"));
            sink.Vector3Ids.Add(SourceId);
            sink.IntIds.Add(Shader.PropertyToID("Hops"));

            binder.Apply(sink);

            Assert.AreEqual(binder.Source.position, sink.Vectors[Shader.PropertyToID("From")]);
            Assert.AreEqual(row[0].position, sink.Vectors[Shader.PropertyToID("Hop0")]);
            Assert.AreEqual(1, sink.Ints[Shader.PropertyToID("Hops")]);
            Assert.IsFalse(sink.Vectors.ContainsKey(SourceId), "the default name is no longer written");
        }

        [Test]
        public void Apply_LocalPositions_ConvertIntoEffectSpace()
        {
            var effect = binder.gameObject.AddComponent<VisualEffect>();
            binder.VisualEffect = effect;
            binder.transform.position = new Vector3(10f, 0f, 0f);
            binder.WriteLocalPositions = true;
            Transform[] row = Row(1);
            binder.SetTargets(row);
            FakeSink sink = FakeSink.WithDefaults();

            binder.Apply(sink);

            Assert.AreEqual(row[0].position - new Vector3(10f, 0f, 0f), sink.Vectors[Target(0)]);
        }

        [UnityTest]
        public IEnumerator RealVisualEffectWithoutProperties_BindsEveryFrameWithoutLogging()
        {
            binder.gameObject.AddComponent<VisualEffect>();
            Transform[] row = Row(3);
            binder.SetTargets(row);

            yield return null;
            Assert.AreEqual(3, binder.HopCount);

            Object.Destroy(row[1].gameObject);
            yield return null;
            yield return null;

            Assert.AreEqual(2, binder.HopCount);
            Assert.AreEqual(2, binder.Bind());
            LogAssert.NoUnexpectedReceived();
        }
    }
}
