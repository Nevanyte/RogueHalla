using System;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Available rotations relative to the architectural slot before projection into the game camera.</summary>
    public enum ChurchArtTurns { AnyAngle, FixedToSlot, ReverseOnly, QuarterTurns }

    [Flags]
    public enum ChurchWindowBays
    {
        None = 0, FrontOuterLeft = 1, FrontInnerLeft = 2, FrontInnerRight = 4, FrontOuterRight = 8,
        RearOuterLeft = 16, RearInnerLeft = 32, RearInnerRight = 64, RearOuterRight = 128, All = 255
    }

    /// <summary>Artist-owned defaults, mounting constraints and supported local rotations of one painting.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Church Art Definition", fileName = "ChurchArt")]
    public sealed class ChurchArtDefinition : ScriptableObject
    {
        public ChurchArtValues Defaults;
        public ChurchArtMount Mount;
        public bool EmitsCandleLight;
        [Tooltip("Emit a steady colored spill from visible stained glass.")] public bool EmitsWindowLight;
        public ChurchArtTurns AllowedTurns;
        [Tooltip("Reflect the painting and its sockets when the architecture is mirrored.")]
        public bool ReflectPainting = true;
        public ChurchWindowBays PermittedBays = ChurchWindowBays.All;

        public float LocalTurn(float degrees)
        {
            switch (AllowedTurns)
            {
                case ChurchArtTurns.FixedToSlot: return 0f;
                case ChurchArtTurns.ReverseOnly: return Mathf.Round(degrees / 180f) * 180f;
                case ChurchArtTurns.QuarterTurns: return Mathf.Round(degrees / 90f) * 90f;
                default: return degrees;
            }
        }

        public bool AllowsWindow(string slot)
        {
            if (Mount != ChurchArtMount.Wall || string.IsNullOrEmpty(slot)) return true;
            int bank;
            if (slot.StartsWith("window.front.bay", StringComparison.Ordinal)) bank = 0;
            else if (slot.StartsWith("window.rear.bay", StringComparison.Ordinal)) bank = 4;
            else return true;
            int bay = slot[slot.Length - 1] - '0';
            return bay >= 0 && bay < 4 && ((int)PermittedBays & (1 << (bank + bay))) != 0;
        }
    }
}
