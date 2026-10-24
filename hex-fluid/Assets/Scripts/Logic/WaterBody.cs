using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// The water as ONE continuous 3D surface over the whole planned flood, instead
    /// of a piece per tile, so there are no seams or hex patterns.
    ///
    /// Build() bakes the flood into a small "flood map" texture covering the board:
    ///   R = when the water front reaches that point (in tiles, on WaterSimulation's clock)
    ///   G = signed distance to the nearest dry ground (+ inside the water, - outside)
    ///   B,A = the current there (board XZ), blended smoothly between tiles
    /// and makes one subdivided mesh at the water level. FlowingWater.shader reads
    /// the map to reveal the front, ripple along the current, raise waves (fading
    /// out at the banks) and fade at the edges. Everything is worked out once, up
    /// front, so playback is just the shader reading a clock.
    ///
    /// Lives under the board (same space as the tiles); WaterSimulation creates it.
    /// </summary>
    public class WaterBody : MonoBehaviour
    {
        /// <summary>One tile of the planned flood.</summary>
        public struct TileFlow
        {
            public HexCoord coord;
            public float arrival;           // the step it floods on (sources: 0)
            public HexCoord[] parents;      // tiles its water flows in from (none for a source)
            public Vector2 flow;            // downstream direction, board XZ, normalised
        }

        /// <summary>Arrival used for sources: already wet when the clock starts at 0.</summary>
        public const float SourceArrival = -1f;

        private static readonly int FloodMapId = Shader.PropertyToID("_FloodMap");
        private static readonly int BankMapId = Shader.PropertyToID("_BankMap");
        private static readonly int FloodRectId = Shader.PropertyToID("_FloodRect");
        private const float InvSqrt3 = 0.5773503f;

        private Mesh _mesh;
        private Texture2D _map, _bankMap;

        /// <summary>Latest arrival anywhere on the water (the far corners of the last tiles).</summary>
        public float MaxArrival { get; private set; }

        /// <summary>
        /// Bakes the flood map and builds the mesh. Call once per run. boardTiles = every
        /// tile on the board: the water gets bank foam only where it meets one of those
        /// that stays dry, not along the board's outside.
        /// </summary>
        public void Build(Material material, float hexSize, IList<TileFlow> tiles, ICollection<HexCoord> boardTiles,
                          float texelsPerHex = 14f, float vertsPerHex = 8f)
        {
            var byCoord = new Dictionary<HexCoord, TileFlow>();
            foreach (var t in tiles) byCoord[t.coord] = t;
            if (byCoord.Count == 0) return;

            // ---- Area: every flooded hex plus a margin of dry ground around it.
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (var t in tiles)
            {
                Vector2 c = Center(t.coord, hexSize);
                min = Vector2.Min(min, c);
                max = Vector2.Max(max, c);
            }
            float margin = hexSize * 1.25f;
            min -= Vector2.one * margin;
            max += Vector2.one * margin;

            float texel = hexSize / Mathf.Max(2f, texelsPerHex);
            int w = Mathf.Clamp(Mathf.CeilToInt((max.x - min.x) / texel), 8, 512);
            int h = Mathf.Clamp(Mathf.CeilToInt((max.y - min.y) / texel), 8, 512);
            Vector2 size = new Vector2(w * texel, h * texel);

            // ---- Per texel: exact signed distance to the banks (from the true hex shapes,
            // so edges are clean lines, not stair steps), arrival and current. Dry texels
            // just past the edge get the values of the water next to them, so the
            // shader's bilinear filtering stays smooth right up to the bank.
            int n = w * h;
            var pixels = new Color[n];
            var banks = new Color[n];                            // R = distance to a dry board tile
            var signedDist = new float[n];
            float flowRadius = hexSize * 1.6f;
            float far = hexSize * 2f;
            MaxArrival = float.MinValue;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                Vector2 p = min + new Vector2((x + 0.5f) * texel, (y + 0.5f) * texel);
                var cell = HexCoord.FromWorld(new Vector3(p.x, 0f, p.y), hexSize);

                // Nearest dry hex (board tile or empty space), nearest dry board tile, and
                // nearest wet hex among the cells around p.
                float toDry = far, toBank = far, toWet = far;
                bool anyWet = false;
                TileFlow nearestWet = default;
                for (int dq = -2; dq <= 2; dq++)
                for (int dr = -2; dr <= 2; dr++)
                {
                    var c = new HexCoord(cell.q + dq, cell.r + dr);
                    if (c.DistanceTo(cell) > 2) continue;
                    float d = HexDistance(p - Center(c, hexSize), hexSize);   // > 0 outside that hex
                    if (byCoord.TryGetValue(c, out var t))
                    {
                        if (d < toWet) { toWet = d; nearestWet = t; anyWet = true; }
                    }
                    else
                    {
                        toDry = Mathf.Min(toDry, d);
                        if (boardTiles == null || boardTiles.Contains(c)) toBank = Mathf.Min(toBank, d);
                    }
                }
                banks[i] = new Color(Mathf.Max(0f, toBank), 0f, 0f, 0f);

                bool wet = byCoord.TryGetValue(cell, out var own);
                if (!wet && !anyWet)
                {
                    pixels[i] = new Color(1e4f, -far, 0f, -1f);
                    signedDist[i] = -far;
                    continue;
                }

                TileFlow tile = wet ? own : nearestWet;
                float arrival = ArrivalAt(p, tile, byCoord, hexSize);
                Vector2 flow = BlendedFlow(p, tile, tiles, hexSize, flowRadius);
                signedDist[i] = wet ? Mathf.Max(0f, toDry) : -Mathf.Max(0f, toWet);
                if (wet) MaxArrival = Mathf.Max(MaxArrival, arrival);
                pixels[i] = new Color(arrival, signedDist[i], flow.x, flow.y);
            }

            _map = new Texture2D(w, h, TextureFormat.RGBAHalf, false, true)
            {
                name = "FloodMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _map.SetPixels(pixels);
            _map.Apply(false, true);

            _bankMap = new Texture2D(w, h, TextureFormat.RHalf, false, true)
            {
                name = "BankMap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _bankMap.SetPixels(banks);
            _bankMap.Apply(false, true);

            // ---- One grid mesh, keeping only the quads near water.
            _mesh = BuildMesh(min, size, hexSize, vertsPerHex, signedDist, w, h, texel);

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            var mpb = new MaterialPropertyBlock();
            mpb.SetTexture(FloodMapId, _map);
            mpb.SetTexture(BankMapId, _bankMap);
            mpb.SetVector(FloodRectId, new Vector4(min.x, min.y, 1f / size.x, 1f / size.y));
            r.SetPropertyBlock(mpb);
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_map != null) Destroy(_map);
            if (_bankMap != null) Destroy(_bankMap);
        }

        private static Vector2 Center(HexCoord c, float hexSize)
        {
            Vector3 v = c.ToWorld(hexSize);
            return new Vector2(v.x, v.z);
        }

        /// <summary>
        /// When the front reaches p: the arrival of the tile it flows in from plus the
        /// distance from that tile's centre, in tile spacings. Continuous across tiles,
        /// so the front is one smooth curve.
        /// </summary>
        private static float ArrivalAt(Vector2 p, TileFlow tile, Dictionary<HexCoord, TileFlow> byCoord, float hexSize)
        {
            if (tile.parents == null || tile.parents.Length == 0) return SourceArrival;   // a source: wet from the start
            float best = float.MaxValue;
            foreach (var parent in tile.parents)
            {
                if (!byCoord.TryGetValue(parent, out var pt)) continue;
                float d = Vector2.Distance(p, Center(parent, hexSize)) / hexSize * InvSqrt3;
                best = Mathf.Min(best, pt.arrival + d);
            }
            return best < float.MaxValue ? best : tile.arrival;
        }

        /// <summary>The current at p: nearby tiles' currents weighted by closeness, so it bends smoothly.</summary>
        private static Vector2 BlendedFlow(Vector2 p, TileFlow own, IList<TileFlow> tiles, float hexSize, float radius)
        {
            Vector2 sum = Vector2.zero;
            foreach (var t in tiles)
            {
                float d = Vector2.Distance(p, Center(t.coord, hexSize));
                if (d >= radius) continue;
                float k = 1f - d / radius;
                sum += t.flow * (k * k);
            }
            return sum.sqrMagnitude > 1e-6f ? sum.normalized : own.flow;
        }

        /// <summary>
        /// Distance from q (relative to a hex centre) to that flat-top hex: negative
        /// inside, positive outside (slightly under-estimated past the corners).
        /// </summary>
        private static float HexDistance(Vector2 q, float hexSize)
        {
            const float C = 0.8660254f;
            float a = Mathf.Abs(q.x * C + q.y * 0.5f);
            float b = Mathf.Abs(q.y);
            float c = Mathf.Abs(-q.x * C + q.y * 0.5f);
            return Mathf.Max(a, Mathf.Max(b, c)) - hexSize * C;
        }

        /// <summary>A flat grid over the area (y = 0, this object sits at the water level), only where water can be.</summary>
        private static Mesh BuildMesh(Vector2 min, Vector2 size, float hexSize, float vertsPerHex,
                                      float[] signedDist, int w, int h, float texel)
        {
            int nx = Mathf.Clamp(Mathf.CeilToInt(size.x / hexSize * vertsPerHex), 2, 400);
            int nz = Mathf.Clamp(Mathf.CeilToInt(size.y / hexSize * vertsPerHex), 2, 400);
            float keep = hexSize * 0.35f;                            // skip quads further than this from water

            bool NearWater(float px, float pz)
            {
                int x = Mathf.Clamp(Mathf.FloorToInt((px - min.x) / texel), 0, w - 1);
                int y = Mathf.Clamp(Mathf.FloorToInt((pz - min.y) / texel), 0, h - 1);
                return signedDist[y * w + x] > -keep;
            }

            var verts = new Vector3[(nx + 1) * (nz + 1)];
            for (int z = 0; z <= nz; z++)
            for (int x = 0; x <= nx; x++)
                verts[z * (nx + 1) + x] = new Vector3(min.x + size.x * x / nx, 0f, min.y + size.y * z / nz);

            var tris = new List<int>();
            for (int z = 0; z < nz; z++)
            for (int x = 0; x < nx; x++)
            {
                float cx = min.x + size.x * (x + 0.5f) / nx;
                float cz = min.y + size.y * (z + 0.5f) / nz;
                if (!NearWater(cx, cz)) continue;

                int v00 = z * (nx + 1) + x, v10 = v00 + 1;
                int v01 = v00 + nx + 1, v11 = v01 + 1;
                tris.Add(v00); tris.Add(v01); tris.Add(v11);      // clockwise seen from above = faces up
                tris.Add(v00); tris.Add(v11); tris.Add(v10);
            }

            var mesh = new Mesh { name = "WaterBody" };
            if (verts.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var b = mesh.bounds;
            b.Expand(new Vector3(0f, 1f, 0f));                    // room for the waves
            mesh.bounds = b;
            return mesh;
        }
    }
}
