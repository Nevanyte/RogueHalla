using System;
using System.Collections.Generic;

namespace Roguehalla.Net.Protocol
{
    /// <summary>Server-published stock and timing facts for the receiving player's Wish.</summary>
    public sealed class WireWishOwner
    {
        public byte Ability;
        public double ReadyAt, RechargeMs;
        public double? FullAt, NextChargeAt;
        public byte MaxCharges, Charges;
        public WireWishCast Cast;

        internal void CopyFrom(WireWishOwner o)
        {
            Ability = o.Ability; ReadyAt = o.ReadyAt; FullAt = o.FullAt; MaxCharges = o.MaxCharges;
            RechargeMs = o.RechargeMs; Charges = o.Charges; NextChargeAt = o.NextChargeAt;
            if (o.Cast == null) Cast = null;
            else { if (Cast == null) Cast = new WireWishCast(); Cast.CopyFrom(o.Cast); }
        }
    }

    /// <summary>Public authoritative identity and deadlines of the currently active Wish cast.</summary>
    public sealed class WireWishCast
    {
        public byte Ability;
        public double StartedAt, LandsAt;

        internal void CopyFrom(WireWishCast o) { Ability = o.Ability; StartedAt = o.StartedAt; LandsAt = o.LandsAt; }
    }

    /// <summary>Field validation and shared wire primitives for protocol-12 Wish and barrier facts.</summary>
    internal static class WishWire
    {
        internal const byte MaxAbility = 4, BlazeAbility = 3;

        static bool Clock(double v) { return v >= 0 && !double.IsNaN(v) && !double.IsInfinity(v); }
        static bool Amount(float v) { return v >= 0 && !float.IsNaN(v) && !float.IsInfinity(v); }
        internal static void ValidateOwner(WireWishOwner v)
        {
            if (v == null || v.Ability > MaxAbility || !Clock(v.ReadyAt) || (v.FullAt.HasValue && !Clock(v.FullAt.Value)) ||
                v.MaxCharges < 1 || v.MaxCharges > 2 || !Clock(v.RechargeMs) || v.Charges > v.MaxCharges ||
                (v.NextChargeAt.HasValue && !Clock(v.NextChargeAt.Value))) throw new ArgumentException("wish owner fields");
            if (v.Cast != null && (v.Cast.Ability > MaxAbility || !Clock(v.Cast.StartedAt) || !Clock(v.Cast.LandsAt) || v.Cast.StartedAt > v.Cast.LandsAt))
                throw new ArgumentException("wish cast fields");
        }
        internal static void ValidateBarrier(float barrier, byte ring)
        {
            if (!Amount(barrier) || ring > 8) throw new ArgumentException("barrier");
        }
        internal static void ValidateClock(double time, string what)
        {
            if (!Clock(time)) throw new ArgumentException(what);
        }
        internal static void WriteOwner(Writer w, WireWishOwner v)
        {
            ValidateOwner(v);
            w.U8(v.Ability); w.F64(v.ReadyAt); OptF64(w, v.FullAt); w.U8(v.MaxCharges); w.F64(v.RechargeMs); w.U8(v.Charges);
            OptF64(w, v.NextChargeAt);
            w.U8(v.Cast == null ? (byte)0 : (byte)1);
            if (v.Cast != null) { w.U8(v.Cast.Ability); w.F64(v.Cast.StartedAt); w.F64(v.Cast.LandsAt); }
        }
        internal static WireWishOwner ReadOwner(ref Reader r, WireWishOwner reuse = null)
        {
            var v = reuse ?? new WireWishOwner();
            v.Ability = r.Code(MaxAbility, "wish ability"); v.ReadyAt = r.F64();
            v.FullAt = r.Flag() ? r.F64() : (double?)null;
            v.MaxCharges = r.U8(); v.RechargeMs = r.F64(); v.Charges = r.U8();
            v.NextChargeAt = r.Flag() ? r.F64() : (double?)null;
            if (r.Flag())
            {
                if (v.Cast == null) v.Cast = new WireWishCast();
                v.Cast.Ability = r.Code(MaxAbility, "wish cast ability"); v.Cast.StartedAt = r.F64(); v.Cast.LandsAt = r.F64();
            }
            else v.Cast = null;
            if (!Clock(v.ReadyAt) || (v.FullAt.HasValue && !Clock(v.FullAt.Value)) || v.MaxCharges < 1 || v.MaxCharges > 2 ||
                !Clock(v.RechargeMs) || v.Charges > v.MaxCharges || (v.NextChargeAt.HasValue && !Clock(v.NextChargeAt.Value)) ||
                (v.Cast != null && (!Clock(v.Cast.StartedAt) || !Clock(v.Cast.LandsAt) || v.Cast.StartedAt > v.Cast.LandsAt)))
                throw new DecodeException(DecodeError.BadField, "wish owner");
            return v;
        }
        internal static void WritePoint(Writer w, float x, float y)
        {
            if (!Quant.PosInRange(x) || !Quant.PosInRange(y)) throw new ArgumentException("position");
            w.U16(Quant.QuantizePos(x)); w.U16(Quant.QuantizePos(y));
        }
        internal static void ReadPoint(ref Reader r, out float x, out float y)
        {
            x = Quant.DequantizePos(r.U16()); y = Quant.DequantizePos(r.U16());
        }
        internal static void OptF64(Writer w, double? v) { if (v.HasValue) { w.U8(1); w.F64(v.Value); } else w.U8(0); }
    }

    /// <summary>Authoritative owner lock. Held targets are public actor references, never raw native IDs.</summary>
    public enum WireOwnerLockKind : byte { Clear = 0, Held = 1 }

    public struct WireOwnerLock
    {
        public WireOwnerLockKind Kind;
        public uint? Target;
    }

    /// <summary>Exact server-published owner combat facts; no readiness or weapon timing is derived client-side.</summary>
    public sealed class WireOwnerCombat
    {
        public WireOwnerLock Lock;
        public uint DrawStartedAt, ReadyAt;
        public double AsMilli, WindupMilli, SpeedMilli;

        internal void CopyFrom(WireOwnerCombat o)
        {
            Lock = o.Lock; DrawStartedAt = o.DrawStartedAt; ReadyAt = o.ReadyAt;
            AsMilli = o.AsMilli; WindupMilli = o.WindupMilli; SpeedMilli = o.SpeedMilli;
        }

    }

    internal static class OwnerCombatWire
    {
        static bool ValidMultiplier(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 50d && Math.Truncate(value) == value;
        }

        internal static void Validate(WireOwnerCombat value)
        {
            if (value == null || (byte)value.Lock.Kind > (byte)WireOwnerLockKind.Held ||
                (value.Lock.Kind == WireOwnerLockKind.Clear && value.Lock.Target.HasValue) ||
                (value.Lock.Target.HasValue && value.Lock.Target.Value == 0) ||
                !ValidMultiplier(value.AsMilli) || !ValidMultiplier(value.WindupMilli) || !ValidMultiplier(value.SpeedMilli))
                throw new ArgumentException("owner combat");
        }

        internal static int Length(WireOwnerCombat value)
        {
            Validate(value);
            return 1 + (value.Lock.Kind == WireOwnerLockKind.Held && value.Lock.Target.HasValue ? Writer.VarintLength(value.Lock.Target.Value) : 0) + 8 * 3 + 4 * 2;
        }

        internal static void Write(Writer w, WireOwnerCombat value)
        {
            Validate(value);
            byte lockTag = value.Lock.Kind == WireOwnerLockKind.Clear ? (byte)0 : value.Lock.Target.HasValue ? (byte)2 : (byte)1;
            w.U8(lockTag);
            if (lockTag == 2) w.Varint(value.Lock.Target.Value);
            w.U32(value.DrawStartedAt); w.U32(value.ReadyAt);
            w.F64(value.AsMilli); w.F64(value.WindupMilli); w.F64(value.SpeedMilli);
        }

        internal static WireOwnerCombat Read(ref Reader r, WireOwnerCombat reuse = null)
        {
            var value = reuse ?? new WireOwnerCombat();
            byte tag = r.U8();
            if (tag > 2) throw new DecodeException(DecodeError.BadField, "owner combat lock");
            value.Lock.Kind = tag == 0 ? WireOwnerLockKind.Clear : WireOwnerLockKind.Held;
            value.Lock.Target = tag == 2 ? (uint?)r.Varint() : (uint?)null;
            value.DrawStartedAt = r.U32(); value.ReadyAt = r.U32();
            value.AsMilli = r.F64(); value.WindupMilli = r.F64(); value.SpeedMilli = r.F64();
            if ((tag == 2 && value.Lock.Target == 0) || !ValidMultiplier(value.AsMilli) || !ValidMultiplier(value.WindupMilli) || !ValidMultiplier(value.SpeedMilli))
                throw new DecodeException(DecodeError.BadField, "owner combat");
            return value;
        }
    }
}
