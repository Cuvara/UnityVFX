using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace Cuvara.VFX.Tests
{
    /// <summary>Creates test objects and destroys them all in <see cref="DestroyAll"/>.</summary>
    sealed class TestObjects
    {
        readonly List<Object> created = new List<Object>();

        public GameObject GameObject(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            created.Add(go);
            return go;
        }

        /// <summary>An inactive template with a VisualEffect (no asset), used as an effect prefab.</summary>
        public GameObject EffectPrefab(string name)
        {
            GameObject go = GameObject(name);
            go.SetActive(false);
            go.AddComponent<VisualEffect>();
            return go;
        }

        public VfxLibrary Library(params VfxLibrary.Entry[] entries)
        {
            var library = ScriptableObject.CreateInstance<VfxLibrary>();
            library.name = "TestLibrary";
            library.SetEntries(entries);
            created.Add(library);
            return library;
        }

        public void DestroyAll()
        {
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
        }
    }
}
