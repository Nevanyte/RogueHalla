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

    /// <summary>Plain facts read from accepted server data (see <see cref="TutorialObserver"/>).</summary>
    public struct TutorialObservation
    {
        public bool HasSelf;
        public float X, Y;
        public WeaponId Weapon;
        public bool HasInventory;
        public byte ActiveSlot;
        public WeaponId? Slot0, Slot1;
        public bool WishBusy;
        public bool HasDummy; public uint DummyId; public float DummyX, DummyY;
        public bool HasRack; public uint RackId; public float RackX, RackY;
        public bool HitDummy;
    }

    /// <summary>STUB (P-TUTORIAL-2): signatures only; the implementer writes the state machine.</summary>
    public sealed class TutorialFlow
    {
        public const int Version = 1;
        public const int StepCount = 7;
        public const float RingRadius = 48f, RingDistance = 120f;

        public TutorialStep Step => TutorialStep.Welcome;
        public bool Running => false;
        public bool Done => false;
        public bool Skipped => false;
        public string Copy => "";
        public TutorialTarget Target => TutorialTarget.None;

        public bool Start() => false;
        public bool Evaluate(TutorialObservation observation) => false;
        public void Skip() { }
        public void Restart() { }
        public void ResumeAt(int step) { }
    }
}
