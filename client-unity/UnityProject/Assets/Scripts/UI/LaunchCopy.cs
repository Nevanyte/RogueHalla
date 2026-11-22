using Roguehalla.Net;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Client
{
    /// <summary>One faction as the launch screen shows it. Every field is presentation text; none of it is a game rule.</summary>
    public readonly struct HouseInfo
    {
        public readonly string Name, Region, Weapon;
        public readonly MythicGlyph WeaponGlyph;
        /// <summary>The map-zone shape that carries the faction's identity without colour (same set as the minimap legend).</summary>
        public readonly MapGlyph Shape;

        public HouseInfo(string name, string region, string weapon, MythicGlyph weaponGlyph, MapGlyph shape)
        { Name = name; Region = region; Weapon = weapon; WeaponGlyph = weaponGlyph; Shape = shape; }
    }

    /// <summary>
    /// Player-facing wording for the launch screen. Pure functions of facts the client already holds (the launch targets, the callsign
    /// text, the session state); nothing here decides whether a launch is allowed, it only explains the decision made elsewhere.
    /// </summary>
    public static class LaunchCopy
    {
        /// <summary>Fire, Haunted, Plague, Frost, Holy display order. Stable faction ids and wire order still call Haunted Sentenced.</summary>
        public static readonly HouseInfo[] Houses =
        {
            new HouseInfo(Roguehalla.Net.Ui.FactionPresentation.Name(0), "Volcanic wastes", "Axes", MythicGlyph.Axe, MapGlyph.Triangle),
            new HouseInfo(Roguehalla.Net.Ui.FactionPresentation.Name(1), "Gothic graveyard", "Swords", MythicGlyph.Sword, MapGlyph.Diamond),
            new HouseInfo(Roguehalla.Net.Ui.FactionPresentation.Name(2), "Forgotten city", "Bows", MythicGlyph.Bow, MapGlyph.Trefoil),
            new HouseInfo(Roguehalla.Net.Ui.FactionPresentation.Name(3), "Frozen plains", "Shields", MythicGlyph.Shield, MapGlyph.Asterisk),
            new HouseInfo(Roguehalla.Net.Ui.FactionPresentation.Name(4), "Desert oasis", "Wands", MythicGlyph.Wand, MapGlyph.Ring),
        };

        public const string Tagline = "Last one standing wins.";

        /// <summary>Why a destination cannot be used, in a sentence a player can act on. Empty when it is available.</summary>
        public static string UnavailableText(MatchLaunchMode mode, MatchLaunchTarget target)
        {
            if (target == null || target.Available) return "";
            bool training = mode == MatchLaunchMode.Training;
            switch (target.Kind)
            {
                case LaunchUnavailableKind.OtherSite:
                    return "This link points at a game server on another site. Open the game from its main address to play.";
                case LaunchUnavailableKind.NoAccountService:
                    return "Accounts aren't reachable from this page, so a run can't start. Reload, or open the game from its main address.";
                case LaunchUnavailableKind.BadAddress:
                    return "This link's game server address isn't valid. Open the game from its main address.";
                default:
                    return training ? "Training isn't offered on this server." : "No arena server is set up for this page.";
            }
        }

        /// <summary>Null when the text is a usable callsign, otherwise the reason it is not (the same 2 to 16 character rule the account service applies).</summary>
        public static string CallsignProblem(string text)
        {
            string trimmed = string.IsNullOrEmpty(text) ? "" : text.Trim();
            if (trimmed.Length == 0) return "Type a callsign to play.";
            try { AccountEndpoint.NormalizeName(trimmed); }
            catch (System.ArgumentException) { return "Callsigns are 2 to 16 letters, numbers, underscores or hyphens."; }
            return null;
        }

        /// <summary>A launch that never reached a confirmed match, phrased for the player instead of the transport's own error text.</summary>
        public static string SessionFailureText(SessionState state, string rejectReason)
        {
            return SessionFailureText(state, null, rejectReason, FailureKind.Other);
        }

        /// <summary>Same, with the reject code and failure kind when the caller has them; the raw reason is never shown.</summary>
        public static string SessionFailureText(SessionState state, RejectCode? code, string rejectReason, FailureKind kind)
        {
            return ConnectionCopy.ForState(state, code, rejectReason, kind);
        }

        public static string PendingText(MatchLaunchMode mode)
        {
            return mode == MatchLaunchMode.Training ? "Opening your training room…" : "Joining the arena…";
        }
    }
}
