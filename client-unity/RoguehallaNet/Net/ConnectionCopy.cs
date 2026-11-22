using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>
    /// Player-facing wording for rejects and terminal connection states (P-ALPHA-CLIENT-1). STUB: returns raw text so the
    /// oracle fails on assertions; the implementer replaces the bodies.
    /// </summary>
    public static class ConnectionCopy
    {
        public static string ForReject(RejectCode code, string reason) { return reason ?? ""; }
        public static string ForFailure(FailureKind kind) { return ""; }
        /// <summary>The line for a session that is over: Rejected, Failed or Ended; "" for any other state.</summary>
        public static string ForState(SessionState state, RejectCode? code, string reason, FailureKind kind) { return ""; }
    }
}
