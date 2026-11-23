using System.Collections.Generic;
using UnityEngine;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client
{
    /// <summary>The name pass of the presenter: pack merging, fighter stacking and ground colours of the world names. Moved unchanged out of the main file.</summary>
    public sealed partial class SpriteActorPresenter
    {
        NameMergeItem[] _mergeItems = new NameMergeItem[32]; bool[] _mergeShown = new bool[32]; uint[] _mergeIds = new uint[32]; int[] _mergeScratch = new int[32];
        public void End()
        {
            _frameLaunches.Clear();
            MergeMobNames();
            _remove.Clear(); foreach (var pair in _active) if (pair.Value.Stamp != _stamp) _remove.Add(pair.Key);
            foreach (uint id in _remove)
            {
                Evict(id);
            }
        }
        // STUB (P-UI-WORLD-NAMES-2): the implementer replaces these four members with the rule.
        /// <summary>Ends a frame with the clock the caller supplies (seconds since the previous frame).</summary>
        public void End(float dt) { End(); }
        /// <summary>Growth in x of every name rectangle handed to the camp labels, world units.</summary>
        public const float NameRectPad = .03f;
        /// <summary>The drawn and target lift of an actor's name cluster this frame; false when it took no part in the layout.</summary>
        public bool TryNameLift(uint id, out float lift, out float target) { lift = target = 0f; return false; }
        void LayoutNames(float dt) { }
        NameStackItem[] _stackItems = new NameStackItem[16]; float[] _lift = new float[16]; uint[] _stackIds = new uint[16];
        float[] _anchorX = new float[32], _anchorY = new float[32], _packFade = new float[32]; int[] _packSize = new int[32];
        readonly List<uint> _packNameIds = new List<uint>();
        readonly List<Rect> _nameRects = new List<Rect>();
        /// <summary>World rects of the fighter names drawn this frame (after stacking), for the camp labels to keep clear of.</summary>
        public void FighterNameRects(List<Rect> into)
        {
            // The elite names step clear of other mobs' bars here, the seam the camp labels read after the frame's actors are placed, then every fighter, elite and pack name is reported.
            _nameRects.Clear();
            foreach (var pair in _active)
            {
                var art = pair.Value;
                if (art.Stamp != _stamp || art.Dead || art.Ind == null) continue;
                var f = art.IndFacts;
                if (f.Dead || !art.Ind.NameActive) continue;
                bool elite = f.Mob && f.Elite;
                if (elite) ClearOfBars(art);
                if (f.Mob && !elite && !_packNameIds.Contains(pair.Key)) continue;
                var nb = art.Ind.NameDrawnBounds;
                _nameRects.Add(new Rect(nb.min.x - .03f, nb.min.y, nb.size.x + .06f, nb.size.y));
            }
            into.AddRange(_nameRects);
        }

        const float BarClearance = .15f;
        int _layoutSerial;

        // Last step of the name pass, once every name is placed: each name takes the colours that read on the ground under its anchor.
        void FinishNames()
        {
            if (Surfaces == null) return;
            foreach (var pair in _active)
            {
                var art = pair.Value;
                if (art.Stamp != _stamp || art.Dead || art.Ind == null) continue;
                if (art.IndFacts.Dead || !art.Ind.NameActive) continue;
                art.Ind.SetNameGround(FloorLuminance(Surfaces.SurfaceAt(art.Ind.NameWorldPosition)));
            }
        }

        void ClearOfBars(Art self)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                var nb = self.Ind.NameDrawnBounds; float lift = 0f;
                foreach (var pair in _active)
                {
                    var o = pair.Value;
                    if (o == self || o.Stamp != _stamp || o.Dead || o.HpBack == null || !o.HpBack.enabled) continue;
                    var b = o.HpBack.bounds;
                    if (b.max.x + BarClearance <= nb.min.x || b.min.x - BarClearance >= nb.max.x || b.max.y + BarClearance <= nb.min.y || b.min.y - BarClearance >= nb.max.y) continue;
                    lift = Mathf.Max(lift, b.max.y + BarClearance - nb.min.y);
                }
                if (lift <= 0f) return;
                self.Ind.LiftName(lift);
            }
        }

        /// <summary>
        /// Mobs of one camp that stand together show exactly one name (P-UNITY-NAME-GROUP-2): over the pack, or over the hovered or locked member.
        /// Then the fighters' names are stacked so none overlaps another. Runs after every actor's indicators were presented. Players are never merged.
        /// </summary>
        void MergeMobNames()
        {
            _nameRects.Clear();
            if (WorldLabelVisibility.Hidden)
            {
                // The Tab map is open: no world name may show through behind its panels.
                foreach (var pair in _active) if (pair.Value.Ind != null) pair.Value.Ind.HideName();
                return;
            }
            int n = 0, fighters = 0;
            foreach (var pair in _active)
            {
                var art = pair.Value;
                if (pair.Value.Stamp != _stamp || art.Dead || art.Ind == null) continue;
                var f = art.IndFacts;
                if (f.Dead || !art.Ind.NameActive) continue;
                if (!f.Mob) { fighters++; continue; }
                if (f.Dummy) continue;
                if (n == _mergeItems.Length)
                {
                    System.Array.Resize(ref _mergeItems, n * 2); System.Array.Resize(ref _mergeShown, n * 2); System.Array.Resize(ref _mergeIds, n * 2); System.Array.Resize(ref _mergeScratch, n * 2);
                    System.Array.Resize(ref _anchorX, n * 2); System.Array.Resize(ref _anchorY, n * 2); System.Array.Resize(ref _packSize, n * 2); System.Array.Resize(ref _packFade, n * 2);
                }
                var at = art.Ind.NameWorldPosition;
                int camp = 0;
                if (!f.Elite && art.MobType.HasValue && _camps != null)
                {
                    var lp = art.LastPosition;
                    camp = NameMerge.CampOfMob(_camps, _campRows, art.MobType.Value, lp.x / ArenaView.Scale, -lp.y / (ArenaView.Scale * ArenaView.GroundSquash)) + 1;
                }
                _mergeItems[n] = new NameMergeItem { Id = pair.Key, Text = f.Name, X = at.x, Y = at.y, Width = art.Ind.NameWorldWidth, Elite = f.Elite, Single = (byte)(f.Hovered ? 2 : f.Locked ? 1 : 0), Camp = camp };
                _mergeIds[n] = pair.Key; n++;
            }
            if (n > 0)
            {
                NameMerge.PackNames(_mergeItems, n, NameMerge.PackDistancePx * ArenaView.Scale, _mergeShown, _mergeScratch, _anchorX, _anchorY);
                for (int i = 0; i < n; i++) { _packSize[i] = 0; _packFade[i] = 0f; }
                for (int i = 0; i < n; i++)
                {
                    _packSize[_mergeScratch[i]]++;
                    // A pack's one name fades with its most visible member, so a carrier at the edge of vision does not make it faint.
                    if (_active.TryGetValue(_mergeIds[i], out var m) && m.Ind != null && m.Ind.TextFade > _packFade[_mergeScratch[i]]) _packFade[_mergeScratch[i]] = m.Ind.TextFade;
                }
                _packNameIds.Clear();
                for (int i = 0; i < n; i++)
                {
                    if (!_active.TryGetValue(_mergeIds[i], out var art)) continue;
                    if (!_mergeShown[i]) { art.Ind.HideName(); continue; }
                    bool pack = _packSize[_mergeScratch[i]] > 1;
                    // Strength is set in OverheadIndicators.Update (full for every shown mob name, DEV-024); only a pack's one name takes the fade of its most visible member.
                    if (pack) art.Ind.NameFullAlpha(_packFade[_mergeScratch[i]]);
                    if (_anchorX[i] != _mergeItems[i].X || _anchorY[i] != _mergeItems[i].Y) art.Ind.MoveName(_anchorX[i], _anchorY[i]);
                    if (pack) _packNameIds.Add(_mergeIds[i]);
                }
            }
            StackFighterNames(fighters);
            FinishNames();
        }

        // Fighters whose names would overlap are stacked, the one nearest the local fighter lowest. Names are never merged.
        void StackFighterNames(int fighters)
        {
            if (fighters == 0) return;
            int total = fighters + _packNameIds.Count;
            if (_stackItems.Length < total)
            {
                int size = total * 2;
                System.Array.Resize(ref _stackItems, size); System.Array.Resize(ref _lift, size); System.Array.Resize(ref _stackIds, size);
            }
            bool haveLocal = false; Vector3 local = default;
            foreach (var pair in _active)
            {
                var art = pair.Value;
                if (pair.Value.Stamp != _stamp || art.Dead || art.Ind == null || !art.IndFacts.Local || art.IndFacts.Mob || !art.Ind.NameActive) continue;
                local = art.Ind.NameWorldPosition; haveLocal = true; break;
            }
            int k = 0;
            foreach (var pair in _active)
            {
                var art = pair.Value;
                if (pair.Value.Stamp != _stamp || art.Dead || art.Ind == null) continue;
                var f = art.IndFacts;
                if (f.Dead || f.Mob || !art.Ind.NameActive) continue;
                var nb = art.Ind.NameDrawnBounds; var at = nb.center; float w = nb.size.x + .06f, h = nb.size.y;   // the drawn mesh, a couple of pixels of gap
                _stackItems[k] = new NameStackItem { Id = pair.Key, X = at.x, Y = nb.min.y, W = w, H = h, Dist = haveLocal ? Mathf.Sqrt((at.x - local.x) * (at.x - local.x) + (at.y - local.y) * (at.y - local.y)) : 0f };
                _stackIds[k] = pair.Key; k++;
            }
            int fighterCount = k;
            // A pack's name takes part last (its distance is offset past every fighter's), so fighters keep their places and the pack steps above.
            foreach (uint id in _packNameIds)
            {
                if (!_active.TryGetValue(id, out var art) || !art.Ind.NameActive) continue;
                var nb = art.Ind.NameDrawnBounds; var at = nb.center;
                float dist = haveLocal ? Mathf.Sqrt((at.x - local.x) * (at.x - local.x) + (at.y - local.y) * (at.y - local.y)) : 0f;
                _stackItems[k] = new NameStackItem { Id = id, X = at.x, Y = nb.min.y, W = nb.size.x + .06f, H = nb.size.y, Dist = 1e6f + dist };
                _stackIds[k] = id; k++;
            }
            NameStack.Separate(_stackItems, k, .02f, _lift);
            for (int i = 0; i < k; i++)
            {
                var item = _stackItems[i];
                if (_lift[i] > 0f && _active.TryGetValue(_stackIds[i], out var art)) art.Ind.LiftName(_lift[i]);
            }
        }
    }
}
