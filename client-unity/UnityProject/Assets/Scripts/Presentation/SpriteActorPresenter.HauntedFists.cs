using Roguehalla.Net;
using UnityEngine;
using Pose = Roguehalla.Client.StormInkActors.HauntedFistsPose;

namespace Roguehalla.Client
{
    public sealed partial class SpriteActorPresenter
    {
        const float HauntedKickHold = .066f;
        const float HauntedRetractEnd = .17f;

        static bool HasHauntedFists(Art art) =>
            StormInkActors.HasHauntedFists(art.Kind, art.Faction, art.WeaponId);

        // A remote swing can already be known while its drawn actor is still
        // approaching the source tick. This uses that lead for cosmetic preparation.
        internal void PrepareHauntedFists(uint owner, float aim, float leadSeconds)
        {
            if (leadSeconds < .03f || !_active.TryGetValue(owner, out var art) ||
                art.Dead || !art.Root.activeSelf || !HasHauntedFists(art)) return;
            float now = Time.unscaledTime;
            if (art.AttackUntil > now || art.WindupUntil > now) return;
            art.WindupAt = now;
            art.WindupUntil = now + Mathf.Min(.16f, leadSeconds);
            art.AttackFacing = aim;
            art.HasAttackFacing = true;
        }

        static Sprite HauntedFistsFrame(Art art, float facing, float now, bool reduced)
        {
            var aim = art.HasAttackFacing ? art.AttackFacing : facing;
            if (art.WishUntil > now || art.BlockUntil > now) return null;
            if (art.AttackUntil > now)
            {
                float age = now - art.AttackAt;
                if (age < HauntedKickHold) return StormInkActors.HauntedFistsFrame(aim, Pose.Kick);
                if (!reduced && age < HauntedRetractEnd && art.HitUntil <= now)
                    return StormInkActors.HauntedFistsFrame(aim, Pose.Retract);
            }
            else if (!reduced && art.WindupUntil > now && art.HitUntil <= now)
            {
                float hookSeconds = art.LocalCue ? .035f : (art.WindupUntil - art.WindupAt) * .44f;
                return StormInkActors.HauntedFistsFrame(aim, now - art.WindupAt < hookSeconds ? Pose.Hook : Pose.Chamber);
            }
            return null;
        }
    }
}
