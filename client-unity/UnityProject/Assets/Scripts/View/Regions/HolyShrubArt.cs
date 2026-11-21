using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Editable presentation settings for the low Holy oasis shrub. Map placement and concealment remain server-authored.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Holy shrub", fileName = "HolyShrubArt")]
    public sealed class HolyShrubArt : ScriptableObject
    {
        [Tooltip("Transparent Storm Ink shrub painting; keep the contact point at the painted foot.")]
        public Texture2D Pigment;

        [Range(.5f, 3f), Tooltip("Nominal visual width in world units. The renderer converts this to atlas pixels.")]
        public float WidthWorldUnits = 1.5f;

        [Range(.35f, 2f), Tooltip("Nominal visual height in world units, including the ground-contact strip.")]
        public float HeightWorldUnits = 1.0625f;

        [Range(0f, .5f), Tooltip("Ground-contact strip in world units below the painted foot anchor.")]
        public float GroundWorldUnits = .125f;

        [Tooltip("Normalized source-art foot point, with (0,0) at bottom left. Used to keep the shrub planted when artwork changes.")]
        public Vector2 TextureFoot = new Vector2(.5f, .055f);

        public static HolyShrubArt Load() => Resources.Load<HolyShrubArt>("StormInk/Props/HolyShrubArt");

        void OnValidate() => RegionPropArt.MarkAuthoredDefinitionsDirty();
    }
}
