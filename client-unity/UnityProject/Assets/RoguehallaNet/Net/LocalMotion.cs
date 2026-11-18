using System;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>
    /// Presentation-only prediction of the local player's walk (P-NET-1 Phase 2; ADR 0008's one narrow prediction exception).
    /// <para>
    /// An order plans the same A* path the server will plan (the Phase 1 <see cref="Pathfinder"/>, built from the client's decoded
    /// <see cref="MapBlob"/>) and <see cref="Step"/> walks that path at the server's <c>PLAYER_SPEED</c> times the owner's published
    /// move-speed mirror (<c>speed_milli / 1000</c>, rh-sim's <c>buffs.moveSpeedMult</c>, floored like rh-sim). The walk is presented
    /// as <c>drawn = sim + visual</c>: <see cref="SimX"/>/<see cref="SimY"/> are the last accepted authoritative self position (updated
    /// on every snapshot, even while an order is pending), and <see cref="VisualX"/>/<see cref="VisualY"/> are the offset from the
    /// model plus the smoothing correction. Nothing here is ever sent: <see cref="Session"/> queues orders, this type only presents
    /// them; it has no reference to <see cref="InputSender"/> and never increases the order rate.
    /// </para>
    /// <para>
    /// Reconciliation is delay-aligned (the legacy client's rebase): a sample is compared with where the model was when the server
    /// was at that point of the walk, not with where it is now. The first sample that echoes a timing order (a start, a stop or a
    /// turn of more than 12 degrees; a same-direction repeat of a held key is not one) measures that order's delay
    /// <c>D = sample time - TICK_MS - order time</c>, and every sample at source time T is then compared with the model's pose at
    /// T - D. Sample times follow the source clock's spacing, anchored to the earliest arrival, so arrival jitter is not error.
    /// A model or composed visual error above 96 u snaps the model to the server; a smaller one shifts the model (and its pose
    /// history) by the error and the visual offset the other way, and the offset decays with a 100 ms time constant but never
    /// faster than 70 to 120 u/s while walking (a correction can slow or hurry the walk, never turn it round) or 255 u/s at rest.
    /// While the pose at T - D already follows an order the server has not applied, the sample is only a bound: it snaps when the
    /// error exceeds 96 u plus the walk since that order. Comparison does not wait for <see cref="OrderPending"/> to clear, so a held
    /// key whose repeats outrun the echo is still reconciled.
    /// </para>
    /// <para>
    /// <see cref="OrderPending"/> clears only when the self block's <c>move_seq</c> echoes the newest order's sequence (sequences
    /// are 1..255; 0 means "no order yet"). An order the server has not echoed within one second is forgotten (the pending gate
    /// clears; the predicted walk itself continues along its path and the samples keep reconciling it).
    /// </para>
    /// <para>
    /// Authoritative displacement, death and revival immediately discard the predicted path, even with an order pending.
    /// Only the local walk is predicted. Attacks, HP, deaths, other actors and mobs are never predicted; hidden speed modifiers
    /// (roots, slows, terrain) are deliberately not guessed, so a server correction owns them (see <see cref="SpeedModifier"/>).
    /// </para>
    /// <para>
    /// The one attack fact the walk mirrors is the server's attack pause (ADR 0008 amendment 2026-11-10; rh-sim
    /// <c>move_multiplier</c>): the owner's walk stops for the whole wind-up of a swing and for the last
    /// <see cref="RangedReleasePauseMs"/> before a bow or wand shot lands. The client never times an attack from a key press. It
    /// places the pause from what the server delivers to the owner: the owner's <c>Windup</c> fx (<see cref="NoteOwnWindup"/>, the
    /// tick it was sent in plus its wait is the landing), and ends it early on the owner's <c>Stagger</c>/<c>Cancel</c> fx
    /// (<see cref="NoteOwnStagger"/>, <see cref="NoteOwnCancel"/>) or a stun or weapon change in the self block. The window is placed
    /// on the server's 50 ms step grid (the walk of the step ending at tick T is stopped when T is inside the pause), stored in
    /// server time and mapped onto the model clock through the same sample stamp and measured delay reconciliation uses, so the
    /// pause moves with the delay estimate rather than with a second clock. A bow draw is not announced by any fx, so a bow release
    /// stays unpredicted (see <see cref="NoteOwnWindup"/>). Proved against <c>protocol/fixtures/ranged_pause_v1.json</c>.
    /// </para>
    /// <para>Single-threaded; no allocation per frame (the pathfinder reuses one scratch instance per <see cref="LocalMotion"/>; an
    /// order allocates only the waypoint array it keeps).</para>
    /// </summary>
    public sealed partial class LocalMotion
    {
        /// <summary>The server's base walk speed (<c>PLAYER_SPEED</c>), world units per second.</summary>
        public const double PlayerSpeed = 170.0;
        /// <summary>The server tick (<c>TICK_MS</c>): its walk is planned and sampled on this grid.</summary>
        public const double TickMs = 50.0;
        /// <summary>An error up to this many world units is absorbed smoothly; beyond it the presented walk snaps to the server.</summary>
        public const double SnapDistance = 96.0;
        /// <summary>The correction offset's exponential time constant, in milliseconds (<c>VISUAL_TAU_MS</c>).</summary>
        public const double VisualTauMs = 100.0;
        /// <summary>An order the server has not echoed within this long is forgotten (<c>ORDER_EXPIRE_MS</c>).</summary>
        public const double OrderExpireMs = 1000.0;
        /// <summary>A sample this far from the model is a divergence for the consecutive-sample counter (legacy <c>DIVERGE_ERR</c>).</summary>
        public const double DivergeErr = 6.0;

        /// <summary>The measured order delay is clamped to this range, in milliseconds (legacy <c>DELAY_MIN_MS</c>/<c>DELAY_MAX_MS</c>).</summary>
        public const double DelayMinMs = -TickMs;
        public const double DelayMaxMs = 400.0;
        /// <summary>While walking, the correction offset runs down at most this fast: 70 u/s, plus 2 per unit past 20, at most 120.</summary>
        public const double CorrectSpeedMoving = 70.0;
        public const double CorrectSpeedMovingMax = 120.0;
        /// <summary>At rest the offset runs down at most this fast (legacy <c>CORRECT_SPEED_REST_MAX</c>, 1.5 x PLAYER_SPEED).</summary>
        public const double CorrectSpeedRestMax = 1.5 * PlayerSpeed;
        /// <summary>rh-sim <c>MOVE_MULT_FLOOR</c>: a walk multiplier above 0 is never below this.</summary>
        public const double MoveMultFloor = 0.35;
        /// <summary>rh-sim <c>RANGED_RELEASE_PAUSE_MS</c>: the walk stops for this long before a bow or wand shot lands. Pinned to the Rust value by <c>ranged_pause_v1.json</c>.</summary>
        public const double RangedReleasePauseMs = 200.0;
        /// <summary>rh-sim <c>TIE_EPS_MS</c>: a landing within this of a step counts as that step's. Pinned by <c>ranged_pause_v1.json</c>.</summary>
        public const double TieEpsMs = 1e-6;
        /// <summary>rh-sim <c>BOW_WINDUP_MAX_S</c> and <c>BOW_WINDUP_INTERVAL_SHARE</c>: a player bow shot lands <c>min(max, share * interval)</c> seconds after its draw starts. Pinned by <c>ranged_pause_v2.json</c>.</summary>
        public const double BowWindupMaxS = 0.5, BowWindupIntervalShare = 0.55;

        /// <summary>A stalled frame walks at most this much (legacy <c>MAX_FRAME_MS</c>); it can never tunnel through a wall.</summary>
        const double MaxStepMs = 100.0;
        /// <summary>A new order whose first leg is within 12 degrees of the current one does not change WHEN the fighter moves.</summary>
        static readonly double SameDirectionCos = Math.Cos(12.0 * Math.PI / 180.0);
        /// <summary>Model poses kept for delay alignment (a frame each while walking; covers DelayMaxMs plus a sample at 120+ Hz).</summary>
        const int HistorySize = 256;
        /// <summary>Unapplied orders kept for delay alignment; more than one second of a 100 ms key repeat.</summary>
        const int OrderRingSize = 32;
        const double ZeroSegmentSq = 1e-18;

        static readonly PathPoint[] NoPath = new PathPoint[0];

        MapBlob _map;
        // P-FROST-WALL-PREDICT-1: the map the walk plans and steers on (_map with every standing wall's tiles LOW; _map itself
        // when no wall stands), and the walls it was built from (keys and tiles, reused arrays; written only when the set changes).
        MapBlob _walkMap;
        AreaKey[] _wallKeys = new AreaKey[4];
        MapTile[][] _wallTiles = new MapTile[4][];
        int _wallCount;
        bool _predictionSuspended;
        bool _alive;
        byte _displaceSeq;
        readonly Pathfinder _pathfinder = new Pathfinder();
        Footprint _foot = Footprint.Legacy;

        // Authoritative self, as last accepted (wire floats, widened for path and smoothing arithmetic).
        double _simX, _simY;
        // Predicted walk model (the server's position along the same path, carried forward by Step).
        double _modelX, _modelY;
        // Smoothing correction kept out of the model: drawn = model + correction (and therefore sim + visual).
        double _corrX, _corrY;
        double _lastStepMs;
        bool _stepClock;

        // The outstanding order and the walk it started.
        bool _pending;
        byte _lastSeq;
        // One fixed byte-domain set for casts issued AFTER the current movement. A new move/stop resets the set;
        // an older cast must never acknowledge a newer walk, even across the 255 -> 1 sequence wrap.
        readonly WireCommand[] _nonMovementAcks = new WireCommand[256];
        readonly bool[] _sentNonMovementAcks = new bool[256];
        readonly uint[] _nonMovementFrames = new uint[256];
        byte _observedSeq;
        WireCommand _movementOrder;
        bool _movementSent;
        uint _movementFrame;
        double _orderAtMs;
        double _goalX, _goalY;
        bool _goalHeld;
        PathPoint[] _path = NoPath;
        int _pathIndex;
        double _walkFromX, _walkFromY;
        // The owner's published move-speed mirror (speed_milli / 1000, floored), 1.0 without an owner block.
        double _speedMult = 1.0;

        // Attack pause windows in SERVER ms: the walk of the server step ending at T is stopped when (from, until] covers it.
        // At most one attack is in flight; a few slots keep a window whose shot just landed while the next is announced.
        const int PauseSlots = 4;
        readonly double[] _pauseFrom = new double[PauseSlots], _pauseUntil = new double[PauseSlots];
        // A bow draw is not cancelled by a stagger (rh-sim stagger_cancel leaves a Draw alone); a cast or swing is.
        readonly bool[] _pauseDraw = new bool[PauseSlots];
        int _pauseCount;
        uint _lastDraw;
        int _lastWeapon = -1;

        // Model pose history (local ms, x, y), oldest first in a fixed ring: where the model was, for delay alignment.
        readonly double[] _histT = new double[HistorySize], _histX = new double[HistorySize], _histY = new double[HistorySize];
        int _histStart, _histCount;
        // Orders the server has not applied yet, in issue order (fixed ring), and whether each changes when the fighter moves.
        readonly byte[] _ordSeq = new byte[OrderRingSize];
        readonly double[] _ordAt = new double[OrderRingSize];
        readonly bool[] _ordTiming = new bool[OrderRingSize];
        readonly WireCommand[] _ordCommand = new WireCommand[OrderRingSize];
        readonly uint[] _ordFrame = new uint[OrderRingSize];
        readonly bool[] _ordSent = new bool[OrderRingSize];
        int _ordStart, _ordCount;
        // The measured delay of the last timing order: a sample at time T is compared with the model at T - _align.
        double _align;
        // Sample time base: the source clock's spacing, anchored to the earliest local arrival.
        bool _hasStamp;
        double _stampMs, _stampServerMs;
        const double MaxStampLagMs = 150;

        public bool Initialized { get; private set; }

        /// <summary>
        /// Whether the walk mirrors the server's attack pause (default on). Off reproduces the unpredicted walk, which is how the
        /// tests measure the pull-back the mirror removes; a presentation never turns it off.
        /// </summary>
        public bool PredictAttackPause { get; set; } = true;

        /// <summary>Attack pause windows currently held (diagnostic).</summary>
        public int AttackPauseWindows { get { return _pauseCount; } }

        /// <summary>The last accepted authoritative self position (the server's x; updated on every snapshot).</summary>
        public float SimX { get { return (float)_simX; } }
        public float SimY { get { return (float)_simY; } }

        /// <summary>The correction offset from the authoritative position: <c>drawn - sim</c>. Zero when nothing is being corrected.</summary>
        public float VisualX { get { return (float)((_modelX + _corrX) - _simX); } }
        public float VisualY { get { return (float)((_modelY + _corrY) - _simY); } }

        /// <summary>The presented position: <c>sim + visual</c>, i.e. the predicted walk plus the decaying correction.</summary>
        public float DrawnX { get { return SimX + VisualX; } }
        public float DrawnY { get { return SimY + VisualY; } }

        /// <summary>True from an order until the server's <c>move_seq</c> echo (or the one-second expiry) clears it.</summary>
        public bool OrderPending { get { return _pending; } }

        /// <summary>Reconciliations that found an error past <see cref="SnapDistance"/> and moved the model to the server.</summary>
        public int Snaps { get; private set; }
        /// <summary>Orders forgotten after <see cref="OrderExpireMs"/> without a matching echo.</summary>
        public int ExpiredOrders { get; private set; }
        /// <summary>Reconciliation samples compared with the server (those not masked by an unapplied order), diagnostic only.</summary>
        public int ReconSamples { get; private set; }
        /// <summary>Samples whose model-versus-server error exceeded <see cref="ReconCorrectionU"/>: a visible correction was queued.</summary>
        public int ReconCorrections { get; private set; }
        /// <summary>Samples whose error exceeded <see cref="ReconBigU"/> (a correction a player could notice).</summary>
        public int ReconBig { get; private set; }
        /// <summary>Largest and summed model-versus-server error over <see cref="ReconSamples"/> (sim units).</summary>
        public double ReconMaxErr { get; private set; }
        public double ReconErrSum { get; private set; }
        public const double ReconCorrectionU = 0.5, ReconBigU = 4.0;
        /// <summary>Clears the reconciliation counters (a proof run starts them at the moment it begins walking).</summary>
        public void ResetReconStats() { ReconSamples = ReconCorrections = ReconBig = 0; ReconMaxErr = ReconErrSum = 0; }

        /// <summary>Samples in a row more than <see cref="DivergeErr"/> from the model while reconciliation was active.</summary>
        public int ConsecutiveOutOfToleranceSamples { get; private set; }

        /// <summary>Keeps the map the server walks; the Phase 1 pathfinder builds its navigation tables once for it.</summary>
        public LocalMotion(MapBlob map) { _map = map; _walkMap = map; _predictionSuspended = map == null; }

        /// <summary>
        /// Stops prediction when the server changes collision geometry that this client cannot represent (for example locked
        /// gates without public gate geometry). Authoritative snapshots keep displaying immediately. The session adapter must
        /// call this before another predicted frame when that condition becomes known.
        /// </summary>
        public void SuspendPrediction()
        {
            _predictionSuspended = true;
            _pending = false;
            ClearNonMovementAcks();
            _goalHeld = false;
            ClearPath();
            _modelX = _simX;
            _modelY = _simY;
            _corrX = _corrY = 0;
            ConsecutiveOutOfToleranceSamples = 0;
            _histCount = 0;
            _ordCount = 0;
        }

        /// <summary>
        /// Supplies the actual current public collision map and resumes prediction. Call after replacing or mutating map
        /// tiles; supplying the same object explicitly invalidates its navigation cache. It never reconstructs gate geometry.
        /// </summary>
        public void SetCollisionMap(MapBlob map)
        {
            SuspendPrediction();
            _map = map;
            _pathfinder.InvalidateMap();
            RebuildWalkMap(); // the walls held stand on the new base map too
            _predictionSuspended = map == null;
        }

        /// <summary>
        /// Records a move order and starts predicting the walk from the local model position (which is the accepted authoritative
        /// position whenever the walk is settled, so the plan root is where the server will apply the order). <paramref name="seq"/>
        /// 0 means the input was dropped and is ignored.
        /// </summary>
        public void Order(float x, float y, byte seq, double nowMs) { Order(x, y, seq, nowMs, null); }

        internal void Order(float x, float y, byte seq, double nowMs, WireCommand order)
        {
            if (seq == 0 || _predictionSuspended || Initialized && !_alive) return;
            ExpireOrders(nowMs);
            ClearNonMovementAcks();
            _movementOrder = order;
            _movementSent = false;
            _lastSeq = seq;
            _pending = true;
            _orderAtMs = nowMs;
            _goalX = x;
            _goalY = y;
            _goalHeld = true;
            MarkStepClock(nowMs);
            bool timing = true;
            if (Initialized)
            {
                // Same-direction re-sends (a held key) keep the walk's timing: only a start, a stop or a turn re-measures it.
                bool walking = _pathIndex < _path.Length;
                double ox = walking ? _path[_pathIndex].X - _modelX : 0, oy = walking ? _path[_pathIndex].Y - _modelY : 0;
                PlanFrom(_modelX, _modelY);
                if (walking && _path.Length > 0)
                {
                    double nx = _path[0].X - _modelX, ny = _path[0].Y - _modelY;
                    double norm = Math.Sqrt(ox * ox + oy * oy) * Math.Sqrt(nx * nx + ny * ny);
                    timing = !(norm > ZeroSegmentSq) || (ox * nx + oy * ny) / norm < SameDirectionCos;
                }
                RecordPose();
            }
            else ClearPath();
            NoteOrder(seq, nowMs, timing, order);
        }

        /// <summary>Records a stop order: the predicted walk ends now and waits for the stop's echo. Sequence 0 is ignored.</summary>
        public void StopOrder(byte seq, double nowMs) { StopOrder(seq, nowMs, null); }

        internal void StopOrder(byte seq, double nowMs, WireCommand order)
        {
            if (seq == 0 || _predictionSuspended || Initialized && !_alive) return;
            ExpireOrders(nowMs);
            ClearNonMovementAcks();
            _movementOrder = order;
            _movementSent = false;
            _lastSeq = seq;
            _pending = true;
            _orderAtMs = nowMs;
            _goalHeld = false;
            ClearPath();
            MarkStepClock(nowMs);
            if (Initialized) RecordPose();
            NoteOrder(seq, nowMs, true, order);
        }

        /// <summary>
        /// Remembers a nonmovement order after the outstanding move/stop. Cast echoes do not change the walk's goal,
        /// path, pending age or step clock. Membership, rather than numeric byte ordering, handles wrap and coalescing.
        /// </summary>
        internal void NoteNonMovementOrder(WireCommand command, double nowMs)
        {
            ExpireOrders(nowMs);
            byte seq = command.Seq.GetValueOrDefault();
            // A reused byte equal to the currently held echo cannot prove a new cast was applied. In that ambiguous
            // case keep waiting for the movement echo/normal expiry instead of guessing from a repeated snapshot.
            if (_pending && seq != 0 && seq != _observedSeq)
            {
                _nonMovementAcks[seq] = command;
                _sentNonMovementAcks[seq] = false;
            }
        }

        internal void NonMovementOrderSent(WireCommand command, uint frame)
        {
            byte seq = command.Seq.GetValueOrDefault();
            if (!ReferenceEquals(_nonMovementAcks[seq], command)) return;
            _sentNonMovementAcks[seq] = true;
            _nonMovementFrames[seq] = frame;
        }

        internal void MovementOrderSent(WireCommand command, uint frame)
        {
            // A later held-key repeat may already be newest when an earlier motion's echo
            // arrives. Keep the actual sent-frame proof per order, not only for the newest one.
            for (int n = 0; n < _ordCount; n++)
            {
                int i = (_ordStart + n) % OrderRingSize;
                if (_ordCommand[i] == null || !ReferenceEquals(_ordCommand[i], command)) continue;
                _ordFrame[i] = frame;
                _ordSent[i] = true;
            }
            if (!ReferenceEquals(_movementOrder, command)) return;
            _movementSent = true;
            _movementFrame = frame;
        }

        void ClearNonMovementAcks()
        {
            Array.Clear(_nonMovementAcks, 0, _nonMovementAcks.Length);
            Array.Clear(_sentNonMovementAcks, 0, _sentNonMovementAcks.Length);
            _movementOrder = null;
            _movementSent = false;
        }

        /// <summary>
        /// Rebases presentation integration at an authoritative source-clock cut. Orders and their expiry are measured
        /// on the injected local clock, so their goal/path/ack bookkeeping survives without a resend or a fresh lifetime.
        /// </summary>
        internal void RebasePresentationClock(double nowMs)
        {
            _lastStepMs = nowMs;
            _stepClock = true;
            _hasStamp = false; // the source clock was cut: the next sample re-anchors the sample time base
            _pauseCount = 0;   // and server-time windows from the old clock mean nothing
            _lastDraw = 0;
        }

        /// <summary>
        /// Presentation tick: advance the predicted walk by the elapsed time (at <see cref="PlayerSpeed"/> times
        /// <see cref="SpeedModifier"/>) and fade the correction offset. Call once per frame with the injected monotonic clock.
        /// </summary>
        public void Step(double nowMs)
        {
            if (!_stepClock) { _lastStepMs = nowMs; _stepClock = true; }
            double dt = nowMs - _lastStepMs;
            _lastStepMs = nowMs;
            if (!(dt > 0)) dt = 0; // also guards NaN and a clock that resynced backwards
            ExpireOrders(nowMs);
            if (dt <= 0) return;
            if (Initialized && _alive && !_predictionSuspended && _pathIndex < _path.Length)
            {
                Walk(Math.Min(dt, MaxStepMs) / 1000.0, nowMs); // a stall must not walk the model through a wall
                RecordPose();
            }
            Decay(dt); // the correction fades with the real elapsed time, not the capped walk time
        }

        /// <summary>
        /// Applies an accepted authoritative self sample. <see cref="SimX"/>/<see cref="SimY"/> always move to it (so the server's
        /// position stays visible while an order is pending) and the sample is reconciled against the model's pose one order delay
        /// earlier (see the type). The echo is checked before the one-second expiry, so a sample at the boundary acknowledges the
        /// order instead of counting it as forgotten. <paramref name="serverMs"/> is the sample's source time (its spacing times
        /// the samples; without it the arrival time is used) and <paramref name="speedMilli"/> the owner block's
        /// <c>speed_milli</c> (null: no owner block, base speed).
        /// </summary>
        public void OnSnapshot(WireSelf me, double nowMs, uint? ackInput = null, double? serverMs = null, double? speedMilli = null)
        {
            if (me == null) { Clear(); return; }
            double stamp = SampleStamp(nowMs, serverMs);
            NoteEcho(me.MoveSeq, stamp, ackInput);
            bool alive = me.Status == 1;
            if (!alive) { _pauseCount = 0; _lastDraw = 0; }
            NoteSelfAttackFacts(me, serverMs);
            bool hardCut = Initialized && (me.DisplaceSeq != _displaceSeq || !alive || !_alive);
            // Echo before advancing the clock: an acknowledgement at the expiry boundary is not a lost order.
            bool castAck = ackInput.HasValue && _nonMovementAcks[me.MoveSeq] != null && _sentNonMovementAcks[me.MoveSeq] &&
                unchecked((int)(ackInput.Value - _nonMovementFrames[me.MoveSeq])) >= 0;
            bool moveAck = me.MoveSeq == _lastSeq && (!ackInput.HasValue ||
                _movementSent && unchecked((int)(ackInput.Value - _movementFrame)) >= 0);
            if (_pending && (moveAck || castAck))
            {
                _pending = false;
                ClearNonMovementAcks();
                _ordCount = 0; // the newest order is handled, so every earlier one is too
            }
            if (!Initialized && _pending) ExpireOrders(nowMs);
            // Real Session.Poll delivers snapshots before its frame Step. Spend elapsed walk time against the old model
            // first; the frame's later Step(same now) then spends zero. Direct callers that already stepped also spend zero.
            if (Initialized && !hardCut && !_predictionSuspended) Step(nowMs);
            // The walk already spent used the speed known until now; this sample's mirror drives the walk from here on.
            _speedMult = WalkMultiplier(speedMilli);
            bool first = !Initialized;
            _simX = me.X;
            _simY = me.Y;
            _alive = alive;
            _displaceSeq = me.DisplaceSeq;
            _observedSeq = me.MoveSeq;
            if (first || hardCut || _predictionSuspended)
            {
                Initialized = true;
                _modelX = _simX;
                _modelY = _simY;
                _corrX = _corrY = 0;
                ConsecutiveOutOfToleranceSamples = 0;
                MarkStepClock(nowMs);
                _histCount = 0;
                if (hardCut || !alive || _predictionSuspended)
                {
                    _pending = false;
                    ClearNonMovementAcks();
                    _ordCount = 0;
                    _goalHeld = false;
                    ClearPath();
                }
                else if (_pending && _goalHeld) PlanFrom(_simX, _simY);
                return;
            }
            Reconcile(stamp, nowMs);
        }

        /// <summary>
        /// A stun or a weapon change in the self block ends the attack pause: both cancel the pending action in the input or status
        /// phase, before that tick's multiplier, and neither has an fx of its own. They apply at this sample's tick, and the earlier
        /// step is the last one the pause covers.
        /// </summary>
        void NoteSelfAttackFacts(WireSelf me, double? serverMs)
        {
            bool stunned = (me.Effects & 1) != 0;
            bool changed = _lastWeapon >= 0 && _lastWeapon != me.Weapon;
            _lastWeapon = me.Weapon;
            if ((stunned || changed) && _pauseCount > 0 && serverMs.HasValue && PredictAttackPause) EndPauseAt(serverMs.Value - TickMs, true);
        }

        /// <summary>Clears the presented self, any pending order and the walk (a snapshot without this player, or a fresh Welcome).</summary>
        public void Clear()
        {
            Initialized = false;
            _simX = _simY = 0;
            _modelX = _modelY = 0;
            _corrX = _corrY = 0;
            _lastStepMs = 0;
            _stepClock = false;
            _alive = false;
            _displaceSeq = 0;
            _pending = false;
            ClearNonMovementAcks();
            _lastSeq = 0;
            _observedSeq = 0;
            _movementOrder = null;
            _movementSent = false;
            _orderAtMs = 0;
            _goalX = _goalY = 0;
            _goalHeld = false;
            ClearPath();
            _speedMult = 1.0;
            _histCount = 0;
            _ordCount = 0;
            _align = 0;
            _hasStamp = false;
            _pauseCount = 0;
            _lastDraw = 0;
            _lastWeapon = -1;
            Snaps = 0;
            ExpiredOrders = 0;
            ConsecutiveOutOfToleranceSamples = 0;
        }

        /// <summary>
        /// The walk-speed multiplier for what the client cannot see (1.0), applied on top of the published owner move-speed mirror
        /// (<c>speed_milli / 1000</c>): hidden weapon windup, roots, slows and terrain are not guessed, because guessing them
        /// would show a walk the server is not doing (ADR 0008). The server corrects the difference. P-NET-2 may extend the body;
        /// callers should go through <see cref="Step"/>.
        /// </summary>
        public static double SpeedModifier(double nowMs) { return 1.0; }
    }
}
