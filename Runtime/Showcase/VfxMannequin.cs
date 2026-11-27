using UnityEngine;

namespace Cuvara.VFX
{
    /// <summary>
    /// A placeholder test character made from code: a clearly artificial capsule mannequin about
    /// <see cref="Height"/> metres tall with the five well-known <see cref="VfxSocket"/>s, for the
    /// Showcase scene and the capture stage when no character model is available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Create(Transform, string)"/> builds it from Unity primitives: two legs, a torso, two
    /// arms, a head and a visor that marks the front. The feet stand at the root's origin (y = 0), the
    /// mannequin faces the root's +Z, its right hand is on +X. No asset is used, no collider is kept,
    /// and one material (URP Lit, neutral grey) is made for all parts.
    /// </para>
    /// <para>
    /// The material belongs to this component and is destroyed with it, so destroying the root
    /// destroys every part and leaks nothing. Socket heights: head 1.69, chest 1.35 (on the front of
    /// the chest), hands 0.82, feet 0 (the ground between the feet).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class VfxMannequin : MonoBehaviour
    {
        /// <summary>Height of the mannequin, top of the head, in metres.</summary>
        public const float Height = 1.8f;

        /// <summary>
        /// Albedo colour set on the mannequin's material. Like every material colour property, Unity
        /// converts it from sRGB to linear in a project in linear colour space.
        /// </summary>
        public static readonly Color Albedo = new Color(0.55f, 0.53f, 0.50f);

        Material material;

        /// <summary>The material shared by all parts; destroyed with this component.</summary>
        public Material Material => material;

        /// <summary>
        /// Creates a mannequin named <paramref name="name"/> under <paramref name="parent"/> (or at the
        /// scene root), feet at the parent's origin, facing the parent's +Z.
        /// </summary>
        /// <returns>The root object; destroy it to remove the mannequin and its material.</returns>
        public static GameObject Create(Transform parent = null, string name = "VfxMannequin")
        {
            var root = new GameObject(string.IsNullOrEmpty(name) ? "VfxMannequin" : name);
            if (parent != null) root.transform.SetParent(parent, false);
            var mannequin = root.AddComponent<VfxMannequin>();
            mannequin.material = CreateMaterial();
            Material shared = mannequin.material;

            Part(root.transform, PrimitiveType.Capsule, "Leg_L", new Vector3(-0.10f, 0.45f, 0f), new Vector3(0.17f, 0.45f, 0.17f), shared);
            Part(root.transform, PrimitiveType.Capsule, "Leg_R", new Vector3(0.10f, 0.45f, 0f), new Vector3(0.17f, 0.45f, 0.17f), shared);
            Part(root.transform, PrimitiveType.Capsule, "Torso", new Vector3(0f, 1.22f, 0f), new Vector3(0.38f, 0.33f, 0.24f), shared);
            Part(root.transform, PrimitiveType.Capsule, "Arm_L", new Vector3(-0.27f, 1.17f, 0f), new Vector3(0.10f, 0.33f, 0.10f), shared);
            Part(root.transform, PrimitiveType.Capsule, "Arm_R", new Vector3(0.27f, 1.17f, 0f), new Vector3(0.10f, 0.33f, 0.10f), shared);
            Part(root.transform, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.69f, 0f), new Vector3(0.22f, 0.22f, 0.22f), shared);
            Part(root.transform, PrimitiveType.Cube, "Visor", new Vector3(0f, 1.71f, 0.10f), new Vector3(0.16f, 0.05f, 0.05f), shared);

            Socket(root.transform, VfxSocket.Head, new Vector3(0f, 1.69f, 0f));
            Socket(root.transform, VfxSocket.Chest, new Vector3(0f, 1.35f, 0.12f));
            Socket(root.transform, VfxSocket.HandLeft, new Vector3(-0.27f, 0.82f, 0f));
            Socket(root.transform, VfxSocket.HandRight, new Vector3(0.27f, 0.82f, 0f));
            Socket(root.transform, VfxSocket.Feet, Vector3.zero);
            return root;
        }

        static Material CreateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var created = new Material(shader) { name = "VfxMannequin", hideFlags = HideFlags.DontSave };
            if (created.HasProperty("_BaseColor")) created.SetColor("_BaseColor", Albedo);
            if (created.HasProperty("_Color")) created.SetColor("_Color", Albedo);
            if (created.HasProperty("_Smoothness")) created.SetFloat("_Smoothness", 0.3f);
            return created;
        }

        static void Part(Transform root, PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            // Removed at once (not at the end of the frame), so the mannequin never has a collider.
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) DestroyImmediate(collider);
            part.transform.SetParent(root, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static void Socket(Transform root, string socketName, Vector3 position)
        {
            var go = new GameObject("VfxSocket_" + socketName);
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            go.AddComponent<VfxSocket>().SocketName = socketName;
        }

        void OnDestroy()
        {
            if (material == null) return;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
            material = null;
        }
    }
}
