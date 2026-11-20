using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Artist-owned pigment, stone scale and carving for the walls enclosing and dividing the nave.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Church masonry", fileName = "NaveMasonry")]
    public sealed class ChurchMasonryArt : ScriptableObject
    {
        [Tooltip("Repeatable pigment. Enable Read/Write on replacement textures.")] public Texture2D Wash;
        [Tooltip("Flat relief painting on the upright wall face. Enable Read/Write on replacement textures.")] public Texture2D CarvedFace;
        [Tooltip("Horizontal UV limits of one repeatable bay in the face painting.")] public Vector2 FaceBayUv = new Vector2(.343f, .657f);
        public Color Stone = new Color(.46f, .44f, .455f);
        public Color Ink = new Color(.145f, .14f, .17f);
        public Color CutEdge = new Color(.69f, .64f, .60f);
        [Range(1, 4), Tooltip("Stone lengths per world unit on a wall face.")] public int StonesPerUnit = 2;
        [Range(.12f, .45f), Tooltip("Height of each stone course in world units.")] public float CourseHeight = .24f;
        [Range(.35f, 1f), Tooltip("Upright height of the interior wall section in world units.")] public float InteriorHeight = .82f;
        [Range(0f, 1f)] public float CarvingStrength = .90f;
        [Range(.5f, 6f), Tooltip("Width of the watercolor repeat in world units.")] public float WashScale = 2.6f;
        Color32[] pigment;
        int imageWidth, imageHeight;
        Color32[] relief;
        int reliefWidth, reliefHeight;

        public static ChurchMasonryArt Load() => Resources.Load<ChurchMasonryArt>("StormInk/Church/Definitions/NaveMasonry");

        internal void ReadPigment()
        {
            relief = CarvedFace != null && CarvedFace.isReadable ? CarvedFace.GetPixels32() : null;
            if (relief != null) { reliefWidth = CarvedFace.width; reliefHeight = CarvedFace.height; }
            if (Wash == null || !Wash.isReadable) { pigment = null; return; }
            pigment = Wash.GetPixels32();
            imageWidth = Wash.width;
            imageHeight = Wash.height;
        }

        float Paper(float x, float y)
        {
            if (pigment == null) return .76f;
            int i = Mathf.FloorToInt(Mathf.Repeat(x / WashScale, 1f) * imageWidth);
            int j = Mathf.FloorToInt(Mathf.Repeat(y / WashScale, 1f) * imageHeight);
            Color32 p = pigment[j * imageWidth + i];
            return (p.r * .27f + p.g * .57f + p.b * .16f) / 255f;
        }

        // Running bond in wall coordinates; hand-painted pigment crosses the joints and is lit by the arena.
        internal Color Masonry(float x, float y, bool coping)
        {
            float pigmentValue = Paper(x, y);
            float brokenEdge = (pigmentValue - .69f) * .075f;
            float v = (y + brokenEdge + .008f * Mathf.Sin(x * 4.8f + y)) / (coping ? .42f : CourseHeight);
            int row = Mathf.FloorToInt(v);
            float u = (x + brokenEdge) * StonesPerUnit + (row & 1) * .5f;
            int stone = Mathf.FloorToInt(u);
            float fx = u - stone, fy = v - row;
            float tone = RegionNoise.Hash01(stone, row, 79);
            float wash = Mathf.Clamp((pigmentValue - .69f) * 2.75f, -.24f, .23f);
            Color body = RegionCanvas.Shade(Stone, .94f + wash + tone * .13f);
            float edge = Mathf.Min(Mathf.Min(fx, 1f - fx) / StonesPerUnit,
                Mathf.Min(fy, 1f - fy) * (coping ? .42f : CourseHeight));
            float joint = 1f - Mathf.SmoothStep(0f, 1f, edge / .019f);
            body = Color.Lerp(body, Ink, joint * (.35f + .18f * tone));
            float catchLight = Mathf.Clamp01(1f - Mathf.Abs(edge - .025f) / .015f);
            return Color.Lerp(body, CutEdge, catchLight * (.11f + .12f * pigmentValue));
        }

        // The albedo plate is mounted on the upright face. Its pilasters meet at the two authored UV limits.
        internal Color Arcade(Color stone, float u, float v)
        {
            if (relief == null || CarvingStrength <= 0f) return stone;
            float column = Mathf.Lerp(FaceBayUv.x, FaceBayUv.y, Mathf.Repeat(u, 1f));
            int x = Mathf.Clamp(Mathf.FloorToInt(column * reliefWidth), 0, reliefWidth - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(v * reliefHeight), 0, reliefHeight - 1);
            Color32 sample = relief[y * reliefWidth + x];
            Color carving = new Color(stone.r * sample.r / 143f, stone.g * sample.g / 136f,
                stone.b * sample.b / 137f);
            return Color.Lerp(stone, carving, CarvingStrength);
        }

        internal Color Cap(float x, float y, bool alongX)
        {
            float along = alongX ? x : y, across = alongX ? y : x;
            float paper = Paper(x, y);
            float phase = Mathf.Repeat(along + (paper - .69f) * .070f, .79f);
            float joint = Mathf.Min(phase, .79f - phase);
            Color surface = RegionCanvas.Shade(Stone, 1.07f + Mathf.Clamp((paper - .69f) * 3.1f, -.24f, .24f));
            float dryBrush = Mathf.Abs(Mathf.Sin(across * 31f + along * 1.25f)) * .08f;
            surface = Color.Lerp(surface, CutEdge, dryBrush);
            return Color.Lerp(surface, Ink, .56f * Mathf.Clamp01(1f - joint / .023f));
        }

        internal RegionCanvas Tile(bool top, int variant)
        {
            var canvas = new RegionCanvas(RegionWallAtlas.TileW, top ? RegionWallAtlas.TopH : RegionWallAtlas.FaceH);
            for (int y = 0; y < canvas.H; y++)
            for (int x = 0; x < canvas.W; x++)
            {
                float u = (x + .5f) / canvas.W, v = (y + .5f) / canvas.H;
                Color stone = Masonry(u + variant, v * (top ? ArenaView.GroundSquash : RegionWalls.WallHeight), top);
                if (!top) stone = Arcade(stone, u, v);
                canvas.Px[y * canvas.W + x] = stone;
            }
            return canvas;
        }
    }
}
