using System;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Registers the shared bow grip and the two ends of the bowstring in each painted view.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Shared bow registration")]
    public sealed class StormInkBowArtAuthoring : StormInkEquipmentRegistration
    {
        [Serializable]
        struct Nocks
        {
            public Vector2 Top;
            public Vector2 Bottom;

            public Nocks(float topX, float bottomX)
            {
                Top = new Vector2(topX, .910f);
                Bottom = new Vector2(bottomX, .094f);
            }
        }

        static readonly Nocks[] PaintedNocks =
        {
            new Nocks(.395f, .395f), new Nocks(.422f, .422f), new Nocks(.496f, .499f), new Nocks(.590f, .590f),
            new Nocks(.607f, .607f), new Nocks(.570f, .570f), new Nocks(.502f, .502f), new Nocks(.419f, .413f)
        };
        [SerializeField] Vector2[] grips =
        {
            new Vector2(.568f, .5f), new Vector2(.537f, .5f), new Vector2(.500f, .5f), new Vector2(.453f, .5f),
            new Vector2(.432f, .5f), new Vector2(.477f, .5f), new Vector2(.498f, .5f), new Vector2(.531f, .5f)
        };
        [SerializeField] Nocks[] nocks = (Nocks[])PaintedNocks.Clone();
        protected override Vector2[] Grips => grips;
        static StormInkBowArtAuthoring _default;

        public static StormInkBowArtAuthoring Load() => _default != null ? _default :
            _default = Resources.Load<StormInkBowArtAuthoring>("StormInk/Weapons/BowArtAuthoring");

        internal static void NockPositions(int row, out Vector2 top, out Vector2 bottom)
        {
            var art = Load();
            var registered = art != null && art.nocks != null && (uint)row < art.nocks.Length
                ? art.nocks[row] : PaintedNocks[row];
            top = registered.Top;
            bottom = registered.Bottom;
        }
    }
}
