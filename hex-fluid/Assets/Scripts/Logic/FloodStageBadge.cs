using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// The flood stage, written on the table itself - like the writing on an
    /// Inscryption game board - instead of a sticker on the screen. "Cloudburst!" when
    /// the source wells up, "Flooding commenced" when the water runs, then "Goal
    /// reached!", "City flooded!" or "Goal not reached!". It listens to
    /// WaterSimulation.StageChanged and pops in again (in choppy, stop-motion steps,
    /// see StickerWobble) on every change.
    ///
    /// Look: the sticker is turned black and white and etched into the wood
    /// (Resources/EtchedSprite.shader: two tones, a little scratch grain, the table
    /// showing through). The caption is etched too - a carved dark edge under the
    /// letters - in its own muted chalk palette, one colour per stage.
    ///
    /// It lies flat on the table and turns to read upright from the camera, or turns
    /// with its placeholder ('spot', 'turnWithSpot').
    ///
    /// Placement: give it a placeholder in 'spot' and it lies right there, at the
    /// placeholder's height too. Otherwise on the table just above (on screen) the
    /// button that starts the water, found automatically - or above whatever 'anchor'
    /// you give it. Without either it
    /// sits beside the board ('followBoard'), or, with that off, exactly where this
    /// object is. Its height follows the table surface under it.
    /// WaterSimulation creates one automatically if the scene has none; add your own
    /// to the scene instead if you want to tweak it in the inspector.
    /// </summary>
    public class FloodStageBadge : MonoBehaviour
    {
        [Serializable]
        public struct StageLook
        {
            public FloodStage stage;
            [Tooltip("Caption under the sticker. Empty = hide the badge in this stage.")]
            public string label;
            [Tooltip("Sticker for this stage. Empty = the default sticker.")]
            public Sprite sticker;
            public Color labelColor;
        }

        [Header("References")]
        [Tooltip("The simulation to follow. Empty = the scene's WaterSimulation.")]
        public WaterSimulation simulation;

        [Tooltip("The board it sits beside. Empty = the scene's HexBoard.")]
        public HexBoard board;

        [Header("Placement on the table")]
        [Tooltip("Placeholder: the badge lies flat exactly where this object is, at its height too (the point between sticker and caption). Empty = placed by 'anchor' below.")]
        public Transform spot;

        [Tooltip("With a spot: ON = it turns with the placeholder (spin the placeholder around Y; its blue Z arrow is the top of the badge). OFF = it always reads upright from the camera.")]
        public bool turnWithSpot = true;

        [Tooltip("No spot: sit just above this object on screen. Empty = the button that starts the water (found automatically).")]
        public Transform anchor;

        [Tooltip("From the anchor's centre to the point between sticker and caption, in table units: X = towards the right of the screen, Y = towards the top.")]
        public Vector2 anchorOffset = new Vector2(0f, 0.85f);

        [Tooltip("No anchor: ON = sit beside the board at 'boardOffset'. OFF = sit where this object is.")]
        public bool followBoard = true;

        [Tooltip("From the board's centre, in table units: X = towards the right of the screen, Y = towards the top.")]
        public Vector2 boardOffset = new Vector2(-4.2f, 0.6f);

        [Tooltip("Lift above the table surface, so it doesn't flicker into the wood.")]
        public float heightOffset = 0.01f;

        [Header("Size (table units)")]
        [Tooltip("Height of the sticker on the table.")]
        public float stickerSize = 1.4f;

        [Tooltip("Caption size (TextMeshPro 3D font size; ~10 = 1 unit tall).")]
        public float labelSize = 3f;

        [Tooltip("Gap between the bottom of the sticker and the top of the caption.")]
        public float labelGap = 0.04f;

        [Header("Etched look")]
        [Tooltip("Sticker used for every stage that has none of its own. Empty = Resources/WaterSticker.")]
        public Sprite defaultSticker;

        [Tooltip("Light parts of the sticker.")]
        public Color stickerInk = new Color(0.90f, 0.87f, 0.80f, 1f);

        [Tooltip("Dark parts of the sticker.")]
        public Color stickerShadow = new Color(0.06f, 0.05f, 0.05f, 1f);

        [Range(0.5f, 6f)] public float stickerContrast = 2.2f;
        [Range(0f, 1f)] public float stickerOpacity = 0.85f;
        [Tooltip("How much the scratch grain eats into the marks.")]
        [Range(0f, 1f)] public float scratchGrain = 0.35f;

        [Tooltip("Optional font for the caption (something hand-drawn / scratchy suits it). Empty = TMP default.")]
        public TMP_FontAsset labelFont;

        [Tooltip("The carved dark edge under the caption's letters.")]
        public Color carveColor = new Color(0.03f, 0.02f, 0.02f, 0.85f);

        [Tooltip("Caption opacity: below 1 lets the wood show through a little.")]
        [Range(0f, 1f)] public float labelOpacity = 0.9f;

        [Header("Stages")]
        public StageLook[] stages =
        {
            new StageLook { stage = FloodStage.Calm,        label = "",                   labelColor = new Color(0.86f, 0.83f, 0.76f) },
            new StageLook { stage = FloodStage.Cloudburst,  label = "Cloudburst!",        labelColor = new Color(0.86f, 0.83f, 0.76f) }, // bone chalk
            new StageLook { stage = FloodStage.Flooding,    label = "Flooding commenced", labelColor = new Color(0.60f, 0.68f, 0.72f) }, // slate
            new StageLook { stage = FloodStage.Contained,   label = "Goal reached!",      labelColor = new Color(0.66f, 0.74f, 0.50f) }, // moss
            new StageLook { stage = FloodStage.CityFlooded, label = "City flooded!",      labelColor = new Color(0.70f, 0.20f, 0.16f) }, // dried blood
            new StageLook { stage = FloodStage.GoalMissed,  label = "Goal not reached!",  labelColor = new Color(0.80f, 0.58f, 0.28f) }, // ochre
        };

        private Transform _flat;          // lies on the table, turned to read upright
        private GameObject _content;      // on/off per stage
        private SpriteRenderer _sticker;
        private TextMeshPro _label;
        private StickerWobble _wobble;
        private bool _searched;           // looked for the Start button already

        private void Start()
        {
            if (simulation == null) simulation = FindFirstObjectByType<WaterSimulation>();
            if (board == null) board = FindFirstObjectByType<HexBoard>();
            if (defaultSticker == null) defaultSticker = Resources.Load<Sprite>("WaterSticker");
            Build();

            if (simulation == null) { Show(FloodStage.Calm); return; }
            simulation.StageChanged += Show;
            Show(simulation.Stage);
        }

        private void OnDestroy()
        {
            if (simulation != null) simulation.StageChanged -= Show;
        }

        /// <summary>Shows the sticker and caption for a stage (hidden if it has no caption).</summary>
        public void Show(FloodStage stage)
        {
            if (_content == null) return;
            StageLook look = LookFor(stage);
            bool visible = !string.IsNullOrEmpty(look.label);
            _content.SetActive(visible);
            if (!visible) return;

            Sprite sprite = look.sticker != null ? look.sticker : defaultSticker;
            _sticker.sprite = sprite;
            _sticker.enabled = sprite != null;
            if (sprite != null)
            {
                // Scale to stickerSize and stand its bottom edge on the hinge, whatever its pivot.
                Bounds b = sprite.bounds;
                float s = stickerSize / Mathf.Max(0.001f, b.size.y);
                _sticker.transform.localScale = new Vector3(s, s, 1f);
                _sticker.transform.localPosition = new Vector3(-b.center.x * s, -b.min.y * s, 0f);
            }

            _label.text = look.label;
            Color c = look.labelColor.a > 0f ? look.labelColor : Color.white;
            c.a = labelOpacity;
            _label.color = c;
            _wobble.Pop();
        }

        private StageLook LookFor(FloodStage stage)
        {
            foreach (var s in stages)
                if (s.stage == stage) return s;
            return new StageLook { stage = stage, label = stage.ToString(), labelColor = new Color(0.86f, 0.83f, 0.76f) };
        }

        // ---- On the table ----------------------------------------------------------

        private void LateUpdate()
        {
            if (_flat == null) return;
            Vector3 up = board != null ? board.transform.up : Vector3.up;
            ScreenAxes(up, out Vector3 right, out Vector3 screenUp);

            if (spot != null)
            {
                _flat.position = spot.position;                          // your placeholder, its height as you set it
            }
            else
            {
                Transform at = Anchor();
                if (at != null)
                    transform.position = at.position + right * anchorOffset.x + screenUp * anchorOffset.y;
                else if (followBoard && board != null)
                    transform.position = board.transform.position + right * boardOffset.x + screenUp * boardOffset.y;

                _flat.position = OnTable(transform.position, up) + up * heightOffset;
            }
            Vector3 top = screenUp;                                      // reading upright from the camera...
            if (spot != null && turnWithSpot)
            {
                Vector3 spotTop = Vector3.ProjectOnPlane(spot.forward, up);
                if (spotTop.sqrMagnitude > 1e-4f) top = spotTop.normalized; // ...or turned like your placeholder
            }
            _flat.rotation = Quaternion.LookRotation(-up, top);          // lying flat
        }

        /// <summary>Marks the placeholder in the Scene view, so you can see where the badge will lie.</summary>
        private void OnDrawGizmos()
        {
            if (spot == null) return;
            Vector3 top = Vector3.ProjectOnPlane(turnWithSpot ? spot.forward : Vector3.forward, Vector3.up);
            if (top.sqrMagnitude < 1e-4f) top = Vector3.forward;
            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.matrix = Matrix4x4.TRS(spot.position, Quaternion.LookRotation(top), Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0f, 0f, stickerSize * 0.5f), new Vector3(stickerSize, 0f, stickerSize));  // the sticker, above the point
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, 0f, stickerSize * 1.2f));
        }

        /// <summary>
        /// 'anchor', or else the 3D button whose click starts this simulation (the
        /// game's Start button; not a main-menu button, those fly away).
        /// </summary>
        private Transform Anchor()
        {
            if (anchor != null) return anchor;
            if (_searched || simulation == null) return null;
            _searched = true;

            var menuButtons = new HashSet<PressableButton3D>();
            foreach (var menu in FindObjectsByType<MainMenu>(FindObjectsSortMode.None))
            {
                if (menu.play3D != null) menuButtons.Add(menu.play3D);
                if (menu.quit3D != null) menuButtons.Add(menu.quit3D);
            }

            foreach (var button in FindObjectsByType<PressableButton3D>(FindObjectsSortMode.None))
            {
                if (menuButtons.Contains(button)) continue;
                for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                    if (button.onClick.GetPersistentTarget(i) == simulation &&
                        button.onClick.GetPersistentMethodName(i) == nameof(WaterSimulation.StartSimulation))
                        return anchor = button.transform;
            }
            return null;
        }

        /// <summary>
        /// The point on the table surface under 'p': the lowest thing a short ray straight
        /// down hits (so props lying on the table don't lift it, and it's too short to
        /// reach a floor under the table). Unchanged if nothing is hit.
        /// </summary>
        private static Vector3 OnTable(Vector3 p, Vector3 up)
        {
            var hits = Physics.RaycastAll(p + up * 0.5f, -up, 1.5f, ~0, QueryTriggerInteraction.Ignore);
            float far = -1f;
            Vector3 best = p;
            foreach (var h in hits)
                if (h.distance > far) { far = h.distance; best = h.point; }
            return best;
        }

        /// <summary>The screen's right and up directions, laid flat onto the table.</summary>
        private static void ScreenAxes(Vector3 up, out Vector3 right, out Vector3 screenUp)
        {
            Camera cam = Camera.main;
            Vector3 camUp = cam != null ? cam.transform.up : Vector3.forward;
            Vector3 camFwd = cam != null ? cam.transform.forward : Vector3.down;
            screenUp = Vector3.ProjectOnPlane(camUp, up);
            if (screenUp.sqrMagnitude < 1e-4f) screenUp = Vector3.ProjectOnPlane(camFwd, up);   // camera looking straight along the table
            if (screenUp.sqrMagnitude < 1e-4f) screenUp = Vector3.forward;
            screenUp.Normalize();
            right = Vector3.Cross(up, screenUp);
        }

        private void Build()
        {
            _flat = new GameObject("FloodStage (on table)").transform;
            _flat.SetParent(transform, false);

            // Everything inside sways together, hinged between the sticker and the caption:
            // the sticker stands on this point (see Show), the caption hangs just under it.
            var swing = new GameObject("Wobble").transform;
            swing.SetParent(_flat, false);
            _content = swing.gameObject;

            var sticker = new GameObject("Sticker").transform;
            sticker.SetParent(swing, false);
            _sticker = sticker.gameObject.AddComponent<SpriteRenderer>();
            Material etch = EtchMaterial();
            if (etch != null) _sticker.sharedMaterial = etch;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(swing, false);
            var rect = (RectTransform)labelGo.transform;
            rect.pivot = new Vector2(0.5f, 1f);                             // its top edge...
            rect.sizeDelta = new Vector2(stickerSize * 6f, labelSize * 0.2f);
            rect.localPosition = new Vector3(0f, -labelGap, 0f);            // ...right under the sticker
            _label = labelGo.AddComponent<TextMeshPro>();
            if (labelFont != null) _label.font = labelFont;
            else if (TMP_Settings.defaultFontAsset == null)
                _label.font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
            _label.fontSize = labelSize;
            _label.fontStyle = FontStyles.Bold;
            _label.alignment = TextAlignmentOptions.Top;
            Carve(_label);

            _wobble = swing.gameObject.AddComponent<StickerWobble>();
            _wobble.faceCamera = false;      // it turns in the table's plane, not towards the camera
            _wobble.swingAngle = 4f;

            LateUpdate();                    // in place before the first frame draws
        }

        /// <summary>
        /// The carved look for the caption: a dark copy of the letters just under and to
        /// one side (TMP underlay), like the edge of a groove cut into the wood.
        /// </summary>
        private void Carve(TextMeshPro text)
        {
            Material m = text.fontMaterial;                  // its own copy
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, carveColor);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.35f);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.35f);
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.1f);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.15f);
            text.fontMaterial = m;
        }

        private Material EtchMaterial()
        {
            Shader shader = Resources.Load<Shader>("EtchedSprite");
            if (shader == null) shader = Shader.Find("HexFluid/EtchedSprite");
            if (shader == null)
            {
                Debug.LogWarning("FloodStageBadge: Resources/EtchedSprite.shader is missing; the sticker is drawn in colour.", this);
                return null;
            }

            var m = new Material(shader) { name = "EtchedSprite (FloodStageBadge)" };
            m.SetColor("_Ink", stickerInk);
            m.SetColor("_Shadow", stickerShadow);
            m.SetFloat("_Contrast", stickerContrast);
            m.SetFloat("_Opacity", stickerOpacity);
            m.SetFloat("_Grain", scratchGrain);
            return m;
        }
    }
}
