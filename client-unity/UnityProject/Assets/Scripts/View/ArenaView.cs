// Pure 2D presentation of delivered public facts. No collision, rules, physics or visibility inference.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using TMPro;
using Roguehalla.Net.Ui;

namespace Roguehalla.Client
{
    /// <summary>The MonoBehaviour that draws the arena: ground, walls, props, actors, effects and fog from the observed state, turning server coordinates into world space. It decides no outcomes.</summary>
    public sealed partial class ArenaView : MonoBehaviour
    {
        public const float Scale = 1f / 32f;
        public const float GroundSquash = .78f;
        public static Vector3 WorldPoint(float x, float y) { return new Vector3(x * Scale, -y * Scale * GroundSquash, 0); }
        public bool HasMap { get; private set; }
        /// <summary>Region and terrain lookup for the lighting, particle and music systems; see <see cref="RegionMap"/>.</summary>
        public RegionMap Regions { get; } = new RegionMap();
        /// <summary>"What is the character standing on" (snow, sand, mud, shallow water, bog...); see <see cref="SurfaceMap"/>.</summary>
        public SurfaceMap Surfaces { get { return _surfaces ?? (_surfaces = new SurfaceMap(Regions)); } }
        SurfaceMap _surfaces;
        /// <summary>Region at a Unity world position (the space <see cref="WorldPoint"/> returns); None when the map has no region plane.</summary>
        public RegionId RegionAt(Vector3 worldPos) { return Regions.RegionAt(worldPos); }
        /// <summary>Region under the local player (debounced); None until the first position is known.</summary>
        public RegionId LocalRegion { get { return Regions.LocalRegion; } }
        /// <summary>Raised (previous, current) when the local player enters another region.</summary>
        public event System.Action<RegionId, RegionId> LocalRegionChanged { add { Regions.LocalRegionChanged += value; } remove { Regions.LocalRegionChanged -= value; } }
        RegionGround _regionGround; RegionWalls _regionWalls; RegionProps _regionProps; LandmarkView _landmarks;
        ColiseumGateWalls _coliseumGates;   // P-COLISEUM-GATES-1: the gates drawn closed while the arena is locked
        /// <summary>Brightness multiplier for region ground and walls (they are lit by the same Light2D set as the actors).</summary>
        public const float RegionExposure = 1f;
        SpriteActorPresenter _actors;
        SurfaceFx _surfaceFx; // SURFACE-FX HOOK 1/3
        VisionFog _visionFog;     // fog of war layer (VisionFog.cs); presentation only
        Session _visionSession;
        // Diagnostics only (url flag vision.fadeDemo=1): every 6 s the nearest remote player is withheld for 2.5 s, as if the server stopped
        // sending it, so the exit and entry fades can be captured on screen. It goes through the real departure path; it never runs by default.
        bool _fadeDemo; uint _demoId; float _demoNext = 8f, _demoUntil;
        HitboxOverlay _overlay;
        HitZonePresenter _hitZones;
        WeaponRangePresenter _weaponRange;
        /// <summary>Set by the caller while the Tab map, a minimap peek, a pending teleport or a pending death is up: legacy hides the weapon range guide then (RaidScene.ts:1637-1638).</summary>
        public bool WeaponRangeSuppressed;
        WorldMarks _worldMarks; CampRespawnLabels _campLabels; System.Func<float, float, bool> _markVisible;
        WorldIndicators _worldIndicators; HurtVignette _vignette;
        readonly CampAttack _campAttack = new CampAttack(); readonly List<CampAttackMob> _campMobs = new List<CampAttackMob>(); MapBlob _campAttackMap;   // camp patience outlines show only while attacked (P-UNITY-CAMP-PATIENCE-SHOW-1)
        CampPatienceOutlines _patience; QuestCarrierFlags _carrierFlags; ActorPoint _carrierActorPoint; readonly List<CarrierFlagRow> _carrierRows = new List<CarrierFlagRow>();
        /// <summary>Read-only counters for the scenario probe (diagnostics): confirmed Hit fx seen, those on the local fighter, the last amount and passive flag.</summary>
        public static int DiagHitFx, DiagHitFxOnMe; public static float DiagLastAmountOnMe; public static bool DiagLastPassiveOnMe;
        /// <summary>Scenario probe counters (diagnostics): Windup fx received and drawn, critical hits, remote fighters carrying a top-stat mark.</summary>
        public static int DiagWindupFx, DiagWindupDrawn, DiagCritFx, DiagTopStat;
        public float DiagVignetteStrength => _vignette != null ? _vignette.Strength : 0f;
        public int DiagFlashes => _flashes.Count;
        uint _hoverId; uint? _huntTarget; float _selfMaxHp = 100f, _localX, _localY; bool _haveLocal; int _wishState; byte _wishAbility;
        /// <summary>Instant lock ring for a wand click (legacy LockDisplay); MatchClient notes orders, Present draws Shown.</summary>
        public readonly LockDisplay LockDisplay = new LockDisplay();
        uint? _lockForNpc; InventoryView? _inventory; uint _localId; int _numberLane;
        WireArchetype[] _archetypes = System.Array.Empty<WireArchetype>();
        /// <summary>True when the <c>?hitboxes=1</c> debug overlay exists (never otherwise).</summary>
        public bool HitboxOverlayActive { get { return _overlay != null; } }
        /// <summary>The hitbox archetype table of the joined match (empty before a Welcome, or from a server that sends none).</summary>
        public WireArchetype[] Archetypes { get { return _archetypes; } }
        string ArchetypeKey(byte id)
        {
            var row = WireArchetype.Find(_archetypes, id);
            return row == null ? null : row.Key;
        }
        /// <summary>Foot box half-extents (sim units) of an archetype, 0 for a circle hull, an unknown id or no table.</summary>
        float FootHx(byte id) { var r = WireArchetype.Find(_archetypes, id); return r != null && r.Hull == 1 ? r.Fx : 0f; }
        float FootHy(byte id) { var r = WireArchetype.Find(_archetypes, id); return r != null && r.Hull == 1 ? r.Fy : 0f; }
        CombatSparks _sparks; uint _sparkRng = 2463534242u; float _nextTrail; Vector3 _camCenter; Vector2 _camHalf = new Vector2(12, 9);     // render pass: trails, impact sparks, hit lights
        ConfirmedWishPresenter _wishes;
        WishFxPresenter _wishFx;
        FrostWallPresenter _frostWall; FrostWishKind _frostWish; Color32 _frostTint;
        bool _collectWishNotes;
        double _sourceTimeMs;
        ShowcaseCatalog _catalog;
        Transform _mapRoot, _propsRoot, _fxRoot;
        readonly List<SpriteRenderer> _props = new List<SpriteRenderer>();
        readonly List<ShowcasePointLight> _lamps = new List<ShowcasePointLight>();
        readonly List<SpriteRenderer> _lampGlows = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> _motes = new List<SpriteRenderer>();
        readonly List<SpriteRenderer> _fog = new List<SpriteRenderer>();
        readonly List<InterpActor> _latestMembership = new List<InterpActor>();
        readonly List<InterpActor> _latestNpcs = new List<InterpActor>();
        readonly HashSet<uint> _membership = new HashSet<uint>();
        readonly List<PendingFx> _pendingFx = new List<PendingFx>(64);
        // Remote-player cues held until render time reaches their tick. Bounded in count, lead and wall time; past any
        // bound a cue is shown at once (the previous on-arrival behaviour).
        const int MaxScheduledFx = 48;
        const double MaxFxLeadMs = 200; // RenderClock's 150 ms maximum delay plus one 50 ms tick
        const float MaxFxHoldSeconds = .25f;
        readonly List<PendingFx> _scheduledFx = new List<PendingFx>(MaxScheduledFx);
        double _renderMs;
        bool _renderReady;
        bool _hasMembership;
        readonly List<Flash> _flashes = new List<Flash>();
        readonly Stack<Flash> _flashPool = new Stack<Flash>();
        Volume _volume;
        Light2D _ambient;
        RegionAtmosphere _atmos;
        Color _fogColor = new Color(.22f, .32f, .48f, .075f);
        /// <summary>Region atmosphere driver (light, grading, haze, particles, accent lights, shadows, audio).</summary>
        public RegionAtmosphere Atmosphere { get { return _atmos; } }
        void ApplyHaze(Color c) { _fogColor = c; for (int i = 0; i < _fog.Count; i++) if (_fog[i] != null) _fog[i].color = c; }
        int _used;
        Vector3 _lastFollow;
        float _smoothX, _smoothY;
        bool _smoothValid;
        /// <summary>Legacy CameraFollow constants: tau 35 ms, cut over 256 u or after a gap of more than 250 ms.</summary>
        const float FollowTauSeconds = .035f, FollowCutUnits = 256f, FollowMaxDt = .25f;
        bool _hasFollow;
        /// <summary>Observed actors currently presented (perf recorder context).</summary>
        public int ObservedActorCount { get { return _actors != null ? _actors.ActiveCount : 0; } }
        public bool IsObserved(uint id) { return _actors != null && _actors.Contains(id); }
        /// <summary>The local player's drawn position in sim units (perf recorder context); false before the first local frame.</summary>
        public bool TryLocalPosition(out float x, out float y) { x = _localX; y = _localY; return _haveLocal; }
        public bool TrySoundContext(uint id, out WeaponId weapon, out FactionId faction, out ActorKind kind, out MobTypeId? type)
        {
            if (_actors != null) return _actors.TrySoundContext(id,out weapon,out faction,out kind,out type);
            weapon=default; faction=default; kind=default; type=null; return false;
        }
        sealed class Flash { public SpriteRenderer Sprite, Plate; public TMP_Text Text; public uint Actor; public float Start, Duration; public Vector3 Position; public Color Color; public float Size; public Color TextColor; public float LaneX; public int AlphaByte = 255; }
        struct PendingFx { public FxKind Kind; public uint Subject; public float Amount; public double DueMs; public float QueuedAt; public float X, Y, A, CreatedAt; public byte Weapon; public uint By; public bool Passive, Crit; public byte SigKind; }

        void Awake()
        {
            ShowcaseSprites.Initialize();
            _catalog = Resources.Load<ShowcaseCatalog>("Showcase/Catalog");
            _actors = new SpriteActorPresenter(transform, _catalog);
            _actors.Surfaces = Surfaces;   // name colours follow the ground under a name (P-UNITY-NAME-LEGIBLE-1)
            _visionFog = new VisionFog(transform); _actors.Delivered = _membership; _actors.OnDeparted = _visionFog.AuditDeparture; _actors.OnArrived = _visionFog.AuditArrival;
            _visionFog.AuditLog = Application.absoluteURL != null && Application.absoluteURL.Contains("vision.audit=1");
            _visionFog.DebugRadiusTiles = DebugRadiusFlag(Application.absoluteURL);
            _fadeDemo = Application.absoluteURL != null && Application.absoluteURL.Contains("vision.fadeDemo=1");
            if (HitboxOverlay.Enabled) _overlay = new HitboxOverlay(Child("Hitbox overlay"), _actors);
            _mapRoot = Child("Authoritative map artwork"); _propsRoot = Child("Observed collections"); _fxRoot = Child("Confirmed effects");
            _hitZones = new HitZonePresenter(Child("Melee hit zones"), id => _actors.Contains(id), UrlSearch());
            _hitZones.Position = (uint id, out float x, out float y) => { if (_actors.TryPosition(id, out var w)) { x = w.x / Scale; y = -w.y / (Scale * GroundSquash); return true; } x = y = 0; return false; };
            _weaponRange = new WeaponRangePresenter(Child("Weapon range guide"));
            _worldMarks = new WorldMarks(Child("World markers"), _catalog); _campLabels = new CampRespawnLabels(Child("Camp respawn labels"));
            _campLabels.NameRectSource = into => { if (_actors != null) _actors.FighterNameRects(into); };
            _worldIndicators = new WorldIndicators(Child("World indicators")); _vignette = new HurtVignette();
            _patience = new CampPatienceOutlines(Child("Camp patience outlines")); _carrierFlags = new QuestCarrierFlags(Child("Quest carrier flags"));
            // the drawn actors: the presenter that Present()s every observed fighter, the local player included
            _carrierActorPoint = (uint id, out float x, out float y) => { if (_actors.TryPosition(id, out var w)) { x = w.x / Scale; y = -w.y / (Scale * GroundSquash); return true; } x = y = 0; return false; };
            _wishes = new ConfirmedWishPresenter(_fxRoot, _actors);
            _wishFx = new WishFxPresenter(Child("Wish effects"), _actors);
            _frostWall = new FrostWallPresenter(Child("Frost walls")); _frostTint = ShowcasePalette.Faction(WishFxModel.FactionIndex(AbilityId.Freeze));
            _sparks = new CombatSparks(Child("Combat sparks"));
            var global = Child("Ambient light");
            _ambient = global.gameObject.AddComponent<Light2D>(); _ambient.lightType = Light2D.LightType.Global;
            _ambient.color = new Color(.81f, .83f, .9f); _ambient.intensity = .86f;
            _volume = gameObject.AddComponent<Volume>(); _volume.isGlobal = true; _volume.priority = 2;
            var profile = Resources.Load<VolumeProfile>("Showcase/Atmosphere");
            if (profile != null) _volume.sharedProfile = profile;
            AtmosphereQuality.Initialize();
            _atmos = gameObject.AddComponent<RegionAtmosphere>();
            for (int i = 0; i < 32; i++) _motes.Add(ShowcaseSprites.Create(transform, "Ambient dust", "glow", 1500,
                new Color(1.6f,1.2f,.6f,.35f), new Vector2(.07f,.07f), Vector3.zero, false));
            for(int i=0;i<3;i++) _fog.Add(ShowcaseSprites.Create(transform,"Layered ambient haze","glow",29990,
                new Color(.22f,.32f,.48f,.075f),new Vector2(20,5),Vector3.zero,false));
            _atmos.Bind(this, _ambient, _volume, ApplyHaze);
            _inspector = CreateInspector();
            _surfaceFx = SurfaceFx.Create(this); _actors.Fx = _surfaceFx; // SURFACE-FX HOOK 2/3
            if (ShowcaseSettings.Current != null) ShowcaseSettings.Current.Changed += ApplyQuality;
            ApplyQuality();
        }
        InspectorCardView _inspector;
        /// <summary>The enemy inspector card (legacy opponent inspector): a fixed top-right panel on its own overlay canvas, display only.</summary>
        public InspectorCardView Inspector { get { return _inspector; } }
        InspectorCardView CreateInspector()
        {
            var go = new GameObject("InspectorCardCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = -1;   // below MatchUI and its modals, above the hurt vignette (-10)
            var scaler = go.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            return new InspectorCardView(go.transform);
        }
        static bool? _indicatorDemo;
        /// <summary>DIAGNOSTICS ONLY (<c>?indicators.demo=1</c>, never on by default): drives the local actor's overhead cluster with TEST values (a bounty, cycling status bits, the lock ring) so every glyph can be captured without staging a fight.</summary>
        static bool IndicatorDemo { get { if (!_indicatorDemo.HasValue) _indicatorDemo = UrlSearch().Contains("indicators.demo=1"); return _indicatorDemo.Value; } }
        void DemoIndicators(uint id, WeaponId? second)
        {
            ushort[] sets = { Indicators.Stunned, Indicators.Silenced | Indicators.AttackSlowed, Indicators.Slowed | Indicators.Dot, Indicators.Rooted | Indicators.AntiHeal,
                Indicators.Haste | Indicators.Casting | Indicators.LastStand, Indicators.Zoned | Indicators.Untargetable, 0 };
            _actors.PresentIndicators(id, 90, true, second ?? WeaponId.Bow, true, sets[(int)(Time.unscaledTime / 2f) % sets.Length]);
        }
        static string UrlSearch() { string url = Application.absoluteURL ?? ""; int q = url.IndexOf('?'); return q >= 0 ? url.Substring(q) : ""; }
        Transform Child(string name) { var go = new GameObject(name); go.transform.SetParent(transform, false); return go.transform; }

        public void Present(List<InterpActor> remote, SelfView me, LocalMotion motion, float aimAngle)
        {
            var pmActors = PerfMarks.Begin();
            _actors.Begin(); ActorField.Begin();      // render pass: presented actors for the surface shaders
            if (_visionSession != null && _visionSession.Map != null) _actors.SetCamps(_visionSession.Map.Camps, _visionSession.Collections.Camps);   // camp packs (P-UNITY-HUD-VISUAL-FIX-4)
            uint mine = me != null ? me.Id : 0; _localId = mine;
            _huntTarget = me != null ? me.HuntTarget : null; _haveLocal = me != null;
            if (me != null) { _selfMaxHp = me.MaxHp; _wishState = WishState(me); _wishAbility = me.Wish.HasValue ? (byte)me.Wish.Value.Ability : (byte)0; }
            uint? lockTarget = me != null && me.OwnerCombat.HasValue && me.OwnerCombat.Value.Lock.Kind == OwnerLockKind.Held ? me.OwnerCombat.Value.Lock.Target : null; 
            // Legacy LockDisplay: an ordered lock shows at once for 500 ms, then the server's synced lock.
            uint shownLock = LockDisplay.Shown(lockTarget ?? 0u, Time.realtimeSinceStartupAsDouble * 1000.0);
            lockTarget = shownLock == 0 ? (uint?)null : shownLock; _lockForNpc = lockTarget;
            _wishFx.BeginFrame(); _wishFx.SetOwner(mine);
            if (_inspector != null)
            {
                uint inspected = _hoverId != 0 ? _hoverId : shownLock;
                if (_hasMembership && !_membership.Contains(inspected)) _inspector.Hide();   // C-OBS: left vision in the newest snapshot
                else _inspector.Show(remote, _hoverId, shownLock, mine, me == null || me.Status == ActorStatus.Alive);
            }
            foreach (var a in remote)
            {
                if (a.Id == mine) continue;
                if (_hasMembership && !_membership.Contains(a.Id)) continue;
                // NPC observer handles may renew on every observation. Never infer continuity
                // from appearance or position; draw their latest typed public facts instead.
                if (_hasMembership && a.Kind != ActorKind.Player) continue;
                _actors.Present(a.Id, a.X, a.Y, a.Facing, a.HpPct, a.Faction, a.Status, a.Weapon, a.Kind, a.MobType, a.Effects, a.SourceSig, false, a.BarrierPct, a.BarrierRingStep, ArchetypeKey(a.Archetype), FootHx(a.Archetype), FootHy(a.Archetype));
                RemoteIndicators(a, lockTarget.HasValue && lockTarget.Value == a.Id);
                _overlay?.NoteActor(a.Id, a.Archetype, (byte)a.Kind); _hitZones?.NoteActor(a.Id, a.Archetype);
                _wishFx.NoteActor(a.Id, a.Kind, a.Effects, a.Status != ActorStatus.Dead && a.Status != ActorStatus.Mia);
                if (_regionProps != null) _regionProps.NoteActor(WorldPoint(a.X, a.Y));
                if (_landmarks != null) _landmarks.NoteOtherActor(WorldPoint(a.X, a.Y));
                ActorField.Note(WorldPoint(a.X, a.Y));
            }
            if (_hasMembership) { _collectWishNotes = true; PresentLatestNpcs(); _collectWishNotes = false; }
            if (me != null && (!_hasMembership || _membership.Contains(me.Id)))
            {
                bool initialized = motion != null && motion.Initialized;
                OwnerVitalBars.Percent(me, out float healthPercent, out float barrierPercent);
                float mx = initialized ? motion.DrawnX : me.X, my = initialized ? motion.DrawnY : me.Y;
                _actors.Present(me.Id, mx, my, aimAngle, healthPercent,
                    me.Faction, me.Status, me.Weapon, ActorKind.Player, null, me.Effects, me.SelfSig, true, barrierPercent, me.BarrierRingStep, ArchetypeKey(me.Archetype), FootHx(me.Archetype), FootHy(me.Archetype));
                var inv = _inventory; WeaponId? second = Indicators.SecondaryWeapon(inv.HasValue, inv.HasValue ? inv.Value.ActiveSlot : (byte)0, inv.HasValue ? inv.Value.Slot0 : null, inv.HasValue ? inv.Value.Slot1 : null);
                if (IndicatorDemo) DemoIndicators(me.Id, second);
                else { _actors.PresentMarks(me.Id, LegacyFacts.Of(me, LegacyFacts.NameOf(_visionSession, me.Id))); _actors.PresentIndicators(me.Id, me.Bounty, second.HasValue, second ?? WeaponId.Fists, false, -1, false, false, _wishState, _wishAbility); }
                _localX = mx; _localY = my;
                _overlay?.NoteActor(me.Id, me.Archetype, (byte)ActorKind.Player); _hitZones?.NoteActor(me.Id, me.Archetype);
                _wishFx.NoteActor(me.Id, ActorKind.Player, me.Effects, me.Status == ActorStatus.Alive);
                Regions.UpdateLocal(mx, my, Time.unscaledTime);
                if (_regionProps != null) _regionProps.NoteActor(WorldPoint(mx, my));
                if (_landmarks != null) _landmarks.NoteActor(WorldPoint(mx, my));
                ActorField.Note(WorldPoint(mx, my));
            }
            _actors.End(); PerfMarks.End(PerfMarks.Sec.Actors, pmActors);
            if (_flushPendingInPresent) FlushPendingFx();      // cues that waited for their subject (RetainObservedActors with presentFollows)
            var pm = PerfMarks.Begin();
            if (_surfaceFx != null) _surfaceFx.Tick(Time.unscaledDeltaTime); // SURFACE-FX HOOK 3/3
            PerfMarks.End(PerfMarks.Sec.SurfaceFx, pm); pm = PerfMarks.Begin();
            _actors.StepFades(); TickVision();
            PerfMarks.End(PerfMarks.Sec.Vision, pm); pm = PerfMarks.Begin();
            if (_regionProps != null) _regionProps.Tick(Time.unscaledDeltaTime);
            if (_landmarks != null) _landmarks.Tick(Time.unscaledDeltaTime);
            PerfMarks.End(PerfMarks.Sec.Props, pm); pm = PerfMarks.Begin();
            ReleaseScheduledFx();
            UpdateEffects();
            PerfMarks.End(PerfMarks.Sec.Effects, pm); pm = PerfMarks.Begin();
            _wishes.Tick(_sourceTimeMs);
            _wishFx.Tick(_sourceTimeMs, Time.unscaledDeltaTime);
            _frostWall.Tick(_sourceTimeMs, Time.unscaledTime, ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion);
            PerfMarks.End(PerfMarks.Sec.WishFx, pm);
            _hitZones?.Tick(Time.unscaledTime, ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion);
            TickWeaponRange(me, motion, aimAngle);
        }

        // Legacy WeaponRangeView/RaidScene: the local fighter's own guide, at its drawn (predicted) position; the aim is the cursor angle the caller passes.
        void TickWeaponRange(SelfView me, LocalMotion motion, float aimAngle)
        {
            if (_weaponRange == null) return;
            var s = _visionSession;
            bool member = me != null && (!_hasMembership || _membership.Contains(me.Id));
            var row = member ? LegacyFacts.WeaponRow(me.Weapon) : null;
            bool initialized = motion != null && motion.Initialized;
            float x = member ? (initialized ? motion.DrawnX : me.X) : 0f, y = member ? (initialized ? motion.DrawnY : me.Y) : 0f;
            bool active = s != null && s.State == SessionState.InMatch && s.Phase == MatchPhase.Active && s.LastResult == null;
            if (!member || !WeaponRangePresenter.ShouldShow(active, me.Status == ActorStatus.Alive, row != null, WeaponRangeSuppressed, x, y)) { _weaponRange.Hide(); return; }
            _weaponRange.Show(row, WeaponRangePresenter.BodyHalfWidth(_archetypes, me.Archetype), aimAngle, LegacyFacts.IsBallWeapon(me.Weapon), x, y);
        }

        // DIAGNOSTICS ONLY (UI lab, ?uilab=1): the two things a Session binding gives the match that a serverless gallery has to ask for. Not called in normal play.
        public void DiagSetExitFades(bool allowed) { _actors.ExitFadesAllowed = allowed; }
        public void DiagDeathMark(float simX, float simY) { _worldIndicators.AddDeath(simX, simY, Time.unscaledTime, MarkVisible); }

        /// <summary>Latest membership wins over delayed interpolation, before the frame is rendered.</summary>
        /// <param name="presentFollows">The caller runs <see cref="Present"/> in this same frame (the match loop does): the creatures,
        /// effects and fades are then drawn once, by Present, instead of twice (see <see cref="RetainObservedActors(List{InterpActor}, SelfView, bool)"/>).</param>
        public void RefreshVisibility(Session session, bool presentFollows = false)
        {
            _archetypes = session != null ? session.Archetypes : System.Array.Empty<WireArchetype>();
            _frostWish = session != null && session.Welcome != null ? session.Welcome.Hud.FrostWish : FrostWishKind.Freeze; _wishFx.FrostWish = _frostWish;
            _hitZones?.SetTable(_archetypes); LegacyFacts.SyncWeapons(session != null ? session.Weapons : null);
            if (_atmos != null) _atmos.SetMatchActive(session != null && session.Phase == MatchPhase.Active);
            _sourceTimeMs = session != null ? session.ServerTimeMs : 0;
            BindVisionSession(session);
            _renderReady = session != null && session.Clock.Ready;
            _renderMs = _renderReady ? session.Clock.RenderMs : 0;
            _latestMembership.Clear();
            if(session!=null && session.Snapshots.Count>0)
            {
                session.Snapshots.Sample(session.Snapshots.NewestMs,_latestMembership);
                RetainObservedActors(_latestMembership,session.Me,presentFollows);
            }
            else RetainObservedActors(_latestMembership,null,presentFollows); // Welcome/resume resets the observation timeline.
        }
        public void RetainObservedActors(List<InterpActor> current,SelfView me) { RetainObservedActors(current, me, false); }
        /// <summary>
        /// With <paramref name="presentFollows"/> the latest creatures, the pending cues, the effects and the fades are left to the
        /// <see cref="Present"/> that follows in the same frame (it draws all of them again anyway): presenting every creature twice a
        /// frame cost more than all the other presenters together. The pending cues are flushed by Present right after it has drawn
        /// the creatures, so a cue still finds its newly delivered subject before the frame is rendered.
        /// </summary>
        public void RetainObservedActors(List<InterpActor> current,SelfView me,bool presentFollows)
        {
            _membership.Clear(); _latestNpcs.Clear();
            if (_fadeDemo) UpdateFadeDemo(current, me);
            foreach(var actor in current)
            {
                if (_demoId != 0 && actor.Id == _demoId) continue;
                _membership.Add(actor.Id);
                if(actor.Kind != ActorKind.Player) _latestNpcs.Add(actor);
            }
            if(me!=null) _membership.Add(me.Id);
            _hasMembership=true; _actors.Retain(_membership);
            _wishes.Retain(_membership);
            _wishFx.Retain(_membership);
            if (presentFollows) { _flushPendingInPresent = true; return; }
            // This also runs after Poll/Present, before rendering, so new observer handles
            // are visible immediately and removed handles leave no cosmetic residue.
            PresentLatestNpcs();
            FlushPendingFx();
            UpdateEffects();
            _actors.StepFades();
        }
        bool _flushPendingInPresent;
        void FlushPendingFx()
        {
            _flushPendingInPresent = false;
            foreach(var fx in _pendingFx) PresentFx(fx);
            _pendingFx.Clear();
        }
        void UpdateFadeDemo(List<InterpActor> current, SelfView me)
        {
            float now = Time.unscaledTime;
            if (_demoId != 0 && now >= _demoUntil) { _demoId = 0; _demoNext = now + 3.5f; }
            if (_demoId != 0 || now < _demoNext || me == null || !_actors.ExitFadesAllowed) return;
            float best = float.MaxValue; uint pick = 0;
            foreach (var a in current)
            {
                if (a.Kind != ActorKind.Player || a.Status != ActorStatus.Alive) continue;
                float dx = a.X - me.X, dy = a.Y - me.Y, d = dx * dx + dy * dy;
                if (d < best && d < 12f * 32 * 12f * 32) { best = d; pick = a.Id; }
            }
            if (pick != 0) { _demoId = pick; _demoUntil = now + 2.5f; }
        }
        static float DebugRadiusFlag(string url)
        {
            const string key = "vision.debugRadius=";
            int i = url == null ? -1 : url.IndexOf(key, System.StringComparison.Ordinal);
            if (i < 0) return 0f;
            int j = i + key.Length, k = j;
            while (k < url.Length && (char.IsDigit(url[k]) || url[k] == '.')) k++;
            return float.TryParse(url.Substring(j, k - j), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }
        // ---- vision hooks: exit/entry fades and the fog layer (presentation only) ----
        void BindVisionSession(Session session)
        {
            if (!ReferenceEquals(session, _visionSession))
            {
                if (_visionSession != null) _visionSession.EventsReceived -= OnVisionEvents;
                _visionSession = session; _campAttack.Clear();
                if (_visionSession != null) _visionSession.EventsReceived += OnVisionEvents;
            }
            // A departing actor fades only while a raid is running; elsewhere (lobby, planning, results) it just goes.
            _actors.ExitFadesAllowed = session != null && session.State == SessionState.InMatch && session.Phase == MatchPhase.Active;
        }
        void OnVisionEvents(ServerMsg.Events message)
        {
            if (message == null) return;
            foreach (var ev in message.List)
            {
                if (ev.Kind != EventKind.Death) continue;
                _actors.NoteDeath(ev.Id);
                // The death X lies where a delivered player was last drawn (legacy FxView.decal); creatures leave none.
                if (_actors.TryPosition(ev.Id, out var pos) && _actors.TrySoundContext(ev.Id, out _, out _, out var kind, out _) && kind == ActorKind.Player)
                    _worldIndicators.AddDeath(pos.x / Scale, -pos.y / (Scale * GroundSquash), Time.unscaledTime, MarkVisible);
            }
        }
        /// <summary>CONTRAST LAB ONLY: drives the fog of war with the local player parked at a server position (the lab has no session). Never called by the game.</summary>
        public void LabFogTick(bool on, float serverX, float serverY) { _visionFog.Tick(on, serverX, serverY, 0, _actors); }
        void TickVision()
        {
            var s = _visionSession;
            bool on = HasMap && s != null && s.State == SessionState.InMatch && s.Phase == MatchPhase.Active && !s.Training
                && s.Me != null && s.Me.Status == ActorStatus.Alive;
            _visionFog.Tick(on, on ? s.Me.X : 0, on ? s.Me.Y : 0, s != null ? s.ServerTimeMs : 0, _actors);
        }
        /// <summary>One remote fighter's overhead facts: its off-hand weapon (fists when empty), Wish-ready icon, name, stat marks, mark plate, immunity, roam and engaged flags.</summary>
        void RemoteIndicators(InterpActor a, bool locked)
        {
            _actors.PresentMarks(a.Id, LegacyFacts.Of(a, a.Kind == ActorKind.Player ? LegacyFacts.NameOf(_visionSession, a.Id) : null));
            bool player = a.Kind == ActorKind.Player; if (player && a.TopStat1.HasValue) DiagTopStat++;
            _actors.PresentIndicators(a.Id, a.Bounty, player, a.OffhandWeapon ?? WeaponId.Fists, locked, -1, a.Id == _hoverId, _huntTarget.HasValue && _huntTarget.Value == a.Id,
                LegacyFacts.RemoteWish(a), a.Faction.HasValue ? (byte)Indicators.WishOf(a.Faction.Value) : (byte)0);
        }
        void PresentLatestNpcs()
        {
            foreach(var a in _latestNpcs)
            {
                _actors.Present(a.Id,a.X,a.Y,a.Facing,a.HpPct,a.Faction,a.Status,a.Weapon,a.Kind,a.MobType,a.Effects,a.SourceSig,false,a.BarrierPct,a.BarrierRingStep,ArchetypeKey(a.Archetype), FootHx(a.Archetype), FootHy(a.Archetype));
                RemoteIndicators(a, _lockForNpc.HasValue && _lockForNpc.Value == a.Id);
                _overlay?.NoteActor(a.Id, a.Archetype, (byte)a.Kind); _hitZones?.NoteActor(a.Id, a.Archetype);
                if (_collectWishNotes) _wishFx.NoteActor(a.Id, a.Kind, a.Effects, a.Status != ActorStatus.Dead && a.Status != ActorStatus.Mia);
                if (_regionProps != null) _regionProps.NoteActor(WorldPoint(a.X, a.Y));
                if (_landmarks != null) _landmarks.NoteOtherActor(WorldPoint(a.X, a.Y));
                ActorField.Note(WorldPoint(a.X, a.Y));
            }
        }

        public void PresentCollections(CollectionsView collections)
        {
            var pmCollections = PerfMarks.Begin();
            _used = 0; _inventory = collections != null ? collections.Inventory : null;
            _frostWall.PresentWalls(collections != null ? collections.Fields : null, _sourceTimeMs);
            if (_coliseumGates != null) _coliseumGates.SetLocked(collections != null && collections.Arena.HasValue && collections.Arena.Value.GatesLocked);
            if (collections == null) _wishFx.PresentAreas(null);
            if (collections != null)
            {
                // Zone, cloud and trail rows: drawn by the Wish presenter at exactly the delivered extents.
                _wishFx.PresentAreas(collections.Fields);
                foreach (var p in collections.Props)
                {
                    var position = WorldPoint(p.X, p.Y); int order = 2000 + Mathf.RoundToInt(p.Y * .5f);
                    _overlay?.NoteProp((byte)p.Kind, p.Weapon.HasValue ? (int)p.Weapon.Value : -1, p.X, p.Y, p.Open, p.Focused);
                    Prop("shadow", position + new Vector3(.1f, -.03f), new Vector2(.9f, .35f), Color.white, order - 3);
                    if (p.Kind == PropAppearance.Ground)
                    {
                        // Ground weapons are the protocol's five carried weapon ids. Unsupported or absent
                        // values get a neutral stone cue and never borrow a known weapon glyph.
                        int weapon = p.Weapon.HasValue ? (int)p.Weapon.Value : -1;
                        string glyph = weapon >= 0 && weapon <= 4 ? "weapon" + weapon : "stone";
                        Color tint = weapon >= 0 && weapon <= 4 ? ShowcasePalette.Gold : ShowcasePalette.MutedText;
                        Prop(glyph, position + new Vector3(0, .22f), new Vector2(.48f, .48f), tint, order);
                    }
                    else if (p.Kind == PropAppearance.WindEmblem)
                    {
                        // A ring, rune and small spark form a wind marker distinct from both weapons and cannon.
                        Prop("ring", position, new Vector2(.72f, .58f), ShowcasePalette.Faction(3), order);
                        Prop("rune", position + new Vector3(0, .13f), new Vector2(.42f, .42f), ShowcasePalette.Text, order + 1);
                        Prop("spark", position + new Vector3(.25f, .2f), new Vector2(.2f, .28f), ShowcasePalette.MutedText, order + 2);
                    }
                    else if (p.Kind == PropAppearance.Cannon)
                    {
                        // Neutral bar, stone and ring shapes as a side-on cannon: a small stone carriage base, a barrel raised 25 degrees
                        // and two equal wheels at the foot.
                        Prop("stone", position + new Vector3(-.03f, .05f), new Vector2(.34f, .16f), ShowcasePalette.MutedText, order);
                        var barrel = Prop("bar", position + new Vector3(.2f, .17f), new Vector2(.7f, .14f), ShowcasePalette.Text, order + 1);
                        barrel.transform.rotation = Quaternion.Euler(0f, 0f, 25f);
                        Prop("ring", position + new Vector3(-.22f, -.1f), new Vector2(.3f, .3f), ShowcasePalette.PanelStroke, order + 2);
                        Prop("ring", position + new Vector3(.22f, -.1f), new Vector2(.3f, .3f), ShowcasePalette.PanelStroke, order + 2);
                    }
                    else
                    {
                        string sprite = p.Kind == PropAppearance.Chest ? "chest" : p.Kind == PropAppearance.Body || p.Kind == PropAppearance.Corpse ? "stone" : "rune";
                        var art = Prop(sprite, position + new Vector3(0, .22f), new Vector2(.78f, p.Kind == PropAppearance.Body ? .3f : .6f), Color.white, order);
                        if (p.Kind == PropAppearance.Corpse)
                        {
                            // The Plague corpse: a generated diseased puddle with pale bones (legacy PlagueCorpseView.paint), flat on the ground.
                            art.sprite = GroundMarkArt.Puddle(); art.transform.position = position; art.transform.localScale = new Vector3(1.32f, 1.32f * .5f * GroundSquash / .78f, 1); art.sortingOrder = order - 2;
                        }
                        if (p.Kind == PropAppearance.Chest)
                        {
                            if (_catalog != null && _catalog.Chest != null) { art.sprite = _catalog.Chest; art.transform.position = position; art.transform.localScale = Vector3.one * 1.3f; }
                            // Open is a delivered boolean fact: show a dark opening and raised lid without
                            // inferring contents, eligibility, or timing.
                            if (p.Open)
                            {
                                Prop("bar", position + new Vector3(0, .12f), new Vector2(.48f, .12f), ShowcasePalette.Panel, order + 1);
                                Prop("bar", position + new Vector3(0, .32f), new Vector2(.62f, .08f), ShowcasePalette.GoldDim, order + 2);
                            }
                        }
                    }
                    if (p.Focused) { Prop("glow", position, new Vector2(1.3f, .68f), new Color(ShowcasePalette.Gold.r / 255f, ShowcasePalette.Gold.g / 255f, ShowcasePalette.Gold.b / 255f, .16f), order - 3); Prop("ring", position, new Vector2(1.32f, .69f), new Color(.03f, .04f, .06f, .85f), order - 3); Prop("ring", position, new Vector2(1.25f, .65f), (Color)ShowcasePalette.Gold, order - 2); }   // dark hairline under the gold ring: it vanished on sand and snow (UI lab)
                }
                foreach (var chest in collections.RewardChests)
                {
                    var p = WorldPoint(chest.X, chest.Y); int order = 2000 + Mathf.RoundToInt(chest.Y * .5f);
                    Prop("glow", p, new Vector2(2, 1), new Color(1, .67f, .2f, chest.Picked ? .08f : .35f), order - 2);
                    var art=Prop("chest", p + new Vector3(0, .25f), new Vector2(.9f, .8f), chest.Picked ? new Color(.48f, .48f, .48f) : Color.white, order);
                    { var ac = art.color; ac.a = WorldIndicatorRules.RewardAlpha((float)((chest.ExpiresAt - _sourceTimeMs) / 1000d)); art.color = ac; } // gone before the server removes it (ContainerView.rewardAlpha)
                    if(_catalog!=null && _catalog.Chest!=null) {art.sprite=_catalog.Chest;art.transform.position=p;art.transform.localScale=Vector3.one*1.6f;}
                    if (chest.CanChoose) Prop("rune", p + new Vector3(0, 1), new Vector2(.3f, .4f), new Color(1, .85f, .46f), order + 2);
                }
                bool trailTick = Time.unscaledTime >= _nextTrail; if (trailTick) _nextTrail = Time.unscaledTime + .025f;
                foreach (var shot in collections.Projectiles)
                {
                    // ADR0024: the ground track is (X, Y); the shot is DRAWN raised by its delivered height (the shooter's chest at launch,
                    // 0 under legacy hitboxes), with a soft shadow left on the ground track so depth reads.
                    var ground = WorldPoint(shot.X, shot.Y); var p = ProjectileDrawPoint(shot.X, shot.Y, shot.Height);
                    bool arrow = shot.Kind == ProjectileKind.Arrow;
                    if (shot.Height > 0f)
                        Prop("shadow", ground, arrow ? new Vector2(.5f, .17f) : new Vector2(.55f, .22f), new Color(0, 0, 0, .55f), 2000 + Mathf.RoundToInt(shot.Y * .5f) - 8);
                    var r = Prop(arrow ? "spark" : "glow", p, arrow ? new Vector2(.6f, .14f) : new Vector2(.7f, .7f),
                        arrow ? new Color(1.6f, 1.25f, .6f) : new Color(1.5f, .55f, 2), 29000);
                    r.transform.rotation = Quaternion.Euler(0, 0, ProjectileScreenAngleDeg(shot.Facing));
                    _overlay?.NoteShot(shot.X, shot.Y, shot.Height);
                    ProjectileFx(p, shot.Facing, arrow, trailTick);      // render pass: trail puffs and a small travelling light
                }
                _sparks.Observe(collections.Tick, collections.Projectiles, _camCenter, _camHalf.x, _camHalf.y, ref _sparkRng);
            }
            for (int i = _used; i < _props.Count; i++) _props[i].gameObject.SetActive(false);
            _markVisible = _markVisible ?? MarkVisible;
            _worldMarks.Update(collections, _markVisible); _campLabels.Update(_visionSession, _markVisible);
            _worldIndicators.Update(collections, _sourceTimeMs, _haveLocal, _localX, _localY, Time.unscaledTime, MarkVisible);
            var vs = _visionSession;
            if (vs != null && !ReferenceEquals(vs.Map, _campAttackMap)) { _campAttackMap = vs.Map; _campAttack.Clear(); }   // a new map is a new match
            _campMobs.Clear();
            foreach (var a in _latestNpcs) if (a.Kind == ActorKind.RegionalMob) _campMobs.Add(new CampAttackMob(a.Id, a.X, a.Y));
            var cm = vs != null ? vs.Map : null;
            _campAttack.Update(Time.realtimeSinceStartupAsDouble * 1000.0, vs != null ? vs.PlayerId : 0, vs != null && vs.Me != null && vs.Me.Status == ActorStatus.Alive, _campMobs, cm != null ? cm.Patience : null, cm != null ? cm.W : 0, cm != null ? cm.H : 0);
            _patience.Update(vs != null ? vs.Map : null, vs != null ? vs.Collections.Camps : null, vs != null ? vs.Phase : (MatchPhase?)null, vs != null && vs.Training, _campAttack);
            QuestCarrierFlag.Rows(vs != null ? vs.Collections.Quests : null, vs != null ? vs.ServerTimeMs : 0, _carrierRows);
            _carrierFlags.Update(_carrierRows, _carrierActorPoint, _markVisible);
            PerfMarks.End(PerfMarks.Sec.Collections, pmCollections);
        }
        /// <summary>The own Wish beside the name: 0 none (no faction Wish, or in the lobby), 1 ready, 2 recharging (legacy PlayerView.setWishIcon).</summary>
        int WishState(SelfView me)
        {
            if (!me.Wish.HasValue || me.Status == ActorStatus.Lobby) return 0;
            var w = me.Wish.Value;
            return _sourceTimeMs >= w.ReadyAt && w.Charges > 0 ? 1 : 2;
        }
        /// <summary>UI-facing: the fighter a click would lock right now (pointer pick), 0 for none. Set by the match client each frame.</summary>
        public void SetHover(uint actorId) { _hoverId = actorId; }
        void ProjectileFx(Vector3 p, float facing, bool arrow, bool trailTick)
        {
            float a = ProjectileScreenAngleDeg(facing) * Mathf.Deg2Rad;
            var back = new Vector3(-Mathf.Cos(a), -Mathf.Sin(a), 0);
            if (trailTick) _sparks.Trail(p + back * .12f, back, arrow ? new Color(2f, 1.4f, .6f) : new Color(1.6f, .6f, 2.2f), arrow ? .11f : .2f);
            if (AtmosphereQuality.AnalyticLights && AtmosphereQuality.FxLights) LocalLightField.StageWorld(p, .6f, arrow ? 2.0f : 2.6f, arrow ? new Color(1f, .75f, .4f) : new Color(.7f, .4f, 1f), arrow ? .55f : 1.1f);
        }
        bool MarkVisible(float x, float y) { var f = _visionFog != null ? _visionFog.Field : null; return f == null || !f.HasSample || !f.IsFogged((int)(x / 32f), (int)(y / 32f)); }
        /// <summary>Where a projectile with ground position (x, y) and height h (sim units) is drawn: the ground point raised h toward screen-up.</summary>
        public static Vector3 ProjectileDrawPoint(float x, float y, float height) { return WorldPoint(x, y - height); }
        /// <summary>Screen rotation of a sprite that points along the ground heading (sim radians, +y south): the ground is drawn squashed.</summary>
        public static float ProjectileScreenAngleDeg(float facing) { return Mathf.Atan2(-Mathf.Sin(facing) * GroundSquash, Mathf.Cos(facing)) * Mathf.Rad2Deg; }
        void LateUpdate() { if (_overlay != null) _overlay.Draw(_archetypes); _vignette?.Tick(Time.unscaledTime, ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion); }
        SpriteRenderer Prop(string sprite, Vector3 p, Vector2 scale, Color color, int order)
        {
            SpriteRenderer r;
            if (_used == _props.Count) { r = ShowcaseSprites.Create(_propsRoot, "Observed artwork", sprite, order, color, scale, p, false); _props.Add(r); }
            else r = _props[_used];
            _used++; r.gameObject.SetActive(true); r.sprite = ShowcaseSprites.Get(sprite); r.color = color; r.sortingOrder = order;
            r.sharedMaterial = ShowcaseSprites.Unlit;
            r.transform.position = p; r.transform.localScale = new Vector3(scale.x, scale.y, 1); r.transform.rotation = Quaternion.identity;
            return r;
        }
        /// <summary>Fades a damage number through the vertex alpha of its current mesh: no text rebuild (TMP_Text.color would regenerate it). The outline fades with it (the SDF shader scales the outline by vertex alpha).</summary>
        /// <summary>Measurement switch (PerfRecorder.SetFlags fx.numberfade=0): fade through TMP_Text.color, the old behaviour, for in-match A/B windows.</summary>
        public static bool LegacyNumberFade;
        static void SetNumberAlpha(Flash f, float alpha)
        {
            int a = Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
            if (LegacyNumberFade) { var lc = f.TextColor; lc.a = alpha; f.Text.color = lc; f.AlphaByte = -1; return; }
            if (a == f.AlphaByte) return;
            var text = f.Text; var info = text.textInfo;
            if (info == null || info.meshInfo == null || info.meshInfo.Length == 0 || info.meshInfo[0].colors32 == null)
            { var tc = f.TextColor; tc.a = alpha; text.color = tc; f.AlphaByte = -1; return; }      // no mesh yet: the slow path still fades correctly
            var colors = info.meshInfo[0].colors32; int n = Mathf.Min(colors.Length, info.meshInfo[0].vertexCount);
            for (int i = 0; i < n; i++) colors[i].a = (byte)a;
            text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
            f.AlphaByte = a;
        }
        static void SetActive<T>(VolumeProfile profile, bool active) where T : VolumeComponent
        {
            T component; if (profile.TryGet(out component)) component.active = active;
        }
        void OnDestroy()
        {
            if (ShowcaseSettings.Current != null) ShowcaseSettings.Current.Changed -= ApplyQuality;
            if (_visionSession != null) _visionSession.EventsReceived -= OnVisionEvents;
            if (_frostWall != null) _frostWall.Dispose();
            if (_vignette != null) _vignette.Dispose();
            if (_visionFog != null) _visionFog.Dispose();
            if (_regionGround != null) _regionGround.Dispose();
            if (_regionWalls != null) _regionWalls.Dispose();
            if (_coliseumGates != null) _coliseumGates.Dispose();
            if (_regionProps != null) _regionProps.Dispose();
            if (_landmarks != null) _landmarks.Dispose();
        }
    }
}
