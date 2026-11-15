using System;

namespace Roguehalla.Net
{
    public enum TutorialStep { Welcome = 0, Move = 1, Pickup = 2, Hit = 3, Swap = 4, Wish = 5, Done = 6 }
    public enum TutorialTargetKind { None, Point, Actor, Container }

    /// <summary>Where the guide arrow points. X/Y are the last observed world position (server units).</summary>
    public readonly struct TutorialTarget
    {
        public TutorialTargetKind Kind { get; }
        public uint Id { get; }
        public float X { get; }
        public float Y { get; }
        public float Radius { get; }
        public TutorialTarget(TutorialTargetKind kind, uint id, float x, float y, float radius = 0f)
        { Kind = kind; Id = id; X = x; Y = y; Radius = radius; }
        public static TutorialTarget None => default(TutorialTarget);
    }

    /// <summary>Plain facts read from accepted server data (see <see cref="TutorialObserver"/>). Nothing here is simulated or inferred.</summary>
    public struct TutorialObservation
    {
        public bool HasSelf;
        public float X, Y;
        /// <summary>The active weapon (Fists when empty-handed).</summary>
        public WeaponId Weapon;
        public bool HasInventory;
        public byte ActiveSlot;
        public WeaponId? Slot0, Slot1;
        /// <summary>The owner's Wish is casting or recharging (cast record, a spent charge, or a future ready time).</summary>
        public bool WishBusy;
        public bool HasDummy; public uint DummyId; public float DummyX, DummyY;
        public bool HasRack; public uint RackId; public float RackX, RackY;
        /// <summary>The owner landed a hit on a plain training dummy since the previous observation (server Fx Hit).</summary>
        public bool HitDummy;
    }

    /// <summary>
    /// Tutorial progress as a pure state machine. Presentation only: it reads accepted server observations and never produces input.
    /// Steps complete only when an observation shows the change; skipping and restarting never touch the world.
    /// </summary>
    public sealed class TutorialFlow
    {
        public const int Version = 1;
        public const int StepCount = 7;
        public const float RingRadius = 48f, RingDistance = 120f;

        static readonly string[] Copies =
        {
            "Welcome to the Training grounds. Nothing here is recorded.",
            "Right-click to walk into the glowing ring.",
            "Walk to the weapon rack and press F to pick up a weapon.",
            "Left-click the dummy to lock on and attack. S stops.",
            "Take a second weapon, then swap with Q, 1 or 2.",
            "Press {wish} to cast your Wish (click to place it if it aims)",
            "Tutorial complete. Enter the Arena when ready.",
        };

        int _index;
        bool _skipped;
        bool _ringSet; float _ringX, _ringY;
        byte? _slotAtEntry;
        bool _wishWasBusy = true;
        TutorialTarget _target;

        public int Index { get { return _index; } }
        public TutorialStep Step { get { return (TutorialStep)_index; } }
        public bool Skipped { get { return _skipped; } }
        /// <summary>At the final card (completed).</summary>
        public bool Done { get { return !_skipped && _index == StepCount - 1; } }
        /// <summary>True while steps are being shown (not skipped, not at the final Done card).</summary>
        public bool Running { get { return !_skipped && _index < StepCount - 1; } }
        /// <summary>One-line copy for the current step. Key names are placeholders the UI fills: {wish}.</summary>
        public string Copy { get { return Copies[_index]; } }
        public TutorialTarget Target { get { return _target; } }

        public void Restart() { _skipped = false; Enter(0); }
        public void Skip() { if (!Done) _skipped = true; _target = TutorialTarget.None; }

        /// <summary>Resume at a stored step. Out-of-range values clamp; a step at or past Done lands on Done.</summary>
        public void ResumeAt(int step) { _skipped = false; Enter(step < 0 ? 0 : step >= StepCount ? StepCount - 1 : step); }

        /// <summary>The explicit Start press on the Welcome card. No effect on any other step.</summary>
        public bool Start() { if (!Running || _index != 0) return false; Enter(1); return true; }

        /// <summary>Advance when the current step's predicate holds on this observation. Returns true when the step changed.</summary>
        public bool Evaluate(TutorialObservation o)
        {
            if (!Running || _index == 0) return false;
            int before = _index;
            switch ((TutorialStep)_index)
            {
                case TutorialStep.Move:
                    {
                        if (!o.HasSelf) break;
                        if (!_ringSet) SetRing(o);
                        _target = new TutorialTarget(TutorialTargetKind.Point, 0, _ringX, _ringY, RingRadius);
                        float dx = o.X - _ringX, dy = o.Y - _ringY;
                        if (dx * dx + dy * dy <= RingRadius * RingRadius) Enter(_index + 1, o);
                        break;
                    }
                case TutorialStep.Pickup:
                    // Negative control: an unchanged empty-handed loadout never completes this step.
                    if (o.HasSelf && (Armed(o.Weapon) || Armed(o.Slot0) || Armed(o.Slot1))) Enter(_index + 1, o);
                    else Retarget(o, false);
                    break;
                case TutorialStep.Hit:
                    if (o.HitDummy) Enter(_index + 1, o);
                    else Retarget(o, true);
                    break;
                case TutorialStep.Swap:
                    {
                        if (!o.HasInventory || !Armed(o.Slot0) || !Armed(o.Slot1)) { _slotAtEntry = null; Retarget(o, false); break; }
                        _target = TutorialTarget.None;
                        if (!_slotAtEntry.HasValue) _slotAtEntry = o.ActiveSlot;
                        else if (o.ActiveSlot != _slotAtEntry.Value) Enter(_index + 1, o);
                        break;
                    }
                case TutorialStep.Wish:
                    // Needs a not-busy to busy transition so a Wish already recharging at entry does not pass the step.
                    if (o.WishBusy && !_wishWasBusy) Enter(_index + 1, o);
                    else _wishWasBusy = o.WishBusy;
                    break;
            }
            return _index != before;
        }

        static bool Armed(WeaponId w) { return w != WeaponId.Fists; }
        static bool Armed(WeaponId? w) { return w.HasValue && w.Value != WeaponId.Fists; }

        void SetRing(TutorialObservation o)
        {
            // Aim the ring at the rack when it is known (so the path leads onward), else straight along +x.
            // Always RingDistance from the spawn, even when the rack is nearer.
            float ux = 1f, uy = 0f;
            if (o.HasRack)
            {
                float rx = o.RackX - o.X, ry = o.RackY - o.Y, len = (float)Math.Sqrt(rx * rx + ry * ry);
                if (len > 1f) { ux = rx / len; uy = ry / len; }
            }
            _ringX = o.X + ux * RingDistance; _ringY = o.Y + uy * RingDistance; _ringSet = true;
        }

        void Retarget(TutorialObservation o, bool dummy)
        {
            if (dummy) _target = o.HasDummy ? new TutorialTarget(TutorialTargetKind.Actor, o.DummyId, o.DummyX, o.DummyY) : TutorialTarget.None;
            else _target = o.HasRack ? new TutorialTarget(TutorialTargetKind.Container, o.RackId, o.RackX, o.RackY) : TutorialTarget.None;
        }

        void Enter(int index) { Enter(index, default(TutorialObservation)); }
        void Enter(int index, TutorialObservation o)
        {
            _index = index; _target = TutorialTarget.None; _slotAtEntry = null; _wishWasBusy = true;
            if (index == 1) _ringSet = false;
            if (index == 2) Retarget(o, false);
            if (index == 3) Retarget(o, true);
        }
    }
}
