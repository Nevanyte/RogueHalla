using System;
using System.Text;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Roguehalla.Client
{
    /// <summary>HUD part for the weapon bar, Wish tile, consumables, pickup hint and the cooldown sweeps.</summary>
    public sealed partial class MatchHud
    {
        void ApplyWishRow(SelfView me)
        {
            if (_championValue[2] == null || me == null) return;
            var block = HudContent;
            bool has = me.Wish.HasValue;
            var wish = me.Wish ?? default(WishView);
            if (_wishRowKeyValid && ReferenceEquals(_wishRowBlock, block) && _wishRowFaction == me.Faction && _wishRowHas == has
                && (block != null || !has || (_wishRowAbility == wish.Ability && _wishRowCharges == wish.Charges && _wishRowMax == wish.MaxCharges))) return;
            _wishRowKeyValid = true; _wishRowBlock = block; _wishRowFaction = me.Faction; _wishRowHas = has;
            _wishRowAbility = wish.Ability; _wishRowCharges = wish.Charges; _wishRowMax = wish.MaxCharges;
            if (block != null)
            {
                var row = HudContentText.Wish(block, me.Faction);
                Set(_championLabel[2], row.Name); Set(_championValue[2], row.Value); Set(_championScaling[2], row.Scaling);
                return;
            }
            Set(_championLabel[2], "Wish"); Set(_championScaling[2], "");
            Set(_championValue[2], has ? wish.Ability + " \u00b7 " + wish.Charges + "/" + wish.MaxCharges : "\u2014");
        }

        // ------------------------------------------------------------------ the action row (every rect from HudRegions)

        /// <summary>The row's offset inside the bar box: the consumables sit under it.</summary>
        const float RowRaise = HudRegions.AbilityBottom - HudRegions.ConsumablesBottom;

        /// <summary>Element i of the ability row seen from inside the bar box.</summary>
        static HudRegion BarElement(int index) => Raised(HudRegions.AbilityElement(index), RowRaise);

        /// <summary>The region moved up by an offset (down when negative) from its edge.</summary>
        static HudRegion Raised(HudRegion region, float by) => new HudRegion(region.Anchor, region.X, region.Y + by, region.Width, region.Height);

        /// <summary>The Shift and Q ability tiles of the row.</summary>
        AbilityTile[] _questTiles = new AbilityTile[2];

        void CreateWeaponBar()
        {
            var theme = UiTheme.Current;
            _weaponBar = _combatActions = HudUi.Box(_hud, "CombatActions", HudAnchor.BottomCenter, 0, HudRegions.ConsumablesBottom,
                HudRegions.AbilityWidth, RowRaise + HudRegions.AbilityHeight);

            // Slots.
            for (int i = 0; i < 2; i++)
            {
                byte slot = (byte)i;
                var rect = HudUi.Box(_weaponBar, "Slot" + (i + 1), BarElement(i * 2));
                var image = HudUi.Surface(rect, Color.white, theme.CornerRadius, theme.Border, true, true);
                var button = _slots[i] = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
                button.targetGraphic = image; button.onClick.AddListener(() => Swap(slot));
                HudUi.ApplyColors(button, theme.SurfaceRaised);
                _slotGroups[i] = rect.gameObject.AddComponent<CanvasGroup>();
                HudUi.Label(HudUi.At(rect, "Key", 5, 4, 20, 12), "Label", (i + 1).ToString(), theme.SizeTiny, theme.TextSecondary);
                _weaponIcons[i] = MythicUi.Icon(rect, "WeaponIcon", MythicGlyph.Sword, 26f);
                _weaponIcons[i].rectTransform.anchorMin = _weaponIcons[i].rectTransform.anchorMax = new Vector2(.5f, 1f);
                _weaponIcons[i].rectTransform.pivot = new Vector2(.5f, 1f); _weaponIcons[i].rectTransform.anchoredPosition = new Vector2(0, -12);
                _slotNames[i] = HudUi.Label(HudUi.At(rect, "Name", 4, 42, HudRegions.WeaponSlotWidth - 8, 14), "Label", "Empty", theme.SizeSmall, theme.TextPrimary, TextAlignmentOptions.Center);
                _slotScaling[i] = HudUi.Label(HudUi.At(rect, "Scaling", 2, 59, HudRegions.WeaponSlotWidth - 4, 11), "Label", "", theme.SizeMicro, theme.TextSecondary, TextAlignmentOptions.Center, wrap: true);
                var sweep = HudUi.Fill(rect, "CooldownSweep", Tone(theme.OutlineDark, .62f));
                sweep.rectTransform.offsetMin = new Vector2(1f, 1f); sweep.rectTransform.offsetMax = new Vector2(-1f, -1f);
                sweep.sprite = UiSprites.Flat(); sweep.type = UnityEngine.UI.Image.Type.Filled;
                sweep.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360; sweep.fillOrigin = 2; sweep.fillClockwise = false; sweep.fillAmount = 0f;
                _slotSweeps[i] = sweep; sweep.enabled = false;
                var active = HudUi.Fill(rect, "ActiveWeapon", theme.BorderStrong);
                active.sprite = UiSprites.Frame(theme, theme.CornerRadius); active.type = UnityEngine.UI.Image.Type.Sliced;
                _weaponActiveBars[i] = active; active.enabled = false;
                _weaponHints[i] = HudIconHint.Attach(button, _root, "Slot " + (i + 1) + " · Empty weapon slot");
            }

            // Space swap chip between the slots.
            var chip = HudUi.Box(_weaponBar, "QSwap", BarElement(1));
            var chipImage = HudUi.Surface(chip, Color.white, theme.CornerRadius, theme.Border, true, true);
            _swapChip = chip.gameObject.AddComponent<UnityEngine.UI.Button>(); _swapChip.targetGraphic = chipImage;
            HudUi.ApplyColors(_swapChip, theme.SurfaceRaised);
            _swapChip.onClick.AddListener(() => { if (_inventoryShown.HasValue) Swap((byte)(_inventoryShown.Value.ActiveSlot == 0 ? 1 : 0)); });
            HudUi.Label(HudUi.At(chip, "Key", 0, 6, HudRegions.SwapWidth, 14), "Label", "Space", theme.SizeSmall, theme.TextPrimary, TextAlignmentOptions.Center, true);
            HudUi.Label(HudUi.At(chip, "Caption", 0, 23, HudRegions.SwapWidth, 11), "Label", "SWAP", 7f, theme.TextSecondary, TextAlignmentOptions.Center);
            HudIconHint.Attach(_swapChip, _root, "Space · Swap weapons");

            CreateWishTile(theme);
            CreateQuestTiles();
            CreateConsumables(theme);
            CreatePickupHint(theme);
            var controls = HudUi.Label(HudUi.Box(_weaponBar, "ControlsLine", HudAnchor.BottomCenter, 0, -14f, 240, 13), "Label", "WASD move · Click attack · Shift/Q quest · R Wish", theme.SizeTiny, theme.TextSecondary, TextAlignmentOptions.Center);
            controls.raycastTarget = false;
        }

        void CreateQuestTiles()
        {
            string[] keys = { "Shift", "Q" };
            for (int i = 0; i < _questTiles.Length; i++)
            {
                var tile = _questTiles[i] = new AbilityTile(_weaponBar, "QuestSpell" + (i + 1), HudRegions.AbilityElement(4 + i), RowRaise, keys[i], _root);
                _questSlotTiles[i] = tile.Root;
                tile.Show(AbilityFace.Empty, "", QuestSpellHint(i, false));
            }
        }

        void CreateWishTile(UiTheme theme)
        {
            _wishTile = HudUi.Box(_weaponBar, "WishTile", BarElement(3));
            var image = HudUi.Surface(_wishTile, Color.white, theme.CornerRadius, theme.Border, true, true);
            var wishInfo = _wishTile.gameObject.AddComponent<UnityEngine.UI.Button>(); wishInfo.targetGraphic = image;
            HudUi.ApplyColors(wishInfo, theme.SurfaceRaised);
            HudUi.Label(HudUi.At(_wishTile, "Key", 5, 4, 14, 12), "Label", "R", theme.SizeTiny, theme.TextSecondary);
            var icon = _wishIcon = MythicUi.Icon(_wishTile, "WishIcon", MythicGlyph.Wish, 24f);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(.5f, 1f);
            icon.rectTransform.pivot = new Vector2(.5f, 1f); icon.rectTransform.anchoredPosition = new Vector2(0, -6);
            _wishSeconds = HudUi.Label(HudUi.At(_wishTile, "Seconds", 0, 6, HudRegions.TileSize, 24), "Label", "", 15f, theme.TextPrimary, TextAlignmentOptions.Center, true);
            _wishSummary = HudUi.Label(HudUi.At(_wishTile, "Name", 2, 36, HudRegions.TileSize - 4, 12), "Label", "—", theme.SizeTiny, theme.TextPrimary, TextAlignmentOptions.Center);
            _wishCharges = HudUi.Label(HudUi.At(_wishTile, "Charges", HudRegions.TileSize - 22, HudRegions.TileSize - 20, 20, 14), "Label", "", theme.SizeSmall, theme.Accent, TextAlignmentOptions.Center, true);
            _wishSweep = HudUi.Fill(_wishTile, "CooldownSweep", Tone(theme.OutlineDark, .62f));
            _wishSweep.rectTransform.offsetMin = new Vector2(1f, 1f); _wishSweep.rectTransform.offsetMax = new Vector2(-1f, -1f);
            _wishSweep.sprite = UiSprites.Flat(); _wishSweep.type = UnityEngine.UI.Image.Type.Filled;
            _wishSweep.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360; _wishSweep.fillOrigin = 2; _wishSweep.fillClockwise = false; _wishSweep.fillAmount = 0f; _wishSweep.enabled = false;
            var cast = HudUi.Box(_wishTile, "CastBar", HudAnchor.BottomLeft, 0, 0, HudRegions.TileSize, 3);
            _wishCastBar = cast.gameObject.AddComponent<UnityEngine.UI.Image>(); _wishCastBar.color = theme.Accent; _wishCastBar.raycastTarget = false;
            _wishCastBar.type = UnityEngine.UI.Image.Type.Filled; _wishCastBar.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal; _wishCastBar.fillAmount = 0f; _wishCastBar.enabled = false;
            // Cast / ready feedback: a faction-tinted wash over the tile, behind its text (driven by TickWishTileMotion).
            _wishGlow = HudUi.Fill(_wishTile, "WishGlow", new Color(1f, 1f, 1f, 0f));
            _wishGlow.rectTransform.offsetMin = new Vector2(1f, 1f); _wishGlow.rectTransform.offsetMax = new Vector2(-1f, -1f);
            _wishGlow.transform.SetSiblingIndex(Mathf.Min(1, _wishTile.childCount - 1)); _wishGlow.enabled = false;
            _wishSummary.text = "—";
            _wishHint = HudIconHint.Attach(wishInfo, _root, "R · Wish\nAwaiting server");
            // Retain the existing formatted detail model for tests/readers, without a permanent world overlay.
            _wishDetails = Panel(_hud, "WishPanel", .775f, .70f, .985f, .915f);
            _wishCard = Text(_wishDetails, "WishCard", "R · WISH\nAwaiting server", .04f, .04f, .96f, .96f, 14);
            _wishCard.color = ShowcasePalette.Gold;
            _wishDetails.gameObject.SetActive(false);
        }

        void CreateConsumables(UiTheme theme)
        {
            var row = HudUi.Box(_weaponBar, "Consumables", new HudRegion(HudRegionAnchor.BottomCenter, 0f, 0f, HudRegions.ConsumablesWidth, HudRegions.ConsumablesHeight));
            // Heal chip: [E] Heal  N large - M small  Healing xN.
            _potionButton = HudUi.Button(row, "Potion", null, HudAnchor.BottomLeft, 0, 0, 200f, HudRegions.ConsumablesHeight, UsePotion, radius: 5f);
            var heal = _potionButton.transform;
            _potionIcon = MythicUi.Icon(heal, "PotionIcon", MythicGlyph.Potion, 14f);
            _potionIcon.rectTransform.anchorMin = _potionIcon.rectTransform.anchorMax = new Vector2(0, .5f);
            _potionIcon.rectTransform.pivot = new Vector2(0, .5f); _potionIcon.rectTransform.anchoredPosition = new Vector2(8, 0);
            HudUi.Label(HudUi.At(heal, "Key", 26, 8, 12, 12), "Label", "E", theme.SizeTiny, theme.TextSecondary);
            HudUi.Label(HudUi.At(heal, "Name", 42, 7, 28, 14), "Label", "Heal", theme.SizeSmall, theme.Success, bold: true);
            _healText = HudUi.Label(HudUi.At(heal, "Count", 72, 7, 76, 14), "Label", "— large · — small", theme.SizeSmall, theme.TextPrimary);
            _healStatus = HudUi.Label(HudUi.At(heal, "Status", 150, 7, 48, 14), "Label", "", theme.SizeSmall, theme.Accent);
            foreach (var fit in new[] { _healText, _healStatus }) { fit.enableAutoSizing = true; fit.fontSizeMin = 8f; fit.fontSizeMax = theme.SizeSmall; }
            _potionHint = HudIconHint.Attach(_potionButton, _root, "E · Drink potion\nAwaiting inventory");
            // Wind chip: "+50% speed · Active Ns" while the owner's boost runs, else "Walk over emblems" (ApplyInventoryHints).
            var wind = HudUi.Box(row, "Wind", HudAnchor.BottomRight, 0, 0, 146f, HudRegions.ConsumablesHeight);
            HudUi.Surface(wind, Tone(theme.SurfaceRaised, .94f), 5f, theme.Border, false, true);
            HudUi.Label(HudUi.At(wind, "Name", 8, 7, 30, 14), "Label", "Wind", theme.SizeSmall, theme.Info, bold: true);
            _windText = HudUi.Label(HudUi.At(wind, "Text", 42, 7, 100, 14), "Label", "Walk over emblems", theme.SizeSmall, theme.TextPrimary);
            _windText.GetComponent<MythicTextScale>().MinSize = 7f;   // the text-size fit shrinks "+50% speed · Active 120s" to about 7.5
        }

        /// <summary>The pickup pill inside the bar: the prompt lane's rect (or its left half when the launcher shares it), 116 above the bar bottom.</summary>
        static HudRegion PickupRegion(bool pair) =>
            Raised(pair ? HudRegions.PromptPair(0) : HudRegions.Prompt(), -HudRegions.ConsumablesBottom);

        void CreatePickupHint(UiTheme theme)
        {
            var lane = PickupRegion(false);
            _pickup = HudUi.Button(_weaponBar, "Pickup", "F", HudAnchor.BottomCenter, lane.X, lane.Y, lane.Width, lane.Height, () => _client.PickupFocus(), theme.SizeTiny, 6f);
            var rect = (RectTransform)_pickup.transform;
            HudUi.SetFrame(rect, theme.BorderStrong);
            // One label carries the F key and the action ("F  take Sword"); it reads just "F" while nothing is in reach.
            _pickupText = _pickup.GetComponentInChildren<TMP_Text>(true);
            _pickupText.alignment = TextAlignmentOptions.MidlineLeft; _pickupText.color = theme.Accent; _pickupText.fontStyle = FontStyles.Bold;
            _pickupText.fontSize = theme.SizeSmall;
            _pickupText.rectTransform.offsetMin = new Vector2(10, 0); _pickupText.rectTransform.offsetMax = new Vector2(-26, 0);
            var icon = HudActionGlyphs.Icon(rect, "PickupIcon", HudActionGlyph.Pickup, 12f);
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(1, .5f); icon.rectTransform.anchoredPosition = new Vector2(-12, 0);
            icon.color = theme.Accent;
            _pickupHint = HudIconHint.Attach(_pickup, _root, "F · Pick up\nNo pickup in range");
            _pickup.gameObject.SetActive(false);
        }

        void RefreshWishTile(WishView? wish, double serverTimeMs, UiTheme theme)
        {
            if (_wishSeconds == null) return;
            if (!wish.HasValue)
            {
                _wishMotion.Observe(null, serverTimeMs, Time.unscaledTime);
                Set(_wishSeconds, ""); Set(_wishCharges, ""); if (_wishSweep != null) _wishSweep.enabled = false;
                if (_wishCastBar != null) _wishCastBar.enabled = false;
                HudUi.SetFrame(_wishTile, theme.Border);
                return;
            }
            var w = wish.Value;
            _wishMotion.Observe(wish, serverTimeMs, Time.unscaledTime);
            double remainingMs = Math.Max(0d, w.ReadyAt - serverTimeMs);
            bool ready = remainingMs <= 0d && w.Charges > 0;
            Set(_wishSeconds, remainingMs > 0d ? Math.Ceiling(remainingMs / 1000d).ToString("0") : "");
            // The seconds sit over the icon (legacy); dim the icon while they show so the number stays legible.
            if (_wishIcon != null) { var tint = MythicUi.GlyphColor(MythicGlyph.Wish); tint.a = remainingMs > 0d ? .3f : 1f; _wishIcon.color = tint; }
            Set(_wishCharges, w.MaxCharges > 1 ? w.Charges.ToString() : "");
            if (_wishSweep != null)
            {
                _wishSweep.enabled = remainingMs > 0d && w.RechargeMs > 0d;
                _wishSweep.fillAmount = w.RechargeMs > 0d ? (float)Math.Min(1d, remainingMs / w.RechargeMs) : 0f;
            }
            if (_wishCastBar != null)
            {
                bool casting = w.Cast.HasValue;
                _wishCastBar.enabled = casting;
                if (casting)
                {
                    var c = w.Cast.Value;
                    double span = Math.Max(1d, c.LandsAt - c.StartedAt);
                    _wishCastBar.fillAmount = (float)Math.Max(0d, Math.Min(1d, (serverTimeMs - c.StartedAt) / span));
                }
            }
            HudUi.SetFrame(_wishTile, w.Cast.HasValue ? theme.BorderStrong : ready ? theme.FactionColor(_faction) : theme.Border);
        }

        /// <summary>
        /// Every frame: the tile flashes when the server accepts a cast (a delivered cast record, a stock spend or a jump in the
        /// next-ready time) and pulses when the Wish is available again. Reduced motion keeps the colour wash and drops the scale.
        /// </summary>
        void TickWishTileMotion()
        {
            if (_wishGlow == null || _wishTile == null) return;
            float now = Time.unscaledTime, flash = _wishMotion.Flash(now), pulse = _wishMotion.ReadyPulse(now);
            bool reduced = ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion;
            float alpha = Mathf.Clamp01(.6f * flash + .4f * pulse);
            if (alpha <= .004f) { if (_wishGlow.enabled) { _wishGlow.enabled = false; _wishTile.localScale = Vector3.one; } return; }
            Color color = UiTheme.Current.FactionColor(_faction); color.a = alpha;
            _wishGlow.enabled = true; _wishGlow.color = color;
            _wishTile.localScale = reduced ? Vector3.one : Vector3.one * (1f + .12f * flash + .05f * pulse);
        }

        void RefreshCooldownSweep(SelfView me)
        {
            // One shared attack-cooldown wedge over both slots; the share left shrinks against the longest cooldown seen.
            if (me.ReadyInMs > _attackCooldownTotal) _attackCooldownTotal = me.ReadyInMs;
            if (me.ReadyInMs == 0) _attackCooldownTotal = 1f;
            float share = Mathf.Clamp01(me.ReadyInMs / Mathf.Max(1f, _attackCooldownTotal));
            for (int i = 0; i < 2; i++)
                if (_slotSweeps[i] != null) { _slotSweeps[i].enabled = share > 0f; _slotSweeps[i].fillAmount = share; }
        }
    }
}
