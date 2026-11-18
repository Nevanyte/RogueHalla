using System;
using System.Collections.Generic;

namespace Roguehalla.Net.Protocol
{
    /// <summary>The Events, Fx, State and Pong frames of <see cref="ServerMsg"/>.</summary>
    public abstract partial class ServerMsg
    {
        public sealed class Events : ServerMsg
        {
            public uint Tick;
            public List<WireEvent> List = new List<WireEvent>();

            public override byte[] Encode()
            {
                if (List.Count > ushort.MaxValue) throw new ArgumentException("too many events");
                int len = 7;
                foreach (var e in List)
                {
                    switch (e.Kind)
                    {
                        case EventKind.Death: len += 6 + (e.By.HasValue ? 4 : 0); break;
                        case EventKind.Bounty: len += 7; break;
                        case EventKind.MatchEnded: len += 3 + (e.Winner.HasValue ? 4 : 0); break;
                        case EventKind.PotionNotice: case EventKind.PickupRefused: case EventKind.ChestRefused: len += 2; break;
                        case EventKind.WindEmblem: len += 1; break;
                        case EventKind.CampCleared: len += 3; break;
                        case EventKind.CannonNotice:
                            if (e.CannonCode < 1 || e.CannonCode > 4 || (e.CannonCode == 4) != e.CannonSeconds.HasValue || (e.CannonSeconds.HasValue && (e.CannonSeconds.Value < 1 || e.CannonSeconds.Value > 75))) throw new ArgumentException("cannon notice");
                            len += 2 + (e.CannonSeconds.HasValue ? 2 : 0); break;
                        case EventKind.Result: len += 20 + (e.Result.KilledBy == null ? 0 : 1 + System.Text.Encoding.UTF8.GetByteCount(e.Result.KilledBy)); break;
                        case EventKind.Toast: len += 1 + (e.ToastText == null ? 0 : 1 + System.Text.Encoding.UTF8.GetByteCount(e.ToastText)); break;
                        default: throw new EncodeException("event kind");
                    }
                    if (len > Wire.MaxServerFrame) throw new ArgumentException("frame too large");
                }
                var w = new Writer(len);
                w.U8(TEvents); w.U32(Tick); w.U16((ushort)List.Count);
                foreach (var e in List)
                {
                    w.U8((byte)e.Kind);
                    switch (e.Kind)
                    {
                        case EventKind.WindEmblem: break;
                        case EventKind.CampCleared: w.U16(e.Camp); break;
                        case EventKind.PotionNotice: if(e.PotionNotice<1 || e.PotionNotice>10) throw new ArgumentException("potion notice");w.U8(e.PotionNotice);break;
                        case EventKind.ChestRefused: if(e.ChestReason < 1 || e.ChestReason > 9) throw new ArgumentException("chest reason");w.U8(e.ChestReason);break;
                        case EventKind.PickupRefused: if (e.PickupReason < 1 || e.PickupReason > 5) throw new ArgumentException("pickup reason"); w.U8(e.PickupReason); break;
                        case EventKind.CannonNotice:
                            if (e.CannonCode < 1 || e.CannonCode > 4 || (e.CannonCode == 4) != e.CannonSeconds.HasValue || (e.CannonSeconds.HasValue && (e.CannonSeconds.Value < 1 || e.CannonSeconds.Value > 75))) throw new ArgumentException("cannon notice");
                            w.U8(e.CannonCode); if (e.CannonSeconds.HasValue) w.U16(e.CannonSeconds.Value); break;
                        case EventKind.Death: w.U32(e.Id); if (e.By.HasValue) { w.U8(1); w.U32(e.By.Value); } else w.U8(0); break;
                        case EventKind.Bounty: w.U32(e.Id); w.U16(e.Bounty); break;
                        case EventKind.MatchEnded: if (e.Winner.HasValue) { w.U8(1); w.U32(e.Winner.Value); } else w.U8(0); w.U8((byte)(e.Draw ? 1 : 0)); break;
                        case EventKind.Toast:
                            if (e.ToastText == null) throw new ArgumentException("toast text");
                            w.Str(e.ToastText);
                            break;
                        case EventKind.Result:
                        {
                            var r = e.Result;
                            w.U8(r.Outcome); w.U8(r.Place); w.U8(r.Players); w.U16(r.Kills);
                            if (r.KilledBy != null) { w.U8(1); w.Str(r.KilledBy); } else w.U8(0);
                            w.U32(r.GvTotal); w.U32(r.GvWin); w.U32(r.DurationSec); w.U8((byte)(r.WinBonusWithheld ? 1 : 0));
                            break;
                        }
                    }
                }
                return w.Finish();
            }
        }

        public sealed class Fx : ServerMsg
        {
            public uint Tick;
            public List<WireFx> List = new List<WireFx>();

            internal static uint ReadRequiredRef(ref Reader r)
            {
                uint id = r.Varint(); if (id == 0) throw new DecodeException(DecodeError.BadField, "wish actor reference"); return id;
            }
            internal static uint? ReadOptionalRef(ref Reader r)
            {
                if (!r.Flag()) return null;
                uint id = r.Varint(); if (id == 0) throw new DecodeException(DecodeError.BadField, "wish actor reference"); return id;
            }
            internal static void ValidateStarted(WireFx f, bool encoding)
            {
                if (f.EventTime < 0 || double.IsNaN(f.EventTime) || double.IsInfinity(f.EventTime) || f.Duration < 0 || double.IsNaN(f.Duration) || double.IsInfinity(f.Duration) ||
                    !f.Caster.HasValue || f.Caster.Value == 0 || (f.Target.HasValue && f.Target.Value == 0) || f.Ability > WishWire.MaxAbility ||
                    (f.HasAnchor && (!Quant.PosInRange(f.AnchorX) || !Quant.PosInRange(f.AnchorY))))
                {
                    if (encoding) throw new ArgumentException("wish started fields");
                    throw new DecodeException(DecodeError.BadField, "wish started");
                }
            }
            internal static void ValidateLanded(WireFx f, bool encoding)
            {
                bool invalid = f.EventTime < 0 || double.IsNaN(f.EventTime) || double.IsInfinity(f.EventTime) || f.Ability > WishWire.MaxAbility ||
                    (f.Caster.HasValue && f.Caster.Value == 0) || (f.Target.HasValue && f.Target.Value == 0) ||
                    (f.HasAnchor && (!Quant.PosInRange(f.AnchorX) || !Quant.PosInRange(f.AnchorY))) ||
                    (f.HasDestination && (!Quant.PosInRange(f.DestinationX) || !Quant.PosInRange(f.DestinationY))) ||
                    (f.Ability == WishWire.BlazeAbility ? f.HasAnchor != f.HasDestination : f.HasDestination) ||
                    (!f.Caster.HasValue && !f.HasAnchor);
                if (invalid)
                {
                    if (encoding) throw new ArgumentException("wish landed fields");
                    throw new DecodeException(DecodeError.BadField, "wish landed");
                }
            }
            internal static void ValidateEnded(WireFx f, bool encoding)
            {
                if (f.EventTime < 0 || double.IsNaN(f.EventTime) || double.IsInfinity(f.EventTime) || !f.Caster.HasValue || f.Caster.Value == 0 ||
                    f.Ability > WishWire.MaxAbility || f.Reason > 2)
                {
                    if (encoding) throw new ArgumentException("wish ended fields");
                    throw new DecodeException(DecodeError.BadField, "wish ended");
                }
            }
            static int WishLength(WireFx f)
            {
                switch (f.Kind)
                {
                    case FxKind.WishStarted:
                        ValidateStarted(f, true);
                        return 1 + 8 + Writer.VarintLength(f.Caster.Value) + 1 + 8 + (f.Target.HasValue ? 1 + Writer.VarintLength(f.Target.Value) : 1) + (f.HasAnchor ? 5 : 1);
                    case FxKind.WishLanded:
                        ValidateLanded(f, true);
                        return 1 + 8 + 1 + (f.Caster.HasValue ? 1 + Writer.VarintLength(f.Caster.Value) : 1) +
                            (f.Target.HasValue ? 1 + Writer.VarintLength(f.Target.Value) : 1) + (f.HasAnchor ? 5 : 1) + (f.HasDestination ? 5 : 1);
                    case FxKind.WishEnded:
                        ValidateEnded(f, true);
                        return 1 + 8 + Writer.VarintLength(f.Caster.Value) + 1 + 1;
                    default: return 0;
                }
            }

            public override byte[] Encode()
            {
                if (List.Count > ushort.MaxValue) throw new ArgumentException("too many fx");
                // Like rh-protocol: a swing off the position grid is refused, never saturated.
                foreach (var f in List)
                {
                    if (f.Kind < FxKind.Swing || f.Kind > FxKind.Heal) throw new EncodeException("fx kind");
                    if (f.Kind == FxKind.Swing && (!Quant.PosInRange(f.X) || !Quant.PosInRange(f.Y))) throw new ArgumentException("position");
                    if (f.Kind == FxKind.Windup && f.Weapon > Wire.MaxKitCode) throw new ArgumentException("windup weapon");
                    if (f.Kind == FxKind.SigHit && !WireFx.HitKindValid(f.HitKind)) throw new ArgumentException("hit kind");
                }
                int len = 1 + Writer.VarintLength(Tick) + Writer.VarintLength((uint)List.Count);
                foreach (var f in List)
                {
                    switch (f.Kind)
                    {
                        case FxKind.WishStarted: case FxKind.WishLanded: case FxKind.WishEnded: len += WishLength(f); break;
                        case FxKind.Teleport: len += 1; break;
                        case FxKind.Block: len += 2 + Writer.VarintLength(f.Id) + Writer.VarintLength(f.By); break;
                        case FxKind.Stagger: case FxKind.Cancel: len += 1 + Writer.VarintLength(f.Id); break;
                        case FxKind.Swing: len += 1 + Writer.VarintLength(f.By) + 1 + 6; break;
                        case FxKind.Windup: len += 1 + Writer.VarintLength(f.By) + 1 + 2 + 2; break;
                        case FxKind.SigHit: len += 1 + Writer.VarintLength(f.Victim) + Writer.VarintLength(f.By) + 1 + 4; break;
                        case FxKind.Heal: len += 1 + Writer.VarintLength(f.Id) + 4; break;
                        default: len += 1 + Writer.VarintLength(f.Victim) + Writer.VarintLength(f.By) + 5; break;
                    }
                }
                if (len > Wire.MaxServerFrame) throw new ArgumentException("frame too large");
                var w = new Writer(len);
                w.U8(TFx); w.Varint(Tick); w.Varint((uint)List.Count);
                foreach (var f in List)
                {
                    w.U8((byte)f.Kind);
                    if (f.Kind == FxKind.Swing) { w.Varint(f.By); w.U8(f.Weapon); w.U16(Quant.QuantizePos(f.X)); w.U16(Quant.QuantizePos(f.Y)); w.U16(Quant.QuantizeFacing(f.A)); }
                    else if (f.Kind == FxKind.Block) { w.Varint(f.Id); w.Varint(f.By); w.U8(f.Weapon); }
                    else if (f.Kind == FxKind.Stagger) w.Varint(f.Id);
                    else if (f.Kind == FxKind.Teleport) { }
                    else if (f.Kind == FxKind.WishStarted)
                    {
                        w.F64(f.EventTime); w.Varint(f.Caster.Value); w.U8(f.Ability); w.F64(f.Duration); WriteOptRef(w, f.Target);
                        w.U8(f.HasAnchor ? (byte)1 : (byte)0); if (f.HasAnchor) WishWire.WritePoint(w, f.AnchorX, f.AnchorY);
                    }
                    else if (f.Kind == FxKind.WishLanded)
                    {
                        w.F64(f.EventTime); w.U8(f.Ability); WriteOptRef(w, f.Caster); WriteOptRef(w, f.Target);
                        WriteOptPoint(w, f.HasAnchor, f.AnchorX, f.AnchorY); WriteOptPoint(w, f.HasDestination, f.DestinationX, f.DestinationY);
                    }
                    else if (f.Kind == FxKind.WishEnded) { w.F64(f.EventTime); w.Varint(f.Caster.Value); w.U8(f.Ability); w.U8(f.Reason); }
                    else if (f.Kind == FxKind.Windup) { w.Varint(f.By); w.U8(f.Weapon); w.U16(Quant.QuantizeFacing(f.A)); w.U16(f.Ms); }
                    else if (f.Kind == FxKind.Cancel) w.Varint(f.Id);
                    else if (f.Kind == FxKind.SigHit) { w.Varint(f.Victim); w.Varint(f.By); w.U8(f.HitKind); w.F32(f.Amount); }
                    else if (f.Kind == FxKind.Heal) { w.Varint(f.Id); w.F32(f.Amount); }
                    else { w.Varint(f.Victim); w.Varint(f.By); w.F32(f.Amount); w.U8((byte)((f.Passive ? 1 : 0) | (f.Critical ? 2 : 0))); }
                }
                return w.Finish();
            }

            static void WriteOptRef(Writer w, uint? id)
            {
                if (id.HasValue) { if (id.Value == 0) throw new ArgumentException("wish actor reference"); w.U8(1); w.Varint(id.Value); }
                else w.U8(0);
            }
            static void WriteOptPoint(Writer w, bool has, float x, float y)
            {
                w.U8(has ? (byte)1 : (byte)0); if (has) WishWire.WritePoint(w, x, y);
            }
        }

        /// <summary>
        /// Keyed state, the latest value per key (reserved in v4: no key exists yet, so only the empty State decodes). Once keys
        /// exist a Welcome (join and resume) is followed by a full State.
        /// </summary>
        public sealed class State : ServerMsg
        {
            public uint Tick;
            public StateItem[] Items = Array.Empty<StateItem>();
            public override byte[] Encode() { var w = new Writer(); w.U8(TState); w.Varint(Tick); StateItem.WriteAll(w, Items); var frame = w.Finish(); if (frame.Length > Wire.MaxServerFrame) throw new EncodeException("state frame too large"); return frame; }
        }

        public sealed class Pong : ServerMsg
        {
            public uint Nonce;
            public double ClientMs, ServerMs;
            public override byte[] Encode() { var w = new Writer(21); w.U8(TPong); w.U32(Nonce); w.F64(ClientMs); w.F64(ServerMs); return w.Finish(); }
        }
    }
}
