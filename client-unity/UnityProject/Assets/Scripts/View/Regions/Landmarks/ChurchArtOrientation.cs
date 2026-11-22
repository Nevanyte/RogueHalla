using UnityEngine;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Client
{
    public readonly struct ChurchOrientation
    {
        public readonly bool Mirrored;
        public readonly int QuarterTurn;
        public readonly Vector2Int Front;
        internal ChurchOrientation(bool mirrored, int quarterTurn, Vector2Int front)
        { Mirrored = mirrored; QuarterTurn = quarterTurn; Front = front; }

        public int ReflectSide(int viewerRelativeSide) { return Mirrored ? -viewerRelativeSide : viewerRelativeSide; }
        public Vector2 LocalToMapOffset(Vector2 landmarkOffset)
        {
            Vector2 f = new Vector2(Front.x, Front.y);
            Vector2 left = new Vector2(f.y, -f.x);
            if (Mirrored) left = -left;
            return left * landmarkOffset.x + f * landmarkOffset.y;
        }
        public Vector2 MapToLocalOffset(Vector2 mapOffset)
        {
            Vector2 f = new Vector2(Front.x, Front.y);
            Vector2 left = new Vector2(f.y, -f.x);
            if (Mirrored) left = -left;
            return new Vector2(Vector2.Dot(mapOffset, left), Vector2.Dot(mapOffset, f));
        }
    }

    /// <summary>Stable cosmetic reflection for the symmetric church. This does not recover generator-private blueprint orientation.</summary>
    public static class ChurchArtOrientation
    {
        public static ChurchOrientation Resolve(MapAnchor anchor, bool? mirrorOverride = null)
        {
            if (anchor == null) return new ChurchOrientation(mirrorOverride ?? false, 0, Vector2Int.up);
            uint h = 0x9e3779b9u;
            h = Mix(h, anchor.Zone); h = Mix(h, anchor.Kind); h = Mix(h, anchor.Front);
            h = Mix(h, anchor.Footprint.X); h = Mix(h, anchor.Footprint.Y);
            h = Mix(h, anchor.Footprint.W); h = Mix(h, anchor.Footprint.H);
            h = Mix(h, anchor.Centre.X); h = Mix(h, anchor.Centre.Y);
            if (anchor.Entrance != null)
                for (int i = 0; i < anchor.Entrance.Length; i++)
                { h = Mix(h, anchor.Entrance[i].X); h = Mix(h, anchor.Entrance[i].Y); }
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            bool mirrored = mirrorOverride ?? ((h & 0x100u) != 0u);
            int dx = 0, dy = 0;
            if (anchor.Entrance != null && anchor.Entrance.Length > 0)
            {
                float ex = 0, ey = 0;
                for (int i = 0; i < anchor.Entrance.Length; i++) { ex += anchor.Entrance[i].X + .5f; ey += anchor.Entrance[i].Y + .5f; }
                ex = ex / anchor.Entrance.Length - (anchor.Footprint.X + anchor.Footprint.W * .5f);
                ey = ey / anchor.Entrance.Length - (anchor.Footprint.Y + anchor.Footprint.H * .5f);
                if (Mathf.Abs(ex) > Mathf.Abs(ey)) dx = ex > 0f ? 1 : -1;
                else dy = ey > 0f ? 1 : -1;
            }
            else
            {
                int cardinal = (anchor.Front + 2) / 4 & 3;
                if (cardinal == 0) dx = 1; else if (cardinal == 1) dy = 1; else if (cardinal == 2) dx = -1; else dy = -1;
            }
            int quarterTurn = dx > 0 ? 0 : dy > 0 ? 1 : dx < 0 ? 2 : 3;
            Vector2Int front = new Vector2Int(dx, dy);
            return new ChurchOrientation(mirrored, quarterTurn, front);
        }

        public static Vector2 TileToLocal(Vector2 tile) { return new Vector2(tile.x, -tile.y * ArenaView.GroundSquash); }
        public static Vector2 LocalToTile(Vector2 local) { return new Vector2(local.x, -local.y / ArenaView.GroundSquash); }
        static uint Mix(uint h, int value) { h ^= unchecked((uint)value) + 0x9e3779b9u + (h << 6) + (h >> 2); return h; }
    }
}
