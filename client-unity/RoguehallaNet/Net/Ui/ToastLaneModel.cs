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

        enum Phase : byte { FadeIn, Hold, Leaving }

        sealed class Item
        {
            public string BaseText, Display, Key;
            public int Count = 1;
            public NoticeTone Tone;
            public bool Sticky;
            public Phase Phase;
            public float Fade;        // seconds into the fade in, or seconds left of the fade out
            public float HoldLeft, Held, Alpha, Slot;
        }

        readonly int _capacity;
        readonly System.Collections.Generic.List<Item> _lines = new System.Collections.Generic.List<Item>();
        readonly System.Collections.Generic.List<Item> _waiting = new System.Collections.Generic.List<Item>();

        public ToastLaneModel(int capacity) { _capacity = capacity < 1 ? 1 : capacity; }

        static string Display(Item i) => i.Count > 1 ? i.BaseText + " x" + i.Count : i.BaseText;

        /// <summary>Null or empty text is ignored.</summary>
        public void Push(string text, NoticeTone tone, string key = null, float holdSeconds = 0f, bool dropIfLate = false)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (key != null)
            {
                var hit = Find(_lines, null, key) ?? Find(_waiting, null, key);
                if (hit != null)
                {
                    hit.BaseText = text; hit.Tone = tone; hit.Count = 1; hit.Display = text;
                    Restart(hit);
                    return;
                }
            }
            else
            {
                var hit = Find(_lines, text, null) ?? Find(_waiting, text, null);
                if (hit != null)
                {
                    hit.Count++; hit.Display = Display(hit);
                    Restart(hit);
                    return;
                }
            }
            var item = new Item { BaseText = text, Display = text, Key = key, Tone = tone, HoldLeft = NoticeRouter.HoldSeconds(text) };
            if (_waiting.Count == 0 && _lines.Count < _capacity) { item.Slot = _lines.Count; _lines.Add(item); }
            else
            {
                _waiting.Add(item);
                if (_waiting.Count > MaxWaiting) _waiting.RemoveAt(0);
            }
        }

        static Item Find(System.Collections.Generic.List<Item> list, string text, string key)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                if (it.Sticky) continue;
                if (key != null ? it.Key == key : (it.Key == null && it.BaseText == text)) return it;
            }
            return null;
        }

        static void Restart(Item item)
        {
            item.HoldLeft = NoticeRouter.HoldSeconds(item.BaseText);
            item.Held = 0f;
            if (item.Phase == Phase.Leaving) Revive(item);
        }

        static void Revive(Item item)
        {
            item.Phase = Phase.FadeIn;
            item.Fade = item.Alpha * FadeInSeconds;
        }

        /// <summary>Null or empty clears it; a sticky line takes slot 0 and counts toward capacity.</summary>
        public void SetSticky(string text, NoticeTone tone)
        {
            Item sticky = _lines.Count > 0 && _lines[0].Sticky ? _lines[0] : null;
            if (string.IsNullOrEmpty(text))
            {
                if (sticky != null && sticky.Phase != Phase.Leaving) { sticky.Phase = Phase.Leaving; sticky.Fade = sticky.Alpha * FadeOutSeconds; }
                return;
            }
            if (sticky != null)
            {
                sticky.BaseText = sticky.Display = text; sticky.Tone = tone;
                if (sticky.Phase == Phase.Leaving) Revive(sticky);
                return;
            }
            _lines.Insert(0, new Item { BaseText = text, Display = text, Tone = tone, Sticky = true, HoldLeft = float.PositiveInfinity });
        }

        /// <summary>Starts the fade out of a showing, non-sticky line with this key. STUB: does nothing.</summary>
        public void End(string key) { }

        /// <summary>Timers run only while visible.</summary>
        public void Tick(float dt, bool visible, bool reducedMotion)
        {
            if (!visible) return;
            for (int i = 0; i < _lines.Count; i++) Advance(_lines[i], dt, reducedMotion);
            RemoveGone(reducedMotion);
            if (_waiting.Count > 0 && _lines.Count >= _capacity && !AnyLeaving())
            {
                for (int i = 0; i < _lines.Count; i++)
                {
                    var it = _lines[i];
                    if (it.Sticky) continue;
                    if (it.Phase == Phase.Hold && it.Held >= MinShowSeconds) { it.Phase = Phase.Leaving; it.Fade = FadeOutSeconds; it.Alpha = 1f; }
                    break;
                }
                RemoveGone(reducedMotion);
            }
            while (_waiting.Count > 0 && _lines.Count < _capacity)
            {
                var it = _waiting[0]; _waiting.RemoveAt(0);
                it.Slot = _lines.Count; _lines.Add(it);
                Advance(it, 0f, reducedMotion);
            }
            float step = dt / GlideSeconds;
            for (int i = 0; i < _lines.Count; i++)
            {
                var it = _lines[i];
                if (reducedMotion || it.Slot == i) { it.Slot = i; continue; }
                float diff = i - it.Slot;
                it.Slot = diff > step ? it.Slot + step : diff < -step ? it.Slot - step : i;
            }
        }

        bool AnyLeaving()
        {
            for (int i = 0; i < _lines.Count; i++) if (_lines[i].Phase == Phase.Leaving) return true;
            return false;
        }

        static void Advance(Item it, float dt, bool reduced)
        {
            switch (it.Phase)
            {
                case Phase.FadeIn:
                    it.Fade += dt;
                    if (reduced || it.Fade >= FadeInSeconds) { it.Phase = Phase.Hold; it.Alpha = 1f; }
                    else it.Alpha = it.Fade / FadeInSeconds;
                    break;
                case Phase.Hold:
                    it.Held += dt;
                    it.HoldLeft -= dt;
                    if (it.HoldLeft <= 0f) { it.Phase = Phase.Leaving; it.Fade = FadeOutSeconds; it.Alpha = 1f; }
                    break;
                case Phase.Leaving:
                    it.Fade -= dt;
                    it.Alpha = it.Fade > 0f ? it.Fade / FadeOutSeconds : 0f;
                    break;
            }
        }

        void RemoveGone(bool reduced)
        {
            for (int i = _lines.Count - 1; i >= 0; i--)
                if (_lines[i].Phase == Phase.Leaving && (reduced || _lines[i].Fade <= 0f)) _lines.RemoveAt(i);
        }

        public void Clear() { _lines.Clear(); _waiting.Clear(); }

        /// <summary>Lines that are drawn (alpha may be 0 on the first frame).</summary>
        public int Count => _lines.Count;

        /// <summary>Index 0 is the oldest drawn line.</summary>
        public ToastLine Line(int index)
        {
            var it = _lines[index];
            return new ToastLine(it.Display, it.Count, it.Tone, it.Alpha, it.Slot, it.Sticky);
        }

        public int Waiting => _waiting.Count;
    }
}
