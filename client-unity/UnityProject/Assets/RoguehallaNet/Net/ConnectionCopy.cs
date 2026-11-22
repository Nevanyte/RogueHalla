using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>
    /// Player-facing wording for rejects and terminal connection states (P-ALPHA-CLIENT-1). The raw server reason and
    /// exception text never appear here: they stay in <c>Session.Error</c> / <c>RejectReason</c> for the log.
    /// </summary>
    public static class ConnectionCopy
    {
        const string Unreachable = "Couldn't reach the game server. Check your connection and try again.";

        public static string ForReject(RejectCode code, string reason)
        {
            switch (code)
            {
                case RejectCode.ServerDraining: return "The server is restarting. Try again in a minute.";
                case RejectCode.MatchFull: return "That match already started or is full. Join the next lobby.";
                case RejectCode.ProfileInUse: return "That callsign is already in a match.";
                case RejectCode.ProtocolMismatch:
                case RejectCode.ContentMismatch: return "The game was updated. Reload the page.";
                case RejectCode.RateLimited: return "Too many attempts. Wait a few seconds and try again.";
                case RejectCode.BadCredentials: return "Your sign-in was not accepted. Sign in again and retry.";
                case RejectCode.NoSuchSession: return "That session has expired. Start a new game.";
                case RejectCode.TooSlow: return "Your link was too slow to keep up. Try again.";
                case RejectCode.Kicked: return "You were removed from the match.";
                case RejectCode.Malformed: return "Something went wrong talking to the server. Reload the page and try again.";
                default: return "The server turned you away. Try again in a moment.";
            }
        }

        public static string ForFailure(FailureKind kind)
        {
            switch (kind)
            {
                case FailureKind.LostConnection: return "Lost connection to the server. Your run couldn't be recovered.";
                case FailureKind.ConnectTimeout: return "Couldn't reach the game server. It took too long to answer; try again.";
                default: return Unreachable;
            }
        }

        /// <summary>The line for a session that is over: Rejected, Failed or Ended; "" for any other state.</summary>
        public static string ForState(SessionState state, RejectCode? code, string reason, FailureKind kind)
        {
            switch (state)
            {
                case SessionState.Rejected: return ForReject(code ?? (RejectCode)0, reason);
                case SessionState.Failed: return ForFailure(kind);
                case SessionState.Ended: return "The match has ended.";
                default: return "";
            }
        }
    }
}
