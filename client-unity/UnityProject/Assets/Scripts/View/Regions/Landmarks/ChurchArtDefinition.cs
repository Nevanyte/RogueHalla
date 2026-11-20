using UnityEngine;

namespace Roguehalla.Client
{
    [CreateAssetMenu(menuName = "RogueHalla/Church Art Definition", fileName = "ChurchArt")]
    public sealed class ChurchArtDefinition : ScriptableObject
    {
        public ChurchArtValues Defaults;
        public ChurchArtMount Mount;
        public bool EmitsCandleLight;
    }
}
