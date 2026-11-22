using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Plague willow", fileName = "PlagueBogWillowArt")]
    public sealed class PlagueWillowArt : StormInkPropArtDefinition
    {
        [Range(1f, 2f), Tooltip("Canopy height multiplier where neighbouring cover cells belong to the same thicket.")]
        public float ThicketHeight = 1.55f;

        [Range(0f, .25f), Tooltip("Variation in the height and spread of bushes within one continuous patch.")]
        public float ThicketVariation = .14f;

        internal void FitThicket(MapBlob map, RegionProps.Placement placement, int seed,
            ref float width, ref float height, ref bool flip)
        {
            if (placement.Shipped || map.Bushes == null || map.Bushes.Length != map.W * map.H) return;
            int x = placement.Tx, y = placement.Ty, index = y * map.W + x;
            byte cover = map.Bushes[index];
            if (cover == 0) return;
            bool joins = (x > 0 && map.Bushes[index - 1] == cover) ||
                (x + 1 < map.W && map.Bushes[index + 1] == cover) ||
                (y > 0 && map.Bushes[index - map.W] == cover) ||
                (y + 1 < map.H && map.Bushes[index + map.W] == cover);
            if (!joins) return;

            float variance = Mathf.Clamp(ThicketVariation, 0f, .25f);
            width *= 1f + variance * RegionNoise.Hash01(x, y, seed + 367);
            height *= Mathf.Clamp(ThicketHeight, 1f, 2f) *
                Mathf.Lerp(1f - variance, 1f + variance, RegionNoise.Hash01(x, y, seed + 369));
            flip = RegionNoise.Hash01(x, y, seed + 373) < .5f;
        }
    }
}
