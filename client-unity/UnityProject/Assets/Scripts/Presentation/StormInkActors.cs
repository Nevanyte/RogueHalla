using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Reads painted fighter poses from the public actor identity. Clip clocks and body sizing come from the original archetype.</summary>
    public static class StormInkActors
    {
        const int Columns = 4, Rows = 4;
        const int Idle = 0, Stride = 1, Windup = 2, Strike = 3;
        static readonly Sheet[] Champions = new Sheet[5];
        static readonly Sheet[] Creatures = new Sheet[30];

        sealed class Sheet
        {
            public readonly Sprite[] Poses = new Sprite[Rows * Columns];
            public Sheet(Texture2D texture)
            {
                if (texture == null) return;
                int width = texture.width / Columns, height = texture.height / Rows;
                for (int row = 0; row < Rows; row++)
                for (int column = 0; column < Columns; column++)
                {
                    var rect = new Rect(column * width, (Rows - 1 - row) * height, width, height);
                    var sprite = Sprite.Create(texture, rect, new Vector2(.5f, 5f / 128f),
                        height * .89f, 0, SpriteMeshType.FullRect, Vector4.zero, false);
                    sprite.name = texture.name + "/" + row + "/" + column;
                    Poses[row * Columns + column] = sprite;
                }
            }
        }

        static Sheet Get(ActorKind kind, FactionId? faction, MobTypeId? mob)
        {
            if (kind == ActorKind.Player && faction == FactionId.Sentenced)
            {
                int i = (int)faction.Value;
                return Champions[i] ??= Load("SentencedChampion");
            }
            if (kind == ActorKind.RegionalMob && mob == MobTypeId.Executioner && faction == FactionId.Sentenced)
            {
                int i = (int)mob.Value;
                return Creatures[i] ??= Load("Executioner");
            }
            return null;
        }

        static Sheet Load(string key) => new Sheet(Resources.Load<Texture2D>("StormInk/Actors/" + key));

        /// <summary>Resolves an ink pose on the delivered actor's existing clip timeline. Returns null when the original art should be used.</summary>
        public static Sprite Frame(ActorKind kind, FactionId? faction, MobTypeId? mob,
            ShowcaseCatalog.Appearance appearance, Sprite[] clip, int direction, int frame, int count, out bool mirror)
        {
            mirror = false;
            if (count < 1) return null;
            var sheet = Get(kind, faction, mob);
            if (sheet == null) return null;
            // Direction-major order shared with the original clips: SW W NW N NE E SE S.
            mirror = direction < 3;
            int row = direction == 3 ? 3 : direction == 2 || direction == 4 ? 2 :
                direction == 1 || direction == 5 ? 1 : 0;
            int pose = Idle;
            if (clip == appearance.Walk) pose = frame * 2 / count == 0 ? Idle : Stride;
            else if (clip == appearance.Attack || clip == appearance.Cast || clip == appearance.Shoot)
                pose = frame * 4 < count ? Windup : frame * 4 < 3 * count ? Strike : Stride;
            else if (clip == appearance.Hit || clip == appearance.Block || clip == appearance.Death) pose = Windup;
            var sprite = sheet.Poses[row * Columns + pose];
            if (sprite == null) mirror = false;
            return sprite;
        }
    }
}
