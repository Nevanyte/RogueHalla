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

        /// <summary>Local milliseconds of the HUD clock; a test replaces it (EditMode time does not advance).</summary>
        public Func<double> HudLocalMs = () => Time.unscaledTimeAsDouble * 1000d;

        /// <summary>The HUD's own clock: server time advanced between snapshots, so wedges and bars move every frame.</summary>
        readonly HudClock _hudClock = new HudClock();
        /// <summary>The attack timer's bar on each weapon slot; only the weapon in hand shows it.</summary>
        readonly UnityEngine.UI.Image[] _attackBars = new UnityEngine.UI.Image[2];
        double _attackReadyAtMs;
        float _previousFlash, _previousReadyPulse;

        /// <summary>Seconds of the HUD's local clock; every press pop and wash ages on it.</summary>
        float HudSeconds { get { return (float)(HudLocalMs() / 1000d); } }

        /// <summary>The server time the row draws at: the HUD clock once it has a snapshot, else the time passed.</summary>
        double ActionNowMs(double serverTimeMs) => _hudClock.Ready ? _hudClock.Now(HudLocalMs()) : serverTimeMs;

        /// <summary>Forgets the clock and the attack timer (a new session, an unbind, a presentation clock cut).</summary>
        void ResetActionClock()
        {
            _hudClock.Reset();
            _previousFlash = _previousReadyPulse = 0f;
            _attackReadyAtMs = 0d; _attackCooldownTotal = 1f;
            WriteAttackBars(0d);
        }

        /// <summary>Every frame: feeds the clock, runs the Wish wash and the press pops, then moves the wedges, the cast bar and the attack bars.</summary>
        void TickActionRow()
        {
            if (_session != null && _session.Latest != null) _hudClock.OnServerTime(_session.ServerTimeMs, HudLocalMs());
            TickWishTileMotion();
            TickPulses();
            if (!_hudClock.Ready) return;
            double now = _hudClock.Now(HudLocalMs());
            if (_wishAbilityTile != null)
            {
                _wishAbilityTile.SweepAt(now);
                var me = _session != null ? _session.Me : null;
                if (me != null && me.Wish.HasValue && me.Wish.Value.Cast.HasValue)
                {
                    var cast = me.Wish.Value.Cast.Value;
                    _wishAbilityTile.CastAt(now, cast.StartedAt, cast.LandsAt);
                }
            }
            for (int i = 0; i < _questTiles.Length; i++) if (_questTiles[i] != null) _questTiles[i].SweepAt(now);
            WriteAttackBars(now);
        }

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
                _weaponIcons[i] = MythicUi.Icon(rect, "WeaponIcon", MythicGlyph.Sword, 26f);
                _weaponIcons[i].rectTransform.anchorMin = _weaponIcons[i].rectTransform.anchorMax = new Vector2(.5f, 1f);
                _weaponIcons[i].rectTransform.pivot = new Vector2(.5f, 1f); _weaponIcons[i].rectTransform.anchoredPosition = new Vector2(0, -12);
                _slotNames[i] = HudUi.Label(HudUi.At(rect, "Name", 4, 42, HudRegions.WeaponSlotWidth - 8, 14), "Label", "Empty", theme.SizeSmall, AbilityTile.EmptyTextColour(theme), TextAlignmentOptions.Center);
                _slotScaling[i] = HudUi.Label(HudUi.At(rect, "Scaling", 2, 59, HudRegions.WeaponSlotWidth - 4, 11), "Label", "", theme.SizeMicro, theme.TextSecondary, TextAlignmentOptions.Center, wrap: true);
                var barBox = HudUi.Box(rect, "AttackBar", HudAnchor.BottomLeft, 6f, 4f, HudRegions.WeaponSlotWidth - 12f, 3f);
                var bar = barBox.gameObject.AddComponent<UnityEngine.UI.Image>();
                bar.sprite = UiSprites.Flat(); bar.color = theme.Accent; bar.raycastTarget = false;
                bar.type = UnityEngine.UI.Image.Type.Filled; bar.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
                bar.fillOrigin = 0; bar.fillAmount = 0f; bar.enabled = false;
                _attackBars[i] = bar;
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
            _wishAbilityTile = new AbilityTile(_weaponBar, "WishTile", HudRegions.AbilityElement(3), RowRaise, "R", _root, MythicGlyph.Wish);
            _wishTile = _wishAbilityTile.Root;
            _wishHint = _wishAbilityTile.Hint; _wishHint.Text = "R · Wish\nAwaiting server";
            _wishAbilityTile.Show(AbilityFace.Empty, AbilityFaces.NoWishLabel);
            // Cast / ready feedback: a faction-tinted wash over the tile, behind its text (driven by TickWishTileMotion).
            _wishGlow = HudUi.Fill(_wishTile, "WishGlow", new Color(1f, 1f, 1f, 0f));
            _wishGlow.rectTransform.offsetMin = new Vector2(1f, 1f); _wishGlow.rectTransform.offsetMax = new Vector2(-1f, -1f);
            _wishGlow.transform.SetSiblingIndex(Mathf.Min(1, _wishTile.childCount - 1)); _wishGlow.enabled = false;
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

        WireHudContent _wishNameBlock; AbilityId _wishNameAbility; string _wishNameText = "";

        /// <summary>The Wish's name, cached against the content block and the ability so an unchanged Wish allocates nothing.</summary>
        string WishNameFor(WishView? wish)
        {
            if (!wish.HasValue) return "";
            var block = HudContent; var ability = wish.Value.Ability;
            if (_wishNameText.Length == 0 || !ReferenceEquals(_wishNameBlock, block) || _wishNameAbility != ability)
            { _wishNameBlock = block; _wishNameAbility = ability; _wishNameText = HudContentText.WishName(block, ability); }
            return _wishNameText;
        }

        void RefreshWishTile(WishView? wish, double serverTimeMs, UiTheme theme)
        {
            if (_wishAbilityTile == null) return;
            _wishMotion.Observe(wish, serverTimeMs, HudSeconds);
            _wishAbilityTile.Show(AbilityFaces.Wish(wish, serverTimeMs), AbilityFaces.WishLabel(wish, WishNameFor(wish)));
            if (wish.HasValue) _wishAbilityTile.Cooldown(wish.Value.ReadyAt, wish.Value.RechargeMs); else _wishAbilityTile.Cooldown(0d, 0d);
            double now = ActionNowMs(serverTimeMs);
            _wishAbilityTile.SweepAt(now);
            if (wish.HasValue && wish.Value.Cast.HasValue) _wishAbilityTile.CastAt(now, wish.Value.Cast.Value.StartedAt, wish.Value.Cast.Value.LandsAt);
        }

        /// <summary>Draws the Wish tile with no Wish delivered (after a rebind, a menu or a spectator view).</summary>
        void ClearWishTile()
        {
            if (_wishAbilityTile == null) return;
            _wishAbilityTile.Show(AbilityFace.Empty, AbilityFaces.NoWishLabel);
            _wishAbilityTile.Cooldown(0d, 0d);
        }

        /// <summary>
        /// Every frame: the tile flashes when the server accepts a cast (a delivered cast record, a stock spend or a jump in the
        /// next-ready time) and pulses when the Wish is available again. Reduced motion keeps the colour wash and drops the scale.
        /// </summary>
        void TickWishTileMotion()
        {
            if (_wishGlow == null || _wishTile == null) return;
            float now = HudSeconds, flash = _wishMotion.Flash(now), pulse = _wishMotion.ReadyPulse(now);
            // The pop belongs to HudPulses (the one scale writer): ask for it on the edges of the two animations.
            if (flash > _previousFlash + .3f || (_previousReadyPulse <= 0f && pulse > 0f)) Pulse(_wishTile);
            _previousFlash = flash; _previousReadyPulse = pulse;
            float alpha = Mathf.Clamp01(.6f * flash + .4f * pulse);
            if (alpha <= .004f) { if (_wishGlow.enabled) _wishGlow.enabled = false; return; }
            Color color = UiTheme.Current.FactionColor(_faction); color.a = alpha;
            _wishGlow.enabled = true; _wishGlow.color = color;
        }

        void RefreshCooldownSweep(SelfView me, double serverTimeMs)
        {
            // The attack timer: the length is the largest time left seen since it was last 0; the bar drains against it.
            if (me.ReadyInMs > _attackCooldownTotal) _attackCooldownTotal = me.ReadyInMs;
            if (me.ReadyInMs == 0) _attackCooldownTotal = 1f;
            _attackReadyAtMs = serverTimeMs + me.ReadyInMs;
            WriteAttackBars(ActionNowMs(serverTimeMs));
        }

        /// <summary>Draws the attack bar of the weapon in hand at server time <paramref name="nowMs"/>; the other slot's bar is off.</summary>
        void WriteAttackBars(double nowMs)
        {
            int active = _inventoryShown.HasValue ? _inventoryShown.Value.ActiveSlot : 0;
            float fill = _attackReadyAtMs > 0d ? Mathf.Clamp01((float)((_attackReadyAtMs - nowMs) / Math.Max(1d, _attackCooldownTotal))) : 0f;
            for (int i = 0; i < _attackBars.Length; i++)
            {
                var bar = _attackBars[i];
                if (bar == null) continue;
                bool on = i == active && fill > 0f;
                if (bar.enabled != on) bar.enabled = on;
                if (on && bar.fillAmount != fill) bar.fillAmount = fill;
            }
        }
    }
}
