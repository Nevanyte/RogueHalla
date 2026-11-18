using System;

namespace Roguehalla.Net.Protocol
{
    /// <summary>MapBlob: semantic and context hashes, canonical equality and run helpers.</summary>
    public sealed partial class MapBlob
    {
        /// <summary>FNV-1a 32 over (w u32 LE, h u32 LE, collision tiles), unchanged from protocol 13.</summary>
        public static uint ComputeHash(uint w, uint h, byte[] tiles)
        {
            uint hash = 0x811c9dc5;
            unchecked
            {
                for (int i = 0; i < 4; i++) { hash ^= (byte)(w >> (8 * i)); hash *= 0x01000193; }
                for (int i = 0; i < 4; i++) { hash ^= (byte)(h >> (8 * i)); hash *= 0x01000193; }
                foreach (byte b in tiles) { hash ^= b; hash *= 0x01000193; }
            }
            return hash;
        }

        /// <summary>FNV-1a32 of the ground semantic bytes of a map without public sections (domain, dimensions, version, presence,
        /// present planes).</summary>
        public static uint ComputeSemanticHash(ushort w, ushort h, byte[] terrain, byte[] regions)
        {
            return ComputeSemanticHash(w, h, terrain, regions, null, null, null, null, null);
        }

        /// <summary>Version 2 semantic hash: domain, w u32, h u32, [version, presence], then in wire order the terrain and region
        /// cells, the camps section's wire bytes, the coliseum section's wire bytes, the bush cells, the props section's wire
        /// bytes and the ground-variant cells (absent sections contribute nothing).</summary>
        public static uint ComputeSemanticHash(ushort w, ushort h, byte[] terrain, byte[] regions,
            MapCamp[] camps, MapColiseum coliseum, byte[] bushes, MapProp[] props, byte[] groundVariants)
        {
            return ComputeSemanticHash(w, h, terrain, regions, camps, coliseum, bushes, props, groundVariants, null);
        }

        /// <summary>Version 3 (a patience plane present): as version 2 over [3, presence, presence2] instead of [2, presence],
        /// and the patience cells after the ground-variant cells. A null plane is the version-2 hash.</summary>
        public static uint ComputeSemanticHash(ushort w, ushort h, byte[] terrain, byte[] regions,
            MapCamp[] camps, MapColiseum coliseum, byte[] bushes, MapProp[] props, byte[] groundVariants, byte[] patience)
        {
            return ComputeSemanticHash(w, h, terrain, regions, camps, coliseum, bushes, props, groundVariants, patience, null);
        }

        /// <summary>P-WIRE17-MAP-3: version 3 with the landmark anchors: the anchors section's wire bytes (count included) after
        /// the patience cells. Null or empty anchors are the hash without them.</summary>
        public static uint ComputeSemanticHash(ushort w, ushort h, byte[] terrain, byte[] regions,
            MapCamp[] camps, MapColiseum coliseum, byte[] bushes, MapProp[] props, byte[] groundVariants, byte[] patience, MapAnchor[] anchors)
        {
            byte presence = Presence(terrain, regions, camps, coliseum, bushes, props, groundVariants);
            uint hash = BeginSemanticHash(w, h, presence, Presence2(patience, anchors));
            if (terrain != null) Feed(ref hash, terrain);
            if (regions != null) Feed(ref hash, regions);
            var lists = new Writer();
            WriteCamps(lists, camps);
            Feed(ref hash, lists.Finish());
            lists = new Writer();
            WriteColiseum(lists, coliseum);
            Feed(ref hash, lists.Finish());
            if (bushes != null) Feed(ref hash, bushes);
            lists = new Writer();
            WriteProps(lists, props);
            Feed(ref hash, lists.Finish());
            if (groundVariants != null) Feed(ref hash, groundVariants);
            if (patience != null) Feed(ref hash, patience);
            lists = new Writer();
            WriteAnchors(lists, anchors);
            Feed(ref hash, lists.Finish());
            return hash;
        }

        /// <summary>FNV-1a32 of the exact protocol-14 map context bytes.</summary>
        public static uint ComputeContextHash(uint collisionHash, uint semanticHash, byte terrainMode)
        {
            uint hash = 0x811c9dc5;
            unchecked
            {
                Feed(ref hash, ContextDomain);
                FeedU32(ref hash, collisionHash);
                FeedU32(ref hash, semanticHash);
                Feed(ref hash, terrainMode);
            }
            return hash;
        }

        /// <summary>Canonical equality for reuse: hashes are only quick rejects, never proof of equality.</summary>
        public bool CanonicalEquals(MapBlob other)
        {
            return other != null && W == other.W && H == other.H && TerrainMode == other.TerrainMode
                && SameBytes(Tiles, other.Tiles) && SameOptionalBytes(Terrain, other.Terrain)
                && SameOptionalBytes(Regions, other.Regions)
                && SameCamps(Camps, other.Camps) && SameColiseum(Coliseum, other.Coliseum)
                && SameOptionalBytes(Bushes, other.Bushes) && SameProps(Props, other.Props)
                && SameOptionalBytes(GroundVariants, other.GroundVariants) && SameOptionalBytes(Patience, other.Patience)
                && SameAnchors(Anchors, other.Anchors);
        }

        static bool SameAnchors(MapAnchor[] a, MapAnchor[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                MapAnchor x = a[i], y = b[i];
                if (x.Zone != y.Zone || x.Kind != y.Kind || x.Front != y.Front || x.Centre.X != y.Centre.X || x.Centre.Y != y.Centre.Y
                    || x.Footprint.X != y.Footprint.X || x.Footprint.Y != y.Footprint.Y || x.Footprint.W != y.Footprint.W
                    || x.Footprint.H != y.Footprint.H || x.Entrance.Length != y.Entrance.Length) return false;
                for (int t = 0; t < x.Entrance.Length; t++)
                    if (x.Entrance[t].X != y.Entrance[t].X || x.Entrance[t].Y != y.Entrance[t].Y) return false;
            }
            return true;
        }

        static bool SameCamps(MapCamp[] a, MapCamp[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].R != b[i].R || a[i].Zone != b[i].Zone) return false;
            return true;
        }

        static bool SameProps(MapProp[] a, MapProp[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].Kind != b[i].Kind || a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].Variant != b[i].Variant || a[i].Flags != b[i].Flags) return false;
            return true;
        }

        static bool SameColiseum(MapColiseum a, MapColiseum b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.X != b.X || a.Y != b.Y || a.W != b.W || a.H != b.H || a.Gates.Length != b.Gates.Length) return false;
            for (int g = 0; g < a.Gates.Length; g++)
            {
                MapGate x = a.Gates[g], y = b.Gates[g];
                if (x.Side != y.Side || x.Tiles.Length != y.Tiles.Length) return false;
                for (int t = 0; t < x.Tiles.Length; t++) if (x.Tiles[t].X != y.Tiles[t].X || x.Tiles[t].Y != y.Tiles[t].Y) return false;
            }
            return true;
        }

        public byte At(int tx, int ty) { return Tiles[ty * W + tx]; }
    }
}
