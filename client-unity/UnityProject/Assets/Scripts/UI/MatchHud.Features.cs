using System;
using System.Collections.Generic;
using Roguehalla.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Roguehalla.Client
{
    /// <summary>HUD part that builds the stage, viewport and combat HUD, applies display preferences, and tracks the match-end and metrics lines.</summary>
    public sealed partial class MatchHud
    {
        BuildDrawer _drawer;
        TacticalMapController _map;
        RectTransform _help;
        HelpGuideView _helpGuide;
        bool _chestFocus, _chestConsumedDigits;
        PointerEventData _pointerQuery;
        readonly List<RaycastResult> _pointerHits = new List<RaycastResult>(32);
        ModernSettingsView _modernSettings;
        UiDisplayPreferences _uiPrefs;
        TMP_Text _phaseStrip, _metrics;
        double _nextMetricsAt;
        float _frameAverage;
        readonly TMP_Text[] _gainValues = new TMP_Text[9];
        // P-UI-HUD-GARBAGE-1: the GAINED strip is keyed on what it shows (the nine raw gains, the mob rows), never on the summary object.
        // _gainCells is a persistent array while a summary is present (empty while none); _gainCellsVersion counts changed elements.
        Roguehalla.Net.Ui.BuildCell[] _gainCells = Array.Empty<Roguehalla.Net.Ui.BuildCell>(), _gainStore;
        int _gainCellsVersion;
        bool _gainsShown, _gainCellsValid, _gainInkValid, _mobKeyValid, _buildKeyPresent;
        int _gainInkPrimary, _gainInkSecondary, _mobShownCount, _buildKeyMobTypes;
        readonly int[] _gainRaw = new int[9];
        readonly bool[] _gainWritten = new bool[9];
        Roguehalla.Net.Protocol.WireMobBonusRow[] _mobShown = new Roguehalla.Net.Protocol.WireMobBonusRow[8];
        readonly UnityEngine.UI.Image[] _weaponIcons = new UnityEngine.UI.Image[2];
        readonly UnityEngine.UI.Image[] _weaponActiveBars = new UnityEngine.UI.Image[2];
        UnityEngine.UI.Image _potionIcon;
        RectTransform _wishDetails;
        RectTransform _vitalsPanel, _buildStrip, _combatActions, _damagePanel;
        TMP_Text _combatStats;
        // Panels (plate, champion, hotbar, minimap) read console focus only while the console is open; the closed CONSOLE button takes focus on a plain Tab.
        bool PanelHudVisible => !BlocksKeyboardInputCore(_trainingConsole?.IsOpen == true) && !(_map?.IsOpen ?? false);
        public TacticalMapController TacticalMap => _map;
        public bool ChestOwnsDigits => _chestFocus || _chestConsumedDigits;
        public bool ConsumesArrowNavigation => _chestFocus || _map != null && _map.ContainsFocus || ReadingPaneSelected;
        bool ReadingPaneSelected => EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
            EventSystem.current.currentSelectedGameObject.activeInHierarchy &&
            EventSystem.current.currentSelectedGameObject.GetComponent<KeyboardScrollPane>() != null;
        public bool BlocksKeyboardInput => BlocksKeyboardInputCore(true);
        bool BlocksKeyboardInputCore(bool consoleFocus) => ResumeChooserVisible || QuestRewardModalVisible || consoleFocus && TrainingInputConsumed || TrainingOwnsKeyboardCore(consoleFocus) || (_client != null && _client.LaunchPending) || _menu != null && (_menu.gameObject.activeSelf || _results.gameObject.activeSelf ||
            _settings.gameObject.activeSelf || (_signIn != null && _signIn.gameObject.activeSelf) || AnyHubPanelVisible || PlayerFlowVisible || Typing || (_drawer != null && _drawer.IsVisible) || (_help != null && _help.gameObject.activeSelf) || (_map != null && _map.IsPlanning));
        public bool BlocksPointerInput => BlocksKeyboardInput || (_map != null && _map.PointerBlocked) ||
            PointerHitsUi();
        bool PointerHitsUi()
        {
            if (EventSystem.current == null || Mouse.current == null) return false;
            if (_pointerQuery == null) _pointerQuery = new PointerEventData(EventSystem.current);
            _pointerQuery.Reset(); _pointerQuery.position = Mouse.current.position.ReadValue();
            _pointerHits.Clear(); EventSystem.current.RaycastAll(_pointerQuery, _pointerHits);
            return _pointerHits.Count > 0;
        }
        Transform FocusRoot
        {
            get
            {
                if (ResumeChooserVisible) return _resumeOverlay;
                if (QuestRewardModalVisible) return QuestRewardFocus;
                if (_recovery != null && _recovery.IsVisible) return _recovery.Root;
                if (_signIn != null && _signIn.gameObject.activeSelf) return _signIn;
                if (_settings.gameObject.activeSelf) return _settings;
                if (_hubGuide != null && _hubGuide.IsVisible) return _hubGuide.FocusRoot;
                if (_hubProgress != null && _hubProgress.IsVisible) return _hubProgress.Root;
                if (_hubCosmetics != null && _hubCosmetics.IsVisible) return _hubCosmetics.Root;
                if (PlayerFlowFocus != null) return PlayerFlowFocus;
                if (_help != null && _help.gameObject.activeSelf) return _help;
                if (_drawer != null && _drawer.IsVisible) return _root.Find("BuildDrawer");
                if (_results.gameObject.activeSelf) return _results;
                if (_menu.gameObject.activeSelf) return _root;
                if (_chestFocus) return _rewards;
                if (_map != null && _map.IsPlanning) return _root.Find("TacticalMap/PlanningDecisions");
                if (_trainingConsole?.ActionsAvailable == true) return _trainingConsole.IsOpen ? _trainingConsole.Panel : _trainingConsole.Root;
                return null;
            }
        }
        bool Typing
        {
            get
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var input = selected != null ? selected.GetComponent<TMP_InputField>() : null;
                return input != null && input.isFocused;
            }
        }

        /// <summary>
        /// Creates the legacy-sized HUD stages (1 unit = 1 legacy CSS px, see <see cref="HudStage"/>): the in-world HUD stage, and a chrome
        /// stage kept above every modal for the always-visible chrome buttons and the settings/help cards.
        /// </summary>
        void EnsureStage()
        {
            if (_stage != null) return;
            _stage = CreateStage("HudStage", out _stageScaler);
            _chromeStage = CreateStage("ChromeStage", out _chromeScaler);
            _stageScaler.ViewportChanged += ApplyViewport; _chromeScaler.ViewportChanged += ApplyViewport;
        }

        RectTransform CreateStage(string name, out HudStage scaler)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(HudStage));
            go.transform.SetParent(_root, false);
            scaler = go.GetComponent<HudStage>();
            scaler.Relayout();
            return (RectTransform)go.transform;
        }

        /// <summary>Re-solves every size that depends on the laid-out width or height (legacy min()/clamp() widths).</summary>
        void ApplyViewport(HudViewport viewport) { ApplyLegacyViewport(viewport); }

        /// <summary>Builds the in-world HUD in legacy anchors; see MatchHud.Legacy.cs.</summary>
        void CreateCombatHud()
        {
            EnsureStage();
            _hud = Rect(_stage, "PlayerHUD", 0, 0, 1, 1);
            _edgeThreats = new EdgeThreatView(_hud);
            CreatePlate();
            CreateWeaponBar();
            CreateDeathLine();
            _edgeThreats.Root.SetAsFirstSibling();
        }
        void SetBaseSize(TMP_Text label, float size)
        {
            int i = _scaledText.IndexOf(label); if (i >= 0) _textBaseSize[i] = size;
            label.fontSize = size * _showcaseSettings.TextScale; label.fontSizeMin = label.fontSize * .75f; label.fontSizeMax = label.fontSize;
        }
        SentencedHuntPanel _huntPanel;
        public SentencedHuntPanel HuntPanel => _huntPanel;
        void InitializeFeatures()
        {
            InitializeHubGuides();
            _drawer = gameObject.AddComponent<BuildDrawer>(); _drawer.Initialize(_root, RefreshHeaderActionsVisibility);
            _huntPanel = gameObject.AddComponent<SentencedHuntPanel>(); _huntPanel.Initialize(_hud);
            _uiPrefs = new UiDisplayPreferences();
            if (Application.isPlaying) ApplyFolds(UiDisplayPreferences.GainsFoldedStored, UiDisplayPreferences.ChampionFoldedStored);
            _map = gameObject.AddComponent<TacticalMapController>(); _map.Initialize(_client, _root, _uiPrefs, _stage);
            _modernSettings = gameObject.AddComponent<ModernSettingsView>();
            _modernSettings.Initialize(_chromeStage, _showcaseSettings, _map, _uiPrefs, () => _modernSettings.SetVisible(false), () => _client.Leave());
            _settings = _modernSettings.Root;
            _uiPrefs.Changed += ApplyDisplayPreferences;
            ApplyDisplayPreferences();
            _helpGuide = gameObject.AddComponent<HelpGuideView>();
            _helpGuide.Initialize(_chromeStage, () => { ToggleHelpClosed(); });
            _help = _helpGuide.Root;
            InitializePlayerFlows();
        }
        void ApplyDisplayPreferences()
        {
            if (_map == null || _uiPrefs == null) return;
            ApplyLegacyViewport(_viewport);
        }
        // P-UNITY-HUD-STATUS-1: the MatchEnded event's winner (roster id), kept here because Session keeps no copy.
        bool _matchEnded; uint? _winnerId; string _winnerLabel; uint? _winnerLabelFor;
        void NoteMatchEnded(Roguehalla.Net.Protocol.ServerMsg.Events batch)
        {
            foreach (var e in batch.List)
                if (e.Kind == Roguehalla.Net.Protocol.EventKind.MatchEnded) { _matchEnded = true; _winnerId = e.Draw ? null : e.Winner; _winnerLabel = null; _winnerLabelFor = null; _nextMetricsAt = 0d; }
        }
        // A new match on a reused Session (rematch without a fresh Bind) must not inherit the previous winner: entering InMatch clears it.
        void NoteSessionState(SessionState state) { if (state == SessionState.InMatch) ResetMatchEnded(); }
        void ResetMatchEnded() { _matchEnded = false; _winnerId = null; _winnerLabel = null; _winnerLabelFor = null; }
        string WinnerLabel()
        {
            if (!_winnerId.HasValue) return null;
            if (_winnerLabel != null && _winnerLabelFor == _winnerId) return _winnerLabel;
            var roster = _session.Collections.Roster;
            for (int i = 0; i < roster.Count; i++)
                if (roster[i].Id == _winnerId.Value && roster[i].Name != null)
                { _winnerLabel = Roguehalla.Net.Ui.Indicators.DisplayName(roster[i].Name, roster[i].Bot); _winnerLabelFor = _winnerId; return _winnerLabel; }
            return null;
        }
        void TickMetrics()
        {
            if (_metrics == null || _phaseStrip == null) return;
            _frameAverage = _frameAverage <= 0 ? Time.unscaledDeltaTime : Mathf.Lerp(_frameAverage, Time.unscaledDeltaTime, .08f);
            if (Time.realtimeSinceStartupAsDouble < _nextMetricsAt) return;
            _nextMetricsAt = Time.realtimeSinceStartupAsDouble + .25;
            // Legacy perf pill: "144 fps - 32 ms", each half only when enabled in settings; fps tone good >= 50, warn >= 30, bad below.
            var theme = UiTheme.Current;
            string metrics = ""; Color tone = theme.TextSecondary;
            int fps = _frameAverage > 0 ? Mathf.RoundToInt(1 / _frameAverage) : 0;
            if (_uiPrefs.ShowFps && fps > 0) { metrics = fps + " fps"; tone = fps >= 50 ? theme.Success : fps >= 30 ? theme.Warning : theme.Danger; }
            if (_uiPrefs.ShowPing && _session != null) metrics += (metrics.Length == 0 ? "" : " · ") + (_session.Sync.HasSample ? _session.Sync.LastRttMs.ToString("0") + " ms" : "— ms");
            _metrics.text = metrics; _metrics.color = tone;
            if (_metricsPill != null) _metricsPill.gameObject.SetActive(metrics.Length > 0);
            bool runOver = _session != null && _session.State == SessionState.Ended && _matchEnded;
            if (_session == null || _session.State != SessionState.InMatch && !runOver) { ClearPhaseLine(); return; }
            if (_client != null && _client.LaunchPending) { ApplyPhaseLine("VERIFYING DESTINATION", Roguehalla.Net.Ui.Indicators.ArenaLineTone.Normal); return; }
            if (_client != null && _client.ConfirmedPractice) { ApplyPhaseLine("PRACTICE · NO RUN REWARDS", Roguehalla.Net.Ui.Indicators.ArenaLineTone.Normal); return; }
            // Legacy top-centre strip (hud.ts statusLine / updateArena), produced by the library. the room minimum comes from ArenaView.MinPlayers; 0 (not delivered) drops "/min".
            MatchPhase phase = runOver ? MatchPhase.Ended : _session.Collections.Plan != null ? MatchPhase.Planning : (_session.Phase ?? MatchPhase.Lobby);
            var text = Roguehalla.Net.Ui.Indicators.PhaseLine(phase, _session.ServerTimeMs, _session.Collections.Arena, _session.Collections.Roster.Count, runOver ? WinnerLabel() : null, out var lineTone);
            if (text == null) { ClearPhaseLine(); return; }
            ApplyPhaseLine(text, lineTone);
        }
        void ToggleBuild()
        {
            if (_drawer == null || _session == null) return;
            SuspendTrainingForPanel();
            _drawer.SetVisible(!_drawer.IsVisible); if (_drawer.IsVisible) { _drawer.Refresh(_session.Collections, _session.Me, _showcaseSettings.TextScale); _map?.SetOpen(false); if (_help != null) _help.gameObject.SetActive(false); } ReleaseChestFocus();
            RefreshHeaderActionsVisibility();
        }
        void ToggleHelp() { if (_help != null) { SuspendTrainingForPanel(); _help.gameObject.SetActive(!_help.gameObject.activeSelf); if (_help.gameObject.activeSelf) { CloseHubPanels(); _drawer?.SetVisible(false); _map?.SetOpen(false); } ReleaseChestFocus(); RefreshHeaderActionsVisibility(); } }
        void ToggleHelpClosed() { ReleaseChestFocus(); RefreshHeaderActionsVisibility(); }
        public void ReadUiInput()
        {
            if (TrainingInputConsumed) return;
            if (ReadResumeChooserInput()) return;
            if (QuestRewardModalVisible)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) CloseQuestRewardModal();
                else if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
                    UiFocusNavigation.Cycle(QuestRewardFocus, keyboard.shiftKey.isPressed);
                _map?.ReadUiInput(false, true);
                return;
            }
            // Text entry owns this whole read; generic navigation must not remove its
            // focus and reclassify the same keys as planning or world input.
            bool typing = Typing;
            bool trainingOwnsKeys = TrainingOwnsKeyboard;
            if (trainingOwnsKeys) ConsumeTrainingUiUpdate();
            RememberWorldHudFocus();
            _chestConsumedDigits = false;
            var kb = Keyboard.current;
            if (kb != null && kb.altKey.isPressed && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) ToggleFullscreen();
            if (_client != null && _client.ConfirmedPractice && BareTrainingEscape(kb) &&
                (TrainingHigherPanelVisible || _chestFocus)) ConsumeTrainingUiUpdate();
            if (ReadTrainingEscape(kb, typing)) return;
            if (!TrainingHigherPanelVisible && ExpandedTrainingDropdown() != null)
            { ConsumeTrainingUiUpdate(); _map?.ReadUiInput(true, true); return; }
            // Recovery's read-only selectable key is still a focused input field. Escape must
            // clear it and close the Hub panel before the ordinary typing gate runs.
            bool aimEsc = _client != null && _client.EscapeConsumedByAim; // this Esc only closed the Frost Wall aim
            if (kb != null && !aimEsc && AnyHubPanelVisible && kb.escapeKey.wasPressedThisFrame && !kb.ctrlKey.isPressed && !kb.altKey.isPressed && !kb.leftMetaKey.isPressed && !kb.rightMetaKey.isPressed)
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                selected?.GetComponent<TMP_InputField>()?.DeactivateInputField();
                EventSystem.current?.SetSelectedGameObject(null);
                CloseHubPanels();
            }
            bool dialog = trainingOwnsKeys || _settings.gameObject.activeSelf || _results.gameObject.activeSelf || _menu.gameObject.activeSelf || (_signIn != null && _signIn.gameObject.activeSelf) || AnyHubPanelVisible ||
                _drawer != null && _drawer.IsVisible || _help != null && _help.gameObject.activeSelf || PlayerFlowVisible;
            bool unrelatedModal = trainingOwnsKeys || _settings.gameObject.activeSelf || _results.gameObject.activeSelf || _menu.gameObject.activeSelf ||
                (_signIn != null && _signIn.gameObject.activeSelf) || AnyHubPanelVisible || PlayerFlowVisible;
            if (kb != null && !typing)
            {
                bool modified = kb.ctrlKey.isPressed || kb.altKey.isPressed || kb.leftMetaKey.isPressed || kb.rightMetaKey.isPressed || kb.shiftKey.isPressed;
                if (!modified)
                {
                    if (kb.escapeKey.wasPressedThisFrame && OfferOpen && !aimEsc)
                    {
                        CloseOffer();
                        _map?.ReadUiInput(typing, true);
                        return;
                    }
                    if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && OfferOpen && !dialog) AcceptOffer();
                    if (!trainingOwnsKeys && kb.jKey.wasPressedThisFrame && (PlayerFlowVisible || PlayerFlowLauncherAllowed))
                    {
                        if (PlayerFlowVisible) ClosePlayerFlows();
                        else ShowPlayerFlow(DefaultPlayerFlowPage);
                        if (TrainingInputConsumed) return;
                    }
                    if (kb.escapeKey.wasPressedThisFrame && !aimEsc && PlayerFlowVisible) ClosePlayerFlows();
                    // Build and Help can switch between each other, but neither may steal keys from
                    // account, menu, result, guide, or settings panels.
                    if (!unrelatedModal && !PlayerFlowVisible && kb.bKey.wasPressedThisFrame && !(_map?.IsPlanning ?? false))
                    { ToggleBuild(); if (TrainingInputConsumed) return; }
                    if (!unrelatedModal && !PlayerFlowVisible && kb.hKey.wasPressedThisFrame)
                    { ToggleHelp(); if (TrainingInputConsumed) return; }
                    if (kb.escapeKey.wasPressedThisFrame && !aimEsc)
                    {
                        if (_signIn.gameObject.activeSelf) SetSignInVisible(false);
                        else
                        if (_settings.gameObject.activeSelf) ToggleSettings();
                        else if (_help.gameObject.activeSelf) ToggleHelp();
                        else if (_drawer.IsVisible) { _drawer.SetVisible(false); RefreshHeaderActionsVisibility(); }
                        else if (_chestFocus) ReleaseChestFocus();
                        if (TrainingInputConsumed) return;
                    }
                    if (!dialog && _rewards.gameObject.activeSelf && kb.cKey.wasPressedThisFrame)
                    {
                        _chestFocus = !_chestFocus;
                        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_chestFocus ? _options[0].gameObject : null);
                    }
                    if (_chestFocus && !dialog)
                    {
                        _chestConsumedDigits = kb.digit1Key.wasPressedThisFrame || kb.digit2Key.wasPressedThisFrame || kb.digit3Key.wasPressedThisFrame || kb.digit0Key.wasPressedThisFrame;
                        if (kb.digit1Key.wasPressedThisFrame) Choose(0);
                        else if (kb.digit2Key.wasPressedThisFrame) Choose(1);
                        else if (kb.digit3Key.wasPressedThisFrame) Choose(2);
                        else if (kb.digit0Key.wasPressedThisFrame) Choose(-1);
                    }
                }
            }
            var focusRoot = FocusRoot;
            if (kb != null && !typing && focusRoot != null && kb.tabKey.wasPressedThisFrame && !kb.ctrlKey.isPressed && !kb.altKey.isPressed && !kb.leftMetaKey.isPressed && !kb.rightMetaKey.isPressed)
                UiFocusNavigation.Cycle(focusRoot, kb.shiftKey.isPressed);
            _map?.ReadUiInput(typing, dialog || PlayerFlowVisible || _chestFocus);
            if (focusRoot != null) UiBrowserStorage.CaptureTab(true, true);
        }
        public void ReleaseUiFocusForWindowLoss()
        {
            _trainingWindowFocused = false; UnbindTrainingSession();
            _recovery?.SetVisible(false); ReleaseChestFocus(); _chestConsumedDigits = false; _map?.ReleaseTransient();
        }
        void ReleaseChestFocus()
        {
            _chestFocus = false;
            if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null || _rewards == null) return;
            if (EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_rewards)) EventSystem.current.SetSelectedGameObject(null);
        }
        void TickFeatures()
        {
            // The launch menu's account, resume and play state is shown only on the launch screen. While a match covers it, it is
            // refreshed four times a second instead of every frame: each refresh builds several status strings (about 3 KB of
            // garbage a frame in a match, so a garbage collection every second or two). It still refreshes every frame while visible.
            bool launchShown = _menu != null && _menu.gameObject.activeSelf || ResumeChooserVisible;
            var pm = PerfMarks.Begin();
            if (launchShown || Time.unscaledTime >= _nextHiddenAccountRefresh) { _nextHiddenAccountRefresh = Time.unscaledTime + .25f; RefreshAccountMenu(); }
            TickHubAtrium();
            PerfMarks.End(PerfMarks.Sec.HudAccount, pm); pm = PerfMarks.Begin();
            TickMetrics();
            TickLobbyRoster();
            TickPlayerFlows();
            if (TrainingHigherPanelVisible) UnbindTrainingSession();
            _trainingConsole?.Refresh();
            AlignTrainingLauncher();
            PerfMarks.End(PerfMarks.Sec.HudFlows, pm); pm = PerfMarks.Begin();
            _map?.Tick();
            PerfMarks.End(PerfMarks.Sec.HudMap, pm); pm = PerfMarks.Begin();
            RefreshWorldHudVisibility();
            SyncTopCentre();
            UpdateEdgeThreats(); TickTerrain();
            PerfMarks.End(PerfMarks.Sec.HudEdge, pm); pm = PerfMarks.Begin();
            if (_session != null) _drawer?.Refresh(_session.Collections, _session.Me, _showcaseSettings.TextScale);
            PerfMarks.End(PerfMarks.Sec.HudDrawer, pm);
        }
        float _nextHiddenAccountRefresh;

        // In the lobby phase: the seated humans in seat order, then the bots on one line, under a "Players · n humans · m bots" header. The room's own minimum is not on
        // the wire, so no "n/m" is shown in a match (the hub poll's numbers are never used here). Names are plain text.
        TMP_Text _lobbyRoster;
        object _lobbyRosterSource; bool _lobbyRosterOn;
        void TickLobbyRoster()
        {
            bool on = _session != null && _session.State == SessionState.InMatch && _hud != null && _hud.gameObject.activeSelf &&
                _session.Collections.Plan == null && (_session.Phase ?? MatchPhase.Lobby) == MatchPhase.Lobby &&
                !(_client != null && _client.LaunchPending) && _session.Collections.Roster.Count > 0;
            if (!on)
            {
                if (_lobbyRosterOn && _lobbyRoster != null) { _lobbyRoster.gameObject.SetActive(false); _lobbyRosterSource = null; }
                _lobbyRosterOn = false;
                return;
            }
            var roster = _session.Collections.Roster;
            if (_lobbyRosterOn && ReferenceEquals(_lobbyRosterSource, roster)) return;
            if (_lobbyRoster == null)
                _lobbyRoster = MythicUi.Text(_hud, "LobbyRoster", "", new Vector2(.02f, .50f), new Vector2(.27f, .78f), 15, ShowcasePalette.Text, TextAlignmentOptions.TopLeft);
            _lobbyRoster.gameObject.SetActive(true);
            _lobbyRosterOn = true; _lobbyRosterSource = roster;
            _lobbyRoster.text = ComposeLobbyRoster(roster, _session.PlayerId);
        }

        /// <summary>Humans first (the local player marked "(you)"), bots folded onto one line, under a "Players · n humans · m bots" header.</summary>
        public static string ComposeLobbyRoster(System.Collections.Generic.IReadOnlyList<RosterEntry> roster, uint me = 0)
        {
            return Roguehalla.Net.Ui.LobbyActivity.SeatedList(roster, me);
        }
        static int CountCards(CollectionsView view) { int count = 0; foreach (byte card in view.Cards) if (card > 0) count++; return count; }
        /// <summary>Redraws the GAINED cells and the mob-bonus row from the owner's summary; null (none arrived yet) hides the
        /// strip and the mob row rather than showing zeros. Each part is written only when what it shows changed.</summary>
        void ApplyBuildSummary(Roguehalla.Net.Protocol.WireBuildSummary summary)
        {
            bool present = summary != null;
            bool flip = present != _gainsShown;
            if (!present && !flip) return;
            if (flip)
            {
                // A presence flip invalidates every remembered copy: the next summary writes everything.
                _gainsShown = present; _gainCellsValid = false; _mobKeyValid = false; _gainCellsVersion = 0; _headerVersion = -1;
                Array.Clear(_gainWritten, 0, _gainWritten.Length);
                _gainCells = present ? (_gainStore ?? (_gainStore = new Roguehalla.Net.Ui.BuildCell[9])) : Array.Empty<Roguehalla.Net.Ui.BuildCell>();
            }
            if (present) WriteGainedCells(summary);
            WriteMobBonusLine(summary);
            if (flip) SetPlateMode(_plateActive); else RefreshGainsHeader();
        }

        static int PackInk(Color32 c) { return c.r << 24 | c.g << 16 | c.b << 8 | c.a; }

        void WriteGainedCells(Roguehalla.Net.Protocol.WireBuildSummary summary)
        {
            var theme = UiTheme.Current;
            int primary = PackInk(theme.TextPrimary), secondary = PackInk(theme.TextSecondary);
            bool inkChanged = !_gainInkValid || primary != _gainInkPrimary || secondary != _gainInkSecondary;
            _gainInkValid = true; _gainInkPrimary = primary; _gainInkSecondary = secondary;
            for (int i = 0; i < _gainRaw.Length; i++)
            {
                int raw = i < summary.Gains.Length ? summary.Gains[i] : 0;
                bool rawChanged = !_gainCellsValid || raw != _gainRaw[i];
                if (rawChanged)
                {
                    _gainRaw[i] = raw; _gainCells[i] = Roguehalla.Net.Ui.BuildHudText.Cell(i, raw);
                    if (_gainCellsValid) _gainCellsVersion++;
                }
                if (_gainValues[i] == null || !(rawChanged || inkChanged || !_gainWritten[i])) continue;
                _gainWritten[i] = true;
                var cell = _gainCells[i];
                _gainValues[i].text = cell.Value;
                // Legacy "dim" cell: a zero value is muted and regular weight.
                _gainValues[i].color = cell.Dim ? theme.TextSecondary : theme.TextPrimary;
                _gainValues[i].fontStyle = cell.Dim ? FontStyles.Normal : FontStyles.Bold;
            }
            _gainCellsValid = true;
        }

        void WriteMobBonusLine(Roguehalla.Net.Protocol.WireBuildSummary summary)
        {
            var rows = summary != null ? summary.MobRows : null;
            int count = rows != null ? rows.Length : 0;
            bool same = _mobKeyValid && _mobShownCount == count;
            for (int i = 0; same && i < count; i++)
                same = _mobShown[i].MobType == rows[i].MobType && _mobShown[i].Stat == rows[i].Stat && _mobShown[i].Stacks == rows[i].Stacks && _mobShown[i].Total == rows[i].Total;
            if (same) return;
            if (_mobShown.Length < count) _mobShown = new Roguehalla.Net.Protocol.WireMobBonusRow[count];
            for (int i = 0; i < count; i++) _mobShown[i] = rows[i];
            _mobShownCount = count; _mobKeyValid = true;
            if (_mobText == null) return;
            var lines = Roguehalla.Net.Ui.BuildHudText.MobLines(summary);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++)
                sb.Append(i > 0 ? " · " : "").Append(lines[i].Label).Append(lines[i].Total.Length > 0 ? " " + lines[i].Total : "");
            Set(_mobText, sb.ToString());
            _mobText.gameObject.SetActive(lines.Count > 0);
        }

        /// <summary>The BUILD block's own key on what its text reads from the summary: presence and the count of distinct mob types.
        /// True when it changed (and remembers the new state).</summary>
        bool BuildSummaryKeyMoved(Roguehalla.Net.Protocol.WireBuildSummary summary)
        {
            bool present = summary != null; int types = 0;
            var rows = present ? summary.MobRows : null;
            for (int i = 0; rows != null && i < rows.Length; i++)
            {
                bool seen = false;
                for (int j = 0; j < i && !seen; j++) seen = rows[j].MobType == rows[i].MobType;
                if (!seen) types++;
            }
            bool moved = present != _buildKeyPresent || types != _buildKeyMobTypes;
            _buildKeyPresent = present; _buildKeyMobTypes = types;
            return moved;
        }
        /// <summary>Test seam: the text of GAINED cell <paramref name="index"/> as drawn.</summary>
        public string GainedCellText(int index) => _gainValues[index] != null ? _gainValues[index].text : null;
        public bool GainedStripVisible => _buildBox != null && _buildBox.gameObject.activeSelf;

        void RefreshBuildIcons(CollectionsView view)
        {
            ApplyBuildSummary(view.Summary);
            if (view.Inventory.HasValue)
            {
                var inv = view.Inventory.Value;
                for (int i = 0; i < 2; i++)
                {
                    var weapon = i == 0 ? inv.Slot0 : inv.Slot1;
                    if (_weaponIcons[i] == null) continue;
                    _weaponIcons[i].enabled = weapon.HasValue;
                    bool inHand = weapon.HasValue && inv.ActiveSlot == i;
                    if (_weaponActiveBars[i] != null) _weaponActiveBars[i].enabled = inHand;
                    if (_slots[i] != null) HudUi.ApplyColors(_slots[i], inHand ? UiTheme.Current.SurfaceSelected : UiTheme.Current.SurfaceRaised);
                    if (_slotNames[i] != null)
                    {
                        Set(_slotNames[i], weapon.HasValue ? WeaponName(weapon.Value) : "Empty");
                        Color nameColour = weapon.HasValue ? (Color)UiTheme.Current.TextPrimary : AbilityTile.EmptyTextColour(UiTheme.Current);
                        if (_slotNames[i].color != nameColour) _slotNames[i].color = nameColour;
                    }
                    if (weapon.HasValue)
                    {
                        MythicGlyph glyph = weapon == WeaponId.Bow ? MythicGlyph.Bow : weapon == WeaponId.Wand ? MythicGlyph.Wand : weapon == WeaponId.Shield ? MythicGlyph.Shield : weapon == WeaponId.Axe ? MythicGlyph.Axe : MythicGlyph.Sword;
                        _weaponIcons[i].sprite = MythicUi.GlyphSprite(glyph);
                        _weaponIcons[i].color = MythicUi.GlyphColor(glyph);
                    }
                }
            }
            else for (int i = 0; i < _weaponIcons.Length; i++)
            {
                if (_slots[i] != null) HudUi.ApplyColors(_slots[i], UiTheme.Current.SurfaceRaised);
                if (_slotNames[i] != null)
                {
                    Set(_slotNames[i], "Empty");
                    Color nameColour = AbilityTile.EmptyTextColour(UiTheme.Current);
                    if (_slotNames[i].color != nameColour) _slotNames[i].color = nameColour;
                }
                if (_weaponIcons[i] != null) _weaponIcons[i].enabled = false;
                if (_weaponActiveBars[i] != null) _weaponActiveBars[i].enabled = false;
            }
        }
    }
}
