using System;
using System.Collections.Generic;

namespace Roguehalla.Net.Protocol
{
    /// <summary>
    /// One visible actor. A value type: a snapshot's actors live inline in its array (no object per actor). The fields are
    /// declared widest first so the struct packs into 28 bytes (including v6 effects/displacement; v7 hunt fields); the wire order is fixed by <see cref="Read"/>/<see cref="Write"/>.
    /// X, Y and Facing are what the wire carries (quantized to 1/8 u and 2 pi / 65536).
    /// </summary>
    public struct WireActor
    {
        public uint Id;
        public float X, Y, Facing;
        public ushort Bounty;
        public byte Faction;
        /// <summary>hp fraction in hundredths (0..100).</summary>
        public byte HpPct;
        public byte Status, Weapon;
        /// <summary>What the entity is: 0 = player (the default; costs no byte). The id namespace is shared by every kind.</summary>
        public byte Kind, DisplaceSeq, MobTypeCode, Sig;
        public ushort Effects;
        /// <summary>Authoritative per-observer barrier share.</summary>
        public byte BarrierPct, BarrierRingStep;
        /// <summary>Hitbox archetype id (protocol 16, ADR0024); 0 is the legacy circle. Its row is in the Welcome's table.</summary>
        public byte Archetype;
        /// <summary>Indicator flags (protocol 16, ADR0026): <see cref="WireIndicators"/> <c>Flag*</c> bits (bit 0 Wish ready, 1 engaged,
        /// 2 teleport immune, 3 roam ready).</summary>
        public byte Flags;
        /// <summary>The weapon in a player's second slot, the one not in hand: 0 empty (legacy draws fists), else weapon code + 1 (at most 16).</summary>
        public byte Offhand;
        /// <summary>A player's top two invested stats (legacy build marks): first key in the low nibble, second in the high one, each
        /// 0 none or 1..9 (ad, ap, damage, health, attack speed, armor, mr, speed, leech). Keys only, never scores.</summary>
        public byte BuildMarks;
        /// <summary>Sentenced mark plate: tier 0..4, brand 0..100, mark tax percent 0..100 (all zero when unmarked).</summary>
        public byte MarkTier, Brand, MarkTax;
        /// <summary>A remote player's walk hint (protocol 19, P-NET-DRAWAHEAD-1a): the heading the server will keep walking, in
        /// 256ths of a turn (0 whenever <see cref="WalkTicks"/> is 0), and the whole ticks it is committed to (a lower bound).
        /// Never set on a mob. A plain count: any pair decodes (an encoder refuses a heading without ticks).</summary>
        public byte WalkDir, WalkTicks;

        // Full actor mask bits (append only): 0 kit, 1 hp, 2 status, 3 bounty, 4 kind ... 10 archetype, 11 flags, 12 off-hand,
        // 13 build marks, 14 mark plate, 15 walk hint (protocol 19: walk dir, walk ticks; set only while the ticks are not 0).
        // A varint (three bytes once bit 14 is set).
        internal const uint MKit = 1 << 0, MHp = 1 << 1, MStatus = 1 << 2, MBounty = 1 << 3, MKind = 1 << 4, MEffects = 1 << 5, MDisplace = 1 << 6, MMobType = 1 << 7, MSig = 1 << 8, MBarrier = 1 << 9, MArchetype = 1 << 10,
            MFlags = 1 << 11, MOffhand = 1 << 12, MBuildMarks = 1 << 13, MMark = 1 << 14, MWalk = 1 << 15,
            MKnown = MKit | MHp | MStatus | MBounty | MKind | MEffects | MDisplace | MMobType | MSig | MBarrier | MArchetype | MFlags | MOffhand | MBuildMarks | MMark | MWalk;
        internal const byte DefaultHpPct = 100;

        /// <summary>Only native quest givers use the reserved absent-identity kit and carry no regional type.</summary>
        internal bool HasValidGiverIdentity
        {
            get { return Kind != 4 || (Faction == 15 && Weapon == 15 && MobTypeCode == 0); }
        }

        internal uint Mask()
        {
            uint m = 0;
            if (WireSelf.Kit(Faction, Weapon) != 0) m |= MKit;
            if (HpPct != DefaultHpPct) m |= MHp;
            if (Status != Wire.DefaultStatus) m |= MStatus;
            if (Bounty != 0) m |= MBounty;
            if (Kind != 0) m |= MKind;
            if (Effects != 0) m |= MEffects;
            if (DisplaceSeq != 0) m |= MDisplace;
            if (MobTypeCode > 30) throw new ArgumentException("mob type");
            if (MobTypeCode != 0) m |= MMobType;
            if (Sig != 0) m |= MSig;
            if (BarrierPct != 0 || BarrierRingStep != 0) m |= MBarrier;
            if (Archetype != 0) m |= MArchetype;
            if (Flags != 0) m |= MFlags;
            if (Offhand != 0) m |= MOffhand;
            if (BuildMarks != 0) m |= MBuildMarks;
            if (MarkTier != 0 || Brand != 0 || MarkTax != 0) m |= MMark;
            if (WalkTicks != 0) m |= MWalk;
            return m;
        }

        internal static uint IdField(uint id, bool first, uint prev) { return first ? id : unchecked(id - prev); }

        internal int Length(bool first, uint prev)
        {
            uint m = Mask();
            return Writer.VarintLength(IdField(Id, first, prev)) + Writer.VarintLength(m) + 6 + ((m & MKit) != 0 ? 1 : 0) + ((m & MHp) != 0 ? 1 : 0)
                + ((m & MStatus) != 0 ? 1 : 0) + ((m & MBounty) != 0 ? Writer.VarintLength(Bounty) : 0) + ((m & MKind) != 0 ? 1 : 0) + ((m & MEffects) != 0 ? 2 : 0) + ((m & MDisplace) != 0 ? 1 : 0) + ((m & MMobType) != 0 ? 1 : 0) + ((m & MSig) != 0 ? 1 : 0) + ((m & MBarrier) != 0 ? 2 : 0) + ((m & MArchetype) != 0 ? 1 : 0)
                + ((m & MFlags) != 0 ? 1 : 0) + ((m & MOffhand) != 0 ? 1 : 0) + ((m & MBuildMarks) != 0 ? 1 : 0) + ((m & MMark) != 0 ? 3 : 0) + ((m & MWalk) != 0 ? 2 : 0);
        }

        /// <summary>Reads one actor; the first has an absolute id, each later one the (non-zero) gap from <paramref name="prev"/>.</summary>
        internal static WireActor Read(ref Reader r, bool first, uint prev)
        {
            WireActor a = default(WireActor);
            uint field = r.Varint();
            if (first) a.Id = field;
            else
            {
                ulong id = (ulong)prev + field;
                if (field == 0 || id > uint.MaxValue) throw new DecodeException(DecodeError.BadField, "actor order");
                a.Id = (uint)id;
            }
            uint mask = r.Varint();
            if ((mask & ~MKnown) != 0) throw new DecodeException(DecodeError.BadField, "actor mask");
            a.X = Quant.DequantizePos(r.U16()); a.Y = Quant.DequantizePos(r.U16()); a.Facing = Quant.DequantizeFacing(r.U16());
            if ((mask & MKit) != 0) { byte k = r.U8(); a.Faction = (byte)(k & 0x0f); a.Weapon = (byte)(k >> 4); } else { a.Faction = 0; a.Weapon = 0; }
            a.HpPct = (mask & MHp) != 0 ? r.U8() : DefaultHpPct;
            if (a.HpPct > 100) throw new DecodeException(DecodeError.BadField, "hp_pct");
            a.Status = (mask & MStatus) != 0 ? r.Code(Wire.MaxStatusCode, "status") : Wire.DefaultStatus;
            a.Bounty = (mask & MBounty) != 0 ? r.VarintU16("bounty") : (ushort)0;
            a.Kind = (mask & MKind) != 0 ? r.U8() : (byte)0;
            a.Effects = (mask & MEffects) != 0 ? r.U16() : (ushort)0;
            a.DisplaceSeq = (mask & MDisplace) != 0 ? r.U8() : (byte)0;
            a.MobTypeCode = (mask & MMobType) != 0 ? r.Code(30,"mob type") : (byte)0;
            a.Sig = (mask & MSig) != 0 ? r.U8() : (byte)0;
            if ((mask & MBarrier) != 0) { a.BarrierPct = r.U8(); a.BarrierRingStep = r.U8(); }
            if (a.BarrierPct > 100 || a.BarrierRingStep > 8) throw new DecodeException(DecodeError.BadField, "barrier");
            a.Archetype = (mask & MArchetype) != 0 ? r.U8() : (byte)0;
            a.Flags = (mask & MFlags) != 0 ? r.U8() : (byte)0;
            a.Offhand = (mask & MOffhand) != 0 ? r.U8() : (byte)0;
            a.BuildMarks = (mask & MBuildMarks) != 0 ? r.U8() : (byte)0;
            if ((mask & MMark) != 0) { a.MarkTier = r.U8(); a.Brand = r.U8(); a.MarkTax = r.U8(); }
            if ((mask & MWalk) != 0) { a.WalkDir = r.U8(); a.WalkTicks = r.U8(); }
            WireIndicators.CheckDecoded(a.Offhand, a.BuildMarks, a.MarkTier, a.Brand, a.MarkTax, a.Flags);
            if (!a.HasValidGiverIdentity) throw new DecodeException(DecodeError.BadField, "giver actor");
            return a;
        }

        internal void Write(Writer w, bool first, uint prev)
        {
            uint m = Mask();
            w.Varint(IdField(Id, first, prev)); w.Varint(m);
            w.U16(Quant.QuantizePos(X)); w.U16(Quant.QuantizePos(Y)); w.U16(Quant.QuantizeFacing(Facing));
            if ((m & MKit) != 0) w.U8(WireSelf.Kit(Faction, Weapon));
            if ((m & MHp) != 0) w.U8(HpPct);
            if ((m & MStatus) != 0) w.U8(Status);
            if ((m & MBounty) != 0) w.Varint(Bounty);
            if ((m & MKind) != 0) w.U8(Kind);
            if ((m & MEffects) != 0) w.U16(Effects);
            if ((m & MDisplace) != 0) w.U8(DisplaceSeq);
            if ((m & MMobType) != 0) w.U8(MobTypeCode);
            if ((m & MSig) != 0) w.U8(Sig);
            if ((m & MBarrier) != 0) { if (BarrierPct > 100 || BarrierRingStep > 8) throw new ArgumentException("barrier"); w.U8(BarrierPct); w.U8(BarrierRingStep); }
            if ((m & MArchetype) != 0) w.U8(Archetype);
            if ((m & MFlags) != 0) w.U8(Flags);
            if ((m & MOffhand) != 0) w.U8(Offhand);
            if ((m & MBuildMarks) != 0) w.U8(BuildMarks);
            if ((m & MMark) != 0) { w.U8(MarkTier); w.U8(Brand); w.U8(MarkTax); }
            if ((m & MWalk) != 0) { w.U8(WalkDir); w.U8(WalkTicks); }
        }
    }

    /// <summary>
    /// The changed self fields of a snapshot delta: <see cref="Mask"/> has the head's self field bits; only the fields whose bit is
    /// set mean anything. The self's id is the base's. Bit numbers are append only (rh-protocol delta.rs).
    /// </summary>
    public struct SelfDelta
    {
        public const uint FX = 1 << 1, FY = 1 << 2, FFacing = 1 << 3, FMoveSeq = 1 << 4, FHp = 1 << 5, FReady = 1 << 6,
            FMaxHp = 1 << 9, FKit = 1 << 10, FKills = 1 << 11, FBounty = 1 << 12, FGv = 1 << 13, FStatus = 1 << 14, FEffects = 1 << 15, FDisplace = 1 << 16, FSig = 1 << 17,
            FHuntTarget = 1 << 18, FHuntStacks = 1 << 19;
        /// <summary>The self field bits (never the present, departed or phase bits of the head).</summary>
        public const uint Fields = 0x7e7e | FEffects | FDisplace | FSig | FHuntTarget | FHuntStacks;
        public uint Mask;
        public float X, Y, Facing, Hp, MaxHp;
        public byte Kit;
        public ushort ReadyInMs, Kills, Bounty, Gv;
        public byte MoveSeq, DisplaceSeq, Sig;
        public ushort Effects;
        /// <summary>The new status (0..3) when <see cref="FStatus"/> is set in <see cref="Mask"/>.</summary>
        public byte StatusValue;
        public uint? HuntTarget;
        public ushort HuntStacks;

        internal static SelfDelta Read(ref Reader r, uint mask)
        {
            var s = new SelfDelta { Mask = mask };
            if ((mask & FX) != 0) s.X = r.F32();
            if ((mask & FY) != 0) s.Y = r.F32();
            if ((mask & FFacing) != 0) s.Facing = r.F32();
            if ((mask & FMoveSeq) != 0) s.MoveSeq = r.U8();
            if ((mask & FHp) != 0) s.Hp = r.F32();
            if ((mask & FReady) != 0) s.ReadyInMs = r.U16();
            if ((mask & FMaxHp) != 0) s.MaxHp = r.F32();
            if ((mask & FKit) != 0) s.Kit = r.U8();
            if ((mask & FKills) != 0) s.Kills = r.VarintU16("kills");
            if ((mask & FBounty) != 0) s.Bounty = r.VarintU16("bounty");
            if ((mask & FGv) != 0) s.Gv = r.VarintU16("gv");
            if ((mask & FStatus) != 0) s.StatusValue = r.Code(Wire.MaxStatusCode, "status");
            if ((mask & FEffects) != 0) s.Effects = r.U16();
            if ((mask & FDisplace) != 0) s.DisplaceSeq = r.U8();
            if ((mask & FSig) != 0) s.Sig = r.U8();
            if ((mask & FHuntTarget) != 0) { uint t = r.Varint(); s.HuntTarget = t == 0 ? (uint?)null : t; }
            if ((mask & FHuntStacks) != 0) s.HuntStacks = r.VarintU16("hunt_stacks");
            return s;
        }

        internal void Write(Writer w)
        {
            if ((Mask & FX) != 0) w.F32(X);
            if ((Mask & FY) != 0) w.F32(Y);
            if ((Mask & FFacing) != 0) w.F32(Facing);
            if ((Mask & FMoveSeq) != 0) w.U8(MoveSeq);
            if ((Mask & FHp) != 0) w.F32(Hp);
            if ((Mask & FReady) != 0) w.U16(ReadyInMs);
            if ((Mask & FMaxHp) != 0) w.F32(MaxHp);
            if ((Mask & FKit) != 0) w.U8(Kit);
            if ((Mask & FKills) != 0) w.Varint(Kills);
            if ((Mask & FBounty) != 0) w.Varint(Bounty);
            if ((Mask & FGv) != 0) w.Varint(Gv);
            if ((Mask & FStatus) != 0) w.U8(StatusValue);
            if ((Mask & FEffects) != 0) w.U16(Effects);
            if ((Mask & FDisplace) != 0) w.U8(DisplaceSeq);
            if ((Mask & FSig) != 0) w.U8(Sig);
            if ((Mask & FHuntTarget) != 0) { if (HuntTarget == 0) throw new ArgumentException("hunt target"); w.Varint(HuntTarget ?? 0); }
            if ((Mask & FHuntStacks) != 0) w.Varint(HuntStacks);
        }

        internal void ApplyTo(WireSelf s)
        {
            if ((Mask & FX) != 0) s.X = X;
            if ((Mask & FY) != 0) s.Y = Y;
            if ((Mask & FFacing) != 0) s.Facing = Facing;
            if ((Mask & FMoveSeq) != 0) s.MoveSeq = MoveSeq;
            if ((Mask & FHp) != 0) s.Hp = Hp;
            if ((Mask & FReady) != 0) s.ReadyInMs = ReadyInMs;
            if ((Mask & FMaxHp) != 0) s.MaxHp = MaxHp;
            if ((Mask & FKit) != 0) { s.Faction = (byte)(Kit & 0x0f); s.Weapon = (byte)(Kit >> 4); }
            if ((Mask & FKills) != 0) s.Kills = Kills;
            if ((Mask & FBounty) != 0) s.Bounty = Bounty;
            if ((Mask & FGv) != 0) s.Gv = Gv;
            if ((Mask & FStatus) != 0) s.Status = StatusValue;
            if ((Mask & FEffects) != 0) s.Effects = Effects;
            if ((Mask & FDisplace) != 0) s.DisplaceSeq = DisplaceSeq;
            if ((Mask & FSig) != 0) s.Sig = Sig;
            if ((Mask & FHuntTarget) != 0) s.HuntTarget = HuntTarget;
            if ((Mask & FHuntStacks) != 0) s.HuntStacks = HuntStacks;
        }
    }

    /// <summary>
    /// One changed remote actor of a snapshot delta: <see cref="Mask"/> (bits <see cref="MX"/>..<see cref="MKind"/>, append only)
    /// says which fields are carried; the others mean nothing. A NEW actor (not in the base) starts from the default actor and must
    /// carry its position (<see cref="MNew"/>).
    /// </summary>
    public struct ActorDelta
    {
        public const uint MX = 1 << 0, MY = 1 << 1, MFacing = 1 << 2, MKit = 1 << 3, MHp = 1 << 4, MBounty = 1 << 5, MStatus = 1 << 6,
            MKind = 1 << 7, MEffects = 1 << 8, MDisplace = 1 << 9, MMobType = 1 << 10, MSig = 1 << 11, MBarrier = 1 << 12, MArchetype = 1 << 13,
            MFlags = 1 << 14, MOffhand = 1 << 15, MBuildMarks = 1 << 16, MMark = 1 << 17, MWalk = 1 << 18, MAll = 0x7ffff, MNew = MX | MY | MFacing;
        public uint Id;
        public uint Mask;
        public ushort X, Y, Facing;
        public byte Kit, HpPct, Status, Kind, DisplaceSeq, MobTypeCode, Sig, BarrierPct, BarrierRingStep, Archetype;
        /// <summary>Indicator facts (protocol 16, ADR0026), as on <see cref="WireActor"/>: <see cref="MFlags"/>, <see cref="MOffhand"/>,
        /// <see cref="MBuildMarks"/> and <see cref="MMark"/> (tier, brand, tax) say which are carried.</summary>
        public byte Flags, Offhand, BuildMarks, MarkTier, Brand, MarkTax;
        /// <summary>The walk hint (protocol 19) when <see cref="MWalk"/> is set; (0, 0) clears it.</summary>
        public byte WalkDir, WalkTicks;
        public ushort Bounty, Effects;

        /// <summary>The carried indicator facts' first fault (fields outside the mask count as zero), or null.</summary>
        internal string IndicatorFault()
        {
            bool mark = (Mask & MMark) != 0;
            return WireIndicators.Fault((Mask & MOffhand) != 0 ? Offhand : (byte)0, (Mask & MBuildMarks) != 0 ? BuildMarks : (byte)0,
                mark ? MarkTier : (byte)0, mark ? Brand : (byte)0, mark ? MarkTax : (byte)0, (Mask & MFlags) != 0 ? Flags : (byte)0);
        }
    }
}
