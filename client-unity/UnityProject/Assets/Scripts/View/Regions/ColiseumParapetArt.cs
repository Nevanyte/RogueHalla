using UnityEngine;

namespace Roguehalla.Client
{
    [CreateAssetMenu(menuName = "RogueHalla/Coliseum parapet")]
    public sealed class ColiseumParapetArt : ScriptableObject
    {
        [Tooltip("Flat albedo sheet shared by the coping and the upright wall. Enable Read/Write in the importer.")]
        public Texture2D Pigment;
        [Tooltip("Normalized rectangle for the overhead coping. The strip spans three world units.")]
        public Rect CopingUv = new Rect(0f, .5f, 1f, .5f);
        [Tooltip("Normalized rectangle for the upright frieze. Its top edge meets the coping.")]
        public Rect FriezeUv = new Rect(0f, 0f, 1f, .5f);
        public Color CopingTint = Color.white;
        public Color FriezeTint = Color.white;

        internal static ColiseumParapetArt Load() =>
            Resources.Load<ColiseumParapetArt>("StormInk/Coliseum/ColiseumParapetArt");

        internal Color32[] ReadPigment() =>
            Pigment != null && Pigment.isReadable ? Pigment.GetPixels32() : null;

        internal RegionCanvas Tile(bool coping, int section, Color32[] pigment)
        {
            var canvas = new RegionCanvas(RegionWallAtlas.TileW, coping ? RegionWallAtlas.TopH : RegionWallAtlas.FaceH);
            Rect uv = coping ? CopingUv : FriezeUv;
            Color tint = coping ? CopingTint : FriezeTint;
            int sections = coping ? RegionWallAtlas.TopVariants : RegionWallAtlas.FaceVariants;
            int w = Pigment.width, h = Pigment.height;
            for (int y = 0; y < canvas.H; y++)
            {
                float v = uv.y + (y + .5f) / canvas.H * uv.height;
                int row = Mathf.Clamp(Mathf.FloorToInt(v * h), 0, h - 1) * w;
                for (int x = 0; x < canvas.W; x++)
                {
                    float u = uv.x + (section + (x + .5f) / canvas.W) / sections * uv.width;
                    Color color = pigment[row + Mathf.Clamp(Mathf.FloorToInt(u * w), 0, w - 1)];
                    Color stone = color * tint;
                    stone.a = 1f;
                    canvas.Px[y * canvas.W + x] = stone;
                }
            }
            return canvas;
        }
    }
}
