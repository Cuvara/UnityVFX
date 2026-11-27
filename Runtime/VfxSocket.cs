using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cuvara.VFX
{
    /// <summary>
    /// A named attach point on a character, for example the right hand that casts a spell.
    /// <see cref="VfxPlayer"/> parents an effect to the socket's transform.
    /// </summary>
    /// <remarks>
    /// Use the well-known names (<see cref="Head"/>, <see cref="Chest"/>, <see cref="HandLeft"/>,
    /// <see cref="HandRight"/>, <see cref="Feet"/>) so effects and characters agree without a mapping.
    /// Other names are allowed. Names compare ordinal and case-sensitive.
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cuvara/VFX/VFX Socket")]
    public sealed class VfxSocket : MonoBehaviour
    {
        /// <summary>Socket at the head.</summary>
        public const string Head = "head";

        /// <summary>Socket at the centre of the chest.</summary>
        public const string Chest = "chest";

        /// <summary>Socket at the left hand.</summary>
        public const string HandLeft = "hand_l";

        /// <summary>Socket at the right hand.</summary>
        public const string HandRight = "hand_r";

        /// <summary>Socket on the ground between the feet.</summary>
        public const string Feet = "feet";

        static readonly string[] wellKnownNames = { Head, Chest, HandLeft, HandRight, Feet };

        // Reused by Find so a lookup does not allocate once the list has grown. Main thread only.
        static readonly List<VfxSocket> searchBuffer = new List<VfxSocket>(8);

        [SerializeField, Tooltip("Socket name; prefer head, chest, hand_l, hand_r or feet.")]
        string socketName = Chest;

        /// <summary>The well-known socket names, in the order head, chest, hand_l, hand_r, feet.</summary>
        public static IReadOnlyList<string> WellKnownNames => wellKnownNames;

        /// <summary>The socket's name.</summary>
        public string SocketName
        {
            get => socketName;
            set => socketName = value;
        }

        /// <summary>True when <paramref name="name"/> is one of <see cref="WellKnownNames"/>.</summary>
        public static bool IsWellKnown(string name) => Array.IndexOf(wellKnownNames, name) >= 0;

        /// <summary>
        /// Finds the first socket named <paramref name="name"/> on <paramref name="root"/> or any of its
        /// descendants, inactive ones included, in hierarchy order.
        /// </summary>
        /// <returns>The socket, or null when <paramref name="root"/> is null or no socket has that name.</returns>
        public static VfxSocket Find(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            searchBuffer.Clear();
            root.GetComponentsInChildren(true, searchBuffer);
            VfxSocket found = null;
            for (int i = 0; i < searchBuffer.Count; i++)
            {
                if (string.Equals(searchBuffer[i].socketName, name, StringComparison.Ordinal))
                {
                    found = searchBuffer[i];
                    break;
                }
            }
            searchBuffer.Clear();
            return found;
        }

        /// <summary>
        /// Like <see cref="Find(Transform, string)"/>, but falls back to <paramref name="root"/> itself
        /// when no socket has that name.
        /// </summary>
        public static Transform FindOrRoot(Transform root, string name)
        {
            VfxSocket socket = Find(root, name);
            return socket != null ? socket.transform : root;
        }
    }
}
