using System;
using System.Collections.Generic;

namespace Roguehalla.Net.Protocol
{
    /// <summary>The Snapshot and SnapshotDelta frames of <see cref="ServerMsg"/>: the full and the incremental world view.</summary>
    public abstract partial class ServerMsg
    {
        public sealed class Snapshot : ServerMsg
        {
            static bool ContainsActor(WireActor[] actors, uint id)
            {
                int lo = 0, hi = actors == null ? 0 : actors.Length;
                while (lo < hi)
                {
                    int mid = lo + ((hi - lo) >> 1);
                    if (actors[mid].Id == id) return true;
                    if (actors[mid].Id < id) lo = mid + 1; else hi = mid;
                }
                return false;
            }

            public uint Tick;
            public byte Phase;
            public uint AckInput;
            /// <summary>Null for a spectator (or a dead player's view without a self).</summary>
            public WireSelf Me;
            /// <summary>Sorted by id, strictly ascending (C-OBS; the wire needs it). Never null; shared empty array when nothing is visible.</summary>
            public WireActor[] Actors = Array.Empty<WireActor>();
            /// <summary>Owner-combat publication; null means unavailable for this frame.</summary>
            public WireOwnerCombat OwnerCombat;
            /// <summary>Protocol 11: the source's public training fact (head bit 16). False for every ordinary match.</summary>
            public bool Training;

            // A nullable double occupies 16 bytes on this runtime. Pack its presence into a byte and keep the value separately
            // so the new optional owner-combat reference does not increase every pooled Snapshot object's size.
            bool _hasServerTimeOverride;
            double _serverTimeOverrideMs;

            /// <summary>Optional per-frame authoritative source time in ms. Null encodes the canonical Tick * 50 default.</summary>
            public double? ServerTimeOverrideMs
            {
                get { return _hasServerTimeOverride ? _serverTimeOverrideMs : (double?)null; }
                set { _hasServerTimeOverride = value.HasValue; if (value.HasValue) _serverTimeOverrideMs = value.Value; }
            }

            /// <summary>The accepted source time for this frame, using Tick * 50 only when the frame has no override.</summary>
            public double ServerMs { get { return _hasServerTimeOverride ? _serverTimeOverrideMs : Tick * (double)Wire.TickMs; } }

            bool HasNonDefaultTime
            {
                get { return _hasServerTimeOverride && BitConverter.DoubleToInt64Bits(_serverTimeOverrideMs) != BitConverter.DoubleToInt64Bits(Tick * (double)Wire.TickMs); }
            }

            /// <summary>Exact size of <see cref="Encode"/>'s frame.</summary>
            public int EncodedLength
            {
                get
                {
                    uint head = Me != null ? Me.Head(Phase) : (Phase != Wire.DefaultPhase ? WireSelf.HPhase : 0);
                    if (Training) head |= WireSelf.HTraining;
                    if (HasNonDefaultTime) head |= WireSelf.HServerTime;
                    if (OwnerCombat != null) head |= WireSelf.HOwnerCombat;
                    int n = 1 + Writer.VarintLength(Tick) + Writer.VarintLength(AckInput) + WireSelf.HeadLength(head);
                    if (Me != null) n += Me.Length(head);
                    if ((head & WireSelf.HServerTime) != 0) n += 8;
                    n += Writer.VarintLength((uint)Actors.Length);
                    uint prev = 0;
                    for (int i = 0; i < Actors.Length; i++) { n += Actors[i].Length(i == 0, prev); prev = Actors[i].Id; }
                    if ((head & WireSelf.HOwnerCombat) != 0) n += OwnerCombatWire.Length(OwnerCombat);
                    return n;
                }
            }

            public override byte[] Encode()
            {
                // The encoder never writes a frame every decoder rejects (and never trims one to fit): rh-protocol does the same.
                if (Actors.Length > Wire.MaxActors) throw new ArgumentException("more than " + Wire.MaxActors + " actors");
                if (Phase > Wire.MaxPhaseCode) throw new ArgumentException("phase");
                if (Me != null && (Me.Faction > Wire.MaxKitCode || Me.Weapon > Wire.MaxKitCode || Me.Status > Wire.MaxStatusCode)) throw new ArgumentException("self faction/weapon/status");
                if (Me != null && Me.HuntTarget == 0) throw new ArgumentException("hunt target");
                if (ServerTimeOverrideMs.HasValue) WishWire.ValidateClock(ServerTimeOverrideMs.Value, "server time");
                if (Me != null) WishWire.ValidateBarrier(Me.Barrier, Me.BarrierRingStep);
                if (Me != null) WireIndicators.CheckEncoded(0, Me.BuildMarks, Me.MarkTier, Me.Brand, Me.MarkTax, Me.Flags);
                if (OwnerCombat != null && Me == null) throw new ArgumentException("owner combat without self");
                for (int i = 0; i < Actors.Length; i++)
                {
                    var a = Actors[i];
                    if (a.Faction > Wire.MaxKitCode || a.Weapon > Wire.MaxKitCode || a.Status > Wire.MaxStatusCode) throw new ArgumentException("actor faction/weapon/status");
                    WireIndicators.CheckEncoded(a.Offhand, a.BuildMarks, a.MarkTier, a.Brand, a.MarkTax, a.Flags);
                    if (!a.HasValidGiverIdentity) throw new ArgumentException("giver actor");
                    if (a.HpPct > 100) throw new ArgumentException("hp_pct");
                    if (!Quant.PosInRange(a.X) || !Quant.PosInRange(a.Y)) throw new ArgumentException("position");
                    if (i > 0 && a.Id <= Actors[i - 1].Id) throw new ArgumentException("actor ids must strictly ascend");
                }
                if (OwnerCombat != null)
                {
                    OwnerCombatWire.Validate(OwnerCombat);
                    if (OwnerCombat.Lock.Target.HasValue && !ContainsActor(Actors, OwnerCombat.Lock.Target.Value))
                        throw new ArgumentException("owner combat target");
                }
                var w = new Writer(EncodedLength);
                w.U8(TSnapshot); w.Varint(Tick); w.Varint(AckInput);
                uint head = Me != null ? Me.Head(Phase) : (Phase != Wire.DefaultPhase ? WireSelf.HPhase : 0);
                if (Training) head |= WireSelf.HTraining;
                if (HasNonDefaultTime) head |= WireSelf.HServerTime;
                if (OwnerCombat != null) head |= WireSelf.HOwnerCombat;
                w.Varint(head);
                if (Me != null) Me.Write(w, head);
                if ((head & WireSelf.HPhase) != 0) w.U8(Phase);
                if ((head & WireSelf.HServerTime) != 0) w.F64(ServerTimeOverrideMs.Value);
                w.Varint((uint)Actors.Length);
                uint prev = 0;
                for (int i = 0; i < Actors.Length; i++) { Actors[i].Write(w, i == 0, prev); prev = Actors[i].Id; }
                if (OwnerCombat != null) OwnerCombatWire.Write(w, OwnerCombat);
                return w.Finish();
            }

            /// <summary>Reads the body of a Snapshot frame into <paramref name="s"/>, reusing its Me object and actor array.</summary>
            internal static Snapshot Read(ref Reader r, Snapshot s)
            {
                s.Tick = r.Varint(); s.AckInput = r.Varint();
                uint head = r.Varint();
                if ((head & ~WireSelf.Known) != 0 || ((head & WireSelf.HPresent) == 0 && (head & WireSelf.SelfBits) != 0) ||
                    ((head & (WireSelf.HWish | WireSelf.HBarrier | WireSelf.HOwnerCombat | WireSelf.HArchetype)) != 0 && (head & WireSelf.HPresent) == 0))
                    throw new DecodeException(DecodeError.BadField, "head");
                s.Training = (head & WireSelf.HTraining) != 0;
                if ((head & WireSelf.HPresent) != 0)
                {
                    var me = s.Me ?? new WireSelf();
                    me.Read(ref r, head);
                    s.Me = me;
                }
                else s.Me = null;
                s.Phase = (head & WireSelf.HPhase) != 0 ? r.Code(Wire.MaxPhaseCode, "phase") : Wire.DefaultPhase;
                s.ServerTimeOverrideMs = (head & WireSelf.HServerTime) != 0 ? r.F64() : (double?)null;
                if (s.ServerTimeOverrideMs.HasValue && (s.ServerTimeOverrideMs.Value < 0 || double.IsNaN(s.ServerTimeOverrideMs.Value) || double.IsInfinity(s.ServerTimeOverrideMs.Value)))
                    throw new DecodeException(DecodeError.BadField, "server time");
                uint count = r.Varint();
                if (count > (uint)Wire.MaxActors) throw new DecodeException(DecodeError.TooMany, count.ToString());
                int n = (int)count;
                var actors = s.Actors;
                if (actors == null || actors.Length != n) actors = n == 0 ? Array.Empty<WireActor>() : new WireActor[n];
                uint prev = 0;
                for (int i = 0; i < n; i++) { actors[i] = WireActor.Read(ref r, i == 0, prev); prev = actors[i].Id; }
                s.Actors = actors;
                s.OwnerCombat = (head & WireSelf.HOwnerCombat) != 0 ? OwnerCombatWire.Read(ref r, s.OwnerCombat) : null;
                if (s.OwnerCombat != null && s.OwnerCombat.Lock.Target.HasValue && !ContainsActor(s.Actors, s.OwnerCombat.Lock.Target.Value))
                    throw new DecodeException(DecodeError.BadField, "owner combat target");
                return s;
            }
        }

        /// <summary>
        /// A snapshot as a delta against the last one the connection received (v3, v4 masks; layout in crates/rh-protocol/src/lib.rs). A
        /// client does not use this directly: <see cref="SnapshotTracker"/> applies it and hands out the resolved
        /// <see cref="Snapshot"/>. The lists are arrays with counts so that a tracker's one instance decodes every frame without
        /// garbage.
        /// </summary>
        public sealed class SnapshotDelta : ServerMsg
        {
            public uint Tick, BaseTick, AckInput;
            /// <summary>The new phase when <see cref="HasPhase"/> (a delta carries a field only when it differs from the base).</summary>
            public byte Phase;
            public bool HasPhase;
            public bool HasSelf;
            public SelfDelta Me;
            public ActorDelta[] Changed = new ActorDelta[8];
            public int ChangedCount;
            public uint[] Departed = new uint[8];
            public int DepartedCount;
            /// <summary>Protocol 11: the source's public training fact (delta head bit 20). False for every ordinary match.</summary>
            public bool Training;

            /// <summary>Whether this delta carries the Wish owner group; Wish=null explicitly clears a prior record.</summary>
            public bool HasWish;
            public WireWishOwner Wish;
            /// <summary>Whether this delta carries an owner barrier/ring pair, including a zero clear.</summary>
            public bool HasBarrier;
            public float Barrier;
            public byte BarrierRingStep;
            /// <summary>Whether this delta carries the self's hitbox archetype id (protocol 16, head bit 25).</summary>
            public bool HasArchetype;
            public byte Archetype;
            /// <summary>Whether this delta carries the self's indicator flags (protocol 16, ADR0026, head bit 26); see <see cref="WireSelf.Flags"/>.</summary>
            public bool HasFlags;
            public byte Flags;
            /// <summary>Whether this delta carries the self's packed build marks (head bit 27); see <see cref="WireSelf.BuildMarks"/>.</summary>
            public bool HasBuildMarks;
            public byte BuildMarks;
            /// <summary>Whether this delta carries the self's mark plate (head bit 28): tier, brand, tax.</summary>
            public bool HasMark;
            public byte MarkTier, Brand, MarkTax;
            /// <summary>Whether this delta carries owner-combat availability; null explicitly clears, omission retains.</summary>
            public bool HasOwnerCombat;
            public WireOwnerCombat OwnerCombat;
            /// <summary>Optional absolute per-frame source time; absent means Tick * 50, never the base's time.</summary>
            public double? ServerTimeOverrideMs;

            // Head bits (append only): 0 self fields, 7 departed, 8 phase, 20 training, 21 Wish, 22 barrier, 23 server time, 24 owner combat.
            const uint HPresent = 1 << 0, HDeparted = 1 << 7, HPhase = 1 << 8, HTraining = 1 << 20,
                HWish = 1 << 21, HBarrier = 1 << 22, HServerTime = 1 << 23, HOwnerCombat = 1 << 24, HArchetype = 1 << 25,
                HFlags = 1 << 26, HBuildMarks = 1 << 27, HMark = 1 << 28,
                Known = HPresent | SelfDelta.Fields | HPhase | HDeparted | HTraining | HWish | HBarrier | HServerTime | HOwnerCombat | HArchetype | HFlags | HBuildMarks | HMark;

            /// <summary>Reads the body of a delta frame (after the tag): syntax and ranges only; whether it applies is the tracker's question.</summary>
            internal static SnapshotDelta Read(ref Reader r, SnapshotDelta d)
            {
                d.Tick = r.Varint();
                uint age = r.Varint();
                if (age < 1 || age > Wire.MaxDeltaAge || age > d.Tick) throw new DecodeException(DecodeError.BadField, "base age");
                d.BaseTick = d.Tick - age;
                d.AckInput = r.Varint();
                uint head = r.Varint();
                if ((head & ~Known) != 0 || ((head & HPresent) == 0 && (head & SelfDelta.Fields) != 0)) throw new DecodeException(DecodeError.BadField, "head");
                d.Training = (head & HTraining) != 0;
                d.HasSelf = (head & HPresent) != 0;
                d.HasWish = (head & HWish) != 0;
                d.HasBarrier = (head & HBarrier) != 0;
                d.HasArchetype = (head & HArchetype) != 0;
                d.HasFlags = (head & HFlags) != 0;
                d.HasBuildMarks = (head & HBuildMarks) != 0;
                d.HasMark = (head & HMark) != 0;
                d.HasOwnerCombat = (head & HOwnerCombat) != 0;
                if ((d.HasWish || d.HasBarrier || d.HasArchetype || d.HasFlags || d.HasBuildMarks || d.HasMark) && !d.HasSelf) throw new DecodeException(DecodeError.BadField, "owner group without self");
                d.Me = d.HasSelf ? SelfDelta.Read(ref r, head & SelfDelta.Fields) : default(SelfDelta);
                d.Wish = d.HasWish && r.Flag() ? WishWire.ReadOwner(ref r, d.Wish) : null;
                if (d.HasBarrier)
                {
                    d.Barrier = r.F32(); d.BarrierRingStep = r.U8();
                    if (d.Barrier < 0 || float.IsNaN(d.Barrier) || float.IsInfinity(d.Barrier) || d.BarrierRingStep > 8)
                        throw new DecodeException(DecodeError.BadField, "barrier");
                }
                d.Archetype = d.HasArchetype ? r.U8() : (byte)0;
                d.Flags = d.HasFlags ? r.U8() : (byte)0;
                d.BuildMarks = d.HasBuildMarks ? r.U8() : (byte)0;
                if (d.HasMark) { d.MarkTier = r.U8(); d.Brand = r.U8(); d.MarkTax = r.U8(); }
                else { d.MarkTier = 0; d.Brand = 0; d.MarkTax = 0; }
                WireIndicators.CheckDecoded(0, d.BuildMarks, d.MarkTier, d.Brand, d.MarkTax, d.Flags);
                d.HasPhase = (head & HPhase) != 0;
                d.Phase = d.HasPhase ? r.Code(Wire.MaxPhaseCode, "phase") : (byte)0;
                d.ServerTimeOverrideMs = (head & HServerTime) != 0 ? r.F64() : (double?)null;
                if (d.ServerTimeOverrideMs.HasValue && (d.ServerTimeOverrideMs.Value < 0 || double.IsNaN(d.ServerTimeOverrideMs.Value) || double.IsInfinity(d.ServerTimeOverrideMs.Value)))
                    throw new DecodeException(DecodeError.BadField, "server time");
                uint n = r.Varint();
                if (n > (uint)Wire.MaxActors) throw new DecodeException(DecodeError.TooMany, n.ToString());
                if (d.Changed.Length < n) d.Changed = new ActorDelta[Math.Max((int)n, d.Changed.Length * 2)];
                d.ChangedCount = (int)n;
                uint prev = 0;
                for (int i = 0; i < (int)n; i++)
                {
                    uint field = r.Varint();
                    ulong id = i == 0 ? field : (ulong)prev + field;
                    if (i > 0 && (field == 0 || id > uint.MaxValue)) throw new DecodeException(DecodeError.BadField, "actor order");
                    prev = (uint)id;
                    ActorDelta a = default(ActorDelta);
                    a.Id = prev;
                    a.Mask = r.Varint();
                    if ((a.Mask & ~ActorDelta.MAll) != 0) throw new DecodeException(DecodeError.BadField, "actor mask");
                    if ((a.Mask & ActorDelta.MX) != 0) a.X = r.U16();
                    if ((a.Mask & ActorDelta.MY) != 0) a.Y = r.U16();
                    if ((a.Mask & ActorDelta.MFacing) != 0) a.Facing = r.U16();
                    if ((a.Mask & ActorDelta.MKit) != 0) a.Kit = r.U8();
                    if ((a.Mask & ActorDelta.MHp) != 0) { a.HpPct = r.U8(); if (a.HpPct > 100) throw new DecodeException(DecodeError.BadField, "hp_pct"); }
                    if ((a.Mask & ActorDelta.MBounty) != 0) a.Bounty = r.VarintU16("bounty");
                    if ((a.Mask & ActorDelta.MStatus) != 0) a.Status = r.Code(Wire.MaxStatusCode, "status");
                    if ((a.Mask & ActorDelta.MKind) != 0) a.Kind = r.U8();
                    if ((a.Mask & ActorDelta.MEffects) != 0) a.Effects = r.U16();
                    if ((a.Mask & ActorDelta.MDisplace) != 0) a.DisplaceSeq = r.U8();
                    if ((a.Mask & ActorDelta.MMobType) != 0) a.MobTypeCode = r.Code(30,"mob type");
                    if ((a.Mask & ActorDelta.MSig) != 0) a.Sig = r.U8();
                    if ((a.Mask & ActorDelta.MBarrier) != 0) { a.BarrierPct = r.U8(); a.BarrierRingStep = r.U8(); if (a.BarrierPct > 100 || a.BarrierRingStep > 8) throw new DecodeException(DecodeError.BadField, "barrier"); }
                    if ((a.Mask & ActorDelta.MArchetype) != 0) a.Archetype = r.U8();
                    if ((a.Mask & ActorDelta.MFlags) != 0) a.Flags = r.U8();
                    if ((a.Mask & ActorDelta.MOffhand) != 0) a.Offhand = r.U8();
                    if ((a.Mask & ActorDelta.MBuildMarks) != 0) a.BuildMarks = r.U8();
                    if ((a.Mask & ActorDelta.MMark) != 0) { a.MarkTier = r.U8(); a.Brand = r.U8(); a.MarkTax = r.U8(); }
                    string fault = a.IndicatorFault();
                    if (fault != null) throw new DecodeException(DecodeError.BadField, fault);
                    d.Changed[i] = a;
                }
                d.DepartedCount = 0;
                if ((head & HDeparted) != 0)
                {
                    uint k = r.Varint();
                    if (k == 0) throw new DecodeException(DecodeError.BadField, "departed");
                    if (k > (uint)Wire.MaxActors) throw new DecodeException(DecodeError.TooMany, k.ToString());
                    if (d.Departed.Length < k) d.Departed = new uint[Math.Max((int)k, d.Departed.Length * 2)];
                    d.DepartedCount = (int)k;
                    uint last = 0;
                    for (int i = 0; i < (int)k; i++)
                    {
                        uint field = r.Varint();
                        ulong id = i == 0 ? field : (ulong)last + field;
                        if (i > 0 && (field == 0 || id > uint.MaxValue)) throw new DecodeException(DecodeError.BadField, "departed order");
                        last = (uint)id;
                        d.Departed[i] = last;
                    }
                }
                d.OwnerCombat = null;
                if (d.HasOwnerCombat)
                {
                    byte available = r.U8();
                    if (available > 1) throw new DecodeException(DecodeError.BadField, "owner combat availability");
                    if (available != 0) d.OwnerCombat = OwnerCombatWire.Read(ref r, d.OwnerCombat);
                    if (d.OwnerCombat != null && !d.HasSelf) throw new DecodeException(DecodeError.BadField, "owner combat");
                }
                return d;
            }

            public override byte[] Encode()
            {
                if (Tick < BaseTick + 1 || Tick - BaseTick > Wire.MaxDeltaAge) throw new ArgumentException("base age");
                if (HasPhase && Phase > Wire.MaxPhaseCode) throw new ArgumentException("phase");
                if (ChangedCount > Wire.MaxActors || DepartedCount > Wire.MaxActors) throw new ArgumentException("too many actors");
                if (HasSelf && (Me.Mask & ~SelfDelta.Fields) != 0) throw new ArgumentException("head");
                if ((HasWish || HasBarrier || HasArchetype || HasFlags || HasBuildMarks || HasMark) && !HasSelf) throw new ArgumentException("owner group without self");
                WireIndicators.CheckEncoded(0, HasBuildMarks ? BuildMarks : (byte)0, HasMark ? MarkTier : (byte)0, HasMark ? Brand : (byte)0,
                    HasMark ? MarkTax : (byte)0, HasFlags ? Flags : (byte)0);
                if (HasOwnerCombat && OwnerCombat != null && !HasSelf) throw new ArgumentException("owner combat without self");
                if (HasWish && Wish != null) WishWire.ValidateOwner(Wish);
                if (HasBarrier) WishWire.ValidateBarrier(Barrier, BarrierRingStep);
                if (HasOwnerCombat && OwnerCombat != null) OwnerCombatWire.Validate(OwnerCombat);
                if (ServerTimeOverrideMs.HasValue) WishWire.ValidateClock(ServerTimeOverrideMs.Value, "server time");
                if (HasSelf && (Me.Mask & SelfDelta.FStatus) != 0 && Me.StatusValue > Wire.MaxStatusCode) throw new ArgumentException("status");
                for (int i = 0; i < ChangedCount; i++)
                {
                    var a = Changed[i];
                    if ((a.Mask & ActorDelta.MStatus) != 0 && a.Status > Wire.MaxStatusCode) throw new ArgumentException("status");
                    if ((a.Mask & ~ActorDelta.MAll) != 0) throw new ArgumentException("actor mask");
                    if ((a.Mask & ActorDelta.MHp) != 0 && a.HpPct > 100) throw new ArgumentException("hp_pct");
                    string fault = a.IndicatorFault();
                    if (fault != null) throw new ArgumentException(fault);
                    if (i > 0 && a.Id <= Changed[i - 1].Id) throw new ArgumentException("actor ids must strictly ascend");
                }
                for (int i = 1; i < DepartedCount; i++) if (Departed[i] <= Departed[i - 1]) throw new ArgumentException("departed ids must strictly ascend");
                var w = new Writer();
                w.U8(TSnapshotDelta); w.Varint(Tick); w.Varint(Tick - BaseTick); w.Varint(AckInput);
                uint head = 0;
                if (Training) head |= HTraining;
                if (HasSelf) head |= HPresent | (Me.Mask & SelfDelta.Fields);
                if (HasWish) head |= HWish;
                if (HasBarrier) head |= HBarrier;
                if (HasArchetype) head |= HArchetype;
                if (HasFlags) head |= HFlags;
                if (HasBuildMarks) head |= HBuildMarks;
                if (HasMark) head |= HMark;
                if (HasOwnerCombat) head |= HOwnerCombat;
                bool hasTime = ServerTimeOverrideMs.HasValue && BitConverter.DoubleToInt64Bits(ServerTimeOverrideMs.Value) != BitConverter.DoubleToInt64Bits(Tick * (double)Wire.TickMs);
                if (hasTime) head |= HServerTime;
                if (HasPhase) head |= HPhase;
                if (DepartedCount > 0) head |= HDeparted;
                w.Varint(head);
                if (HasSelf) Me.Write(w);
                if (HasWish) { w.U8(Wish == null ? (byte)0 : (byte)1); if (Wish != null) WishWire.WriteOwner(w, Wish); }
                if (HasBarrier) { w.F32(Barrier); w.U8(BarrierRingStep); }
                if (HasArchetype) w.U8(Archetype);
                if (HasFlags) w.U8(Flags);
                if (HasBuildMarks) w.U8(BuildMarks);
                if (HasMark) { w.U8(MarkTier); w.U8(Brand); w.U8(MarkTax); }
                if (HasPhase) w.U8(Phase);
                if (hasTime) w.F64(ServerTimeOverrideMs.Value);
                w.Varint((uint)ChangedCount);
                for (int i = 0; i < ChangedCount; i++)
                {
                    var a = Changed[i];
                    w.Varint(i == 0 ? a.Id : a.Id - Changed[i - 1].Id);
                    if ((a.Mask & ActorDelta.MMobType)!=0 && a.MobTypeCode>30) throw new ArgumentException("mob type");
                    w.Varint(a.Mask & ActorDelta.MAll);
                    if ((a.Mask & ActorDelta.MX) != 0) w.U16(a.X);
                    if ((a.Mask & ActorDelta.MY) != 0) w.U16(a.Y);
                    if ((a.Mask & ActorDelta.MFacing) != 0) w.U16(a.Facing);
                    if ((a.Mask & ActorDelta.MKit) != 0) w.U8(a.Kit);
                    if ((a.Mask & ActorDelta.MHp) != 0) w.U8(a.HpPct);
                    if ((a.Mask & ActorDelta.MBounty) != 0) w.Varint(a.Bounty);
                    if ((a.Mask & ActorDelta.MStatus) != 0) w.U8(a.Status);
                    if ((a.Mask & ActorDelta.MKind) != 0) w.U8(a.Kind);
                    if ((a.Mask & ActorDelta.MEffects) != 0) w.U16(a.Effects);
                    if ((a.Mask & ActorDelta.MDisplace) != 0) w.U8(a.DisplaceSeq);
                    if ((a.Mask & ActorDelta.MMobType) != 0) w.U8(a.MobTypeCode);
                    if ((a.Mask & ActorDelta.MSig) != 0) w.U8(a.Sig);
                    if ((a.Mask & ActorDelta.MBarrier) != 0) { if (a.BarrierPct > 100 || a.BarrierRingStep > 8) throw new ArgumentException("barrier"); w.U8(a.BarrierPct); w.U8(a.BarrierRingStep); }
                    if ((a.Mask & ActorDelta.MArchetype) != 0) w.U8(a.Archetype);
                    if ((a.Mask & ActorDelta.MFlags) != 0) w.U8(a.Flags);
                    if ((a.Mask & ActorDelta.MOffhand) != 0) w.U8(a.Offhand);
                    if ((a.Mask & ActorDelta.MBuildMarks) != 0) w.U8(a.BuildMarks);
                    if ((a.Mask & ActorDelta.MMark) != 0) { w.U8(a.MarkTier); w.U8(a.Brand); w.U8(a.MarkTax); }
                }
                if (DepartedCount > 0)
                {
                    w.Varint((uint)DepartedCount);
                    for (int i = 0; i < DepartedCount; i++) w.Varint(i == 0 ? Departed[i] : Departed[i] - Departed[i - 1]);
                }
                if (HasOwnerCombat)
                {
                    w.U8(OwnerCombat == null ? (byte)0 : (byte)1);
                    if (OwnerCombat != null) OwnerCombatWire.Write(w, OwnerCombat);
                }
                return w.Finish();
            }
        }
    }
}
