namespace Roguehalla.Net
{
    /// <summary>The phase a match is in as the wire reports it: lobby, active, ended or planning.</summary>
    public enum MatchPhase : byte { Lobby = 0, Active = 1, Ended = 2, Planning = 3 }
}
