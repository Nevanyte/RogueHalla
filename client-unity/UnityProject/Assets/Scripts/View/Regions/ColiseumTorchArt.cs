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

        public static ColiseumTorchArt Load() => Resources.Load<ColiseumTorchArt>("StormInk/Props/ColiseumTorchArt");

        /// <summary>Authored body and flame must both exist; otherwise the original painted procedural torch is used whole.</summary>
        public bool IsReady => HasReadablePigment && Flame != null && TryGetFittedSocketOffset(out _);

        /// <summary>
        /// Offset from the planted foot in source-art world units, using the shared contain-fit and transparent-crop cache
        /// used by RegionPropArt and other socket-based props.
        /// </summary>
        public bool TryGetFittedSocketOffset(out Vector2 offset)
        {
            return TryGetFittedSourceOffset(FlameSocketUv, out offset);
        }

        /// <summary>Map-space tile offset for a placed torch, reflecting the source socket with the painting.</summary>
        public bool TryGetPlacedSocketOffset(float placementScale, bool mirrored, out Vector2 offset)
        {
            if (!TryGetFittedSocketOffset(out offset)) return false;
            if (mirrored) offset.x = -offset.x;
            offset *= Mathf.Max(0f, placementScale);
            return true;
        }

    }
}
