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
            _questActionCaption = _questActionLauncher.GetComponentInChildren<TMP_Text>(true);

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
            else if (_questChoice.HasValue && !_questChoice.Equals(_questChoiceDeferred) &&
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

            bool showHud = worldAvailable && WorldHudVisible && QuestPresentation.HasRewardHud(view);
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
            _questActionLauncher.gameObject.SetActive(_nearbyQuestCount > 0 || pendingCount > 0);
            _questActionLauncher.interactable = _questChoice.HasValue || _nearbyQuestCount > 0;
            Set(_questActionCaption, _questChoice.HasValue ? "Choose quest reward" :
                _nearbyQuestCount > 0 ? "Nearby quest actions · " + _nearbyQuestCount :
                pendingCount > 0 ? "Return to turn in your reward" : "");
            UpdatePromptLane();
        }

        /// <summary>One prompt lane: the pickup pill and the quest launcher share it, side by side when both show. Rewritten only when the pair state changes.</summary>
        void UpdatePromptLane()
        {
            bool pair = _questRewardHud.gameObject.activeSelf && _questActionLauncher.gameObject.activeSelf
                && _pickup != null && _pickup.gameObject.activeSelf;
            if (pair == _promptPair) return;
            _promptPair = pair;
            HudUi.Place(_questRewardHud, pair ? HudRegions.PromptPair(1) : HudRegions.Prompt());
            if (_pickup != null) HudUi.Place((RectTransform)_pickup.transform, PickupRegion(pair));
            RefitLabel(_questActionCaption); RefitLabel(_pickupText);
        }

        static void RefitLabel(TMP_Text label)
        {
            var fit = label != null ? label.GetComponent<MythicTextScale>() : null;
            if (fit != null) fit.Refit();
        }

        void BindQuestRewardActions(Session session, uint epoch, QuestView view)
        {
            int nearby = 0;
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
                    if (!_questChoice.HasValue || _questChoice.Equals(_questChoiceDeferred)) _questChoice = action.Quest;
                    if (!_questChoice.Equals(_questChoiceDeferred)) break;
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
                    Set(nearbyButton.GetComponentInChildren<TMP_Text>(true),
                        action.Action == QuestPresentation.PickupReward || quest == null ? action.Label : quest.Name + " · " + action.Label);
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

        bool WaitingForQuestChoice => _questChoice.HasValue && _questChoice.Equals(_questChoiceSent) &&
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
        }
    }

    /// <summary>Labels and presentation routing for quest data already delivered to this seat.</summary>
    internal static class QuestPresentation
    {
        public const byte Abandon = 1, RewardChoice = 8, PickupReward = 9;

        // Rust board rewards' wire order. These labels are presentation only.
        static readonly string[] Rewards =
        {
            "Warden's Cage", "Crown Power", "Hook", "Shoulder Charge", "Tornado", "Turret", "Rebirth Egg", "Warhorn",
            "Smite", "Spell Shield", "The Bargain", "Trade Up", "Forget-Me-Not Seed", "Swap-Stick", "Chef's Kiss",
            "Lantern Ward", "Cleanse", "Scout Falcon", "Stasis", "Bolas", "WallBuster", "BlindRage", "StillMind",
            "Feed or Hunt", "Warm Coat", "Breaker's Helm"
        };

        public static string RewardName(byte reward) => reward < Rewards.Length ? Rewards[reward] : "Quest spell";

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
            foreach (var row in view.Rows)
                if (IsBoardRow(row)) return true;
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
