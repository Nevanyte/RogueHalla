using System.Collections.Generic;
using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>
    /// Watching the rest of the match after a final death. The server decides what a spectator sees: once this seat's run has
    /// ended in death, a Target command names the player to watch (by roster id) and the server then sends the world as that
    /// player sees it (rh-sim <c>Sim::spectate_eye</c>), including that player as an actor row. When the asked-for player is
    /// not standing, the server watches the next standing player after it in id order (wrapping); <see cref="Pick"/> mirrors
    /// that rule over the delivered actors, which always include the watched player, so the client knows whom to follow.
    /// Presentation only: it decides nothing and every frame comes from the server.
    /// </summary>
    public sealed class Spectator
    {
        readonly List<uint> _alive = new List<uint>(16);

        /// <summary>True from <see cref="Start"/> until <see cref="Stop"/> or a revive.</summary>
        public bool Active { get; private set; }
        /// <summary>The roster id last asked for (0 when inactive).</summary>
        public uint Requested { get; private set; }
        /// <summary>The player being watched per the newest snapshot, 0 while nobody is (yet) delivered.</summary>
        public uint Watching { get; private set; }

        /// <summary>Starts watching: <paramref name="first"/> is the preferred player (e.g. the killer), or 0 for the first standing player.</summary>
        public void Start(Session session, uint first)
        {
            Active = true;
            Watching = 0;
            Ask(session, first == 0 ? 1u : first);
        }

        /// <summary>Moves on to the next standing player in id order.</summary>
        public void Next(Session session)
        {
            if (!Active) return;
            uint from = Watching != 0 ? Watching : Requested;
            Ask(session, from + 1);
        }

        public void Stop() { Active = false; Requested = 0; Watching = 0; }

        void Ask(Session session, uint id)
        {
            if (id == 0) id = 1;
            if (id == session.PlayerId) id++;
            Requested = id;
            session.Input.Target(id);
        }

        /// <summary>
        /// Per frame. Ends when the local player stands again (a Rebirth Egg revive): the server then sends its own view.
        /// Otherwise reads whom the newest snapshot follows.
        /// </summary>
        public void Update(Session session)
        {
            if (!Active) return;
            var me = session.Me;
            if (me != null && me.Status == ActorStatus.Alive) { Stop(); return; }
            var latest = session.Latest;
            _alive.Clear();
            if (latest != null)
                foreach (var a in latest.Actors)
                    if (a.Kind == (byte)ActorKind.Player && a.Status == (byte)ActorStatus.Alive && a.Id != session.PlayerId) _alive.Add(a.Id);
            Watching = Pick(Requested, _alive);
        }

        /// <summary>
        /// The server's rule over what it delivered: the requested player when it is a standing actor, else the first standing
        /// actor after it in id order, wrapping; 0 when none. <paramref name="aliveAscending"/> holds the delivered standing
        /// players' ids in ascending order (the wire's actor order).
        /// </summary>
        public static uint Pick(uint requested, IReadOnlyList<uint> aliveAscending)
        {
            if (aliveAscending.Count == 0) return 0;
            for (int i = 0; i < aliveAscending.Count; i++)
                if (aliveAscending[i] >= requested) return aliveAscending[i];
            return aliveAscending[0];
        }

        /// <summary>The roster id of a display name (the result's "killed by"), or 0.</summary>
        public static uint RosterIdOf(IReadOnlyList<RosterEntry> roster, string name, uint self)
        {
            if (string.IsNullOrEmpty(name) || roster == null) return 0;
            for (int i = 0; i < roster.Count; i++)
                if (roster[i].Id != self && roster[i].Name == name) return roster[i].Id;
            return 0;
        }
    }
}
