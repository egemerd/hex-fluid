using System;
using System.Collections.Generic;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// Axial hex coordinate (q, r). Cube is derived (s = -q-r).
    /// This is the single source of truth for hex math: neighbors,
    /// distance, and world placement. Everything else builds on it.
    ///
    /// Layout is FLAT-TOP (flat edge on top and bottom). Directions are
    /// indexed 0..5. Edge index N on a tile = the boundary toward neighbor N.
    /// </summary>
    [Serializable]
    public struct HexCoord : IEquatable<HexCoord>
    {
        public int q;
        public int r;

        public int s => -q - r; // cube constraint q + r + s == 0

        public HexCoord(int q, int r)
        {
            this.q = q;
            this.r = r;
        }

        // 6 axial direction vectors (flat-top visual mapping).
        // Index here MUST match HexEdge direction indexing.
        public static readonly HexCoord[] Directions =
        {
            new HexCoord(1, 0),   // 0 NE
            new HexCoord(1, -1),  // 1 SE
            new HexCoord(0, -1),  // 2 S
            new HexCoord(-1, 0),  // 3 SW
            new HexCoord(-1, 1),  // 4 NW
            new HexCoord(0, 1),   // 5 N
        };

        /// <summary>The neighbor in direction 0..5.</summary>
        public HexCoord Neighbor(int dir) => this + Directions[((dir % 6) + 6) % 6];

        /// <summary>Opposite direction index (for shared-edge lookups).</summary>
        public static int Opposite(int dir) => (dir + 3) % 6;

        public IEnumerable<HexCoord> AllNeighbors()
        {
            for (int i = 0; i < 6; i++) yield return Neighbor(i);
        }

        /// <summary>Hex distance in tiles.</summary>
        public int DistanceTo(HexCoord b)
        {
            return (Mathf.Abs(q - b.q)
                  + Mathf.Abs(r - b.r)
                  + Mathf.Abs(s - b.s)) / 2;
        }

        /// <summary>
        /// World position for a FLAT-TOP layout.
        /// hexSize = outer radius (center to corner) in world units.
        /// </summary>
        public Vector3 ToWorld(float hexSize, float y = 0f)
        {
            float x = hexSize * (1.5f * q);
            float z = hexSize * (Mathf.Sqrt(3f) / 2f * q + Mathf.Sqrt(3f) * r);
            return new Vector3(x, y, z);
        }

        /// <summary>Inverse of ToWorld: world point -> nearest hex. Useful for the editor / mouse picking.</summary>
        public static HexCoord FromWorld(Vector3 pos, float hexSize)
        {
            float qf = (2f / 3f * pos.x) / hexSize;
            float rf = (-1f / 3f * pos.x + Mathf.Sqrt(3f) / 3f * pos.z) / hexSize;
            return RoundCube(qf, rf);
        }

        // Rounds fractional axial to the nearest valid hex.
        private static HexCoord RoundCube(float qf, float rf)
        {
            float sf = -qf - rf;
            int rq = Mathf.RoundToInt(qf);
            int rr = Mathf.RoundToInt(rf);
            int rs = Mathf.RoundToInt(sf);

            float dq = Mathf.Abs(rq - qf);
            float dr = Mathf.Abs(rr - rf);
            float ds = Mathf.Abs(rs - sf);

            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new HexCoord(rq, rr);
        }

        public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.q + b.q, a.r + b.r);
        public static HexCoord operator -(HexCoord a, HexCoord b) => new HexCoord(a.q - b.q, a.r - b.r);

        public bool Equals(HexCoord other) => q == other.q && r == other.r;
        public override bool Equals(object obj) => obj is HexCoord o && Equals(o);
        public override int GetHashCode() => (q * 73856093) ^ (r * 19349663);
        public override string ToString() => $"({q},{r})";

        public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);
        public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);
    }
}