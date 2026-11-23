using System;
using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Quest spell slots, nearby interactions and turn-in choices for the local player.</summary>
    public sealed partial class MatchHud
    {
        RectTransform _questRewardHud, _questChoiceOverlay, _nearbyQuestOverlay, _nearbyQuestList;
        readonly RectTransform[] _questSlotTiles = new RectTransform[2];
        readonly (byte Reward, byte Charges, double Seconds)?[] _questSlotKeys = new (byte, byte, double)?[2];
        readonly string[] _questSlotHints = new string[2], _questSlotEquipped = new string[2];
        readonly Roguehalla.Net.Ui.CooldownSpan[] _questSpans = new Roguehalla.Net.Ui.CooldownSpan[2];
        bool _questTilesFresh = true, _promptPair, _questViewKnown;
        float _promptPairScale = 1f;
        bool _promptLaneHooked;
        readonly UnityEngine.UI.Button[] _questChoiceButtons = new UnityEngine.UI.Button[3];
        readonly List<UnityEngine.UI.Button> _nearbyQuestButtons = new List<UnityEngine.UI.Button>();
        TMP_Text _questChoiceTitle, _questChoiceSpells, _questChoiceStatus, _questActionCaption;
        UnityEngine.UI.Button _questActionLauncher;
        Session _questRewardSession;
        QuestView _questRewardView;
        QuestHandle? _questChoice, _questChoiceDeferred, _questChoiceSent;
        uint _questRewardEpoch;
        double _questChoiceRetryAt;
        int _nearbyQuestCount;

        QuestOfferCard _offerCard;
        Session _offerSession;
        uint _offerEpoch;
        int _offerTickFrame = -1;
        // The offer prompt: the handle and giver label last seen in reach, and whether it can be pressed now.
        QuestHandle? _promptOffer;
        string _promptGiver, _promptCaption, _promptCaptionGiver;
        bool _promptCaptionKey;
        bool _offerPrompt, _offerPromptLive;
        public QuestOfferCard OfferCard { get { return _offerCard; } }
        public bool OfferOpen { get { return _offerCard != null && _offerCard.IsOpen; } }

        /// <summary>The HUD's answer to F when there is nothing to pick up: closes an open offer card, else opens the offer in reach.</summary>
        public bool TryInteract()
        {
            if (_offerCard == null) return false;
            if (_offerCard.IsOpen) { CloseOffer(); return true; }
            return OpenOffer();
        }

        void CloseOffer() { if (_offerCard != null) _offerCard.Close(); }

        bool QuestRewardModalVisible => _questChoiceOverlay?.gameObject.activeSelf == true || _nearbyQuestOverlay?.gameObject.activeSelf == true;
        RectTransform QuestRewardFocus => _questChoiceOverlay?.gameObject.activeSelf == true ? _questChoiceOverlay : _nearbyQuestOverlay;

        void InitializeQuestRewardHud()
        {
            _questRewardHud = HudUi.Box(_hud, "QuestRewards", HudRegions.Prompt());
            _questActionLauncher = HudUi.Button(_questRewardHud, "QuestActionLauncher", "", HudAnchor.BottomLeft, 0, 0,
                HudRegions.PromptWidth, HudRegions.PromptHeight, OpenQuestActions, radius: 5f);
            var fill = (RectTransform)_questActionLauncher.transform;
            fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one; fill.pivot = new Vector2(.5f, .5f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            _questActionCaption = HudUi.Label(fill, "Label", "", 11f, UiTheme.Current.TextPrimary, TextAlignmentOptions.Center);
            _offerCard = new QuestOfferCard(_hud, () => AcceptOffer(), CloseOffer);

            _questChoiceOverlay = MythicUi.Panel(_root, "QuestRewardChoiceOverlay", Vector2.zero, Vector2.one, new Color(0, 0, 0, .68f));
            var card = MythicUi.Panel(_questChoiceOverlay, "QuestRewardChoice", new Vector2(.28f, .23f), new Vector2(.72f, .77f));
            _questChoiceTitle = MythicUi.Text(card, "Reward", "", new Vector2(.06f, .63f), new Vector2(.94f, .96f), 20,
                ShowcasePalette.Gold, TextAlignmentOptions.Center, heading: true);
            _questChoiceSpells = MythicUi.Text(card, "Equipped", "", new Vector2(.06f, .48f), new Vector2(.94f, .63f), 14,
                alignment: TextAlignmentOptions.Center);
            for (int i = 0; i < _questChoiceButtons.Length; i++)
            {
                float y = .35f - i * .11f;
                _questChoiceButtons[i] = MythicUi.Button(card, "RewardChoice" + i, "", new Vector2(.12f, y),
                    new Vector2(.88f, y + .095f), () => { }, fontSize: 14);
            }
            _questChoiceStatus = MythicUi.Text(card, "RewardStatus", "", new Vector2(.05f, .01f), new Vector2(.69f, .11f), 12);
            MythicUi.Button(card, "ChooseLater", "LATER", new Vector2(.72f, .012f), new Vector2(.96f, .10f),
                CloseQuestRewardModal, MythicUi.ButtonStyle.Quiet, 12);

            _nearbyQuestOverlay = MythicUi.Panel(_root, "NearbyQuestActionsOverlay", Vector2.zero, Vector2.one, new Color(0, 0, 0, .68f));
            var nearby = MythicUi.Panel(_nearbyQuestOverlay, "NearbyQuestActions", new Vector2(.29f, .25f), new Vector2(.71f, .75f));
            MythicUi.Text(nearby, "Title", "NEARBY QUEST ACTIONS", new Vector2(.04f, .83f), new Vector2(.73f, .98f),
                18, ShowcasePalette.Gold, heading: true);
            MythicUi.Button(nearby, "Close", "CLOSE", new Vector2(.75f, .84f), new Vector2(.96f, .96f),
                CloseQuestRewardModal, MythicUi.ButtonStyle.Quiet, 12);
            QuestJournalView.CreateScroll(nearby, new Vector2(.04f, .04f), new Vector2(.96f, .82f), out _nearbyQuestList, "Actions");

            _questRewardHud.gameObject.SetActive(false);
            _questChoiceOverlay.gameObject.SetActive(false);
            _nearbyQuestOverlay.gameObject.SetActive(false);
        }

        void RefreshQuestRewardHud()
        {
            if (_questRewardHud == null) return;
            var session = _session;
            uint epoch = session?.TimelineEpoch ?? 0;
            if (!ReferenceEquals(_questRewardSession, session) || _questRewardEpoch != epoch)
            {
                ResetQuestRewardHud();
                _questRewardSession = session; _questRewardEpoch = epoch;
            }
            QuestView view = Active && session.LastResult == null ? session.Collections.Quests : null;
            if (view?.Available != true) view = null;
            bool replacement = !ReferenceEquals(_questRewardView, view);
            if (replacement)
            {
                _questRewardView = view;
                _questChoice = null;
                if (_questChoiceDeferred.HasValue && !QuestPresentation.IsPending(view, _questChoiceDeferred.Value)) _questChoiceDeferred = null;
                if (_questChoiceSent.HasValue && !QuestPresentation.IsPending(view, _questChoiceSent.Value)) _questChoiceSent = null;
                BindQuestRewardActions(session, epoch, view);
            }

            bool worldAvailable = view != null && session.Phase == MatchPhase.Active;
            if (!worldAvailable || (_map?.IsOpen ?? false))
            {
                _questChoiceOverlay.gameObject.SetActive(false);
                _nearbyQuestOverlay.gameObject.SetActive(false);
            }
            else if (_questChoice.HasValue && !QuestPresentation.SameHandle(_questChoice, _questChoiceDeferred) &&
                (_questChoiceOverlay.gameObject.activeSelf || _nearbyQuestOverlay.gameObject.activeSelf ||
                    _questJournal?.IsVisible == true || WorldHudVisible))
            {
                bool newlyOpen = !_questChoiceOverlay.gameObject.activeSelf;
                _nearbyQuestOverlay.gameObject.SetActive(false);
                _questChoiceOverlay.gameObject.SetActive(true);
                if (newlyOpen)
                {
                    if (_questJournal?.IsVisible == true) ClosePlayerFlows();
                    _questChoiceOverlay.SetAsLastSibling();
                    ReleaseChestFocus();
                    UiFocusNavigation.Cycle(_questChoiceOverlay, false);
                }
            }
            else
            {
                _questChoiceOverlay.gameObject.SetActive(false);
                if (_nearbyQuestCount == 0) _nearbyQuestOverlay.gameObject.SetActive(false);
            }

            RefreshOffer(view, worldAvailable);
            bool showHud = worldAvailable && WorldHudVisible && (_offerPrompt || QuestPresentation.HasRewardHud(view));
            _questRewardHud.gameObject.SetActive(showHud);
            if (session != null) RefreshQuestTiles(view, session.ServerTimeMs);
            if (_questChoiceOverlay.gameObject.activeSelf)
            {
                bool waiting = WaitingForQuestChoice;
                foreach (var button in _questChoiceButtons) button.interactable = !waiting;
                Set(_questChoiceStatus, waiting ? "Waiting for server…" : "");
            }
            if (!showHud) { UpdatePromptLane(); return; }

            int pendingCount = view.Pending.Count;
            _questActionLauncher.gameObject.SetActive(_nearbyQuestCount > 0 || pendingCount > 0 || _offerPrompt);
            bool others = _questChoice.HasValue || _nearbyQuestCount > 0;
            _questActionLauncher.interactable = others || _offerPrompt && _offerPromptLive;
            if (!others && _offerPrompt) Set(_questActionCaption, OfferCaption());
            else Set(_questActionCaption, _questChoice.HasValue ? "Choose quest reward" :
                _nearbyQuestCount > 0 ? NearbyActionsCaption() :
                pendingCount > 0 ? "Return to turn in your reward" : "");
            UpdatePromptLane();
        }

        string _nearbyCaption = "";

        /// <summary>The launcher caption for the nearby actions, built when they are bound.</summary>
        string NearbyActionsCaption() => _nearbyCaption;

        /// <summary>"F · Talk to giver", or "Talk to giver" beside the pickup pill; rebuilt only when the giver or the pair changes.</summary>
        string OfferCaption()
        {
            bool beside = _pickup != null && _pickup.gameObject.activeSelf;
            bool key = !beside;
            if (_promptCaption == null || !string.Equals(_promptCaptionGiver, _promptGiver) || _promptCaptionKey != key)
            {
                _promptCaptionGiver = _promptGiver; _promptCaptionKey = key;
                _promptCaption = (key ? "F \u00b7 Talk to " : "Talk to ") + _promptGiver;
            }
            return _promptCaption;
        }

        bool OfferGatesHold(Session session)
        {
            if (session == null || !ReferenceEquals(session, _session) || !Active || session.Phase != MatchPhase.Active || session.LastResult != null) return false;
            var me = session.Me;
            return me != null && me.Status == ActorStatus.Alive && WorldHudVisible;
        }

        bool OpenOffer()
        {
            var session = _session;
            if (_offerCard == null || !OfferGatesHold(session)) return false;
            var view = session.Collections.Quests;
            var me = session.Me;
            int reach;
            var pick = QuestOffer.Pick(view, me.X, me.Y, out reach);
            if (pick == null || !QuestPresentation.ShowsOffer(pick)) return false;
            _offerSession = session; _offerEpoch = session.TimelineEpoch;
            _offerCard.Open(pick, reach, session.ServerTimeMs);
            PlaceOffer();
            return true;
        }

        bool AcceptOffer()
        {
            var card = _offerCard; var session = _session;
            if (card == null || !card.IsOpen || card.Accepting || !card.Shown.HasValue || session == null || !ReferenceEquals(session, _offerSession)) return false;
            if (session.State != SessionState.InMatch || session.LastResult != null || session.Phase != MatchPhase.Active || session.TimelineEpoch != _offerEpoch) return false;
            var row = QuestOffer.Find(session.Collections.Quests, card.Shown.Value);
            if (row == null || !row.CanAccept) return false;
            session.Input.QuestAccept(card.Shown.Value);
            card.MarkAccepting();
            return true;
        }

        /// <summary>Ticks, refreshes, closes and places the offer card, and sets the prompt state. Runs before the box is shown or hidden.</summary>
        void RefreshOffer(QuestView view, bool worldAvailable)
        {
            var card = _offerCard;
            if (card == null) return;
            if (_offerTickFrame != Time.frameCount)
            {
                _offerTickFrame = Time.frameCount;
                card.Tick(Mathf.Min(Time.unscaledDeltaTime, .1f));
            }
            var session = _session;
            bool gates = worldAvailable && OfferGatesHold(session);
            QuestRowView pick = null;
            int reach = 0;
            if (gates)
            {
                var me = session.Me;
                pick = QuestOffer.Pick(view, me.X, me.Y, out reach);
                if (pick != null && !QuestPresentation.ShowsOffer(pick)) pick = null;
            }
            if (card.IsOpen)
            {
                QuestRowView row = null;
                if (gates && card.Shown.HasValue && ReferenceEquals(session, _offerSession) && session.TimelineEpoch == _offerEpoch)
                    row = QuestOffer.Find(view, card.Shown.Value);
                if (row == null || row.OwnerAccepted || !QuestPresentation.ShowsOffer(row)) card.Close();
                else
                {
                    card.NoteReach(row.CanAccept);
                    if (card.Lapsed) card.Close();
                    else
                    {
                        card.Refresh(row, reach, session.ServerTimeMs);
                        PlaceOffer();
                    }
                }
            }
            if (card.IsOpen || !gates)
            {
                _offerPrompt = false; _offerPromptLive = false;
                if (!gates) _promptOffer = null;
                return;
            }
            card.NoteReach(pick != null);
            if (pick != null)
            {
                _promptOffer = pick.Handle; _promptGiver = pick.GiverLabel ?? "";
                _offerPrompt = true; _offerPromptLive = true;
            }
            else
            {
                bool held = _promptOffer.HasValue && !card.Lapsed;
                if (!held) _promptOffer = null;
                _offerPrompt = held; _offerPromptLive = false;
            }
        }

        void PlaceOffer()
        {
            var card = _offerCard;
            if (card == null || _hud == null) return;
            int rows = _questTracker != null ? _questTracker.Rows : 0;
            float dockTop = PlateChainBottom() + HudRegions.TrackerGap;
            float floor = TrackerFloor();
            var region = HudRegions.OfferCard(_hud.rect.height, dockTop, rows, card.WantedHeight, floor + HudRegions.TrackerGap);
            var dock = HudRegions.TrackerDock(dockTop, rows);
            float top = QuestOffer.Lift(region.Y, region.Height, dock.Y + dock.Height, floor);
            card.Place(new HudRegion(region.Anchor, region.X, top, region.Width, region.Height));
        }

        /// <summary>One prompt lane: the pickup pill and the quest launcher share it, side by side when both show. Rewritten only when the pair state changes.</summary>
        void UpdatePromptLane()
        {
            if (!_promptLaneHooked && _stageScaler != null)
            {
                // The row's scale changes with the viewport, which runs no refresh: follow it (the scaler lives as long as this HUD).
                _promptLaneHooked = true;
                _stageScaler.ViewportChanged += FollowRowScale;
            }
            bool pair = _questRewardHud.gameObject.activeSelf && _questActionLauncher.gameObject.activeSelf
                && _pickup != null && _pickup.gameObject.activeSelf;
            float raw = _weaponBar != null ? _weaponBar.localScale.x : 1f;
            bool stateChanged = pair != _promptPair;
            if (!stateChanged && raw == _promptPairScale) return;
            _promptPair = pair;
            _promptPairScale = raw;
            float s = HudRegions.PairScale(raw);
            var at = pair ? HudRegions.PromptPair(1, s) : HudRegions.PromptLone(s);
            var box = new HudRegion(at.Anchor, at.X, at.Y, pair ? HudRegions.PromptPairWidth : HudRegions.PromptWidth, HudRegions.PromptHeight);
            HudUi.Place(_questRewardHud, box);
            _questRewardHud.localScale = s == 1f ? Vector3.one : new Vector3(s, s, 1f);
            if (!stateChanged) return;
            if (_pickup != null) HudUi.Place((RectTransform)_pickup.transform, PickupRegion(pair));
            RefitLabel(_questActionCaption); RefitLabel(_pickupText);
        }

        void FollowRowScale(HudViewport viewport)
        {
            if (_questRewardHud != null && _questActionLauncher != null) UpdatePromptLane();
        }

        static void RefitLabel(TMP_Text label)
        {
            var fit = label != null ? label.GetComponent<MythicTextScale>() : null;
            if (fit != null) fit.Refit();
        }

        void BindQuestRewardActions(Session session, uint epoch, QuestView view)
        {
            int nearby = 0;
            string firstText = null;
            foreach (var button in _questChoiceButtons)
            {
                button.onClick.RemoveAllListeners();
                button.gameObject.SetActive(false);
            }
            Set(_questChoiceStatus, "");
            if (view != null)
            {
                foreach (var action in view.Actions)
                {
                    if (action.Action != QuestPresentation.RewardChoice) continue;
                    if (!QuestPresentation.IsPending(view, action.Quest)) continue;
                    if (!_questChoice.HasValue || QuestPresentation.SameHandle(_questChoice, _questChoiceDeferred)) _questChoice = action.Quest;
                    if (!QuestPresentation.SameHandle(_questChoice, _questChoiceDeferred)) break;
                }
                if (_questChoice.HasValue)
                {
                    var row = QuestPresentation.FindRow(view, _questChoice.Value);
                    Set(_questChoiceTitle, row == null ? "Choose a quest reward" : row.Name + "\n" + row.Reward);
                }
                foreach (var action in view.Actions)
                {
                    if (action.Action == QuestPresentation.RewardChoice)
                    {
                        if (!_questChoice.HasValue || !_questChoice.Value.Equals(action.Quest) || action.Target >= _questChoiceButtons.Length)
                            continue;
                        var button = _questChoiceButtons[action.Target];
                        button.gameObject.SetActive(true);
                        Set(button.GetComponentInChildren<TMP_Text>(true), action.Label);
                        button.onClick.AddListener(() => SubmitQuestRewardChoice(session, epoch, view, action.Quest, action.Action, action.Target));
                        continue;
                    }
                    // The journal owns abandonment. The HUD surfaces the remaining offered interactions.
                    if (action.Action == QuestPresentation.Abandon) continue;
                    var nearbyButton = QuestJournalView.ActionButton(_nearbyQuestButtons, _nearbyQuestList, nearby++);
                    var quest = QuestPresentation.FindRow(view, action.Quest);
                    string text = QuestPresentation.ActionText(view, action);
                    if (nearby == 1) firstText = string.IsNullOrEmpty(action.Label) ? null : text;
                    Set(nearbyButton.GetComponentInChildren<TMP_Text>(true), text);
                    nearbyButton.onClick.RemoveAllListeners();
                    nearbyButton.onClick.AddListener(() =>
                    {
                        if (!_nearbyQuestOverlay.gameObject.activeSelf || !ReferenceEquals(_session, session)) return;
                        if (QuestPresentation.TrySend(session, epoch, view, action.Quest, action.Action, action.Target))
                            CloseQuestRewardModal();
                    });
                    nearbyButton.gameObject.SetActive(true);
                }
            }
            for (int i = nearby; i < _nearbyQuestButtons.Count; i++)
            {
                _nearbyQuestButtons[i].onClick.RemoveAllListeners();
                _nearbyQuestButtons[i].gameObject.SetActive(false);
            }
            _nearbyQuestCount = nearby;
            _nearbyCaption = nearby == 0 ? "" : nearby == 1 ? firstText ?? "Nearby quest actions \u00b7 1" : "Nearby quest actions \u00b7 " + nearby;
        }

        void SubmitQuestRewardChoice(Session session, uint epoch, QuestView view, QuestHandle quest, byte action, byte target)
        {
            if (!_questChoiceOverlay.gameObject.activeSelf || !ReferenceEquals(_session, session) ||
                !_questChoice.HasValue || !_questChoice.Value.Equals(quest) || WaitingForQuestChoice) return;
            if (!QuestPresentation.TrySend(session, epoch, view, quest, action, target)) return;
            _questChoiceSent = quest;
            _questChoiceRetryAt = Time.unscaledTimeAsDouble + 1;
            Set(_questChoiceStatus, "Waiting for server…");
            foreach (var button in _questChoiceButtons) button.interactable = false;
        }

        bool WaitingForQuestChoice => _questChoice.HasValue && QuestPresentation.SameHandle(_questChoice, _questChoiceSent) &&
            Time.unscaledTimeAsDouble < _questChoiceRetryAt;

        /// <summary>The Shift and Q tiles and the "Equipped" line, from the delivered spells (both Empty without a quest view or spell).
        /// Strings are built only when a slot's change key differs, so a steady frame allocates nothing.</summary>
        void RefreshQuestTiles(QuestView view, double serverTime)
        {
            bool changed = false;
            var spells = view?.Spells;
            int count = spells != null ? spells.Count : 0;
            for (int i = 0; i < _questTiles.Length; i++)
            {
                var tile = _questTiles[i];
                if (tile == null) continue;
                (byte Reward, byte Charges, double Seconds)? key = null;
                double readyAt = 0;
                for (int k = 0; k < count; k++)
                {
                    var spell = spells[k];
                    if (spell.Slot != i) continue;
                    readyAt = spell.ReadyAt;
                    key = (spell.Reward, spell.Charges, Math.Ceiling(Math.Max(0, spell.ReadyAt - serverTime) / 1000));
                }
                bool known = view != null;
                if (_questTilesFresh || !Nullable.Equals(key, _questSlotKeys[i]) || known != _questViewKnown || _questSlotHints[i] == null)
                {
                    _questSlotKeys[i] = key; changed = true;
                    _questSlotHints[i] = key.HasValue ? QuestSpellText(i, key) : QuestSpellHint(i, known);
                    _questSlotEquipped[i] = QuestSpellText(i, key).Replace('\n', ' ');
                }
                if (key.HasValue)
                {
                    double total = _questSpans[i].Total(readyAt, serverTime);
                    var face = AbilityFaces.Spell(true, key.Value.Charges, readyAt, serverTime, total);
                    tile.Show(face, QuestPresentation.RewardName(key.Value.Reward), _questSlotHints[i]);
                    tile.Cooldown(readyAt, total);
                }
                else
                {
                    _questSpans[i] = default(Roguehalla.Net.Ui.CooldownSpan);
                    tile.Show(AbilityFace.Empty, "", _questSlotHints[i]);
                    tile.Cooldown(0, 0);
                }
            }
            _questViewKnown = view != null;
            _questTilesFresh = false;
            if (changed || _questChoiceSpells.text.Length == 0)
                Set(_questChoiceSpells, _questSlotEquipped[0] + "\n" + _questSlotEquipped[1]);
        }

        /// <summary>The hint of a tile with no spell in it.</summary>
        static string QuestSpellHint(int slot, bool questViewKnown)
        {
            string first = (slot == 0 ? "Shift" : "Q") + " \u00b7 Quest spell";
            return questViewKnown ? first + "\nEmpty: a quest reward fills this slot" : first;
        }

        static string QuestSpellText(int slot, (byte Reward, byte Charges, double Seconds)? key)
        {
            string hotkey = slot == 0 ? "SHIFT" : "Q";
            if (!key.HasValue) return hotkey + " \u00b7 Empty";
            var spell = key.Value;
            string cooldown = spell.Seconds > 0 ? spell.Seconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "s" : "Ready";
            return hotkey + " \u00b7 " + QuestPresentation.RewardName(spell.Reward) + "\n" +
                spell.Charges + (spell.Charges == 1 ? " charge \u00b7 " : " charges \u00b7 ") + cooldown;
        }

        void OpenQuestActions()
        {
            if (!_questActionLauncher.gameObject.activeInHierarchy || !WorldHudVisible) return;
            if (_questChoice.HasValue)
            {
                _questChoiceDeferred = null;
                RefreshQuestRewardHud();
            }
            else if (_nearbyQuestCount > 0)
            {
                ReleaseChestFocus();
                _nearbyQuestOverlay.gameObject.SetActive(true);
                _nearbyQuestOverlay.SetAsLastSibling();
                UiFocusNavigation.Cycle(_nearbyQuestOverlay, false);
            }
            else if (_offerPrompt && _offerPromptLive) OpenOffer();
        }

        void CloseQuestRewardModal()
        {
            if (_questChoiceOverlay?.gameObject.activeSelf == true) _questChoiceDeferred = _questChoice;
            _questChoiceOverlay?.gameObject.SetActive(false);
            _nearbyQuestOverlay?.gameObject.SetActive(false);
        }

        void ResetQuestRewardHud()
        {
            CloseQuestRewardModal();
            CloseOffer();
            _offerSession = null; _promptOffer = null; _offerPrompt = _offerPromptLive = false;
            _questRewardHud?.gameObject.SetActive(false);
            _questTilesFresh = true;
            for (int i = 0; i < _questTiles.Length; i++)
            {
                _questSlotKeys[i] = null; _questSlotHints[i] = null; _questSpans[i] = default(Roguehalla.Net.Ui.CooldownSpan);
                if (_questTiles[i] != null) { _questTiles[i].Show(AbilityFace.Empty, "", QuestSpellHint(i, false)); _questTiles[i].Cooldown(0, 0); }
            }
            if (_questRewardHud != null) UpdatePromptLane();
            _questRewardSession = null;
            _questRewardView = null;
            _questChoice = _questChoiceDeferred = _questChoiceSent = null;
            _nearbyQuestCount = 0;
            _nearbyCaption = "";
            Set(_questActionCaption, "");
        }
    }

    /// <summary>Labels and presentation routing for quest data already delivered to this seat.</summary>
    internal static class QuestPresentation
    {
        public const byte Abandon = 1, RewardChoice = 8, PickupReward = 9;

        /// <summary>Null-aware handle compare that boxes nothing (both null equal, one null not).</summary>
        public static bool SameHandle(QuestHandle? a, QuestHandle? b) =>
            a.HasValue ? b.HasValue && a.Value.Equals(b.Value) : !b.HasValue;

        // Rust board rewards' wire order. These labels are presentation only.
        static readonly string[] Rewards =
        {
            "Warden's Cage", "Crown Power", "Hook", "Shoulder Charge", "Tornado", "Turret", "Rebirth Egg", "Warhorn",
            "Smite", "Spell Shield", "The Bargain", "Trade Up", "Forget-Me-Not Seed", "Swap-Stick", "Chef's Kiss",
            "Lantern Ward", "Cleanse", "Scout Falcon", "Stasis", "Bolas", "WallBuster", "BlindRage", "StillMind",
            "Feed or Hunt", "Warm Coat", "Breaker's Helm"
        };

        public static string RewardName(byte reward) => reward < Rewards.Length ? Rewards[reward] : "Quest spell";

        /// <summary>Window and launcher text of a nearby action: its label, then the quest name when there is one.</summary>
        internal static string ActionText(QuestView view, QuestActionView action)
        {
            string label = action.Label ?? "";
            var row = FindRow(view, action.Quest);
            return action.Action == PickupReward || row == null || string.IsNullOrEmpty(row.Name) ? label : label + " \u00b7 " + row.Name;
        }

        public static QuestRowView FindRow(QuestView view, QuestHandle handle)
        {
            foreach (var row in view.Rows)
                if (row.Handle.Equals(handle)) return row;
            return null;
        }

        public static bool IsPending(QuestView view, QuestHandle handle)
        {
            if (view == null) return false;
            foreach (var quest in view.Pending)
                if (quest.Equals(handle)) return true;
            return false;
        }

        public static bool HasRewardHud(QuestView view)
        {
            if (view.Spells.Count != 0 || view.Pending.Count != 0 || view.Actions.Count != 0) return true;
            return HasBoardRows(view);
        }

        public static bool HasBoardRows(QuestView view)
        {
            if (view == null) return false;
            var rows = view.Rows;
            for (int i = 0; i < rows.Count; i++)
                if (IsBoardRow(rows[i])) return true;
            return false;
        }

        // Multiword board IDs use underscores. Legacy IDs use hyphens or distinct single words.
        public static bool IsBoardRow(QuestRowView row) =>
            row.Def == "letters" || row.Def == "watchtowers" || row.Def == "gatecrasher" || row.Def?.IndexOf('_') >= 0;
        public static bool ShowsOffer(QuestRowView row) => !row.OwnerCompleted || !IsBoardRow(row);

        public static bool TrySend(Session session, uint epoch, QuestView view, QuestHandle quest, byte action, byte target)
        {
            if (session == null || session.State != SessionState.InMatch || session.LastResult != null ||
                session.TimelineEpoch != epoch || view?.Available != true || !ReferenceEquals(session.Collections.Quests, view))
                return false;
            foreach (var offered in view.Actions)
                if (offered.Quest.Equals(quest) && offered.Action == action && offered.Target == target)
                {
                    session.Input.QuestInteract(new WireQuestAction
                    {
                        Quest = offered.Quest,
                        Action = offered.Action,
                        Target = offered.Target,
                    });
                    return true;
                }
            return false;
        }
    }
}
