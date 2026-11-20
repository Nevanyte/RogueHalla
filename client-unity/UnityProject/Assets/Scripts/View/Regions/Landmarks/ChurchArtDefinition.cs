using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Artist-owned defaults for a church furnishing, including its mounting mode and candle response.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Church Art Definition", fileName = "ChurchArt")]
    public sealed class ChurchArtDefinition : ScriptableObject
    {
        public ChurchArtValues Defaults;
        public ChurchArtMount Mount;
        public bool EmitsCandleLight;
    }
}
