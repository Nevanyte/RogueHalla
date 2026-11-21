using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Editable presentation settings for the low Holy oasis shrub. Map placement and concealment remain server-authored.</summary>
    [CreateAssetMenu(menuName = "RogueHalla/Storm Ink/Holy shrub", fileName = "HolyShrubArt")]
    public sealed class HolyShrubArt : StormInkPropArtDefinition
    {
        public static HolyShrubArt Load() => Resources.Load<HolyShrubArt>("StormInk/Props/HolyShrubArt");
    }
}
