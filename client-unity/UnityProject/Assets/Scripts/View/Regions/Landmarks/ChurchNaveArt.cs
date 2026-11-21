using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Artist-owned spacing for the oak banks and the worn slate processional aisle.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Church pew banks and aisle", fileName = "NaveSeating")]
    public sealed class ChurchNaveArt : ScriptableObject
    {
        [Min(0f), Tooltip("Distance from the outside edge of either end wall to the first pew centre, in world units.")]
        public float OuterRowInset = 2.90f;
        [Min(.1f), Tooltip("Centre-to-centre distance between adjacent pews in a bank, in world units.")]
        public float RowSpacing = 1.45f;
        [Min(0f), Tooltip("Distance from the doorway axis to either bank centre, in world units.")]
        public float BankOffset = 2.96f;
        [Min(.1f), Tooltip("Full width of the worn slate path between the doors, in world units.")]
        public float AisleWidth = 3f;
        [Range(0f, 1f), Tooltip("Strength of the worn path across the aisle flagstones.")]
        public float Wear = .72f;

        [Tooltip("Top-down flagstones for the complete aisle, with the sanctuary at the top. Enable Read/Write on replacement textures.")]
        public Texture2D Flagstones;
        [Range(0f, 1f), Tooltip("Pigment in the central stone engraving.")] public float CarvingStrength = .62f;

        public static ChurchNaveArt Load() => Resources.Load<ChurchNaveArt>("StormInk/Church/Definitions/NaveSeating");

        internal AislePainting CreatePainting(Vector2Int front, bool mirrored, float length) =>
            new AislePainting(this, front, mirrored, length);

        // The floor builder reads the plate once per nave. Shading and light spill come from the arena.
        internal readonly struct AislePainting
        {
            readonly Color32[] pixels;
            readonly int columns, rows;
            readonly Vector2Int front, transverse;
            readonly float width, length, wear, carving;

            internal AislePainting(ChurchNaveArt art, Vector2Int front, bool mirrored, float length)
            {
                var plate = art.Flagstones;
                pixels = plate != null && plate.isReadable ? plate.GetPixels32() : null;
                columns = pixels != null ? plate.width : 0;
                rows = pixels != null ? plate.height : 0;
                this.front = front;
                transverse = new Vector2Int(front.y, -front.x) * (mirrored ? -1 : 1);
                width = Mathf.Max(.1f, art.AisleWidth);
                this.length = Mathf.Max(.1f, length);
                wear = Mathf.Clamp01(art.Wear);
                carving = Mathf.Clamp01(art.CarvingStrength);
            }

            internal Color Paint(Color slate, float dx, float dy)
            {
                Color field = slate * .91f;
                float across = dx * transverse.x + dy * transverse.y;
                float edge = width * .5f - Mathf.Abs(across);
                if (pixels == null || edge <= 0f) return field;

                float along = dx * front.x + dy * front.y;
                float u = .5f + across / width, v = .5f - along / length;
                // Crop the outermost cut edges of the source. Each course is painted once from sill to sill.
                int x = Mathf.RoundToInt(Mathf.Lerp(.025f, .975f, u) * (columns - 1));
                int y = Mathf.RoundToInt(Mathf.Lerp(.015f, .985f, v) * (rows - 1));
                Color wash = pixels[y * columns + x];
                float value = wash.grayscale;
                Color stone = Color.Lerp(new Color(value, value, value * 1.035f), wash, .40f);
                float footsteps = Mathf.Clamp01(1f - 2f * Mathf.Abs(u - .5f));
                stone *= 1f + wear * (.14f + .10f * footsteps);

                // Let the outer stones meet the approved slate field through a broken, feathered joint.
                float join = Mathf.Min(width * .24f, .32f) * (.78f + .42f * value);
                float blend = Mathf.SmoothStep(0f, 1f, edge / join);
                stone = Color.Lerp(field, stone, blend);

                // Three recessed lozenges mark the open crossing.
                float a = Mathf.Abs(across), b = Mathf.Abs(along);
                float centreCut = Mathf.Abs(a + b * .78f - .33f);
                float pendant = Mathf.Abs(a + Mathf.Abs(b - .61f) * .78f - .14f);
                float cut = Mathf.Min(centreCut, pendant);
                float ink = Mathf.Clamp01(1f - cut / .050f) * carving;
                return stone * (1f - ink * (.66f + .25f * value) * blend);
            }
        }
    }
}
