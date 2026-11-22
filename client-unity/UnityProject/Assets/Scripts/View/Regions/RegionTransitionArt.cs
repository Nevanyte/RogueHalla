using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Controls the weathering of outdoor ground and decorative plants and stones between regions.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Region transitions", fileName = "RegionTransitions")]
    public sealed class RegionTransitionArt : ScriptableObject
    {
        [Range(0f, 1f), Tooltip("Straw colour and thinning of vegetation toward snow.")]
        public float GrassWeathering = 1f;
        [Range(0f, 1f), Tooltip("Change from volcanic scree to small pebbles as the ash runs out.")]
        public float RockBreakup = 1f;
        [Range(0f, 1f), Tooltip("Missing corners, fractured slabs and loose stone chips in the last flagstone courses.")]
        public float PavingBreakup = 1f;
        [Range(0f, 1f), Tooltip("Wind-cut snow tongues and exposed pockets of sand at the oasis edge.")]
        public float SnowEdge = 1f;
        [Range(0f, 1f), Tooltip("How much local ground pigment shows through the roots and bases of the selected props at material boundaries.")]
        public float FootWash = .78f;
        [Tooltip("Regional prop art keys whose feet can pick up the ground wash.")]
        public string[] GroundedProps;
        [Tooltip("Optional dry sedge painting for the cold side of a grassy verge.")]
        public Texture2D WinterSedge;
        [Tooltip("Painted sedge width and height in world units.")]
        public Vector2 SedgeSize = new Vector2(.94f, .47f);
        [Tooltip("Ground contact in the cropped sedge painting, measured from the lower left.")]
        public Vector2 SedgeFoot = new Vector2(.37f, .14f);
        public const string SedgeKey = "edge.grass";

#if UNITY_EDITOR
        void OnValidate() => RegionPropArt.MarkAuthoredDefinitionsDirty();
#endif
        public static RegionTransitionArt Load() => Resources.Load<RegionTransitionArt>("StormInk/Surfaces/RegionTransitions");

        public string GrassView(string key, RegionField field, float[] weights, RegionProps.Placement plant)
        {
            if (WinterSedge == null || !WinterSedge.isReadable || GrassWeathering <= 0f || field == null || !field.Ready ||
                (key != "holy.grass" && key != "plague.fern" && key != "sent.deadgrass")) return key;
            field.Sample(plant.X, plant.Y, weights);
            if (weights[5] > .1f || weights[3] < .035f || weights[1] + weights[2] + weights[4] < .025f) return key;
            float cold = Mathf.InverseLerp(.035f, .36f, weights[3]) * Mathf.Clamp01(GrassWeathering);
            return RegionNoise.Hash01(plant.Tx, plant.Ty, 139) < cold ? SedgeKey : key;
        }

        internal Vector4 MaterialControls => new Vector4(Mathf.Clamp01(GrassWeathering), Mathf.Clamp01(RockBreakup),
            Mathf.Clamp01(PavingBreakup), Mathf.Clamp01(SnowEdge));

        /// <summary>World-space foot, inverse contact height and strength, cached at placement.</summary>
        public Vector4 ContactAt(string artKey, RegionField field, float[] weights, Vector2 tile, float footY, float height)
        {
            if (FootWash <= 0f || field == null || !field.Ready || GroundedProps == null ||
                System.Array.IndexOf(GroundedProps, artKey) < 0) return Vector4.zero;
            field.Sample(tile.x, tile.y, weights);
            if (weights[5] > .10f) return Vector4.zero;
            float largest = Mathf.Max(Mathf.Max(weights[0], weights[1]), Mathf.Max(weights[2], Mathf.Max(weights[3], weights[4])));
            float fringe = Mathf.InverseLerp(.025f, .43f, 1f - largest);
            if (fringe <= 0f) return Vector4.zero;
            return new Vector4(footY, 1f / Mathf.Clamp(.23f * height, .045f, .27f), Mathf.Clamp01(FootWash) * fringe, 0f);
        }
    }
}
