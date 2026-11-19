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

        // The painting already includes its stepped footing. Seat its front edge on the
        // blocked group and leave a low, chamfered socket visible around the back.
        static void BuildStormInkPier(Landmark lm, Group group, Sprite pier)
        {
            float baseY = -(group.MaxY + 1) * Sq;
            float width = group.MaxX - group.MinX + 1;
            float depth = (group.MaxY - group.MinY + 1) * Sq;
            float centreX = group.Centre.x;
            const float skin = .07f;
            var socket = new Canvas(centreX - width * .5f, baseY, width, depth + skin, 20);
            socket.Paint((x, y) =>
            {
                float across = Mathf.Abs(x - centreX);
                float along = y - baseY;
                float rimX = width * .5f - across;
                float rimY = Mathf.Min(along, depth + skin - along);
                float rim = Mathf.Min(Mathf.Min(rimX, rimY), (rimX + rimY - .20f) * .71f);
                if (rim < 0f) return default;
                float brush = Noise(x * .95f, y * 2f, 17);
                var pigment = Color.Lerp(new Color(.56f, .51f, .42f), new Color(.70f, .64f, .52f), brush);
                return Shade(pigment, .77f + .23f * Mathf.Clamp01(rim / .12f));
            });
            int order = 2000 + 16 * group.MaxY + 1;
            var foot = socket.ToSprite(lm, "Hall footing", "Hall column", order);
            var painted = StormInkLandmarkSprite.Place(lm, pier, "Holy sun pier",
                new Vector2(centreX, baseY + .18f), new Vector2(width, 4.38f), new Vector2(.5f, .06f), order + 1);
            // Cool the bays. The altar and the furnishings on the nave retain their warmer pigment.
            painted.color = new Color(.79f, .81f, .80f);
            painted.transform.SetParent(foot.transform, true);
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
