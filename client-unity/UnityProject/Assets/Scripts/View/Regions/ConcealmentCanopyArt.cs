using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Shared presentation proportions for the shrubs rooted on the delivered concealment grid.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Concealment canopy", fileName = "ConcealmentCanopyArt")]
    public sealed class ConcealmentCanopyArt : ScriptableObject
    {
        [Range(.75f, 1.5f), Tooltip("Spread relative to the regional painting. The centre stays on the bush cell.")]
        public float WidthScale = 1.14f;

        [Range(.75f, 2f), Tooltip("Height relative to the regional painting, above the existing root pivot.")]
        public float HeightScale = 1.55f;

        [Range(0f, .5f), Tooltip("Distance toward the camera in map cells used to sort the near branches against actors.")]
        public float ForegroundDepth = .35f;
    }
}
