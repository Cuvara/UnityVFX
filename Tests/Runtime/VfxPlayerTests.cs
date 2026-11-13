using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using AllocIs = UnityEngine.TestTools.Constraints.Is;

namespace Cuvara.VFX.Tests
{
    public sealed class VfxPlayerTests
    {
        const string Spark = "spark";
        const string Aura = "aura";
        const float SparkSeconds = 0.1f;
        const float AuraTailSeconds = 0.1f;

        TestObjects objects;
        VfxPlayer player;

        [SetUp]
        public void SetUp()
        {
            objects = new TestObjects();
            VfxLibrary library = objects.Library(
                new VfxLibrary.Entry(Spark, objects.EffectPrefab("SparkFx"), SparkSeconds, "impact"),
                new VfxLibrary.Entry(Aura, objects.EffectPrefab("AuraFx"), AuraTailSeconds, "buff"));
            player = objects.GameObject("Player").AddComponent<VfxPlayer>();
            player.Library = library;
        }

        [TearDown]
        public void TearDown() => objects.DestroyAll();

        static IEnumerator WaitSeconds(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
            yield return null; // the player's Update of the frame after the deadline
        }

        [UnityTest]
        public IEnumerator OneShot_PlaysAtPosition_ThenReturnsToPoolAfterDuration()
        {
            var position = new Vector3(1f, 2f, 3f);
            VfxHandle handle = player.Play(Spark, position);

            Assert.IsTrue(handle.IsValid);
            Assert.IsFalse(handle.IsLooping);
            Assert.AreEqual(Spark, handle.EffectId);
            GameObject instance = handle.GameObject;
            Assert.IsTrue(instance.activeInHierarchy);
            Assert.IsNull(instance.transform.parent);
            Assert.AreEqual(position, instance.transform.position);
            Assert.AreEqual(1, player.ActiveCount);
            Assert.AreEqual(0, player.PooledCount(Spark));

            yield return null;
            Assert.IsTrue(handle.IsValid, "still playing one frame later");

            yield return WaitSeconds(SparkSeconds);

            Assert.IsFalse(handle.IsValid);
            Assert.IsNull(handle.GameObject);
            Assert.IsFalse(instance.activeInHierarchy, "a pooled instance is disabled");
            Assert.AreEqual(0, player.ActiveCount);
            Assert.AreEqual(1, player.PooledCount(Spark));
            Assert.AreEqual(1, player.LiveCount(Spark));
        }

        [UnityTest]
        public IEnumerator Play_ReusesPooledInstance_AndOldHandleStaysInvalid()
        {
            VfxHandle first = player.Play(Spark, Vector3.zero);
            GameObject instance = first.GameObject;
            yield return WaitSeconds(SparkSeconds);
            Assert.AreEqual(1, player.PooledCount(Spark));

            VfxHandle second = player.Play(Spark, Vector3.one);

            Assert.AreSame(instance, second.GameObject, "the pooled instance is reused");
            Assert.AreEqual(1, player.CreatedTotal(Spark));
            Assert.AreEqual(0, player.PooledCount(Spark));
            Assert.IsFalse(first.IsValid, "a handle does not revive when its instance is reused");
            Assert.AreNotEqual(first, second);
            Assert.IsFalse(player.Stop(first, true));
            Assert.IsTrue(second.IsValid);
        }

        [UnityTest]
        public IEnumerator Pool_IsPerEffectId()
        {
            VfxHandle spark = player.Play(Spark, Vector3.zero);
            VfxHandle aura = player.Play(Aura, Vector3.zero);
            Assert.AreNotSame(spark.GameObject, aura.GameObject);

            player.StopAll(true);
            yield return null;

            Assert.AreEqual(1, player.PooledCount(Spark));
            Assert.AreEqual(1, player.PooledCount(Aura));
            Assert.AreEqual(1, player.CreatedTotal(Spark));
            Assert.AreEqual(1, player.CreatedTotal(Aura));
        }

        [UnityTest]
        public IEnumerator Looped_PlaysPastDuration_UntilStopped()
        {
            VfxHandle handle = player.Play(Aura, Vector3.zero, loop: true);
            Assert.IsTrue(handle.IsLooping);

            yield return WaitSeconds(AuraTailSeconds * 3f);
            Assert.IsTrue(handle.IsValid, "a looped instance ignores the duration while playing");

            Assert.IsTrue(handle.Stop());
            Assert.IsTrue(handle.IsValid, "a non-immediate stop keeps the instance for the tail");

            yield return WaitSeconds(AuraTailSeconds);

            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(1, player.PooledCount(Aura));
            Assert.IsFalse(handle.Stop(), "stopping an invalid handle reports false");
        }

        [Test]
        public void StopImmediate_ReturnsToPoolAtOnce()
        {
            VfxHandle handle = player.Play(Aura, Vector3.zero, loop: true);

            Assert.IsTrue(player.Stop(handle, immediate: true));

            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(0, player.ActiveCount);
            Assert.AreEqual(1, player.PooledCount(Aura));
        }

        [Test]
        public void PlayOnSocket_ParentsInstanceAtSocketOrigin()
        {
            Transform character = objects.GameObject("Character").transform;
            character.position = new Vector3(5f, 0f, 0f);
            GameObject handObject = objects.GameObject("Hand", character);
            handObject.transform.localPosition = new Vector3(0.3f, 1.2f, 0f);
            var hand = handObject.AddComponent<VfxSocket>();
            hand.SocketName = VfxSocket.HandRight;

            VfxHandle handle = player.Play(Spark, VfxSocket.Find(character, VfxSocket.HandRight));

            Transform instance = handle.GameObject.transform;
            Assert.AreSame(handObject.transform, instance.parent);
            Assert.AreEqual(Vector3.zero, instance.localPosition);
            Assert.AreEqual(handObject.transform.position, instance.position);
        }

        [UnityTest]
        public IEnumerator SocketDestroyed_InstanceIsForgotten()
        {
            GameObject character = objects.GameObject("Character");
            var socket = character.AddComponent<VfxSocket>();
            VfxHandle handle = player.Play(Aura, socket, loop: true);
            Assert.AreEqual(1, player.LiveCount(Aura));

            Object.Destroy(character);
            yield return null;
            yield return null;

            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(0, player.ActiveCount);
            Assert.AreEqual(0, player.LiveCount(Aura));
            Assert.AreEqual(0, player.PooledCount(Aura));
        }

        [Test]
        public void ReleasedInstancesAboveTheCap_AreDestroyed()
        {
            player.MaxPooledPerEffect = 2;
            for (int i = 0; i < 3; i++) player.Play(Spark, Vector3.zero);
            Assert.AreEqual(3, player.LiveCount(Spark));

            player.StopAll(true);

            Assert.AreEqual(2, player.PooledCount(Spark));
            Assert.AreEqual(2, player.LiveCount(Spark));
        }

        [Test]
        public void Prewarm_FillsPoolWithoutPlaying()
        {
            LogAssert.Expect(LogType.Error, new Regex("unknown effect id 'unknown'"));

            Assert.AreEqual(3, player.Prewarm(Spark, 3));
            Assert.AreEqual(3, player.PooledCount(Spark));
            Assert.AreEqual(0, player.ActiveCount);
            Assert.AreEqual(-1, player.Prewarm("unknown", 1));
        }

        [Test]
        public void UnknownId_ReturnsNoneAndLogsError()
        {
            LogAssert.Expect(LogType.Error, new Regex("unknown effect id 'nope'"));

            VfxHandle handle = player.Play("nope", Vector3.zero);

            Assert.AreEqual(VfxHandle.None, handle);
            Assert.IsFalse(handle.IsValid);
            Assert.AreEqual(0, player.ActiveCount);
        }

        [Test]
        public void WarmPool_PlayAndReturn_DoNotAllocate()
        {
            player.Prewarm(Spark, 2);
            var position = new Vector3(1f, 0f, 0f);
            TestDelegate playAndReturn = () =>
            {
                VfxHandle handle = player.Play(Spark, position);
                player.Stop(handle, true);
            };
            // Run the delegate once first: Mono resolves a string literal on the first execution of
            // the method that loads it, which the recorder counts as GC.Alloc (see AUTHORING.md).
            playAndReturn();

            Assert.That(playAndReturn, AllocIs.Not.AllocatingGCMemory());
        }

        [Test]
        public void WarmPool_PlayOnSocketAndReturn_DoNotAllocate()
        {
            var socket = objects.GameObject("Socket").AddComponent<VfxSocket>();
            player.Prewarm(Aura, 1);
            TestDelegate playAndReturn = () =>
            {
                VfxHandle handle = player.Play(Aura, socket, loop: true);
                handle.Stop(immediate: true);
            };
            playAndReturn();

            Assert.That(playAndReturn, AllocIs.Not.AllocatingGCMemory());
        }
    }
}
