using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Cuvara.VFX.Editor
{
    /// <summary>
    /// Finds every <see cref="IVfxVerb"/> in the loaded assemblies, keyed by <see cref="IVfxVerb.Name"/>.
    /// </summary>
    /// <remarks>
    /// Built once per domain. Verbs are ordered by name (ordinal). When two types claim the same name,
    /// the one whose full type name sorts first wins and <see cref="Problems"/> names both.
    /// </remarks>
    public static class VfxVerbRegistry
    {
        static SortedDictionary<string, IVfxVerb> s_Verbs;
        static List<string> s_Problems;

        /// <summary>The verbs, by name.</summary>
        public static IReadOnlyDictionary<string, IVfxVerb> Verbs
        {
            get
            {
                EnsureLoaded();
                return s_Verbs;
            }
        }

        /// <summary>Registration problems (duplicate names, constructors that threw).</summary>
        public static IReadOnlyList<string> Problems
        {
            get
            {
                EnsureLoaded();
                return s_Problems;
            }
        }

        /// <summary>The verb names, ordered.</summary>
        public static IReadOnlyList<string> Names => Verbs.Keys.ToList();

        /// <summary>The verb with that name, or null.</summary>
        public static IVfxVerb Find(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            return Verbs.TryGetValue(name, out var verb) ? verb : null;
        }

        static void EnsureLoaded()
        {
            if (s_Verbs != null)
                return;

            var verbs = new SortedDictionary<string, IVfxVerb>(StringComparer.Ordinal);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            var problems = new List<string>();
            var types = TypeCache.GetTypesDerivedFrom<IVfxVerb>()
                .Where(t => !t.IsAbstract && !t.IsInterface && !t.ContainsGenericParameters && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal);

            foreach (var type in types)
            {
                IVfxVerb verb;
                try
                {
                    verb = (IVfxVerb)Activator.CreateInstance(type);
                }
                catch (Exception exception)
                {
                    problems.Add($"verb type {type.FullName} could not be created: {exception.Message}");
                    continue;
                }

                if (string.IsNullOrEmpty(verb.Name))
                {
                    problems.Add($"verb type {type.FullName} has an empty name");
                    continue;
                }

                if (verbs.ContainsKey(verb.Name))
                {
                    problems.Add($"verb '{verb.Name}' is claimed by {owners[verb.Name]} and {type.FullName}; {owners[verb.Name]} is used");
                    continue;
                }

                verbs.Add(verb.Name, verb);
                owners.Add(verb.Name, type.FullName);
            }

            s_Problems = problems;
            s_Verbs = verbs;
        }
    }
}
