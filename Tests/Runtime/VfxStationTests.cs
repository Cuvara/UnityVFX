using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cuvara.VFX.Tests
{
    /// <summary>
    /// <see cref="VfxStation"/>: plays on its interval and on the direct call, changes its effect at
    /// run time, resolves anchor and target sockets, and binds a <see cref="ChainLightningBinder"/> of
    /// the played instance to the anchor and the targets.
    /// </summary>
    public sealed class VfxStationTests
    {
        const string Spark = "spark";
        const string Chain = "chain";
        const string Area = "area";

        TestObjects objects;
        VfxPlayer player;

        [SetUp]
        public void SetUp()
        {
            objects = new TestObjects();
            GameObject chainPrefab = objects.EffectPrefab("ChainFx");
            chainPrefab.AddComponent<ChainLightningBinder>();
            VfxLibrary library = objects.Library(
                new VfxLibrary.Entry(Spark, objects.EffectPrefab("SparkFx"), 0.1f),
                new VfxLibrary.Entry(Chain, chainPrefab, 0.1f),
                new VfxLibrary.Entry(Area, objects.EffectPrefab("AreaFx"), 0.1f));
            player = objects.GameObject("Player").AddComponent<VfxPlayer>();
            player.Library = library;
        }

        [TearDown]
        public void TearDown() => objects.DestroyAll();

        VfxStation Station(string effectId, VfxStationTrigger trigger)
        {
            GameObject go = objects.GameObject("Station");
            go.SetActive(false);
            var station = go.AddComponent<VfxStation>();
            station.Player = player;
            station.EffectId = effectId;
            station.Trigger = trigger;
            go.SetActive(true);
            return station;
        }

        Transform Character(string name, Vector3 position, params (string socket, Vector3 local)[] sockets)
        {
            Transform root = objects.GameObject(name).transform;
            root.position = position;
            foreach (var (socket, local) in sockets)
            {
                Transform t = objects.GameObject("VfxSocket_" + socket, root).transform;
                t.localPosition = local;
                t.gameObject.AddComponent<VfxSocket>().SocketName = socket;
            }
            return root;
        }

        [Test]
        public void PlayNow_PlaysTheEffect_AtTheStation_AndCountsIt()
        {
            VfxStation station = Station(Spark, VfxStationTrigger.Manual);
            station.transform.position = new Vector3(4f, 0f, 2f);
            int raised = 0;
            station.Played += (s, h) => raised++;

            VfxHandle handle = station.PlayNow();

            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(Spark, handle.EffectId);
            Assert.AreEqual(station.transform, handle.GameObject.transform.parent, "attached to the anchor, here the station itself");
            Assert.AreEqual(station.transform.position, handle.GameObject.transform.position);
            Assert.AreEqual(1, station.PlayCount);
            Assert.AreEqual(1, raised);
            Assert.AreEqual(handle, station.LastHandle);
        }

        [Test]
        public void PlayNow_OnASocketOfTheAnchor_AttachedOrPlaced()
        {
            Transform caster = Character("Caster", new Vector3(1f, 0f, 0f), (VfxSocket.HandRight, new Vector3(0.3f, 0.9f, 0f)));
            VfxStation station = Station(Spark, VfxStationTrigger.Manual);
            station.Anchor = caster;
            station.AnchorSocket = VfxSocket.HandRight;
            Transform hand = VfxSocket.Find(caster, VfxSocket.HandRight).transform;
            Assert.AreEqual(hand, station.ResolvedAnchor);

            VfxHandle attached = station.PlayNow();
            Assert.AreEqual(hand, attached.GameObject.transform.parent);

            station.AttachToAnchor = false;
            VfxHandle placed = station.PlayNow();
            Assert.IsNull(placed.GameObject.transform.parent);
            Assert.That(Vector3.Distance(hand.position, placed.GameObject.transform.position), Is.LessThan(1e-5f));

            station.AnchorSocket = "tail";
            Assert.AreEqual(caster, station.ResolvedAnchor, "an unknown socket falls back to the anchor");
        }

        [Test]
        public void EffectId_ChangesAtRunTime_AndTheNextPlayUsesIt()
        {
            VfxStation station = Station(Spark, VfxStationTrigger.Manual);
            Assert.AreEqual(Spark, station.PlayNow().EffectId);
            station.EffectId = Area;
            Assert.AreEqual(Area, station.PlayNow().EffectId);
            Assert.AreEqual(2, station.PlayCount);
        }

        [Test]
        public void PlayNow_WithAnUnknownEffect_ReturnsNone_AndDoesNotCount()
        {
            VfxStation station = Station("missing", VfxStationTrigger.Manual);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("unknown effect id 'missing'"));
            Assert.IsFalse(station.PlayNow().IsValid);
            Assert.AreEqual(0, station.PlayCount);
        }

        [UnityTest]
        public IEnumerator WithoutAnEffect_TheIntervalTriggerStaysSilent()
        {
            // No player at all either: an empty slot must not even look for one.
            player.gameObject.SetActive(false);
            VfxStation station = Station(null, VfxStationTrigger.Interval);
            station.Player = null;
            station.LoopInterval = 0.05f;
            station.ResetTimer();
            float start = Time.time;
            while (Time.time - start < 0.2f) yield return null;

            Assert.IsFalse(station.PlayNow().IsValid);
            station.EffectId = string.Empty;
            Assert.IsFalse(station.PlayNow().IsValid);
            Assert.AreEqual(0, station.PlayCount);
            Assert.IsNull(station.Player, "a station without an effect does not look up a player");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator IntervalTrigger_PlaysOnEnable_ThenEveryInterval()
        {
            VfxStation station = Station(Spark, VfxStationTrigger.Interval);
            station.LoopInterval = 0.2f;
            station.ResetTimer();
            float start = Time.time;
            yield return null;
            Assert.AreEqual(1, station.PlayCount, "the first play is on the first update");

            while (Time.time - start < 0.5f) yield return null;
            // Plays at 0, 0.2 and 0.4 s; one more when the last frame crossed 0.6 s.
            Assert.That(station.PlayCount, Is.InRange(3, 4));
        }

        [UnityTest]
        public IEnumerator ManualAndKeyTriggers_DoNotPlayOnTheirOwn()
        {
            VfxStation manual = Station(Spark, VfxStationTrigger.Manual);
            VfxStation key = Station(Spark, VfxStationTrigger.Key);
            key.Key = KeyCode.Alpha1;
            for (int i = 0; i < 10; i++) yield return null;
            Assert.AreEqual(0, manual.PlayCount);
            Assert.AreEqual(0, key.PlayCount, "no key was pressed");
        }

        [Test]
        public void ChainLightningBinder_IsBound_ToTheAnchorSocket_AndTheTargetSockets()
        {
            Transform caster = Character("Caster", Vector3.zero, (VfxSocket.HandRight, new Vector3(0.3f, 0.9f, 0.2f)));
            var targets = new List<Transform>();
            for (int i = 0; i < 3; i++)
                targets.Add(Character("Target" + i, new Vector3(0f, 0f, 3.5f * (i + 1)), (VfxSocket.Chest, new Vector3(0f, 1.35f, 0.1f))));
            VfxStation station = Station(Chain, VfxStationTrigger.Manual);
            station.Anchor = caster;
            station.AnchorSocket = VfxSocket.HandRight;
            station.TargetSocket = VfxSocket.Chest;
            station.Targets.AddRange(targets);

            VfxHandle handle = station.PlayNow();

            var binder = handle.GameObject.GetComponent<ChainLightningBinder>();
            Assert.AreEqual(VfxSocket.Find(caster, VfxSocket.HandRight).transform, binder.Source);
            Assert.AreEqual(3, binder.Targets.Count);
            for (int i = 0; i < 3; i++)
                Assert.AreEqual(VfxSocket.Find(targets[i], VfxSocket.Chest).transform, binder.Targets[i], "target " + i);
            Assert.AreEqual(3, binder.HopCount, "bound at once, not only in LateUpdate");
        }

        [Test]
        public void ChainLightningBinder_GetsTheValidTargets_UpToItsLimit()
        {
            VfxStation station = Station(Chain, VfxStationTrigger.Manual);
            station.Targets.Add(null);
            Transform hidden = Character("Hidden", Vector3.zero);
            hidden.gameObject.SetActive(false);
            station.Targets.Add(hidden);
            var valid = new List<Transform>();
            for (int i = 0; i < ChainLightningBinder.MaxTargets + 3; i++)
            {
                Transform t = Character("Target" + i, new Vector3(i, 0f, 0f));
                valid.Add(t);
                station.Targets.Add(t);
            }

            VfxHandle handle = station.PlayNow();

            var binder = handle.GameObject.GetComponent<ChainLightningBinder>();
            Assert.AreEqual(station.transform, binder.Source, "no anchor: the station itself");
            Assert.AreEqual(ChainLightningBinder.MaxTargets, binder.Targets.Count);
            for (int i = 0; i < ChainLightningBinder.MaxTargets; i++)
                Assert.AreEqual(valid[i], binder.Targets[i], "target " + i);
            Assert.AreEqual(ChainLightningBinder.MaxTargets, binder.HopCount);
        }

        [Test]
        public void PlayNow_OfAnEffectWithoutBinder_LeavesTheTargetsAlone()
        {
            VfxStation station = Station(Spark, VfxStationTrigger.Manual);
            station.Targets.Add(Character("Target", Vector3.one));
            VfxHandle handle = station.PlayNow();
            Assert.IsNull(handle.GameObject.GetComponentInChildren<ChainLightningBinder>(true));
            Assert.AreEqual(1, station.Targets.Count);
        }

        [Test]
        public void WasPressedThisFrame_IsFalse_ForNoKey_AndForAKeyNobodyPressed()
        {
            Assert.IsFalse(VfxStation.WasPressedThisFrame(KeyCode.None));
            Assert.IsFalse(VfxStation.WasPressedThisFrame(KeyCode.Space));
        }

#if CUVARA_VFX_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        [TestCase(KeyCode.A, "A")]
        [TestCase(KeyCode.M, "M")]
        [TestCase(KeyCode.Z, "Z")]
        [TestCase(KeyCode.Alpha0, "Digit0")]
        [TestCase(KeyCode.Alpha1, "Digit1")]
        [TestCase(KeyCode.Alpha9, "Digit9")]
        [TestCase(KeyCode.Keypad0, "Numpad0")]
        [TestCase(KeyCode.Keypad9, "Numpad9")]
        [TestCase(KeyCode.F1, "F1")]
        [TestCase(KeyCode.F12, "F12")]
        [TestCase(KeyCode.Space, "Space")]
        [TestCase(KeyCode.Return, "Enter")]
        [TestCase(KeyCode.KeypadEnter, "NumpadEnter")]
        [TestCase(KeyCode.Escape, "Escape")]
        [TestCase(KeyCode.Tab, "Tab")]
        [TestCase(KeyCode.Backspace, "Backspace")]
        [TestCase(KeyCode.UpArrow, "UpArrow")]
        [TestCase(KeyCode.RightArrow, "RightArrow")]
        public void InputSystemKey_MapsFromKeyCode(KeyCode keyCode, string expected)
        {
            Assert.IsTrue(VfxStation.TryMapKey(keyCode, out UnityEngine.InputSystem.Key mapped));
            Assert.AreEqual(expected, mapped.ToString());
        }

        [Test]
        public void InputSystemKey_DoesNotMapAMouseButton()
        {
            Assert.IsFalse(VfxStation.TryMapKey(KeyCode.Mouse0, out _));
        }
#endif
    }
}
