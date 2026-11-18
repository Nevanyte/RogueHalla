using System;

namespace Roguehalla.Net.Protocol
{
    /// <summary>The canonical public map: collision tile classes row-major (0 floor, 1 wall, 2 low), the public ground planes and
    /// (protocol 17) the public sections legacy clients generated themselves: camps, coliseum and gates, bushes, props, ground
    /// variants.</summary>
    public sealed partial class MapBlob
    {
        /// <summary>Extension layout version: protocol 17 is version 2; version 1 is refused. Version 3 (P-WIRE17-MAP-2) adds a
        /// second presence byte after the first, used exactly when a second-presence section (the patience plane, P-WIRE17-MAP-3's
        /// landmark anchors) is present.</summary>
        const byte GroundVersion = 2, GroundVersionPresence2 = 3;
        const byte Presence2Patience = 0x01, Presence2Anchors = 0x02, KnownPresence2 = 0x03;
        /// <summary>Six decodable planes (collision, terrain, regions, bushes, ground variants, patience), as Rust's
        /// MAX_DECODED_PLANES and MAX_CUMULATIVE_RUNS.</summary>
        const long MaxDecodedPlaneBytes = Wire.MaxMapDecodedBytes;
        const byte TerrainPresent = 0x01, RegionPresent = 0x02, CampsPresent = 0x04, ColiseumPresent = 0x08,
            BushesPresent = 0x10, PropsPresent = 0x20, GroundVariantsPresent = 0x40, KnownPresence = 0x7f;
        public const int MaxCamps = 64, MaxColiseumGates = 8, MaxGateTiles = 16, MaxProps = 8192;
        /// <summary>P-WIRE17-MAP-3: anchor caps and the wire's landmark kind codes (as Rust's `rh_protocol::map`).</summary>
        public const int MaxAnchors = 5, MaxAnchorEntrance = 16;
        public const byte AnchorKindVolcano = 1, AnchorKindChurch = 2, AnchorKindBogTree = 3, AnchorKindCave = 4, AnchorKindHall = 5;
        static readonly MapAnchor[] NoAnchors = new MapAnchor[0];
        /// <summary>An anchor row before its entrance tiles (zone, kind, centre, footprint, front, entrance count); one entrance
        /// tile; DIR16 directions.</summary>
        const int AnchorFixedLength = 16, AnchorTileLength = 4, AnchorDirections = 16;
        const byte MaxCampZone = 5, MaxGateSide = 3;
        /// <summary>Dressing value caps (P-DRESS-BAKE-1), the same as Rust's `rh_protocol::map`: prop flags tall and legacy;
        /// a rule prop's kind 1..MaxPropKind and variant at most MaxPropVariant; a legacy prop's kind below LegacyPropKinds;
        /// every ground-variant cell at most MaxGroundVariant.</summary>
        public const byte PropFlagTall = 1, PropFlagLegacy = 2, MaxPropKind = 100, LegacyPropKinds = 34, MaxPropVariant = 3,
            MaxGroundVariant = 15;
        /// <summary>World units per tile: a prop's tile (x / 32, y / 32) must be inside the map.</summary>
        const int PropTile = 32;
        const int CampRowLength = 7, PropRowLength = 7;
        static readonly MapCamp[] NoCamps = new MapCamp[0];
        static readonly MapProp[] NoProps = new MapProp[0];
        const int GroundFixedLength = 11; // version, flags, semantic hash, mode and context hash
        static readonly byte[] GroundDomain = { (byte)'r', (byte)'h', (byte)'-', (byte)'g', (byte)'r', (byte)'o', (byte)'u', (byte)'n', (byte)'d', (byte)'/', (byte)'1', 0 };
        static readonly byte[] ContextDomain = { (byte)'r', (byte)'h', (byte)'-', (byte)'m', (byte)'a', (byte)'p', (byte)'-', (byte)'c', (byte)'o', (byte)'n', (byte)'t', (byte)'e', (byte)'x', (byte)'t', (byte)'/', (byte)'1', 0 };

        public ushort W, H;
        public byte[] Tiles;
        public uint LayerHash;
        /// <summary>Optional row-major terrain plane. Null means absent; an all-zero array is present data.</summary>
        public byte[] Terrain { get; private set; }
        /// <summary>Optional row-major region plane. Null means absent; an all-zero array is present data.</summary>
        public byte[] Regions { get; private set; }
        /// <summary>Actual room selector: 0 Off, 1 Map, 2 All.</summary>
        public byte TerrainMode { get; private set; }
        public uint SemanticHash { get; private set; }
        public uint ContextHash { get; private set; }
        /// <summary>Camp markers in map order (the WireCamp row order). Empty when the section is absent.</summary>
        public MapCamp[] Camps { get; private set; } = NoCamps;
        /// <summary>The coliseum rectangle and gates, or null when absent.</summary>
        public MapColiseum Coliseum { get; private set; }
        /// <summary>Row-major bush plane: 0 none, else the legacy bush id. Null when absent.</summary>
        public byte[] Bushes { get; private set; }
        /// <summary>Cosmetic props (reserved). Empty when absent.</summary>
        public MapProp[] Props { get; private set; } = NoProps;
        /// <summary>Row-major ground-variant plane (reserved). Null when absent.</summary>
        public byte[] GroundVariants { get; private set; }
        /// <summary>P-WIRE17-MAP-2: row-major camp patience plane (0, or the 1-based index into Camps of the camp whose patience
        /// range covers the tile), carried by a section-version-3 extension. Null when absent.</summary>
        public byte[] Patience { get; private set; }
        /// <summary>P-WIRE17-MAP-3: each zone's landmark anchor in zone-code order (second presence bit 0x02, after the patience
        /// plane). Empty when absent.</summary>
        public MapAnchor[] Anchors { get; private set; } = NoAnchors;

        public const byte Floor = 0, Wall = 1, Low = 2;
        public const byte GroundTerrainOff = 0, GroundTerrainMap = 1, GroundTerrainAll = 2;

        public MapBlob(ushort w, ushort h, byte[] tiles)
            : this(w, h, tiles, null, null, GroundTerrainOff)
        {
        }

        public MapBlob(ushort w, ushort h, byte[] tiles, byte[] terrain, byte[] regions, byte terrainMode = GroundTerrainOff)
        {
            W = w;
            H = h;
            Tiles = tiles;
            LayerHash = ComputeHash(w, h, tiles);
            Terrain = terrain;
            Regions = regions;
            TerrainMode = terrainMode;
            SemanticHash = ComputeSemanticHash(w, h, terrain, regions);
            ContextHash = ComputeContextHash(LayerHash, SemanticHash, terrainMode);
        }

        MapBlob() { }

        /// <summary>A copy carrying the protocol 17 public sections, hashes recomputed. Empty or null means absent. Refuses (never
        /// clamps) a section a decoder would refuse.</summary>
        public MapBlob WithPublic(MapCamp[] camps, MapColiseum coliseum, byte[] bushes, MapProp[] props, byte[] groundVariants)
        {
            return WithPublic(camps, coliseum, bushes, props, groundVariants, null);
        }

        /// <summary>P-WIRE17-MAP-2: <see cref="WithPublic(MapCamp[], MapColiseum, byte[], MapProp[], byte[])"/> plus the camp
        /// patience plane (null = absent; a present plane makes the extension section version 3). Every cell must be at most
        /// the camp count.</summary>
        public MapBlob WithPublic(MapCamp[] camps, MapColiseum coliseum, byte[] bushes, MapProp[] props, byte[] groundVariants, byte[] patience)
        {
            return WithPublic(camps, coliseum, bushes, props, groundVariants, patience, null);
        }

        /// <summary>P-WIRE17-MAP-3: the six-section overload plus the landmark anchors (null or empty = absent; present anchors
        /// make the extension section version 3). Refuses, with the decoder's reasons, what a decoder would refuse.</summary>
        public MapBlob WithPublic(MapCamp[] camps, MapColiseum coliseum, byte[] bushes, MapProp[] props, byte[] groundVariants, byte[] patience,
            MapAnchor[] anchors)
        {
            var map = new MapBlob
            {
                W = W, H = H, Tiles = Tiles, LayerHash = LayerHash, Terrain = Terrain, Regions = Regions, TerrainMode = TerrainMode,
                Camps = camps ?? NoCamps, Coliseum = coliseum, Bushes = bushes, Props = props ?? NoProps, GroundVariants = groundVariants,
                Patience = patience, Anchors = anchors ?? NoAnchors,
            };
            CheckPublic(map.W, map.H, map.Camps, map.Coliseum, map.Bushes, map.Props, map.GroundVariants, map.Patience, map.Anchors);
            map.SemanticHash = map.PublicSemanticHash();
            map.ContextHash = ComputeContextHash(map.LayerHash, map.SemanticHash, map.TerrainMode);
            return map;
        }

        uint PublicSemanticHash() => ComputeSemanticHash(W, H, Terrain, Regions, Camps, Coliseum, Bushes, Props, GroundVariants, Patience, Anchors);
    }
}
