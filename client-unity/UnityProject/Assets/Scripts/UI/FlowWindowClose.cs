using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguehalla.Client
{
    /// <summary>The CLOSE button of the three window tabs (Quests, Traders, Notices): one place, one look, one size.</summary>
    public static class FlowWindowClose
    {
        public static readonly Vector2 AnchorMin = new Vector2(.87f, .915f), AnchorMax = new Vector2(.975f, .995f);
        public const MythicUi.ButtonStyle Style = MythicUi.ButtonStyle.Quiet;
        public const float FontSize = 16f;
        /// <summary>Minimum button width in canvas units; it grows to the left from AnchorMax.x (96.24 for the word at the smallest size at text size 1.5, plus the 12 of insets, rounded up).</summary>
        public const float MinWidth = 109f;

        public static UnityEngine.UI.Button Create(Transform card, string name, UnityEngine.Events.UnityAction onClick)
        {
            var button = MythicUi.Button(card, name, "CLOSE", AnchorMin, AnchorMax, onClick, Style, FontSize);
            var keeper = button.gameObject.AddComponent<FlowWindowCloseWidth>();
            keeper.Apply();
            return button;
        }
    }

    /// <summary>Keeps the CLOSE button at least <see cref="FlowWindowClose.MinWidth"/> wide by moving its left anchor whenever its parent changes size.</summary>
    [ExecuteAlways]
    public sealed class FlowWindowCloseWidth : UIBehaviour
    {
        public void Apply()
        {
            var rect = transform as RectTransform;
            var parent = transform.parent as RectTransform;
            if (rect == null || parent == null) return;
            float parentWidth = parent.rect.width;
            if (parentWidth <= 1f) return;
            float width = Mathf.Max((FlowWindowClose.AnchorMax.x - FlowWindowClose.AnchorMin.x) * parentWidth, FlowWindowClose.MinWidth);
            float left = Mathf.Max(0f, FlowWindowClose.AnchorMax.x - width / parentWidth);
            if (Mathf.Abs(rect.anchorMin.x - left) < 1e-6f) return;
            rect.anchorMin = new Vector2(left, rect.anchorMin.y);
        }

        protected override void OnRectTransformDimensionsChange() { Apply(); }
        protected override void OnEnable() { Apply(); }
    }
}
