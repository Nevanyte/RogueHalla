using System.Collections.Generic;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Draws the delivered Hall's upright piers, altar and paired aisle lamps.</summary>
    public static partial class HallLandmark
    {
        static void BuildStormInk(Landmark lm, TempleLayout temple)
        {
            var pier = Resources.Load<Sprite>("StormInk/Holy/SunPier");
            foreach (var group in temple.SignaturePiers)
            {
                if (pier == null) BuildColumn(lm, group);
                else BuildStormInkPier(lm, group, pier);
            }

            var altar = Resources.Load<Sprite>("StormInk/Holy/SunShrine");
            if (altar == null) BuildShrine(lm, temple.ShrineWalls);
            else BuildStormInkShrine(lm, temple, altar);

            var lamp = Resources.Load<Sprite>("StormInk/Holy/AisleLamp");
            foreach (var point in temple.Lamps)
            {
                var contact = new Vector2(point.x, -point.y * Sq);
                int order = StormInkLandmarkSprite.RowOrder(point.y);
                if (lamp == null) Post(lm, "Holy aisle lamp", contact.x, contact.y, .58f, .85f, true, order);
                else StormInkLandmarkSprite.Place(lm, lamp, "Holy aisle lamp", contact,
                    new Vector2(.82f, TempleLayout.AisleLampHeight), new Vector2(.5f, TempleLayout.LampFoot), order, flame: true);
            }
        }

        // The painting includes the stepped base that meets the floor.
        static void BuildStormInkPier(Landmark lm, Group group, Sprite pier)
        {
            float baseY = -(group.MaxY + 1) * Sq;
            float width = group.MaxX - group.MinX + 1;
            float centreX = group.Centre.x;
            var column = new GameObject("Hall column");
            column.transform.SetParent(lm.Root.transform, false);
            column.transform.localPosition = new Vector3(centreX, baseY, 0f);
            int order = 2000 + 16 * group.MaxY + 2;
            var painted = StormInkLandmarkSprite.Place(lm, pier, "Holy sun pier",
                new Vector2(centreX, baseY + .18f), new Vector2(width, 4.38f), new Vector2(.5f, .06f), order);
            // Cool the bays. The altar and the furnishings on the nave retain their warmer pigment.
            painted.color = new Color(.79f, .81f, .80f);
            painted.transform.SetParent(column.transform, true);
        }

        static void BuildStormInkShrine(Landmark lm, TempleLayout temple, Sprite altar)
        {
            var walls = temple.ShrineWalls;
            var door = temple.ShrineDoor;
            var cover = new List<MapTile>();
            for (int y = walls.MinY; y <= walls.MaxY; y++) for (int x = walls.MinX; x <= walls.MaxX; x++)
                if ((lm.IsWall(x, y) || lm.IsInterior(x, y)) && !door.Contains(new Vector2Int(x, y)))
                    cover.Add(new MapTile((ushort)x, (ushort)y));
            lm.SetCover(cover);

            DoorNotch(walls, door, out bool horizontal, out bool outwardNorthOrWest);
            var lamp = Resources.Load<Sprite>("StormInk/Holy/AisleLamp");
            if (lamp == null) BuildDoor(lm, door, horizontal, outwardNorthOrWest);
            else
            {
                foreach (var end in temple.ShrineLamps)
                    StormInkLandmarkSprite.Place(lm, lamp, "Hall door", new Vector2(end.x, -end.y * Sq),
                        new Vector2(.63f, TempleLayout.JambLampHeight), new Vector2(.5f, TempleLayout.LampFoot),
                        StormInkLandmarkSprite.RowOrder(end.y), flame: true);
            }
            BuildLight(lm);

            Vector2 p = temple.Shrine;
            var sprite = StormInkLandmarkSprite.Place(lm, altar, "Holy sun shrine", new Vector2(p.x, -p.y * Sq),
                new Vector2(4.35f, 4.6f), new Vector2(.5f, .18f), 0);
            // The real shrine's wall and door masks still decide when the altar yields to an actor.
            lm.AddRoofRenderer(sprite);
        }
    }
}
