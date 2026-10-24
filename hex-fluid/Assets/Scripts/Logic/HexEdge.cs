using System;
using UnityEngine;

namespace HexFluid
{
    /// <summary>
    /// A boundary between two adjacent tiles. Water flows from one tile to
    /// another ONLY across an open edge, so a barricade is stored here, on
    /// the shared edge, which makes blocking correct in both directions.
    ///
    /// An edge is identified by a canonical (a,b) pair so that the edge
    /// between X and Y is the SAME object regardless of lookup order.
    /// </summary>
    [Serializable]
    public struct EdgeKey : IEquatable<EdgeKey>
    {
        public HexCoord a;
        public HexCoord b;

        /// <summary>
        /// Build the canonical key for the boundary between two coords.
        /// Sorting makes (X,Y) and (Y,X) identical.
        /// </summary>
        public static EdgeKey Between(HexCoord x, HexCoord y)
        {
            // deterministic ordering by (q, then r)
            bool xFirst = x.q < y.q || (x.q == y.q && x.r <= y.r);
            return new EdgeKey
            {
                a = xFirst ? x : y,
                b = xFirst ? y : x
            };
        }

        public bool Equals(EdgeKey other) => a == other.a && b == other.b;
        public override bool Equals(object obj) => obj is EdgeKey o && Equals(o);
        public override int GetHashCode() => a.GetHashCode() ^ (b.GetHashCode() * 31);
        public override string ToString() => $"[{a}|{b}]";
    }

    /// <summary>
    /// What sits on an edge. Extend this enum as you add edge features
    /// (one-way valves, filters, breakable walls, etc.).
    /// </summary>
    public enum EdgeState
    {
        Open = 0,       // water passes freely
        Barricade = 1,  // blocks water (e.g. wood wall you built)
        Wall = 2,       // permanent map boundary, never removable
    }

    /// <summary>Serializable record the level asset stores for non-open edges only.</summary>
    [Serializable]
    public struct EdgeEntry
    {
        public HexCoord a;
        public HexCoord b;
        public EdgeState state;
    }
}