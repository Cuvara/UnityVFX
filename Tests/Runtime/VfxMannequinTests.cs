using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cuvara.VFX.Tests
{
    /// <summary>
    /// <see cref="VfxMannequin.Create"/>: the code-built placeholder character has the five well-known
    /// sockets, is about 1.8 m tall with its feet at y = 0 and faces +Z, has no collider, and destroying
    /// its root destroys its parts and its material.
    /// </summary>
    public sealed class VfxMannequinTests
    {
        GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        static Bounds RendererBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        [Test]
        public void Create_HasTheFiveWellKnownSockets_AtPlausibleHeights()
        {
            root = VfxMannequin.Create();
            Assert.AreEqual(VfxSocket.WellKnownNames.Count, root.GetComponentsInChildren<VfxSocket>(true).Length);
            foreach (string name in VfxSocket.WellKnownNames)
                Assert.IsNotNull(VfxSocket.Find(root.transform, name), name);

            float Height(string name) => VfxSocket.Find(root.transform, name).transform.position.y;
            Assert.That(Height(VfxSocket.Head), Is.InRange(1.55f, 1.8f));
            Assert.That(Height(VfxSocket.Chest), Is.InRange(1.2f, 1.5f));
            Assert.That(Height(VfxSocket.HandLeft), Is.InRange(0.7f, 1.0f));
            Assert.That(Height(VfxSocket.HandRight), Is.InRange(0.7f, 1.0f));
            Assert.AreEqual(0f, Height(VfxSocket.Feet), 1e-6f);
            Assert.That(VfxSocket.Find(root.transform, VfxSocket.HandRight).transform.position.x, Is.GreaterThan(0f), "facing +Z, the right hand is on +X");
            Assert.That(VfxSocket.Find(root.transform, VfxSocket.Chest).transform.position.z, Is.GreaterThan(0f), "the chest socket is on the front");
        }

        [Test]
        public void Create_IsAbout1Point8MetresTall_WithItsFeetOnTheGround_UnderItsParent()
        {
            var parent = new GameObject("Parent");
            try
            {
                parent.transform.position = new Vector3(2f, 0.5f, -3f);
                root = VfxMannequin.Create(parent.transform, "Dummy");
                Assert.AreEqual("Dummy", root.name);
                Assert.AreEqual(parent.transform, root.transform.parent);
                Bounds bounds = RendererBounds(root);
                Assert.AreEqual(0.5f, bounds.min.y, 0.01f, "feet at the parent's origin");
                Assert.AreEqual(0.5f + VfxMannequin.Height, bounds.max.y, 0.02f, "top of the head");
                Assert.AreEqual(2f, bounds.center.x, 0.01f);
                Transform visor = root.transform.Find("Visor");
                Assert.That(visor.localPosition.z, Is.GreaterThan(0f), "the visor marks the front, +Z");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Create_HasNoCollider_AndOneSharedLitMaterial()
        {
            root = VfxMannequin.Create();
            Assert.AreEqual(0, root.GetComponentsInChildren<Collider>(true).Length);
            var mannequin = root.GetComponent<VfxMannequin>();
            Assert.IsNotNull(mannequin.Material);
            Assert.AreEqual("Universal Render Pipeline/Lit", mannequin.Material.shader.name);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                Assert.AreSame(mannequin.Material, renderer.sharedMaterial, renderer.name);
        }

        [UnityTest]
        public IEnumerator DestroyingTheRoot_DestroysItsPartsAndItsMaterial()
        {
            root = VfxMannequin.Create();
            Material material = root.GetComponent<VfxMannequin>().Material;
            Transform head = root.transform.Find("Head");
            int materialsBefore = Resources.FindObjectsOfTypeAll<Material>().Length;

            Object.Destroy(root);
            yield return null;

            Assert.IsTrue(root == null, "root destroyed");
            Assert.IsTrue(head == null, "parts destroyed with the root");
            Assert.IsTrue(material == null, "the material is destroyed with the mannequin");
            Assert.AreEqual(materialsBefore - 1, Resources.FindObjectsOfTypeAll<Material>().Length, "exactly the mannequin's material is gone");
        }
    }
}
