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

        bool _cropCached;
        Texture2D _cachedPigment;
#if UNITY_EDITOR
        Hash128 _cachedPigmentHash;
#endif
        Vector2Int _cachedCropSize;

        internal bool HasReadablePigment => Pigment != null && Pigment.isReadable;
        internal int WidthPixels(int fallback) => !HasReadablePigment ? fallback : Mathf.RoundToInt(Mathf.Clamp(WidthWorldUnits, .5f, 3f) * RegionPropArt.PixelsPerUnit);
        internal int HeightPixels(int fallback) => !HasReadablePigment ? fallback : Mathf.RoundToInt(Mathf.Clamp(HeightWorldUnits, .35f, 4f) * RegionPropArt.PixelsPerUnit);
        internal int GroundPixels(int fallback, int heightPixels) => !HasReadablePigment ? fallback : Mathf.Min(Mathf.RoundToInt(Mathf.Clamp(GroundWorldUnits, 0f, .5f) * RegionPropArt.PixelsPerUnit), heightPixels - 1);
        internal Vector2 ClampedTextureFoot(Vector2 fallback) => !HasReadablePigment ? fallback : new Vector2(Mathf.Clamp01(TextureFoot.x), Mathf.Clamp01(TextureFoot.y));

        /// <summary>Maps a normalized source-art point to the same foot-relative contain-fit used by RegionPropArt.</summary>
        protected bool TryGetFittedSourceOffset(Vector2 sourceAnchor, out Vector2 offset)
        {
            offset = default;
            if (!HasReadablePigment || !EnsureCropSize()) return false;
            float usableHeight = Mathf.Max(.001f, HeightWorldUnits - GroundWorldUnits);
            float fit = Mathf.Min(Mathf.Max(.001f, WidthWorldUnits) / _cachedCropSize.x,
                usableHeight / _cachedCropSize.y);
            Vector2 fittedSize = new Vector2(_cachedCropSize.x * fit, _cachedCropSize.y * fit);
            offset = Vector2.Scale(sourceAnchor - ClampedTextureFoot(new Vector2(.5f, .055f)), fittedSize);
            return true;
        }

        bool EnsureCropSize()
        {
            bool cacheMatches = _cropCached && _cachedPigment == Pigment;
#if UNITY_EDITOR
            var importedHash = Pigment != null ? Pigment.imageContentsHash : default;
            cacheMatches &= _cachedPigmentHash == importedHash;
            _cachedPigmentHash = importedHash;
#endif
            if (cacheMatches)
                return _cachedCropSize.x > 0 && _cachedCropSize.y > 0;

            _cachedPigment = Pigment;
            _cachedCropSize = default;
            _cropCached = true;
            if (Pigment == null || !Pigment.isReadable) return false;
            try
            {
                var pixels = Pigment.GetPixels32();
                int minX = Pigment.width, minY = Pigment.height, maxX = -1, maxY = -1;
                for (int y = 0; y < Pigment.height; y++)
                for (int x = 0; x < Pigment.width; x++)
                {
                    if (pixels[y * Pigment.width + x].a / 255f <= RegionPropArt.EdgeAlpha) continue;
                    minX = Mathf.Min(minX, x);
                    minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x);
                    maxY = Mathf.Max(maxY, y);
                }
                if (maxX < minX || maxY < minY) return false;
                _cachedCropSize = new Vector2Int(maxX - minX + 1, maxY - minY + 1);
                return true;
            }
            catch (UnityException)
            {
                return false;
            }
        }

        protected virtual void OnValidate()
        {
            _cropCached = false;
            RegionPropArt.MarkAuthoredDefinitionsDirty();
        }
    }
}
