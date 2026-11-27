using System;
using System.Collections.Generic;
using UnityEngine;
#if CUVARA_VFX_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using InputKey = UnityEngine.InputSystem.Key;
#endif

namespace Cuvara.VFX
{
    /// <summary>How a <see cref="VfxStation"/> starts its effect on its own.</summary>
    public enum VfxStationTrigger
    {
        /// <summary>Only <see cref="VfxStation.PlayNow"/> plays it.</summary>
        Manual = 0,

        /// <summary>Plays when the station is enabled, then every <see cref="VfxStation.LoopInterval"/> seconds.</summary>
        Interval = 1,

        /// <summary>Plays when <see cref="VfxStation.Key"/> is pressed.</summary>
        Key = 2,
    }

    /// <summary>
    /// One station of the Showcase: a label, an effect slot (an id of the <see cref="VfxPlayer"/>'s
    /// <see cref="VfxLibrary"/>, changeable at run time), an anchor where the effect plays (a
    /// transform, or a <see cref="VfxSocket"/> of a caster), targets, and a trigger (a loop interval,
    /// a key, or manual only).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A play goes through <see cref="VfxPlayer"/> as a one-shot. With <see cref="AttachToAnchor"/> the
    /// instance is parented to the anchor; otherwise it is placed at the anchor's position and
    /// rotation. Without an anchor the station's own transform is used. When the played instance
    /// carries a <see cref="ChainLightningBinder"/>, the station sets the anchor as its source and the
    /// valid targets (at most <see cref="ChainLightningBinder.MaxTargets"/>) as its targets, and binds
    /// them at once.
    /// </para>
    /// <para>
    /// The anchor and each target may be resolved to a socket: with <see cref="AnchorSocket"/> (or
    /// <see cref="TargetSocket"/>) set, the station looks for a <see cref="VfxSocket"/> of that name under
    /// the transform and falls back to the transform itself (<see cref="VfxSocket.FindOrRoot"/>).
    /// </para>
    /// <para>
    /// The key trigger reads the Input System when the project has the package and its active input
    /// handling includes it (<c>CUVARA_VFX_INPUT_SYSTEM</c>, a version define of the
    /// <c>com.unity.inputsystem</c> package, and <c>ENABLE_INPUT_SYSTEM</c>), else the legacy Input
    /// Manager (<c>ENABLE_LEGACY_INPUT_MANAGER</c>); with neither it never fires. The key is a
    /// <see cref="KeyCode"/> in both cases (letters, digits, keypad digits, F1 to F12, space, return,
    /// escape, tab, backspace and the arrows map to the Input System).
    /// </para>
    /// <para>
    /// The interval runs on scaled time, so <see cref="Time.timeScale"/> slows it with the effect.
    /// <c>Update</c> does not allocate managed memory. Main thread only.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cuvara/VFX/VFX Station")]
    public sealed class VfxStation : MonoBehaviour
    {
        [SerializeField, Tooltip("Name shown by the Showcase overlay.")]
        string label = "Station";

        [SerializeField, Tooltip("Plays the effect; when empty, the first VfxPlayer of the scene.")]
        VfxPlayer player;

        [SerializeField, Tooltip("Effect id of the player's library.")]
        string effectId;

        [SerializeField, Tooltip("Where the effect plays, for example the caster; empty: this transform.")]
        Transform anchor;

        [SerializeField, Tooltip("Socket name under the anchor (hand_r, chest, ...); empty: the anchor itself.")]
        string anchorSocket;

        [SerializeField, Tooltip("Parent the effect to the anchor; otherwise place it at the anchor's pose.")]
        bool attachToAnchor = true;

        [SerializeField, Tooltip("Targets in hop order, for example characters; null or inactive ones are skipped by the binder.")]
        List<Transform> targets = new List<Transform>();

        [SerializeField, Tooltip("Socket name under each target (chest, ...); empty: the target itself.")]
        string targetSocket;

        [SerializeField, Tooltip("What plays the effect on its own.")]
        VfxStationTrigger trigger = VfxStationTrigger.Interval;

        [SerializeField, Min(0.05f), Tooltip("Seconds between two plays of the Interval trigger.")]
        float loopInterval = 2f;

        [SerializeField, Tooltip("Key of the Key trigger.")]
        KeyCode key = KeyCode.None;

        readonly List<Transform> resolvedTargets = new List<Transform>(ChainLightningBinder.MaxTargets);
        float timer;
        bool playOnNextUpdate;

        /// <summary>Raised after every successful play, with the station and the handle of the instance.</summary>
        public event Action<VfxStation, VfxHandle> Played;

        /// <summary>Name shown by the Showcase overlay.</summary>
        public string Label
        {
            get => label;
            set => label = value;
        }

        /// <summary>The player; when null, the first <see cref="VfxPlayer"/> of the scene is used at the next play.</summary>
        public VfxPlayer Player
        {
            get => player;
            set => player = value;
        }

        /// <summary>The effect id played; may change at run time (the next play uses the new id).</summary>
        public string EffectId
        {
            get => effectId;
            set => effectId = value;
        }

        /// <summary>The anchor transform; null means the station's own transform.</summary>
        public Transform Anchor
        {
            get => anchor;
            set => anchor = value;
        }

        /// <summary>Socket name looked up under <see cref="Anchor"/>; null or empty uses the anchor itself.</summary>
        public string AnchorSocket
        {
            get => anchorSocket;
            set => anchorSocket = value;
        }

        /// <summary>Parent the effect to the anchor (true) or place it at the anchor's pose (false).</summary>
        public bool AttachToAnchor
        {
            get => attachToAnchor;
            set => attachToAnchor = value;
        }

        /// <summary>The targets in hop order. Edit the list in place.</summary>
        public List<Transform> Targets => targets;

        /// <summary>Socket name looked up under each target; null or empty uses the target itself.</summary>
        public string TargetSocket
        {
            get => targetSocket;
            set => targetSocket = value;
        }

        /// <summary>What plays the effect on its own.</summary>
        public VfxStationTrigger Trigger
        {
            get => trigger;
            set
            {
                if (trigger == value) return;
                trigger = value;
                ResetTimer();
            }
        }

        /// <summary>Seconds between two plays of <see cref="VfxStationTrigger.Interval"/> (at least 0.05).</summary>
        public float LoopInterval
        {
            get => loopInterval;
            set => loopInterval = Mathf.Max(0.05f, value);
        }

        /// <summary>Key of <see cref="VfxStationTrigger.Key"/>.</summary>
        public KeyCode Key
        {
            get => key;
            set => key = value;
        }

        /// <summary>The handle of the last play (it may have ended).</summary>
        public VfxHandle LastHandle { get; private set; }

        /// <summary>Number of successful plays since the station was created.</summary>
        public int PlayCount { get; private set; }

        /// <summary>The transform the effect plays on: the socket under the anchor, the anchor, or this transform.</summary>
        public Transform ResolvedAnchor => Resolve(anchor != null ? anchor : transform, anchorSocket);

        /// <summary>
        /// Plays the effect now and binds a <see cref="ChainLightningBinder"/> of the instance to the
        /// anchor and the targets.
        /// </summary>
        /// <remarks>
        /// A station without an effect (<see cref="EffectId"/> null or empty) is silent: it returns
        /// <see cref="VfxHandle.None"/> and logs nothing, so an empty slot of the Showcase does not log
        /// at every trigger.
        /// </remarks>
        /// <returns>The handle, or <see cref="VfxHandle.None"/> when the station has no effect, there is no player or the play failed (the player logs why).</returns>
        public VfxHandle PlayNow()
        {
            if (string.IsNullOrEmpty(effectId)) return VfxHandle.None;
            if (player == null) player = FindAnyObjectByType<VfxPlayer>();
            if (player == null)
            {
                Debug.LogError($"VfxStation '{label}': no VfxPlayer to play '{effectId}'", this);
                return VfxHandle.None;
            }

            Transform at = ResolvedAnchor;
            VfxHandle handle = attachToAnchor
                ? player.Play(effectId, at)
                : player.Play(effectId, at.position, at.rotation);
            if (!handle.IsValid) return handle;

            ChainLightningBinder binder = handle.GameObject.GetComponentInChildren<ChainLightningBinder>(true);
            if (binder != null) Bind(binder, at);

            LastHandle = handle;
            PlayCount++;
            Played?.Invoke(this, handle);
            return handle;
        }

        /// <summary>Restarts the interval: the next <c>Update</c> plays at once.</summary>
        public void ResetTimer()
        {
            timer = 0f;
            playOnNextUpdate = trigger == VfxStationTrigger.Interval;
        }

        /// <summary>Sets <paramref name="binder"/>'s source to <paramref name="source"/> and its targets to the resolved valid targets.</summary>
        void Bind(ChainLightningBinder binder, Transform source)
        {
            ResolveTargets();
            binder.Source = source;
            List<Transform> list = binder.Targets;
            list.Clear();
            for (int i = 0; i < resolvedTargets.Count; i++) list.Add(resolvedTargets[i]);
            binder.Bind();
        }

        /// <summary>Fills <see cref="resolvedTargets"/> with the valid targets (socket or transform), at most the binder's limit.</summary>
        void ResolveTargets()
        {
            resolvedTargets.Clear();
            for (int i = 0; i < targets.Count && resolvedTargets.Count < ChainLightningBinder.MaxTargets; i++)
            {
                Transform t = targets[i];
                if (t == null || !t.gameObject.activeInHierarchy) continue;
                resolvedTargets.Add(Resolve(t, targetSocket));
            }
        }

        static Transform Resolve(Transform root, string socket) =>
            string.IsNullOrEmpty(socket) ? root : VfxSocket.FindOrRoot(root, socket);

        void OnEnable() => ResetTimer();

        void OnValidate() => loopInterval = Mathf.Max(0.05f, loopInterval);

        void Update()
        {
            switch (trigger)
            {
                case VfxStationTrigger.Interval:
                    if (playOnNextUpdate)
                    {
                        playOnNextUpdate = false;
                        timer = 0f;
                        PlayNow();
                        break;
                    }
                    timer += Time.deltaTime;
                    if (timer >= loopInterval)
                    {
                        // Keep the remainder so the plays do not drift with the frame rate.
                        timer -= loopInterval;
                        if (timer >= loopInterval) timer = 0f;
                        PlayNow();
                    }
                    break;
                case VfxStationTrigger.Key:
                    if (WasPressedThisFrame(key)) PlayNow();
                    break;
            }
        }

        /// <summary>
        /// True when <paramref name="keyCode"/> went down this frame, through the Input System or the
        /// legacy Input Manager (see the class remarks). False for <see cref="KeyCode.None"/>, an
        /// unmapped key, or when neither input backend is active.
        /// </summary>
        public static bool WasPressedThisFrame(KeyCode keyCode)
        {
            if (keyCode == KeyCode.None) return false;
#if CUVARA_VFX_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && TryMapKey(keyCode, out InputKey mapped))
                return keyboard[mapped].wasPressedThisFrame;
            return false;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(keyCode);
#else
            return false;
#endif
        }

#if CUVARA_VFX_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        /// <summary>The Input System key of <paramref name="keyCode"/>, for the keys the class remarks list.</summary>
        internal static bool TryMapKey(KeyCode keyCode, out InputKey mapped)
        {
            if (keyCode >= KeyCode.A && keyCode <= KeyCode.Z)
            {
                mapped = InputKey.A + (keyCode - KeyCode.A);
                return true;
            }
            if (keyCode >= KeyCode.Alpha1 && keyCode <= KeyCode.Alpha9)
            {
                mapped = InputKey.Digit1 + (keyCode - KeyCode.Alpha1);
                return true;
            }
            if (keyCode >= KeyCode.Keypad0 && keyCode <= KeyCode.Keypad9)
            {
                mapped = InputKey.Numpad0 + (keyCode - KeyCode.Keypad0);
                return true;
            }
            if (keyCode >= KeyCode.F1 && keyCode <= KeyCode.F12)
            {
                mapped = InputKey.F1 + (keyCode - KeyCode.F1);
                return true;
            }
            switch (keyCode)
            {
                case KeyCode.Alpha0: mapped = InputKey.Digit0; return true;
                case KeyCode.Space: mapped = InputKey.Space; return true;
                case KeyCode.Return: mapped = InputKey.Enter; return true;
                case KeyCode.KeypadEnter: mapped = InputKey.NumpadEnter; return true;
                case KeyCode.Escape: mapped = InputKey.Escape; return true;
                case KeyCode.Tab: mapped = InputKey.Tab; return true;
                case KeyCode.Backspace: mapped = InputKey.Backspace; return true;
                case KeyCode.UpArrow: mapped = InputKey.UpArrow; return true;
                case KeyCode.DownArrow: mapped = InputKey.DownArrow; return true;
                case KeyCode.LeftArrow: mapped = InputKey.LeftArrow; return true;
                case KeyCode.RightArrow: mapped = InputKey.RightArrow; return true;
                default: mapped = InputKey.None; return false;
            }
        }
#endif
    }
}
