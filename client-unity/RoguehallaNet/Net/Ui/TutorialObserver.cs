using Roguehalla.Net.Protocol;

namespace Roguehalla.Net
{
    /// <summary>
    /// Builds a <see cref="TutorialObservation"/> from a session's accepted state. Read-only: it subscribes to the session's Fx only
    /// to remember the owner's latest landed hit, and it sends nothing.
    /// </summary>
    public sealed class TutorialObserver : System.IDisposable
    {
        Session _session;
        uint _hitVictim;

        public TutorialObserver(Session session) { _session = session; if (session != null) session.FxReceived += OnFx; }

        public void Dispose() { if (_session != null) _session.FxReceived -= OnFx; _session = null; }

        void OnFx(ServerMsg.Fx fx)
        {
            uint me = _session == null ? 0 : _session.PlayerId;
            if (me == 0) return;
            foreach (var f in fx.List) if (f.Kind == FxKind.Hit && f.By == me && f.Victim != me) _hitVictim = f.Victim;
        }

        /// <summary>False until the session has a self and an accepted snapshot.</summary>
        public bool TryBuild(out TutorialObservation o)
        {
            o = default(TutorialObservation);
            var s = _session; var me = s == null ? null : s.Me; var snap = s == null ? null : s.Latest;
            if (me == null || snap == null) return false;
            o.HasSelf = true; o.X = me.X; o.Y = me.Y; o.Weapon = me.Weapon;
            var inv = s.Collections.Inventory;
            if (inv.HasValue) { o.HasInventory = true; o.ActiveSlot = inv.Value.ActiveSlot; o.Slot0 = inv.Value.Slot0; o.Slot1 = inv.Value.Slot1; }
            var w = me.Wish;
            o.WishBusy = w.HasValue && (w.Value.Cast.HasValue || w.Value.Charges < w.Value.MaxCharges || w.Value.ReadyAt > s.ServerTimeMs);
            float best = float.MaxValue;
            var actors = snap.Actors;
            for (int i = 0; i < actors.Length; i++)
            {
                if (actors[i].Kind != (byte)ActorKind.TrainingDummy || actors[i].Weapon == (byte)WeaponId.Shield) continue;
                if (_hitVictim == actors[i].Id) o.HitDummy = true;
                float dx = actors[i].X - me.X, dy = actors[i].Y - me.Y, d = dx * dx + dy * dy;
                if (d < best) { best = d; o.HasDummy = true; o.DummyId = actors[i].Id; o.DummyX = actors[i].X; o.DummyY = actors[i].Y; }
            }
            _hitVictim = 0;
            best = float.MaxValue;
            var props = s.Collections.Props;
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p.Kind != PropAppearance.Ground || !p.Weapon.HasValue) continue;
                float dx = p.X - me.X, dy = p.Y - me.Y, d = dx * dx + dy * dy;
                if (d < best) { best = d; o.HasRack = true; o.RackId = p.Handle.Value; o.RackX = p.X; o.RackY = p.Y; }
            }
            return true;
        }
    }
}
