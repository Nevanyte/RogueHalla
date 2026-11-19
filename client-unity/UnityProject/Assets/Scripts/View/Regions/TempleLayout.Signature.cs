using System.Collections.Generic;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Derives the signature Hall's single axis and furniture plan from public map geometry.</summary>
    public sealed partial class TempleLayout
    {
        public bool IsSignature { get { return SignatureAnchor != null; } }
        public Vector2 Entrance { get; private set; }
        public Vector2 Door { get; private set; }
        public Vector2 Shrine { get; private set; }

        internal MapAnchor SignatureAnchor { get; private set; }
        internal List<HallLandmark.Group> SignaturePiers { get; private set; }
        internal HallLandmark.Group ShrineWalls { get; private set; }
        internal List<Vector2Int> ShrineDoor { get; private set; }

        bool TryBuildSignature(MapBlob map)
        {
            foreach (var anchor in map.Anchors)
                if (anchor.Kind == MapBlob.AnchorKindHall && anchor.Zone == (byte)RegionId.Holy && TryBuildSignature(map, anchor))
                    return true;
            return false;
        }

        bool TryBuildSignature(MapBlob map, MapAnchor anchor)
        {
            var f = anchor.Footprint;
            if (anchor.Entrance.Length == 0 || f.W == 0 || f.W != f.H || f.X + f.W > map.W || f.Y + f.H > map.H)
                return false;

            var bounds = new RectInt(f.X, f.Y, f.W, f.H);
            var groups = HallLandmark.FindGroups(bounds, (x, y) => map.At(x, y) == MapBlob.Wall);
            var shrine = HallLandmark.ShrineGroup(groups);
            var piers = groups.FindAll(g => g.IsPier);
            if (shrine == null || piers.Count < 6) return false;

            var doors = HallLandmark.DoorTiles(shrine, (x, y) => map.At(x, y) == MapBlob.Floor);
            if (doors.Count == 0) return false;

            var entrance = Vector2.zero;
            foreach (var tile in anchor.Entrance) entrance += new Vector2(tile.X + .5f, tile.Y + .5f);
            entrance /= anchor.Entrance.Length;

            var door = Vector2.zero;
            foreach (var tile in doors) door += new Vector2(tile.x + .5f, tile.y + .5f);
            door /= doors.Count;

            Vector2 direction = door - entrance;
            if (direction.sqrMagnitude < 1f || Mathf.Min(Mathf.Abs(direction.x), Mathf.Abs(direction.y)) > .01f)
                return false;
            var axis = direction.normalized;
            var across = new Vector2(-axis.y, axis.x);

            // The public healing-light patch supplies the altar's depth. Its footing stays on the door axis.
            float depth = Vector2.Dot(shrine.Centre - entrance, axis);
            int lit = 0;
            float lightDepth = 0f;
            for (int y = shrine.MinY + 1; y < shrine.MaxY; y++) for (int x = shrine.MinX + 1; x < shrine.MaxX; x++)
            {
                if (map.Terrain == null || map.Terrain[y * map.W + x] != (byte)TerrainKind.Light) continue;
                lightDepth += Vector2.Dot(new Vector2(x + .5f, y + .5f) - entrance, axis);
                lit++;
            }
            if (lit > 0) depth = lightDepth / lit;
            if (depth <= direction.magnitude) return false;

            SignatureAnchor = anchor;
            SignaturePiers = piers;
            ShrineWalls = shrine;
            ShrineDoor = doors;
            Entrance = entrance;
            Door = door;
            Shrine = entrance + depth * axis;
            Hall = bounds;
            X0 = f.X; Y0 = f.Y; Size = f.W; Found = true; Reason = "";
            CenterX = (Entrance.x + Shrine.x) * .5f;
            CenterY = (Entrance.y + Shrine.y) * .5f;

            for (int y = bounds.yMin; y < bounds.yMax; y++) for (int x = bounds.xMin; x < bounds.xMax; x++)
                if (map.Regions[y * map.W + x] == (byte)RegionId.Holy) HolyInside++;

            _block = new byte[map.W * map.H];
            foreach (var pier in piers)
            {
                Pillars.Add(new Vector2Int(pier.MinX, pier.MinY));
                foreach (var tile in pier.Tiles)
                    _block[tile.y * MapW + tile.x] = (byte)(1 + tile.x - pier.MinX + 3 * (tile.y - pier.MinY));
            }

            AddLampPair(map, entrance + direction * (3f / 29f), across);
            AddLampPair(map, entrance + direction * (16f / 29f), across);
            AddLampPair(map, entrance + direction * (26f / 29f), across);
            return true;
        }

        void AddLampPair(MapBlob map, Vector2 centre, Vector2 across)
        {
            const float AisleLampOffset = 2.15f;
            var left = centre + across * AisleLampOffset;
            var right = centre - across * AisleLampOffset;
            if (!PlainFloor(map, left) || !PlainFloor(map, right)) return;
            Lamps.Add(left);
            Lamps.Add(right);
        }

        static bool PlainFloor(MapBlob map, Vector2 p)
        {
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y);
            return (uint)x < (uint)map.W && (uint)y < (uint)map.H && map.At(x, y) == MapBlob.Floor &&
                (map.Terrain == null || map.Terrain[y * map.W + x] == 0);
        }

        internal void BindSignatureGround(Material material)
        {
            if (!IsSignature) return;
            var stone = Resources.Load<Texture2D>("StormInk/Holy/TempleStone");
            material.SetVector("_StormHallAxis", new Vector4(Entrance.x, Entrance.y, Shrine.x, Shrine.y));
            material.SetVector("_StormHallPlan", new Vector4(1f, stone != null ? 1f : 0f, Vector2.Distance(Entrance, Door), 0f));
            if (stone != null) material.SetTexture("_StormTempleStone", stone);
        }
    }
}
