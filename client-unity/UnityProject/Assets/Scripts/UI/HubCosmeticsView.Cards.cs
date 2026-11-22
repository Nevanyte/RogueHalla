using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Roguehalla.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Roguehalla.Client
{
    /// <summary>
    /// Builds one card per catalog item (shop, wardrobe, titles), the notice cards, their accent and details text, and the card focus capture and retirement helpers used when the grid is rebuilt.
    /// </summary>
    public sealed partial class HubCosmeticsView
    {
        void AddCosmeticCard(AccountCosmeticDefinition item, AccountFullProfile profile, bool allowMutation)
        {
            var card = NewCard("Cosmetic_" + SafeName(item.Id));
            var equipmentPreview = HubArtworkAssets.EquipmentPreview(item.Id);
            if (equipmentPreview != null) AddEquipmentPreview(card, equipmentPreview);
            else AddAccent(card, item);
            // Rows top to bottom (no overlap): preview/accent, name, category, details, then state and the action below.
            Vector2 nameMin = equipmentPreview != null ? new Vector2(.27f, .70f) : new Vector2(.045f, .66f);
            Vector2 nameMax = equipmentPreview != null ? new Vector2(.96f, .94f) : new Vector2(.96f, .84f);
            MythicUi.Text(card, "ItemName", item.Name, nameMin, nameMax, 21, ShowcasePalette.Text).fontStyle = FontStyles.Bold;
            string category = SlotLabel(item.Slot);
            Vector2 categoryMin = equipmentPreview != null ? new Vector2(.27f, .57f) : new Vector2(.045f, .55f);
            Vector2 categoryMax = equipmentPreview != null ? new Vector2(.96f, .70f) : new Vector2(.96f, .66f);
            MythicUi.Text(card, "ItemCategory", category, categoryMin, categoryMax, 13, ShowcasePalette.MutedText);
            var info = CatalogDetails(item);
            MythicUi.Text(card, "CatalogDetails", info, new Vector2(.045f, .29f), new Vector2(.96f, .55f), 15, ShowcasePalette.Text);

            bool owned = profile != null && _owned.Contains(item.Id);
            bool equipped = profile != null && _equipped.TryGetValue(item.Slot, out var current) && current == item.Id;
            string state = item.Price.HasValue ? "Price · " + FormatGv(item.Price.Value) + " GV" : "Not for sale.";
            var op = profile == null ? null : FindLatest(item, _tab == HubCosmeticsTab.Shop ? AccountMutationKind.Purchase : AccountMutationKind.Equip);
            bool submitting = op != null && op.Submitting;
            if (op != null) state = MutationText(op.Operation);
            if (submitting) state = "Waiting for confirmation…";
            if (_tab == HubCosmeticsTab.Wardrobe || _tab == HubCosmeticsTab.Titles)
            {
                if (equipped) state = "Equipped · " + SlotLabel(item.Slot) + (op != null && op.Operation.State != AccountMutationState.Confirmed ? "  ·  " + MutationText(op.Operation) : "");
                else if (owned && op == null) state = "Owned · ready to equip";
                else if (!owned && op == null) state = "Not owned · wardrobe is read only for this item.";
            }
            MythicUi.Text(card, "ItemState", state, new Vector2(.045f, .07f), new Vector2(.66f, .27f), 14,
                op != null && op.Operation.State == AccountMutationState.Refused ? ShowcasePalette.Warning : ShowcasePalette.MutedText);

            if (!allowMutation || profile == null) return;
            Button action = null;
            bool busy = HasCurrentOwnerMutationInFlight(profile.Name) || submitting;
            if (_tab == HubCosmeticsTab.Shop)
            {
                if (op != null && op.Operation.State == AccountMutationState.Unknown)
                    action = ActionButton(card, "RetrySame", "RETRY SAME PURCHASE", () => Retry(op), !busy);
                else if (owned)
                    action = ActionButton(card, "Owned", "OWNED", null, false);
                else if (item.Price.HasValue)
                    action = ActionButton(card, "Purchase", "REQUEST  " + FormatGv(item.Price.Value) + " GV", () => Purchase(item), !busy && op?.Operation.State != AccountMutationState.InFlight);
                else
                    action = ActionButton(card, "NotForSale", "NO LISTED PRICE", null, false);
            }
            else
            {
                if (op != null && op.Operation.State == AccountMutationState.Unknown)
                    action = ActionButton(card, "RetrySame", "RETRY SAME EQUIP", () => Retry(op), !busy);
                else if (equipped)
                    action = ActionButton(card, "Unequip", "UNEQUIP  ·  " + SlotLabel(item.Slot), () => Equip(item.Slot, null), !busy && op?.Operation.State != AccountMutationState.InFlight);
                else if (owned)
                    action = ActionButton(card, "Equip", "EQUIP  ·  " + SlotLabel(item.Slot), () => Equip(item.Slot, item.Id), !busy && op?.Operation.State != AccountMutationState.InFlight);
                else
                    action = ActionButton(card, "NotOwned", "NOT OWNED", null, false);
            }
            if (op != null && (op.Operation.State == AccountMutationState.InFlight || submitting) && action != null)
                action.GetComponentInChildren<TMP_Text>().text = "REQUEST IN FLIGHT";
        }

        void AddUnknownOwnedCard(string id, AccountFullProfile profile)
        {
            var card = NewCard("UnknownOwned_" + SafeName(id));
            AddAccent(card, null);
            MythicUi.Text(card, "UnknownTitle", "Unknown owned cosmetic", new Vector2(.045f, .75f), new Vector2(.96f, .96f), 20, ShowcasePalette.Gold);
            MythicUi.Text(card, "UnknownId", id, new Vector2(.045f, .56f), new Vector2(.96f, .78f), 15, ShowcasePalette.Text);
            bool equipped = profile.Cosmetics.Equipped.Values.Contains(id, StringComparer.Ordinal);
            MythicUi.Text(card, "UnknownStatus", equipped
                ? "Owned and equipped · no matching catalog definition is available here."
                : "Owned · no matching catalog definition is available here. This entry is read only.",
                new Vector2(.045f, .08f), new Vector2(.96f, .52f), 15, ShowcasePalette.MutedText);
        }

        void AddNoticeCard(string title, string message)
        {
            var card = NewCard("Notice_" + SafeName(title));
            MythicUi.Text(card, "NoticeTitle", title, new Vector2(.045f, .68f), new Vector2(.96f, .94f), 22, ShowcasePalette.Gold);
            MythicUi.Text(card, "NoticeMessage", message, new Vector2(.045f, .10f), new Vector2(.96f, .70f), 17, ShowcasePalette.Text);
        }

        RectTransform NewCard(string name)
        {
            var card = MythicUi.Panel(_content, name, Vector2.zero, Vector2.one, UiTheme.With(ShowcasePalette.PanelRaised, .88f), false);
            card.gameObject.AddComponent<LayoutElement>().minHeight = 180;
            var outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = ShowcasePalette.PanelStroke; outline.effectDistance = new Vector2(1, -1); outline.useGraphicAlpha = true;
            _cards.Add(card.gameObject);
            return card;
        }

        static void AddEquipmentPreview(RectTransform card, Sprite preview)
        {
            var rect = MythicUi.Rect(card, "EquipmentPreview", new Vector2(.045f, .68f), new Vector2(.235f, .96f));
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = preview;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        void AddAccent(RectTransform card, AccountCosmeticDefinition item)
        {
            if (item != null && item.Color.HasValue)
            {
                Color32 color = ToColor(item.Color.Value);
                var swatch = MythicUi.Rect(card, "CatalogColorSample", new Vector2(.045f, .87f), new Vector2(.085f, .96f));
                var image = swatch.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false;
                MythicUi.Text(card, "CatalogColorLabel", "COLOR PREVIEW", new Vector2(.10f, .86f), new Vector2(.50f, .96f), 11, ShowcasePalette.MutedText);
            }
            else
            {
                var bar = MythicUi.Rect(card, "GoldAccent", new Vector2(.045f, .925f), new Vector2(.25f, .945f));
                var image = bar.gameObject.AddComponent<Image>(); image.color = ShowcasePalette.GoldDim; image.raycastTarget = false;
            }
        }

        string CatalogDetails(AccountCosmeticDefinition item)
        {
            var parts = new List<string>();
            if (item.Text != null) parts.Add("Title text · " + item.Text);
            if (item.Target != null) parts.Add("For · " + SlotLabel(item.Target));
            if (item.Style != null) parts.Add("Style · " + SlotLabel(item.Style));
            if (item.Width.HasValue) parts.Add("Line width · " + item.Width.Value.ToString("0.##", CultureInfo.InvariantCulture));
            if (parts.Count == 0) parts.Add("Cosmetic appearance preview.");
            return string.Join("\n", parts);
        }

        Button ActionButton(Transform parent, string name, string label, Action onClick, bool interactable = true)
        {
            var button = MythicUi.Button(parent, name, label, new Vector2(.69f, .075f), new Vector2(.96f, .255f), () => onClick?.Invoke(),
                interactable ? MythicUi.ButtonStyle.Primary : MythicUi.ButtonStyle.Secondary);
            button.interactable = interactable;
            button.navigation = Navigation.defaultNavigation;
            return button;
        }

        internal static bool CanSubmitEquip(string slot, string itemId, ICollection<string> owned, IDictionary<string, string> equipped)
        {
            if (string.IsNullOrEmpty(slot) || owned == null || equipped == null) return false;
            if (itemId == null) return equipped.TryGetValue(slot, out var current) && !string.IsNullOrEmpty(current);
            return owned.Contains(itemId);
        }

        internal static FocusKey CaptureCardFocus(Transform content, GameObject selected)
        {
            if (content == null || selected == null) return default;
            Transform cursor = selected.transform;
            while (cursor != null && cursor.parent != content) cursor = cursor.parent;
            if (cursor == null || cursor.parent != content) return default;
            string path = "";
            Transform node = selected.transform;
            while (node != cursor)
            {
                path = string.IsNullOrEmpty(path) ? node.name : node.name + "/" + path;
                node = node.parent;
            }
            return new FocusKey(cursor.name, path);
        }

        internal static Transform ResolveCardFocus(IReadOnlyList<GameObject> liveCards, FocusKey key)
        {
            if (liveCards == null || !key.IsValid) return null;
            foreach (var live in liveCards)
            {
                if (live == null || live.name != key.CardName) continue;
                return string.IsNullOrEmpty(key.ChildPath) ? live.transform : live.transform.Find(key.ChildPath);
            }
            return null;
        }

        internal static void RetireCard(GameObject card)
        {
            // Destroy is deferred in players. Exclude old rows from rendering, input and layout immediately,
            // while leaving the object active so an in-progress Button.OnSubmit can finish safely.
            var layout = card.GetComponent<UnityEngine.UI.LayoutElement>();
            if (layout == null) layout = card.AddComponent<UnityEngine.UI.LayoutElement>();
            layout.ignoreLayout = true;
            var group = card.GetComponent<UnityEngine.CanvasGroup>();
            if (group == null) group = card.AddComponent<UnityEngine.CanvasGroup>();
            group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
        }
    }
}
