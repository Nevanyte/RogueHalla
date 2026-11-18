using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>One area as it was drawn this frame (server-unit radius turned into world size), for diagnostics and tests.</summary>
    public struct WishDrawnArea
    {
        public AreaKind Kind;
        public AbilityId Ability;
        public Vector3 Center, End;
        /// <summary>World width/height of the ring (circle areas) or length/thickness of the band (trails).</summary>
        public Vector2 Size;
    }

    /// <summary>
    /// The five Wish sequences: cast telegraph, landing, the area while it lives, status looks on the affected actors and the
    /// end effect. Everything is drawn from delivered facts only: Wish fx records (cast, landing, end), the area rows
    /// (zone, cloud, trail) and the public status bits of observed actors. Nothing is simulated here.
    /// Sprites are immediate-mode from a pool, particles come from four capped systems, and no frame allocates.
    /// </summary>
    public sealed partial class WishFxPresenter
    {
        public const int CueCapacity = 48, GhostCapacity = 12, ActorCapacity = 160, SpriteCap = 420;
        const float S = ArenaView.Scale, G = ArenaView.GroundSquash;
        const int GroundOrder = 1230, RingOrder = 1240, BurstOrder = 29950, ParticleOrder = 29990;
        const double TrailScorchMs = 2000;

        enum CueType : byte { Windup, Landing, Shatter, Radiant, Break }
        struct Cue
        {
            public CueType Type; public byte Ability; public uint Caster, Target;
            public bool HasAnchor, HasDest, Fired;
            public float X, Y, X2, Y2; public Vector3 Pos;
            public double Start, Dur; public uint Seed;
        }
        struct Ghost { public WishAreaEnded Ended; public double At; }
        struct ActorNote { public uint Id; public ActorKind Kind; public ushort Effects; public bool Alive; }

        readonly SpriteActorPresenter _actors;
        readonly Pen _pen;
        readonly Particles _particles;
        readonly Cue[] _cues = new Cue[CueCapacity];
        int _cueCount;
        readonly Ghost[] _ghosts = new Ghost[GhostCapacity];
        int _ghostCount;
        readonly ActorNote[] _notes = new ActorNote[ActorCapacity];
        int _noteCount;
        readonly WishAreaTracker _areaTracker = new WishAreaTracker();
        readonly WishStatusTracker _statusTracker = new WishStatusTracker();
        readonly List<WishAreaEnded> _ended = new List<WishAreaEnded>(8);
        readonly List<WishDrawnArea> _drawn = new List<WishDrawnArea>(16);
        readonly float[] _acc = new float[12];
        readonly Dictionary<uint, Vector3> _lastPos = new Dictionary<uint, Vector3>(16);
        IReadOnlyList<AreaView> _areas = System.Array.Empty<AreaView>();
        uint _owner;
        double _sourceMs, _smoothMs, _lastBlazeMs = double.NegativeInfinity;
        float _sourceWall;
        // The latest Freeze landing, so a stun that appears at that spot is styled as ice. Nothing else is inferred from it.
        double _freezeAt = double.NegativeInfinity; float _freezeX, _freezeY;

        public WishFxPresenter(Transform parent, SpriteActorPresenter actors)
        {
            _actors = actors;
            var root = new GameObject("Wish effects").transform; root.SetParent(parent, false);
            _pen = new Pen(root, SpriteCap);
            _particles = new Particles(root);
        }

        // ---------------------------------------------------------------- introspection (tests, diagnostics)
        public int ActiveCueCount { get { return _cueCount; } }
        public int GhostCount { get { return _ghostCount; } }
        public int SpritesDrawn { get { return _pen.Used; } }
        public int SpritePoolSize { get { return _pen.PoolSize; } }
        public IReadOnlyList<WishDrawnArea> DrawnAreas { get { return _drawn; } }
        public int LiveParticles { get { return _particles.Live; } }
        public int ParticleCapacity { get { return _particles.Capacity; } }
        public bool IsReduced { get { return Reduced; } }
        static bool Reduced { get { return ShowcaseSettings.Current != null && ShowcaseSettings.Current.ReducedMotion; } }

        public void SetOwner(uint id) { _owner = id; }
        /// <summary>Which Wish Frost has here; under Wall the Freeze windup beam is not drawn (FrostWallPresenter draws the wall's telegraph).</summary>
        public FrostWishKind FrostWish;

        // ---------------------------------------------------------------- inputs
        /// <summary>Copies a delivered Wish fx record into a bounded cue. Same acceptance rules as the confirmed-cue layer.</summary>
        public void Accept(WireFx f)
        {
            if (f == null || f.Ability > 4 || double.IsNaN(f.EventTime) || double.IsInfinity(f.EventTime) || f.EventTime < 0) return;
            if (f.Kind != FxKind.WishStarted && f.Kind != FxKind.WishLanded && f.Kind != FxKind.WishEnded) return;
            if (f.Kind != FxKind.WishStarted && f.Caster.HasValue)
                for (int i = _cueCount - 1; i >= 0; i--)
                    if (_cues[i].Type == CueType.Windup && _cues[i].Caster == f.Caster.Value && _cues[i].Ability == f.Ability && _cues[i].Start <= f.EventTime) RemoveCue(i);
            if (f.Kind == FxKind.WishEnded) return;
            var cue = new Cue
            {
                Ability = f.Ability, Caster = f.Caster ?? 0, Target = f.Target ?? 0, HasAnchor = f.HasAnchor, HasDest = f.HasDestination,
                X = f.AnchorX, Y = f.AnchorY, X2 = f.DestinationX, Y2 = f.DestinationY, Start = f.EventTime,
                Seed = (uint)(f.EventTime * 7 + f.Ability * 131 + (f.Caster ?? 0) * 17)
            };
            if (f.Kind == FxKind.WishStarted)
            {
                if (!f.Caster.HasValue || !(f.Duration > 0)) return;
                cue.Type = CueType.Windup; cue.Dur = f.Duration;
                _actors.WishCue(cue.Caster, (AbilityId)f.Ability);
            }
            else
            {
                if (!f.HasAnchor) return;
                if (f.Ability == (byte)AbilityId.Blaze && !f.HasDestination) return;
                if (f.Ability != (byte)AbilityId.Blaze && f.HasDestination) return;
                cue.Type = CueType.Landing; cue.Dur = LandingMs((AbilityId)f.Ability);
                if (f.Ability == (byte)AbilityId.Freeze) { _freezeAt = f.EventTime; _freezeX = f.AnchorX; _freezeY = f.AnchorY; }
                if (f.Caster.HasValue && (f.Ability == (byte)AbilityId.Blaze || f.Ability == (byte)AbilityId.LastStand)) _actors.WishCue(cue.Caster, (AbilityId)f.Ability);
            }
            AddCue(cue);
        }

        static double LandingMs(AbilityId a)
        {
            switch (a) { case AbilityId.Trial: return 650; case AbilityId.LastStand: return 850; case AbilityId.Blaze: return 700; default: return 600; }
        }

        void AddCue(in Cue cue)
        {
            if (_cueCount == CueCapacity) RemoveCue(0);
            _cues[_cueCount++] = cue;
        }
        void RemoveCue(int i) { for (int j = i + 1; j < _cueCount; j++) _cues[j - 1] = _cues[j]; _cueCount--; }

        /// <summary>Start of an actor-presenting pass; clears the per-frame status notes.</summary>
        public void BeginFrame() { _noteCount = 0; }

        /// <summary>Records one observed actor's public status bits. Players also feed the edge tracker.</summary>
        public void NoteActor(uint id, ActorKind kind, ushort effects, bool alive)
        {
            for (int i = 0; i < _noteCount; i++) if (_notes[i].Id == id) { _notes[i].Effects = effects; _notes[i].Alive = alive; _notes[i].Kind = kind; return; }
            if (_noteCount < ActorCapacity) _notes[_noteCount++] = new ActorNote { Id = id, Kind = kind, Effects = effects, Alive = alive };
        }

        public void PresentAreas(IReadOnlyList<AreaView> fields) { _areas = fields ?? (IReadOnlyList<AreaView>)System.Array.Empty<AreaView>(); }

        /// <summary>A departed caster takes its windup telegraph with it.</summary>
        public void Retain(HashSet<uint> membership)
        {
            for (int i = _cueCount - 1; i >= 0; i--)
                if (_cues[i].Type == CueType.Windup && !membership.Contains(_cues[i].Caster)) RemoveCue(i);
        }

        public void Reset()
        {
            _cueCount = 0; _ghostCount = 0; _noteCount = 0; _areaTracker.Clear(); _statusTracker.Clear(); _drawn.Clear(); _lastPos.Clear();
            _areas = System.Array.Empty<AreaView>(); _freezeAt = _lastBlazeMs = double.NegativeInfinity; _sourceMs = 0;
            for (int i = 0; i < _acc.Length; i++) _acc[i] = 0;
            _pen.Begin(); _pen.End(); _particles.Clear(); _actors.ClearWishTints();
        }

        // ---------------------------------------------------------------- frame
        public void Tick(double serverTimeMs, float dt)
        {
            if (double.IsNaN(serverTimeMs) || serverTimeMs < 0) return;
            float wall = Time.unscaledTime;
            if (serverTimeMs != _sourceMs) { if (serverTimeMs < _sourceMs) { _cueCount = 0; _ghostCount = 0; } _sourceMs = serverTimeMs; _sourceWall = wall; }
            // Snapshots arrive every 50 ms; between them time advances on the local clock so motion is smooth.
            _smoothMs = _sourceMs + System.Math.Min(100d, (wall - _sourceWall) * 1000d);
            double now = _smoothMs; float t = wall; bool reduced = Reduced;
            _pen.Begin(); _drawn.Clear(); _actors.ClearWishTints();
            _particles.Configure(reduced);
            if (dt > .25f) dt = .25f;

            DrawAreas(now, t, reduced, dt);
            DrawStatuses(now, t, reduced, dt);
            DrawCues(now, t, reduced);
            DrawGhosts(now, t, reduced);
            _pen.End();
        }

        // ---------------------------------------------------------------- helpers
        static Vector3 W(float x, float y) { return ArenaView.WorldPoint(x, y); }
        static Color Fac(int faction, float alpha = 1f) { Color c = ShowcasePalette.Faction(faction); c.a = alpha; return c; }
        static Color Mix(Color a, Color b, float t) { return Color.Lerp(a, b, t); }
        static Color A(Color c, float alpha) { c.a = alpha; return c; }
        static Color Deep(Color c, float alpha) { Color d = Color.Lerp(c, new Color(.02f, .03f, .06f), .62f); d.a = alpha; return d; }
        static int OrderOf(Vector3 p) { return 2000 + Mathf.RoundToInt(-p.y / (S * G) * .5f); }
        static Vector2 Ell(float radiusWorld) { return new Vector2(radiusWorld * 2f, radiusWorld * 2f * G); }
        bool Pos(uint id, out Vector3 p) { p = default(Vector3); return id != 0 && _actors.TryPosition(id, out p); }

        void Line(string key, Vector3 a, Vector3 b, float thick, Color c, int order)
        {
            Vector3 d = b - a; float len = d.magnitude; if (len < .001f) return;
            _pen.Draw(key, (a + b) * .5f, new Vector2(len, thick), Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, c, order);
        }
        void Ring(string key, Vector3 c, float radiusWorld, Color col, int order) { _pen.Draw(key, c, Ell(radiusWorld), 0, col, order); }
        /// <summary>A ring with a dark underlay so it reads on bright snow as well as on dark stone.</summary>
        void SafeRing(string key, Vector3 c, float radiusWorld, Color col, int order, float underlay = .85f)
        {
            Ring(key, c, radiusWorld * 1.025f, Deep(col, col.a * underlay), order - 1);
            Ring(key, c, radiusWorld, col, order);
        }

        // ---------------------------------------------------------------- small utilities
        uint _rng = 0x9e3779b9u;
        static float Random01(ref uint s) { unchecked { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xffffff) / 16777215f; } }
        Vector3 Rand2(float radius) { float a = Random01(ref _rng) * 6.2832f, d = Mathf.Sqrt(Random01(ref _rng)) * radius; return new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d * G, 0); }

        /// <summary>How many particles an emitter owes this frame at <paramref name="perSecond"/> (fractions carry in <paramref name="acc"/>).</summary>
        static int EmitCount(ref float acc, float dt, float perSecond)
        {
            if (Quality.Particles <= 0f) return 0;
            acc += dt * perSecond;
            int n = Mathf.Min(3, (int)acc); acc -= (int)acc;
            return n;
        }

    }
}
