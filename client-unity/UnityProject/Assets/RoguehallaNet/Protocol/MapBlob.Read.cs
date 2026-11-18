using System;

namespace Roguehalla.Net.Protocol
{
    /// <summary>MapBlob: decoding of the network map and the collision fixture.</summary>
    public sealed partial class MapBlob
    {
        /// <summary>Reads the retained collision-only parity-fixture format. Never used for network Welcome decoding.</summary>
        public static MapBlob ReadCollisionFixture(ref Reader r)
        {
            Reader scan = r;
            ushort w = scan.U16(), h = scan.U16();
            uint expectedHash = scan.U32(), runs = scan.U32();
            int total = CheckedTileCount(w, h);
            Reader plane = scan;
            uint countedRuns = 0;
            if (ValidateCollisionPlane(ref scan, w, h, total, runs, ref countedRuns) != expectedHash)
                throw BadMap("hash");
            scan.End();
            byte[] tiles = DecodePlane(ref plane, runs, total, 2);
            plane.End();
            r = plane;
            return new MapBlob(w, h, tiles);
        }

        /// <summary>Strict current network map, including its mandatory protocol-14 ground section.</summary>
        public static MapBlob Read(ref Reader r)
        {
            ushort w = r.U16(), h = r.U16();
            uint collisionHash = r.U32();
            uint collisionRuns = r.U32();
            int total = CheckedTileCount(w, h);

            // Preflight with value-copy readers over the original bounded frame. No tile plane or run vector is allocated.
            Reader collisionStart = r;
            Reader scan = r;
            uint countedRuns = 0;
            uint calculatedCollisionHash = ValidateCollisionPlane(ref scan, w, h, total, collisionRuns, ref countedRuns);
            if (calculatedCollisionHash != collisionHash) throw BadMap("hash");
            uint extensionLength = scan.U32();
            if (extensionLength < GroundFixedLength || extensionLength > Wire.MaxServerFrame || extensionLength > scan.Remaining)
                throw BadMap("extension length");
            Reader extension = scan.TakeReader((int)extensionLength);
            GroundHeader ground = ValidateGroundExtension(extension, collisionStart, collisionRuns, total, countedRuns, collisionHash, w, h);
            scan.End();

            // Only now allocate the decoded planes; total decoded plane storage is explicitly capped at 6 MiB.
            Reader decode = r;
            byte[] tiles = DecodePlane(ref decode, collisionRuns, total, 2);
            uint decodeExtensionLength = decode.U32();
            if (decodeExtensionLength != extensionLength) throw BadMap("extension length");
            Reader body = decode.TakeReader((int)decodeExtensionLength);
            byte version = body.U8();
            byte presence = body.U8();
            byte presence2 = version == GroundVersionPresence2 ? body.U8() : (byte)0;
            uint semanticHash = body.U32();
            byte[] terrain = (presence & TerrainPresent) != 0 ? DecodeSemanticPlane(ref body, total, 7) : null;
            byte[] regions = (presence & RegionPresent) != 0 ? DecodeSemanticPlane(ref body, total, 6) : null;
            MapCamp[] camps = (presence & CampsPresent) != 0 ? ReadCamps(ref body) : NoCamps;
            MapColiseum coliseum = (presence & ColiseumPresent) != 0 ? ReadColiseum(ref body) : null;
            byte[] bushes = (presence & BushesPresent) != 0 ? DecodeSemanticPlane(ref body, total, byte.MaxValue) : null;
            MapProp[] props = (presence & PropsPresent) != 0 ? ReadProps(ref body) : NoProps;
            byte[] groundVariants = (presence & GroundVariantsPresent) != 0 ? DecodeSemanticPlane(ref body, total, byte.MaxValue) : null;
            byte[] patience = (presence2 & Presence2Patience) != 0 ? DecodeSemanticPlane(ref body, total, byte.MaxValue) : null;
            MapAnchor[] anchors = (presence2 & Presence2Anchors) != 0 ? ReadAnchors(ref body) : NoAnchors;
            byte terrainMode = body.U8();
            uint contextHash = body.U32();
            body.End();
            r = decode;

            if (version != ground.Version || presence != ground.Presence || presence2 != ground.Presence2 || semanticHash != ground.SemanticHash
                || terrainMode != ground.TerrainMode || contextHash != ground.ContextHash)
                throw BadMap("ground changed after validation");
            return new MapBlob
            {
                W = w, H = h, Tiles = tiles, LayerHash = collisionHash,
                Terrain = terrain, Regions = regions, TerrainMode = terrainMode,
                SemanticHash = semanticHash, ContextHash = contextHash,
                Camps = camps, Coliseum = coliseum, Bushes = bushes, Props = props, GroundVariants = groundVariants,
                Patience = patience, Anchors = anchors,
            };
        }

        // Decoders for sections the preflight already validated.
        static MapCamp[] ReadCamps(ref Reader r)
        {
            var camps = new MapCamp[r.U8()];
            for (int i = 0; i < camps.Length; i++)
            {
                ushort x = r.U16(), y = r.U16(), radius = r.U16();
                camps[i] = new MapCamp(x, y, radius, r.U8());
            }
            return camps;
        }

        static MapColiseum ReadColiseum(ref Reader r)
        {
            ushort x = r.U16(), y = r.U16(), w = r.U16(), h = r.U16();
            var gates = new MapGate[r.U8()];
            for (int g = 0; g < gates.Length; g++)
            {
                byte side = r.U8();
                var tiles = new MapTile[r.U8()];
                for (int t = 0; t < tiles.Length; t++)
                {
                    ushort tx = r.U16();
                    tiles[t] = new MapTile(tx, r.U16());
                }
                gates[g] = new MapGate(side, tiles);
            }
            return new MapColiseum(x, y, w, h, gates);
        }

        static MapProp[] ReadProps(ref Reader r)
        {
            var props = new MapProp[r.U16()];
            for (int i = 0; i < props.Length; i++)
            {
                byte kind = r.U8();
                ushort x = r.U16(), y = r.U16();
                byte variant = r.U8();
                props[i] = new MapProp(kind, x, y, variant, r.U8());
            }
            return props;
        }

        static MapAnchor[] ReadAnchors(ref Reader r)
        {
            var anchors = new MapAnchor[r.U8()];
            for (int i = 0; i < anchors.Length; i++)
            {
                byte zone = r.U8(), kind = r.U8();
                ushort cx = r.U16(), cy = r.U16();
                ushort fx = r.U16(), fy = r.U16(), fw = r.U16(), fh = r.U16();
                byte front = r.U8();
                var entrance = new MapTile[r.U8()];
                for (int t = 0; t < entrance.Length; t++)
                {
                    ushort tx = r.U16();
                    entrance[t] = new MapTile(tx, r.U16());
                }
                anchors[i] = new MapAnchor(zone, kind, new MapTile(cx, cy), new MapRect(fx, fy, fw, fh), front, entrance);
            }
            return anchors;
        }
    }
}
