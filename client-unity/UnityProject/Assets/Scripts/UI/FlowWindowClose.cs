using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>The CLOSE button of the three window tabs (Quests, Traders, Notices): one place, one look, one size.</summary>
    public static class FlowWindowClose
    {
        public static readonly Vector2 AnchorMin = new Vector2(.87f, .915f), AnchorMax = new Vector2(.975f, .995f);
        public const MythicUi.ButtonStyle Style = MythicUi.ButtonStyle.Quiet;
        public const float FontSize = 16f;
        /// <summary>Minimum button width in canvas units; it grows to the left from AnchorMax.x. The stub's value.</summary>
        public const float MinWidth = 0f;

        /// <summary>Stub: the implementer builds the button here.</summary>
        public static UnityEngine.UI.Button Create(Transform card, string name, UnityEngine.Events.UnityAction onClick) => null;
    }
}
