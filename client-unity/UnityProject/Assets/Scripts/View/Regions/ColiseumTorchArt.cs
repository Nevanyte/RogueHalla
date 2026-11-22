using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Editable Coliseum torch body, flame socket and pooled light values.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Coliseum torch", fileName = "ColiseumTorchArt")]
    public sealed class ColiseumTorchArt : StormInkPropArtDefinition
    {
        [Header("Animated flame")]
        [Tooltip("Separate painted flame sprite. The body's pigment stays unlit; the old baked torch remains the fallback.")]
        public Sprite Flame;
        [Tooltip("Flame origin in normalized coordinates of the visible alpha-cropped body art, with (0,0) at bottom left. Uses the same space as TextureFoot.")]
        public Vector2 FlameSocketUv = new Vector2(.5f, .83f);
        [Min(.01f), Tooltip("Animated flame width in world units, before placement scale.")]
        public float FlameWidthWorldUnits = .22f;
        [Min(.01f), Tooltip("Animated flame height in world units, before placement scale.")]
        public float FlameHeightWorldUnits = .4f;
        [ColorUsage(false, false), Tooltip("Warm pigment tint for the separate flame sprite.")]
        public Color FlameTint = new Color(1f, .82f, .48f, 1f);

        [Header("Warm spill")]
        [ColorUsage(false, false), Tooltip("Light color. Defaults to the existing Coliseum temple-lamp warm tone.")]
        public Color LightColor = new Color(1f, .848f, .604f, 1f);
        [Min(.01f), Tooltip("Pooled point-light intensity; existing temple-lamp baseline is 1.5.")]
        public float LightIntensity = 1.5f;
        [Min(.01f), Tooltip("Pooled point-light radius in world units; existing temple-lamp baseline is 3.6.")]
        public float LightRadius = 3.6f;
        [Min(0f), Tooltip("Analytic floor and actor spill gain; existing temple-lamp baseline is .6.")]
        public float SpillIntensity = .6f;
        [Range(0f, 1f), Tooltip("Subtle light flicker amount; existing temple-lamp baseline is .3. Reduced Motion disables it.")]
        public float Flicker = .3f;

        bool _cropCached;
        Texture2D _cachedPigment;
#if UNITY_EDITOR
        Hash128 _cachedPigmentHash;
#endif
        Vector2Int _cachedCropSize;

        public static ColiseumTorchArt Load() => Resources.Load<ColiseumTorchArt>("StormInk/Props/ColiseumTorchArt");

        /// <summary>Authored body and flame must both exist; otherwise the original painted procedural torch is used whole.</summary>
        public bool IsReady => HasReadablePigment && Flame != null && TryGetFittedSocketOffset(out _);

        /// <summary>
        /// Offset from the planted foot in source-art world units, using the exact contain-fit and transparent-crop dimensions
        /// used by RegionPropArt. Cached per SO, so map instances do not rescan the source texture.
        /// </summary>
        public bool TryGetFittedSocketOffset(out Vector2 offset)
        {
            offset = default;
            if (!HasReadablePigment || !EnsureCropSize()) return false;
            Vector2 foot = ClampedTextureFoot(new Vector2(.5f, .055f));
            float usableHeight = Mathf.Max(.001f, HeightWorldUnits - GroundWorldUnits);
            float fit = Mathf.Min(Mathf.Max(.001f, WidthWorldUnits) / _cachedCropSize.x, usableHeight / _cachedCropSize.y);
            Vector2 fittedSize = new Vector2(_cachedCropSize.x * fit, _cachedCropSize.y * fit);
            offset = Vector2.Scale(FlameSocketUv - foot, fittedSize);
            return true;
        }

        /// <summary>Map-space tile offset for a placed torch, reflecting the source socket with the painting.</summary>
        public bool TryGetPlacedSocketOffset(float placementScale, bool mirrored, out Vector2 offset)
        {
            if (!TryGetFittedSocketOffset(out offset)) return false;
            if (mirrored) offset.x = -offset.x;
            offset *= Mathf.Max(0f, placementScale);
            return true;
        }

        bool EnsureCropSize()
        {
            bool cacheMatches = _cropCached && _cachedPigment == Pigment;
#if UNITY_EDITOR
            // A texture reimport can replace its pixels without replacing the asset reference.
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
                    minX = Mathf.Min(minX, x); minY = Mathf.Min(minY, y);
                    maxX = Mathf.Max(maxX, x); maxY = Mathf.Max(maxY, y);
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

        protected override void OnValidate()
        {
            _cropCached = false;
            base.OnValidate();
        }
    }
}
