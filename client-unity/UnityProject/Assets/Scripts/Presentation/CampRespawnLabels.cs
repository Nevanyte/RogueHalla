using System;
using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;
using TMPro;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>
    /// "RESPAWN Ns" / "RESPAWN READY" in the world below a cleared camp (legacy CampWards.ts:19-30: 11 px monospace #e5edf7 with a
    /// dark stroke, centred 32 units below the camp). Text comes from the pure <see cref="CampMarks"/> model; labels are pooled and the
    /// text is only rewritten when it changes, so a steady frame allocates nothing. Presentation only.
    /// </summary>
    public sealed class CampRespawnLabels
    {
        const int Order = 29000;
        const float OffsetUnits = 32f;
        readonly Transform _root;
        readonly List<CampWorldLabel> _rows = new List<CampWorldLabel>();
        readonly List<TextMeshPro> _labels = new List<TextMeshPro>();
        public int Used { get; private set; }
        public string TextAt(int i) { return i >= 0 && i < Used ? _labels[i].text : null; }

        // Reward badges and ward beacons share the file (legacy CampBuffs.ts, CampWards.ts); pooled, no per-frame allocation.
        const float BadgeOffsetUnits = -44f, BeaconTextOffsetUnits = -48f, BadgeScale = .55f, BadgeStep = .62f, PlaqueHeight = .78f;
        readonly List<CampBadgeRow> _badges = new List<CampBadgeRow>();
        readonly List<CampWardRow> _wards = new List<CampWardRow>();
        readonly List<SpriteRenderer> _badgeIcons = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> _plaques = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> _beacons = new List<SpriteRenderer>();
        readonly List<TextMeshPro> _beaconTexts = new List<TextMeshPro>();
        public int BadgesUsed { get; private set; }
        public int BeaconsUsed { get; private set; }
        public string BeaconTextAt(int i) { return i >= 0 && i < BeaconsUsed ? _beaconTexts[i].text : null; }

        static readonly MythicGlyph[] StatGlyphs = { MythicGlyph.Sword, MythicGlyph.Wish, MythicGlyph.Axe, MythicGlyph.Bow, MythicGlyph.Potion, MythicGlyph.Health, MythicGlyph.Armor, MythicGlyph.Shield, MythicGlyph.Speed };
        static readonly string[] StatKeys = { "ad", "ap", "damage", "attackSpeed", "leech", "health", "armor", "mr", "speed" };

        /// <summary>The GAINED strip's glyph for a legacy combat-icon key (same table as MatchHud.Legacy.cs:130 <c>CellGlyph</c>, in
        /// <see cref="CampBadges.IconKey"/> order); an unknown key reads as damage.</summary>
        public static MythicGlyph StatGlyph(string iconKey)
        {
            for (int i = 0; i < StatKeys.Length; i++) if (StatKeys[i] == iconKey) return StatGlyphs[i];
            return MythicGlyph.Axe;
        }

        /// <summary>The sprite for a badge icon key: the stat glyph, or the crate glyph for the elite augment chest (no stat glyph exists).</summary>
        public static Sprite Art(string iconKey) { return iconKey == "chest" ? IndicatorArt.Get("crate") : MythicUi.GlyphSprite(StatGlyph(iconKey)); }

        static Sprite _plaque, _beacon;

        /// <summary>Dark rounded plaque with a light edge (CampBuffs.ts draw: 0x101820 fill, 0xd7e6f3 line, radius 6), 9-sliced.</summary>
        static Sprite Plaque()
        {
            if (_plaque != null) return _plaque;
            const int W = 48, H = 32; const float R = 10f;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = Mathf.Max(R - (x + .5f), (x + .5f) - (W - R), 0f), dy = Mathf.Max(R - (y + .5f), (y + .5f) - (H - R), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy) - R;
                    float outer = Mathf.Clamp01(.5f - d), edge = Mathf.Clamp01(1.5f + d);
                    byte a = (byte)Mathf.RoundToInt(255f * outer * .96f);
                    px[y * W + x] = Color32.Lerp(new Color32(0x10, 0x18, 0x20, a), new Color32(0xd7, 0xe6, 0xf3, a), edge);
                }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "Camp badge plaque", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply(false, true);
            return _plaque = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(.5f, .5f), H, 0, SpriteMeshType.FullRect, new Vector4(12, 12, 12, 12));
        }

        /// <summary>Pole-and-flag beacon over a ground ellipse (CampWards.ts:46-49), white so the look colour tints it. Pivot at the ellipse centre.</summary>
        static Sprite Beacon()
        {
            if (_beacon != null) return _beacon;
            const int W = 48, H = 56;
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 3; sy++) for (int sx = 0; sx < 3; sx++) if (BeaconCovers(x + (sx + .5f) / 3f, y + (sy + .5f) / 3f)) hit++;
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(255 * hit / 9));
                }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = "Camp ward beacon", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px); tex.Apply(false, true);
            return _beacon = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(24f / W, 10f / H), 32f);
        }

        static bool BeaconCovers(float x, float y)
        {
            float ex = (x - 24f) / 22f, ey = (y - 10f) / 9f, e = Mathf.Sqrt(ex * ex + ey * ey);
            if (e < 1f && e > .78f) return true;
            if (x > 23f && x < 25f && y >= 10f && y <= 29f) return true;
            if (y >= 30f && y <= 42f) return Mathf.Abs(x - 24f) <= 7f * (42f - y) / 12f;
            return false;
        }

        Func<float, float, bool> _visible;
        readonly Func<double, double, bool> _canSee;

        public CampRespawnLabels(Transform root) { _root = root; _canSee = (x, y) => _visible == null || _visible((float)x, (float)y); }

        /// <summary>Redraws from the session's camp rows and map; hides every label when there is no map, no rows or the match is not active.</summary>
        public void Update(Session session) { Update(session, null); }

        /// <summary>As above, and the world reward badges (icon only, over living camps the viewer can see through <paramref name="visible"/>) and ward beacons.</summary>
        public void Update(Session session, Func<float, float, bool> visible)
        {
            _badges.Clear(); _wards.Clear(); _rows.Clear();
            if (session != null && session.Map != null && session.State == SessionState.InMatch)
            {
                bool active = !session.Training;
                _visible = visible;
                CampBadges.Rewards(session.Map.Camps, session.Collections.Camps, active, CampSurface.World, _canSee, _badges);
                CampBadges.Wards(session.Map.Camps, session.Collections.Camps, session.ServerTimeMs, active, _wards);
                CampMarks.WorldLabels(session.Map.Camps, session.Collections.Camps, session.ServerTimeMs, active, _rows);
            }
            DrawFrame(Time.unscaledDeltaTime, false);
        }

        /// <summary>
        /// P-UNITY-NAME-GROUP-2 seam: world-space rects of the fighter names drawn this frame (Unity world units, y up). The respawn and ward
        /// labels must not intersect any of them. ArenaView sets it to the presenter's name rects (one line there, owned by another packet).
        /// </summary>
        public System.Action<List<Rect>> NameRectSource;
        /// <summary>Heads and health bars the camp reward plaque should yield to.</summary>
        public System.Action<List<Rect>> HeaderRectSource;

        /// <summary>
        /// P-UNITY-NAME-GROUP-2 seam for tests: draws the respawn labels and the ward beacons with their labels from ready rows, without a
        /// Session, as one fresh frame. <see cref="Update(Session, Func{float, float, bool})"/> does the same from the session's rows.
        /// </summary>
        public void Draw(List<CampWorldLabel> rows, List<CampWardRow> wards) { Draw(rows, wards, new List<CampBadgeRow>()); }

        /// <summary>
        /// As above, and the world reward badges (plaque and icons) from ready rows (P-UNITY-NAME-LEGIBLE-1 seam: the badge of an elite's camp must not
        /// cover the elite's name). One fresh frame: labels at their wanted lift at once, badges fully drawn, no state carried.
        /// </summary>
        public void Draw(List<CampWorldLabel> rows, List<CampWardRow> wards, List<CampBadgeRow> badges)
        {
            Copy(rows, wards, badges);
            ResetState();
            DrawFrame(0f, true);
        }

        /// <summary>Test seam: whether reduced motion is on; unset, it reads <see cref="ShowcaseSettings.Current"/>.</summary>
        public Func<bool> ReducedMotionSource;

        /// <summary>Draws one frame of <paramref name="dt"/> seconds with the lift, linger and fade state.</summary>
        public void Draw(List<CampWorldLabel> rows, List<CampWardRow> wards, List<CampBadgeRow> badges, float dt)
        {
            Copy(rows, wards, badges);
            DrawFrame(dt, false);
        }

        void Copy(List<CampWorldLabel> rows, List<CampWardRow> wards, List<CampBadgeRow> badges)
        {
            _rows.Clear(); _rows.AddRange(rows);
            _wards.Clear(); _wards.AddRange(wards);
            _badges.Clear(); _badges.AddRange(badges);
        }

        // ---- Stable labels (P-UI-CAMP-LABEL-STABLE-1): a lift with memory per camp and kind, a linger and a fade for badges. ----
        const float LingerSeconds = .2f, FadeSeconds = .15f, ForgetSeconds = 1f, WardGap = .08f;

        struct LiftState { public int Index, Kind; public LabelLift Lift; public float Unseen; public bool Seen; }
        struct BadgeState { public int Index; public CampBadgeRow Row; public LabelLift Lift; public float Alpha, Gone, Top; public bool Seen, Drawn; }
        readonly List<LiftState> _lifts = new List<LiftState>();
        readonly List<BadgeState> _badgeStates = new List<BadgeState>();
        bool _ran, _wasHidden;

        void ResetState() { _lifts.Clear(); _badgeStates.Clear(); }

        bool ReducedMotion()
        {
            if (ReducedMotionSource != null) return ReducedMotionSource();
            return ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion;
        }

        void DrawFrame(float dt, bool forceFresh)
        {
            bool hidden = WorldLabelVisibility.Hidden;
            bool fresh = forceFresh || !_ran || _wasHidden || dt > ForgetSeconds;
            _ran = true; _wasHidden = hidden;
            if (fresh || hidden) ResetState();
            if (dt < 0f || fresh) dt = 0f;
            bool reduced = ReducedMotion();
            for (int i = 0; i < _lifts.Count; i++) { var s = _lifts[i]; s.Seen = false; _lifts[i] = s; }
            FetchNames();
            DrawBadges(dt, fresh, reduced); DrawBeacons(dt, reduced); DrawRows(dt, reduced);
            for (int i = _lifts.Count - 1; i >= 0; i--)
            {
                var s = _lifts[i];
                if (s.Seen) continue;
                s.Unseen += dt;
                if (s.Unseen > ForgetSeconds) _lifts.RemoveAt(i); else _lifts[i] = s;
            }
        }

        // The state of one camp's label of one kind; a camp not drawn for a while starts again at its wanted lift.
        float TextLift(int index, int kind, float wanted, float dt, bool reduced)
        {
            for (int i = 0; i < _lifts.Count; i++)
            {
                var s = _lifts[i];
                if (s.Seen || s.Index != index || s.Kind != kind) continue;
                s.Lift = s.Lift.Step(wanted, dt, reduced); s.Seen = true; s.Unseen = 0f; _lifts[i] = s;
                return s.Lift.Value;
            }
            var n = new LiftState { Index = index, Kind = kind, Lift = LabelLift.Start(wanted), Seen = true };
            _lifts.Add(n);
            return n.Lift.Value;
        }

        readonly List<Rect> _names = new List<Rect>();
        readonly List<Rect> _badgeObstacles = new List<Rect>();
        readonly Dictionary<TextMeshPro, SpriteRenderer> _plates = new Dictionary<TextMeshPro, SpriteRenderer>();
        void FetchNames() { _names.Clear(); if (NameRectSource != null) NameRectSource(_names); }

        static Sprite _plate;
        static Sprite PlateSprite()
        {
            if (_plate != null) return _plate;
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Camp label plate", filterMode = FilterMode.Point };
            var px = new Color32[16]; for (int i = 0; i < 16; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply(false, true);
            return _plate = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 4f);
        }

        // The label's dark backing plate, a child of the label so it moves with it; sized to the text when the text changes.
        static void FitPlate(TextMeshPro label, Dictionary<TextMeshPro, SpriteRenderer> plates)
        {
            label.ForceMeshUpdate();
            plates.TryGetValue(label, out var plate);
            if (plate == null)
            {
                var go = new GameObject("Plate"); go.transform.SetParent(label.transform, false);
                plate = go.AddComponent<SpriteRenderer>(); plate.sprite = PlateSprite();
                plate.color = new Color(0x10 / 255f, 0x18 / 255f, 0x20 / 255f, .88f);
                plate.sortingOrder = Order - 1;
                plates[label] = plate;
            }
            var b = label.textBounds;
            plate.transform.localPosition = b.center;
            plate.transform.localScale = new Vector3(b.size.x + .3f, b.size.y + .16f, 1f);
        }

        // How far up the label must go from its base to clear every fighter name (one line at a time, as before), measured with the label
        // at its base. baseYMin is the plate's lower edge there.
        float WantedClear(TextMeshPro label, out float baseYMin)
        {
            _plates.TryGetValue(label, out var plate);
            baseYMin = 0f;
            if (plate == null) return 0f;
            var pb = plate.bounds; var rect = new Rect(pb.min.x, pb.min.y, pb.size.x, pb.size.y);
            baseYMin = rect.yMin;
            float total = 0f;
            for (int pass = 0; pass < 8 && _names.Count > 0; pass++)
            {
                float lift = 0f;
                for (int i = 0; i < _names.Count; i++)
                    if (rect.Overlaps(_names[i])) lift = Mathf.Max(lift, _names[i].yMax - rect.yMin + .04f);
                if (lift <= 0f) break;
                total += lift; rect.y += lift;
            }
            return total;
        }

        void DrawRows(float dt, bool reduced)
        {
            Used = WorldLabelVisibility.Hidden ? 0 : _rows.Count;   // the Tab map is open: no world label behind its panels
            for (int i = 0; i < Used; i++)
            {
                var label = Get(i);
                var row = _rows[i];
                bool changed = label.text != row.Text;
                if (changed) label.text = row.Text;
                var home = ArenaView.WorldPoint((float)row.WorldX, (float)row.WorldY + OffsetUnits);
                label.transform.position = home;
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
                if (changed || !_plates.ContainsKey(label)) FitPlate(label, _plates);
                float lift = TextLift(row.Index, 0, WantedClear(label, out _), dt, reduced);
                if (lift > 0f) label.transform.position = home + new Vector3(0f, lift, 0f);
            }
            for (int i = Used; i < _labels.Count; i++) if (_labels[i].gameObject.activeSelf) _labels[i].gameObject.SetActive(false);
        }

        static Color32 Rgb(uint rgb) { return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255); }

        SpriteRenderer Icon(List<SpriteRenderer> pool, int i, string name, int order)
        {
            while (pool.Count <= i)
            {
                var go = new GameObject(name); go.transform.SetParent(_root, false);
                var r = go.AddComponent<SpriteRenderer>(); r.sortingOrder = order; go.SetActive(false);
                pool.Add(r);
            }
            return pool[i];
        }

        void Place(SpriteRenderer r, Sprite sprite, Color32 color, Vector3 at, float scale)
        {
            if (r.sprite != sprite) r.sprite = sprite;
            r.color = color; r.transform.position = at; r.transform.localScale = Vector3.one * scale;
            if (!r.gameObject.activeSelf) r.gameObject.SetActive(true);
        }

        static Color32 Rgba(uint rgb, byte alpha) { return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, alpha); }

        float WantedBadge(CampBadgeRow b, int n)
        {
            var c = ArenaView.WorldPoint((float)b.WorldX, (float)b.WorldY + BadgeOffsetUnits);
            float w = n * BadgeStep + .3f;
            var rect = new Rect(c.x - w * .5f, c.y - PlaqueHeight * .5f, w, PlaqueHeight);
            return CombatHudPlacement.Lift(rect, _badgeObstacles).y - rect.y;
        }

        static int IconCount(CampBadgeRow b) { return (b.IconKey != null ? 1 : 0) + (b.EliteIconKey != null ? 1 : 0); }

        // The camp position is the public anchor; keep the local shift with the plaque and its icons so the reward continues to identify
        // the camp. Each camp's badge has a state (lift, alpha, the last row) so it glides, lingers and fades instead of hopping and blinking.
        void DrawBadges(float dt, bool fresh, bool reduced)
        {
            int used = 0, plaques = 0;
            if (WorldLabelVisibility.Hidden)
            {
                BadgesUsed = 0;
                for (int i = 0; i < _badgeIcons.Count; i++) if (_badgeIcons[i].gameObject.activeSelf) _badgeIcons[i].gameObject.SetActive(false);
                for (int i = 0; i < _plaques.Count; i++) if (_plaques[i].gameObject.activeSelf) _plaques[i].gameObject.SetActive(false);
                return;
            }
            for (int i = 0; i < _badgeStates.Count; i++) { var s = _badgeStates[i]; s.Seen = false; s.Drawn = false; _badgeStates[i] = s; }
            _badgeObstacles.Clear();
            if (_badges.Count > 0 || _badgeStates.Count > 0)
            {
                _badgeObstacles.AddRange(_names);
                HeaderRectSource?.Invoke(_badgeObstacles);
            }
            float step = dt / FadeSeconds;
            for (int i = 0; i < _badges.Count; i++)
            {
                var b = _badges[i];
                int n = IconCount(b);
                if (n == 0) continue;
                float wanted = WantedBadge(b, n);
                int at = -1;
                for (int j = 0; j < _badgeStates.Count; j++) if (!_badgeStates[j].Seen && _badgeStates[j].Index == b.Index) { at = j; break; }
                if (at >= 0)
                {
                    var s = _badgeStates[at];
                    s.Row = b; s.Seen = true; s.Gone = 0f;
                    s.Lift = s.Lift.Step(wanted, dt, reduced);
                    s.Alpha = reduced ? 1f : Mathf.Min(1f, s.Alpha + step);
                    _badgeStates[at] = s;
                }
                else
                    _badgeStates.Add(new BadgeState { Index = b.Index, Row = b, Lift = LabelLift.Start(wanted), Seen = true, Alpha = fresh || reduced ? 1f : Mathf.Min(1f, step) });
            }
            for (int i = _badgeStates.Count - 1; i >= 0; i--)
            {
                var s = _badgeStates[i];
                if (s.Seen) continue;
                s.Gone += dt;
                if (s.Gone > LingerSeconds) s.Alpha = reduced ? 0f : s.Alpha - step;
                if (s.Alpha <= 0f) _badgeStates.RemoveAt(i); else _badgeStates[i] = s;
            }
            for (int si = 0; si < _badgeStates.Count; si++)
            {
                var s = _badgeStates[si];
                var b = s.Row;
                int n = IconCount(b);
                byte alpha = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(s.Alpha));
                var centre = ArenaView.WorldPoint((float)b.WorldX, (float)b.WorldY + BadgeOffsetUnits) + new Vector3(0f, s.Lift.Value, 0f);
                var back = Icon(_plaques, plaques++, "Camp reward plaque", Order - 1);
                Place(back, Plaque(), new Color32(255, 255, 255, alpha), centre, 1f);
                // The sprite goes in first: assigning it resets a sliced size to the sprite's own.
                var size = new Vector2(n * BadgeStep + .3f, PlaqueHeight);
                if (back.drawMode != SpriteDrawMode.Sliced) back.drawMode = SpriteDrawMode.Sliced;
                if (back.size != size) back.size = size;
                int k = 0;
                if (b.IconKey != null)
                    Place(Icon(_badgeIcons, used++, "Camp reward badge", Order), Art(b.IconKey), Rgba(b.IconRgb, alpha), centre + new Vector3((k++ - (n - 1) * .5f) * BadgeStep, 0f, 0f), BadgeScale);
                if (b.EliteIconKey != null)
                    Place(Icon(_badgeIcons, used++, "Camp reward badge", Order), Art(b.EliteIconKey), Rgba(b.EliteIconRgb, alpha), centre + new Vector3((k++ - (n - 1) * .5f) * BadgeStep, 0f, 0f), BadgeScale);
                s.Top = centre.y + PlaqueHeight * .5f; s.Drawn = true;
                _badgeStates[si] = s;
            }
            BadgesUsed = used;
            for (int i = used; i < _badgeIcons.Count; i++) if (_badgeIcons[i].gameObject.activeSelf) _badgeIcons[i].gameObject.SetActive(false);
            for (int i = plaques; i < _plaques.Count; i++) if (_plaques[i].gameObject.activeSelf) _plaques[i].gameObject.SetActive(false);
        }

        // Top edge of the plaque drawn this frame for a camp, if any.
        bool PlaqueTop(int index, out float top)
        {
            for (int i = 0; i < _badgeStates.Count; i++)
                if (_badgeStates[i].Drawn && _badgeStates[i].Index == index) { top = _badgeStates[i].Top; return true; }
            top = 0f; return false;
        }

        void DrawBeacons(float dt, bool reduced)
        {
            BeaconsUsed = WorldLabelVisibility.Hidden ? 0 : _wards.Count;
            for (int i = 0; i < BeaconsUsed; i++)
            {
                var w = _wards[i];
                Place(Icon(_beacons, i, "Camp ward beacon", Order - 1), Beacon(), Rgb(w.BeaconRgb), ArenaView.WorldPoint((float)w.WorldX, (float)w.WorldY), 1f);
                var label = TextAt(_beaconTexts, i);
                bool changed = label.text != w.WorldText;
                if (changed) label.text = w.WorldText;
                label.color = Rgb(w.TextRgb);
                var home = ArenaView.WorldPoint((float)w.WorldX, (float)w.WorldY + BeaconTextOffsetUnits);
                label.transform.position = home;
                if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
                if (changed || !_plates.ContainsKey(label)) FitPlate(label, _plates);
                float wanted = WantedClear(label, out float baseYMin);
                // A camp with a badge too: the text wants a gap above the plaque as drawn; the lift glides to it.
                if (PlaqueTop(w.Index, out float top)) wanted = Mathf.Max(wanted, top + WardGap - baseYMin);
                float lift = TextLift(w.Index, 1, wanted, dt, reduced);
                if (lift > 0f) label.transform.position = home + new Vector3(0f, lift, 0f);
            }
            for (int i = BeaconsUsed; i < _beacons.Count; i++) if (_beacons[i].gameObject.activeSelf) _beacons[i].gameObject.SetActive(false);
            for (int i = BeaconsUsed; i < _beaconTexts.Count; i++) if (_beaconTexts[i].gameObject.activeSelf) _beaconTexts[i].gameObject.SetActive(false);
        }

        TextMeshPro TextAt(List<TextMeshPro> pool, int i)
        {
            while (pool.Count <= i)
            {
                var t = NewLabel("Camp ward label"); t.alignment = TextAlignmentOptions.Bottom;
                pool.Add(t);
            }
            return pool[i];
        }

        TextMeshPro Get(int i)
        {
            while (_labels.Count <= i) _labels.Add(NewLabel("Camp respawn label"));
            return _labels[i];
        }

        TextMeshPro NewLabel(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(_root, false);
            var t = go.AddComponent<TextMeshPro>();
            var font = Resources.Load<TMP_FontAsset>("Showcase/NunitoBold");
            t.font = font != null ? font : TMP_Settings.defaultFontAsset;
            t.alignment = TextAlignmentOptions.Center; t.enableWordWrapping = false;
            t.fontSize = 3.4f; t.color = new Color32(0xe5, 0xed, 0xf7, 255);
            t.fontSharedMaterial = OverheadIndicators.OutlinedMaterial(t.font);
            t.rectTransform.sizeDelta = new Vector2(6f, .6f);
            t.GetComponent<MeshRenderer>().sortingOrder = Order;
            go.SetActive(false);
            return t;
        }
    }
}
