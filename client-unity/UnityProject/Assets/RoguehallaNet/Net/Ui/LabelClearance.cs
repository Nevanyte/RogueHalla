namespace Roguehalla.Net.Ui
{
    /// <summary>A rectangle in world units. Stub (P-UI-WORLD-LABELS-1): the implementer replaces every body.</summary>
    public readonly struct LabelBox
    {
        public readonly float X0, Y0, X1, Y1;
        public LabelBox(float x0, float y0, float x1, float y1) { X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; }
        public static LabelBox Centred(float cx, float cy, float width, float height) { return default; }
        public bool Meets(in LabelBox other, float pad) { return false; }
    }

    /// <summary>One label's clearance state. Stub.</summary>
    public struct LabelClearance
    {
        public const float HidePad = .10f, KeepPad = .40f, FadeSeconds = .15f;
        float _alpha;
        public float Alpha { get { return Has ? _alpha : 1f; } }
        public bool Hidden { get; private set; }
        public bool Has { get; private set; }
        public void Forget() { }
        public float Next(in LabelBox label, bool haveCluster, in LabelBox cluster, float dt, bool reduced) { return 1f; }
    }

    /// <summary>The clearance states kept by object. Stub.</summary>
    public sealed class LabelClearanceBook
    {
        public const int Capacity = 64;
        public const float SameSpot = .01f;
        public int Count { get { return 0; } }
        public void Begin() { }
        public void Clear() { }
        public float Next(int kind, float x, float y, in LabelBox label, bool haveCluster, in LabelBox cluster, float dt, bool reduced) { return 1f; }
        public float Keep(int kind, float x, float y) { return 1f; }
    }

    /// <summary>Keeps the last cluster rectangle through one false frame. Stub.</summary>
    public struct ClusterHold
    {
        public bool Read(bool answered, in LabelBox rect, out LabelBox use) { use = default; return false; }
        public void Reset() { }
    }
}
