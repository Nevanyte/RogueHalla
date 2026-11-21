using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Shared editable texture, world size and ground-contact settings for region props rendered through RegionPropArt.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Region prop art", fileName = "StormInkPropArt")]
    public class StormInkPropArtDefinition : ScriptableObject
    {
        [Tooltip("Readable transparent source painting. Unassigned or unreadable sources keep the procedural fallback.")]
        public Texture2D Pigment;

        [Range(.5f, 3f), Tooltip("Nominal visual width in world units; the atlas rasterizes at 64 pixels per unit.")]
        public float WidthWorldUnits = 1.5f;

        [Range(.35f, 4f), Tooltip("Nominal visual height in world units, including the ground-contact strip.")]
        public float HeightWorldUnits = 1.0625f;

        [Range(0f, .5f), Tooltip("World units reserved below the source art's foot anchor.")]
        public float GroundWorldUnits = .125f;

        [Tooltip("Normalized source-art foot point, with (0,0) at bottom left. It is aligned to the delivered map position.")]
        public Vector2 TextureFoot = new Vector2(.5f, .055f);

        internal bool HasReadablePigment => Pigment != null && Pigment.isReadable;
        internal int WidthPixels(int fallback) => !HasReadablePigment ? fallback : Mathf.RoundToInt(Mathf.Clamp(WidthWorldUnits, .5f, 3f) * RegionPropArt.PixelsPerUnit);
        internal int HeightPixels(int fallback) => !HasReadablePigment ? fallback : Mathf.RoundToInt(Mathf.Clamp(HeightWorldUnits, .35f, 4f) * RegionPropArt.PixelsPerUnit);
        internal int GroundPixels(int fallback, int heightPixels) => !HasReadablePigment ? fallback : Mathf.Min(Mathf.RoundToInt(Mathf.Clamp(GroundWorldUnits, 0f, .5f) * RegionPropArt.PixelsPerUnit), heightPixels - 1);
        internal Vector2 ClampedTextureFoot(Vector2 fallback) => !HasReadablePigment ? fallback : new Vector2(Mathf.Clamp01(TextureFoot.x), Mathf.Clamp01(TextureFoot.y));

        protected virtual void OnValidate() => RegionPropArt.MarkAuthoredDefinitionsDirty();
    }
}
