using System;
using System.Text;
using Roguehalla.Net;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// The legacy-anchored in-match HUD (docs/rework/LEGACY_LAYOUT.md sections 2-8 and 12). Everything here is placed in HUD units on
    /// the stages (1 unit = 1 legacy CSS px) and reads its look from <see cref="UiTheme"/>. It shows only facts the server delivered.
    /// </summary>
    public sealed partial class MatchHud
    {
        // Legacy numbers (LEGACY_LAYOUT.md). Kept as constants so tests and layout share one source.
        internal const float ChromeButton = 30f, ChromeGap = 6f, ChromeTop = 10f, ChromeRight = 12f, ChromePerfGap = 8f;
        internal const float PlateLeft = 12f, PlateTop = 10f, PlateInner = 278f;
        internal const float ChampionLeft = 12f, ChampionBottom = 48f, ChampionHeight = 126f;
        internal const float QuestLauncherLeft = 12f, QuestLauncherBottom = 12f;

        HudViewport _viewport;
        // Plate.
        RectTransform _plateBody, _healthRow, _buildBox, _extrasBody;
        TMP_Text _plateStatus, _hpValue, _extrasLabel, _cardsText, _mobText;
        UnityEngine.UI.Image _hpTrough;
        UnityEngine.UI.Button _extrasToggle;
        readonly RectTransform[] _buildCells = new RectTransform[9];
        bool _extrasOpen, _plateActive;
        // Weapon bar.
        readonly TMP_Text[] _slotNames = new TMP_Text[2], _slotScaling = new TMP_Text[2];
        readonly UnityEngine.UI.Image[] _slotSweeps = new UnityEngine.UI.Image[2];
        readonly CanvasGroup[] _slotGroups = new CanvasGroup[2];
        UnityEngine.UI.Button _swapChip, _potionButton, _windChip;
        RectTransform _weaponBar, _wishTile;
        TMP_Text _healText, _healStatus, _windText, _pickupText;
        UnityEngine.UI.Image _wishGlow;
        AbilityTile _wishAbilityTile;
        readonly WishTileMotion _wishMotion = new WishTileMotion();
        float _attackCooldownTotal = 1f;
        // Champion combat HUD.
        RectTransform _championHud;
        TMP_Text _championName;
        UnityEngine.UI.Image _championIcon;
        readonly TMP_Text[] _championValue = new TMP_Text[3], _championScaling = new TMP_Text[3], _championLabel = new TMP_Text[3];
        readonly RectTransform[] _championRow = new RectTransform[3];
        bool _championWanted, _barWanted;
        // Death line, glows, chrome, top-centre and toast pills.
        TMP_Text _deathLine;
        EdgeThreatView _edgeThreats;
        RectTransform _metricsPill, _bannerPill, _phasePill;

        static Color Tone(Color32 color, float alpha) { return UiTheme.With(color, alpha); }
        static string Hex(Color32 c) { return "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2"); }

        // ------------------------------------------------------------------ chrome (section 2)

        void CreateLegacyChrome()
        {
            var theme = UiTheme.Current;
            var glyphs = new[] { HudActionGlyph.Help, HudActionGlyph.Settings, HudActionGlyph.Fullscreen };
            var names = new[] { "Help", "Settings", "Fullscreen" };
            var hints = new[] { "H · Controls and field guide", "Settings · audio, text size and display", "Alt+Enter · Fullscreen" };
            UnityEngine.Events.UnityAction[] actions = { ToggleHelp, ToggleSettings, ToggleFullscreen };
            for (int i = 0; i < 3; i++)
            {
                float right = ChromeRight + i * (ChromeButton + ChromeGap);
                var button = HudUi.Button(_chromeStage, names[i], null, HudAnchor.TopRight, right, ChromeTop, ChromeButton, ChromeButton, actions[i]);
                _headerActions[i] = button;
                var icon = HudActionGlyphs.Icon(button.transform, "ActionIcon", glyphs[i], 16f);
                icon.color = theme.TextPrimary;
                HudIconHint.Attach(button, _root, hints[i]);
            }
            float perfRight = ChromeRight + 3 * ChromeButton + 2 * ChromeGap + ChromePerfGap;
            _metricsPill = HudUi.Box(_chromeStage, "PerformancePill", HudAnchor.TopRight, perfRight, ChromeTop + 3f, 124f, 24f);
            HudUi.Surface(_metricsPill, Tone(theme.SurfaceRaised, .85f), theme.CornerRadius, theme.Border, false);
            _metrics = HudUi.Label(_metricsPill, "PerformanceReadout", "", theme.SizeSmall, theme.TextSecondary, TextAlignmentOptions.MidlineRight);
            _metrics.rectTransform.offsetMin = new Vector2(8, 0); _metrics.rectTransform.offsetMax = new Vector2(-8, 0);
            _metricsPill.gameObject.SetActive(false);
        }

        void ToggleFullscreen() { Screen.fullScreen = !Screen.fullScreen; }

        // ------------------------------------------------------------------ top centre pills and toasts (section 8)



        // ------------------------------------------------------------------ HUD plate (sections 6 and 7)

        static readonly string[] CellNames = { "Offense", "SO", "Damage gained", "Attack speed", "Life steal", "Health gained", "Defense", "Soul Defense", "Move speed" };
        static readonly int[] CellBoost = { 0, 1, -1, 5, 7, 4, 2, 3, 6 };
        static readonly MythicGlyph[] CellGlyph = { MythicGlyph.Sword, MythicGlyph.Wish, MythicGlyph.Axe, MythicGlyph.Bow, MythicGlyph.Potion, MythicGlyph.Health, MythicGlyph.Armor, MythicGlyph.Shield, MythicGlyph.Speed };
        const float BuildCellWidth = 138f, BuildCellHeight = 14f, BuildRowStep = 17f, BuildColumnStep = 140f, BuildTop = 15f;

        void CreatePlate()
        {
            var theme = UiTheme.Current;
            _vitalsPanel = HudUi.Box(_hud, "VitalsPanel", HudAnchor.TopLeft, PlateLeft, PlateTop, HudLayout.PlateWidth(1920f), 60f);
            HudUi.Surface(_vitalsPanel, Tone(theme.Surface, .94f), theme.CornerRadius, theme.Border, false, true);
            _plateBody = HudUi.Inset(_vitalsPanel, "PlateBody", 8, 5, 8, 6);

            // Not active: status line (600) and vitals line, plus the kills / GV line Unity already received.
            _plateStatus = HudUi.Label(HudUi.At(_plateBody, "StatusRow", 0, 0, PlateInner, 15), "HudStatus", "", theme.SizeSmall, theme.TextPrimary, bold: true);
            _vitals = HudUi.Label(HudUi.At(_plateBody, "VitalsRow", 0, 16, PlateInner, 15), "Vitals", "Waiting for player state", theme.SizeSmall, theme.TextSecondary);
            _combatStats = HudUi.Label(HudUi.At(_plateBody, "StatsRow", 0, 32, PlateInner, 15), "CombatStats", "Awaiting player stats", theme.SizeTiny, theme.TextSecondary);

            // Active: HP label, 8 px meter, value and bounty tag.
            _healthRow = HudUi.At(_plateBody, "HealthRow", 0, 0, PlateInner, 15);
            HudUi.Label(HudUi.At(_healthRow, "HpLabel", 0, 0, 16, 15), "Label", "HP", theme.SizeTiny, theme.TextSecondary, bold: true);
            var bar = HudUi.At(_healthRow, "OwnerVitalBar", 20, 3.5f, 76, 8);
            _hpTrough = HudUi.Surface(bar, Tone(theme.SurfaceSunken, 1f), 5f, theme.Border, false);
            var inner = HudUi.Inset(bar, "Meter", 1, 1, 1, 1);
            _healthGhostFill = Rect(inner, "HealthGhost", 0, 0, 0, 1);
            _healthGhostFill.gameObject.AddComponent<UnityEngine.UI.Image>().color = Tone(theme.Danger, .55f);
            _healthGhostFill.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            _healthBarFill = Rect(inner, "HealthFill", 0, 0, 0, 1);
            _healthBarFill.gameObject.AddComponent<UnityEngine.UI.Image>().color = theme.Success;
            _healthBarFill.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            _barrierBarFill = Rect(inner, "BarrierFill", 0, 0, 0, 1);
            _barrierBarFill.gameObject.AddComponent<UnityEngine.UI.Image>().color = theme.Info;
            _barrierBarFill.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            _hpValue = HudUi.Label(HudUi.At(_healthRow, "HpValue", 102, 0, 122, 15), "Value", "", theme.SizeSmall, theme.TextPrimary, bold: true);
            _hpValue.richText = true;

            CreateBuildHud(theme);
            CreateExtras(theme);
            CreateDamageReadout();
            SetPlateMode(false);
        }

        void CreateBuildHud(UiTheme theme)
        {
            // Legacy "Gained" strip: label + nine fixed cells, left rule in the stat colour, 13 px icon, name left, value right.
            _buildStrip = HudUi.At(_plateBody, "BuildStrip", 0, 18, PlateInner, BuildTop + 5 * BuildRowStep);
            _buildBox = _buildStrip;
            HudUi.Label(HudUi.At(_buildStrip, "GainedLabel", 0, 0, PlateInner, 12), "Label", "GAINED", theme.SizeTiny, theme.TextSecondary).characterSpacing = 3f;
            for (int i = 0; i < 9; i++)
            {
                int boost = CellBoost[i];
                float x = (i % 2) * BuildColumnStep, y = BuildTop + (i / 2) * BuildRowStep;
                var cell = HudUi.At(_buildStrip, "Build" + CellNames[i].Replace(" ", ""), x, y, BuildCellWidth, BuildCellHeight);
                _buildCells[i] = cell;
                HudUi.Surface(cell, Tone(theme.SurfaceSunken, .8f), theme.CornerRadiusSmall, null, true);
                Color32 stat = boost >= 0 ? theme.StatColor(boost) : theme.Danger;
                var rule = HudUi.Box(cell, "Rule", HudAnchor.TopLeft, 0, 0, 3, BuildCellHeight);
                rule.gameObject.AddComponent<UnityEngine.UI.Image>().color = stat;
                rule.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
                var icon = MythicUi.Icon(cell, "StatIcon", CellGlyph[i], 13f);
                icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f);
                icon.rectTransform.pivot = new Vector2(0, .5f); icon.rectTransform.anchoredPosition = new Vector2(4, 0);
                icon.color = stat;
                var name = HudUi.Label(HudUi.At(cell, "Name", 17, 0, 73, BuildCellHeight), "Label", CellNames[i], theme.SizeTiny, theme.TextSecondary);
                // The names keep a floor of 7 under the text-size fit; the values 6.
                name.GetComponent<MythicTextScale>().MinSize = 7f;
                var value = HudUi.Label(HudUi.At(cell, "Value", 90, 0, 48, BuildCellHeight), "Count", "—", theme.SizeTiny, theme.TextPrimary, TextAlignmentOptions.MidlineRight, true);
                value.GetComponent<MythicTextScale>().MinSize = 6f;
                _gainValues[i] = value;
                var select = cell.gameObject.AddComponent<UnityEngine.UI.Button>();
                select.targetGraphic = cell.GetComponent<UnityEngine.UI.Image>();
                HudUi.ApplyColors(select, Color.white);
                HudIconHint.Attach(select, _root, CellNames[i] + " · gained from boosts, cards and farmed mobs\nOpen B for the full build ledger.");
            }
        }

        void CreateExtras(UiTheme theme)
        {
            float y = 18 + BuildTop + 5 * BuildRowStep + 3;
            _extrasToggle = HudUi.Button(_plateBody, "BuildDetails", null, HudAnchor.TopLeft, 0, y, PlateInner, 14, ToggleExtras, radius: 3f);
            _extrasToggle.GetComponent<UnityEngine.UI.Image>().enabled = false;
            _extrasToggle.GetComponent<RectTransform>().Find("Frame").gameObject.SetActive(false);
            _extrasLabel = HudUi.Label(_extrasToggle.transform, "Label", "Cards & mob bonuses", theme.SizeTiny, theme.TextSecondary);
            _build = _extrasLabel;
            _buildHint = HudIconHint.Attach(_extrasToggle, _root, "B · Full build details\nClick to show cards and mob bonuses");
            _extrasBody = HudUi.At(_plateBody, "ExtrasBody", 0, y + 16, PlateInner, 62);
            _cardsText = HudUi.Label(HudUi.At(_extrasBody, "Cards", 0, 0, PlateInner, 40), "Label", "Cards: none", theme.SizeSmall, theme.TextPrimary, TextAlignmentOptions.TopLeft, wrap: true);
            _mobText = HudUi.Label(HudUi.At(_extrasBody, "MobBonuses", 0, 42, PlateInner, 18), "Label", "", theme.SizeSmall, theme.TextSecondary, TextAlignmentOptions.TopLeft, wrap: true);
            _mobText.gameObject.SetActive(false); // hidden until a summary arrives
            _extrasBody.gameObject.SetActive(false);
        }

        void RefreshCardsRow(CollectionsView view)
        {
            if (_cardsText == null) return;
            var cards = new StringBuilder();
            for (int i = 0; i < view.Cards.Count; i++)
                if (view.Cards[i] > 0) cards.Append(cards.Length > 0 ? " · " : "").Append(CardDisplayCatalog.Title((byte)i)).Append(" ×").Append(view.Cards[i]);
            Set(_cardsText, cards.Length > 0 ? "Cards: " + cards : view.HasBuild ? "Cards: none" : "Cards: awaiting server");
            // The mob-bonus row is drawn from the build summary in ApplyBuildSummary.
        }

        void ToggleExtras()
        {
            _extrasOpen = !_extrasOpen;
            _extrasBody.gameObject.SetActive(_extrasOpen);
            SetPlateMode(_plateActive);
        }

        /// <summary>Legacy hud.ts:21, the first line of the practice HUD block (no Kills or GV line is shown beside it).</summary>
        internal const string TrainingBanner = "TRAINING — nothing here is recorded: no GV, no stats";
        bool _platePractice;

        /// <summary>Active: health row, build strip and extras. Otherwise: status, vitals and kills lines (legacy hud-status / hud-vitals).</summary>
        void SetPlateMode(bool active)
        {
            _plateActive = active;
            _platePractice = _client != null && _client.ConfirmedPractice;
            if (_healthRow == null) return;
            _healthRow.gameObject.SetActive(active); _buildBox.gameObject.SetActive(active && _gainsShown); _extrasToggle.gameObject.SetActive(active);
            _extrasBody.gameObject.SetActive(active && _extrasOpen);
            _plateStatus.gameObject.SetActive(!active); _vitals.gameObject.SetActive(!active); _combatStats.gameObject.SetActive(!active && !_platePractice);
            float height = active ? 5 + 18 + BuildTop + 5 * BuildRowStep + 3 + 14 + (_extrasOpen ? 66 : 0) + 8 : 5 + (_platePractice ? 31 : 48) + 6;
            _vitalsPanel.sizeDelta = new Vector2(_vitalsPanel.sizeDelta.x, height);
        }

        // ------------------------------------------------------------------ champion combat HUD (section 5)

        void CreateDamageReadout()
        {
            var theme = UiTheme.Current;
            _championHud = _damagePanel = HudUi.Box(_hud, "ChampionCombatHud", HudAnchor.BottomLeft, ChampionLeft, ChampionBottom, 250f, ChampionHeight);
            HudUi.Surface(_championHud, Tone(theme.Surface, .94f), 7f, theme.Border, true, true);
            _championIcon = LaunchUi.FactionShape(_championHud, "FactionIcon", 4, 8f, 7f, 14f, theme.TextPrimary);
            _championName = HudUi.Label(HudUi.At(_championHud, "Head", 26, 6, 130, 16), "Champion", "Champion", 12f, theme.TextPrimary, bold: true);
            _damageWeapon = HudUi.Label(HudUi.At(_championHud, "Weapon", 150, 8, 92, 14), "Weapon", "—", theme.SizeTiny, theme.Accent, TextAlignmentOptions.MidlineRight);
            string[] labels = { "Attack", "Passive", "Wish" };
            for (int i = 0; i < 3; i++)
            {
                var row = HudUi.At(_championHud, "Row" + labels[i], 8, 28 + i * 32, 234, 30);
                row.gameObject.AddComponent<UnityEngine.UI.Image>().color = Tone(theme.SurfaceSunken, .7f);
                row.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
                var rule = HudUi.Box(row, "Rule", HudAnchor.TopLeft, 0, 0, 2, 30);
                rule.gameObject.AddComponent<UnityEngine.UI.Image>().color = theme.AccentDim; rule.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
                _championRow[i] = row;
                _championLabel[i] = HudUi.Label(HudUi.At(row, "Name", 6, 2, 72, 14), "Label", labels[i], theme.SizeTiny, theme.TextSecondary, bold: true);
                _championLabel[i].GetComponent<MythicTextScale>().MinSize = 8f;
                _championValue[i] = HudUi.Label(HudUi.At(row, "Value", 80, 1, 152, 15), "Value", "—", 12f, theme.TextPrimary, bold: true);
                _championValue[i].GetComponent<MythicTextScale>().MinSize = i == 2 ? 8f : 10f;   // the longest Wish value needs the smaller floor
                _championScaling[i] = HudUi.Label(HudUi.At(row, "Scaling", 6, 17, 224, 11), "Scaling", "", theme.SizeMicro, theme.TextSecondary);
            }
            _damageHit = _championValue[0]; _damageRate = _championScaling[0];
            var focus = _championHud.gameObject.AddComponent<UnityEngine.UI.Button>();
            focus.targetGraphic = _championHud.GetComponent<UnityEngine.UI.Image>();
            HudUi.ApplyColors(focus, Color.white);
            _damageHint = HudIconHint.Attach(focus, _root, DamageUnavailable);
        }

        Roguehalla.Net.Protocol.WireHudContent _hudContent;
        /// <summary>P-UNITY-HUD-BUILD-3: the Welcome's HUD content block the panel and slots read (null until delivered). Unset, it is the session's.</summary>
        public Roguehalla.Net.Protocol.WireHudContent HudContent
        {
            get { return _hudContent ?? _session?.Welcome?.Hud; }
            set { _hudContent = value; }
        }

        /// <summary>P-UNITY-HUD-BUILD-3: draws the Wish row (label, value, scaling) for <paramref name="me"/>. With the HUD block the label is the
        /// ability name and the lines are legacy's numbers; without it, the stock "Ability · charges/max" text under the label "Wish".</summary>
        // The Wish row is rebuilt only when an input it reads changes (it runs every frame).
        Roguehalla.Net.Protocol.WireHudContent _wishRowBlock; FactionId _wishRowFaction; bool _wishRowHas, _wishRowKeyValid; AbilityId _wishRowAbility; int _wishRowCharges, _wishRowMax;


        const string DamageUnavailable = "Combat overview\nRaw values are before target mitigation; this is not measured DPS.";






        // ------------------------------------------------------------------ death line (section 8)

        void CreateDeathLine()
        {
            var theme = UiTheme.Current;
            var box = HudUi.Box(_hud, "DeathLine", HudAnchor.Center, 0, 0, 380f, 64f);
            // An opaque dark plate: red letters over bright sand or snow read as pale pink without one (marker contrast, 4.5:1 on any ground).
            HudUi.Surface(box, UiTheme.With(theme.OutlineDark, .9f), 10f, theme.Danger, false, false);
            _deathLine = HudUi.Label(box, "Label", "YOU DIED", theme.SizeDisplay, theme.Danger, TextAlignmentOptions.Center, true, UiFontRole.Display);
            _deathLine.characterSpacing = 6f;
            _deathLine.outlineWidth = .22f; _deathLine.outlineColor = theme.OutlineDark; // legible over any ground
            box.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ stubs that need data the client does not receive

        // Edge-threat glows toward visible enemies are fed by MatchHud.EdgeThreats.cs (the vision radius is delivered, WireVision.Radius).

        // ------------------------------------------------------------------ per-frame presentation

        void RefreshLegacyHud(SelfView me, double serverTimeMs)
        {
            var theme = UiTheme.Current;
            bool alive = me != null && me.Status == ActorStatus.Alive;
            bool practice = _client != null && _client.ConfirmedPractice;
            bool active = alive && _session != null && _session.Phase == MatchPhase.Active && !practice;
            if (active != _plateActive || practice != _platePractice) SetPlateMode(active);
            _barWanted = alive;
            _championWanted = _session != null && Roguehalla.Net.Ui.ChampionPanelText.Visible(alive, _session.Phase == MatchPhase.Active, practice);
            if (_deathLine != null)
            {
                bool dead = me != null && me.Status == ActorStatus.Dead && _session != null && !_results.gameObject.activeSelf && !practice && !Spectating;
                var box = _deathLine.transform.parent.gameObject;
                if (box.activeSelf != dead) box.SetActive(dead);
            }
            if (me == null) { RefreshHudElementVisibility(); return; }

            // Health row value, tone and bounty tag.
            float hp = me.Hp > 0 ? me.Hp : 0f, max = me.MaxHp > 0 ? me.MaxHp : 0f;
            var fill = _healthBarFill != null ? _healthBarFill.GetComponent<UnityEngine.UI.Image>() : null;
            if (fill != null)
            {
                float share = max > 0 ? hp / max : 1f;
                fill.color = share <= .18f ? theme.Danger : share <= .35f ? theme.Warning : theme.Success;
            }
            if (_hpValue != null)
            {
                var value = new StringBuilder().Append(hp.ToString("0")).Append('/').Append(max.ToString("0"));
                if (me.Barrier > 0) value.Append(" +").Append(me.Barrier.ToString("0"));
                if (me.Bounty > 0) value.Append(" <color=").Append(Hex(theme.Accent)).Append("> · Bounty ").Append(me.Bounty).Append("</color>");
                Set(_hpValue, value.ToString());
            }
            if (_championName != null)
            {
                Set(_championName, ShowcasePalette.GetFactionName((int)me.Faction));
                _championName.color = theme.FactionColor((int)me.Faction);
                if (_championIcon != null) { _championIcon.sprite = MapGlyphs.Sprite(LaunchCopy.Houses[Mathf.Clamp((int)me.Faction, 0, 4)].Shape); _championIcon.color = theme.FactionColor((int)me.Faction); }
            }
            // Wish row of the champion HUD: the ability name and numbers the server delivered.
            ApplyWishRow(me);
            RefreshWishTile(me.Wish, serverTimeMs, theme);
            RefreshCooldownSweep(me);
            RefreshHudElementVisibility();
        }






        // ------------------------------------------------------------------ viewport-dependent sizes


        void ApplyLegacyViewport(HudViewport viewport)
        {
            _viewport = viewport;
            PlacePhasePill();
            if (_vitalsPanel != null) _vitalsPanel.sizeDelta = new Vector2(HudLayout.PlateWidth(viewport.UiWidth), _vitalsPanel.sizeDelta.y);
            if (_championHud != null)
                _championHud.sizeDelta = new Vector2(HudLayout.ChampionWidth(viewport.UiWidth, viewport.Narrow), _championHud.sizeDelta.y);
            if (_weaponBar != null)
            {
                // The row shrinks to clear the champion panel and the minimap; the minimap keeps its size.
                float minimap = HudLayout.MinimapSize(viewport.UiWidth, viewport.UiHeight, _uiPrefs != null ? _uiPrefs.MinimapScale : 1f);
                float scale = HudRegions.AbilityRowScale(viewport.UiWidth, ChampionLeft + HudLayout.ChampionWidth(viewport.UiWidth, viewport.Narrow),
                    viewport.UiWidth - HudLayout.MinimapGutter - minimap);
                _weaponBar.localScale = new Vector3(scale, scale, 1f);
            }
            _map?.ApplyViewport(viewport, _uiPrefs != null ? _uiPrefs.MinimapScale : 1f);
            ApplyRewardsAnchor();
            _modernSettings?.ApplyViewport(viewport);
            _helpGuide?.ApplyViewport(viewport);
            _hubCosmetics?.ApplyViewport(viewport); _hubProgress?.ApplyViewport(viewport); _hubGuide?.ApplyViewport(viewport);
            _hubLobby?.ApplyViewport(viewport);
        }

        void ApplyRewardsAnchor()
        {
            if (_rewards == null || _stage == null || _viewport.UiWidth <= 0f) return;
            float mm = HudLayout.MinimapSize(_viewport.UiWidth, _viewport.UiHeight, _uiPrefs != null ? _uiPrefs.MinimapScale : 1f);
            // Right 12, top 56, bottom = minimap + 34 (so ~22 above the minimap), width min(248, 0.42 uiW).
            _rewards.anchorMin = new Vector2(1, 0); _rewards.anchorMax = new Vector2(1, 1); _rewards.pivot = new Vector2(1, .5f);
            _rewards.sizeDelta = new Vector2(HudLayout.ChestWidth(_viewport.UiWidth, _viewport.Narrow), -(56f + mm + 34f));
            _rewards.anchoredPosition = new Vector2(-12f, (mm + 34f - 56f) / 2f);
        }
    }
}
