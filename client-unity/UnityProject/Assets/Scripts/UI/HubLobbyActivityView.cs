using System;
using System.Globalization;
using System.Text;
using TMPro;
using Roguehalla.Net;
using Roguehalla.Net.Ui;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// The hub's lobby-activity card (legacy ui/lobbyActivity.ts): who is in a game and who waits in the hub. Polled every 15 s while the
    /// hub is up (never during a match); a signed-in hub posts its presence as the poll, an anonymous one only reads. Names are plain text; only the card's own status lines are coloured.
    /// </summary>
    public sealed class HubLobbyActivityView : MonoBehaviour
    {
        RectTransform _panel;
        TMP_Text _body;
        MythicTextScale _scale;
        MatchClient _client;
        LobbyActivityTransport _transport;
        LobbyActivityModel _model = LobbyActivityModel.Empty;
        float _receivedAt = float.NegativeInfinity, _nextPollAt, _nextRenderAt;
        bool _inHub, _shown, _inFlight;
        int _generation;
        string _tabId, _presenceName, _presenceToken;
        Uri _presenceBase;

        public bool IsVisible => _panel != null && _panel.gameObject.activeSelf;
        public LobbyActivityModel Model => _model;
        public string Text => _body == null ? "" : _body.text;

        public void Initialize(Transform root, MatchClient client)
        {
            _client = client;
            _tabId = Guid.NewGuid().ToString("N");
            _transport = gameObject.GetComponent<LobbyActivityTransport>() ?? gameObject.AddComponent<LobbyActivityTransport>();
            _panel = MythicUi.Panel(root, "HubLobbyActivityPanel", new Vector2(.76f, .06f), new Vector2(.99f, .46f), ShowcasePalette.Panel, false);
            _body = MythicUi.Text(_panel, "LobbyActivityBody", "", new Vector2(.02f, .02f), new Vector2(.98f, .98f), 15, ShowcasePalette.Text, TextAlignmentOptions.TopLeft);
            // The unavailable state is nine lines. MythicUi.Text attached a MythicTextScale (and MatchHud.ApplyTextScale rewrites size and minimum through it on every
            // settings change, which undid plain auto-size): put it in fit-to-box mode so the size is the largest from 15 x scale down to 11 at which the text fits the card.
            _scale = _body.GetComponent<MythicTextScale>();
            if (_scale != null) { _scale.BaseSize = 15f; _scale.FitBox = true; _scale.MinSize = 11f; }
            _body.enableAutoSizing = true; _body.fontSizeMin = 11f; _body.fontSizeMax = 15f;   // until the first Apply
            _body.richText = true;   // status colours only: every name has its angle brackets stripped (LobbyActivity.CleanName)
            _panel.gameObject.SetActive(false);
        }

        /// <summary>Card size and right/top inset in HUD units (legacy stage): the empty top-right floor of the hub room.</summary>
        public const float CardWidth = 300f, CardHeight = 150f, InsetRight = 12f, InsetTop = 122f;

        /// <summary>Places the card for the stage <paramref name="viewport"/> (HUD units) clear of the hub room's tiles, top right under the wordmark row.</summary>
        public void ApplyViewport(HudViewport viewport)
        {
            if (_panel == null) return;
            float w = Mathf.Max(viewport.UiWidth, 320f), h = Mathf.Max(viewport.UiHeight, 320f);
            float width = Mathf.Min(CardWidth, w - 2f * InsetRight), height = Mathf.Min(CardHeight, h - InsetTop - InsetRight);
            _panel.anchorMin = new Vector2(1f - (InsetRight + width) / w, 1f - (InsetTop + height) / h);
            _panel.anchorMax = new Vector2(1f - InsetRight / w, 1f - InsetTop / h);
            _panel.offsetMin = _panel.offsetMax = Vector2.zero;
            if (_scale != null && _panel.gameObject.activeInHierarchy) _scale.Refit();   // the box changed with the window
        }

        /// <summary>Called every frame by the hub: <paramref name="inHub"/> while the join menu is up, <paramref name="shown"/> when no other hub panel covers the card.</summary>
        public void SetHub(bool inHub, bool shown)
        {
            if (_panel == null) return;
            if (inHub != _inHub)
            {
                _inHub = inHub;
                if (inHub) { _nextPollAt = 0; Render(); }
                else Leave();
            }
            if (shown != _shown || _panel.gameObject.activeSelf != (inHub && shown))
            {
                _shown = shown;
                _panel.gameObject.SetActive(inHub && shown);
                if (inHub && shown) Render();
            }
        }

        void Update()
        {
            if (!_inHub || _client == null) return;
            if (Time.realtimeSinceStartup >= _nextPollAt && !_inFlight) Poll();
            else if (_shown && Time.realtimeSinceStartup >= _nextRenderAt) { _nextRenderAt = Time.realtimeSinceStartup + 1f; Render(); }
        }

        void Poll()
        {
            _nextPollAt = Time.realtimeSinceStartup + LobbyActivity.PollMs / 1000f;
            AccountEndpoint endpoint;
            try { endpoint = _client.ResolveAccountEndpoint(); }
            catch (ArgumentException) { Render(); return; }
            _inFlight = true;
            int generation = _generation;
            Action<int, string> done = (status, body) =>
            {
                _inFlight = false;
                if (generation != _generation || !_inHub) return;
                if (status == 200)
                {
                    var parsed = LobbyActivity.Parse(body);
                    // A failed or unreadable poll keeps the last good snapshot and its age.
                    if (parsed.Available) { _model = parsed; _receivedAt = Time.realtimeSinceStartup; }
                }
                Render();
            };
            if (TryPresence(endpoint, out string name, out string token) && LobbyActivityTransport.PresenceBody(name, _tabId) != null)
            {
                _presenceBase = endpoint.ApiBase; _presenceName = name; _presenceToken = token;
                _transport.Heartbeat(endpoint.ApiBase, token, name, _tabId, done);
            }
            else _transport.Get(endpoint.ApiBase, null, done);
        }

        bool TryPresence(AccountEndpoint endpoint, out string name, out string token)
        {
            name = token = null;
            var snapshot = _client.AccountOwner?.Snapshot;
            if (snapshot == null || snapshot.State != AccountHubState.Owned || string.IsNullOrEmpty(snapshot.SelectedName)) return false;
            try
            {
                string normalized = AccountEndpoint.NormalizeName(snapshot.SelectedName);
                string stored = new PlayerPrefsAccountCredentials().Read(endpoint.CredentialStorageScope, normalized);
                if (!AccountSession.ValidToken(stored)) return false;
                name = snapshot.SelectedName; token = stored;
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        void Leave()
        {
            _generation++;
            _inFlight = false;
            if (_presenceToken != null && _transport != null) _transport.Withdraw(_presenceBase, _presenceToken, _presenceName, _tabId);
            _presenceToken = null;
        }

        void OnDisable() { if (_inHub) { _inHub = false; Leave(); } }
        void OnApplicationQuit() { if (_inHub) { _inHub = false; Leave(); } }

        void Render()
        {
            if (_body == null) return;
            double age = (Time.realtimeSinceStartup - _receivedAt) * 1000.0;
            var theme = UiTheme.Current;
            var style = new Style(Hex(theme.TextSecondary), Hex(theme.Success), Hex(theme.Accent));
            _body.text = Compose(_model, _model.Available && age <= LobbyActivity.StaleMs ? (long)Math.Max(0, age) : long.MaxValue, _model.Available, style);
        }

        static string Hex(Color32 c) { return "#" + ColorUtility.ToHtmlStringRGB(c); }

        /// <summary>Rich-text colours for <see cref="Compose"/>: muted section lines, a "joinable" tone for rooms still gathering, an accent for running ones.</summary>
        public sealed class Style
        {
            public readonly string Muted, Open, Running;
            public Style(string muted, string open, string running) { Muted = muted; Open = open; Running = running; }
        }

        /// <summary>
        /// The card text. <paramref name="ageMs"/> is the age of <paramref name="model"/>; names are already plain and bounded (angle
        /// brackets removed by <see cref="LobbyActivity.CleanName"/>), so they cannot carry rich-text tags. With no <paramref name="style"/>
        /// the text is plain (tests). Layout: a "Players online · N" title, then per room a coloured status line and its people on one
        /// line (bots folded into a count), then who waits in the hub, so ten or more people still fit the card.
        /// </summary>
        public static string Compose(LobbyActivityModel model, long ageMs, bool hadData, Style style = null)
        {
            bool stale = LobbyActivity.IsStale(model, ageMs);
            var sb = new StringBuilder();
            Bold(sb, style, stale ? LobbyActivity.OnlineTitle : LobbyActivity.OnlineTitle + " · " + model.OnlineCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            if (stale)
            {
                Tint(sb, style?.Muted, hadData ? LobbyActivity.StatusStale : LobbyActivity.StatusNever).Append('\n');
                sb.Append(LobbyActivity.Unavailable);
                return sb.ToString();
            }
            if (model.Rooms.Count == 0 && model.Waiting.Count == 0)
            {
                Tint(sb, style?.Muted, LobbyActivity.NobodyOnline);
                return sb.ToString();
            }
            foreach (var room in LobbyActivity.DisplayOrder(model.Rooms))
            {
                string tone = style == null ? null : room.Phase == LobbyPhase.Lobby ? style.Open : room.Phase == LobbyPhase.Finished ? style.Muted : style.Running;
                Tint(sb, tone, LobbyActivity.RoomStatus(room)).Append('\n');
                sb.Append(LobbyActivity.RoomNames(room)).Append('\n');
            }
            if (model.Waiting.Count > 0)
            {
                Tint(sb, style?.Muted, LobbyActivity.HubTitle + " · " + model.Waiting.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
                sb.Append(LobbyActivity.NameList(LobbyActivity.WaitingNames(model), LobbyActivity.MaxNamesShown));
            }
            return sb.ToString().TrimEnd('\n');
        }

        static StringBuilder Tint(StringBuilder sb, string hex, string text)
        {
            return hex == null ? sb.Append(text) : sb.Append("<color=").Append(hex).Append('>').Append(text).Append("</color>");
        }

        static StringBuilder Bold(StringBuilder sb, Style style, string text)
        {
            return style == null ? sb.Append(text) : sb.Append("<b>").Append(text).Append("</b>");
        }
    }
}
