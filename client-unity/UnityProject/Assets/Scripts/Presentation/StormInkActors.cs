using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Reads painted fighter poses from the public actor identity. Clip clocks and body sizing come from the original archetype.</summary>
    public static partial class StormInkActors
    {
        const int Columns = 4, Rows = 8, CreaturesCapacity = 30;
        const int Idle = 0, Stride = 1, Windup = 2, Strike = 3;
        static readonly Sheet[] Champions = new Sheet[5];
        static readonly Sheet[] Creatures = new Sheet[CreaturesCapacity];

        sealed class Sheet
        {
            public readonly Sprite[] Poses;
            public readonly int ColumnCount;
            public Sheet(Texture2D texture, int columns)
            {
                ColumnCount = columns;
                Poses = new Sprite[Rows * columns];
                if (texture == null || texture.width * Rows != texture.height * columns) return;
                int width = texture.width / columns, height = texture.height / Rows;
                for (int row = 0; row < Rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    var rect = new Rect(column * width, (Rows - 1 - row) * height, width, height);
                    var sprite = Sprite.Create(texture, rect, new Vector2(.5f, 5f / 128f),
                        height * .89f, 0, SpriteMeshType.FullRect, Vector4.zero, false);
                    sprite.name = texture.name + "/" + row + "/" + column;
                    Poses[row * columns + column] = sprite;
                }
            }
        }

        static Sheet Get(ActorKind kind, FactionId? faction, MobTypeId? mob)
        {
            if (kind == ActorKind.Player && faction.HasValue && (int)faction.Value < Champions.Length)
            {
                int i = (int)faction.Value;
                return Champions[i] ??= Load(faction.Value + "Champion");
            }
            if (kind == ActorKind.RegionalMob && mob.HasValue && (int)mob.Value < Creatures.Length &&
                ShowcaseIdentityAssets.TryGetMobInfo(mob.Value, out var info) && info.Faction == faction)
            {
                int i = (int)mob.Value;
                return Creatures[i] ??= Load(mob.Value.ToString(), MobColumns(mob.Value));
            }
            return null;
        }

        static int MobColumns(MobTypeId mob) => mob == MobTypeId.PoxBehemoth || mob == MobTypeId.BloodSaint ? 5 : Columns;

        static Sheet Load(string key, int columns = Columns) => new Sheet(Resources.Load<Texture2D>("StormInk/Actors/" + key), columns);

        // Catalog order is SW, W, NW, N, NE, E, SE, S. The painted rows start at south and turn east.
        public static int PoseRow(int catalogDirection) => (7 - catalogDirection) & 7;

        // Delivered headings use screen coordinates: east is zero and positive pi/2 faces south.
        public static int PoseRowForFacing(float facing) =>
            PoseRow(Mathf.RoundToInt(facing / (Mathf.PI / 4f)) + 5);

        /// <summary>Resolves an ink pose on the delivered actor's existing clip timeline. Returns null when the original art should be used.</summary>
        public static Sprite Frame(ActorKind kind, FactionId? faction, MobTypeId? mob,
            ShowcaseCatalog.Appearance appearance, Sprite[] clip, int direction, int frame, int count, out bool mirror)
        {
            mirror = false;
            if (count < 1) return null;
            var sheet = Get(kind, faction, mob);
            if (sheet == null) return null;
            int row = PoseRow(direction);
            int pose = Idle;
            if (clip == appearance.Walk) pose = frame * 2 / count == 0 ? Idle : Stride;
            else if (clip == appearance.Attack || clip == appearance.Cast || clip == appearance.Shoot)
                pose = frame * 4 < count ? Windup : frame * 4 < 3 * count ? Strike : sheet.ColumnCount == 5 ? 4 : Stride;
            else if (clip == appearance.Hit || clip == appearance.Block || clip == appearance.Death)
                pose = mob == MobTypeId.BloodSaint ? Idle : Windup;
            return sheet.Poses[row * sheet.ColumnCount + pose];
        }
    }
}
