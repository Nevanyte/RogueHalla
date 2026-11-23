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
        /// <summary>Ends a frame with the presenter's own clock (the difference of two reads of <c>Now()</c>; the first End has dt 0).</summary>
        public void End()
        {
            float now = Now();
            float dt = _haveEndClock ? now - _endClock : 0f;
            _endClock = now; _haveEndClock = true;
            End(dt);
        }
        float _endClock; bool _haveEndClock;
        int _solvedStamp = -1;
        int _glideStamp = -1;

        /// <summary>Ends a frame with the clock the caller supplies (seconds since the previous frame): merges the pack names, lays every name out, evicts what was not drawn.</summary>
        public void End(float dt)
        {
            _frameLaunches.Clear();
            MergeMobNames(dt);
            if (WorldLabelVisibility.Hidden) { }
            else if (_solvedStamp != _stamp) { LayoutNames(dt); _solvedStamp = _stamp; }
            else ApplyLifts();
            _remove.Clear(); foreach (var pair in _active) if (pair.Value.Stamp != _stamp) _remove.Add(pair.Key);
            foreach (uint id in _remove)
            {
                Evict(id);
            }
        }
        /// <summary>Growth in x of every name rectangle handed to the camp labels, world units.</summary>
        public const float NameRectPad = .03f;
        /// <summary>The drawn and target lift of an actor's name cluster this frame; false when it took no part in the layout.</summary>
        public bool TryNameLift(uint id, out float lift, out float target)
        {
            if (_active.TryGetValue(id, out var art) && art.LayoutStamp == _stamp) { lift = art.LayoutLift; target = art.LayoutTarget; return true; }
            lift = target = 0f; return false;
        }

        NameMergeItem[] _mergeItems = new NameMergeItem[32]; bool[] _mergeShown = new bool[32]; uint[] _mergeIds = new uint[32]; int[] _mergeScratch = new int[32];
        float[] _anchorX = new float[32], _anchorY = new float[32], _packFade = new float[32]; int[] _packSize = new int[32]; long[] _packKey = new long[32];
        readonly List<Rect> _nameRects = new List<Rect>();
        readonly OverheadBoxLayout _layout = new OverheadBoxLayout();
        readonly List<Art> _boxArts = new List<Art>();

        // Forgets every lift and every stored rectangle (the Tab map is open, names are hidden).
        void ClearNameLayout()
        {
            _layout.Clear(); _nameRects.Clear(); _solvedStamp = -1;
            foreach (var pair in _active) pair.Value.LayoutStamp = -1;
        }

        // One rigid box per drawn name (fighters, elites, a pack's one name, lone mobs, dummies); each rises over the earlier boxes and other actors' bars.
        void LayoutNames(float dt)
        {
            _layout.Begin(); _boxArts.Clear(); _nameRects.Clear();
            foreach (var pair in _active)
            {
                var art = pair.Value;
                if (art.Stamp != _stamp || art.Dead || !art.Root.activeInHierarchy || art.HpBack == null || !art.HpBack.enabled) continue;
                var b = art.HpBack.bounds;
                _layout.AddFixed(b.min.x, b.min.y, b.max.x, b.max.y);
            }
            foreach (var pair in _active)
            {
                var art = pair.Value;
                if (art.Stamp != _stamp || art.Dead || art.Ind == null || !art.Root.activeInHierarchy) continue;
                var f = art.IndFacts;
                if (f.Dead || !art.Ind.NameActive) continue;
                byte cls; long key;
                if (!f.Mob) { cls = f.Local ? OverheadBoxLayout.ClassLocal : OverheadBoxLayout.ClassFighter; key = OverheadBoxLayout.FighterKey(pair.Key); }
                else if (f.Elite) { cls = OverheadBoxLayout.ClassElite; key = OverheadBoxLayout.SerialKey(art.LayoutKey); }
                else { cls = OverheadBoxLayout.ClassMob; key = f.Dummy ? OverheadBoxLayout.SerialKey(art.LayoutKey) : art.LayoutNameKey; }
                var at = art.Ind.NameBasePosition;
                _layout.AddBox(key, cls, at.x, at.y, !(f.Local && !f.Mob));
                art.Ind.AddClusterParts(_layout, art.LayoutPack);
                _boxArts.Add(art);
            }
            _layout.Solve(dt, UiMotion.Reduced);
            for (int i = 0; i < _boxArts.Count; i++)
            {
                var art = _boxArts[i];
                art.LayoutStamp = _stamp; art.LayoutLift = _layout.Lift(i); art.LayoutTarget = _layout.Target(i);
                for (int p = 0; p < _layout.PartCount(i); p++)
                {
                    _layout.PartRect(i, p, true, out float x0, out float y0, out float x1, out float y1);
                    _nameRects.Add(new Rect(x0 - NameRectPad, y0, x1 - x0 + 2f * NameRectPad, y1 - y0));
                }
            }
            ApplyLifts();
        }

        // Puts every laid-out cluster at its stored lift (no solve).
        void ApplyLifts()
        {
            foreach (var pair in _active)
            {
                var art = pair.Value;
                if (art.Stamp == _stamp && art.LayoutStamp == _stamp && art.Ind != null) art.Ind.LiftCluster(art.LayoutLift);
            }
        }

        /// <summary>World rects of every part of every name cluster drawn this frame, at their target lift, for the camp labels to keep clear of.</summary>
        public void FighterNameRects(List<Rect> into) { into.AddRange(_nameRects); }

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
                art.Ind.SetNameGround(FloorLuminance(Surfaces.SurfaceAt(art.Ind.NameBasePosition)));
            }
        }

        /// <summary>
        /// Mobs of one camp that stand together show exactly one name (P-UNITY-NAME-GROUP-2): over the pack, or over the hovered or locked member.
        /// Then the fighters' names are stacked so none overlaps another. Runs after every actor's indicators were presented. Players are never merged.
        /// </summary>
        void MergeMobNames(float dt)
        {
            if (WorldLabelVisibility.Hidden)
            {
                ClearNameLayout();
                // The Tab map is open: no world name may show through behind its panels.
                foreach (var pair in _active) if (pair.Value.Ind != null) pair.Value.Ind.HideName();
                return;
            }
            int n = 0;
            foreach (var pair in _active)
            {
                var art = pair.Value;
                art.LayoutPack = false;
                if (pair.Value.Stamp != _stamp || art.Dead || art.Ind == null) continue;
                var f = art.IndFacts;
                if (f.Dead || !art.Ind.NameActive) continue;
                if (!f.Mob) continue;
                if (f.Dummy) continue;
                if (n == _mergeItems.Length)
                {
                    System.Array.Resize(ref _mergeItems, n * 2); System.Array.Resize(ref _mergeShown, n * 2); System.Array.Resize(ref _mergeIds, n * 2); System.Array.Resize(ref _mergeScratch, n * 2);
                    System.Array.Resize(ref _anchorX, n * 2); System.Array.Resize(ref _anchorY, n * 2); System.Array.Resize(ref _packSize, n * 2); System.Array.Resize(ref _packFade, n * 2); System.Array.Resize(ref _packKey, n * 2);
                }
                var at = art.Ind.NameBasePosition;
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
                for (int i = 0; i < n; i++) { _packSize[i] = 0; _packFade[i] = 0f; _packKey[i] = long.MaxValue; }
                for (int i = 0; i < n; i++)
                {
                    _packSize[_mergeScratch[i]]++;
                    if (_active.TryGetValue(_mergeIds[i], out var member) && OverheadBoxLayout.SerialKey(member.LayoutKey) < _packKey[_mergeScratch[i]]) _packKey[_mergeScratch[i]] = OverheadBoxLayout.SerialKey(member.LayoutKey);
                    // A pack's one name fades with its most visible member, so a carrier at the edge of vision does not make it faint.
                    if (_active.TryGetValue(_mergeIds[i], out var m) && m.Ind != null && m.Ind.TextFade > _packFade[_mergeScratch[i]]) _packFade[_mergeScratch[i]] = m.Ind.TextFade;
                }
                for (int i = 0; i < n; i++)
                {
                    if (!_active.TryGetValue(_mergeIds[i], out var art)) continue;
                    if (!_mergeShown[i]) { art.Ind.HideName(); continue; }
                    bool pack = _packSize[_mergeScratch[i]] > 1;
                    art.LayoutPack = pack;
                    art.LayoutNameKey = _mergeItems[i].Camp > 0 ? OverheadBoxLayout.CampKey(_mergeItems[i].Camp) : _packKey[_mergeScratch[i]];
                    // Strength is set in OverheadIndicators.Update (full for every shown mob name, DEV-024); only a pack's one name takes the fade of its most visible member.
                    if (pack) art.Ind.NameFullAlpha(_packFade[_mergeScratch[i]]);
                    if (_anchorX[i] != _mergeItems[i].X || _anchorY[i] != _mergeItems[i].Y) art.Ind.MoveName(_anchorX[i], _anchorY[i]);
                }
            }
            FinishNames();
        }
    }
}
