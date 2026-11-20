using System.Collections.Generic;
using Roguehalla.Net;
using Roguehalla.Net.Protocol;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Tracks visible combat cues, clip durations and painted weapon sockets for observed actors.</summary>
    public sealed partial class SpriteActorPresenter
    {
        static bool HasWeaponTrack(Art a) => StormInkWeaponMotion.Supports(a.Kind, a.Faction, a.WeaponId) ||
            StormInkActors.HasMobWeaponTrack(a.Kind, a.Faction, a.MobType, a.WeaponId);

        static void BindArtistSources(Art art, WeaponId? weapon, float facing, StormInkFireAxeAuthoring fire,
            StormInkFireSwordAuthoring sword, StormInkFireBowAuthoring bow = null)
        {
            art.BodyArtistSource = StormInkArtistSource.BindBody(art.BodyArtistSource, art.Body, fire, sword, bow);
            if (art.Weapon.enabled && weapon.HasValue)
                art.WeaponArtistSource = StormInkArtistSource.BindWeapon(art.WeaponArtistSource, art.Weapon, weapon,
                    StormInkActors.PoseRowForFacing(facing), false);
            else
                art.WeaponArtistSource?.Clear();
        }

        static bool TryWeaponHand(Art a, bool leftHand, out Vector2 uv) => a.Kind == ActorKind.RegionalMob
            ? StormInkActors.TryMobHand(a.MobType, a.Body.sprite, leftHand, out uv) ||
                StormInkActors.TryCreatureFocus(a.MobType, a.Body.sprite, out uv)
            : StormInkActors.TryHand(a.Faction, a.Body.sprite, leftHand, out uv);

        static StormInkWeaponMotion.Pose RegionalWeaponPose(Art a, StormInkWeaponMotion.Pose pose,
            StormInkWeaponMotion.Phase phase, float progress, int row, bool reduced)
        {
            if (a.Kind != ActorKind.RegionalMob) return pose;
            return a.MobType switch
            {
                MobTypeId.AshDrake => StormInkActors.DrakeCast(pose, phase, progress, row, reduced),
                MobTypeId.Executioner => StormInkActors.ExecutionerCut(pose, phase, progress, row, reduced),
                _ => pose
            };
        }

        static float EquipmentScale(Art a)
        {
            // A bite-mounted focus must clear the drake's ground marker and overhead health bar.
            float scale = StormInkWeaponMotion.Size / ActorVisualScale;
            return a.Kind == ActorKind.RegionalMob && a.MobType == MobTypeId.AshDrake ? scale * .53f : scale;
        }

        static void ClearWeaponMotion(Art a)
        {
            a.AttackAt = a.AttackUntil = a.WindupAt = a.WindupUntil = 0f;
            a.HasAttackFacing = a.LocalCue = false;
            a.DrawStartedAt = a.MotionSuccessor = 0;
            a.Bowstring?.Clear(a.Weapon, a.OutBody);
        }

        /// <summary>Local click feedback settles the champion into a guard until a delivered cue commits the motion.</summary>
        public void LocalAttackCue(uint id)
        {
            if (!_active.TryGetValue(id, out var art) || art.Dead || !art.WeaponId.HasValue) return;
            float now = Time.unscaledTime;
            if (HasWeaponTrack(art) || HasHauntedFists(art))
            {
                if (art.AttackUntil > now || art.WindupUntil > now) return;
                art.WindupAt = now;
                art.WindupUntil = now + .16f;
                art.HasAttackFacing = false;
                art.LocalCue = true;
                return;
            }
            if (art.AttackUntil > now && !art.LocalCue) return;
            art.AttackAt = now; art.LocalCue = true;
            art.AttackUntil = now + ClipDuration(AttackFrames(art.Appearance, art.WeaponId.Value, out float fps), fps, .32f);
        }

        const float LocalCueWindow = .6f;

        public void ConfirmedCue(uint id, FxKind kind, WeaponId? cueWeapon = null, float? aim = null,
            float windupMs = 0f)
        {
            if (!_active.TryGetValue(id, out var art) || art.Dead) return;
            float now = Time.unscaledTime;
            if (kind == FxKind.Cancel || kind == FxKind.Teleport)
            {
                ClearWeaponMotion(art);
                return;
            }
            if (kind == FxKind.Windup)
            {
                bool unarmed = HasHauntedFists(art);
                if ((!HasWeaponTrack(art) && !unarmed) || !art.Root.activeSelf || cueWeapon != art.WeaponId) return;
                // Player melee can deliver Windup and Swing for the same source
                // tick. A zero-duration windup must not reset existing preparation.
                if (unarmed && !(windupMs > 0f)) return;
                ClearWeaponMotion(art);
                art.WindupAt = now;
                art.WindupUntil = now + windupMs * .001f;
                art.AttackFacing = aim.GetValueOrDefault();
                art.HasAttackFacing = aim.HasValue;
                return;
            }
            if (kind == FxKind.Swing && art.WeaponId.HasValue)
            {
                bool tracked = HasWeaponTrack(art) || HasHauntedFists(art);
                if (tracked && (!art.Root.activeSelf || (cueWeapon.HasValue && cueWeapon != art.WeaponId))) return;
                if (!tracked && art.LocalCue && now - art.AttackAt < LocalCueWindow) { art.LocalCue = false; return; }
                art.LocalCue = false;
                if (art.Kind == ActorKind.Player && art.Faction == FactionId.Fire && art.WeaponId == WeaponId.Axe)
                    art.FireAxeReleaseCount++;
                if (art.Kind == ActorKind.Player && art.Faction == FactionId.Fire && art.WeaponId == WeaponId.Sword)
                    art.FireSwordReleaseCount++;
                art.WindupUntil = 0f;
                art.AttackAt = now;
                art.AttackFacing = aim.GetValueOrDefault();
                art.HasAttackFacing = aim.HasValue;
                var clip = AttackFrames(art.Appearance, art.WeaponId.Value, out float fps);
                art.AttackUntil = now + ClipDuration(clip, fps, .32f);
            }
            else if (kind == FxKind.Hit || kind == FxKind.Stagger)
            {
                if (kind == FxKind.Stagger) ClearWeaponMotion(art);
                art.HitAt = now;
                art.HitUntil = now + ClipDuration(art.Appearance?.Hit, art.Appearance != null ? art.Appearance.HitFPS : 10, .2f);
            }
            else if (kind == FxKind.Block)
            {
                art.BlockAt = now;
                art.BlockUntil = now + ClipDuration(art.Appearance?.Block, art.Appearance != null ? art.Appearance.BlockFPS : 10, .2f);
            }
        }

        // Collections arrive before the actor pass. A renewed or swapped owner needs its current
        // position, equipment and appearance installed before the launch can select its release.
        readonly List<NetProjectileView> _frameLaunches = new List<NetProjectileView>(8);

        internal void QueueProjectile(uint owner, ProjectileKind kind, float x, float y, float aim) =>
            _frameLaunches.Add(new NetProjectileView { Owner = owner, Kind = kind, X = x, Y = y, Facing = aim });

        void ApplyFrameLaunches(uint owner)
        {
            for (int i = 0; i < _frameLaunches.Count; i++)
            {
                var shot = _frameLaunches[i];
                if (shot.Owner != owner) continue;
                ObservedProjectile(owner, shot.Kind, shot.X, shot.Y, shot.Facing);
                _frameLaunches.RemoveAt(i);
                i--;
            }
        }

        internal void ObservedProjectile(uint owner, ProjectileKind kind, float x, float y, float aim)
        {
            if (!_active.TryGetValue(owner, out var art) || !art.VisibleForCombatCue || !HasWeaponTrack(art) ||
                (kind != ProjectileKind.Arrow && kind != ProjectileKind.Ball)) return;
            // The projected collection has no launch id. A newly seen shot may be an old shot arriving from fog.
            // Require it to be near the delivered owner, and require the delivered weapon to agree.
            var origin = art.Root.transform.position;
            float dx = x - origin.x / ArenaView.Scale;
            float dy = y + origin.y / (ArenaView.Scale * ArenaView.GroundSquash);
            if (dx * dx + dy * dy > 80f * 80f) return;
            var weapon = kind == ProjectileKind.Arrow ? WeaponId.Bow : WeaponId.Wand;
            if (kind == ProjectileKind.Arrow && art.Root.activeSelf && art.Fade.Opacity > 0f &&
                art.Kind == ActorKind.Player && art.Faction == FactionId.Fire && art.WeaponId == WeaponId.Bow)
                art.FireBowShotCount++;
            ConfirmedCue(owner, FxKind.Swing, weapon, aim);
        }

        /// <summary>
        /// Plays the character clip that fits a delivered Wish cue on an observed actor: Cast for Freeze, Swarm and Trial, Attack
        /// for Blaze Step, Block for Last Stand (each falling back to another clip when the character lacks it).
        /// Presentation only; the cue was confirmed by the server's Wish fx record.
        /// </summary>
        public bool WishCue(uint id, AbilityId ability)
        {
            if (!_active.TryGetValue(id, out var art) || art.Dead) return false;
            byte clip = ability == AbilityId.Blaze ? (byte)1 : ability == AbilityId.LastStand ? (byte)2 : (byte)0;
            Sprite[] frames; float fps; WishFrames(art.Appearance, clip, out frames, out fps);
            float now = Time.unscaledTime;
            art.WishClip = clip; art.WishAt = now; art.WishUntil = now + ClipDuration(frames, fps, .4f);
            return true;
        }
        static void WishFrames(ShowcaseCatalog.Appearance a, byte clip, out Sprite[] frames, out float fps)
        {
            frames = null; fps = 10;
            if (a == null) return;
            if (clip == 2 && a.Block != null && a.Block.Length > 0) { frames = a.Block; fps = a.BlockFPS; return; }
            if (clip == 1 && a.Attack != null && a.Attack.Length > 0) { frames = a.Attack; fps = a.AttackFPS; return; }
            if (a.Cast != null && a.Cast.Length > 0) { frames = a.Cast; fps = a.CastFPS; return; }
            if (a.Attack != null && a.Attack.Length > 0) { frames = a.Attack; fps = a.AttackFPS; return; }
            if (a.Block != null && a.Block.Length > 0) { frames = a.Block; fps = a.BlockFPS; }
        }
        /// <summary>Tints an observed actor's body toward <paramref name="tint"/> for the status it shows. Cleared by <see cref="ClearWishTints"/>.</summary>
        public void SetWishTint(uint id, Color tint, float amount)
        {
            if (_active.TryGetValue(id, out var art)) { art.WishTint = tint; art.WishTintAmount = Mathf.Clamp01(amount); }
        }
        public void ClearWishTints() { foreach (var pair in _active) pair.Value.WishTintAmount = 0f; }
        static float ClipDuration(Sprite[] clip,float fps,float fallback)
        { return clip!=null && clip.Length>0 ? clip.Length/8f/Mathf.Max(1,fps) : fallback; }
        static bool WishClipFrames(ShowcaseCatalog.Appearance a, byte clip, out Sprite[] frames, out float fps)
        { WishFrames(a, clip, out frames, out fps); return frames != null && frames.Length > 0; }
        static Sprite[] AttackFrames(ShowcaseCatalog.Appearance a,WeaponId weapon,out float fps)
        {
            fps=a!=null ? a.AttackFPS : 10;
            if(a==null) return null;
            if(weapon==WeaponId.Bow && a.Shoot!=null && a.Shoot.Length>0) {fps=a.ShootFPS;return a.Shoot;}
            if(weapon==WeaponId.Wand && a.Cast!=null && a.Cast.Length>0) {fps=a.CastFPS;return a.Cast;}
            return a.Attack;
        }

        static Vector3 BodySocket(SpriteRenderer body, Vector2 socket)
        {
            var transform = body.transform; // Body and weapon share the grounded actor parent.
            return transform.localPosition + transform.localRotation * SpriteSocket(body.sprite, socket, transform.localScale);
        }

        static Vector3 SpriteSocket(Sprite sprite, Vector2 socket, Vector3 signedScale)
        {
            Vector2 pixel = Vector2.Scale(socket, sprite.rect.size) - sprite.pivot;
            return Vector3.Scale((Vector3)(pixel / sprite.pixelsPerUnit), signedScale);
        }
    }
}
