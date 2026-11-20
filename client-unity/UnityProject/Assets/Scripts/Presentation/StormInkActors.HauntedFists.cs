using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Resolves each facing and pose in the shared Haunted unarmed sheet.</summary>
    public static partial class StormInkActors
    {
        internal enum HauntedFistsPose { Hook, Chamber, Kick, Retract }
        static Sheet _hauntedFists;

        internal static bool HasHauntedFists(ActorKind kind, FactionId? faction, WeaponId? weapon)
        {
            if (kind != ActorKind.Player || faction != FactionId.Sentenced || weapon != WeaponId.Fists)
                return false;
            _hauntedFists ??= Load("SentencedFists");
            return _hauntedFists.Poses[0] != null;
        }

        internal static Sprite HauntedFistsFrame(float aim, HauntedFistsPose pose) =>
            _hauntedFists.Poses[PoseRowForFacing(aim) * Columns + (int)pose];
    }
}
