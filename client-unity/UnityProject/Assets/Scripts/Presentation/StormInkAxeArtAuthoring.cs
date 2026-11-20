using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Registration for the single physical axe across holders and ground pickups.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Shared axe registration")]
    public sealed class StormInkAxeArtAuthoring : StormInkEquipmentRegistration
    {
        [SerializeField] Vector2[] grips =
        {
            new Vector2(.385f, .27f), new Vector2(.432f, .27f), new Vector2(.498f, .27f), new Vector2(.564f, .27f),
            new Vector2(.611f, .27f), new Vector2(.568f, .27f), new Vector2(.502f, .27f), new Vector2(.406f, .27f),
        };
        protected override Vector2[] Grips => grips;
        static StormInkAxeArtAuthoring _default;

        public static StormInkAxeArtAuthoring Load() => _default != null ? _default :
            _default = Resources.Load<StormInkAxeArtAuthoring>("StormInk/Weapons/AxeArtAuthoring");

    }
}
