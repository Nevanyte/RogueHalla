using System;

namespace Roguehalla.Net.Protocol
{
    /// <summary>MapBlob: the ground header and the preflight validators.</summary>
    public sealed partial class MapBlob
    {
        struct GroundHeader
        {
            public byte Version, Presence, Presence2, TerrainMode;
            public uint SemanticHash, ContextHash;
        }

        static GroundHeader ValidateGroundExtension(
            Reader extension, Reader collisionStart, uint collisionRuns, int total, uint countedRuns, uint collisionHash, ushort w, ushort h)
        {
            byte version = extension.U8();
            if (version != GroundVersion && version != GroundVersionPresence2) throw BadMap("section version");
            byte presence = extension.U8();
            if ((presence & ~KnownPresence) != 0) throw BadMap("presence");
            // Version 3 needs a second presence byte with a known bit: no such section means version 2 (one encoding per map).
            byte presence2 = 0;
            if (version == GroundVersionPresence2)
            {
                presence2 = extension.U8();
                if (presence2 == 0 || (presence2 & ~KnownPresence2) != 0) throw BadMap("presence");
            }
            uint expectedSemanticHash = extension.U32();
            uint actualSemanticHash = BeginSemanticHash(w, h, presence, presence2);
            int campCount = 0;

            if ((presence & TerrainPresent) != 0)
            {
                uint runs = extension.U32();
                countedRuns = AddRunCount(countedRuns, runs, total, extension.Remaining);
                Reader terrainCompatibility = extension;
                ValidateSemanticPlane(ref extension, runs, total, 7, "terrain value", ref actualSemanticHash);
                if (!CompatibleTerrain(collisionStart, collisionRuns, terrainCompatibility, runs, total))
                    throw BadMap("terrain collision");
            }
            if ((presence & RegionPresent) != 0)
            {
                uint runs = extension.U32();
                countedRuns = AddRunCount(countedRuns, runs, total, extension.Remaining);
                ValidateSemanticPlane(ref extension, runs, total, 6, "region value", ref actualSemanticHash);
            }
            // Protocol 17 public sections, in presence-bit order. List sections hash their exact wire bytes.
            if ((presence & CampsPresent) != 0)
            {
                Reader start = extension;
                campCount = ValidateCamps(ref extension);
                FeedConsumed(ref actualSemanticHash, start, extension.Remaining);
            }
            if ((presence & ColiseumPresent) != 0)
            {
                Reader start = extension;
                ValidateColiseum(ref extension, w, h);
                FeedConsumed(ref actualSemanticHash, start, extension.Remaining);
            }
            if ((presence & BushesPresent) != 0)
            {
                uint runs = extension.U32();
                countedRuns = AddRunCount(countedRuns, runs, total, extension.Remaining);
                ValidatePublicPlane(ref extension, runs, total, ref actualSemanticHash);
            }
            if ((presence & PropsPresent) != 0)
            {
                Reader start = extension;
                ValidateProps(ref extension, w, h);
                FeedConsumed(ref actualSemanticHash, start, extension.Remaining);
            }
            if ((presence & GroundVariantsPresent) != 0)
            {
                uint runs = extension.U32();
                countedRuns = AddRunCount(countedRuns, runs, total, extension.Remaining);
                // The run checks first ("run", "length"), then the values, as Rust does.
                if (ValidatePublicPlane(ref extension, runs, total, ref actualSemanticHash) > MaxGroundVariant)
                    throw BadMap("ground variant");
            }
            if ((presence2 & Presence2Patience) != 0)
            {
                uint runs = extension.U32();
                // A run count the extension cannot hold is truncation before its bounds (as Rust and JS), then the plane's
                // own run checks, then the values: a cell names a camp of the camps section (1-based) or is 0.
                if ((ulong)runs * 3 > (ulong)extension.Remaining) throw new DecodeException(DecodeError.Truncated);
                countedRuns = AddRunCount(countedRuns, runs, total, extension.Remaining);
                if (ValidatePublicPlane(ref extension, runs, total, ref actualSemanticHash) > campCount)
                    throw BadMap("patience camp");
            }
            if ((presence2 & Presence2Anchors) != 0)
            {
                Reader start = extension;
                ValidateAnchors(ref extension, w, h);
                FeedConsumed(ref actualSemanticHash, start, extension.Remaining);
            }
            if (actualSemanticHash != expectedSemanticHash) throw BadMap("semantic hash");

            byte terrainMode = extension.U8();
            if (terrainMode > GroundTerrainAll) throw new DecodeException(DecodeError.BadField, "terrain mode");
            uint contextHash = extension.U32();
            if (ComputeContextHash(collisionHash, expectedSemanticHash, terrainMode) != contextHash)
                throw BadMap("context hash");
            if (extension.Remaining != 0) throw BadMap("extension trailing");
            extension.End();
            if ((long)total * (1 + CountPresent(presence) + ((presence2 & Presence2Patience) != 0 ? 1 : 0)) > MaxDecodedPlaneBytes)
                throw BadMap("decoded bytes");
            if (countedRuns > Wire.MaxMapRuns) throw BadMap("run count");
            return new GroundHeader
            {
                Version = version, Presence = presence, Presence2 = presence2, TerrainMode = terrainMode,
                SemanticHash = expectedSemanticHash, ContextHash = contextHash,
            };
        }

        /// <summary>Feeds the bytes a section validator consumed: from <paramref name="start"/> until <paramref name="remainingAfter"/>
        /// bytes are left.</summary>
        static void FeedConsumed(ref uint hash, Reader start, int remainingAfter)
        {
            int consumed = start.Remaining - remainingAfter;
            for (int i = 0; i < consumed; i++) Feed(ref hash, start.U8());
        }

        /// <summary>Validates the camps section and returns its camp count.</summary>
        static int ValidateCamps(ref Reader r)
        {
            int count = r.U8();
            if (count == 0 || count > MaxCamps) throw BadMap("camp count");
            if (r.Remaining < count * CampRowLength) throw new DecodeException(DecodeError.Truncated);
            for (int i = 0; i < count; i++)
            {
                r.U16(); r.U16(); r.U16();
                byte zone = r.U8();
                if (zone == 0 || zone > MaxCampZone) throw BadMap("camp zone");
            }
            return count;
        }

        static void ValidateColiseum(ref Reader r, ushort w, ushort h)
        {
            ushort x = r.U16(), y = r.U16(), cw = r.U16(), ch = r.U16();
            if (!ColiseumRectFits(x, y, cw, ch, w, h)) throw BadMap("coliseum rect");
            int gates = r.U8();
            if (gates > MaxColiseumGates) throw BadMap("gate count");
            for (int g = 0; g < gates; g++)
            {
                if (r.U8() > MaxGateSide) throw BadMap("gate side");
                int tiles = r.U8();
                if (tiles == 0 || tiles > MaxGateTiles) throw BadMap("gate tiles");
                for (int t = 0; t < tiles; t++)
                {
                    ushort tx = r.U16(), ty = r.U16();
                    if (tx >= w || ty >= h) throw BadMap("gate tile");
                }
            }
        }

        /// <summary>P-WIRE17-MAP-3: the anchors section. Each row's fixed 16 bytes must be in the extension before any is read,
        /// and the entrance bytes are tested against what remains, so a short section is Truncated (as Rust and JS).</summary>
        static void ValidateAnchors(ref Reader r, ushort w, ushort h)
        {
            int count = r.U8();
            if (count == 0 || count > MaxAnchors) throw BadMap("anchor count");
            byte previous = 0;
            for (int i = 0; i < count; i++)
            {
                if (r.Remaining < AnchorFixedLength) throw new DecodeException(DecodeError.Truncated);
                byte zone = r.U8(), kind = r.U8();
                ushort cx = r.U16(), cy = r.U16();
                ushort fx = r.U16(), fy = r.U16(), fw = r.U16(), fh = r.U16();
                byte front = r.U8();
                int tiles = r.U8();
                string fault = AnchorFault(zone, kind, new MapTile(cx, cy), new MapRect(fx, fy, fw, fh), front, previous, w, h);
                if (fault != null) throw BadMap(fault);
                if (tiles > MaxAnchorEntrance) throw BadMap("anchor entrance");
                if (r.Remaining < tiles * AnchorTileLength) throw new DecodeException(DecodeError.Truncated);
                for (int t = 0; t < tiles; t++)
                {
                    ushort tx = r.U16(), ty = r.U16();
                    if (tx >= w || ty >= h) throw BadMap("anchor entrance");
                }
                previous = zone;
            }
        }

        static void ValidateProps(ref Reader r, ushort w, ushort h)
        {
            int count = r.U16();
            if (count == 0 || count > MaxProps) throw BadMap("prop count");
            if (r.Remaining < count * PropRowLength) throw new DecodeException(DecodeError.Truncated);
            MapProp previous = default;
            for (int i = 0; i < count; i++)
            {
                byte kind = r.U8();
                ushort x = r.U16(), y = r.U16();
                byte variant = r.U8();
                var prop = new MapProp(kind, x, y, variant, r.U8());
                string fault = PropFault(prop, i > 0, previous, w, h);
                if (fault != null) throw BadMap(fault);
                previous = prop;
            }
        }

        /// <summary>A bush or ground-variant plane: a zero or overlong run is "run", a short plane "length". Returns the
        /// largest value (the caller checks its own cap).</summary>
        static byte ValidatePublicPlane(ref Reader r, uint runs, int total, ref uint hash)
        {
            int length = 0;
            byte max = 0;
            for (uint i = 0; i < runs; i++)
            {
                byte value = r.U8();
                int runLength = r.U16();
                if (runLength == 0 || runLength > total - length) throw BadMap("run");
                length += runLength;
                if (value > max) max = value;
                for (int j = 0; j < runLength; j++) Feed(ref hash, value);
            }
            if (length != total) throw BadMap("length");
            return max;
        }

        static uint ValidateCollisionPlane(ref Reader r, ushort w, ushort h, int total, uint runs, ref uint countedRuns)
        {
            countedRuns = AddRunCount(countedRuns, runs, total, r.Remaining);
            uint hash = 0x811c9dc5;
            unchecked
            {
                FeedU32(ref hash, w);
                FeedU32(ref hash, h);
                int length = 0;
                for (uint i = 0; i < runs; i++)
                {
                    byte value = r.U8();
                    int runLength = r.U16();
                    if (value > Low || runLength == 0 || runLength > total - length) throw BadMap("run");
                    length += runLength;
                    for (int j = 0; j < runLength; j++) Feed(ref hash, value);
                }
                if (length != total) throw BadMap("length");
            }
            return hash;
        }

        static void ValidateSemanticPlane(ref Reader r, uint runs, int total, byte maxValue, string valueError, ref uint hash)
        {
            int length = 0;
            unchecked
            {
                for (uint i = 0; i < runs; i++)
                {
                    byte value = r.U8();
                    int runLength = r.U16();
                    if (value > maxValue) throw BadMap(valueError);
                    if (runLength == 0 || runLength > total - length) throw BadMap("run");
                    length += runLength;
                    for (int j = 0; j < runLength; j++) Feed(ref hash, value);
                }
            }
            if (length != total) throw BadMap("run");
        }

        static bool CompatibleTerrain(Reader collision, uint collisionRuns, Reader terrain, uint terrainRuns, int total)
        {
            int collisionRemaining = 0, terrainRemaining = 0, read = 0;
            byte collisionValue = 0, terrainValue = 0;
            uint collisionIndex = 0, terrainIndex = 0;
            while (read < total)
            {
                if (collisionRemaining == 0)
                {
                    if (collisionIndex >= collisionRuns) return false;
                    collisionValue = collision.U8(); collisionRemaining = collision.U16(); collisionIndex++;
                    if (collisionRemaining == 0 || collisionRemaining > total - read || collisionValue > Low) return false;
                }
                if (terrainRemaining == 0)
                {
                    if (terrainIndex >= terrainRuns) return false;
                    terrainValue = terrain.U8(); terrainRemaining = terrain.U16(); terrainIndex++;
                    if (terrainRemaining == 0 || terrainRemaining > total - read || terrainValue > 7) return false;
                }
                if (terrainValue >= 1 && terrainValue <= 3 && collisionValue != Wall && collisionValue != Low) return false;
                if (terrainValue >= 4 && collisionValue != Floor) return false;
                int take = Math.Min(collisionRemaining, terrainRemaining);
                read += take;
                collisionRemaining -= take;
                terrainRemaining -= take;
            }
            return collisionRemaining == 0 && terrainRemaining == 0 && collisionIndex == collisionRuns && terrainIndex == terrainRuns;
        }

        static byte[] DecodePlane(ref Reader r, uint runs, int total, byte maxValue)
        {
            byte[] plane = new byte[total];
            int length = 0;
            for (uint i = 0; i < runs; i++)
            {
                byte value = r.U8();
                int runLength = r.U16();
                if (value > maxValue || runLength == 0 || runLength > total - length) throw BadMap("run");
                Array.Fill(plane, value, length, runLength);
                length += runLength;
            }
            if (length != total) throw BadMap("length");
            return plane;
        }

        static byte[] DecodeSemanticPlane(ref Reader r, int total, byte maxValue)
        {
            uint runs = r.U32();
            CheckRunCount(runs, total, r.Remaining);
            return DecodePlane(ref r, runs, total, maxValue);
        }

        static uint AddRunCount(uint accumulated, uint runs, int total, int available)
        {
            CheckRunCount(runs, total, available);
            ulong sum = (ulong)accumulated + runs;
            if (sum > (ulong)Wire.MaxMapRuns) throw BadMap("run count");
            return (uint)sum;
        }

        static void CheckRunCount(uint runs, int total, int available)
        {
            if (runs == 0 || runs > total) throw BadMap("run");
            if ((ulong)runs * 3 > (ulong)available) throw new DecodeException(DecodeError.Truncated);
        }

        static int CheckedTileCount(ushort w, ushort h)
        {
            long total = (long)w * h;
            if (w == 0 || h == 0 || total > Wire.MaxMapTiles) throw BadMap("size");
            return (int)total;
        }

        void CheckContent()
        {
            int total = CheckedTileCount(W, H);
            if (Tiles == null || Tiles.Length != total) throw new EncodeException("bad map length");
            for (int i = 0; i < Tiles.Length; i++) if (Tiles[i] > Low) throw new EncodeException("bad map tile");
            if (ComputeHash(W, H, Tiles) != LayerHash) throw new EncodeException("bad map hash");
            CheckPlaneForWrite(Terrain, total, 7, "terrain");
            CheckPlaneForWrite(Regions, total, 6, "region");
            if (Terrain != null && !CompatibleTerrainArrays(Tiles, Terrain)) throw new EncodeException("bad terrain collision");
            CheckPublic(W, H, Camps, Coliseum, Bushes, Props, GroundVariants, Patience, Anchors);
            if (PublicSemanticHash() != SemanticHash) throw new EncodeException("bad semantic hash");
        }

        static void CheckPlaneForWrite(byte[] plane, int total, byte maxValue, string name)
        {
            if (plane == null) return;
            if (plane.Length != total) throw new EncodeException("bad " + name + " length");
            for (int i = 0; i < plane.Length; i++) if (plane[i] > maxValue) throw new EncodeException("bad " + name + " value");
        }

        static bool CompatibleTerrainArrays(byte[] tiles, byte[] terrain)
        {
            for (int i = 0; i < terrain.Length; i++)
            {
                byte kind = terrain[i], collision = tiles[i];
                if (kind >= 1 && kind <= 3 && collision != Wall && collision != Low) return false;
                if (kind >= 4 && collision != Floor) return false;
            }
            return true;
        }

        /// <summary>Decoded planes besides collision: terrain, regions, bushes, ground variants.</summary>
        static int CountPresent(byte presence) => ((presence & TerrainPresent) == 0 ? 0 : 1) + ((presence & RegionPresent) == 0 ? 0 : 1)
            + ((presence & BushesPresent) == 0 ? 0 : 1) + ((presence & GroundVariantsPresent) == 0 ? 0 : 1);
        static byte Presence(byte[] terrain, byte[] regions, MapCamp[] camps, MapColiseum coliseum, byte[] bushes, MapProp[] props, byte[] groundVariants)
            => (byte)((terrain == null ? 0 : TerrainPresent) | (regions == null ? 0 : RegionPresent)
                | (camps == null || camps.Length == 0 ? 0 : CampsPresent) | (coliseum == null ? 0 : ColiseumPresent)
                | (bushes == null ? 0 : BushesPresent) | (props == null || props.Length == 0 ? 0 : PropsPresent)
                | (groundVariants == null ? 0 : GroundVariantsPresent));
        static byte Presence2(byte[] patience, MapAnchor[] anchors)
            => (byte)((patience == null ? 0 : Presence2Patience) | (anchors == null || anchors.Length == 0 ? 0 : Presence2Anchors));

        static bool SameOptionalBytes(byte[] a, byte[] b) => a == null ? b == null : b != null && SameBytes(a, b);
        static bool SameBytes(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static void Feed(ref uint hash, byte value) { unchecked { hash = (hash ^ value) * 0x01000193; } }
        static void Feed(ref uint hash, byte[] values) { for (int i = 0; i < values.Length; i++) Feed(ref hash, values[i]); }
        static void FeedU32(ref uint hash, uint value)
        {
            Feed(ref hash, (byte)value);
            Feed(ref hash, (byte)(value >> 8));
            Feed(ref hash, (byte)(value >> 16));
            Feed(ref hash, (byte)(value >> 24));
        }

        /// <summary>Domain, w, h, then [2, presence], or [3, presence, presence2] when presence2 is non-zero.</summary>
        static uint BeginSemanticHash(ushort w, ushort h, byte presence, byte presence2)
        {
            uint hash = 0x811c9dc5;
            Feed(ref hash, GroundDomain);
            FeedU32(ref hash, w);
            FeedU32(ref hash, h);
            Feed(ref hash, presence2 == 0 ? GroundVersion : GroundVersionPresence2);
            Feed(ref hash, presence);
            if (presence2 != 0) Feed(ref hash, presence2);
            return hash;
        }

        static DecodeException BadMap(string reason) => new DecodeException(DecodeError.BadMap, reason);
    }
}
