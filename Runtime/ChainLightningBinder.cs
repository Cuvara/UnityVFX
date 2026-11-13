using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace Cuvara.VFX
{
    /// <summary>
    /// Writes the position of a source and of up to <see cref="MaxTargets"/> targets, and the hop count,
    /// into exposed properties of a <see cref="VisualEffect"/> every frame, for a chain-lightning graph.
    /// </summary>
    /// <remarks>
    /// <para>Exposed properties (names configurable, defaults in brackets):</para>
    /// <list type="bullet">
    /// <item><description><c>Vector3</c> source position [<see cref="DefaultSourceProperty"/>].</description></item>
    /// <item><description><c>Vector3</c> target positions [<see cref="DefaultTargetPropertyPrefix"/> + index:
    /// <c>TargetPosition0</c> .. <c>TargetPosition7</c>].</description></item>
    /// <item><description>hop count [<see cref="DefaultHopCountProperty"/>], exposed as <c>int</c>, <c>uint</c>
    /// or <c>float</c>.</description></item>
    /// </list>
    /// <para>
    /// Null, destroyed and inactive targets are skipped: the valid targets are written to consecutive
    /// slots from index 0, and the hop count is their number (at most <see cref="MaxTargets"/>; valid
    /// targets beyond that are ignored). Slots from the hop count up repeat the last written position
    /// (the source when there is no valid target), so a graph that reads every slot draws no stray arc.
    /// </para>
    /// <para>
    /// Positions are world space unless <see cref="WriteLocalPositions"/> is set, which converts them
    /// into the space of the <see cref="VisualEffect"/>'s transform. A property the graph does not
    /// expose is skipped without a log (cached property ids and <c>HasVector3</c>/<c>HasInt</c> guards),
    /// so the binder works with any graph. The update runs in <c>LateUpdate</c>, after gameplay moved
    /// the targets. No managed allocation per frame.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    [AddComponentMenu("Cuvara/VFX/Chain Lightning Binder")]
    public sealed class ChainLightningBinder : MonoBehaviour
    {
        /// <summary>Fixed maximum number of targets written to the graph.</summary>
        public const int MaxTargets = 8;

        /// <summary>Default name of the source position property.</summary>
        public const string DefaultSourceProperty = "SourcePosition";

        /// <summary>Default prefix of the target position properties (followed by 0 .. <see cref="MaxTargets"/> - 1).</summary>
        public const string DefaultTargetPropertyPrefix = "TargetPosition";

        /// <summary>Default name of the hop count property.</summary>
        public const string DefaultHopCountProperty = "HopCount";

        [SerializeField, Tooltip("Effect to write to; defaults to the VisualEffect on this object.")]
        VisualEffect visualEffect;

        [SerializeField, Tooltip("Where the chain starts, for example the caster's hand socket.")]
        Transform source;

        [SerializeField, Tooltip("Targets in hop order; null or inactive entries are skipped.")]
        List<Transform> targets = new List<Transform>();

        [SerializeField, Tooltip("Convert positions into the VisualEffect's local space (for a graph in Local space).")]
        bool writeLocalPositions;

        [SerializeField] string sourceProperty = DefaultSourceProperty;
        [SerializeField] string targetPropertyPrefix = DefaultTargetPropertyPrefix;
        [SerializeField] string hopCountProperty = DefaultHopCountProperty;

        // Shader.PropertyToID never returns a negative id; -1 marks an empty property name.
        const int NoId = -1;

        int sourceId;
        readonly int[] targetIds = new int[MaxTargets];
        int hopCountId;
        bool idsReady;
        VisualEffectSink sink;

        /// <summary>The effect written to.</summary>
        public VisualEffect VisualEffect
        {
            get => visualEffect;
            set
            {
                visualEffect = value;
                sink = null;
            }
        }

        /// <summary>Where the chain starts.</summary>
        public Transform Source
        {
            get => source;
            set => source = value;
        }

        /// <summary>The target list in hop order. Edit it in place or use <see cref="SetTargets"/>.</summary>
        public List<Transform> Targets => targets;

        /// <summary>Convert positions into the space of the <see cref="VisualEffect"/>'s transform.</summary>
        public bool WriteLocalPositions
        {
            get => writeLocalPositions;
            set => writeLocalPositions = value;
        }

        /// <summary>Name of the <c>Vector3</c> source property. Setting it refreshes the cached id.</summary>
        public string SourceProperty
        {
            get => sourceProperty;
            set
            {
                sourceProperty = value;
                idsReady = false;
            }
        }

        /// <summary>Prefix of the <c>Vector3</c> target properties. Setting it refreshes the cached ids.</summary>
        public string TargetPropertyPrefix
        {
            get => targetPropertyPrefix;
            set
            {
                targetPropertyPrefix = value;
                idsReady = false;
            }
        }

        /// <summary>Name of the hop count property. Setting it refreshes the cached id.</summary>
        public string HopCountProperty
        {
            get => hopCountProperty;
            set
            {
                hopCountProperty = value;
                idsReady = false;
            }
        }

        /// <summary>Hop count written by the last update (number of valid targets, at most <see cref="MaxTargets"/>).</summary>
        public int HopCount { get; private set; }

        /// <summary>Replaces the targets (copies <paramref name="newTargets"/>; null clears them).</summary>
        public void SetTargets(IEnumerable<Transform> newTargets)
        {
            targets.Clear();
            if (newTargets != null) targets.AddRange(newTargets);
        }

        /// <summary>Writes the current positions now instead of waiting for <c>LateUpdate</c>.</summary>
        /// <returns>The hop count written.</returns>
        public int Bind()
        {
            if (visualEffect == null) visualEffect = GetComponent<VisualEffect>();
            if (visualEffect == null)
            {
                HopCount = CountValidTargets();
                return HopCount;
            }
            if (sink == null || sink.Effect != visualEffect) sink = new VisualEffectSink(visualEffect);
            return Apply(sink);
        }

        /// <summary>Writes the current positions into <paramref name="target"/> (the testable core of <see cref="Bind"/>).</summary>
        internal int Apply(IVfxPropertySink target)
        {
            EnsureIds();
            Transform space = writeLocalPositions && visualEffect != null ? visualEffect.transform : null;

            Vector3 last = Vector3.zero;
            bool hasSource = source != null;
            if (hasSource)
            {
                last = ToSpace(space, source.position);
                if (sourceId != NoId && target.HasVector3(sourceId)) target.SetVector3(sourceId, last);
            }

            int hops = 0;
            for (int i = 0; i < targets.Count && hops < MaxTargets; i++)
            {
                Transform t = targets[i];
                if (!IsValid(t)) continue;
                last = ToSpace(space, t.position);
                int id = targetIds[hops];
                if (id != NoId && target.HasVector3(id)) target.SetVector3(id, last);
                hops++;
            }
            if (hops > 0 || hasSource)
            {
                for (int slot = hops; slot < MaxTargets; slot++)
                {
                    int id = targetIds[slot];
                    if (id != NoId && target.HasVector3(id)) target.SetVector3(id, last);
                }
            }

            if (hopCountId != NoId)
            {
                if (target.HasInt(hopCountId)) target.SetInt(hopCountId, hops);
                else if (target.HasUInt(hopCountId)) target.SetUInt(hopCountId, (uint)hops);
                else if (target.HasFloat(hopCountId)) target.SetFloat(hopCountId, hops);
            }
            HopCount = hops;
            return hops;
        }

        /// <summary>Recomputes the cached property ids from the property names.</summary>
        public void RefreshPropertyIds() => idsReady = false;

        void OnEnable()
        {
            idsReady = false;
            sink = null;
        }

        void OnValidate()
        {
            idsReady = false;
            sink = null;
        }

        void LateUpdate() => Bind();

        void EnsureIds()
        {
            if (idsReady) return;
            sourceId = PropertyId(sourceProperty);
            bool hasPrefix = !string.IsNullOrEmpty(targetPropertyPrefix);
            for (int i = 0; i < MaxTargets; i++)
                targetIds[i] = hasPrefix ? Shader.PropertyToID(targetPropertyPrefix + i) : NoId;
            hopCountId = PropertyId(hopCountProperty);
            idsReady = true;
        }

        int CountValidTargets()
        {
            int hops = 0;
            for (int i = 0; i < targets.Count && hops < MaxTargets; i++)
                if (IsValid(targets[i])) hops++;
            return hops;
        }

        // Unity's null check also catches a destroyed transform.
        static bool IsValid(Transform t) => t != null && t.gameObject.activeInHierarchy;

        static int PropertyId(string name) => string.IsNullOrEmpty(name) ? NoId : Shader.PropertyToID(name);

        static Vector3 ToSpace(Transform space, Vector3 world) => space != null ? space.InverseTransformPoint(world) : world;
    }

    /// <summary>The part of <see cref="VisualEffect"/> the binder writes through; a seam for tests.</summary>
    interface IVfxPropertySink
    {
        bool HasVector3(int id);
        void SetVector3(int id, Vector3 value);
        bool HasInt(int id);
        void SetInt(int id, int value);
        bool HasUInt(int id);
        void SetUInt(int id, uint value);
        bool HasFloat(int id);
        void SetFloat(int id, float value);
    }

    sealed class VisualEffectSink : IVfxPropertySink
    {
        public readonly VisualEffect Effect;

        public VisualEffectSink(VisualEffect effect) => Effect = effect;

        public bool HasVector3(int id) => Effect.HasVector3(id);
        public void SetVector3(int id, Vector3 value) => Effect.SetVector3(id, value);
        public bool HasInt(int id) => Effect.HasInt(id);
        public void SetInt(int id, int value) => Effect.SetInt(id, value);
        public bool HasUInt(int id) => Effect.HasUInt(id);
        public void SetUInt(int id, uint value) => Effect.SetUInt(id, value);
        public bool HasFloat(int id) => Effect.HasFloat(id);
        public void SetFloat(int id, float value) => Effect.SetFloat(id, value);
    }
}
