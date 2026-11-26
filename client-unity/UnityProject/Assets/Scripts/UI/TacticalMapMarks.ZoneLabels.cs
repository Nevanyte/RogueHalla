using System;
using System.Collections.Generic;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;
using MapRect = Roguehalla.Net.Ui.MapRect;

namespace Roguehalla.Client
{
    /// <summary>
    /// The zone names of the Tab map and where each stands clear of the marks (P-UI-ZONE-NAMES-1): one of six spots around the zone icon,
    /// chosen by <see cref="MapLabelPlacement"/> from the boxes of the camp, ward, chest and delivered marks, never per frame.
    /// </summary>
    public sealed partial class TacticalMapMarks
    {
        /// <summary>Why a placement pass is asked for: a forced one (map or layer change), a layer rect event, or a change of the presented marks.</summary>
        enum ZonePass { Forced, Rect, Present }

        readonly struct MeasureKey : IEquatable<MeasureKey>
        {
            readonly string _text; readonly float _size;
            public MeasureKey(string text, float size) { _text = text; _size = size; }
            public bool Equals(MeasureKey o) { return _size == o._size && string.Equals(_text, o._text, StringComparison.Ordinal); }
            public override bool Equals(object obj) { return obj is MeasureKey k && Equals(k); }
            public override int GetHashCode() { return (_text != null ? _text.GetHashCode() : 0) * 31 + _size.GetHashCode(); }
        }

        readonly List<TMP_Text> _zoneLabels = new List<TMP_Text>();
        RectTransform _zoneLabelHost;

        readonly int[] _zoneStored = NewSpots(), _zoneNow = NewSpots();
        readonly Vector2[] _zoneOff = new Vector2[MapZones.Count + 1], _zoneBox = new Vector2[MapZones.Count + 1];
        readonly float[] _zoneNameSizes = new float[2 * (MapZones.Count + 1)], _zoneNameSizesStored = new float[2 * (MapZones.Count + 1)];
        readonly float[] _zoneNameFont = new float[MapZones.Count + 1];
        readonly Vector2[] _zoneNameMeasured = new Vector2[MapZones.Count + 1];
        readonly bool[] _zoneNameTaken = new bool[MapZones.Count + 1];   // the kept size was taken from a label active in the hierarchy
        readonly List<MapRect> _zoneSpots = new List<MapRect>(MapLabelPlacement.Spots), _zoneMarks = new List<MapRect>(64), _zoneMarksStored = new List<MapRect>(64);
        readonly Dictionary<MeasureKey, Vector2> _measured = new Dictionary<MeasureKey, Vector2>();
        float _lastRectW = float.NaN, _lastRectH = float.NaN;

        static int[] NewSpots() { var a = new int[MapZones.Count + 1]; for (int i = 0; i < a.Length; i++) a[i] = -1; return a; }

        /// <summary>How many placement passes have run.</summary>
        public int ZoneLabelPlacements { get; private set; }

        /// <summary>The spot the name of <paramref name="region"/> is on now; -1: no name.</summary>
        public int ZoneLabelSpot(int region) { return region >= 1 && region <= MapZones.Count ? _zoneNow[region] : -1; }

        /// <summary>The rect of that spot in layer units; null: no name.</summary>
        public Rect? ZoneLabelRect(int region)
        {
            if (ZoneLabelSpot(region) < 0) return null;
            var centre = ZoneAt(region) + _zoneOff[region]; var box = _zoneBox[region];
            return new Rect(centre.x - box.x * .5f, centre.y - box.y * .5f, box.x, box.y);
        }

        /// <summary>The dark plate behind icon and name in layer units; null: no name.</summary>
        public Rect? ZonePlateRect(int region)
        {
            if (ZoneLabelSpot(region) < 0) return null;
            Vector2 centre, size; ZonePlate(region, out centre, out size);
            return new Rect(centre.x - size.x * .5f, centre.y - size.y * .5f, size.x, size.y);
        }

        /// <summary>The plate between the icon and the name: today's 104 x 56 under the icon for spot 0.</summary>
        void ZonePlate(int region, out Vector2 centre, out Vector2 size)
        {
            var at = ZoneAt(region);
            if (_zoneNow[region] < 0) { centre = at + new Vector2(0f, -12f); size = new Vector2(104f, 56f); return; }
            var d = _zoneOff[region];
            centre = at + d * .5f; size = new Vector2(104f + Mathf.Abs(d.x), 32f + Mathf.Abs(d.y));
        }

        /// <summary>The zone host (a sibling of this object) takes this object's transform so the labels sit in the layer's units.</summary>
        void FollowLayer()
        {
            if (_zoneLabelHost == null || transform.parent == null || _zoneLabelHost.parent != transform.parent) return;
            var me = rectTransform;
            _zoneLabelHost.anchorMin = me.anchorMin; _zoneLabelHost.anchorMax = me.anchorMax;
            _zoneLabelHost.offsetMin = me.offsetMin; _zoneLabelHost.offsetMax = me.offsetMax;
            _zoneLabelHost.pivot = me.pivot; _zoneLabelHost.localScale = me.localScale;
        }

        Vector2 ZoneAt(int region) { return At(new Vector2((float)_zoneX[region] * 32f, (float)_zoneY[region] * 32f)); }

        /// <summary>The zone labels live in their own full-size container, a later sibling of this mesh (and of the fog), so nothing the mesh draws covers them.</summary>
        RectTransform ZoneLabelHost()
        {
            if (_zoneLabelHost != null) return _zoneLabelHost;
            var host = new GameObject("Zone labels", typeof(RectTransform));
            var parent = transform.parent != null ? transform.parent : transform;
            host.transform.SetParent(parent, false);
            _zoneLabelHost = (RectTransform)host.transform;
            _zoneLabelHost.anchorMin = Vector2.zero; _zoneLabelHost.anchorMax = Vector2.one; _zoneLabelHost.offsetMin = _zoneLabelHost.offsetMax = Vector2.zero;
            FollowLayer();
            return _zoneLabelHost;
        }

        void RefreshZoneLabels(ZonePass kind)
        {
            FollowLayer();
            bool show = Large && _map != null;
            if (show && _zoneLabelHost != null) _zoneLabelHost.SetAsLastSibling();   // later siblings may have been added since it was created
            for (int i = 0; i < MapZones.Count; i++)
            {
                int region = i + 1;
                bool on = show && _zoneFound[region];
                if (i >= _zoneLabels.Count)
                {
                    if (!on) { _zoneNow[region] = -1; continue; }
                    var label = HudUi.Label(ZoneLabelHost(), "ZoneLabel" + region, MapZones.Name(region).ToUpperInvariant(), 10f, UiTheme.Current.TextPrimary, TextAlignmentOptions.Center, false, UiFontRole.Display);
                    var rect = label.rectTransform; rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(120f, 18f);
                    label.fontSharedMaterial = OverheadIndicators.OutlinedMaterial(label.font);   // dark outline: reads over the cream self ring
                    _zoneLabels.Add(label);
                }
                var text = _zoneLabels[i];
                if (text.gameObject.activeSelf != on) text.gameObject.SetActive(on);
                if (!on) _zoneNow[region] = -1;
            }
            if (!show) return;
            var layer = rectTransform.rect;
            if (kind == ZonePass.Rect && layer.width == _lastRectW && layer.height == _lastRectH) return;
            PlaceZoneNames(kind, layer);
        }

        /// <summary>One placement pass: rebuild the mark boxes, choose a spot per name, move the labels. Forced and present passes store the result.</summary>
        void PlaceZoneNames(ZonePass kind, Rect layer)
        {
            BuildZoneMarks(_zoneMarks);
            _lastRectW = layer.width; _lastRectH = layer.height;
            bool store = kind != ZonePass.Rect, changed = false;
            for (int region = 1; region <= MapZones.Count; region++)
            {
                int i = region - 1;
                if (i >= _zoneLabels.Count || !_zoneLabels[i].gameObject.activeSelf) { if (_zoneNow[region] != -1) { _zoneNow[region] = -1; changed = true; } continue; }
                float w = _zoneNameSizes[2 * region], h = _zoneNameSizes[2 * region + 1];
                var icon = ZoneAt(region);
                _zoneSpots.Clear();
                for (int k = 0; k < MapLabelPlacement.Spots; k++) _zoneSpots.Add(MapLabelPlacement.Spot(k, icon.x, icon.y, w, h));
                int pick = MapLabelPlacement.Choose(_zoneSpots, _zoneMarks, _zoneStored[region]);
                Vector2 off, box = new Vector2(w, h);
                if (pick < 0) { pick = 0; off = new Vector2(0f, -MapLabelPlacement.Drop); }
                else { var r = _zoneSpots[pick]; off = new Vector2(r.X + r.W * .5f - icon.x, r.Y + r.H * .5f - icon.y); }
                if (store) _zoneStored[region] = pick;
                if (_zoneNow[region] != pick || _zoneOff[region] != off || _zoneBox[region] != box) changed = true;
                _zoneNow[region] = pick; _zoneOff[region] = off; _zoneBox[region] = box;
                var at = icon + off;
                var rt = _zoneLabels[i].rectTransform;
                if (rt.anchoredPosition != at) rt.anchoredPosition = at;
            }
            if (store)
            {
                _zoneMarksStored.Clear();
                for (int i = 0; i < _zoneMarks.Count; i++) _zoneMarksStored.Add(_zoneMarks[i]);
                for (int i = 0; i < _zoneNameSizes.Length; i++) _zoneNameSizesStored[i] = _zoneNameSizes[i];
            }
            ZoneLabelPlacements++;
            if (changed) SetVerticesDirty();
        }

        /// <summary>After <see cref="Present"/> rebuilt its lists: a pass only if the marks (or the names' sizes) differ from the last storing pass's.</summary>
        void PlaceZoneNamesIfMarksChanged()
        {
            if (!Large || _map == null || _zoneLabels.Count == 0) return;
            BuildZoneMarks(_zoneMarks);
            bool same = _zoneMarks.Count == _zoneMarksStored.Count;
            for (int i = 0; same && i < _zoneMarks.Count; i++)
            {
                var a = _zoneMarks[i]; var b = _zoneMarksStored[i];
                same = a.X == b.X && a.Y == b.Y && a.W == b.W && a.H == b.H;
            }
            for (int i = 0; same && i < _zoneNameSizes.Length; i++) same = _zoneNameSizes[i] == _zoneNameSizesStored[i];
            if (!same) RefreshZoneLabels(ZonePass.Present);
        }

        void ResetZoneSpots() { for (int r = 0; r < _zoneStored.Length; r++) _zoneStored[r] = -1; }

        // ---- the marks a name must clear (packet 1.2), in layer units ----------------------------------------------------------------

        void BuildZoneMarks(List<MapRect> into)
        {
            into.Clear();
            for (int region = 1; region <= MapZones.Count; region++)
            {
                int i = region - 1; float w = 0f, h = 0f;
                if (i < _zoneLabels.Count && _zoneLabels[i].gameObject.activeSelf)
                {
                    var label = _zoneLabels[i]; float font = label.fontSize;
                    if (label.gameObject.activeInHierarchy)
                    {
                        if (!_zoneNameTaken[region] || _zoneNameFont[region] != font)
                        {
                            // Labels created while the map was closed can only fit their final box once active.
                            if (!_zoneNameTaken[region]) label.GetComponent<MythicTextScale>().Refit();
                            _zoneNameMeasured[region] = label.GetPreferredValues(label.text, 0f, 0f);
                            _zoneNameFont[region] = label.fontSize;
                            _zoneNameTaken[region] = true;
                        }
                    }
                    else if (_zoneNameFont[region] != font) _zoneNameTaken[region] = false;
                    if (_zoneNameTaken[region]) { w = _zoneNameMeasured[region].x; h = _zoneNameMeasured[region].y; }
                }
                _zoneNameSizes[2 * region] = w; _zoneNameSizes[2 * region + 1] = h;
            }
            if (_map == null) return;
            const float s = 1.2f;
            var amountLabel = _badgeTexts.Count > 0 ? _badgeTexts[0] : null;
            var clockLabel = _campClocks.Count > 0 ? _campClocks[0] : null;
            var chestLabel = _chestTexts.Count > 0 ? _chestTexts[0] : null;
            for (int i = 0; i < _camps.Count; i++)
            {
                var camp = _camps[i];
                var p = At(new Vector2((float)camp.WorldX, (float)camp.WorldY));
                float box = camp.IconKey != null ? 17f * s : 6f * s + 4f;
                MapLabelPlacement.AddBox(into, p.x, p.y, box, box);
                if (camp.Elite) MapLabelPlacement.AddBox(into, p.x, p.y + 13.5f, 6f * s + 4f, 6f * s + 4f);
                if (i < _ctCount && _ctPlaces[i] != CampTextPlacement.None && _ctRects[i].W > 0f) into.Add(_ctRects[i]);
                if (clockLabel != null && camp.Clock != null && !ChestBadges.Replaces(_chestRows, camp.Index)) AddMeasured(into, clockLabel, camp.Clock, p.x, p.y - 16f, 0);
            }
            if (Large)
                for (int i = 0; i < _wards.Count; i++)
                {
                    var ward = _wards[i];
                    var p = At(new Vector2((float)ward.WorldX, (float)ward.WorldY));
                    MapLabelPlacement.AddBox(into, p.x - 13.2f, p.y + 2f, 8f * s + 4f, 8f * s + 4f);
                    if (amountLabel != null && ward.Countdown != null) AddMeasured(into, amountLabel, ward.Countdown, p.x - 21.2f, p.y + 2f, 1);
                }
            for (int i = 0; i < _chests.Count; i++)
            {
                var p = At(_chests[i]);
                MapLabelPlacement.AddBox(into, p.x, p.y, 11f * s + 4f, 11f * s + 4f);
            }
            for (int i = 0; i < _chestRows.Count; i++)
            {
                var row = _chestRows[i];
                var at = ChestTextAt(At(new Vector2((float)row.WorldX, (float)row.WorldY)));
                MapLabelPlacement.AddBox(into, at.x, at.y, (row.Text.Length * 6f + 8f) * s, 12f * s);
                if (chestLabel != null) AddMeasured(into, chestLabel, row.Text, at.x, at.y, 0);
            }
            var layer = rectTransform.rect;
            for (int i = 0; i < _delivered.Count; i++)
            {
                var p = At(_delivered[i].At);
                if (!layer.Contains(p)) continue;
                float box = DeliveredSize(_delivered[i].Kind) * s + 4f;
                MapLabelPlacement.AddBox(into, p.x, p.y, box, box);
            }
            MapLabelPlacement.AddEdges(into, layer.xMin, layer.yMin, layer.width, layer.height);
        }

        void AddMeasured(List<MapRect> into, TMP_Text label, string text, float x, float cy, int align)
        {
            var size = Measure(label, text);
            MapLabelPlacement.AddText(into, x, cy, size.x, size.y, align);
        }

        /// <summary>The box of a 9-unit text from its pool's first label, cached by (text, font size) so a steady pass measures nothing.</summary>
        Vector2 Measure(TMP_Text label, string text)
        {
            var key = new MeasureKey(text, label.fontSize);
            Vector2 size;
            if (_measured.TryGetValue(key, out size)) return size;
            if (!label.gameObject.activeInHierarchy) return Vector2.zero;   // not measured: nothing stored
            if (_measured.Count > 256) _measured.Clear();
            size = label.GetPreferredValues(text, 0f, 0f);
            _measured[key] = size;
            return size;
        }

        /// <summary>The marker sizes of <c>OnPopulateMesh</c> for a delivered point, before the 1.2 scale and the outline.</summary>
        static float DeliveredSize(PointKind kind)
        {
            switch (kind)
            {
                case PointKind.Objective: return 11f;
                case PointKind.TrackedObjective: return 13f;
                case PointKind.PublicObjective: return 14f;
                case PointKind.QuestReward: return 9f;
                case PointKind.RebirthEgg: return 10f;
                case PointKind.HatchingEgg: return 14f;
                case PointKind.Crown: return 16f;
                case PointKind.Smuggler: return 14f;
                case PointKind.Fort: return 16f;
                case PointKind.Meditation: return 15f;
                case PointKind.Bounty: return 16f;
                case PointKind.Giver: return 13f;
                default: return 13f;   // Merchant
            }
        }
    }
}
