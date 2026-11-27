using UnityEngine;

namespace Cuvara.VFX.Samples.Showcase
{
    /// <summary>
    /// A character place of the Showcase scene: in play mode it instantiates its character prefab as a
    /// child, or the code-built placeholder <see cref="VfxMannequin"/> when the prefab reference is
    /// empty or its asset is missing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scene never holds a character model itself, so it opens and plays when
    /// <c>Samples~/Showcase/Characters</c> holds no model (the third-party characters may leave the
    /// repository). Stations point at the spawn point, not at the character: <see cref="VfxStation"/>
    /// resolves its sockets under the spawn point at every play, so the sockets of the spawned
    /// character (or of the placeholder, which has the same five) are found either way.
    /// </para>
    /// <para>
    /// The character's feet stand at the spawn point and it faces the spawn point's +Z. The fallback
    /// logs one information line (not a warning or an error). In edit mode nothing is spawned; the
    /// Scene view shows a wire box of the character place.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cuvara/VFX/Showcase/Showcase Character Spawn")]
    public sealed class ShowcaseCharacterSpawn : MonoBehaviour
    {
        [SerializeField, Tooltip("Character prefab with VfxSockets; empty or missing: the placeholder mannequin.")]
        GameObject prefab;

        GameObject instance;
        bool isPlaceholder;

        /// <summary>The character prefab; null places the placeholder mannequin.</summary>
        public GameObject Prefab
        {
            get => prefab;
            set => prefab = value;
        }

        /// <summary>The spawned character (or placeholder), or null before <see cref="Spawn"/>.</summary>
        public GameObject Instance => instance;

        /// <summary>True when the spawned object is the placeholder mannequin.</summary>
        public bool IsPlaceholder => isPlaceholder;

        /// <summary>
        /// Spawns the character once: an instance of <see cref="Prefab"/>, or the placeholder
        /// mannequin when there is no prefab. A second call returns the same object.
        /// </summary>
        public GameObject Spawn()
        {
            if (instance != null) return instance;
            if (prefab != null)
            {
                instance = Instantiate(prefab, transform, false);
                instance.name = prefab.name;
                isPlaceholder = false;
            }
            else
            {
                instance = VfxMannequin.Create(transform, "Placeholder");
                isPlaceholder = true;
                Debug.Log($"Showcase: '{name}' has no character prefab (empty reference or no model in Samples~/Showcase/Characters); placed the placeholder mannequin with the five sockets", this);
            }
            return instance;
        }

        void Awake() => Spawn();

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 1.8f, 0.3f));
            Gizmos.DrawLine(new Vector3(0f, 1.6f, 0f), new Vector3(0f, 1.6f, 0.5f));
        }
    }
}
