namespace Roguehalla.Net.Ui
{
    /// <summary>One drawn toast line. Slot 0 is the lane's first position, fractional while gliding.</summary>
    public readonly struct ToastLine
    {
        public readonly string Text;
        public readonly int Count;
        public readonly NoticeTone Tone;
        public readonly float Alpha;
        public readonly float Slot;
        public readonly bool Sticky;

        public ToastLine(string text, int count, NoticeTone tone, float alpha, float slot, bool sticky)
        { Text = text; Count = count; Tone = tone; Alpha = alpha; Slot = slot; Sticky = sticky; }
    }

    /// <summary>One toast lane: what lines show, how opaque each is and in which slot, as time passes.</summary>
    public sealed class ToastLaneModel
    {
        public const float FadeInSeconds = .12f, FadeOutSeconds = .25f, GlideSeconds = .12f, MinShowSeconds = 1f;
        public const int MaxWaiting = 6;

        public ToastLaneModel(int capacity) { }

        /// <summary>Null or empty text is ignored.</summary>
        public void Push(string text, NoticeTone tone, string key = null) { }

        /// <summary>Null or empty clears it; a sticky line takes slot 0 and counts toward capacity.</summary>
        public void SetSticky(string text, NoticeTone tone) { }

        /// <summary>Timers run only while visible.</summary>
        public void Tick(float dt, bool visible, bool reducedMotion) { }

        public void Clear() { }

        /// <summary>Lines that are drawn (alpha may be 0 on the first frame).</summary>
        public int Count => 0;

        /// <summary>Index 0 is the oldest drawn line.</summary>
        public ToastLine Line(int index) => default(ToastLine);

        public int Waiting => 0;
    }
}
