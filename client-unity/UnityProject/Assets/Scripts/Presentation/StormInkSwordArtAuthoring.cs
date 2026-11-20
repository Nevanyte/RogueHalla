using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Shared handle and floor registration for the single transferable sword.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Shared sword registration")]
    public sealed class StormInkSwordArtAuthoring : ScriptableObject
    {
        [SerializeField] Vector2[] grips =
        {
            new Vector2(.500f, .17f), new Vector2(.500f, .17f), new Vector2(.500f, .17f), new Vector2(.502f, .17f),
            new Vector2(.498f, .17f), new Vector2(.500f, .17f), new Vector2(.498f, .17f), new Vector2(.494f, .17f),
        };
        [SerializeField] Vector2 groundCentre = new Vector2(0f, .39f);
        static StormInkSwordArtAuthoring _default;

        public static StormInkSwordArtAuthoring Load() => _default != null ? _default :
            _default = Resources.Load<StormInkSwordArtAuthoring>("StormInk/Weapons/SwordArtAuthoring");

        public Vector2 GroundCentre => groundCentre;

        public Vector2 Grip(int row, Vector2 fallback) => (uint)row < grips.Length ? grips[row] : fallback;
    }
}
