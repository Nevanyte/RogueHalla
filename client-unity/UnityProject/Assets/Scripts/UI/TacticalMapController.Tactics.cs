using System.Collections.Generic;
using System.Text;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// The Tab map's held texts (contacts in view, quests and traders, the weapon guide): each is rebuilt only when what it shows changed,
    /// with reused builders and lists (P-UI-MAP-PAINT-1).
    /// </summary>
    public sealed partial class TacticalMapController
    {
        // Contacts: two ints per non-self actor in sample order (faction + 1, 0 when none; kind, 255 when unnamed). -1 count = invalid.
        int[] _tacContacts = new int[0];
        int _tacContactCount = -1;
        // Quests and traders: the strings last presented, kept to avoid rewriting a steady scroll on every snapshot.
        List<string> _tacQuests = new List<string>(), _tacQuestsNow = new List<string>();
        QuestView _tacGuideView;
        string _tacGuideCopy;
        readonly StringBuilder _tacText = new StringBuilder();
        readonly List<(int Faction, int Kind, int Count)> _tacGroups = new List<(int Faction, int Kind, int Count)>();
        static string[] _weaponKeys;

        static string WeaponKey(byte id)
        {
            if (_weaponKeys == null)
            {
                var keys = new string[256];
                for (int i = 0; i < 256; i++) keys[i] = ((WeaponId)i).ToString().ToLowerInvariant();
                _weaponKeys = keys;
            }
            return _weaponKeys[id];
        }

        void PresentTactics()
        {
            _title.text = _bound.Phase == MatchPhase.Lobby ? "LOBBY MAP" : "TACTICAL MAP";
            PresentHeader();
            RefreshOwnZone();
            if (_lastContactsSnapshot == _bound.SnapshotsReceived) return;
            _lastContactsSnapshot = _bound.SnapshotsReceived;
            _contacts.Clear(); _bound.Snapshots.Sample(_bound.Snapshots.NewestMs, _contacts);
            int count = 0;
            if (_tacContacts.Length < _contacts.Count * 2) _tacContacts = new int[System.Math.Max(_contacts.Count * 2, _tacContacts.Length * 2)];
            bool changed = false;
            for (int i = 0; i < _contacts.Count; i++)
            {
                var actor = _contacts[i];
                if (actor.Id == _bound.PlayerId) continue;
                int f = actor.Faction.HasValue ? (int)actor.Faction.Value + 1 : 0;
                int k = PlayerWords.IsNamed(actor.Kind) ? (int)actor.Kind : actor.Kind == ActorKind.Player ? 254 : 255;
                if (count >= _tacContactCount || _tacContacts[count * 2] != f || _tacContacts[count * 2 + 1] != k) changed = true;
                if (!changed) { count++; continue; }
                _tacContacts[count * 2] = f; _tacContacts[count * 2 + 1] = k; count++;
            }
            if (changed || count != _tacContactCount)
            {
                // Rewrite the whole signature (the early-out above left the matching prefix untouched, and the rest is written now).
                count = 0;
                for (int i = 0; i < _contacts.Count; i++)
                {
                    var actor = _contacts[i];
                    if (actor.Id == _bound.PlayerId) continue;
                    _tacContacts[count * 2] = actor.Faction.HasValue ? (int)actor.Faction.Value + 1 : 0;
                    _tacContacts[count * 2 + 1] = PlayerWords.IsNamed(actor.Kind) ? (int)actor.Kind : actor.Kind == ActorKind.Player ? 254 : 255;
                    count++;
                }
                _tacContactCount = count;
                BuildContactsText();
            }
            var me = _bound.Me;
            string key = me != null ? WeaponKey((byte)me.Weapon) : "fists";
            if (_counterGuide.ActiveWeapon != key) _counterGuide.SetWeapon(key);
            var legendNow = _counterGuide.LegendRoot != null ? _counterGuide.LegendRoot.GetComponentInChildren<TMP_Text>() : null;
            if (legendNow != null && legendNow.text != _laidLegend && _panel.gameObject.activeSelf) ApplyLayout(true);
            PresentQuestsText();
        }

        void BuildContactsText()
        {
            var text = _tacText; text.Length = 0;
            int shown = 0;
            var groups = _tacGroups; groups.Clear();
            for (int i = 0; i < _contacts.Count; i++)
            {
                var actor = _contacts[i];
                if (actor.Id == _bound.PlayerId) continue;
                shown++;
                if (actor.Kind == ActorKind.Player)
                {
                    text.Append(actor.Faction.HasValue ? ShowcasePalette.GetFactionName((int)actor.Faction.Value) : "Neutral").Append(" · Player\n");
                    continue;
                }
                int f = actor.Faction.HasValue ? (int)actor.Faction.Value : -1;
                int k = PlayerWords.IsNamed(actor.Kind) ? (int)actor.Kind : 255;
                int at = 0;
                while (at < groups.Count && (groups[at].Faction < f || (groups[at].Faction == f && groups[at].Kind < k))) at++;
                if (at < groups.Count && groups[at].Faction == f && groups[at].Kind == k) groups[at] = (f, k, groups[at].Count + 1);
                else groups.Insert(at, (f, k, 1));
            }
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                PlayerWords.AppendContactRow(text, g.Faction < 0 ? null : ShowcasePalette.GetFactionName(g.Faction), (ActorKind)g.Kind, g.Count);
                text.Append('\n');
            }
            if (shown == 0) text.Append("No other contacts in view.");
            _contactsText.text = Trimmed(text);
        }

        /// <summary>The builder's text without trailing newlines (rows are separated, not terminated).</summary>
        static string Trimmed(StringBuilder sb)
        {
            int n = sb.Length;
            while (n > 0 && sb[n - 1] == '\n') n--;
            return sb.ToString(0, n);
        }

        void PresentQuestsText()
        {
            var now = _tacQuestsNow; now.Clear();
            var qs = _bound.Collections.Quests;
            if (!ReferenceEquals(qs, _tacGuideView) || _tacGuideCopy == null)
            {
                _tacGuideView = qs;
                _tacGuideCopy = QuestMapGuide.Copy(qs);
            }
            now.Add(_tacGuideCopy);
            var trade = _bound.Collections.Trade;
            if (trade != null && trade.Available)
                for (int i = 0; i < trade.Merchants.Count; i++) now.Add(trade.Merchants[i].Name);
            bool same = now.Count == _tacQuests.Count;
            for (int i = 0; same && i < now.Count; i++) same = string.Equals(now[i], _tacQuests[i]);
            if (same) return;
            _tacQuestsNow = _tacQuests; _tacQuests = now;
            var quests = _tacText; quests.Length = 0;
            quests.Append(now[0]);
            for (int i = 1; i < now.Count; i++) quests.Append("\n\n").Append(now[i]).Append(" · trader");
            _questsText.text = Trimmed(quests);
        }
    }
}
