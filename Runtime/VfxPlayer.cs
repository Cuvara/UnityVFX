using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Cuvara.VFX
{
    /// <summary>
    /// Plays effects of a <see cref="VfxLibrary"/> by id, at a world position or on a
    /// <see cref="VfxSocket"/>, one-shot or looped, from a pool per effect id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A one-shot instance returns to its pool <see cref="VfxLibrary.Entry.DurationSeconds"/> after the
    /// play. A looped instance plays until <see cref="Stop(VfxHandle, bool)"/>: a non-immediate stop
    /// sends the Stop event to every <see cref="VisualEffect"/> of the instance (spawners stop, alive
    /// particles finish) and returns it after the same duration; an immediate stop returns it at once.
    /// </para>
    /// <para>
    /// Pooled instances are children of an inactive object under this player, so they are disabled
    /// without a <c>SetActive</c> call and a fresh instance does not start playing on instantiation.
    /// A play moves the instance out of that object, which enables its <see cref="VisualEffect"/>
    /// components; a <see cref="VisualEffect"/> that becomes enabled starts again and sends its initial
    /// event (<c>OnPlay</c> by default), so a reused instance plays from the start. Any prefab with a <see cref="VisualEffect"/>
    /// works; the prefab needs no script of this package.
    /// </para>
    /// <para>
    /// Once a pool holds an instance (see <see cref="Prewarm(string, int)"/>), a play and the return to
    /// the pool do not allocate managed memory. Main thread only.
    /// </para>
    /// <para>
    /// An instance attached to a socket is destroyed with the socket's object; the player notices it
    /// on its next update and forgets the instance.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cuvara/VFX/VFX Player")]
    public sealed class VfxPlayer : MonoBehaviour
    {
        [SerializeField, Tooltip("Effects this player can play.")]
        VfxLibrary library;

        [SerializeField, Min(0), Tooltip("Free instances kept per effect id; a returned instance above this count is destroyed.")]
        int maxPooledPerEffect = 16;

        [SerializeField, Tooltip("Time one-shot durations with unscaled time (for example while the game is paused).")]
        bool useUnscaledTime;

        readonly Dictionary<string, VfxPool> pools = new Dictionary<string, VfxPool>(StringComparer.Ordinal);
        readonly List<VfxInstance> active = new List<VfxInstance>(32);
        Transform poolRoot;

        /// <summary>
        /// The library effects are looked up in. Setting another library stops every instance at once
        /// and destroys the pools.
        /// </summary>
        public VfxLibrary Library
        {
            get => library;
            set
            {
                if (library == value) return;
                StopAll(true);
                ClearPools();
                library = value;
            }
        }

        /// <summary>Free instances kept per effect id; a returned instance above this count is destroyed.</summary>
        public int MaxPooledPerEffect
        {
            get => maxPooledPerEffect;
            set => maxPooledPerEffect = Mathf.Max(0, value);
        }

        /// <summary>Time one-shot durations with unscaled time.</summary>
        public bool UseUnscaledTime
        {
            get => useUnscaledTime;
            set => useUnscaledTime = value;
        }

        /// <summary>Number of instances that play now (fading ones included).</summary>
        public int ActiveCount => active.Count;

        float Now => useUnscaledTime ? Time.unscaledTime : Time.time;

        /// <summary>Plays <paramref name="effectId"/> at a world position with no rotation.</summary>
        public VfxHandle Play(string effectId, Vector3 position, bool loop = false) =>
            Play(effectId, position, Quaternion.identity, loop);

        /// <summary>Plays <paramref name="effectId"/> at a world position and rotation, not parented.</summary>
        /// <returns>The handle, or <see cref="VfxHandle.None"/> (with a logged error) when the id is unknown.</returns>
        public VfxHandle Play(string effectId, Vector3 position, Quaternion rotation, bool loop = false)
        {
            VfxInstance instance = Acquire(effectId);
            if (instance == null) return VfxHandle.None;
            Transform t = instance.Transform;
            t.SetPositionAndRotation(position, rotation);
            t.SetParent(null, true);
            return Activate(instance, loop);
        }

        /// <summary>Plays <paramref name="effectId"/> attached to <paramref name="socket"/> (local origin, no local rotation).</summary>
        /// <returns>The handle, or <see cref="VfxHandle.None"/> (with a logged error) when the id is unknown or the socket is null.</returns>
        public VfxHandle Play(string effectId, VfxSocket socket, bool loop = false)
        {
            if (socket == null)
            {
                Debug.LogError($"VfxPlayer: cannot play '{effectId}' on a null socket", this);
                return VfxHandle.None;
            }
            return Play(effectId, socket.transform, loop);
        }

        /// <summary>Plays <paramref name="effectId"/> attached to any transform (local origin, no local rotation).</summary>
        /// <returns>The handle, or <see cref="VfxHandle.None"/> (with a logged error) when the id is unknown or the parent is null.</returns>
        public VfxHandle Play(string effectId, Transform parent, bool loop = false)
        {
            if (parent == null)
            {
                Debug.LogError($"VfxPlayer: cannot play '{effectId}' on a null parent", this);
                return VfxHandle.None;
            }
            VfxInstance instance = Acquire(effectId);
            if (instance == null) return VfxHandle.None;
            Transform t = instance.Transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.SetParent(parent, false);
            return Activate(instance, loop);
        }

        /// <summary>
        /// Stops the instance of <paramref name="handle"/>. Non-immediate: sends the Stop event to its
        /// <see cref="VisualEffect"/> components and returns it to the pool after the library duration.
        /// Immediate: returns it to the pool now (alive particles vanish).
        /// </summary>
        /// <returns>False when the handle is invalid or belongs to another player.</returns>
        public bool Stop(VfxHandle handle, bool immediate = false)
        {
            if (!handle.IsValid || handle.Instance.Owner != this) return false;
            VfxInstance instance = handle.Instance;
            if (immediate)
            {
                Release(instance);
                return true;
            }
            if (instance.Stopping) return true;
            instance.Stopping = true;
            VisualEffect[] effects = instance.Effects;
            for (int i = 0; i < effects.Length; i++)
                if (effects[i] != null) effects[i].Stop();
            float end = Now + Mathf.Max(0f, instance.Pool.Entry.DurationSeconds);
            if (end < instance.EndTime) instance.EndTime = end;
            return true;
        }

        /// <summary>Stops every playing instance (see <see cref="Stop(VfxHandle, bool)"/>).</summary>
        public void StopAll(bool immediate = true)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (i >= active.Count) continue;
                VfxInstance instance = active[i];
                if (immediate) Release(instance);
                else Stop(new VfxHandle(instance), false);
            }
        }

        /// <summary>
        /// Creates free instances of <paramref name="effectId"/> until its pool holds
        /// <paramref name="count"/> of them (capped by <see cref="MaxPooledPerEffect"/>), so the first
        /// plays do not instantiate.
        /// </summary>
        /// <returns>The number of free instances in the pool, or -1 when the id is unknown.</returns>
        public int Prewarm(string effectId, int count)
        {
            VfxPool pool = GetPool(effectId);
            if (pool == null) return -1;
            int target = Mathf.Min(count, maxPooledPerEffect);
            while (pool.Free.Count < target)
                pool.Free.Push(CreateInstance(pool));
            return pool.Free.Count;
        }

        /// <summary>Number of free instances in the pool of <paramref name="effectId"/> (0 when it has none).</summary>
        public int PooledCount(string effectId) =>
            effectId != null && pools.TryGetValue(effectId, out VfxPool pool) ? pool.Free.Count : 0;

        /// <summary>Instances of <paramref name="effectId"/> that exist now, free or playing.</summary>
        internal int LiveCount(string effectId) =>
            effectId != null && pools.TryGetValue(effectId, out VfxPool pool) ? pool.Live : 0;

        /// <summary>Instances of <paramref name="effectId"/> ever instantiated by this player.</summary>
        internal int CreatedTotal(string effectId) =>
            effectId != null && pools.TryGetValue(effectId, out VfxPool pool) ? pool.CreatedTotal : 0;

        void Update()
        {
            float now = Now;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (i >= active.Count) continue;
                VfxInstance instance = active[i];
                if (instance.GameObject == null)
                {
                    // Destroyed with the object it was attached to.
                    RemoveActive(instance);
                    instance.Generation++;
                    instance.Pool.Live--;
                    continue;
                }
                if (now >= instance.EndTime) Release(instance);
            }
        }

        void OnDestroy()
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                VfxInstance instance = active[i];
                instance.Generation++;
                instance.IsActive = false;
                if (instance.GameObject != null) Destroy(instance.GameObject);
            }
            active.Clear();
            pools.Clear();
        }

        VfxPool GetPool(string effectId)
        {
            if (effectId != null && pools.TryGetValue(effectId, out VfxPool pool)) return pool;
            if (library == null)
            {
                Debug.LogError($"VfxPlayer: no library; cannot play '{effectId}'", this);
                return null;
            }
            if (!library.TryGet(effectId, out VfxLibrary.Entry entry))
            {
                Debug.LogError($"VfxPlayer: unknown effect id '{effectId}' in library '{library.name}'", this);
                return null;
            }
            if (entry.Prefab == null)
            {
                Debug.LogError($"VfxPlayer: effect '{effectId}' has no prefab", this);
                return null;
            }
            pool = new VfxPool(effectId, entry);
            pools.Add(effectId, pool);
            return pool;
        }

        VfxInstance Acquire(string effectId)
        {
            VfxPool pool = GetPool(effectId);
            if (pool == null) return null;
            while (pool.Free.Count > 0)
            {
                VfxInstance instance = pool.Free.Pop();
                if (instance.GameObject != null) return instance;
                pool.Live--; // destroyed from outside while pooled
            }
            return CreateInstance(pool);
        }

        VfxInstance CreateInstance(VfxPool pool)
        {
            GameObject go = Instantiate(pool.Entry.Prefab, PoolRoot, false);
            go.name = pool.Entry.Prefab.name;
            pool.Live++;
            pool.CreatedTotal++;
            return new VfxInstance(this, pool, go);
        }

        VfxHandle Activate(VfxInstance instance, bool loop)
        {
            if (!instance.GameObject.activeSelf) instance.GameObject.SetActive(true);
            instance.Looping = loop;
            instance.Stopping = false;
            instance.IsActive = true;
            instance.EndTime = loop ? float.PositiveInfinity : Now + Mathf.Max(0f, instance.Pool.Entry.DurationSeconds);
            instance.ActiveIndex = active.Count;
            active.Add(instance);
            return new VfxHandle(instance);
        }

        void Release(VfxInstance instance)
        {
            if (!instance.IsActive) return;
            RemoveActive(instance);
            instance.Generation++;
            VfxPool pool = instance.Pool;
            if (instance.GameObject == null)
            {
                pool.Live--;
                return;
            }
            if (pool.Free.Count >= maxPooledPerEffect)
            {
                pool.Live--;
                Destroy(instance.GameObject);
                return;
            }
            instance.Transform.SetParent(PoolRoot, false);
            pool.Free.Push(instance);
        }

        void RemoveActive(VfxInstance instance)
        {
            int index = instance.ActiveIndex;
            int last = active.Count - 1;
            if (index < 0 || index > last || active[index] != instance) index = active.IndexOf(instance);
            if (index >= 0)
            {
                if (index != last)
                {
                    VfxInstance moved = active[last];
                    active[index] = moved;
                    moved.ActiveIndex = index;
                }
                active.RemoveAt(last);
            }
            instance.ActiveIndex = -1;
            instance.IsActive = false;
            instance.Stopping = false;
        }

        void ClearPools()
        {
            foreach (VfxPool pool in pools.Values)
            {
                while (pool.Free.Count > 0)
                {
                    VfxInstance instance = pool.Free.Pop();
                    if (instance.GameObject != null) Destroy(instance.GameObject);
                }
            }
            pools.Clear();
        }

        Transform PoolRoot
        {
            get
            {
                if (poolRoot != null) return poolRoot;
                var go = new GameObject("VfxPool");
                go.SetActive(false);
                poolRoot = go.transform;
                poolRoot.SetParent(transform, false);
                return poolRoot;
            }
        }
    }

    sealed class VfxPool
    {
        public readonly string Id;
        public readonly VfxLibrary.Entry Entry;
        public readonly Stack<VfxInstance> Free = new Stack<VfxInstance>(4);
        public int Live;
        public int CreatedTotal;

        public VfxPool(string id, VfxLibrary.Entry entry)
        {
            Id = id;
            Entry = entry;
        }
    }

    sealed class VfxInstance
    {
        public readonly VfxPlayer Owner;
        public readonly VfxPool Pool;
        public readonly GameObject GameObject;
        public readonly Transform Transform;
        public readonly VisualEffect[] Effects;
        public int Generation;
        public int ActiveIndex = -1;
        public bool IsActive;
        public bool Looping;
        public bool Stopping;
        public float EndTime;

        public VfxInstance(VfxPlayer owner, VfxPool pool, GameObject gameObject)
        {
            Owner = owner;
            Pool = pool;
            GameObject = gameObject;
            Transform = gameObject.transform;
            Effects = gameObject.GetComponentsInChildren<VisualEffect>(true);
        }
    }
}
