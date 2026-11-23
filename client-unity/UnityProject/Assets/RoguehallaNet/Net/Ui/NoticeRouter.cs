using Roguehalla.Net.Protocol;

namespace Roguehalla.Net.Ui
{
    /// <summary>The toast lane a notice goes to; None shows no toast.</summary>
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

        static NoticeRoute None => default(NoticeRoute);

        /// <summary>Lane None for kinds that show no toast.</summary>
        public static NoticeRoute Route(WireEvent item)
        {
            if (item == null) return None;
            switch (item.Kind)
            {
                case EventKind.Toast: return RouteToast(item.ToastText);
                case EventKind.PotionNotice:
                    switch ((PotionNotice)item.PotionNotice)
                    {
                        case PotionNotice.Stored: case PotionNotice.LargeStored:
                            return new NoticeRoute(NoticeLane.Event, NoticeTone.Good, null, null);
                        case PotionNotice.Used: case PotionNotice.LargeUsed:
                            return new NoticeRoute(NoticeLane.Feedback, NoticeTone.Good, null, null);
                        case PotionNotice.BagFull: case PotionNotice.CampBagFull: case PotionNotice.Blocked:
                        case PotionNotice.Busy: case PotionNotice.Full: case PotionNotice.Empty:
                            return new NoticeRoute(NoticeLane.Feedback, NoticeTone.Warn, null, null);
                        default: return None;
                    }
                case EventKind.PickupRefused: return new NoticeRoute(NoticeLane.Feedback, NoticeTone.Warn, "Can't pick that up", null);
                case EventKind.ChestRefused: return new NoticeRoute(NoticeLane.Feedback, NoticeTone.Warn, "That choice was refused", null);
                case EventKind.WindEmblem: return new NoticeRoute(NoticeLane.Event, NoticeTone.Info, "Wind emblem", null);
                case EventKind.CannonNotice:
                    switch (item.CannonCode)
                    {
                        case 1: return new NoticeRoute(NoticeLane.Feedback, NoticeTone.Warn, "Cannon sealed", null);
                        case 2: return new NoticeRoute(NoticeLane.Feedback, NoticeTone.Warn, "Cannot enter cannon", null);
                        case 3: return new NoticeRoute(NoticeLane.Event, NoticeTone.Info, "Cannon blast", null);
                        default:
                            return new NoticeRoute(NoticeLane.Feedback, NoticeTone.Info,
                                item.CannonSeconds.HasValue ? "Cannon reloading · " + item.CannonSeconds.Value + "s" : "Cannon reloading", null);
                    }
                default: return None;
            }
        }

        static NoticeRoute RouteToast(string text)
        {
            if (string.IsNullOrEmpty(text)) return None;
            string shown = Short(text);
            if (text.StartsWith("Quest complete", System.StringComparison.Ordinal)) return new NoticeRoute(NoticeLane.Event, NoticeTone.Good, shown, null);
            if (text.StartsWith("Quest accepted", System.StringComparison.Ordinal) || text.StartsWith("Accepted:", System.StringComparison.Ordinal))
                return new NoticeRoute(NoticeLane.Event, NoticeTone.Info, shown, null);
            if (text.StartsWith("Stat trade", System.StringComparison.Ordinal)) return new NoticeRoute(NoticeLane.Event, NoticeTone.Good, shown, null);
            if (text.IndexOf("kill stack", System.StringComparison.Ordinal) >= 0) return new NoticeRoute(NoticeLane.Event, NoticeTone.Good, shown, KillStackKey);
            return new NoticeRoute(NoticeLane.Event, NoticeTone.Info, shown, null);
        }

        /// <summary>clamp(2.2 + 0.045 * max(0, characters - 20), 2.2, 4.5) seconds.</summary>
        public static float HoldSeconds(string text)
        {
            int n = text == null ? 0 : text.Length;
            float hold = 2.2f + .045f * (n > 20 ? n - 20 : 0);
            return hold < 2.2f ? 2.2f : hold > 4.5f ? 4.5f : hold;
        }

        /// <summary>At most MaxChars characters, the last one "…" when cut.</summary>
        public static string Short(string text)
        {
            if (text == null || text.Length <= MaxChars) return text;
            return text.Substring(0, MaxChars - 1) + "…";
        }
    }
}
