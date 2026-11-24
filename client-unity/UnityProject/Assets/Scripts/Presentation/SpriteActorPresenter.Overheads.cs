using System.Collections.Generic;
using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Positions and outlines the status indicators above each visible actor.</summary>
    public sealed partial class SpriteActorPresenter
    {
        /// <summary>
        /// The overhead cluster (hp track and fill, names, marks, status plates and icons, text) is drawn in the "Strokes" layer, above every
        /// body and prop and below the strokes' own order ties: it is therefore NOT in the grab the stroke shader reads, so a dark plate beside a fighter never decides that
        /// fighter's stroke polarity (the ground does), and the plates keep their own relative order. Bodies, the held weapon, the footprint, the contact shadow and every ground ring
        /// or bubble stay in "Default".
        /// </summary>
        static void MoveOverheadToStrokes(Transform root, Art a)
        {
            if (!StrokeLayer.Exists) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == a.Body || r == a.Weapon || r == a.WeaponSupport || r == a.PoxCenser || r == a.Halo || r == a.Shadow || r == a.Cast) continue;
                string n = r.gameObject.name;
                if (n.IndexOf("ring", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("bubble", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.Contains("shade")
                    || n.StartsWith(OverheadExtras.FireRoamPrefix, System.StringComparison.Ordinal)
                    || n == "Contact shadow" || n == "Cast shadow" || n == "Faction mantle") continue;
                bool authored = false;
                for (var tr = r.transform.parent; tr != null && tr != root; tr = tr.parent) if (tr.name == "Authored character") { authored = true; break; }
                if (authored || n == "Authored character") continue;
                r.sortingLayerID = StrokeLayer.Id;
            }
        }

        static bool NeedsStroke(string n)
        {
            if (n == null || n.EndsWith(" outline") || n == "Character contrast rim" || n == "Authored character" || n == "Contact shadow" || n == "Cast shadow" || n.StartsWith("Faction footprint") || n == "Faction mantle" || n.Contains("shade")) return false;
            return n.IndexOf("plate", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("backer", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("ring", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void SyncPlates(Art a, bool dark)
        {
            var p = a.Plates; if (p == null) return;
            for (int i = 0; i < p.Length; i++) p[i].SyncFollow(1f, dark);
        }

        static readonly Dictionary<ShowcaseCatalog.Appearance, float> HeadTops = new Dictionary<ShowcaseCatalog.Appearance, float>();
        /// <summary>Height of the tallest idle or walk frame above the feet in actor units (cached per look; the sprites are trimmed to their opaque pixels).</summary>
        static float HeadTop(ShowcaseCatalog.Appearance a)
        {
            if (HeadTops.TryGetValue(a, out float top)) return top;
            float m = 0f;
            foreach (var clip in new[] { a.Idle, a.Walk })
                if (clip != null) foreach (var sp in clip) if (sp != null) m = Mathf.Max(m, sp.bounds.max.y);
            top = .08f + m * a.WorldHeight; HeadTops[a] = top; return top;
        }

        readonly List<KeyValuePair<uint, Art>> _signatureCues = new List<KeyValuePair<uint, Art>>();
        readonly List<Rect> _signatureObstacles = new List<Rect>();

        static readonly System.Comparison<KeyValuePair<uint, Art>> SignatureOrder = CompareSignatureCues;

        internal void PlaceSignatureCues(List<Rect> numberEnvelopes)
        {
            _signatureCues.Clear();
            foreach (var pair in _active)
            {
                var art = pair.Value;
                if (art.Stamp == _stamp && !art.Dead && art.Ind.HasSignatureCue) _signatureCues.Add(pair);
            }
            if (_signatureCues.Count == 0) return;

            _signatureObstacles.Clear();
            _signatureObstacles.AddRange(numberEnvelopes);
            FighterHeaderRects(_signatureObstacles);
            foreach (var art in _active.Values)
            {
                if (art.Stamp != _stamp || art.Dead || !art.Root.activeSelf) continue;
                art.Ind.AddIndicatorObstacles(_signatureObstacles);
            }

            _signatureCues.Sort(SignatureOrder);
            foreach (var pair in _signatureCues)
            {
                var art = pair.Value;
                var bounds = CombatHudPlacement.SignatureCue(art.Ind.SignatureCueBounds,
                    art.Root.transform.position.x, _signatureObstacles);
                art.Ind.PlaceSignatureCue(bounds.center);
                _signatureObstacles.Add(bounds);
            }
        }

        static int CompareSignatureCues(KeyValuePair<uint, Art> a, KeyValuePair<uint, Art> b)
        {
            int local = b.Value.IndFacts.Local.CompareTo(a.Value.IndFacts.Local);
            if (local != 0) return local;
            var first = a.Value.Root.transform.position;
            var second = b.Value.Root.transform.position;
            int row = first.y.CompareTo(second.y);
            if (row != 0) return row;
            int column = first.x.CompareTo(second.x);
            return column != 0 ? column : a.Key.CompareTo(b.Key);
        }

        /// <summary>
        /// The delivered facts only the caller has (bounty, the local player's off-hand weapon, whether this actor is the attack lock), then the
        /// overhead cluster is drawn. Called once per actor right after <see cref="Present"/>.
        /// </summary>
        public void PresentIndicators(uint id, int bounty, bool hasSecondary, WeaponId secondary, bool locked, int effectsOverride = -1,
            bool hovered = false, bool hunted = false, int wishState = 0, byte wishAbility = 0)
        {
            if (!_active.TryGetValue(id, out var a)) return;
            var fx = a.IndFacts; fx.Bounty = bounty; fx.HasSecondary = hasSecondary; fx.Secondary = secondary; fx.Locked = locked; if (effectsOverride >= 0) fx.Effects = (ushort)effectsOverride;
            fx.Hovered = hovered; fx.Hunted = hunted; fx.WishState = wishState; fx.WishAbility = wishAbility;
            a.IndFacts = fx; a.Ind.Update(in a.IndFacts);
            SyncPlates(a, AtmosphereShadows.GroundIsDark(a.Root.transform.position.x, -a.Root.transform.position.y / ArenaView.GroundSquash));   // CONTRAST: plates were just updated
            if (a.Fade.Entering || a.Fade.Opacity < .999f) a.Ind.ApplyOpacity(a.Fade.Opacity);
        }

        /// <summary>The protocol 16 indicator facts of an observed actor (names, stat marks, mark plate, immunity, roam, engaged); read on the next <see cref="PresentIndicators"/>.</summary>
        public void PresentMarks(uint id, in OverheadIndicators.Marks marks)
        {
            if (!_active.TryGetValue(id, out var a)) return;
            var fx = a.IndFacts; fx.M = marks; a.IndFacts = fx;
        }
    }
}
