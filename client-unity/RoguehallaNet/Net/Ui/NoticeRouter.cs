using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Ui
{
    public enum NoticeLane : byte { None = 0, Event = 1, Feedback = 2 }

    public enum NoticeTone : byte { Info = 0, Good = 1, Warn = 2, Bad = 3 }

    /// <summary>Where one notice goes. Text is null when the Unity side supplies the words (potion notices).
    /// Key is null, or the family whose line this notice replaces in place.</summary>
    public readonly struct NoticeRoute
    {
        public readonly NoticeLane Lane;
        public readonly NoticeTone Tone;
        public readonly string Text;
        public readonly string Key;

        public NoticeRoute(NoticeLane lane, NoticeTone tone, string text, string key) { Lane = lane; Tone = tone; Text = text; Key = key; }
    }

    /// <summary>Decides, for one delivered server event, which toast lane shows it, in which tone and with which words.</summary>
    public static class NoticeRouter
    {
        public const int MaxChars = 80;
        public const string KillStackKey = "kill-stack";

        /// <summary>Lane None for kinds that show no toast.</summary>
        public static NoticeRoute Route(WireEvent item) => default(NoticeRoute);

        /// <summary>clamp(2.2 + 0.045 * max(0, characters - 20), 2.2, 4.5) seconds.</summary>
        public static float HoldSeconds(string text) => 0f;

        /// <summary>At most MaxChars characters, the last one "…" when cut.</summary>
        public static string Short(string text) => text;
    }
}
