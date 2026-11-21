using System;
using System.Collections.Generic;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>
    /// Draw-ahead of remote fighters (P-NET-DRAWAHEAD-1b; legacy packages/client/src/net/remoteDraw.ts). Presentation only: nothing
    /// here is sent, and the server's rewind is untouched (attacks keep <see cref="RenderClock.RenderMs"/>, the time the clock
    /// without draw-ahead gives).
    /// <para>
    /// The clock's draw time (<see cref="RenderClock.DrawMs"/>) may run up to one tick past a fighter's newest sample. A remote PLAYER
    /// is drawn past it only while the newest snapshot's walk hint says it keeps walking (<see cref="WireActor.WalkTicks"/> &gt;= 1):
    /// along <see cref="WireActor.WalkDir"/>, at the speed its last two samples show, for at most min(<see cref="TickMs"/>,
    /// WalkTicks x <see cref="TickMs"/>, one tick's walk at PLAYER_SPEED), clipped at walls, and never into a windup, a cast, a stun or
    /// root, a death or a cut. With no hint it holds the newest sample. A MOB has no hint: one that is moving walks on at its newest
    /// step's velocity for at most <see cref="MobExtrapolateMs"/> (held while it is <see cref="InterpActor.Engaged"/>).
    /// </para>
    /// <para>
    /// Corrections: when what a fighter is drawn toward changes (a new sample, a new hint, a hold), the difference goes into a
    /// per-fighter offset that decays over draw time, speed-capped, toward the truth and never past it (the LocalMotion pattern).
    /// An offset that would carry the figure into a wall slides it on from where it was drawn last. A cut (a <c>DisplaceSeq</c>
    /// change or a jump over <see cref="SnapshotBuffer.TeleportDistance"/>) is drawn where it lands and resets the offset; an
    /// offset over <see cref="SnapDistance"/> snaps.
    /// </para>
    /// Sampling makes no garbage once every fighter in view has been seen (per-fighter state is kept and recycled by id).
    /// </summary>
    public sealed class RemoteDraw
    {
        /// <summary>A mob has no walk hint: one that moves is drawn at most this far past its newest sample (legacy MOB_EXTRAPOLATE_MS).</summary>
        public const double MobExtrapolateMs = 25;
        /// <summary>An offset past this snaps (legacy REMOTE_SNAP_DISTANCE).</summary>
        public const double SnapDistance = 32;
        /// <summary>The offset decays toward 0 with this time constant (ms of draw time; legacy REMOTE_VISUAL_TAU_MS)...</summary>
        public const double VisualTauMs = 50;
        /// <summary>...no faster than this (u/s) while the fighter walks (+2 per unit past 20, at most 150)...</summary>
        public const double CorrectSpeedMoving = 120;
        /// <summary>...or while it stands (+3 per unit past 10, at most 1.5 x PLAYER_SPEED).</summary>
        public const double CorrectSpeedRest = 44;
        /// <summary>Legacy PLAYER_SPEED (u/s): the cap on how far past its newest sample a fighter is drawn is one tick of this walk.</summary>
        public const double PlayerSpeed = 170;
        const double CatchUpShare = 1.0, AheadShare = 0.9;
        const double MinWalkSpeed = 5, MaxWalkSpeed = 2.5 * PlayerSpeed;
        /// <summary>The wall clip's body: a hair under the real one (PLAYER_RADIUS - 1).</summary>
        const double ClipRadius = Pathfinder.PlayerRadius - 1;
        const double SlideShrinkU = 3;
        const double Tile = 32;
        const int MaxWindows = 8;
        /// <summary>Fighters not drawn for this many frames give their state back (it is rebuilt from nothing when they return).</summary>
        const int ForgetFrames = 600;

        const byte KindWindup = 0, KindCast = 1;

        sealed class Fighter
        {
            public uint Id;
            public double OffX, OffY, LastX, LastY;
            public int Frame;
            public bool HasModel;
            public double MT0, MX0, MY0, MVx, MVy, MBudget;
            public bool HasReach;
            public double RT, RDx, RDy, RSpeed, Reach;
            public byte SeenSeq;
            public double LastBaseX, LastBaseY;
            public bool HasBase;
            public readonly double[] WinFrom = new double[MaxWindows], WinUntil = new double[MaxWindows];
            public readonly byte[] WinKind = new byte[MaxWindows];
            public int WinCount;

            public void Clear(uint id)
            {
                Id = id; OffX = OffY = LastX = LastY = 0; Frame = 0; HasModel = false; HasReach = false; SeenSeq = 0; HasBase = false;
                WinCount = 0;
            }
        }

        readonly SnapshotBuffer _snapshots;
        readonly Dictionary<uint, Fighter> _fighters = new Dictionary<uint, Fighter>();
        readonly Stack<Fighter> _pool = new Stack<Fighter>();
        readonly List<uint> _drop = new List<uint>();
        MapBlob _reachMap;
        double _lastDrawMs = double.NaN, _dtMs;
        int _frame;

        /// <summary>The walls to clip at (null: nothing is drawn ahead, as legacy without a map).</summary>
        public MapBlob Map;

        /// <summary>Server tick interval (ms), from the Welcome: converts a walk hint's ticks to time, and is the most a player is drawn ahead.</summary>
        public double TickMs = 50;

        /// <summary>Offsets that snapped (over <see cref="SnapDistance"/>), and offsets cut at a wall with no clear spot to slide from (each a visible jump).</summary>
        public int Snaps, ResetsUnderSnap;

        public RemoteDraw(SnapshotBuffer snapshots) { _snapshots = snapshots; }

        /// <summary>The most a fighter is drawn past its newest sample (u): one tick's walk at PLAYER_SPEED (legacy REMOTE_MAX_AHEAD_U).</summary>
        public double MaxAheadU { get { return PlayerSpeed * TickMs / 1000.0; } }

        /// <summary>Forgets every fighter and hold window (a new Welcome, a source-clock cut).</summary>
        public void Reset()
        {
            foreach (var f in _fighters.Values) _pool.Push(f);
            _fighters.Clear();
            _lastDrawMs = double.NaN;
            _dtMs = 0;
        }

        // ------------------------------------------------------------------------------------------------------------------------
        // Hold windows (legacy noteFx). Times are server ms on the snapshot clock.

        /// <summary>
        /// A <c>Windup</c> fx by <paramref name="by"/> at server time <paramref name="tickMs"/> that lands <paramref name="ms"/> later: a
        /// swing holds it for the whole windup, a bow or wand shot from <see cref="LocalMotion.RangedReleasePauseMs"/> before it lands.
        /// </summary>
        public void NoteWindup(uint by, double tickMs, double ms, byte weapon)
        {
            bool ranged = weapon == (byte)WeaponId.Bow || weapon == (byte)WeaponId.Wand;
            double from = tickMs + (ranged ? Math.Max(0, ms - LocalMotion.RangedReleasePauseMs) : 0);
            AddWindow(by, from, tickMs + ms, KindWindup);
        }

        /// <summary>A cast (Wish) by <paramref name="by"/> from <paramref name="fromMs"/> for <paramref name="ms"/>: held while it casts.</summary>
        public void NoteCast(uint by, double fromMs, double ms) { if (ms > 0) AddWindow(by, fromMs, fromMs + ms, KindCast); }

        /// <summary>The cast ended (landed or cancelled): its hold goes.</summary>
        public void NoteCastEnd(uint by) { DropWindows(by, KindCast); }

        /// <summary>A <c>Cancel</c> or <c>Stagger</c> of <paramref name="id"/>: its windup hold goes.</summary>
        public void NoteAttackEnd(uint id) { DropWindows(id, KindWindup); }

        void AddWindow(uint id, double from, double until, byte kind)
        {
            if (double.IsNaN(from) || double.IsNaN(until)) return;
            var f = Get(id);
            if (f.WinCount == MaxWindows) RemoveWindow(f, 0);
            f.WinFrom[f.WinCount] = from;
            f.WinUntil[f.WinCount] = until;
            f.WinKind[f.WinCount] = kind;
            f.WinCount++;
        }

        void DropWindows(uint id, byte kind)
        {
            Fighter f;
            if (!_fighters.TryGetValue(id, out f)) return;
            for (int i = f.WinCount - 1; i >= 0; i--) if (f.WinKind[i] == kind) RemoveWindow(f, i);
        }

        static void RemoveWindow(Fighter f, int i)
        {
            for (int j = i + 1; j < f.WinCount; j++) { f.WinFrom[j - 1] = f.WinFrom[j]; f.WinUntil[j - 1] = f.WinUntil[j]; f.WinKind[j - 1] = f.WinKind[j]; }
            f.WinCount--;
        }

        /// <summary>The earliest time at or after <paramref name="t0"/> a window holds the fighter (+infinity: none known).</summary>
        static double StopAt(Fighter f, double t0)
        {
            double at = double.PositiveInfinity;
            for (int i = f.WinCount - 1; i >= 0; i--)
            {
                if (f.WinUntil[i] < t0 - 1000) { RemoveWindow(f, i); continue; }
                if (f.WinUntil[i] > t0 && f.WinFrom[i] < f.WinUntil[i]) at = Math.Min(at, Math.Max(f.WinFrom[i], t0));
            }
            return at;
        }

        Fighter Get(uint id)
        {
            Fighter f;
            if (_fighters.TryGetValue(id, out f)) return f;
            f = _pool.Count > 0 ? _pool.Pop() : new Fighter();
            f.Clear(id);
            f.Frame = 0;
            _fighters[id] = f;
            return f;
        }

        // ------------------------------------------------------------------------------------------------------------------------
        // Sampling

        /// <summary>
        /// Fills <paramref name="outActors"/> as <see cref="SnapshotBuffer.Sample"/> does at <paramref name="drawMs"/> (the
        /// clock's <see cref="RenderClock.DrawMs"/>, once per frame), with remote fighters' positions drawn ahead. False when empty.
        /// </summary>
        public bool Sample(double drawMs, List<InterpActor> outActors)
        {
            if (!_snapshots.Sample(drawMs, outActors)) return false;
            _dtMs = double.IsNaN(_lastDrawMs) ? 0 : Math.Min(250, Math.Max(0, drawMs - _lastDrawMs));
            _lastDrawMs = drawMs;
            _frame++;
            if (!ReferenceEquals(Map, _reachMap))
            {
                _reachMap = Map;
                foreach (var f in _fighters.Values) f.HasReach = false;
            }
            int n = _snapshots.Count;
            ServerMsg.Snapshot sN = _snapshots.At(n - 1), sP = n >= 2 ? _snapshots.At(n - 2) : null, sP2 = n >= 3 ? _snapshots.At(n - 3) : null;
            bool sortN = SortedById(sN.Actors), sortP = sP != null && SortedById(sP.Actors), sortP2 = sP2 != null && SortedById(sP2.Actors);
            for (int i = 0; i < outActors.Count; i++)
            {
                InterpActor a = outActors[i];
                Draw(ref a, drawMs, sN, sortN, sP, sortP, sP2, sortP2);
                outActors[i] = a;
            }
            if (_frame % 64 == 0) Forget();
            return true;
        }

        void Forget()
        {
            _drop.Clear();
            // Windows are otherwise pruned only while a fighter is drawn ahead; a held or vanished fighter (a mob whose
            // observer handle renewed) would keep its stale windows, and with them its state, for the whole match.
            double stale = _snapshots.Count > 0 ? _snapshots.At(_snapshots.Count - 1).ServerMs - 1000 : double.NegativeInfinity;
            foreach (var kv in _fighters)
            {
                var f = kv.Value;
                for (int i = f.WinCount - 1; i >= 0; i--) if (f.WinUntil[i] < stale) RemoveWindow(f, i);
                if (_frame - f.Frame > ForgetFrames && f.WinCount == 0) _drop.Add(kv.Key);
            }
            for (int i = 0; i < _drop.Count; i++)
            {
                _pool.Push(_fighters[_drop[i]]);
                _fighters.Remove(_drop[i]);
            }
        }

        void Draw(ref InterpActor a, double t, ServerMsg.Snapshot sN, bool sortN, ServerMsg.Snapshot sP, bool sortP, ServerMsg.Snapshot sP2, bool sortP2)
        {
            double baseX = a.X, baseY = a.Y;
            var st = Get(a.Id);

            // The newest step (legacy SnapshotBuffer.walk): this fighter in the newest snapshot and the one before, neither a cut, at
            // most two ticks apart; its speed is the faster of the newest two steps.
            int iN = Find(sN.Actors, sortN, a.Id);
            bool ok = false;
            double nT = 0, nX = 0, nY = 0, vx = 0, vy = 0, walkSpeed = 0;
            byte walkDir = 0, walkTicks = 0;
            if (iN >= 0)
            {
                ref readonly WireActor N = ref sN.Actors[iN];
                nT = sN.ServerMs; nX = N.X; nY = N.Y;
                walkDir = N.WalkDir; walkTicks = N.WalkTicks;
                int iP = sP != null ? Find(sP.Actors, sortP, a.Id) : -1;
                double maxGap = 2 * TickMs;
                if (iP >= 0)
                {
                    ref readonly WireActor P = ref sP.Actors[iP];
                    double dt = sN.ServerMs - sP.ServerMs;
                    if (!Cut(P, N) && dt > 0 && dt <= maxGap)
                    {
                        ok = true;
                        vx = (N.X - P.X) * 1000.0 / dt;
                        vy = (N.Y - P.Y) * 1000.0 / dt;
                        walkSpeed = Math.Sqrt(vx * vx + vy * vy);
                        int iP2 = sP2 != null ? Find(sP2.Actors, sortP2, a.Id) : -1;
                        if (iP2 >= 0)
                        {
                            ref readonly WireActor P2 = ref sP2.Actors[iP2];
                            double dt2 = sP.ServerMs - sP2.ServerMs;
                            if (!Cut(P2, P) && dt2 > 0 && dt2 <= maxGap)
                            {
                                double ddx = P.X - P2.X, ddy = P.Y - P2.Y;
                                walkSpeed = Math.Max(walkSpeed, Math.Sqrt(ddx * ddx + ddy * ddy) * 1000.0 / dt2);
                            }
                        }
                    }
                }
            }

            bool player = a.Kind == ActorKind.Player;
            double cap = player ? TickMs : MobExtrapolateMs;
            double budget = 0, dirX = 0, dirY = 0, speed = 0;
            double stepSpeed = ok ? Math.Sqrt(vx * vx + vy * vy) : 0;
            if (player)
            {
                // The hint is in the newest snapshot itself, so it is always from the same sample as the newest step.
                if (ok && walkTicks >= 1)
                {
                    budget = Math.Min(TickMs, walkTicks * TickMs);
                    double ang = walkDir * Math.PI / 128.0;
                    dirX = Math.Cos(ang);
                    dirY = Math.Sin(ang);
                    speed = walkSpeed;
                }
            }
            else if (stepSpeed > MinWalkSpeed)
            {
                budget = MobExtrapolateMs;
                dirX = vx / stepSpeed;
                dirY = vy / stepSpeed;
                speed = stepSpeed;
            }
            const ushort holdEffects = Ui.Indicators.Stunned | Ui.Indicators.Rooted | Ui.Indicators.Casting;
            if (speed < MinWalkSpeed || speed > MaxWalkSpeed || a.Status != ActorStatus.Alive || (a.Effects & holdEffects) != 0
                || (!player && a.Engaged)) budget = 0;
            if (budget > 0) budget = Math.Min(budget, Math.Min(Math.Max(0, StopAt(st, nT) - nT), MaxAheadU * 1000.0 / speed));
            if (budget > 0)
            {
                var map = Map;
                if (map == null) budget = 0;
                else
                {
                    if (!st.HasReach || st.RT != nT || st.RDx != dirX || st.RDy != dirY || st.RSpeed != speed)
                    {
                        st.HasReach = true;
                        st.RT = nT; st.RDx = dirX; st.RDy = dirY; st.RSpeed = speed;
                        st.Reach = ClearRun(map, nX, nY, dirX, dirY, speed * cap / 1000.0, ClipRadius);
                    }
                    budget = Math.Min(budget, st.Reach * 1000.0 / speed);
                }
            }

            // sampleAhead: past the newest sample, walk on along the direction for at most the budget.
            if (budget > 0 && iN >= 0 && t > nT)
            {
                double e = Math.Min(t - nT, budget) / 1000.0;
                baseX = nX + dirX * speed * e;
                baseY = nY + dirY * speed * e;
            }
            double mVx = dirX * speed, mVy = dirY * speed;

            bool cut = st.HasBase && (a.DisplaceSeq != st.SeenSeq || Dist(a.X, a.Y, st.LastBaseX, st.LastBaseY) > SnapshotBuffer.TeleportDistance);
            st.SeenSeq = a.DisplaceSeq;
            st.LastBaseX = a.X; st.LastBaseY = a.Y;
            st.HasBase = true;
            if (st.Frame != 0 && st.Frame != _frame - 1)
            {
                // Not drawn last frame: there is no figure on screen to keep continuous.
                st.OffX = st.OffY = 0;
                st.HasModel = false;
            }
            if (cut)
            {
                // A cut is drawn where it lands, never slid into.
                st.OffX = st.OffY = 0;
            }
            else
            {
                if (st.HasModel && t > st.MT0 && (st.MT0 != nT || st.MBudget != budget || st.MVx != mVx || st.MVy != mVy))
                {
                    // What is drawn stays where it was: the change goes into the offset.
                    double e = Math.Min(t - st.MT0, st.MBudget) / 1000.0;
                    st.OffX += st.MX0 + st.MVx * e - baseX;
                    st.OffY += st.MY0 + st.MVy * e - baseY;
                }
                double mag = Math.Sqrt(st.OffX * st.OffX + st.OffY * st.OffY);
                if (mag > SnapDistance) { st.OffX = st.OffY = 0; Snaps++; }
                else if (mag < 1e-4) st.OffX = st.OffY = 0;
                else
                {
                    double capSpeed = CorrectionCap(mag, st.OffX, st.OffY, ok ? vx : 0, ok ? vy : 0);
                    double dec = Math.Min(mag, Math.Min(mag * (1 - Math.Exp(-_dtMs / VisualTauMs)), capSpeed * _dtMs / 1000.0));
                    double k = (mag - dec) / mag;
                    st.OffX *= k;
                    st.OffY *= k;
                }
            }
            st.HasModel = iN >= 0;
            st.MT0 = nT; st.MX0 = nX; st.MY0 = nY; st.MVx = mVx; st.MVy = mVy; st.MBudget = budget;

            // The offset may not carry the fighter into a wall: from where it was drawn last it moves toward its target as far as it
            // would have moved this frame, sliding along the wall, and the offset becomes what that leaves. Only with nowhere clear to
            // slide from is the offset cut at the wall (a visible jump, counted).
            var walls = Map;
            if (walls != null && (st.OffX != 0 || st.OffY != 0))
            {
                double px = baseX + st.OffX, py = baseY + st.OffY;
                if (!CircleClear(walls, px, py, ClipRadius))
                {
                    double left = Dist(baseX, baseY, st.LastX, st.LastY);
                    double k = left > 1e-9 ? Math.Min(1, Dist(px, py, st.LastX, st.LastY) / left) : 0;
                    double qx, qy;
                    if (st.Frame == _frame - 1
                        && SlideFrom(walls, st.LastX, st.LastY, st.LastX + (baseX - st.LastX) * k, st.LastY + (baseY - st.LastY) * k, baseX, baseY, out qx, out qy))
                    {
                        st.OffX = qx - baseX;
                        st.OffY = qy - baseY;
                    }
                    else
                    {
                        double mag = Math.Sqrt(st.OffX * st.OffX + st.OffY * st.OffY);
                        double ux = st.OffX / mag, uy = st.OffY / mag;
                        double run = ClearRun(walls, baseX, baseY, ux, uy, mag, ClipRadius);
                        if (mag - run > 0.5) ResetsUnderSnap++;
                        st.OffX = ux * run;
                        st.OffY = uy * run;
                    }
                }
            }
            double outX = baseX + st.OffX, outY = baseY + st.OffY;
            a.X = (float)outX;
            a.Y = (float)outY;
            st.LastX = outX;
            st.LastY = outY;
            st.Frame = _frame;
        }

        /// <summary>A step from <paramref name="p"/> to <paramref name="c"/> is a cut (as <see cref="SnapshotBuffer"/> draws it).</summary>
        static bool Cut(in WireActor p, in WireActor c)
        {
            float dx = c.X - p.X, dy = c.Y - p.Y;
            return p.DisplaceSeq != c.DisplaceSeq || dx * dx + dy * dy > SnapshotBuffer.TeleportDistance * SnapshotBuffer.TeleportDistance;
        }

        static double Dist(double ax, double ay, double bx, double by) { double dx = ax - bx, dy = ay - by; return Math.Sqrt(dx * dx + dy * dy); }

        static bool SortedById(WireActor[] a)
        {
            for (int i = 1; i < a.Length; i++) if (a[i - 1].Id >= a[i].Id) return false;
            return true;
        }

        /// <summary>Index of <paramref name="id"/> (the last one, as <see cref="SnapshotBuffer"/> pairs them), or -1.</summary>
        static int Find(WireActor[] a, bool sorted, uint id)
        {
            if (sorted)
            {
                int lo = 0, hi = a.Length - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    uint m = a[mid].Id;
                    if (m == id) return mid;
                    if (m < id) lo = mid + 1; else hi = mid - 1;
                }
                return -1;
            }
            for (int i = a.Length - 1; i >= 0; i--) if (a[i].Id == id) return i;
            return -1;
        }

        /// <summary>
        /// How fast (u/s) an offset of <paramref name="mag"/> may shrink given the fighter's newest step (legacy correctionCap): at rest
        /// the slow caps keep a small slide under the figure's walk start; drawn behind a walk it may catch up at its walk speed on top;
        /// drawn ahead or to the side the pull back is under the walk speed, so it slows and never turns round.
        /// </summary>
        public static double CorrectionCap(double mag, double offX, double offY, double vx, double vy)
        {
            double s = Math.Sqrt(vx * vx + vy * vy);
            if (s <= MinWalkSpeed) return Math.Min(1.5 * PlayerSpeed, CorrectSpeedRest + 3 * Math.Max(0, mag - 10));
            double moving = Math.Min(150, CorrectSpeedMoving + 2 * Math.Max(0, mag - 20));
            return offX * vx + offY * vy < 0 ? Math.Max(moving, CatchUpShare * s) : Math.Min(moving, AheadShare * s);
        }

        // ------------------------------------------------------------------------------------------------------------------------
        // Wall geometry (legacy shared/src/geometry.ts circleClear, clearRun; MASK_MOVE: any solid tile, out of bounds blocks)

        static bool Blocked(MapBlob map, long tx, long ty)
        {
            if (tx < 0 || ty < 0 || tx >= map.W || ty >= map.H) return true;
            return map.Tiles[ty * map.W + tx] != MapBlob.Floor;
        }

        /// <summary>A disc of <paramref name="radius"/> at (x, y) overlaps no solid tile (tangent is not overlap).</summary>
        internal static bool CircleClear(MapBlob map, double cx, double cy, double radius)
        {
            if (double.IsNaN(cx + cy + radius) || double.IsInfinity(cx + cy + radius)) return false;
            long tx = (long)Math.Floor(cx / Tile), ty = (long)Math.Floor(cy / Tile);
            if (radius <= 0) return !Blocked(map, tx, ty);
            long reach = Math.Max(1, (long)Math.Ceiling(radius / Tile));
            double r2 = radius * radius;
            for (long j = ty - reach; j <= ty + reach; j++)
                for (long i = tx - reach; i <= tx + reach; i++)
                {
                    if (!Blocked(map, i, j)) continue;
                    double nx = Math.Min(Math.Max(cx, i * Tile), (i + 1) * Tile);
                    double ny = Math.Min(Math.Max(cy, j * Tile), (j + 1) * Tile);
                    double ddx = cx - nx, ddy = cy - ny;
                    if (ddx * ddx + ddy * ddy < r2) return false;
                }
            return true;
        }

        /// <summary>
        /// A disc swept from (x, y) along (ux, uy) for <paramref name="len"/> touches no solid tile: discs at both ends and every
        /// <see cref="SweepStepU"/> on the way (tiles are 32 u, the swept discs here are under 12 u, so a gap between two discs is
        /// a sliver far thinner than a tile corner can poke through by more than a hair).
        /// </summary>
        static bool SweepClear(MapBlob map, double x, double y, double ux, double uy, double len, double radius)
        {
            if (!CircleClear(map, x + ux * len, y + uy * len, radius)) return false;
            int steps = (int)Math.Ceiling(len / SweepStepU);
            for (int s = 1; s < steps; s++)
            {
                double d = len * s / steps;
                if (!CircleClear(map, x + ux * d, y + uy * d, radius)) return false;
            }
            return true;
        }

        const double SweepStepU = 1;

        /// <summary>
        /// How far a disc of <paramref name="radius"/> at (x, y) can travel along (ux, uy), up to <paramref name="maxDist"/>, touching no
        /// solid tile (legacy clearRun): 0 when it already overlaps one, <paramref name="maxDist"/> when the run is clear, else the clear
        /// end of six bisections.
        /// </summary>
        internal static double ClearRun(MapBlob map, double x, double y, double ux, double uy, double maxDist, double radius)
        {
            if (!CircleClear(map, x, y, radius)) return 0;
            if (!(maxDist > 0)) return 0;
            if (SweepClear(map, x, y, ux, uy, maxDist, radius)) return maxDist;
            double lo = 0, hi = maxDist;
            for (int i = 0; i < 6; i++)
            {
                double mid = (lo + hi) / 2;
                if (SweepClear(map, x, y, ux, uy, mid, radius)) lo = mid;
                else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Moves a disc from (x0, y0) toward (x1, y1), a step on the way to the goal, as a body would (legacy slideFrom): straight while
        /// clear, then the rest along x and then y. Its radius shrinks in <see cref="SlideShrinkU"/> steps while the start or the goal
        /// overlaps a wall. False when even a point at the start is in one.
        /// </summary>
        static bool SlideFrom(MapBlob map, double x0, double y0, double x1, double y1, double gx, double gy, out double ox, out double oy)
        {
            double ax = x0, ay = y0;
            double r = ClipRadius;
            while (r > 0 && !(CircleClear(map, ax, ay, r) && CircleClear(map, gx, gy, r))) r = Math.Max(0, r - SlideShrinkU);
            ox = ax; oy = ay;
            if (!CircleClear(map, ax, ay, r)) return false;
            double dx = x1 - x0, dy = y1 - y0;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len > 1e-9)
            {
                double run = ClearRun(map, ax, ay, dx / len, dy / len, len, r);
                ax += dx / len * run;
                ay += dy / len * run;
                double rx = x1 - ax;
                if (Math.Abs(rx) > 1e-6) ax += Math.Sign(rx) * ClearRun(map, ax, ay, Math.Sign(rx), 0, Math.Abs(rx), r);
                double ry = y1 - ay;
                if (Math.Abs(ry) > 1e-6) ay += Math.Sign(ry) * ClearRun(map, ax, ay, 0, Math.Sign(ry), Math.Abs(ry), r);
            }
            ox = ax; oy = ay;
            return true;
        }
    }
}
