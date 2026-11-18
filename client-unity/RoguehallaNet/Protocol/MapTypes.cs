using System;

namespace Roguehalla.Net.Protocol
{
    /// <summary>One camp marker (protocol 17), index-aligned with the WireCamp rows: legacy CampDef x, y and clearing radius r
    /// in world units, zone code 1..5.</summary>
    public readonly struct MapCamp
    {
        public readonly ushort X, Y, R;
        public readonly byte Zone;
        public MapCamp(ushort x, ushort y, ushort r, byte zone) { X = x; Y = y; R = r; Zone = zone; }
    }

    /// <summary>A tile coordinate inside the map.</summary>
    public readonly struct MapTile
    {
        public readonly ushort X, Y;
        public MapTile(ushort x, ushort y) { X = x; Y = y; }
    }

    /// <summary>A coliseum gate: side 0 north, 1 east, 2 south, 3 west; its tiles in legacy order.</summary>
    public sealed class MapGate
    {
        public readonly byte Side;
        public readonly MapTile[] Tiles;
        public MapGate(byte side, MapTile[] tiles) { Side = side; Tiles = tiles; }
    }

    /// <summary>The coliseum rectangle in tiles and its gates in legacy order.</summary>
    public sealed class MapColiseum
    {
        public readonly ushort X, Y, W, H;
        public readonly MapGate[] Gates;
        public MapColiseum(ushort x, ushort y, ushort w, ushort h, MapGate[] gates) { X = x; Y = y; W = w; H = h; Gates = gates; }
    }

    /// <summary>A cosmetic prop of the server's map dressing (P-DRESS-BAKE-1). x, y in world units; rows sorted by (y, x, kind).
    /// Validated on decode; no Unity script draws it until P-UNITY-DRESS-1.</summary>
    public readonly struct MapProp
    {
        public readonly byte Kind, Variant, Flags;
        public readonly ushort X, Y;
        public MapProp(byte kind, ushort x, ushort y, byte variant, byte flags) { Kind = kind; X = x; Y = y; Variant = variant; Flags = flags; }
    }

    /// <summary>A tile rectangle inside the map (P-WIRE17-MAP-3: an anchor's footprint).</summary>
    public readonly struct MapRect
    {
        public readonly ushort X, Y, W, H;
        public MapRect(ushort x, ushort y, ushort w, ushort h) { X = x; Y = y; W = w; H = h; }
    }

    /// <summary>P-WIRE17-MAP-3: one zone's landmark anchor, tile units (layout in docs/backlog/packets/P-WIRE17-MAP-3.md "Wire
    /// layout"). Zone is the zone code 1..5; Kind one of the MapBlob.AnchorKind* codes; Front a DIR16 index below 16; Entrance
    /// the way in, generator order, empty for a landmark without one.</summary>
    public sealed class MapAnchor
    {
        public readonly byte Zone, Kind, Front;
        public readonly MapTile Centre;
        public readonly MapRect Footprint;
        public readonly MapTile[] Entrance;
        public MapAnchor(byte zone, byte kind, MapTile centre, MapRect footprint, byte front, MapTile[] entrance)
        {
            Zone = zone; Kind = kind; Centre = centre; Footprint = footprint; Front = front; Entrance = entrance ?? new MapTile[0];
        }
    }
}
