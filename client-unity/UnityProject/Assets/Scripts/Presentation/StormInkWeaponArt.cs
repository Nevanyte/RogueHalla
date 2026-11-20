using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Owns the five physical item turntables used in the world. The art has no faction input.</summary>
    public static class StormInkWeaponArt
    {
        const int Views = 8;
        // Order matches WeaponId on the wire. Fists use the unarmed presentation.
        static readonly string[] Textures = { "SwordViews", "BowViews", "WandViews", "ShieldViews", "AxeViews" };
        static readonly Sprite[][] Sheets = new Sprite[Textures.Length][];

        // Grip positions measured in the 256px authored cells, ordered S, SE, E, NE, N, NW, W, SW.
        static readonly float[,] GripX =
        {
            { .500f, .500f, .500f, .502f, .498f, .500f, .498f, .494f }, // sword handle
            { .568f, .537f, .500f, .453f, .432f, .477f, .498f, .531f }, // bow binding
            { .500f, .492f, .490f, .496f, .500f, .504f, .506f, .504f }, // wand grip
            { .500f, .500f, .500f, .500f, .500f, .500f, .500f, .500f }, // shield straps
            { .385f, .432f, .498f, .564f, .611f, .568f, .502f, .406f }, // axe haft
        };
        static readonly float[] GripY = { .17f, .50f, .18f, .49f, .27f };
        static readonly float[] BowstringX = { .395f, .420f, .500f, .590f, .607f, .570f, .498f, .418f };

        public static Sprite Get(WeaponId weapon) => Get(weapon, 0);

        public static Sprite Get(WeaponId weapon, int poseRow)
        {
            int index = (int)weapon;
            if ((uint)index >= Textures.Length || (uint)poseRow >= Views) return null;
            var sprites = Sheets[index] ??= Load(Textures[index]);
            return sprites[poseRow];
        }

        static Sprite[] Load(string name)
        {
            var sprites = new Sprite[Views];
            var texture = Resources.Load<Texture2D>("StormInk/Weapons/" + name);
            if (texture == null || texture.width != 2 * texture.height) return sprites;
            int side = texture.width / 4;
            for (int view = 0; view < Views; view++)
            {
                var rect = new Rect((view % 4) * side, (1 - view / 4) * side, side, side);
                var sprite = Sprite.Create(texture, rect, new Vector2(.5f, .5f), side, 0,
                    SpriteMeshType.FullRect, Vector4.zero, false);
                sprite.name = name + "/" + view;
                sprites[view] = sprite;
            }
            return sprites;
        }

        public static Vector2 Grip(WeaponId weapon, int poseRow)
        {
            int index = (int)weapon;
            if ((uint)index >= Textures.Length || (uint)poseRow >= Views) return new Vector2(.5f, .5f);
            var grip = new Vector2(GripX[index, poseRow], GripY[index]);
            return weapon == WeaponId.Axe ? StormInkAxeArtAuthoring.Load()?.Grip(poseRow, grip) ?? grip : grip;
        }

        public static Vector2 GroundCentre(WeaponId weapon) =>
            weapon == WeaponId.Axe ? StormInkAxeArtAuthoring.Load()?.GroundCentre ?? new Vector2(0, .39f) : new Vector2(0, .39f);

        public static Vector2 Support(WeaponId weapon, int poseRow)
        {
            var grip = Grip(weapon, poseRow);
            if ((uint)poseRow >= Views) return grip;
            if (weapon == WeaponId.Bow) return new Vector2(BowstringX[poseRow], grip.y);
            if (weapon == WeaponId.Axe) return new Vector2(grip.x, .54f);
            return grip;
        }
    }
}
