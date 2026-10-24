#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HexFluid.EditorTools
{
    /// <summary>
    /// The level generator tool. Open via: Window > HexFluid > Level Editor.
    ///
    /// Workflow:
    ///   1. Assign (or create) a LevelData asset at the top.
    ///   2. Pick a Paint Mode in the toolbar.
    ///   3. Left-click hexes in the 2D grid to paint tiles / roles / resources.
    ///   4. Click an EDGE (the line between two hexes) to toggle barricades.
    ///   5. Hit Validate to confirm the level is playable, then Save.
    ///
    /// Everything writes straight into the LevelData asset, so the same asset
    /// you edit here is what HexBoard plays at runtime. Add new paint modes by
    /// extending the PaintMode enum and the ApplyPaint switch.
    /// </summary>
    public class HexLevelEditorWindow : EditorWindow
    {
        private LevelData level;

        private enum PaintMode
        {
            AddRemoveTile,
            SetRole,
            SetResource,
            SetModel,
            Barricade,
        }

        private PaintMode mode = PaintMode.AddRemoveTile;

        // paint parameters
        private TileRole paintRole = TileRole.Source;
        private ResourceType paintResource = ResourceType.Wood;
        private int paintResourceAmount = 3;
        private int paintModelIndex = -1;

        // view
        private Vector2 pan = new Vector2(320, 260);
        private float cellSize = 26f; // pixels, editor-space hex radius
        private Vector2 scroll;
        private readonly List<string> _problems = new();

        [MenuItem("Window/HexFluid/Level Editor")]
        public static void Open()
        {
            var w = GetWindow<HexLevelEditorWindow>("Hex Level Editor");
            w.minSize = new Vector2(640, 480);
        }

        void OnGUI()
        {
            DrawToolbar();
            if (level == null)
            {
                EditorGUILayout.HelpBox(
                    "Assign a LevelData asset to begin. " +
                    "Create one via Assets > Create > HexFluid > Level.",
                    MessageType.Info);
                return;
            }

            DrawPaintControls();
            DrawCanvas();
            HandleCanvasInput();
            DrawFooter();
        }

        // ---------------------------------------------------------------

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("Level:", GUILayout.Width(40));
            var newLevel = (LevelData)EditorGUILayout.ObjectField(level, typeof(LevelData), false);
            if (newLevel != level) { level = newLevel; if (level) level.BuildLookups(); }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(70)))
                RunValidation();
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(50)))
                SaveLevel();
            if (GUILayout.Button("Clear Board", EditorStyles.toolbarButton, GUILayout.Width(90)))
                if (EditorUtility.DisplayDialog("Clear Board", "Remove all tiles and edges?", "Yes", "Cancel"))
                    ClearBoard();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPaintControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            mode = (PaintMode)GUILayout.Toolbar((int)mode, new[]
            {
                "Tile", "Role", "Resource", "Model", "Barricade"
            });

            switch (mode)
            {
                case PaintMode.AddRemoveTile:
                    EditorGUILayout.LabelField("Left-click empty = add tile. Left-click tile = remove.");
                    break;
                case PaintMode.SetRole:
                    paintRole = (TileRole)EditorGUILayout.EnumPopup("Role to paint", paintRole);
                    break;
                case PaintMode.SetResource:
                    paintResource = (ResourceType)EditorGUILayout.EnumPopup("Resource", paintResource);
                    paintResourceAmount = EditorGUILayout.IntField("Amount", paintResourceAmount);
                    break;
                case PaintMode.SetModel:
                    EditorGUILayout.LabelField($"Model palette size: {level.tileModels.Count} (-1 = default)");
                    paintModelIndex = EditorGUILayout.IntSlider("Model index", paintModelIndex, -1,
                        Mathf.Max(0, level.tileModels.Count - 1));
                    break;
                case PaintMode.Barricade:
                    EditorGUILayout.LabelField("Click the line BETWEEN two tiles to toggle a barricade.");
                    break;
            }

            EditorGUILayout.BeginHorizontal();
            cellSize = EditorGUILayout.Slider("Zoom", cellSize, 14f, 50f);
            if (GUILayout.Button("Recenter", GUILayout.Width(80)))
                pan = new Vector2(position.width * 0.5f, position.height * 0.5f);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ---------------------------------------------------------------
        // Canvas drawing
        // ---------------------------------------------------------------

        private Rect _canvasRect;

        private void DrawCanvas()
        {
            _canvasRect = GUILayoutUtility.GetRect(position.width, position.height - 190);
            GUI.Box(_canvasRect, GUIContent.none);

            Handles.BeginGUI();

            // Draw existing tiles.
            foreach (var t in level.tiles)
            {
                Vector2 c = HexToScreen(t.coord);
                if (!_canvasRect.Contains(c)) continue;
                DrawHex(c, cellSize, RoleColor(t.role), true);

                // resource dot
                if (t.resource != ResourceType.None)
                    EditorGUI.DrawRect(new Rect(c.x - 4, c.y - 4, 8, 8), Color.yellow);

                // coord label (small)
                var st = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = Color.black;
                GUI.Label(new Rect(c.x - 20, c.y - 8, 40, 16), $"{t.coord.q},{t.coord.r}", st);
            }

            // Draw barricades/walls on edges.
            foreach (var e in level.edges)
            {
                Vector2 ca = HexToScreen(e.a);
                Vector2 cb = HexToScreen(e.b);
                Vector2 mid = (ca + cb) * 0.5f;
                Handles.color = e.state == EdgeState.Wall ? Color.gray : new Color(0.55f, 0.27f, 0.07f);
                // draw a thick perpendicular bar at the shared edge
                Vector2 dir = (cb - ca).normalized;
                Vector2 perp = new Vector2(-dir.y, dir.x) * (cellSize * 0.5f);
                Handles.DrawAAPolyLine(5f, mid - perp, mid + perp);
            }

            Handles.EndGUI();
        }

        private void DrawHex(Vector2 center, float radius, Color fill, bool outline)
        {
            Vector3[] pts = new Vector3[7];
            for (int i = 0; i < 6; i++)
            {
                // flat-top: first corner at 0 deg (flat edges on top and bottom)
                float ang = Mathf.Deg2Rad * (60 * i);
                pts[i] = new Vector3(center.x + radius * Mathf.Cos(ang),
                                     center.y + radius * Mathf.Sin(ang), 0);
            }
            pts[6] = pts[0];

            Handles.color = fill;
            Handles.DrawAAConvexPolygon(pts);
            if (outline)
            {
                Handles.color = Color.black;
                Handles.DrawAAPolyLine(2f, pts);
            }
        }

        private Color RoleColor(TileRole role) => role switch
        {
            TileRole.Source => new Color(0.3f, 0.6f, 1f),
            TileRole.Goal => new Color(0.3f, 1f, 0.5f),
            TileRole.City => new Color(1f, 0.5f, 0.3f),
            TileRole.Resource => new Color(0.8f, 0.8f, 0.4f),
            _ => new Color(0.85f, 0.85f, 0.85f),
        };

        // ---------------------------------------------------------------
        // Screen <-> hex mapping (editor-space, pixels)
        // ---------------------------------------------------------------

        private Vector2 HexToScreen(HexCoord c)
        {
            float x = cellSize * (1.5f * c.q);
            float y = cellSize * (Mathf.Sqrt(3f) / 2f * c.q + Mathf.Sqrt(3f) * c.r);
            return new Vector2(_canvasRect.x + pan.x + x, _canvasRect.y + pan.y + y);
        }

        private HexCoord ScreenToHex(Vector2 p)
        {
            float lx = p.x - _canvasRect.x - pan.x;
            float ly = p.y - _canvasRect.y - pan.y;
            float qf = (2f / 3f * lx) / cellSize;
            float rf = (-1f / 3f * lx + Mathf.Sqrt(3f) / 3f * ly) / cellSize;
            return RoundHex(qf, rf);
        }

        private HexCoord RoundHex(float qf, float rf)
        {
            float sf = -qf - rf;
            int rq = Mathf.RoundToInt(qf), rr = Mathf.RoundToInt(rf), rs = Mathf.RoundToInt(sf);
            float dq = Mathf.Abs(rq - qf), dr = Mathf.Abs(rr - rf), ds = Mathf.Abs(rs - sf);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new HexCoord(rq, rr);
        }

        // ---------------------------------------------------------------
        // Input
        // ---------------------------------------------------------------

        private void HandleCanvasInput()
        {
            Event e = Event.current;
            if (!_canvasRect.Contains(e.mousePosition)) return;

            // Pan with middle mouse or alt+left.
            if (e.type == EventType.MouseDrag && (e.button == 2 || (e.button == 0 && e.alt)))
            {
                pan += e.delta; Repaint(); e.Use(); return;
            }

            // Zoom with scroll.
            if (e.type == EventType.ScrollWheel)
            {
                cellSize = Mathf.Clamp(cellSize - e.delta.y, 14f, 50f);
                Repaint(); e.Use(); return;
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                if (mode == PaintMode.Barricade) ApplyEdgeClick(e.mousePosition);
                else ApplyTileClick(ScreenToHex(e.mousePosition));
                e.Use();
                Repaint();
            }
        }

        private void ApplyTileClick(HexCoord c)
        {
            Undo.RecordObject(level, "Edit Hex Level");
            var tile = level.GetTile(c);

            switch (mode)
            {
                case PaintMode.AddRemoveTile:
                    if (tile == null) level.tiles.Add(new HexTileData(c));
                    else RemoveTile(c);
                    break;

                case PaintMode.SetRole:
                    if (tile == null) { tile = new HexTileData(c); level.tiles.Add(tile); }
                    tile.role = paintRole;
                    break;

                case PaintMode.SetResource:
                    if (tile == null) { tile = new HexTileData(c); level.tiles.Add(tile); }
                    tile.resource = paintResource;
                    tile.resourceAmount = paintResourceAmount;
                    if (paintResource != ResourceType.None) tile.role = TileRole.Resource;
                    break;

                case PaintMode.SetModel:
                    if (tile == null) { tile = new HexTileData(c); level.tiles.Add(tile); }
                    tile.modelIndex = paintModelIndex;
                    break;
            }

            level.BuildLookups();
            EditorUtility.SetDirty(level);
        }

        private void RemoveTile(HexCoord c)
        {
            level.tiles.RemoveAll(t => t.coord == c);
            // also drop edges touching this tile
            level.edges.RemoveAll(e => e.a == c || e.b == c);
        }

        private void ApplyEdgeClick(Vector2 mouse)
        {
            // Find the closest tile, then the neighbor whose shared-edge midpoint
            // is nearest the click. Toggle a barricade there.
            HexCoord center = ScreenToHex(mouse);
            if (!level.HasTile(center)) return;

            float best = float.MaxValue;
            HexCoord bestNeighbor = center;
            bool found = false;

            for (int d = 0; d < 6; d++)
            {
                var n = center.Neighbor(d);
                if (!level.HasTile(n)) continue;
                Vector2 mid = (HexToScreen(center) + HexToScreen(n)) * 0.5f;
                float dist = Vector2.Distance(mid, mouse);
                if (dist < best) { best = dist; bestNeighbor = n; found = true; }
            }

            if (!found) return;
            Undo.RecordObject(level, "Toggle Barricade");
            var cur = level.GetEdge(center, bestNeighbor);
            level.SetEdge(center, bestNeighbor,
                cur == EdgeState.Barricade ? EdgeState.Open : EdgeState.Barricade);
            EditorUtility.SetDirty(level);
        }

        // ---------------------------------------------------------------

        private void DrawFooter()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField($"Tiles: {level.tiles.Count}   Edges: {level.edges.Count}",
                EditorStyles.miniLabel);
            if (_problems.Count > 0)
            {
                foreach (var p in _problems)
                    EditorGUILayout.HelpBox(p, MessageType.Warning);
            }
            EditorGUILayout.EndVertical();
        }

        private void RunValidation()
        {
            bool ok = level.Validate(_problems);
            if (ok) ShowNotification(new GUIContent("Level is playable ✔"));
            Repaint();
        }

        private void SaveLevel()
        {
            if (!level.Validate(_problems))
            {
                if (!EditorUtility.DisplayDialog("Level has problems",
                    "This level failed validation. Save anyway?", "Save", "Cancel"))
                    return;
            }
            EditorUtility.SetDirty(level);
            AssetDatabase.SaveAssets();
            ShowNotification(new GUIContent("Saved"));
        }

        private void ClearBoard()
        {
            Undo.RecordObject(level, "Clear Board");
            level.tiles.Clear();
            level.edges.Clear();
            level.BuildLookups();
            EditorUtility.SetDirty(level);
            Repaint();
        }
    }
}
#endif