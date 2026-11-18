using System;

namespace Roguehalla.Net.Protocol
{
    /// <summary>MapBlob: encoding of the collision blob and the public sections.</summary>
    public sealed partial class MapBlob
    {
        /// <summary>Length of the run starting at <paramref name="tiles"/> index <paramref name="i"/>.</summary>
        static int RunAt(byte[] tiles, int i)
        {
            byte t = tiles[i];
            int end = Math.Min(tiles.Length, i + ushort.MaxValue), j = i + 1;
            while (j < end && tiles[j] == t) j++;
            return j - i;
        }

        static int RunCount(byte[] tiles)
        {
            if (tiles == null) return 0;
            int runs = 0;
            for (int i = 0; i < tiles.Length; i += RunAt(tiles, i)) runs++;
            return runs;
        }

        /// <summary>Exact size of the collision blob and the extension (ground planes and public sections; version 3 adds the
        /// second presence byte, the patience plane and the anchors).</summary>
        public int EncodedLength
        {
            get
            {
                long extension = GroundFixedLength + (Presence2(Patience, Anchors) == 0 ? 0 : 1)
                    + (Terrain == null ? 0L : 4L + 3L * RunCount(Terrain))
                    + (Regions == null ? 0L : 4L + 3L * RunCount(Regions))
                    + PublicLength();
                long encoded = 12L + 3L * RunCount(Tiles) + 4L + extension;
                if (60L + encoded > Wire.MaxServerFrame) throw new EncodeException("welcome too large");
                return checked((int)encoded);
            }
        }

        public void Write(Writer w) { Write(w, TerrainMode); }

        /// <summary>Writes semantic map content using the room's actual context selector.</summary>
        public void Write(Writer w, byte terrainMode)
        {
            CheckContent();
            if (terrainMode > GroundTerrainAll) throw new EncodeException("bad map terrain mode");
            int extensionLength = checked((int)(GroundFixedLength + (Presence2(Patience, Anchors) == 0 ? 0 : 1)
                + (Terrain == null ? 0 : 4 + 3 * RunCount(Terrain))
                + (Regions == null ? 0 : 4 + 3 * RunCount(Regions))
                + PublicLength()));
            // EncodedLength includes the fixed Welcome header check, before its Writer allocates.
            _ = EncodedLength;

            w.U16(W); w.U16(H); w.U32(LayerHash);
            WritePlane(w, Tiles);
            w.U32((uint)extensionLength);
            byte presence2 = Presence2(Patience, Anchors);
            w.U8(presence2 == 0 ? GroundVersion : GroundVersionPresence2);
            w.U8(Presence(Terrain, Regions, Camps, Coliseum, Bushes, Props, GroundVariants));
            if (presence2 != 0) w.U8(presence2);
            w.U32(SemanticHash);
            if (Terrain != null) WritePlane(w, Terrain);
            if (Regions != null) WritePlane(w, Regions);
            WriteCamps(w, Camps);
            WriteColiseum(w, Coliseum);
            if (Bushes != null) WritePlane(w, Bushes);
            WriteProps(w, Props);
            if (GroundVariants != null) WritePlane(w, GroundVariants);
            if (Patience != null) WritePlane(w, Patience);
            WriteAnchors(w, Anchors);
            w.U8(terrainMode);
            w.U32(ComputeContextHash(LayerHash, SemanticHash, terrainMode));
        }

        long PublicLength()
        {
            long n = 0;
            if (Camps.Length > 0) n += 1 + (long)CampRowLength * Camps.Length;
            if (Coliseum != null)
            {
                n += 9;
                foreach (var g in Coliseum.Gates) n += 2 + 4L * g.Tiles.Length;
            }
            if (Bushes != null) n += 4 + 3L * RunCount(Bushes);
            if (Props.Length > 0) n += 2 + (long)PropRowLength * Props.Length;
            if (GroundVariants != null) n += 4 + 3L * RunCount(GroundVariants);
            if (Patience != null) n += 4 + 3L * RunCount(Patience);
            if (Anchors.Length > 0)
            {
                n += 1;
                foreach (var a in Anchors) n += AnchorFixedLength + (long)AnchorTileLength * a.Entrance.Length;
            }
            return n;
        }

        static void WriteCamps(Writer w, MapCamp[] camps)
        {
            if (camps == null || camps.Length == 0) return;
            w.U8((byte)camps.Length);
            foreach (var c in camps) { w.U16(c.X); w.U16(c.Y); w.U16(c.R); w.U8(c.Zone); }
        }

        static void WriteColiseum(Writer w, MapColiseum c)
        {
            if (c == null) return;
            w.U16(c.X); w.U16(c.Y); w.U16(c.W); w.U16(c.H);
            w.U8((byte)c.Gates.Length);
            foreach (var g in c.Gates)
            {
                w.U8(g.Side);
                w.U8((byte)g.Tiles.Length);
                foreach (var t in g.Tiles) { w.U16(t.X); w.U16(t.Y); }
            }
        }

        static void WriteProps(Writer w, MapProp[] props)
        {
            if (props == null || props.Length == 0) return;
            w.U16((ushort)props.Length);
            foreach (var p in props) { w.U8(p.Kind); w.U16(p.X); w.U16(p.Y); w.U8(p.Variant); w.U8(p.Flags); }
        }

        static void WriteAnchors(Writer w, MapAnchor[] anchors)
        {
            if (anchors == null || anchors.Length == 0) return;
            w.U8((byte)anchors.Length);
            foreach (var a in anchors)
            {
                w.U8(a.Zone); w.U8(a.Kind);
                w.U16(a.Centre.X); w.U16(a.Centre.Y);
                w.U16(a.Footprint.X); w.U16(a.Footprint.Y); w.U16(a.Footprint.W); w.U16(a.Footprint.H);
                w.U8(a.Front);
                w.U8((byte)a.Entrance.Length);
                foreach (var t in a.Entrance) { w.U16(t.X); w.U16(t.Y); }
            }
        }

        static void CheckPublic(ushort w, ushort h, MapCamp[] camps, MapColiseum coliseum, byte[] bushes, MapProp[] props, byte[] groundVariants,
            byte[] patience, MapAnchor[] anchors)
        {
            int total = CheckedTileCount(w, h);
            if (camps.Length > MaxCamps) throw new EncodeException("bad map camp count");
            foreach (var c in camps) if (c.Zone == 0 || c.Zone > MaxCampZone) throw new EncodeException("bad map camp zone");
            if (coliseum != null)
            {
                if (!ColiseumRectFits(coliseum.X, coliseum.Y, coliseum.W, coliseum.H, w, h)) throw new EncodeException("bad map coliseum rect");
                if (coliseum.Gates == null || coliseum.Gates.Length > MaxColiseumGates) throw new EncodeException("bad map gate count");
                foreach (var g in coliseum.Gates)
                {
                    if (g.Side > MaxGateSide) throw new EncodeException("bad map gate side");
                    if (g.Tiles == null || g.Tiles.Length == 0 || g.Tiles.Length > MaxGateTiles) throw new EncodeException("bad map gate tiles");
                    foreach (var t in g.Tiles) if (t.X >= w || t.Y >= h) throw new EncodeException("bad map gate tile");
                }
            }
            if (bushes != null && bushes.Length != total) throw new EncodeException("bad map plane length");
            if (groundVariants != null && groundVariants.Length != total) throw new EncodeException("bad map plane length");
            if (patience != null && patience.Length != total) throw new EncodeException("bad map plane length");
            if (props.Length > MaxProps) throw new EncodeException("bad map prop count");
            for (int i = 0; i < props.Length; i++)
            {
                string fault = PropFault(props[i], i > 0, i > 0 ? props[i - 1] : default, w, h);
                if (fault != null) throw new EncodeException("bad map " + fault);
            }
            if (groundVariants != null)
                foreach (var v in groundVariants) if (v > MaxGroundVariant) throw new EncodeException("bad map ground variant");
            if (patience != null)
                foreach (var v in patience) if (v > camps.Length) throw new EncodeException("bad map patience camp");
            // P-WIRE17-MAP-3: the decoder's anchor checks in its order (count, then per row: fixed part, entrance count, tiles).
            if (anchors.Length > MaxAnchors) throw new EncodeException("bad map anchor count");
            byte previous = 0;
            foreach (var a in anchors)
            {
                if (a == null) throw new EncodeException("bad map anchor");
                string fault = AnchorFault(a.Zone, a.Kind, a.Centre, a.Footprint, a.Front, previous, w, h);
                if (fault != null) throw new EncodeException("bad map " + fault);
                if (a.Entrance.Length > MaxAnchorEntrance) throw new EncodeException("bad map anchor entrance");
                foreach (var t in a.Entrance) if (t.X >= w || t.Y >= h) throw new EncodeException("bad map anchor entrance");
                previous = a.Zone;
            }
        }

        /// <summary>The checks on an anchor row's fixed part in the wire rules' order (zone, order against the previous row's
        /// zone, 0 for the first, kind, footprint, centre inside the half-open footprint, front), shared by the encoder and the
        /// decoder: null when valid, else the Rust reason.</summary>
        static string AnchorFault(byte zone, byte kind, MapTile centre, MapRect f, byte front, byte previous, ushort w, ushort h)
        {
            if (zone == 0 || zone > MaxCampZone) return "anchor zone";
            if (zone <= previous) return "anchor order";
            if (kind < AnchorKindVolcano || kind > AnchorKindHall) return "anchor kind";
            if (!ColiseumRectFits(f.X, f.Y, f.W, f.H, w, h)) return "anchor footprint";
            if (centre.X < f.X || centre.X >= f.X + f.W || centre.Y < f.Y || centre.Y >= f.Y + f.H) return "anchor centre";
            if (front >= AnchorDirections) return "anchor front";
            return null;
        }

        /// <summary>The value checks of one prop row in the wire rules' order (flags, kind, variant, tile, order), shared by the
        /// encoder and the decoder: null when the row is valid, else the Rust reason.</summary>
        static string PropFault(MapProp p, bool hasPrevious, MapProp previous, ushort w, ushort h)
        {
            if ((p.Flags & ~(PropFlagTall | PropFlagLegacy)) != 0) return "prop flags";
            if ((p.Flags & PropFlagLegacy) != 0)
            {
                // A legacy prop's kind is a generator catalogue index; its variant is the generator's v (not checked).
                if (p.Kind >= LegacyPropKinds) return "prop kind";
            }
            else
            {
                if (p.Kind == 0 || p.Kind > MaxPropKind) return "prop kind";
                if (p.Variant > MaxPropVariant) return "prop variant";
            }
            if (p.X / PropTile >= w || p.Y / PropTile >= h) return "prop tile";
            if (hasPrevious && !PropAfter(previous, p)) return "prop order";
            return null;
        }

        /// <summary>Whether <paramref name="b"/> is strictly after <paramref name="a"/> in (y, x, kind).</summary>
        static bool PropAfter(MapProp a, MapProp b)
        {
            if (b.Y != a.Y) return b.Y > a.Y;
            if (b.X != a.X) return b.X > a.X;
            return b.Kind > a.Kind;
        }

        static bool ColiseumRectFits(ushort x, ushort y, ushort cw, ushort ch, ushort w, ushort h)
            => cw >= 1 && ch >= 1 && x + cw <= w && y + ch <= h;

        static void WritePlane(Writer w, byte[] plane)
        {
            w.U32((uint)RunCount(plane));
            for (int i = 0; i < plane.Length;)
            {
                int n = RunAt(plane, i);
                w.U8(plane[i]); w.U16((ushort)n);
                i += n;
            }
        }
    }
}
