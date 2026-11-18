using System;
using System.Collections.Generic;

namespace Roguehalla.Net.Protocol
{
    /// <summary>Reliable event kinds (wire tags). <see cref="CampCleared"/> (protocol 16, ADR0026) carries <see cref="WireEvent.Camp"/>.</summary>
    public enum EventKind : byte { Death = 1, Bounty = 2, MatchEnded = 3, Result = 4, PickupRefused = 5, ChestRefused = 6, PotionNotice = 7, WindEmblem = 8, CannonNotice = 9, Toast = 10, CampCleared = 11 }

    /// <summary>Reliable, ordered events. Only the fields of its Kind are meaningful.</summary>
    public sealed class WireEvent
    {
        public EventKind Kind;
        public uint Id;
        public uint? By;
        public ushort Bounty;
        public uint? Winner;
        public bool Draw;
        public WireResult Result;
        public byte PickupReason, ChestReason, PotionNotice, CannonCode;
        public ushort? CannonSeconds;
        public string ToastText;
        /// <summary><see cref="EventKind.CampCleared"/>: the cleared camp's index in the map's camp list (the legacy camp-clear ring cue).</summary>
        public ushort Camp;
    }

    public sealed class WireResult
    {
        public byte Outcome, Place, Players;
        public ushort Kills;
        public string KilledBy;
        public uint GvTotal, GvWin, DurationSec;
        public bool WinBonusWithheld;
    }

    /// <summary>
    /// Fx kinds (wire tags). Protocol 16 (ADR0026): <see cref="Windup"/> (<see cref="WireFx.By"/> will swing <see cref="WireFx.Weapon"/>
    /// toward <see cref="WireFx.A"/> in <see cref="WireFx.Ms"/> ms unless a Cancel or Stagger for it ends it first),
    /// <see cref="Cancel"/> (<see cref="WireFx.Id"/>'s own order cancelled its windup: drop the telegraph), <see cref="SigHit"/>
    /// (signature or ability damage: <see cref="WireFx.Victim"/>, <see cref="WireFx.By"/>, <see cref="WireFx.HitKind"/>,
    /// <see cref="WireFx.Amount"/>) and <see cref="Heal"/> (<see cref="WireFx.Id"/> healed <see cref="WireFx.Amount"/>).
    /// </summary>
    public enum FxKind : byte { Swing = 1, Hit = 2, Block = 3, Stagger = 4, Teleport = 5, WishStarted = 6, WishLanded = 7, WishEnded = 8, Windup = 9, Cancel = 10, SigHit = 11, Heal = 12 }

    public sealed class WireFx
    {
        /// <summary><see cref="HitKind"/> of a <see cref="FxKind.SigHit"/>: a burn chunk (signature damage over time).</summary>
        public const byte HitKindBurn = 1;
        /// <summary>A disease chunk.</summary>
        public const byte HitKindDisease = 2;
        /// <summary>A backlash hit.</summary>
        public const byte HitKindBacklash = 3;
        /// <summary>Mark damage.</summary>
        public const byte HitKindMark = 4;
        /// <summary>Card bleed: reserved (the sim emits no bleed chunks yet), accepted by decoders.</summary>
        public const byte HitKindBleed = 5;
        /// <summary>Ability damage: <c>HitKindAbility + ability code</c> (Freeze 0, Swarm 1, Trial 2, Blaze 3, Last Stand 4), so 16..20.</summary>
        public const byte HitKindAbility = 16;

        /// <summary>A <see cref="FxKind.SigHit"/> kind a decoder accepts: 1..5 or 16..20.</summary>
        public static bool HitKindValid(byte kind) { return (kind >= HitKindBurn && kind <= HitKindBleed) || (kind >= HitKindAbility && kind <= HitKindAbility + 4); }

        public FxKind Kind;
        /// <summary>The source (Swing, Hit, Block, Windup, SigHit); u32.MaxValue when absent or redacted on a Hit or SigHit.</summary>
        public uint By, Victim;
        public uint Id;
        public bool Passive;
        /// <summary>A <see cref="FxKind.Hit"/> was a critical strike (protocol 16: bit 1 of the hit's flags byte).</summary>
        public bool Critical;
        /// <summary>Swing, Block: the weapon code; Windup: the weapon code (0..15).</summary>
        public byte Weapon;
        /// <summary><see cref="FxKind.Windup"/>: milliseconds until the swing (whole ms, saturated at 65535).</summary>
        public ushort Ms;
        /// <summary><see cref="FxKind.SigHit"/>: <see cref="HitKindBurn"/> .. <see cref="HitKindBleed"/>, or <see cref="HitKindAbility"/> + ability code.</summary>
        public byte HitKind;
        public float X, Y, A, Amount;
        public double EventTime, Duration;
        public byte Ability, Reason;
        public uint? Caster, Target;
        public bool HasAnchor, HasDestination;
        public float AnchorX, AnchorY, DestinationX, DestinationY;
    }
}
