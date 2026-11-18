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
    /// <summary>The tabs of the cosmetics hub page: Shop, Wardrobe and Titles.</summary>
    public enum HubCosmeticsTab { Shop, Wardrobe, Titles }

    /// <summary>Account backed cosmetics pages. Catalog labels are metadata; ownership and every mutation remain server owned.</summary>
    [DisallowMultipleComponent]
    public sealed partial class HubCosmeticsView : MonoBehaviour
    {
        sealed class MutationEntry
        {
            public AccountMutation Operation;
            public AccountApiResult Result;
            public bool Submitting;
        }

        readonly List<AccountCosmeticDefinition> _catalog = new List<AccountCosmeticDefinition>();
        readonly Dictionary<string, AccountCosmeticDefinition> _catalogById = new Dictionary<string, AccountCosmeticDefinition>(StringComparer.Ordinal);
        readonly List<MutationEntry> _mutations = new List<MutationEntry>();
        readonly List<string> _wardrobeSlots = new List<string>();
        readonly HashSet<string> _owned = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> _equipped = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<GameObject> _cards = new List<GameObject>();

        AccountCoordinator _owner;
        RectTransform _root, _panel, _content;
        ScrollRect _scroll;
        GridLayoutGroup _grid;
        Button[] _tabButtons;
        Button _close, _refresh, _slotFilter;
        TMP_Text _heading, _wallet, _slotFilterLabel, _status;
        HubCosmeticsTab _tab;
        AccountApiResult _currentRead;
        string _observedName = "";
        string _titleSlot;
        GameObject _returnFocus;
        long _viewGeneration;
        float _textScale = 1f;
        int _slotFilterIndex;
        bool _initialized, _catalogLoaded, _building;

        internal readonly struct FocusKey
        {
            public readonly string CardName;
            public readonly string ChildPath;
            public FocusKey(string cardName, string childPath) { CardName = cardName; ChildPath = childPath; }
            public bool IsValid => !string.IsNullOrEmpty(CardName);
        }

        public bool IsVisible => _panel != null && _panel.gameObject.activeSelf;
        public RectTransform Root => _panel;
        public HubCosmeticsTab CurrentTab => _tab;
        public int CatalogCount => _catalog.Count;
        public IReadOnlyList<string> WardrobeSlots => _wardrobeSlots.AsReadOnly();

        public void Initialize(Transform root, AccountCoordinator owner)
        {
            if (_initialized) throw new InvalidOperationException("HubCosmeticsView is already initialized.");
            if (root == null) throw new ArgumentNullException(nameof(root));
            _root = root as RectTransform;
            if (_root == null) throw new ArgumentException("The cosmetics root must be a RectTransform.", nameof(root));
            _owner = owner;
            _observedName = owner?.Snapshot?.SelectedName ?? "";
            LoadCatalog();
            Build();
            _initialized = true;
            if (_owner?.Flow != null) _owner.Flow.Changed += OnOwnerChanged;
            _tab = HubCosmeticsTab.Shop;
            UpdateTabs();
            UpdateSlotFilterVisibility();
            _panel.gameObject.SetActive(false);
        }

        public void Show(HubCosmeticsTab tab)
        {
            if (!_initialized && _panel == null) return;
            bool changed = _tab != tab;
            _tab = tab;
            UpdateTabs();
            UpdateSlotFilterVisibility();
            if (!IsVisible) SetVisible(true);
            else if (changed) RebuildCards();
        }

        public void SetVisible(bool visible)
        {
            if (_panel == null || _panel.gameObject.activeSelf == visible) return;
            if (visible)
            {
                _returnFocus = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
                _panel.gameObject.SetActive(true);
                _panel.SetAsLastSibling();
                if (EventSystem.current != null && _tabButtons != null)
                    EventSystem.current.SetSelectedGameObject(_tabButtons[(int)_tab].gameObject);
                RenderProfileGuidance();
            }
            else
            {
                ++_viewGeneration;
                _currentRead = null;
                _panel.gameObject.SetActive(false);
                if (EventSystem.current != null && _returnFocus != null && _returnFocus.activeInHierarchy)
                    EventSystem.current.SetSelectedGameObject(_returnFocus);
            }
        }

        public void RefreshScale(float scale)
        {
            _textScale = Mathf.Clamp(scale, .85f, 1.5f);
            if (_panel != null) MythicUi.ApplyTextScale(_panel, _textScale);
            UpdateGridLayout();
        }

        /// <summary>Docks the panel in the shared hub panel area for the current window (see <see cref="HubPanelDock"/>).</summary>
        public void ApplyViewport(HudViewport viewport) { HubPanelDock.Apply(_panel, viewport); }

        public bool HandleEscape()
        {
            if (!IsVisible) return false;
            SetVisible(false);
            return true;
        }

        void Update()
        {
            if (IsVisible && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) HandleEscape();
        }

        void LoadCatalog()
        {
            TextAsset source = Resources.Load<TextAsset>("Account/cosmetics");
            if (source == null || !AccountFullJson.TryCosmeticCatalog(source.text, out var catalog)) return;
            _catalog.AddRange(catalog);
            _catalogLoaded = _catalog.Count > 0;
            foreach (var item in _catalog)
            {
                _catalogById[item.Id] = item;
                if (_titleSlot == null && item.Text != null) _titleSlot = item.Slot;
            }
            if (_titleSlot != null)
                _wardrobeSlots.AddRange(_catalog.Select(item => item.Slot)
                    .Where(slot => !string.Equals(slot, _titleSlot, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal));
            _wardrobeSlots.Sort(StringComparer.Ordinal);
        }

        void Build()
        {
            _panel = MythicUi.Panel(_root, "HubCosmeticsPanel", new Vector2(.37f, .17f), new Vector2(.98f, .87f), ShowcasePalette.Panel, true);
            _heading = MythicUi.Text(_panel, "CosmeticsHeading", "ACCOUNT COSMETICS", new Vector2(.03f, .91f), new Vector2(.72f, .99f), 27, ShowcasePalette.Gold, heading: true);
            MythicUi.Rule(_panel, "HeadingRule", new Vector2(.035f, .905f), new Vector2(.40f, .908f));
            _wallet = MythicUi.Text(_panel, "Wallet", "PROFILE · refreshing", new Vector2(.035f, .84f), new Vector2(.69f, .91f), 18, ShowcasePalette.Text);
            _close = MythicUi.Button(_panel, "CloseCosmetics", "CLOSE  ×", new Vector2(.84f, .92f), new Vector2(.975f, .99f), () => SetVisible(false), MythicUi.ButtonStyle.Secondary);
            _refresh = MythicUi.Button(_panel, "RefreshProfile", "REFRESH PROFILE", new Vector2(.70f, .84f), new Vector2(.975f, .91f), RefreshProfile, MythicUi.ButtonStyle.Secondary);
            _tabButtons = new Button[3];
            string[] labels = { "SHOP", "WARDROBE", "TITLES" };
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                int index = i;
                float left = .035f + i * .225f;
                _tabButtons[i] = MythicUi.Button(_panel, labels[i] + "Tab", labels[i], new Vector2(left, .755f), new Vector2(left + .21f, .825f), () => Show((HubCosmeticsTab)index), MythicUi.ButtonStyle.Secondary);
            }
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                var navigation = new Navigation { mode = Navigation.Mode.Explicit };
                navigation.selectOnLeft = _tabButtons[(i + _tabButtons.Length - 1) % _tabButtons.Length];
                navigation.selectOnRight = _tabButtons[(i + 1) % _tabButtons.Length];
                navigation.selectOnDown = _refresh;
                _tabButtons[i].navigation = navigation;
            }
            _slotFilter = MythicUi.Button(_panel, "WardrobeSlotFilter", "SLOT · ALL  ›", new Vector2(.035f, .685f), new Vector2(.34f, .745f), CycleSlotFilter);
            _slotFilterLabel = MythicUi.Text(_panel, "WardrobeSlotFilterHelp", "Cycle through catalog slots", new Vector2(.36f, .685f), new Vector2(.96f, .745f), 14, ShowcasePalette.MutedText);
            _scroll = MythicUi.ScrollView(_panel, "CosmeticsScroll", new Vector2(.025f, .09f), new Vector2(.975f, .675f), out _content);
            _grid = _content.GetComponent<GridLayoutGroup>();
            _scroll.scrollSensitivity = 38;
            var readingPane = _scroll.gameObject.AddComponent<KeyboardScrollPane>();
            readingPane.Initialize(_scroll);
            _status = MythicUi.Text(_panel, "CosmeticsStatus", "", new Vector2(.03f, .015f), new Vector2(.97f, .085f), 16, ShowcasePalette.MutedText);
            UpdateGridLayout();
            UpdateTabs();
            UpdateSlotFilterVisibility();
        }

        void RefreshProfile()
        {
            if (!_initialized) return;
            ++_viewGeneration;
            _currentRead = null;
            var snapshot = _owner?.Snapshot;
            var api = _owner?.Api;
            if (api == null || _owner?.Flow == null)
            {
                _status.text = "Account services are unavailable on this page.";
                RebuildCards();
                return;
            }
            if (snapshot == null || snapshot.State != AccountHubState.Owned || snapshot.Profile == null)
            {
                _status.text = "Load an owned profile first, then choose REFRESH PROFILE to view account cosmetics.";
                _wallet.text = "PROFILE · not loaded";
                RebuildCards();
                return;
            }
            _status.text = "Refreshing the saved profile…";
            _wallet.text = "PROFILE · refreshing";
            RebuildCards();
            ReadProfileAsync(api, _viewGeneration);
        }

        async void ReadProfileAsync(AccountApiService api, long generation)
        {
            AccountApiResult result;
            try { result = await api.ReadProfileAsync(); }
            catch
            {
                if (CanApplyView(generation, api))
                {
                    _status.text = "Profile refresh failed. Try again when the account service is available.";
                    RebuildCards();
                }
                return;
            }
            if (!CanApply(generation, api, result)) return;
            _currentRead = result;
            if (result.Failure != AccountApiFailure.None)
            {
                _status.text = AccountFailureText(result.Failure);
                _wallet.text = "PROFILE · unavailable";
            }
            else
            {
                var profile = _owner.Snapshot.FullProfile;
                _status.text = profile == null ? "No complete profile was delivered." : "Choose a cosmetic to save to your profile.";
                _wallet.text = profile == null ? "PROFILE · unavailable" : "GV · " + FormatGv(profile.Gv) + "   ·   LIFETIME " + FormatGv(profile.GvLifetime);
            }
            RebuildCards();
        }

        bool CanApplyView(long generation, AccountApiService api)
        {
            return IsVisible && generation == _viewGeneration && _owner != null && ReferenceEquals(_owner.Api, api);
        }

        bool CanApply(long generation, AccountApiService api, AccountApiResult result)
        {
            return CanApplyView(generation, api) && api.IsCurrent(result);
        }

        void OnOwnerChanged(AccountHubSnapshot snapshot)
        {
            string selected = snapshot?.SelectedName ?? "";
            if (!string.Equals(selected, _observedName, StringComparison.Ordinal))
            {
                _observedName = selected;
                ++_viewGeneration;
                _currentRead = null;
                _status.text = string.IsNullOrEmpty(selected)
                    ? "Open an owned profile, then choose REFRESH PROFILE to see its cosmetics."
                    : "Account changed · choose REFRESH PROFILE after the owned profile is loaded.";
                _wallet.text = "PROFILE · not loaded";
                if (IsVisible) RebuildCards();
                return;
            }
            if (_currentRead != null && (_owner.Api == null || !_owner.Api.IsCurrent(_currentRead))) _currentRead = null;
            if (UsableProfile(snapshot) == null)
            {
                _wallet.text = "PROFILE · not loaded";
                _status.text = "Load an owned profile first, then choose REFRESH PROFILE to view account cosmetics.";
            }
            if (IsVisible) RebuildCards();
        }

        AccountFullProfile CurrentProfile()
        {
            if (!IsVisible || _owner?.Flow == null || _owner.Api == null) return null;
            if (_currentRead != null && !_owner.Api.IsCurrent(_currentRead)) _currentRead = null;
            return UsableProfile(_owner.Snapshot);
        }

        AccountFullProfile UsableProfile(AccountHubSnapshot snapshot)
        {
            if (snapshot == null || snapshot.State != AccountHubState.Owned ||
                !SameOwner(snapshot.SelectedName, _observedName)) return null;
            var profile = snapshot.FullProfile;
            if (profile == null || !SameOwner(profile.Name, snapshot.SelectedName) || snapshot.Profile == null ||
                !SameOwner(snapshot.Profile.Name, profile.Name)) return null;
            return profile;
        }

        void RebuildCards()
        {
            if (_content == null || _building) return;
            _building = true;
            try
            {
                FocusKey focusKey = CaptureCardFocus(_content,
                    EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null);
                if (focusKey.IsValid && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                for (int i = _content.childCount - 1; i >= 0; i--)
                {
                    var oldCard = _content.GetChild(i).gameObject;
                    RetireCard(oldCard); DestroyOwned(oldCard);
                }
                _cards.Clear();
                _owned.Clear(); _equipped.Clear();
                var profile = CurrentProfile();
                if (profile != null)
                {
                    foreach (string id in profile.Cosmetics.Owned) _owned.Add(id);
                    foreach (var pair in profile.Cosmetics.Equipped) _equipped[pair.Key] = pair.Value;
                    _wallet.text = "GV · " + FormatGv(profile.Gv) + "   ·   LIFETIME " + FormatGv(profile.GvLifetime);
                }
                if (!_catalogLoaded)
                {
                    AddNoticeCard("Catalog unavailable", "Cosmetics could not be loaded. Try again shortly.");
                }
                else if (profile == null)
                {
                    AddNoticeCard("Profile required", "Load your callsign to buy or equip cosmetics. You can browse the collection here.");
                    foreach (var item in ItemsForTab()) AddCosmeticCard(item, null, false);
                }
                else
                {
                    var items = ItemsForTab().ToList();
                    foreach (var item in items) AddCosmeticCard(item, profile, true);
                    if (_tab == HubCosmeticsTab.Wardrobe && _slotFilterIndex == 0)
                    {
                        var unknown = profile.Cosmetics.Owned.Where(id => !_catalogById.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToArray();
                        foreach (string id in unknown) AddUnknownOwnedCard(id, profile);
                    }
                    if (_cards.Count == 0) AddNoticeCard("No items in this view", _tab == HubCosmeticsTab.Wardrobe ? "Choose another category." : "No cosmetics match this page yet.");
                    _status.text = MutationSummary(profile) ?? "Choose a cosmetic to buy or equip.";
                }
                UpdateGridLayout();
                Canvas.ForceUpdateCanvases();
                var restoredFocus = ResolveCardFocus(_cards, focusKey);
                if (restoredFocus != null && restoredFocus.gameObject.activeInHierarchy && EventSystem.current != null)
                    EventSystem.current.SetSelectedGameObject(restoredFocus.gameObject);
            }
            finally { _building = false; }
        }

        IEnumerable<AccountCosmeticDefinition> ItemsForTab()
        {
            if (_tab == HubCosmeticsTab.Titles)
                return _titleSlot == null ? Enumerable.Empty<AccountCosmeticDefinition>() : _catalog.Where(item => item.Slot == _titleSlot);
            if (_tab == HubCosmeticsTab.Wardrobe)
            {
                IEnumerable<AccountCosmeticDefinition> wardrobe = _catalog.Where(item => item.Slot != _titleSlot);
                string selected = SelectedWardrobeSlot();
                return selected == null ? wardrobe : wardrobe.Where(item => item.Slot == selected);
            }
            return _catalog;
        }

        string SelectedWardrobeSlot()
        {
            if (_tab != HubCosmeticsTab.Wardrobe || _slotFilterIndex <= 0 || _slotFilterIndex > _wardrobeSlots.Count) return null;
            return _wardrobeSlots[_slotFilterIndex - 1];
        }

        void CycleSlotFilter()
        {
            if (_wardrobeSlots.Count == 0) return;
            _slotFilterIndex = (_slotFilterIndex + 1) % (_wardrobeSlots.Count + 1);
            UpdateSlotFilterVisibility();
            RebuildCards();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_slotFilter.gameObject);
        }

        void UpdateSlotFilterVisibility()
        {
            if (_slotFilter == null) return;
            bool visible = _tab == HubCosmeticsTab.Wardrobe;
            _slotFilter.gameObject.SetActive(visible);
            _slotFilterLabel.gameObject.SetActive(visible);
            string slot = SelectedWardrobeSlot();
            string label = slot == null ? "ALL CATALOG SLOTS" : SlotLabel(slot);
            _slotFilter.GetComponentInChildren<TMP_Text>().text = "SLOT  ·  " + label + "  ›";
            _slotFilterLabel.text = _wardrobeSlots.Count == 0 ? "No wearable categories are available." :
                (slot == null ? "Choose a category to browse." : "Showing · " + SlotLabel(slot));
        }

        void UpdateTabs()
        {
            if (_tabButtons == null) return;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                bool selected = i == (int)_tab;
                MythicUi.Select(_tabButtons[i], selected);
                var text = _tabButtons[i].GetComponentInChildren<TMP_Text>();
                if (text != null) text.color = selected ? ShowcasePalette.Text : ShowcasePalette.MutedText;
            }
        }

        void RenderProfileGuidance()
        {
            if (_owner?.Api == null || _owner.Flow == null)
            {
                _status.text = "Account services are unavailable on this page.";
                _wallet.text = "PROFILE · unavailable";
            }
            else if (_owner.Snapshot?.State == AccountHubState.Owned && _owner.Snapshot.Profile != null)
            {
                _status.text = "Choose REFRESH PROFILE to load the saved cosmetics and wallet.";
                _wallet.text = "PROFILE · ready to refresh";
            }
            else
            {
                _status.text = "Load an owned profile first, then choose REFRESH PROFILE to view account cosmetics.";
                _wallet.text = "PROFILE · not loaded";
            }
            RebuildCards();
        }

        void UpdateGridLayout()
        {
            if (_grid == null) return;
            float available = _panel != null && _panel.rect.width > 0 ? _panel.rect.width - 42 : Screen.width * .61f - 42;
            if (available < 320) available = 560;
            int columns = available >= 940 && _textScale <= 1.15f ? 2 : 1;
            float gap = 12;
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = columns;
            _grid.cellSize = new Vector2(Mathf.Max(260, (available - gap * (columns - 1) - 16) / columns), (columns == 2 ? 246 : 220) * _textScale);
            _grid.spacing = new Vector2(gap, gap);
            _grid.padding = new RectOffset(8, 8, 8, 12);
        }

        void DestroyOwned(GameObject go)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }

        static bool SameOwner(string left, string right)
        {
            try { return AccountEndpoint.NormalizeName(left) == AccountEndpoint.NormalizeName(right); }
            catch (ArgumentException) { return false; }
        }

        static Color32 ToColor(uint value) => new Color32((byte)(value >> 16), (byte)(value >> 8), (byte)value, 255);
        static string FormatGv(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
        static string SlotLabel(string slot) => string.IsNullOrEmpty(slot) ? "UNKNOWN SLOT" : slot.Replace('-', ' ').Replace('_', ' ').ToUpperInvariant();
        static string SafeName(string value) => string.IsNullOrEmpty(value) ? "Empty" : value.Replace('/', '_').Replace(' ', '_');

        void OnDestroy()
        {
            if (_owner?.Flow != null) _owner.Flow.Changed -= OnOwnerChanged;
            ++_viewGeneration;
        }
    }
}
