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

        public static RegionTransitionArt Load() => Resources.Load<RegionTransitionArt>("StormInk/Surfaces/RegionTransitions");

        internal Vector4 MaterialControls => new Vector4(Mathf.Clamp01(GrassWeathering), Mathf.Clamp01(RockBreakup),
            Mathf.Clamp01(PavingBreakup), Mathf.Clamp01(SnowEdge));
    }
}
