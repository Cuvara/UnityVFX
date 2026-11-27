using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace Cuvara.VFX.Editor.Capture
{
    /// <summary>
    /// The optional characters of a capture: a caster and up to <see cref="ChainLightningBinder.MaxTargets"/>
    /// targets in a lane, as a scale reference and as the source and targets of every
    /// <see cref="ChainLightningBinder"/> of the captured effect.
    /// </summary>
    /// <remarks>
    /// <para><b>Lane.</b> The effect stays at the origin, the camera's focus. The lane runs along +X and
    /// is centred on the origin: with <c>n</c> targets <c>s</c> metres apart the caster stands at
    /// x = -n·s/2 facing +X, target <c>i</c> (1 to n) at x = -n·s/2 + i·s facing the caster (-X), all on
    /// the ground (y = 0, z = 0). With no target the caster stands at the origin.</para>
    /// <para><b>Characters.</b> <see cref="VfxCaptureRequest.CharacterPlaced"/> is a prefab path, or
    /// <see cref="VfxCaptureRequest.Placeholder"/> for the code-built <see cref="VfxMannequin"/>. Each
    /// <see cref="Animator"/> of a placed character is evaluated once at time 0
    /// (<see cref="Animator.Rebind"/>, <see cref="Animator.Update(float)"/> with 0) and then disabled, so
    /// the pose is the first frame of the default state and never changes during the capture.</para>
    /// <para><b>Binding.</b> Every <see cref="ChainLightningBinder"/> under the effect gets the caster's
    /// source socket as its source and the targets' target sockets as its targets and binds at once,
    /// before the effect's first simulation step. A <c>.vfx</c> effect has no binder; when characters are
    /// placed and its graph exposes a property of the binder's default names, a binder is added to it.
    /// A missing socket falls back to the character's root, with a warning.</para>
    /// </remarks>
    public sealed class VfxCaptureCast
    {
        sealed class Placed
        {
            public GameObject Root;
            public Transform Socket;
            public bool SocketFound;
        }

        sealed class BinderRecord
        {
            public string Name;
            public bool Added;
            public int HopCount;
            public object Source;
            public List<object> Targets;
            public object GraphHopCount;
        }

        readonly VfxCaptureRequest m_Request;
        readonly List<Placed> m_Targets = new List<Placed>();
        readonly List<BinderRecord> m_Binders = new List<BinderRecord>();
        Placed m_Caster;

        VfxCaptureCast(VfxCaptureRequest request) => m_Request = request;

        /// <summary>True when the request places characters.</summary>
        public static bool IsRequested(VfxCaptureRequest request) => request != null && !string.IsNullOrEmpty(request.Character);

        /// <summary>The x of the caster for <paramref name="targetCount"/> targets <paramref name="spacing"/> metres apart.</summary>
        public static float CasterX(int targetCount, double spacing) => (float)(-targetCount * spacing * 0.5);

        /// <summary>
        /// Places the caster and the targets in <paramref name="stage"/> and binds every
        /// <see cref="ChainLightningBinder"/> under <paramref name="effectRoot"/>. Warnings go to
        /// <paramref name="result"/>. Returns null when the request places no character (nothing is done).
        /// </summary>
        public static VfxCaptureCast Place(VfxCaptureRequest request, VfxCaptureStage stage, GameObject effectRoot, VfxResult result)
        {
            if (!IsRequested(request))
                return null;
            if (request.CharacterFallback != null)
                result.Warning(request.CharacterFallback);
            var cast = new VfxCaptureCast(request);
            GameObject prefab = null;
            if (request.CharacterPlaced != VfxCaptureRequest.Placeholder)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(request.CharacterPlaced);
                if (prefab == null)
                    throw new InvalidOperationException($"character prefab '{request.CharacterPlaced}' could not be loaded");
            }

            var casterX = CasterX(request.TargetCount, request.TargetSpacing);
            cast.m_Caster = cast.Spawn(prefab, "Caster", new Vector3(casterX, 0f, 0f), Quaternion.Euler(0f, 90f, 0f), request.SourceSocket, stage);
            for (int i = 1; i <= request.TargetCount; i++)
            {
                var position = new Vector3(casterX + (float)(i * request.TargetSpacing), 0f, 0f);
                cast.m_Targets.Add(cast.Spawn(prefab, "Target" + i, position, Quaternion.Euler(0f, -90f, 0f), request.TargetSocket, stage));
            }

            if (!cast.m_Caster.SocketFound)
                result.Warning($"the caster has no VfxSocket '{request.SourceSocket}': its root is the source");
            var missing = cast.m_Targets.Count(t => !t.SocketFound);
            if (missing > 0)
                result.Warning($"{missing} target(s) have no VfxSocket '{request.TargetSocket}': their roots are the targets");

            cast.Bind(effectRoot);
            return cast;
        }

        Placed Spawn(GameObject prefab, string name, Vector3 position, Quaternion rotation, string socketName, VfxCaptureStage stage)
        {
            GameObject root;
            if (prefab == null)
            {
                root = VfxMannequin.Create(null, name);
                root.transform.SetPositionAndRotation(position, rotation);
            }
            else
            {
                root = Object.Instantiate(prefab, position, rotation);
                root.name = name;
            }
            stage.Own(root);
            FreezePose(root);
            var socket = VfxSocket.Find(root.transform, socketName);
            return new Placed { Root = root, Socket = socket != null ? socket.transform : root.transform, SocketFound = socket != null };
        }

        /// <summary>Evaluates every <see cref="Animator"/> under <paramref name="root"/> at time 0 once, then disables it.</summary>
        static void FreezePose(GameObject root)
        {
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
            {
                if (animator.runtimeAnimatorController != null && animator.isActiveAndEnabled)
                {
                    animator.Rebind();
                    animator.Update(0f);
                }
                animator.enabled = false;
            }
        }

        void Bind(GameObject effectRoot)
        {
            var binders = effectRoot.GetComponentsInChildren<ChainLightningBinder>(true).ToList();
            var added = new HashSet<ChainLightningBinder>();
            if (binders.Count == 0 && m_Request.Kind == "vfx")
            {
                var effect = effectRoot.GetComponent<VisualEffect>();
                if (effect != null && ExposesBinderProperties(effect))
                {
                    var binder = effectRoot.AddComponent<ChainLightningBinder>();
                    binder.VisualEffect = effect;
                    binders.Add(binder);
                    added.Add(binder);
                }
            }

            var targets = m_Targets.Select(t => t.Socket).ToList();
            foreach (var binder in binders)
            {
                binder.Source = m_Caster.Socket;
                binder.SetTargets(targets);
                var hops = binder.Bind();
                var effect = binder.VisualEffect;
                var record = new BinderRecord
                {
                    Name = Path(binder.transform, effectRoot.transform),
                    Added = added.Contains(binder),
                    HopCount = hops,
                    Targets = new List<object>(),
                };
                if (effect != null)
                {
                    var sourceId = string.IsNullOrEmpty(binder.SourceProperty) ? -1 : Shader.PropertyToID(binder.SourceProperty);
                    if (sourceId >= 0 && effect.HasVector3(sourceId))
                        record.Source = Vector(effect.GetVector3(sourceId));
                    for (int slot = 0; slot < hops && !string.IsNullOrEmpty(binder.TargetPropertyPrefix); slot++)
                    {
                        var id = Shader.PropertyToID(binder.TargetPropertyPrefix + slot);
                        if (effect.HasVector3(id))
                            record.Targets.Add(Vector(effect.GetVector3(id)));
                    }
                    if (!string.IsNullOrEmpty(binder.HopCountProperty))
                    {
                        var id = Shader.PropertyToID(binder.HopCountProperty);
                        if (effect.HasInt(id))
                            record.GraphHopCount = (long)effect.GetInt(id);
                        else if (effect.HasUInt(id))
                            record.GraphHopCount = (long)effect.GetUInt(id);
                        else if (effect.HasFloat(id))
                            record.GraphHopCount = Math.Round(effect.GetFloat(id), 4);
                    }
                }
                m_Binders.Add(record);
            }
        }

        static bool ExposesBinderProperties(VisualEffect effect) =>
            effect.HasVector3(Shader.PropertyToID(ChainLightningBinder.DefaultSourceProperty)) ||
            effect.HasVector3(Shader.PropertyToID(ChainLightningBinder.DefaultTargetPropertyPrefix + "0"));

        /// <summary>
        /// The number of characters not fully inside <paramref name="camera"/>'s view (the world bounds
        /// of a character's renderers, all eight corners; <c>in_frame</c> in <c>stats.json</c>).
        /// </summary>
        public int CountOutsideFrame(Camera camera)
        {
            var outside = 0;
            foreach (var placed in All())
            {
                if (!InFrame(placed.Root, camera))
                    outside++;
            }
            return outside;
        }

        IEnumerable<Placed> All()
        {
            if (m_Caster != null)
                yield return m_Caster;
            foreach (var target in m_Targets)
                yield return target;
        }

        static bool InFrame(GameObject root, Camera camera)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0)
                return Inside(camera.WorldToViewportPoint(root.transform.position));
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x, (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                if (!Inside(camera.WorldToViewportPoint(corner)))
                    return false;
            }
            return true;
        }

        static bool Inside(Vector3 viewport) => viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;

        /// <summary>The <c>characters</c> field of <c>stats.json</c>.</summary>
        public JsonObject CharactersJson(Camera camera)
        {
            JsonObject Character(Placed placed) => new JsonObject
            {
                { "position", Vector(placed.Root.transform.position) },
                { "socket", Vector(placed.Socket.position) },
                { "socket_found", placed.SocketFound },
                { "in_frame", camera != null && InFrame(placed.Root, camera) },
            };
            return new JsonObject
            {
                { "character", m_Request.Character },
                { "placed", m_Request.CharacterPlaced },
                { "source_socket", m_Request.SourceSocket },
                { "target_socket", m_Request.TargetSocket },
                { "target_count", (long)m_Request.TargetCount },
                { "target_spacing", Math.Round(m_Request.TargetSpacing, 4) },
                { "caster", Character(m_Caster) },
                { "targets", m_Targets.Select(t => (object)Character(t)).ToList() },
            };
        }

        /// <summary>The <c>binders</c> field of <c>stats.json</c>: one entry per bound <see cref="ChainLightningBinder"/>.</summary>
        public List<object> BindersJson() => m_Binders.Select(b => (object)new JsonObject
        {
            { "name", b.Name },
            { "added", b.Added },
            { "hop_count", (long)b.HopCount },
            { "graph_hop_count", b.GraphHopCount },
            { "source", b.Source },
            { "targets", b.Targets },
        }).ToList();

        static List<object> Vector(Vector3 v) => new List<object> { Math.Round(v.x, 4), Math.Round(v.y, 4), Math.Round(v.z, 4) };

        static string Path(Transform transform, Transform root)
        {
            var names = new List<string>();
            for (var t = transform; t != null && t != root; t = t.parent)
                names.Add(t.name);
            names.Reverse();
            return names.Count == 0 ? root.name : root.name + "/" + string.Join("/", names);
        }
    }
}
