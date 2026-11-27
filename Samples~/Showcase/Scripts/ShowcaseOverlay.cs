using System.Collections.Generic;
using UnityEngine;

namespace Cuvara.VFX.Samples.Showcase
{
    /// <summary>
    /// The play-mode overlay of the Showcase scene: select a station, pick its effect from the
    /// <see cref="VfxLibrary"/> of the player, replay it, change the time scale, the lighting preset and
    /// the camera preset. It also draws the label of every station in the Game View.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Presets come only from the shared preset data: <see cref="SetLightingPreset"/> applies
    /// <see cref="VfxLightingPreset.Get"/> through <see cref="VfxLightingRig"/>, and
    /// <see cref="SetCameraPreset"/> places the camera with <see cref="VfxCameraPreset.Get"/> through
    /// <see cref="VfxCameraRig"/> around the selected station, the same code the capture stage uses.
    /// The overlay has no lighting or camera numbers of its own.
    /// </para>
    /// <para>
    /// Keys are read with <see cref="VfxStation.WasPressedThisFrame"/>, which uses the Input System or
    /// the legacy Input Manager, whichever the project has active, so this assembly compiles and runs
    /// with either or both. The panel is drawn with IMGUI and works with the mouse too.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cuvara/VFX/Showcase/Showcase Overlay")]
    public sealed class ShowcaseOverlay : MonoBehaviour
    {
        /// <summary>The time scales the overlay offers, slowest first.</summary>
        public static readonly float[] TimeScales = { 0.1f, 0.25f, 0.5f, 1f, 2f };

        const KeyCode NextStationKey = KeyCode.Tab;
        const KeyCode PreviousEffectKey = KeyCode.Q;
        const KeyCode NextEffectKey = KeyCode.E;
        const KeyCode ReplayKey = KeyCode.Space;
        const KeyCode TimeScaleKey = KeyCode.T;
        const KeyCode LightingKey = KeyCode.L;
        const KeyCode CameraKey = KeyCode.C;
        const KeyCode PanelKey = KeyCode.H;
        const float PanelHeight = 118f;
        const float LabelMargin = 12f;

        [SerializeField, Tooltip("The stations, in the order of the number keys 1 to 9.")]
        List<VfxStation> stations = new List<VfxStation>();

        [SerializeField, Tooltip("Player whose library lists the effects; empty: the first station's player, else the first in the scene.")]
        VfxPlayer player;

        [SerializeField, Tooltip("Lighting rig the lighting presets are applied to.")]
        VfxLightingRig lightingRig;

        [SerializeField, Tooltip("Camera rig placed around the selected station.")]
        VfxCameraRig cameraRig;

        [SerializeField, Tooltip("Index of the station selected on start.")]
        int selectedIndex;

        [SerializeField, Tooltip("Camera preset applied on start: gameplay, close or side.")]
        string cameraPreset = VfxCameraPreset.DefaultName;

        [SerializeField, Tooltip("Height of a station label above the station, in metres.")]
        float labelHeight = 3.2f;

        [SerializeField, Tooltip("Labels of stations other than the selected one show up to this distance from the camera, in metres.")]
        float labelMaxDistance = 12f;

        [SerializeField, Tooltip("Show the panel on start (H toggles it); the station labels always show.")]
        bool panelVisible = true;

        float initialTimeScale = 1f;
        GUIStyle panelStyle;
        GUIStyle buttonStyle;
        GUIStyle headerStyle;
        GUIStyle textStyle;
        GUIStyle valueStyle;
        GUIStyle labelStyle;
        GUIStyle selectedLabelStyle;
        Texture2D labelBackground;
        Texture2D selectedLabelBackground;

        /// <summary>The stations, in key order.</summary>
        public IReadOnlyList<VfxStation> Stations => stations;

        /// <summary>Index of the selected station, or -1 when there is none.</summary>
        public int SelectedIndex => stations.Count == 0 ? -1 : Mathf.Clamp(selectedIndex, 0, stations.Count - 1);

        /// <summary>The selected station, or null.</summary>
        public VfxStation SelectedStation => SelectedIndex < 0 ? null : stations[SelectedIndex];

        /// <summary>The player whose library the effect picker lists.</summary>
        public VfxPlayer Player => ResolvePlayer();

        /// <summary>The lighting rig.</summary>
        public VfxLightingRig LightingRig => lightingRig;

        /// <summary>The camera rig.</summary>
        public VfxCameraRig CameraRig => cameraRig;

        /// <summary>Name of the lighting preset applied last.</summary>
        public string LightingPreset => lightingRig != null ? lightingRig.Preset : null;

        /// <summary>Name of the camera preset applied last.</summary>
        public string CameraPreset => cameraPreset;

        /// <summary>True while the panel shows.</summary>
        public bool PanelVisible
        {
            get => panelVisible;
            set => panelVisible = value;
        }

        /// <summary>Selects the station at <paramref name="index"/> and places the camera around it with the current camera preset.</summary>
        /// <returns>False when the index is out of range.</returns>
        public bool SelectStation(int index)
        {
            if (index < 0 || index >= stations.Count || stations[index] == null) return false;
            selectedIndex = index;
            return SetCameraPreset(cameraPreset);
        }

        /// <summary>Sets the effect of the selected station (null or empty: no effect) and replays it.</summary>
        /// <returns>False when there is no station, or the id is not in the library.</returns>
        public bool SetEffect(string effectId)
        {
            VfxStation station = SelectedStation;
            if (station == null) return false;
            if (!string.IsNullOrEmpty(effectId))
            {
                VfxLibrary library = ResolvePlayer() != null ? ResolvePlayer().Library : null;
                if (library == null || !library.Contains(effectId)) return false;
            }
            station.EffectId = effectId;
            Replay();
            return true;
        }

        /// <summary>
        /// Steps the selected station's effect through "no effect" and the library's ids in order
        /// (<paramref name="step"/> 1 is the next, -1 the previous) and replays it.
        /// </summary>
        public void StepEffect(int step)
        {
            VfxStation station = SelectedStation;
            VfxPlayer resolved = ResolvePlayer();
            if (station == null || resolved == null || resolved.Library == null) return;
            IReadOnlyList<VfxLibrary.Entry> entries = resolved.Library.Entries;
            int count = entries.Count + 1; // slot 0 is "no effect"
            int current = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].Id == station.EffectId)
                {
                    current = i + 1;
                    break;
                }
            }
            int next = ((current + step) % count + count) % count;
            SetEffect(next == 0 ? null : entries[next - 1]?.Id);
        }

        /// <summary>Plays the selected station's effect now and restarts its interval from this play.</summary>
        public void Replay()
        {
            VfxStation station = SelectedStation;
            if (station == null || string.IsNullOrEmpty(station.EffectId)) return;
            if (station.Trigger == VfxStationTrigger.Interval)
                station.ResetTimer(); // its next update plays, then the interval runs from there
            else
                station.PlayNow();
        }

        /// <summary>Sets <see cref="Time.timeScale"/> (clamped to 0.01..10); leaving play mode restores the value of the start.</summary>
        public void SetTimeScale(float scale) => Time.timeScale = Mathf.Clamp(scale, 0.01f, 10f);

        /// <summary>Applies the lighting preset <paramref name="presetName"/> (day, dusk or night) from <see cref="VfxLightingPreset.Get"/>.</summary>
        /// <returns>False when there is no such preset or no lighting rig.</returns>
        public bool SetLightingPreset(string presetName)
        {
            VfxLightingPreset lighting = VfxLightingPreset.Get(presetName);
            if (lighting == null || lightingRig == null) return false;
            lightingRig.ApplyPreset(lighting);
            return true;
        }

        /// <summary>
        /// Places the camera with the camera preset <paramref name="presetName"/> (gameplay, close or
        /// side) from <see cref="VfxCameraPreset.Get"/> around the selected station.
        /// </summary>
        /// <returns>False when there is no such preset, no camera rig or no station.</returns>
        public bool SetCameraPreset(string presetName)
        {
            VfxCameraPreset preset = VfxCameraPreset.Get(presetName);
            VfxStation station = SelectedStation;
            if (preset == null || cameraRig == null || station == null) return false;
            cameraRig.Focus = station.transform;
            cameraRig.FocusOffset = Vector3.zero;
            if (!cameraRig.ApplyPreset(preset)) return false;
            cameraPreset = preset.name;
            return true;
        }

        void Awake() => initialTimeScale = Time.timeScale;

        void Start()
        {
            if (lightingRig == null) lightingRig = FindAnyObjectByType<VfxLightingRig>();
            if (cameraRig == null) cameraRig = FindAnyObjectByType<VfxCameraRig>();
            SelectStation(SelectedIndex);
        }

        void OnDestroy()
        {
            Time.timeScale = initialTimeScale;
            if (labelBackground != null) Destroy(labelBackground);
            if (selectedLabelBackground != null) Destroy(selectedLabelBackground);
        }

        void Update()
        {
            for (int i = 0; i < stations.Count && i < 9; i++)
                if (VfxStation.WasPressedThisFrame(KeyCode.Alpha1 + i)) SelectStation(i);
            if (VfxStation.WasPressedThisFrame(NextStationKey) && stations.Count > 0)
                SelectStation((SelectedIndex + 1) % stations.Count);
            if (VfxStation.WasPressedThisFrame(NextEffectKey)) StepEffect(1);
            if (VfxStation.WasPressedThisFrame(PreviousEffectKey)) StepEffect(-1);
            if (VfxStation.WasPressedThisFrame(ReplayKey)) Replay();
            if (VfxStation.WasPressedThisFrame(TimeScaleKey)) SetTimeScale(TimeScales[(TimeScaleIndex() + 1) % TimeScales.Length]);
            if (VfxStation.WasPressedThisFrame(LightingKey)) SetLightingPreset(NextName(VfxLightingPreset.Names, LightingPreset));
            if (VfxStation.WasPressedThisFrame(CameraKey)) SetCameraPreset(NextName(VfxCameraPreset.Names, cameraPreset));
            if (VfxStation.WasPressedThisFrame(PanelKey)) panelVisible = !panelVisible;
        }

        VfxPlayer ResolvePlayer()
        {
            if (player != null) return player;
            for (int i = 0; i < stations.Count; i++)
            {
                if (stations[i] != null && stations[i].Player != null)
                {
                    player = stations[i].Player;
                    return player;
                }
            }
            player = FindAnyObjectByType<VfxPlayer>();
            return player;
        }

        int TimeScaleIndex()
        {
            int best = 0;
            for (int i = 1; i < TimeScales.Length; i++)
                if (Mathf.Abs(TimeScales[i] - Time.timeScale) < Mathf.Abs(TimeScales[best] - Time.timeScale)) best = i;
            return best;
        }

        static string NextName(IReadOnlyList<string> names, string current)
        {
            for (int i = 0; i < names.Count; i++)
                if (names[i] == current) return names[(i + 1) % names.Count];
            return names[0];
        }

        // ------------------------------------------------------------------ IMGUI

        void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.5f, 3f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            DrawLabels(scale);
            if (panelVisible) DrawPanel(scale);
            else GUI.Label(new Rect(16f, Screen.height / scale - 40f, 360f, 30f), "H: show the Showcase panel", textStyle);
            GUI.matrix = previous;
        }

        void DrawLabels(float scale)
        {
            Camera cam = cameraRig != null && cameraRig.TargetCamera != null ? cameraRig.TargetCamera : Camera.main;
            if (cam == null) return;
            for (int i = 0; i < stations.Count; i++)
            {
                VfxStation station = stations[i];
                if (station == null) continue;
                Vector3 screen = cam.WorldToScreenPoint(station.transform.position + Vector3.up * labelHeight);
                // Far stations stand behind the selected one: their labels would only stack up.
                if (screen.z <= 0f || (i != SelectedIndex && screen.z > labelMaxDistance)) continue;
                string effect = string.IsNullOrEmpty(station.EffectId) ? "no effect" : station.EffectId;
                string text = $"{i + 1}  {station.Label}\n{effect}";
                GUIStyle style = i == SelectedIndex ? selectedLabelStyle : labelStyle;
                Vector2 size = style.CalcSize(new GUIContent(text));
                // IMGUI's y runs down; the GUI matrix scales screen pixels by `scale`.
                var center = new Vector2(screen.x / scale, (Screen.height - screen.y) / scale);
                float x = center.x - size.x * 0.5f;
                float y = center.y - size.y;
                if (i == SelectedIndex)
                {
                    // Keep the selected station's label on screen (the close preset frames below it).
                    x = Mathf.Clamp(x, LabelMargin, Screen.width / scale - size.x - LabelMargin);
                    y = Mathf.Clamp(y, LabelMargin, Screen.height / scale - PanelHeight - 3f * LabelMargin - size.y);
                }
                GUI.Label(new Rect(x, y, size.x, size.y), text, style);
            }
        }

        // A bar along the bottom edge: the stations stand in the middle of every camera preset's frame.
        void DrawPanel(float scale)
        {
            const float margin = LabelMargin, height = PanelHeight;
            float width = Screen.width / scale - 2f * margin;
            GUILayout.BeginArea(new Rect(margin, Screen.height / scale - height - margin, width, height), panelStyle);

            VfxStation station = SelectedStation;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Cuvara VFX Showcase", headerStyle);
            GUILayout.Space(18f);
            GUILayout.Label("Station (1-" + Mathf.Min(stations.Count, 9) + ", Tab)", textStyle);
            for (int i = 0; i < stations.Count; i++)
            {
                if (stations[i] == null) continue;
                bool on = GUILayout.Toggle(i == SelectedIndex, (i + 1).ToString(), buttonStyle, GUILayout.Width(40f));
                if (on && i != SelectedIndex) SelectStation(i);
            }
            GUILayout.Label(station != null ? station.Label : "no station", textStyle, GUILayout.Width(190f));
            GUILayout.Space(18f);
            GUILayout.Label("Effect (Q, E)", textStyle);
            if (GUILayout.Button("<", buttonStyle, GUILayout.Width(34f))) StepEffect(-1);
            string effect = station == null || string.IsNullOrEmpty(station.EffectId) ? "no effect" : station.EffectId;
            GUILayout.Label(effect, valueStyle, GUILayout.Width(150f));
            if (GUILayout.Button(">", buttonStyle, GUILayout.Width(34f))) StepEffect(1);
            if (GUILayout.Button("Replay (Space)", buttonStyle, GUILayout.Width(140f))) Replay();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Time scale (T)", textStyle);
            int current = TimeScaleIndex();
            for (int i = 0; i < TimeScales.Length; i++)
            {
                bool selected = i == current && Mathf.Approximately(Time.timeScale, TimeScales[i]);
                if (GUILayout.Toggle(selected, TimeScales[i].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), buttonStyle, GUILayout.Width(52f)) && !selected)
                    SetTimeScale(TimeScales[i]);
            }
            GUILayout.Space(18f);
            GUILayout.Label("Lighting (L)", textStyle);
            PresetRow(VfxLightingPreset.Names, LightingPreset, true);
            GUILayout.Space(18f);
            GUILayout.Label("Camera (C)", textStyle);
            PresetRow(VfxCameraPreset.Names, cameraPreset, false);
            GUILayout.FlexibleSpace();
            GUILayout.Label("H hides the panel", textStyle);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void PresetRow(IReadOnlyList<string> names, string current, bool lighting)
        {
            for (int i = 0; i < names.Count; i++)
            {
                bool on = GUILayout.Toggle(names[i] == current, names[i], buttonStyle, GUILayout.Width(92f));
                if (!on || names[i] == current) continue;
                if (lighting) SetLightingPreset(names[i]);
                else SetCameraPreset(names[i]);
            }
        }

        void EnsureStyles()
        {
            if (panelStyle != null) return;
            labelBackground = SolidTexture(new Color(0.05f, 0.06f, 0.08f, 0.72f));
            selectedLabelBackground = SolidTexture(new Color(0.55f, 0.38f, 0.08f, 0.85f));
            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(14, 14, 10, 12) };
            panelStyle.normal.background = labelBackground;
            headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 21, fontStyle = FontStyle.Bold, wordWrap = false };
            headerStyle.normal.textColor = Color.white;
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = false };
            textStyle.normal.textColor = new Color(0.80f, 0.82f, 0.86f);
            valueStyle = new GUIStyle(textStyle) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            valueStyle.normal.textColor = Color.white;
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                richText = false,
                padding = new RectOffset(12, 12, 6, 6),
            };
            labelStyle.normal.textColor = Color.white;
            labelStyle.normal.background = labelBackground;
            selectedLabelStyle = new GUIStyle(labelStyle);
            selectedLabelStyle.normal.background = selectedLabelBackground;
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16 };
        }

        static Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
