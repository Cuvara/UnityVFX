using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Cuvara.VFX
{
    /// <summary>
    /// Maps an effect id to the prefab that plays it, with the effect's duration and tags.
    /// </summary>
    /// <remarks>
    /// Lookup is by ordinal, case-sensitive id. When two entries share an id the first one wins;
    /// <see cref="Validate(List{string})"/> reports duplicates, empty ids, missing prefabs and prefabs
    /// without a <see cref="VisualEffect"/>, so an editor check or a test can fail on them.
    /// </remarks>
    [CreateAssetMenu(fileName = "VfxLibrary", menuName = "Cuvara/VFX/Library", order = 0)]
    public sealed class VfxLibrary : ScriptableObject
    {
        /// <summary>One effect of the library.</summary>
        [Serializable]
        public sealed class Entry
        {
            [SerializeField, Tooltip("Stable effect id, for example chain-lightning.")]
            string id;

            [SerializeField, Tooltip("Prefab with at least one VisualEffect component.")]
            GameObject prefab;

            [SerializeField, Min(0f), Tooltip("Seconds until a one-shot instance returns to the pool; for a looped instance, the tail after Stop.")]
            float durationSeconds = 1f;

            [SerializeField, Tooltip("Free-form tags, for example lightning or impact.")]
            string[] tags = Array.Empty<string>();

            /// <summary>Creates an empty entry (used by Unity serialization).</summary>
            public Entry()
            {
            }

            /// <summary>Creates an entry.</summary>
            /// <param name="id">Effect id.</param>
            /// <param name="prefab">Prefab with a <see cref="VisualEffect"/>.</param>
            /// <param name="durationSeconds">Seconds a one-shot plays before it returns to the pool.</param>
            /// <param name="tags">Optional tags.</param>
            public Entry(string id, GameObject prefab, float durationSeconds, params string[] tags)
            {
                this.id = id;
                this.prefab = prefab;
                this.durationSeconds = durationSeconds;
                this.tags = tags ?? Array.Empty<string>();
            }

            /// <summary>Effect id.</summary>
            public string Id => id;

            /// <summary>Prefab instantiated by <see cref="VfxPlayer"/>.</summary>
            public GameObject Prefab => prefab;

            /// <summary>
            /// Seconds a one-shot instance plays before it returns to the pool. For a looped instance it
            /// is the tail between <see cref="VfxPlayer.Stop(VfxHandle, bool)"/> and the return.
            /// </summary>
            public float DurationSeconds => durationSeconds;

            /// <summary>Tags of the effect (never null).</summary>
            public IReadOnlyList<string> Tags => tags ?? Array.Empty<string>();

            /// <summary>True when <paramref name="tag"/> is one of <see cref="Tags"/> (ordinal).</summary>
            public bool HasTag(string tag)
            {
                if (tags == null) return false;
                for (int i = 0; i < tags.Length; i++)
                    if (string.Equals(tags[i], tag, StringComparison.Ordinal)) return true;
                return false;
            }
        }

        [SerializeField]
        List<Entry> entries = new List<Entry>();

        Dictionary<string, Entry> lookup;

        /// <summary>All entries in serialized order, duplicates included.</summary>
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>Replaces all entries (editor tooling and tests) and rebuilds the lookup.</summary>
        public void SetEntries(IEnumerable<Entry> newEntries)
        {
            entries = newEntries == null ? new List<Entry>() : new List<Entry>(newEntries);
            lookup = null;
        }

        /// <summary>Finds the entry of <paramref name="id"/>. Does not allocate once the lookup is built.</summary>
        /// <returns>False when the id is null, empty or unknown.</returns>
        public bool TryGet(string id, out Entry entry)
        {
            entry = null;
            if (string.IsNullOrEmpty(id)) return false;
            if (lookup == null) BuildLookup();
            return lookup.TryGetValue(id, out entry);
        }

        /// <summary>True when the library has an entry for <paramref name="id"/>.</summary>
        public bool Contains(string id) => TryGet(id, out _);

        /// <summary>
        /// Checks the library and appends one message per problem to <paramref name="problems"/>:
        /// an empty id, a duplicate id, a missing prefab, a prefab without a <see cref="VisualEffect"/>
        /// and a negative duration.
        /// </summary>
        /// <returns>The number of problems found by this call.</returns>
        public int Validate(List<string> problems)
        {
            if (problems == null) throw new ArgumentNullException(nameof(problems));
            int before = problems.Count;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null)
                {
                    problems.Add($"entry {i}: null entry");
                    continue;
                }
                if (string.IsNullOrWhiteSpace(entry.Id))
                    problems.Add($"entry {i}: empty id");
                else if (!seen.Add(entry.Id))
                    problems.Add($"entry {i}: duplicate id '{entry.Id}' (the first entry with this id wins)");
                string label = string.IsNullOrWhiteSpace(entry.Id) ? $"entry {i}" : $"entry {i} '{entry.Id}'";
                if (entry.Prefab == null)
                    problems.Add($"{label}: no prefab");
                else if (entry.Prefab.GetComponentInChildren<VisualEffect>(true) == null)
                    problems.Add($"{label}: prefab '{entry.Prefab.name}' has no VisualEffect");
                if (entry.DurationSeconds < 0f)
                    problems.Add($"{label}: negative duration {entry.DurationSeconds}");
            }
            return problems.Count - before;
        }

        /// <summary>Convenience overload of <see cref="Validate(List{string})"/>.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            Validate(problems);
            return problems;
        }

        void OnEnable() => lookup = null;

        void OnValidate() => lookup = null;

        void BuildLookup()
        {
            lookup = new Dictionary<string, Entry>(entries.Count, StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.Id) || lookup.ContainsKey(entry.Id)) continue;
                lookup.Add(entry.Id, entry);
            }
        }
    }
}
