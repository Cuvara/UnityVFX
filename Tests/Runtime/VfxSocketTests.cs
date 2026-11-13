using NUnit.Framework;
using UnityEngine;

namespace Cuvara.VFX.Tests
{
    public sealed class VfxSocketTests
    {
        TestObjects objects;

        [SetUp]
        public void SetUp() => objects = new TestObjects();

        [TearDown]
        public void TearDown() => objects.DestroyAll();

        VfxSocket AddSocket(Transform parent, string name)
        {
            GameObject go = objects.GameObject("Socket_" + name, parent);
            var socket = go.AddComponent<VfxSocket>();
            socket.SocketName = name;
            return socket;
        }

        [Test]
        public void WellKnownNames_HaveTheContractValues()
        {
            Assert.AreEqual("head", VfxSocket.Head);
            Assert.AreEqual("chest", VfxSocket.Chest);
            Assert.AreEqual("hand_l", VfxSocket.HandLeft);
            Assert.AreEqual("hand_r", VfxSocket.HandRight);
            Assert.AreEqual("feet", VfxSocket.Feet);
            CollectionAssert.AreEqual(new[] { "head", "chest", "hand_l", "hand_r", "feet" }, VfxSocket.WellKnownNames);
            Assert.IsTrue(VfxSocket.IsWellKnown("hand_r"));
            Assert.IsFalse(VfxSocket.IsWellKnown("Hand_R"));
            Assert.IsFalse(VfxSocket.IsWellKnown("tail"));
        }

        [Test]
        public void Find_ReturnsNestedSocketByName_IncludingInactive()
        {
            Transform root = objects.GameObject("Character").transform;
            Transform spine = objects.GameObject("Spine", root).transform;
            Transform arm = objects.GameObject("ArmR", spine).transform;
            AddSocket(spine, VfxSocket.Chest);
            VfxSocket hand = AddSocket(arm, VfxSocket.HandRight);
            arm.gameObject.SetActive(false);

            Assert.AreSame(hand, VfxSocket.Find(root, VfxSocket.HandRight));
        }

        [Test]
        public void Find_UnknownNameOrNullRoot_ReturnsNull()
        {
            Transform root = objects.GameObject("Character").transform;
            AddSocket(root, VfxSocket.Head);

            Assert.IsNull(VfxSocket.Find(root, VfxSocket.Feet));
            Assert.IsNull(VfxSocket.Find(root, "Head"));
            Assert.IsNull(VfxSocket.Find(null, VfxSocket.Head));
            Assert.IsNull(VfxSocket.Find(root, null));
        }

        [Test]
        public void Find_DoesNotSearchOtherHierarchies()
        {
            Transform a = objects.GameObject("A").transform;
            Transform b = objects.GameObject("B").transform;
            AddSocket(b, VfxSocket.Head);

            Assert.IsNull(VfxSocket.Find(a, VfxSocket.Head));
        }

        [Test]
        public void FindOrRoot_FallsBackToRoot()
        {
            Transform root = objects.GameObject("Character").transform;
            VfxSocket feet = AddSocket(root, VfxSocket.Feet);

            Assert.AreSame(feet.transform, VfxSocket.FindOrRoot(root, VfxSocket.Feet));
            Assert.AreSame(root, VfxSocket.FindOrRoot(root, VfxSocket.Head));
        }
    }
}
