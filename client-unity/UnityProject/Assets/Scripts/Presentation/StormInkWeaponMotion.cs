using Roguehalla.Net;
using UnityEngine;

namespace Roguehalla.Client
{
    /// <summary>Local presentation tracks sampled against delivered action cues. Positions are in actor units.</summary>
    internal static class StormInkWeaponMotion
    {
        internal enum Phase { Guard, Ready, Strike, Follow, Block }
        internal enum Hand { Right, Left }

        internal readonly struct Pose
        {
            public readonly Vector2 Grip;
            public readonly float Angle, BodyLean;
            public readonly bool Behind, FlipFallback, HasSupport, SupportBehind;

            public Pose(Vector2 grip, float angle, bool behind, bool flipFallback, float bodyLean,
                bool hasSupport, bool supportBehind)
            {
                Grip = grip;
                Angle = angle;
                Behind = behind;
                FlipFallback = flipFallback;
                BodyLean = bodyLean;
                HasSupport = hasSupport;
                SupportBehind = supportBehind;
            }
        }

        readonly struct Key
        {
            public readonly float Reach, Height, Angle;
            public Key(float reach, float height, float angle) { Reach = reach; Height = height; Angle = angle; }
            public static Key Mix(Key a, Key b, float t) => new Key(Mathf.Lerp(a.Reach, b.Reach, t),
                Mathf.Lerp(a.Height, b.Height, t), Mathf.Lerp(a.Angle, b.Angle, t));
            public static Key operator +(Key a, Key b) => new Key(a.Reach + b.Reach, a.Height + b.Height, a.Angle + b.Angle);
        }

        readonly struct Track
        {
            public readonly Key Guard, Ready, Strike, Follow;
            public Track(Key guard, Key ready, Key strike, Key follow)
            { Guard = guard; Ready = ready; Strike = strike; Follow = follow; }
        }

        readonly struct Style
        {
            public readonly Track Offsets;
            public readonly float ChargeCurve, FollowThrough, ContactHold, Lean;
            public Style(Track offsets, float chargeCurve, float followThrough, float contactHold, float lean)
            {
                Offsets = offsets;
                ChargeCurve = chargeCurve;
                FollowThrough = followThrough;
                ContactHold = contactHold;
                Lean = lean;
            }
        }

        // Guard, load, contact, exit. Angles turn the weapon's upright art about its grip.
        static readonly Track[] Weapons =
        {
            new Track(new Key(.29f,.76f,-20), new Key(.16f,1.01f,65), new Key(.52f,.87f,-88), new Key(.38f,.60f,-152)), // Sword
            new Track(new Key(.35f,.74f,-11), new Key(.50f,.93f,-3), new Key(.57f,.94f,0), new Key(.44f,.82f,-19)), // Bow
            new Track(new Key(.27f,.86f,-27), new Key(.19f,1.09f,29), new Key(.54f,.95f,-81), new Key(.42f,.93f,-46)), // Wand
            new Track(new Key(.24f,.87f,5), new Key(.13f,.85f,20), new Key(.49f,1.03f,-19), new Key(.48f,.81f,-39)), // Shield
            new Track(new Key(.31f,.65f,-48), new Key(.06f,1.13f,90), new Key(.46f,1.02f,-66), new Key(.58f,.53f,-137)), // Axe
        };

        // Fire drops low before a fast exit. Haunted loads close and drives a short strike from a square stance.
        // Plague loops out from a tucked hand. Frost holds its line. Holy lifts into a broad, deliberate arc.
        static readonly Style[] Champions =
        {
            new Style(new Track(new Key(.04f,-.05f,-9), new Key(.04f,-.23f,23), new Key(.12f,-.09f,-7), new Key(.02f,-.17f,-18)), .65f,.26f,.055f,12f),
            new Style(new Track(new Key(-.04f,.03f,8), new Key(-.10f,-.04f,-15), new Key(-.10f,.04f,4), new Key(-.18f,.02f,28)), 1.15f,.36f,.090f,3f),
            new Style(new Track(new Key(-.09f,-.07f,21), new Key(-.13f,-.11f,43), new Key(.03f,-.14f,-9), new Key(.14f,.08f,-28)), 1.4f,.53f,.060f,8f),
            new Style(new Track(new Key(.02f,.07f,-4), new Key(.02f,.02f,-27), new Key(-.03f,.05f,0), new Key(-.08f,.11f,20)), 2.5f,.18f,.105f,1f),
            new Style(new Track(new Key(.06f,.08f,-12), new Key(-.02f,.13f,30), new Key(.07f,.12f,10), new Key(-.02f,.11f,-23)), 1f,.62f,.075f,5f),
        };

        internal static bool Supports(ActorKind kind, FactionId? faction, WeaponId? weapon) =>
            kind == ActorKind.Player && faction.HasValue && (int)faction.Value < Champions.Length &&
            weapon.HasValue && (int)weapon.Value < Weapons.Length;

        internal static float ContactHold(FactionId faction) => Champions[(int)faction].ContactHold;

        internal static Pose Sample(FactionId faction, WeaponId weapon, Phase phase, float progress, int poseRow, bool reduced)
        {
            var style = Champions[(int)faction];
            var track = Weapons[(int)weapon];
            var guard = track.Guard + style.Offsets.Guard;
            var ready = track.Ready + style.Offsets.Ready;
            var strike = track.Strike + style.Offsets.Strike;
            var follow = track.Follow + style.Offsets.Follow;
            float t = Mathf.Clamp01(progress);
            float lean = 0f;
            Key key = guard;
            switch (phase)
            {
                case Phase.Ready:
                    float load = reduced ? 1f : Mathf.Pow(t * t * (3f - 2f * t), style.ChargeCurve);
                    key = Key.Mix(guard, ready, load);
                    lean = -.45f * style.Lean * load;
                    break;
                case Phase.Strike:
                    key = strike;
                    lean = style.Lean;
                    break;
                case Phase.Follow when !reduced:
                    if (t < style.FollowThrough) key = Key.Mix(strike, follow, t / style.FollowThrough);
                    else key = Key.Mix(follow, guard, Mathf.SmoothStep(0f, 1f, (t - style.FollowThrough) / (1f - style.FollowThrough)));
                    lean = style.Lean * (1f - t);
                    break;
                case Phase.Block:
                    // A delivered block braces the hand over the torso. Attack data never triggers this pose.
                    key = new Key(.31f, 1f, 10f) + style.Offsets.Guard;
                    break;
            }

            float yaw = (2 - poseRow) * Mathf.PI / 4f;
            float across = Mathf.Cos(yaw), depth = -Mathf.Sin(yaw);
            if (Mathf.Abs(across) < .001f) across = 0f;
            if (Mathf.Abs(depth) < .001f) depth = 0f;

            // Right and left are relative to the actor, even when seen from behind. Project the
            // lateral shoulder offset and the forward reach independently into the camera plane.
            Hand hand = PrimaryHand(weapon);
            float side = hand == Hand.Right ? 1f : -1f;
            float shoulderWidth = weapon == WeaponId.Shield ? .23f : .17f;
            float gripDepth = depth * key.Reach - across * side * shoulderWidth;
            var grip = new Vector2(across * key.Reach + depth * side * shoulderWidth,
                key.Height - .17f * gripDepth);
            bool flip = across < 0f || (across == 0f && grip.x < 0f);
            float handedAngle = flip ? -1f : 1f;
            float aimTilt = Mathf.Atan2(depth * ArenaView.GroundSquash, Mathf.Abs(across)) * Mathf.Rad2Deg;
            float angle = handedAngle * (key.Angle + aimTilt * (weapon == WeaponId.Bow ? .46f : .32f));
            bool behind = gripDepth > .06f;

            // The right hand meets the bowstring; the left supports the axe haft. Two hand
            // slots never swap when the orientation moves through a diagonal or turns away.
            bool twoHanded = weapon == WeaponId.Bow && phase == Phase.Ready ||
                weapon == WeaponId.Axe && (phase == Phase.Ready || phase == Phase.Strike || phase == Phase.Follow && !reduced);
            bool supportBehind = depth * (key.Reach - .17f) + across * side * shoulderWidth > .06f;
            return new Pose(grip, angle, behind, flip, reduced ? 0f : -handedAngle * lean,
                twoHanded, supportBehind);
        }

        internal static Hand PrimaryHand(WeaponId weapon) =>
            weapon == WeaponId.Bow || weapon == WeaponId.Shield ? Hand.Left : Hand.Right;

        internal static Color SupportWrap(FactionId faction) => faction switch
        {
            FactionId.Sentenced => new Color32(33, 33, 39, 255),
            FactionId.Plague => new Color32(76, 85, 56, 255),
            FactionId.Frost => new Color32(72, 92, 109, 255),
            FactionId.Holy => new Color32(170, 152, 111, 255),
            _ => new Color32(151, 112, 90, 255),
        };

        static readonly string[] Keys = { "weapon0", "weapon1", "weapon2", "weapon3", "weapon4", "weapon5" };
        internal static string KeyFor(WeaponId weapon) => Keys[Mathf.Clamp((int)weapon, 0, Keys.Length - 1)];

        // Authored equipment has a uniform pixels-per-height convention. Use one scale in world space.
        internal const float Size = .88f;
    }
}
