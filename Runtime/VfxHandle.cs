using System;
using UnityEngine;

namespace Cuvara.VFX
{
    /// <summary>
    /// Refers to one playing instance of <see cref="VfxPlayer"/>. Stop a looped effect with it.
    /// </summary>
    /// <remarks>
    /// A handle becomes invalid when its instance returns to the pool; a later play that reuses the same
    /// instance does not revive an old handle. The default value is <see cref="None"/>.
    /// </remarks>
    public readonly struct VfxHandle : IEquatable<VfxHandle>
    {
        internal readonly VfxInstance Instance;
        internal readonly int Generation;

        /// <summary>The invalid handle, returned when a play fails.</summary>
        public static readonly VfxHandle None = default;

        internal VfxHandle(VfxInstance instance)
        {
            Instance = instance;
            Generation = instance.Generation;
        }

        /// <summary>True while the instance plays (or fades out after a non-immediate stop).</summary>
        public bool IsValid => Instance != null && Instance.IsActive && Instance.Generation == Generation;

        /// <summary>Effect id of the instance, or null when the handle is invalid.</summary>
        public string EffectId => IsValid ? Instance.Pool.Id : null;

        /// <summary>Root object of the instance, or null when the handle is invalid. Do not destroy it.</summary>
        public GameObject GameObject => IsValid ? Instance.GameObject : null;

        /// <summary>True when the instance was played looped.</summary>
        public bool IsLooping => IsValid && Instance.Looping;

        /// <summary>Same as <see cref="VfxPlayer.Stop(VfxHandle, bool)"/> on the player that played it.</summary>
        /// <returns>False when the handle is already invalid.</returns>
        public bool Stop(bool immediate = false) => IsValid && Instance.Owner.Stop(this, immediate);

        /// <inheritdoc />
        public bool Equals(VfxHandle other) => ReferenceEquals(Instance, other.Instance) && Generation == other.Generation;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is VfxHandle other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => Instance == null ? 0 : (Instance.GetHashCode() * 397) ^ Generation;

        /// <summary>Compares two handles.</summary>
        public static bool operator ==(VfxHandle a, VfxHandle b) => a.Equals(b);

        /// <summary>Compares two handles.</summary>
        public static bool operator !=(VfxHandle a, VfxHandle b) => !a.Equals(b);
    }
}
