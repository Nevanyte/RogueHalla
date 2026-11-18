using System;
using System.Collections.Generic;

namespace Roguehalla.Net.Protocol
{
    /// <summary>
    /// Legacy indicator facts (protocol 16, ADR0026; rh-protocol lib.rs <c>indicator_fault</c>): the rules every decoder and
    /// encoder applies to the flags, off-hand, build-mark and mark-plate bytes of actors and the self. Verdicts only, never clocks.
    /// </summary>
    public static class WireIndicators
    {
        /// <summary>Flag bit 0: the player's Wish is ready now (legacy <c>wishReadyAt</c> passed, outside the lobby).</summary>
        public const byte FlagWishReady = 1 << 0;
        /// <summary>Flag bit 1: a regional mob is fighting (its attack cooldown runs or its bow is drawn; legacy MobView <c>fighting</c>).</summary>
        public const byte FlagEngaged = 1 << 1;
        /// <summary>Flag bit 2: the teleport-immunity ring is up (at most 1.5 s of immunity left; legacy <c>immuneUntil</c>).</summary>
        public const byte FlagTeleportImmune = 1 << 2;
        /// <summary>Flag bit 3: Fire's charged roaming burst is armed (legacy <c>roamReady</c>).</summary>
        public const byte FlagRoamReady = 1 << 3;
        /// <summary>Every flag bit a decoder accepts; any other bit is refused.</summary>
        public const byte FlagsKnown = 0x0f;
        /// <summary>Largest off-hand code: 0 an empty second slot, else the weapon code + 1.</summary>
        public const byte MaxOffhandCode = Wire.MaxKitCode + 1;
        /// <summary>Largest build-mark stat key: 0 none, 1..9 = ad, ap, damage, health, attack speed, armor, mr, speed, leech.</summary>
        public const byte MaxStatKey = 9;
        /// <summary>Largest Sentenced mark tier.</summary>
        public const byte MaxMarkTier = 4;

        /// <summary>A packed build-mark byte is valid: two keys 0..9 (first in the low nibble), the second only after a first, never equal.</summary>
        public static bool BuildMarksValid(byte v)
        {
            int first = v & 0x0f, second = v >> 4;
            return first <= MaxStatKey && second <= MaxStatKey && (second == 0 || (first != 0 && first != second));
        }

        /// <summary>The first reason a decoder refuses these facts (as rh-protocol names it), or null.</summary>
        internal static string Fault(byte offhand, byte buildMarks, byte tier, byte brand, byte tax, byte flags)
        {
            if (offhand > MaxOffhandCode) return "offhand";
            if (!BuildMarksValid(buildMarks)) return "build marks";
            if (tier > MaxMarkTier || brand > 100 || tax > 100) return "mark";
            if ((flags & ~FlagsKnown & 0xff) != 0) return "flags";
            return null;
        }

        internal static void CheckDecoded(byte offhand, byte buildMarks, byte tier, byte brand, byte tax, byte flags)
        {
            string why = Fault(offhand, buildMarks, tier, brand, tax, flags);
            if (why != null) throw new DecodeException(DecodeError.BadField, why);
        }

        internal static void CheckEncoded(byte offhand, byte buildMarks, byte tier, byte brand, byte tax, byte flags)
        {
            string why = Fault(offhand, buildMarks, tier, brand, tax, flags);
            if (why != null) throw new ArgumentException(why);
        }
    }

    /// <summary>The receiving player's own state. A class on purpose: <c>Snapshot.Me</c> is null for a spectator.</summary>
    public sealed class WireSelf
    {
        public uint Id;
        public byte Faction;
        public float X, Y, Facing, Hp, MaxHp;
        public byte Status, Weapon;
        public ushort ReadyInMs, Kills, Bounty, Gv;
        public byte MoveSeq, DisplaceSeq, Sig;
        public ushort Effects;
        public uint? HuntTarget;
        public ushort HuntStacks;
        /// <summary>Optional authoritative Wish owner record. Null means no owner publication is present.</summary>
        public WireWishOwner Wish;
        /// <summary>Authoritative owner barrier amount.</summary>
        public float Barrier;
        /// <summary>Authoritative owner barrier ring step (0..8).</summary>
        public byte BarrierRingStep;
        /// <summary>The own hitbox archetype id (protocol 16, ADR0024); its row is in <see cref="ServerMsg.Welcome.Archetypes"/>.</summary>
        public byte Archetype;
        /// <summary>The own indicator flags (protocol 16, ADR0026): <see cref="WireIndicators.FlagWishReady"/>,
        /// <see cref="WireIndicators.FlagTeleportImmune"/>, <see cref="WireIndicators.FlagRoamReady"/> (a self is never engaged).</summary>
        public byte Flags;
        /// <summary>The own overhead build marks, packed as <see cref="WireActor.BuildMarks"/>.</summary>
        public byte BuildMarks;
        /// <summary>The own Sentenced mark plate: tier 0..4, brand 0..100, mark tax percent 0..100 (all zero when unmarked).</summary>
        public byte MarkTier, Brand, MarkTax;

        internal void CopyFrom(WireSelf o)
        {
            Id = o.Id; Faction = o.Faction; X = o.X; Y = o.Y; Facing = o.Facing; Hp = o.Hp; MaxHp = o.MaxHp; Status = o.Status; Weapon = o.Weapon;
            ReadyInMs = o.ReadyInMs; Kills = o.Kills; Bounty = o.Bounty; Gv = o.Gv; MoveSeq = o.MoveSeq; Effects = o.Effects; DisplaceSeq = o.DisplaceSeq; Sig = o.Sig;
            HuntTarget = o.HuntTarget; HuntStacks = o.HuntStacks;
            if (o.Wish == null) Wish = null;
            else { if (Wish == null) Wish = new WireWishOwner(); Wish.CopyFrom(o.Wish); }
            Barrier = o.Barrier; BarrierRingStep = o.BarrierRingStep; Archetype = o.Archetype;
            Flags = o.Flags; BuildMarks = o.BuildMarks; MarkTier = o.MarkTier; Brand = o.Brand; MarkTax = o.MarkTax;
        }

        // Head bits (rh-protocol lib.rs, append only): a varint, so bits below 7 take one byte.
        internal const uint HPresent = 1 << 0, HKit = 1 << 1, HHp = 1 << 2, HReady = 1 << 3, HKills = 1 << 4, HBounty = 1 << 5,
            HMoveSeq = 1 << 6, HGv = 1 << 7, HMaxHpF32 = 1 << 8, HStatus = 1 << 9, HPhase = 1 << 10, HEffects = 1 << 11, HDisplace = 1 << 12, HSig = 1 << 13,
            HHuntTarget = 1 << 14, HHuntStacks = 1 << 15, HTraining = 1 << 16, HWish = 1 << 17, HBarrier = 1 << 18, HServerTime = 1 << 19, HOwnerCombat = 1 << 20, HArchetype = 1 << 21,
            HFlags = 1 << 22, HBuildMarks = 1 << 23, HMark = 1 << 24;
        internal const uint SelfBits = 0x03ff | HEffects | HDisplace | HSig | HHuntTarget | HHuntStacks | HFlags | HBuildMarks | HMark,
            Known = SelfBits | HPhase | HTraining | HWish | HBarrier | HServerTime | HOwnerCombat | HArchetype;

        static bool MaxHpIsVarint(float v)
        {
            if (!(v >= 0f && v <= 65535f)) return false;
            return BitConverter.SingleToInt32Bits(v) == BitConverter.SingleToInt32Bits((float)(ushort)v);
        }

        internal static byte Kit(byte faction, byte weapon) { return (byte)((faction & 0x0f) | (weapon << 4)); }

        /// <summary>The head of a Snapshot: the present bit and every field that differs from its default (this is the self), and the phase bit when the phase is not the default.</summary>
        internal uint Head(byte phase)
        {
            uint m = HPresent;
            if (phase != Wire.DefaultPhase) m |= HPhase;
            if (Kit(Faction, Weapon) != 0) m |= HKit;
            if (BitConverter.SingleToInt32Bits(Hp) != BitConverter.SingleToInt32Bits(MaxHp)) m |= HHp;
            if (ReadyInMs != 0) m |= HReady;
            if (Kills != 0) m |= HKills;
            if (Bounty != 0) m |= HBounty;
            if (Gv != 0) m |= HGv;
            if (MoveSeq != 0) m |= HMoveSeq;
            if (!MaxHpIsVarint(MaxHp)) m |= HMaxHpF32;
            if (Status != Wire.DefaultStatus) m |= HStatus;
            if (Effects != 0) m |= HEffects;
            if (DisplaceSeq != 0) m |= HDisplace;
            if (Sig != 0) m |= HSig;
            if (HuntTarget.HasValue) m |= HHuntTarget;
            if (HuntStacks != 0) m |= HHuntStacks;
            if (Wish != null) m |= HWish;
            if (BitConverter.SingleToInt32Bits(Barrier) != BitConverter.SingleToInt32Bits(0f) || BarrierRingStep != 0) m |= HBarrier;
            if (Archetype != 0) m |= HArchetype;
            if (Flags != 0) m |= HFlags;
            if (BuildMarks != 0) m |= HBuildMarks;
            if (MarkTier != 0 || Brand != 0 || MarkTax != 0) m |= HMark;
            return m;
        }

        /// <summary>Bytes of the head itself, with the phase byte that follows the self when the head has the phase bit.</summary>
        internal static int HeadLength(uint head) { return Writer.VarintLength(head) + ((head & HPhase) != 0 ? 1 : 0); }

        /// <summary>Bytes of the self record after the head.</summary>
        internal int Length(uint head)
        {
            int n = Writer.VarintLength(Id) + 12;
            n += (head & HMaxHpF32) != 0 ? 4 : Writer.VarintLength((uint)MaxHp);
            if ((head & HKit) != 0) n += 1;
            if ((head & HHp) != 0) n += 4;
            if ((head & HReady) != 0) n += 2;
            if ((head & HKills) != 0) n += Writer.VarintLength(Kills);
            if ((head & HBounty) != 0) n += Writer.VarintLength(Bounty);
            if ((head & HMoveSeq) != 0) n += 1;
            if ((head & HGv) != 0) n += Writer.VarintLength(Gv);
            if ((head & HStatus) != 0) n += 1;
            if ((head & HEffects) != 0) n += 2;
            if ((head & HDisplace) != 0) n += 1;
            if ((head & HSig) != 0) n += 1;
            if (HuntTarget.HasValue) n += Writer.VarintLength(HuntTarget.Value);
            if ((head & HHuntStacks) != 0) n += Writer.VarintLength(HuntStacks);
            if ((head & HWish) != 0) n += WireWishOwnerLength(Wish);
            if ((head & HBarrier) != 0) n += 5;
            if ((head & HArchetype) != 0) n += 1;
            if ((head & HFlags) != 0) n += 1;
            if ((head & HBuildMarks) != 0) n += 1;
            if ((head & HMark) != 0) n += 3;
            return n;
        }

        static int WireWishOwnerLength(WireWishOwner v)
        {
            WishWire.ValidateOwner(v);
            return 1 + 8 + (v.FullAt.HasValue ? 9 : 1) + 1 + 8 + 1 + (v.NextChargeAt.HasValue ? 9 : 1) + 1 + (v.Cast == null ? 0 : 17);
        }

        /// <summary>Reads the self after its head (<paramref name="head"/> has HPresent set); the phase, if the head has one, is the caller's.</summary>
        internal void Read(ref Reader r, uint head)
        {
            Id = r.Varint(); X = r.F32(); Y = r.F32(); Facing = r.F32();
            MaxHp = (head & HMaxHpF32) != 0 ? r.F32() : r.VarintU16("max_hp");
            if ((head & HKit) != 0) { byte k = r.U8(); Faction = (byte)(k & 0x0f); Weapon = (byte)(k >> 4); } else { Faction = 0; Weapon = 0; }
            Hp = (head & HHp) != 0 ? r.F32() : MaxHp;
            ReadyInMs = (head & HReady) != 0 ? r.U16() : (ushort)0;
            Kills = (head & HKills) != 0 ? r.VarintU16("kills") : (ushort)0;
            Bounty = (head & HBounty) != 0 ? r.VarintU16("bounty") : (ushort)0;
            MoveSeq = (head & HMoveSeq) != 0 ? r.U8() : (byte)0;
            Gv = (head & HGv) != 0 ? r.VarintU16("gv") : (ushort)0;
            Status = (head & HStatus) != 0 ? r.Code(Wire.MaxStatusCode, "status") : Wire.DefaultStatus;
            Effects = (head & HEffects) != 0 ? r.U16() : (ushort)0;
            DisplaceSeq = (head & HDisplace) != 0 ? r.U8() : (byte)0;
            Sig = (head & HSig) != 0 ? r.U8() : (byte)0;
            if ((head & HHuntTarget) != 0) { uint t = r.Varint(); if (t == 0) throw new DecodeException(DecodeError.BadField, "hunt target"); HuntTarget = t; } else HuntTarget = null;
            HuntStacks = (head & HHuntStacks) != 0 ? r.VarintU16("hunt_stacks") : (ushort)0;
            Wish = (head & HWish) != 0 ? WishWire.ReadOwner(ref r, Wish) : null;
            if ((head & HBarrier) != 0) { Barrier = r.F32(); BarrierRingStep = r.U8(); }
            else { Barrier = 0f; BarrierRingStep = 0; }
            if (Barrier < 0 || float.IsNaN(Barrier) || float.IsInfinity(Barrier) || BarrierRingStep > 8)
                throw new DecodeException(DecodeError.BadField, "barrier");
            Archetype = (head & HArchetype) != 0 ? r.U8() : (byte)0;
            Flags = (head & HFlags) != 0 ? r.U8() : (byte)0;
            BuildMarks = (head & HBuildMarks) != 0 ? r.U8() : (byte)0;
            if ((head & HMark) != 0) { MarkTier = r.U8(); Brand = r.U8(); MarkTax = r.U8(); }
            else { MarkTier = 0; Brand = 0; MarkTax = 0; }
            WireIndicators.CheckDecoded(0, BuildMarks, MarkTier, Brand, MarkTax, Flags);
        }

        internal void Write(Writer w, uint head)
        {
            w.Varint(Id); w.F32(X); w.F32(Y); w.F32(Facing);
            if ((head & HMaxHpF32) != 0) w.F32(MaxHp); else w.Varint((uint)MaxHp);
            if ((head & HKit) != 0) w.U8(Kit(Faction, Weapon));
            if ((head & HHp) != 0) w.F32(Hp);
            if ((head & HReady) != 0) w.U16(ReadyInMs);
            if ((head & HKills) != 0) w.Varint(Kills);
            if ((head & HBounty) != 0) w.Varint(Bounty);
            if ((head & HMoveSeq) != 0) w.U8(MoveSeq);
            if ((head & HGv) != 0) w.Varint(Gv);
            if ((head & HStatus) != 0) w.U8(Status);
            if ((head & HEffects) != 0) w.U16(Effects);
            if ((head & HDisplace) != 0) w.U8(DisplaceSeq);
            if ((head & HSig) != 0) w.U8(Sig);
            if (HuntTarget.HasValue) w.Varint(HuntTarget.Value);
            if ((head & HHuntStacks) != 0) w.Varint(HuntStacks);
            if ((head & HWish) != 0) WishWire.WriteOwner(w, Wish);
            if ((head & HBarrier) != 0) { WishWire.ValidateBarrier(Barrier, BarrierRingStep); w.F32(Barrier); w.U8(BarrierRingStep); }
            if ((head & HArchetype) != 0) w.U8(Archetype);
            if ((head & HFlags) != 0) w.U8(Flags);
            if ((head & HBuildMarks) != 0) w.U8(BuildMarks);
            if ((head & HMark) != 0) { w.U8(MarkTier); w.U8(Brand); w.U8(MarkTax); }
        }
    }
}
