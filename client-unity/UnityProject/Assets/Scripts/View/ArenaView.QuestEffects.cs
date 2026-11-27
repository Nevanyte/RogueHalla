// Renders authoritative quest barriers and transient world cues using the arena artwork pool.
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    public sealed partial class ArenaView
    {
        Sprite _questCageFence;

        void PresentQuestCage(AreaView cage)
        {
            if (cage.Tiles == null || cage.Tiles.Length == 0) return;
            if (_questCageFence == null)
            {
                var texture = RegionWallAtlas.Atlas;
                var cell = RegionWallAtlas.LowFor(RegionWallAtlas.SetFor(WallStyle.Timber), 2)[0];
                var pixels = new Vector2(texture.width, texture.height);
                var min = Vector2.Scale(cell.Min, pixels);
                var size = Vector2.Scale(cell.Max - cell.Min, pixels);
                _questCageFence = Sprite.Create(texture, new Rect(min, size), new Vector2(.5f, .42f), RegionWallAtlas.TileW);
            }
            foreach (var tile in cage.Tiles)
            {
                float x = tile.X * 32 + 16, y = tile.Y * 32 + 16;
                int order = 2000 + Mathf.RoundToInt(y * .5f);
                var position = WorldPoint(x, y);
                Prop("shadow", position, new Vector2(.88f, .35f), Color.white, order);
                bool vertical = Mathf.Abs(x - cage.X) > Mathf.Abs(y - cage.Y);
                var scale = vertical ? new Vector2(GroundSquash, .78f) : new Vector2(1f, .78f);
                var fence = Prop("stone", position + new Vector3(0, .12f), scale, Color.white, order + 3, overrideSprite: _questCageFence);
                if (vertical) fence.transform.rotation = Quaternion.Euler(0, 0, 90);
            }
        }

        void PresentQuestCue(AreaView cue)
        {
            if ((byte)cue.Kind < FieldsState.KindQuestTurret) return;
            var at = WorldPoint(cue.X, cue.Y);
            int order = 2000 + Mathf.RoundToInt(cue.Y * .5f);
            var gold = ShowcasePalette.Gold;
            var light = ShowcasePalette.Text;
            var ward = ShowcasePalette.Faction(2);
            switch (cue.Kind)
            {
                case AreaKind.QuestTurret:
                    Prop("shadow", at, new Vector2(.9f, .38f), Color.white, order);
                    Prop("stone", at + new Vector3(0, .12f), new Vector2(.56f, .38f), ShowcasePalette.MutedText, order + 1);
                    var barrel = Prop("bar", at + new Vector3(.11f, .38f), new Vector2(.67f, .14f), gold, order + 2);
                    barrel.transform.rotation = Quaternion.Euler(0, 0, 30);
                    Prop("rune", at + new Vector3(-.07f, .26f), new Vector2(.25f, .25f), light, order + 3);
                    break;
                case AreaKind.QuestWard:
                    Prop("ring", at, new Vector2(1.1f, .76f), ward, order);
                    Prop("bar", at + new Vector3(0, .32f), new Vector2(.12f, .68f), ward, order + 1);
                    Prop("spark", at + new Vector3(0, .62f), new Vector2(.37f, .42f), light, order + 2);
                    break;
                case AreaKind.QuestFalcon:
                    var heading = WorldPoint(cue.X2, cue.Y2) - at;
                    float angle = heading.sqrMagnitude > .001f ? Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg : 0;
                    var left = Prop("bar", at + new Vector3(-.15f, .5f), new Vector2(.47f, .12f), gold, order);
                    var right = Prop("bar", at + new Vector3(.15f, .5f), new Vector2(.47f, .12f), gold, order);
                    left.transform.rotation = Quaternion.Euler(0, 0, angle + 29);
                    right.transform.rotation = Quaternion.Euler(0, 0, angle - 29);
                    Prop("spark", at + new Vector3(0, .59f), new Vector2(.24f, .3f), light, order + 1);
                    break;
                case AreaKind.QuestHook:
                    Prop("ring", at + new Vector3(0, .3f), new Vector2(.46f, .47f), gold, order);
                    Prop("bar", at + new Vector3(-.24f, .34f), new Vector2(.27f, .1f), light, order + 1);
                    break;
                case AreaKind.QuestBolas:
                    Prop("bar", at + new Vector3(0, .3f), new Vector2(.55f, .07f), light, order);
                    Prop("stone", at + new Vector3(-.25f, .3f), new Vector2(.24f, .24f), gold, order + 1);
                    Prop("stone", at + new Vector3(.25f, .3f), new Vector2(.24f, .24f), gold, order + 1);
                    break;
                case AreaKind.QuestTornado:
                    bool imminent = _sourceTimeMs < cue.EndsAt;
                    Prop("ring", at, new Vector2(1.55f, 1.05f), imminent ? gold : light, order);
                    Prop("ring", at + new Vector3(0, .35f), new Vector2(.93f, .55f), light, order + 1);
                    Prop("ring", at + new Vector3(0, .68f), new Vector2(.58f, .32f), light, order + 2);
                    break;
                case AreaKind.QuestSeed:
                    // This cue is privately delivered to the planter, including in fog.
                    Prop("ring", at, new Vector2(.78f, .52f), ward, VisionFog.SortingOrder + 1);
                    Prop("rune", at + new Vector3(0, .24f), new Vector2(.4f, .4f), ward, VisionFog.SortingOrder + 2);
                    break;
                case AreaKind.QuestCrown:
                    Prop("ring", at, new Vector2(1.1f, .7f), gold, order);
                    Prop("rune", at + new Vector3(0, 1.05f), new Vector2(.42f, .42f), gold, order + 1);
                    break;
                case AreaKind.QuestSmuggler:
                    Prop("ring", at, new Vector2(.94f, .60f), ShowcasePalette.Faction(1), order);
                    Prop("bar", at + new Vector3(0, .85f), new Vector2(.35f, .24f), gold, order + 1);
                    break;
                case AreaKind.QuestFort:
                    Prop("ring", at, new Vector2(1.04f, .68f), ShowcasePalette.Faction(3), order);
                    Prop("stone", at + new Vector3(0, .95f), new Vector2(.47f, .19f), light, order + 1);
                    break;
                case AreaKind.QuestMeditation:
                    Prop("ring", at, new Vector2(.83f, .52f), ward, order);
                    Prop("spark", at + new Vector3(0, .92f), new Vector2(.25f, .33f), light, order + 1);
                    break;
                case AreaKind.QuestBounty:
                    Prop("ring", at + new Vector3(0, .95f), new Vector2(.37f, .37f), ShowcasePalette.Faction(0), order);
                    Prop("spark", at + new Vector3(0, .95f), new Vector2(.14f, .18f), light, order + 1);
                    break;
            }
        }
    }
}
