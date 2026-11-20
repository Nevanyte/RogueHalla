using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client
{
    /// <summary>Owned visual objects keyed only by currently supplied observer handles. Nothing here simulates a rule.</summary>
    public sealed partial class SpriteActorPresenter
    {
        readonly Transform _parent;
        public SurfaceFx Fx; // SURFACE-FX HOOK: set by the owning ArenaView, so cues go to the arena that presents
        readonly Dictionary<uint, Art> _active = new Dictionary<uint, Art>();
        readonly Stack<Art> _pool = new Stack<Art>();
        readonly List<uint> _remove = new List<uint>();
        readonly List<OverheadElement> _barLayout = new List<OverheadElement>();
        readonly ShowcaseCatalog _catalog;
        int _stamp;
        const float ActorVisualScale = 1.2f;
        /// <summary>The faction footprint outline sits at this fraction of the sprite's half-size (FactionIndicators textures).</summary>
        const float FootprintOutline = .72f;
        public SpriteActorPresenter(Transform parent, ShowcaseCatalog catalog) { _parent = parent; _catalog = catalog; SpriteOutline.ConfigureFromUrl(Application.absoluteURL); /* CONTRAST */ }
        public bool Contains(uint id) { return _active.ContainsKey(id); }
        /// <summary>
        /// The delivered camps (Session.Map.Camps and Session.Collections.Camps, index-aligned) so a mob's name can be packed by camp
        /// (P-UNITY-HUD-VISUAL-FIX-4, <see cref="NameMerge.CampOfMob"/>). Null or empty: every mob has no camp and packs by distance.
        /// Stub: stores nothing; the implementer keeps the arrays and uses them in the pack merge. ArenaView calls it (coordinator wiring).
        /// </summary>
        public void SetCamps(MapCamp[] camps, IReadOnlyList<CampView> rows) { _camps = camps; _campRows = rows; }
        MapCamp[] _camps; IReadOnlyList<CampView> _campRows;
        /// <summary>
        /// The room's surface lookup (region and terrain plane plus decorative layers), set by the owning ArenaView. A world name asks it what lies under
        /// the name's anchor so its face and outline colours read on that ground (P-UNITY-NAME-LEGIBLE-1, <see cref="Roguehalla.Net.Ui.NamePalette"/>).
        /// Null (no map, tests): every name keeps the dark-ground colours it has today. Stub: nothing reads it yet.
        /// </summary>
        public SurfaceMap Surfaces;
        public int ActiveCount { get { return _active.Count; } }
        /// <summary>True when the actor's overhead name label is drawn this frame (test-visible; P-UNITY-HUD-VISUAL-FIX-2).</summary>
        public bool NameShown(uint id) { return _active.TryGetValue(id, out var art) && art.Ind.NameActive; }
        public bool IsLiving(uint id) { return _active.TryGetValue(id, out var art) && !art.Dead; }
        public bool TrySoundContext(uint id, out WeaponId weapon, out FactionId faction, out ActorKind kind, out MobTypeId? type)
        {
            if (_active.TryGetValue(id, out var art) && art.WeaponId.HasValue && art.Faction.HasValue)
            { weapon=art.WeaponId.Value; faction=art.Faction.Value; kind=art.Kind; type=art.MobType; return true; }
            weapon=default; faction=default; kind=default; type=null; return false;
        }
        /// <summary>Clears local-time animation phases after a presentation clock cut.</summary>
        public void ResetTransientTimers()
        {
            foreach (var pair in _active)
            {
                var a = pair.Value;
                ClearWeaponMotion(a);
                a.HitUntil = a.BlockUntil = 0; a.WishUntil = 0;
                a.Gait.Reset();
                a.FireAxeReleaseCount = 0;
                a.FireSwordReleaseCount = a.FireBowShotCount = 0;
                a.AttackAt = a.HitAt = a.BlockAt = a.DeadAt = 0;
                a.HoldDeathFrame = a.Dead;
            }
        }
        public bool TryPosition(uint id, out Vector3 position)
        {
            if (_active.TryGetValue(id, out var art)) { position = art.Root.transform.position; return true; }
            position = default; return false;
        }
        public void Begin() { _stamp++; }
        NameMergeItem[] _mergeItems = new NameMergeItem[32]; bool[] _mergeShown = new bool[32]; uint[] _mergeIds = new uint[32]; int[] _mergeScratch = new int[32];
        public void End()
        {
            MergeMobNames();
            _remove.Clear(); foreach (var pair in _active) if (pair.Value.Stamp != _stamp) _remove.Add(pair.Key);
            foreach (uint id in _remove)
            {
                Evict(id);
            }
        }
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

        /// <summary>Presented head bands and health bars, used by the camp reward plaque.</summary>
        public void FighterHeaderRects(List<Rect> into)
        {
            foreach (var art in _active.Values)
            {
                if (art.Stamp != _stamp || art.Dead || !art.Root.activeSelf || !art.Body.enabled) continue;
                var body = art.Body.bounds;
                float halfWidth = Mathf.Min(.52f, body.extents.x * .70f);
                float height = Mathf.Min(.68f, body.extents.y * .75f);
                into.Add(new Rect(body.center.x - halfWidth, body.max.y - height, halfWidth * 2f, height));
                if (art.HpBack.enabled) into.Add(CombatHudPlacement.FromBounds(art.HpBack.bounds));
            }
        }

        // Luminance of what a player sees on each surface, for NamePalette.IsPale: the averages measured for P-UNITY-NAME-LEGIBLE-1 (ground texels through the
        // ground shader, scaled by the drawn-light ratio). Every SurfaceKind has an explicit entry; NameLegibleTableEditModeTests keeps the measured ones
        // in step with the texels. The kinds with no texel source are decisions, not measurements:
        //   Ember, Lava, Bog: dark. Charred floor, obsidian crust and murky green with a glow; a light face reads on them, and the glow is not modelled.
        //   Grass: the Plague mud average (the grass patches are part of the Plague sample, the brightest decile of Mud).
        //   ShallowWater: dark. There is no water terrain in the ground table; legacy draws it a deep blue, below the .18 switch.
        //   None: dark. No region (outside the map): the palette of today.
        const float AssumedDark = .05f;
        static readonly float Basalt = Lum(0x46251C), Flagstone = Lum(0x272223), Mud = Lum(0x394C23), Snow = Lum(0xA1BED0), Sand = Lum(0xCC9F55),
            Limestone = Lum(0x9C9183), Ice = Lum(0xA6CAE3), HolyLight = Lum(0xAC8B4B), TempleFloor = Lum(0xBEAA86);
        static float Lum(uint srgb) { return (float)NamePalette.Luminance(srgb); }

        /// <summary>The relative luminance the name palette assumes for the ground of <paramref name="kind"/>. No allocation.</summary>
        public static float FloorLuminance(SurfaceKind kind)
        {
            switch (kind)
            {
                case SurfaceKind.Basalt: return Basalt;
                case SurfaceKind.Flagstone: return Flagstone;
                case SurfaceKind.Mud: return Mud;
                case SurfaceKind.Snow: return Snow;
                case SurfaceKind.Sand: return Sand;
                case SurfaceKind.Limestone: return Limestone;
                case SurfaceKind.Ice: return Ice;
                case SurfaceKind.HolyLight: return HolyLight;
                case SurfaceKind.TempleFloor: return TempleFloor;
                case SurfaceKind.Grass: return Mud;
                case SurfaceKind.Ember: case SurfaceKind.Lava: case SurfaceKind.Bog: case SurfaceKind.ShallowWater: case SurfaceKind.None: return AssumedDark;
            }
            throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "a SurfaceKind with no floor luminance");
        }

        const float BarClearance = .15f;

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

        public void Retain(HashSet<uint> membership, List<InterpActor> observed = null)
        {
            _remove.Clear(); foreach(var pair in _active) if(!membership.Contains(pair.Key)) _remove.Add(pair.Key);
            MarkImmediateMotionSuccessors(observed);
            foreach(uint id in _remove) Evict(id);
        }
        void Evict(uint id)
        {
            var art=_active[id];
            if (TryParkBody(id, art)) return; // a dead creature waits for its renewed id (SpriteActorPresenter.Fade.cs)
            if (art.MotionSuccessor == 0 && (HasWeaponTrack(art) || HasHauntedFists(art))) ClearWeaponMotion(art);
            if (TryBeginExit(id, art)) return; // a living actor that left sight fades out (SpriteActorPresenter.Fade.cs)
            _active.Remove(id); Release(art);
        }
        void Release(Art art)
        {
            if (IsCreature(art.Fade.Kind)) MobArt.Destroyed++;
            art.Light.SetVisible(false);
            art.Barrier.enabled = false;
            art.Ind.HideAll();
            foreach (var segment in art.BarrierRing) segment.enabled = false;
            art.Identity.Clear();
            art.BodyArtistSource?.Clear();
            art.WeaponArtistSource?.Clear();
            art.Root.SetActive(false);
            _pool.Push(art);
        }

        public void Present(uint id, float x, float y, float facing, float hp, FactionId? faction, ActorStatus status, WeaponId? weapon,
            ActorKind kind, MobTypeId? mob, ushort effects, byte signature, bool local, float barrierPct = 0f, byte barrierRingStep = 0, string archetypeKey = null, float footHx = 0f, float footHy = 0f, uint drawStartedAt = 0, double sampleTickMs = 0)
        {
            if (!_active.TryGetValue(id, out var a))
            {
                if (!local && IsCreature(kind) && TryContinue(id, kind, faction, mob, archetypeKey, x, y, status == ActorStatus.Dead || status == ActorStatus.Mia, out a)) _active.Add(id, a); // a renewed handle
                else
                {
                    a = _pool.Count > 0 ? _pool.Pop() : Create(); _active.Add(id, a);
                    a.Root.SetActive(true); a.LastPosition = ArenaView.WorldPoint(x, y); ClearWeaponMotion(a);
                    a.FireAxeReleaseCount = a.FireSwordReleaseCount = a.FireBowShotCount = 0;
                    a.Gait.Reset(); a.StampTime = Time.unscaledTime;
                    a.HitUntil=a.BlockUntil=0;a.Dead=false;a.HoldDeathFrame=false;a.WishUntil=0;a.WishTintAmount=0;
                    if (IsCreature(kind)) MobArt.Created++;
                    BeginEnter(a, local, kind, IsCreature(kind) ? VisionFade.EnterStartAlpha(status) : 0f);
                }
            }
            if (IsCreature(kind)) a.Look = LookOf(kind, faction, mob, archetypeKey);
            a.Stamp = _stamp; a.Fade.Note(id, local, status, kind);
            var position = ArenaView.WorldPoint(x, y);
            // Gait reads displacement only (presentation). Present can run twice in a frame for NPCs: a zero-dt call leaves
            // LastPosition alone so the next real frame still sees the whole displacement.
            float gaitNow = Time.unscaledTime, gaitDt = gaitNow - a.StampTime;
            if (gaitDt > 1e-4f)
            {
                float dxs = (position.x - a.LastPosition.x) / ArenaView.Scale;
                float dys = -(position.y - a.LastPosition.y) / (ArenaView.Scale * ArenaView.GroundSquash);
                float walkFps = a.Appearance != null ? a.Appearance.WalkFPS : 10f;
                Gait.Advance(ref a.Gait, dxs, dys, gaitDt, facing, walkFps, local ? 0f : .12f);
                a.StampTime = gaitNow; a.LastPosition = position;
            }
            a.Root.transform.position = position;
            int order = 2000 + Mathf.RoundToInt(y * .5f);
            bool neutral = kind == ActorKind.QuestGiver;
            Color color = faction.HasValue ? ShowcasePalette.Faction((int)faction.Value) : ShowcasePalette.MutedText;
            bool dead = status == ActorStatus.Dead || status == ActorStatus.Mia;
            if(dead && !a.Dead) { a.DeadAt=Time.unscaledTime; a.HoldDeathFrame=false; }
            if(!dead) a.HoldDeathFrame=false;
            float hiddenAlpha = Indicators.HiddenAlpha(effects, local);
            if (dead || hiddenAlpha <= 0f || a.WeaponId != weapon || a.Faction != faction || a.MobType != mob || a.Kind != kind)
            {
                ClearWeaponMotion(a);
                a.BlockUntil = 0f;
            }
            a.Dead=dead;a.VisibleForCombatCue=!dead && hiddenAlpha > 0f;a.WeaponId=weapon; a.Faction=faction; a.Kind=kind; a.MobType=mob;
            bool trackedWeapon = !dead && hiddenAlpha > 0f && HasWeaponTrack(a);
            bool hauntedFists = HasHauntedFists(a);
            bool saint = kind == ActorKind.RegionalMob && mob == MobTypeId.BloodSaint;
            bool paintedWand = false;
            var phase = StormInkWeaponMotion.Phase.Guard;
            float weaponProgress = 0f, poseFacing = facing;
            if (trackedWeapon)
            {
                if (weapon == WeaponId.Bow && a.DrawStartedAt != drawStartedAt)
                {
                    // The owner snapshot carries a start edge only. A cleared draw cannot start a release.
                    if (drawStartedAt != 0)
                    {
                        a.WindupAt = gaitNow - Mathf.Max(0f, (float)(sampleTickMs - drawStartedAt) * .001f);
                        a.WindupUntil = 0f;
                        a.LocalCue = false;
                    }
                    a.DrawStartedAt = drawStartedAt;
                }
                if (a.BlockUntil > gaitNow && weapon == WeaponId.Shield)
                    phase = StormInkWeaponMotion.Phase.Block;
                else if (a.AttackUntil > gaitNow)
                {
                    float hold = StormInkWeaponMotion.ContactHold(faction.Value);
                    float age = gaitNow - a.AttackAt;
                    phase = age < hold ? StormInkWeaponMotion.Phase.Strike : StormInkWeaponMotion.Phase.Follow;
                    weaponProgress = (age - hold) / Mathf.Max(.01f, a.AttackUntil - a.AttackAt - hold);
                    if (a.HasAttackFacing) poseFacing = a.AttackFacing;
                }
                else if (a.WindupUntil > gaitNow || a.DrawStartedAt != 0)
                {
                    phase = StormInkWeaponMotion.Phase.Ready;
                    // Cosmetic bow pull saturates while the delivered draw remains open. Only a shown arrow starts its release.
                    weaponProgress = a.DrawStartedAt != 0 ? (gaitNow - a.WindupAt) * 4f :
                        (gaitNow - a.WindupAt) / Mathf.Max(.01f, a.WindupUntil - a.WindupAt);
                    if (a.LocalCue) weaponProgress = weapon == WeaponId.Sword
                        ? Mathf.Clamp01(weaponProgress) : Mathf.Min(.48f, weaponProgress);
                    if (a.HasAttackFacing) poseFacing = a.AttackFacing;
                }
            }
            bool isMob = kind != ActorKind.Player;
            // The server's archetype key (catalog key) picks the look; a key the catalog lacks falls back to the faction/type mapping.
            string key = ShowcaseActorLook.AppearanceKey(kind, faction, mob, archetypeKey);
            a.Appearance = !neutral && _catalog != null ? _catalog.Find(key) : null;
            if (a.Appearance == null && !neutral && _catalog != null && ShowcaseActorLook.IsArchetypeLook(archetypeKey))
                a.Appearance = _catalog.Find(ShowcaseActorLook.AppearanceKey(kind, faction, mob));
            if (a.Appearance == null && _catalog != null && kind == ActorKind.Player) a.Appearance = _catalog.Find("hero");
            if (a.Appearance == null && _catalog != null && !neutral) a.Appearance = _catalog.Find("knight");
            bool reduced = ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion;
            var appearance = a.Appearance;
            var fireAxeArt = kind == ActorKind.Player && faction == FactionId.Fire &&
                (weapon == WeaponId.Axe || weapon == WeaponId.Sword || weapon == WeaponId.Bow)
                ? StormInkFireAxeAuthoring.Load() : null;
            var fireSwordArt = kind == ActorKind.Player && faction == FactionId.Fire && weapon == WeaponId.Sword
                ? StormInkFireSwordAuthoring.Load() : null;
            float fallAngle = dead && (appearance == null || appearance.Death == null || appearance.Death.Length == 0) ? 75f : 0f;
            Sprite[] frames=appearance?.Idle;
            float fps=appearance!=null ? appearance.IdleFPS : 10;
            float elapsed=Time.unscaledTime;bool loop=true,walkPhase=false,useWeaponPhase=false;
            if(appearance!=null)
            {
                if(dead && appearance.Death!=null && appearance.Death.Length>0)
                { frames=appearance.Death;fps=appearance.DeathFPS;elapsed-=a.DeadAt;loop=false; }
                else if(a.WishUntil>Time.unscaledTime && WishClipFrames(appearance,a.WishClip,out frames,out fps))
                { elapsed-=a.WishAt;loop=false; }
                else if(a.BlockUntil>Time.unscaledTime && appearance.Block!=null && appearance.Block.Length>0)
                { frames=appearance.Block;fps=appearance.BlockFPS;elapsed-=a.BlockAt;loop=false; }
                else if(phase != StormInkWeaponMotion.Phase.Strike && a.HitUntil>gaitNow && appearance.Hit!=null && appearance.Hit.Length>0)
                { frames=appearance.Hit;fps=appearance.HitFPS;elapsed-=a.HitAt;loop=false; }
                else if(trackedWeapon ? phase == StormInkWeaponMotion.Phase.Ready || phase == StormInkWeaponMotion.Phase.Strike ||
                    phase == StormInkWeaponMotion.Phase.Follow && !reduced : !hauntedFists && a.AttackUntil > gaitNow && weapon.HasValue)
                {
                    frames = AttackFrames(appearance, weapon.Value, out fps);
                    elapsed -= a.AttackAt; loop = false;
                    useWeaponPhase = trackedWeapon;
                }
                else if(a.Gait.Walking && appearance.Walk.Length>0)
                { frames=appearance.Walk;fps=appearance.WalkFPS;walkPhase=true; }
            }
            float? measuredHeight = null;
            Sprite shieldBody = null, axeBody = null, swordBody = null, bowBody = null;
            float fireSwordAngleOffset = 0f;
            int fireSwordDepthOffset = 0;
            if (frames != null && frames.Length > 0)
            {
                // Catalog arrays are direction-major, with exactly eight source directions. Facing is artwork only.
                int count = frames.Length / 8;
                // Flare: SW,W,NW,N,NE,E,SE,S; server facing zero is screen east.
                int direction = ((Mathf.RoundToInt((useWeaponPhase ? poseFacing : facing) / (Mathf.PI / 4)) + 5) % 8 + 8) % 8;
                int rawFrame=walkPhase ? 0 : Mathf.Max(0,Mathf.FloorToInt(elapsed*Mathf.Max(1,fps)));
                int frame = dead && a.HoldDeathFrame ? count - 1 :
                    reduced ? (dead ? count-1 : 0) : walkPhase ? Gait.FrameIndex(a.Gait.Phase,count) : loop ? rawFrame%Mathf.Max(1,count) : Mathf.Min(count-1,rawFrame);
                if (useWeaponPhase)
                {
                    // The assembled sheet's wind-up and contact poses meet the delivered combat phases.
                    frame = phase == StormInkWeaponMotion.Phase.Ready ? 0 :
                        phase == StormInkWeaponMotion.Phase.Strike ? count / 2 : count - 1;
                }
                // A two-frame catalog attack has no separate recovery drawing. Its duration
                // still drives the action, while painted art uses all four authored pose slots.
                int inkFrame = useWeaponPhase ? phase == StormInkWeaponMotion.Phase.Ready ? 0 :
                    phase == StormInkWeaponMotion.Phase.Strike ? 2 : 3 : frame;
                var painted = StormInkActors.Frame(kind, faction, mob, appearance, frames, direction,
                    inkFrame, useWeaponPhase ? 4 : count, out bool mirror);
                // The Saint’s non-transferable wand belongs to the painted left hand in every frame.
                if (saint && weapon != WeaponId.Wand) painted = null;
                paintedWand = saint && painted != null;
                Sprite unarmed = hauntedFists && !dead && hiddenAlpha > 0f
                    ? HauntedFistsFrame(a, facing, gaitNow, reduced) : null;
                if (useWeaponPhase || phase == StormInkWeaponMotion.Phase.Block && frames == appearance.Block)
                    shieldBody = StormInkActors.HolyShieldFrame(kind, faction, weapon, direction, phase);
                if (!dead && hiddenAlpha > 0f && (useWeaponPhase || frames == appearance.Idle || frames == appearance.Walk))
                {
                    int row = StormInkActors.PoseRow(direction);
                    int stage = reduced || fireAxeArt == null ? 0 : walkPhase
                        ? Gait.FrameIndex(a.Gait.Phase, fireAxeArt.StrideStagesFor(row)) : fireAxeArt.IdleStage(row, elapsed);
                    bool stride = fireAxeArt != null ? walkPhase : walkPhase && frame * 2 >= count;
                    SelectFireWeaponBody(a, kind, faction, weapon, direction, phase, weaponProgress,
                        useWeaponPhase, stride, stage, reduced, fireSwordArt, out axeBody, out swordBody,
                        out fireSwordAngleOffset, out fireSwordDepthOffset);
                    bowBody = SelectFireBowBody(a, kind, faction, weapon, direction, phase, weaponProgress,
                        useWeaponPhase, stride, stage, fireAxeArt);
                }
                a.Body.sprite = unarmed ?? shieldBody ?? swordBody ?? bowBody ?? axeBody ?? painted ?? frames[Mathf.Min(frames.Length - 1, direction * count + frame)];
                bool paintedPox = IsPox(a) && painted != null;
                float height = paintedWand ? 1.24f : paintedPox ? PoxHeight : appearance.WorldHeight;
                if (axeBody != null && fireAxeArt != null && fireAxeArt.TryHand(axeBody, false, out _))
                    measuredHeight = height = fireAxeArt.WorldHeight;
                if (swordBody != null && fireSwordArt != null && fireSwordArt.TryHand(swordBody, false, out _))
                    measuredHeight = height = fireSwordArt.WorldHeight;
                if (bowBody != null) measuredHeight = height = StormInkFireBowAuthoring.Load().WorldHeight;
                if (paintedWand) measuredHeight = height + .15f;
                if (paintedPox) measuredHeight = height + .41f;
                float fall = painted != null && dead && frames == appearance.Death
                    ? (count > 1 ? frame / (float)(count - 1) : 1f) : 0f;
                if (fall > 0f) fallAngle = (mirror ? -78f : 78f) * fall;
                // Mirror the transform so the child contrast outline follows the same brush contour.
                a.Body.transform.localScale = new Vector3(mirror ? -height : height, height, height);
                a.Body.transform.localPosition = new Vector3(Mathf.Sin(fallAngle * Mathf.Deg2Rad) * height * fall * .45f, paintedWand ? -.02f : paintedPox ? -.04f : .08f, 0);
            }
            else
            {
                a.Body.sprite = neutral ? ShowcaseIdentityAssets.GetFactionSilhouette(-1) : ShowcaseSprites.Get("rune");
                a.Body.transform.localScale = new Vector3(.8f, 1.35f, 1);
                a.Body.transform.localPosition = new Vector3(0, .55f, 0);
            }
            // Overheads follow the look's body height (Appearance.WorldHeight), so a tall or short look keeps its cluster just above its head.
            float headY = appearance != null && frames != null && frames.Length > 0 ? .08f + (measuredHeight ?? appearance.WorldHeight) : 1.68f;
            // CONTRAST: the overhead cluster clears the figure's real top (hat, horns, a raised bow), not only the catalog height, so a bar never cuts the head's contour.
            if (appearance != null && frames != null && frames.Length > 0) headY = Mathf.Max(headY, HeadTop(appearance) + ActorOutlineModel.Width * 1.2f);
            float lift = headY - 1.68f;
            // Creatures keep their true colours while the death clip plays; the grey tint comes only once it has ended (or when there is no clip).
            bool clipPlaying = dead && IsCreature(kind) && appearance != null && appearance.Death != null && appearance.Death.Length > 0
                && Time.unscaledTime - a.DeadAt < ClipDuration(appearance.Death, appearance.DeathFPS, 0f);
            a.Body.color = dead && !clipPlaying ? new Color(.4f, .43f, .48f, .65f) : Color.Lerp(Color.white, color, isMob ? .14f : .22f);
            if (!dead && a.WishTintAmount > 0f) a.Body.color = Color.Lerp(a.Body.color, a.WishTint, a.WishTintAmount);
            a.Body.sortingOrder = order;
            a.Body.transform.localRotation = Quaternion.Euler(0, 0, fallAngle);
            // CONTRAST: the dark rim and the pale halo are gone; the adaptive outline (SpriteOutline) is synced at the end of Present.
            int factionArt = faction.HasValue ? (int)faction.Value : -1;
            a.Identity.Present(factionArt,kind,mob,order,!dead,lift);
            a.Shadow.sortingOrder = order - 8;
            // The ground shadow is sized to the look's foot box (the server's ground rectangle, the same one the footprint mark spans), in the actor's local units.
            AtmosphereShadows.ApplyActor(a.Shadow, x / 32f, y / 32f,
                footHx > 0f && footHy > 0f ? 2f * footHx * ArenaView.Scale / ActorVisualScale : 0f,
                footHx > 0f && footHy > 0f ? 2f * footHy * ArenaView.Scale * ArenaView.GroundSquash / ActorVisualScale : 0f, a.Cast);
            a.Cast.sortingOrder = order - 9;
            a.Halo.sortingOrder = order - 7;
            // The ground mark is the foot box when the server delivered one (ADR0024): its outline spans the box, drawn on the squashed
            // ground inside the 1.2 actor scale. Without a box (legacy or no table) it keeps the old fixed size.
            a.Halo.transform.localScale = footHx > 0f && footHy > 0f
                ? new Vector3(2f * footHx * ArenaView.Scale / (ActorVisualScale * FootprintOutline), 2f * footHy * ArenaView.Scale * ArenaView.GroundSquash / (ActorVisualScale * FootprintOutline), 1)
                : new Vector3(local ? 1.05f : .9f, .48f, 1);
            a.Halo.sprite = FactionIndicators.GetFootprint(factionArt);
            a.Halo.color = color;
            a.Halo.enabled = true;
            a.HpBack.sortingOrder = order + 4; a.Hp.sortingOrder = order + 5; a.Barrier.sortingOrder = order + 6;
            float barrierShare = dead || (isMob && !neutral) ? 0f : Mathf.Clamp01(barrierPct / 100f);
            bool elite = false; string label = null;
            if (kind == ActorKind.RegionalMob && mob.HasValue && ShowcaseIdentityAssets.TryGetMobInfo(mob.Value, out var mobInfo)) { elite = mobInfo.Elite; label = mobInfo.DisplayName; }
            // The hp bar is drawn from the legacy overhead layout (OverheadStack): size per actor kind, green above 50%, amber above 25%, red below,
            // the barrier as a segment after the fill. A mob shows it when hurt, an elite and a dummy always; an engaged mob counts as a dummy (always).
            // A quest giver and a dead actor have none.
            var theme = UiTheme.Current;
            var barFacts = new OverheadFacts
            {
                Kind = kind == ActorKind.TrainingDummy ? OverheadActorKind.Dummy
                    : kind == ActorKind.RegionalMob ? (elite ? OverheadActorKind.Elite : a.IndFacts.M.Engaged ? OverheadActorKind.Dummy : OverheadActorKind.Mob)
                    : OverheadActorKind.Player,
                Dead = dead || neutral, HpPct = Mathf.Clamp01(hp / 100f) * 100f, BarrierPct = barrierShare * 100f,
            };
            OverheadStack.Build(in barFacts, _barLayout);
            a.Hp.enabled = a.HpBack.enabled = a.Barrier.enabled = false;
            float barBase = 1.8f + lift - OverheadStack.ToUnits(33.2f);   // the bar's centre stays where it was; the layout's y is up from the feet
            for (int i = 0; i < _barLayout.Count; i++)
            {
                var e = _barLayout[i];
                SpriteRenderer r;
                switch (e.Kind)
                {
                    case OverheadKind.HpBack: r = a.HpBack; break;
                    case OverheadKind.HpFill:
                        r = a.Hp;
                        a.Hp.color = (Color)(e.Tone == OverheadTone.Green ? theme.Success : e.Tone == OverheadTone.Amber ? theme.Warning : theme.Danger);
                        break;
                    case OverheadKind.BarrierFill: r = a.Barrier; a.Barrier.color = (Color)theme.Info; break;
                    default: continue;
                }
                r.enabled = true;
                r.transform.localPosition = new Vector3(OverheadStack.ToUnits(e.X), barBase + OverheadStack.ToUnits(e.Y + e.H * .5f), 0);
                r.transform.localScale = new Vector3(OverheadStack.ToUnits(e.W), OverheadStack.ToUnits(e.H), 1);
            }
            int ringSteps = dead || (isMob && !neutral) ? 0 : Mathf.Clamp(barrierRingStep, (byte)0, (byte)8);
            for (int i = 0; i < a.BarrierRing.Length; i++)
            {
                var segment = a.BarrierRing[i];
                segment.enabled = false; // the barrier is the legacy bubble round the body (OverheadExtras), not ticks above the head
                float ringAngle = i * Mathf.PI / 4f - Mathf.PI / 2f;
                segment.transform.localPosition = new Vector3(Mathf.Cos(ringAngle) * .19f, 2.18f + lift + Mathf.Sin(ringAngle) * .19f, 0);
                segment.color = (Color)theme.Info;
                segment.sortingOrder = order + 10;
            }
            a.WeaponSupport.enabled = false;
            bool drawingString = false;
            if (a.PoxCenser != null) a.PoxCenser.enabled = false;
            bool bespokeCleaver = trackedWeapon && PresentPoxCleaver(a, StormInkActors.PoseRowForFacing(poseFacing), order, hiddenAlpha, dead);
            if (trackedWeapon && !bespokeCleaver && !paintedWand)
            {
                int poseRow = StormInkActors.PoseRowForFacing(poseFacing);
                var pose = StormInkWeaponMotion.Sample(faction.Value, weapon.Value, phase, weaponProgress, poseRow, reduced);
                pose = RegionalWeaponPose(a, pose, phase, weaponProgress, poseRow, reduced);
                if (useWeaponPhase && shieldBody == null && axeBody == null && swordBody == null)
                    a.Body.transform.localRotation = Quaternion.Euler(0, 0, pose.BodyLean);
                var authored = StormInkWeaponArt.Get(weapon.Value, poseRow);
                var sprite = authored != null ? authored : ShowcaseSprites.Get(StormInkWeaponMotion.KeyFor(weapon.Value));
                float scale = EquipmentScale(a);
                var signedScale = new Vector3(authored == null && pose.FlipFallback ? -scale : scale, scale, scale);
                var offset = SpriteSocket(sprite, StormInkWeaponArt.Grip(weapon.Value, poseRow), signedScale);

                bool leftHand = StormInkWeaponMotion.PrimaryHand(weapon.Value) == StormInkWeaponMotion.Hand.Left;
                bool registered = TryWeaponHand(a, leftHand, out var handUv);
                Vector3 grip = registered ? BodySocket(a.Body, handUv) : (Vector3)pose.Grip;
                float angle = weapon == WeaponId.Axe && axeBody != null && fireAxeArt != null && phase == StormInkWeaponMotion.Phase.Guard
                    ? fireAxeArt.GuardAxeAngle(pose.Angle, poseRow) : pose.Angle;
                if (swordBody != null) angle += fireSwordAngleOffset;
                Vector3 supportFromGrip = Vector3.zero;
                Vector3? drawPalm = null;
                if (axeBody == null && pose.HasSupport && useWeaponPhase)
                {
                    supportFromGrip = SpriteSocket(sprite, StormInkWeaponArt.Support(weapon.Value, poseRow), signedScale) - offset;
                    if (registered && TryWeaponHand(a, !leftHand, out var supportUv))
                    {
                        // Both palms can guide angle when close enough to the fixed weapon geometry.
                        // The painted primary palm remains the pivot and the sprite keeps its uniform size.
                        var supportPalm = BodySocket(a.Body, supportUv);
                        var palms = supportPalm - grip;
                        if (weapon == WeaponId.Bow && authored != null && !a.LocalCue)
                            drawPalm = supportPalm;
                        else if (palms.sqrMagnitude > .001f && Mathf.Abs(palms.magnitude - supportFromGrip.magnitude) < .10f)
                        {
                            float restAngle = Mathf.Atan2(supportFromGrip.y, supportFromGrip.x) * Mathf.Rad2Deg;
                            float palmsAngle = Mathf.Atan2(palms.y, palms.x) * Mathf.Rad2Deg;
                            angle += Mathf.Clamp(Mathf.DeltaAngle(angle + restAngle, palmsAngle), -18f, 18f);
                        }
                    }
                }

                var rotation = Quaternion.Euler(0, 0, angle);
                a.Weapon.sprite = sprite;
                a.Weapon.sortingOrder = order + (pose.Behind ? -2 : 2) +
                    fireSwordDepthOffset;
                a.Weapon.transform.localScale = signedScale;
                a.Weapon.transform.localRotation = rotation;
                a.Weapon.transform.localPosition = grip - rotation * offset;
                if (drawPalm.HasValue)
                {
                    a.Bowstring ??= StormInkBowstring.Create(a.Root.transform);
                    drawingString = a.Bowstring != null;
                    a.Bowstring?.Draw(a.Weapon, a.OutBody, poseRow, drawPalm.Value, hiddenAlpha);
                }
                if (axeBody == null && pose.HasSupport && useWeaponPhase && !registered)
                {
                    var support = a.WeaponSupport;
                    support.enabled = true;
                    support.sortingOrder = order + (pose.SupportBehind ? -1 : 3);
                    support.transform.localPosition = grip + rotation * supportFromGrip;
                    support.transform.localRotation = rotation;
                    var wrap = StormInkWeaponMotion.SupportWrap(faction.Value);
                    wrap.a = hiddenAlpha;
                    support.color = wrap;
                }
            }
            else if (!bespokeCleaver && !paintedWand)
            {
                a.Weapon.sortingOrder = order + 2;
                a.Weapon.transform.localPosition = new Vector3(Mathf.Cos(facing) * .42f, .6f - Mathf.Sin(facing) * .12f, 0);
                a.Weapon.sprite = weapon.HasValue
                    ? StormInkWeaponArt.Get(weapon.Value, StormInkActors.PoseRowForFacing(facing)) ??
                        ShowcaseSprites.Get(StormInkWeaponMotion.KeyFor(weapon.Value))
                    : null;
                a.Weapon.transform.localRotation = Quaternion.Euler(0, 0, -facing * Mathf.Rad2Deg - 90);
                a.Weapon.transform.localScale = Vector3.one * (weapon == WeaponId.Fists ? .35f : StormInkWeaponMotion.Size / ActorVisualScale);
            }
            if (!drawingString) a.Bowstring?.Clear(a.Weapon, a.OutBody);
            a.Weapon.color = weapon == WeaponId.Fists ? new Color(.9f, .86f, .72f, hiddenAlpha) : new Color(1f, 1f, 1f, hiddenAlpha);
            a.Weapon.enabled = !paintedWand && !hauntedFists && !dead && weapon.HasValue && hiddenAlpha > 0f;
            BindArtistSources(a, weapon, trackedWeapon ? poseFacing : facing,
                axeBody != null || swordBody != null || bowBody != null ? fireAxeArt : null, swordBody != null ? fireSwordArt : null,
                FireBowBodySource(bowBody));
            a.Status.enabled = false; // the generic status cue is replaced by the per-status glyphs (OverheadIndicators)
            a.Status.sortingOrder = order + 6; a.Status.color = color;
            a.Status.transform.localPosition = new Vector3(.55f, 1.5f, 0);
            int shown = 0;
            for (int bit = 0; bit < a.StatusIcons.Length; bit++)
            {
                var icon = a.StatusIcons[bit]; icon.enabled = false;
                if (!icon.enabled) continue;
                icon.transform.localPosition = new Vector3(-.45f + shown++ * .16f, 2, 0);
                icon.sortingOrder = order + 7;
            }
            a.Light.Configure(color, local ? .65f : .2f, .3f, 2.4f, .5f, false, true);
            a.Light.SetVisible(!dead && local);
            // A hidden fighter (Swarm) is not drawn for anyone else and faint for its owner (legacy PlayerView).
            if (hiddenAlpha > 0f && hiddenAlpha < 1f) { var bc = a.Body.color; bc.a *= hiddenAlpha; a.Body.color = bc; }
            bool hideAll = hiddenAlpha <= 0f; if (a.Root.activeSelf == hideAll) a.Root.SetActive(!hideAll);
            // CONTRAST: outlines last, once every source sprite, transform, enabled state and sort order is final for the frame.
            {
                bool darkGround = AtmosphereShadows.GroundIsDark(x / 32f, y / 32f);
                float bodyAlpha = ActorOutlineModel.StateAlpha(dead, hiddenAlpha > 0f ? hiddenAlpha : 1f);
                a.OutBody.Sync(order, bodyAlpha, darkGround);
                a.OutHp.Sync(a.HpBack.sortingOrder, 1f, darkGround);
                a.OutFoot.Sync(a.Halo.sortingOrder, 1f, darkGround);
                SyncPlates(a, darkGround);
            }
            // Geometry for the overhead cluster; PresentIndicators adds the facts only the caller has.
            var fx = a.IndFacts; float barY = 1.8f + lift;
            fx.Order = order; fx.HeadY = headY; fx.BarY = barY; fx.Dead = dead; fx.Local = local; fx.Mob = kind == ActorKind.RegionalMob || kind == ActorKind.TrainingDummy;
            fx.Elite = elite; fx.Dummy = kind == ActorKind.TrainingDummy; fx.HpPct = hp; fx.Effects = effects; fx.Name = fx.Mob ? (kind == ActorKind.TrainingDummy ? "Training dummy" : label) : null;
            fx.Reduced = reduced; fx.BarrierStep = ringSteps; fx.Sig = signature;
            a.IndFacts = fx;
            // SURFACE-FX HOOK: delivered actors only (see SurfaceFx); prints, ripples and footsteps come from here and nowhere else.
            // A fighter that is hidden from this viewer (not drawn at all) leaves no prints, ripples or footsteps either.
            if (Fx != null && !hideAll) Fx.Note(id, x, y, kind, local, dead, appearance != null ? appearance.WorldHeight : 1.6f, a.Gait.Walking, a.Gait.Phase, appearance != null && appearance.Walk != null ? appearance.Walk.Length / 8 : 0);
        }

        Art Create()
        {
            var a = new Art { Root = new GameObject("Observed actor") };
            a.Root.transform.SetParent(_parent, false);
            var t = a.Root.transform;
            // Scale character art and its overhead indicators together about the grounded actor position.
            t.localScale = new Vector3(ActorVisualScale, ActorVisualScale, 1f);
            a.Shadow = ShowcaseSprites.Create(t, "Contact shadow", "actorshadow", 0, Color.white, new Vector2(1.2f, .45f), new Vector3(.13f, -.04f), false);
            a.Cast = ShowcaseSprites.Create(t, "Cast shadow", "actorshadow", 0, Color.white, new Vector2(1.2f, .45f), new Vector3(.13f, -.04f), false);
            a.Halo = ShowcaseSprites.Create(t, "Faction footprint", "ring", 1, Color.white, Vector2.one, Vector3.zero, false);
            a.Body = ShowcaseSprites.Create(t, "Authored character", "rune", 2, Color.white, Vector2.one, Vector3.zero);
            a.Body.sharedMaterial = ShowcaseSprites.ActorLit;      // render pass (fx.actorlights): characters are lit by the analytic local lights, with a rim on the lit edge
            a.Identity = ShowcaseIdentityOwner.Configure(t);
            a.Identity.StandaloneFactionBadge = false; // The existing tested backed faction mark remains canonical here.
            a.HpBack = ShowcaseSprites.Create(t, "Health and barrier track", "bar", 4, (Color)UiTheme.Current.OutlineDark, new Vector2(1.04f, .145f), new Vector3(0, 1.8f), false);
            a.Hp = ShowcaseSprites.Create(t, "Confirmed health", "bar", 5, Color.white, Vector2.one, Vector3.zero, false);
            a.Barrier = ShowcaseSprites.Create(t, "Confirmed barrier", "bar", 6, ShowcasePalette.Gold, Vector2.one, Vector3.zero, false);
            a.BarrierRing = new SpriteRenderer[8];
            for (int i = 0; i < a.BarrierRing.Length; i++)
            {
                float angle = i * Mathf.PI / 4f - Mathf.PI / 2f;
                var segment = ShowcaseSprites.Create(t, "Barrier ring step " + (i + 1), "bar", 10,
                    ShowcasePalette.Gold, new Vector2(.16f, .045f),
                    new Vector3(Mathf.Cos(angle) * .19f, 2.18f + Mathf.Sin(angle) * .19f, 0), false);
                segment.transform.localRotation = Quaternion.Euler(0, 0, angle * Mathf.Rad2Deg);
                segment.enabled = false;
                a.BarrierRing[i] = segment;
            }
            a.Weapon = ShowcaseSprites.Create(t, "Equipped weapon cue", "bar", 3, Color.white, Vector2.one, Vector3.zero, false);
            a.WeaponSupport = ShowcaseSprites.Create(t, "Supporting hand on weapon", "handwrap", 4, Color.white,
                new Vector2(.16f, .13f), Vector3.zero, false);
            a.WeaponSupport.enabled = false;
            a.Status = ShowcaseSprites.Create(t, "Public status cue", "rune", 6, Color.white, new Vector2(.18f, .18f), new Vector3(.55f, 1.5f), false);
            a.StatusIcons = new SpriteRenderer[13];
            for(int bit=0;bit<a.StatusIcons.Length;bit++)
                a.StatusIcons[bit] = ShowcaseSprites.Create(t, "Public status bit " + bit, "sigil" + bit%5, 7,
                    bit<3 ? new Color(1,.8f,.4f) : bit<5 ? new Color(.5f,.85f,1) : bit<7 ? new Color(1,.45f,.35f) : new Color(.8f,.7f,1), new Vector2(.14f,.14f), Vector3.zero,false);
            a.Light = ShowcaseSprites.CreateLight(t, "Observed local accent", new Vector3(0, .6f, -1));
            a.Light.Configure(Color.white, .2f, .3f, 2.4f, .5f, false, true);
            a.Ind = new OverheadIndicators(t);
            // CONTRAST: one adaptive neutral outline per figure and per gated marker (SpriteOutline.cs); faction identity stays in shape and in the colour inside.
            a.OutBody = new SpriteOutline(a.Body, "Character contrast rim", ActorOutlineModel.Width, ActorOutlineModel.TargetRatio);
            a.OutBody.SetCompanion(a.Weapon, true);   // one line around the body and the weapon it holds: never a second stroke over the other part
            a.OutHp = new SpriteOutline(a.HpBack, "Health and barrier track outline", ActorOutlineModel.MarkerWidth, ActorOutlineModel.MarkerTargetRatio);
            a.OutFoot = new SpriteOutline(a.Halo, "Faction footprint outline", ActorOutlineModel.FootWidth, ActorOutlineModel.MarkerTargetRatio);
            a.OutFoot.SetCompanion(a.Body, false);   // the footprint's stroke is cut where the body stands on it
            // Every overhead plate, backer and ground ring (name, bounty, status, off-hand, Wish, stat marks, mark plate, signature, barrier ring steps, elite, lock, root, zone,
            // immunity and roam rings, identity badges) gets the same thin adaptive hairline; the glyph or text inside stays as drawn.
            var plates = new List<SpriteOutline>();
            foreach (var r in t.GetComponentsInChildren<SpriteRenderer>(true))
                if (NeedsStroke(r.gameObject.name)) plates.Add(new SpriteOutline(r, r.gameObject.name + " outline", ActorOutlineModel.MarkerWidth, ActorOutlineModel.MarkerTargetRatio));
            a.Plates = plates.ToArray();
            MoveOverheadToStrokes(t, a);
            return a;
        }
    }
}
