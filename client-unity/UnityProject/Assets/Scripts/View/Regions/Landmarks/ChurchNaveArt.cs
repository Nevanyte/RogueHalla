using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Artist-owned spacing for the oak banks and the worn slate processional aisle.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Church pew banks and aisle", fileName = "NaveSeating")]
    public sealed class ChurchNaveArt : ScriptableObject
    {
        [Min(0f), Tooltip("Distance from the outside edge of either end wall to the first pew centre, in world units.")]
        public float OuterRowInset = 2.75f;
        [Min(.1f), Tooltip("Centre-to-centre distance between adjacent pews in a bank, in world units.")]
        public float RowSpacing = 1.95f;
        [Min(0f), Tooltip("Distance from the doorway axis to either bank centre, in world units.")]
        public float BankOffset = 2.96f;
        [Min(.1f), Tooltip("Full width of the worn slate path between the doors, in world units.")]
        public float AisleWidth = 3f;
        [Range(0f, 1f), Tooltip("Amount of pale wear on the aisle flagstones and their carved border.")]
        public float Wear = .72f;

        public static ChurchNaveArt Load() => Resources.Load<ChurchNaveArt>("StormInk/Church/Definitions/NaveSeating");

        // Map-space distances keep the flagstone courses, border and central carving on the doorway axis.
        internal static Color PaintAisle(Color slate, float across, float along, float width, float wear)
        {
            float x = Mathf.Abs(across);
            float half = width * .5f;
            Color field = slate * .91f;
            float edge = half - x;
            if (edge < -.06f) return field;
            float tooth = Mathf.Clamp01(slate.grayscale);
            float dry = Mathf.Clamp01((tooth - .12f) * 1.8f);
            float patch = Mathf.SmoothStep(0f, 1f, (edge + .06f) / .14f);
            Color worn = slate * (1.05f + .15f * dry) + new Color(.045f, .034f, .021f);
            Color stone = Color.Lerp(field, worn, wear * patch);

            // Recessed border and offset flagstone joints. A thin lip catches the slate pigment.
            float border = Mathf.Abs(edge - .08f + (tooth - .4f) * .035f);
            float rowPhase = along / 1.55f;
            int row = Mathf.FloorToInt(rowPhase);
            float rowJoint = Mathf.Abs(rowPhase - Mathf.Round(rowPhase)) * 1.55f;
            float colPhase = across / 1.48f + .5f * (row & 1);
            float colJoint = Mathf.Abs(colPhase - Mathf.Round(colPhase)) * 1.48f;
            float mortar = Mathf.Clamp01(1f - Mathf.Min(rowJoint, colJoint) / .029f);
            float carving = Mathf.Clamp01(1f - border / .033f);
            stone *= 1f - wear * (.36f * carving + .21f * mortar * patch);
            stone += new Color(.11f, .092f, .073f) * (wear * dry * Mathf.Clamp01(1f - Mathf.Abs(border - .049f) / .027f));

            // A single group of three engraved lozenges marks the open crossing.
            float middle = Mathf.Abs(along);
            float diamond = Mathf.Abs(x + middle * .82f - .30f);
            float pendant = Mathf.Abs(x + Mathf.Abs(middle - .55f) * .82f - .13f);
            float etch = Mathf.Clamp01(1f - Mathf.Min(diamond, pendant) / .036f);
            return stone * (1f - .56f * etch * (.64f + .36f * dry));
        }
    }
}
