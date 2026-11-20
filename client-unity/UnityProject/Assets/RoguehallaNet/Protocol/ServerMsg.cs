using System;
using System.Collections.Generic;

namespace Roguehalla.Net.Protocol
{
    /// <summary>Server-to-client frames: tag constants, the decoder and the Welcome and Reject frames; the other frames are in the partial files beside it.</summary>
    public abstract partial class ServerMsg
    {
        const byte TWelcome = 0x81, TReject = 0x82, TSnapshot = 0x83, TEvents = 0x84, TFx = 0x85, TPong = 0x86, TSnapshotDelta = 0x87, TState = 0x88;

        public abstract byte[] Encode();

        public static ServerMsg Decode(byte[] frame) { return Decode(frame, frame.Length, null); }

        /// <summary>Decodes the first <paramref name="length"/> bytes (transports may hand over a reused buffer).</summary>
        public static ServerMsg Decode(byte[] frame, int length) { return Decode(frame, length, null); }

        /// <summary>
        /// As <see cref="Decode(byte[], int)"/>, but a Snapshot frame is decoded INTO <paramref name="reuse"/> (when given) and
        /// returns it: its Me object and its actor array are reused when they fit, so a steady stream of snapshots makes no
        /// garbage. Pass only a snapshot nothing else still reads (SnapshotBuffer.TakeSpare hands out such one). Other frames
        /// ignore <paramref name="reuse"/>. If decoding throws, <paramref name="reuse"/> is left partly overwritten.
        /// </summary>
        /// <remarks>The rate-less overloads read snapshot times at 20 Hz (<see cref="Wire.TickMs"/>).</remarks>
        public static ServerMsg Decode(byte[] frame, int length, Snapshot reuse) { return DecodeAt(frame, length, reuse, Wire.TickMs); }

        /// <summary>
        /// P-TICK-3 (ADR 0029): as <see cref="Decode(byte[], int, Snapshot)"/> on a connection whose Welcome said
        /// <paramref name="tickRate"/>: a Snapshot or SnapshotDelta without a source-time override stands for
        /// Tick * (1000 / tickRate) ms, and a delta's base age is bounded by <see cref="Wire.MaxDeltaAgeAt"/> at that rate. Only
        /// 20 and 40 are rates; any other is refused with a <see cref="DecodeException"/>.
        /// </summary>
        public static ServerMsg Decode(byte[] frame, int length, Snapshot reuse, byte tickRate)
        {
            return DecodeAt(frame, length, reuse, Wire.TickMsOf(tickRate));
        }

        /// <summary>The decoder, with snapshot times at <paramref name="tickMs"/> per tick (50 or 25).</summary>
        internal static ServerMsg DecodeAt(byte[] frame, int length, Snapshot reuse, int tickMs)
        {
            if (length > Wire.MaxServerFrame) throw new DecodeException(DecodeError.TooLarge, length.ToString());
            var r = new Reader(frame, length);
            ServerMsg msg;
            switch (r.U8())
            {
                case TWelcome:
                {
                    var welcome = new Welcome
                    {
                        Protocol = r.U16(), ContentHash = r.U64(), MatchId = r.Array16(), PlayerId = r.U32(), Session = r.Array16(),
                        TickRate = r.U8(),
                    };
                    // Protocol 21: only 20 and 40 Hz are rates (refused before the rest is read, as rh-protocol does).
                    Wire.TickMsOf(welcome.TickRate);
                    welcome.Tick = r.U32(); welcome.ServerMs = r.F64(); welcome.Archetypes = WireArchetype.ReadTable(ref r);
                    welcome.Vision = WireVision.Read(ref r); welcome.Weapons = WireWeapon.ReadTable(ref r);
                    welcome.Hud = WireHudContent.Read(ref r); welcome.Map = MapBlob.Read(ref r);
                    if (!welcome.Vision.Fits(welcome.Map.W, welcome.Map.H)) throw new DecodeException(DecodeError.BadField, "vision");
                    msg = welcome;
                    break;
                }
                case TReject:
                {
                    ushort code = r.U16();
                    if (code < 1) throw new DecodeException(DecodeError.BadTag, "reject code " + code);
                    msg = new Reject { Code = (RejectCode)code, Reason = r.Str() };
                    break;
                }
                case TSnapshot:
                    msg = Snapshot.Read(ref r, reuse ?? new Snapshot(), tickMs);
                    break;
                case TSnapshotDelta:
                    msg = SnapshotDelta.Read(ref r, new SnapshotDelta(), tickMs);
                    break;
                case TEvents:
                {
                    var e = new Events { Tick = r.U32() };
                    int n = r.U16();
                    for (int i = 0; i < n; i++)
                    {
                        byte tag = r.U8();
                        var ev = new WireEvent { Kind = (EventKind)tag };
                        switch (tag)
                        {
                            case 8: break;
                            case 5: ev.PickupReason = r.U8(); if (ev.PickupReason < 1 || ev.PickupReason > 5) throw new DecodeException(DecodeError.BadField, "pickup reason"); break;
                            case 7: ev.PotionNotice=r.U8(); if(ev.PotionNotice<1 || ev.PotionNotice>10) throw new DecodeException(DecodeError.BadField,"potion notice"); break;
                            case 6: ev.ChestReason = r.U8(); if(ev.ChestReason < 1 || ev.ChestReason > 9) throw new DecodeException(DecodeError.BadField,"chest reason");break;
                            case 9:
                                ev.CannonCode = r.U8();
                                if (ev.CannonCode < 1 || ev.CannonCode > 4) throw new DecodeException(DecodeError.BadField, "cannon notice");
                                if (ev.CannonCode == 4) { ev.CannonSeconds = r.U16(); if (ev.CannonSeconds < 1 || ev.CannonSeconds > 75) throw new DecodeException(DecodeError.BadField, "cannon notice"); }
                                break;
                            case 1: ev.Id = r.U32(); if (r.Flag()) ev.By = r.U32(); break;
                            case 2: ev.Id = r.U32(); ev.Bounty = r.U16(); break;
                            case 3: if (r.Flag()) ev.Winner = r.U32(); ev.Draw = r.U8() != 0; break;
                            case 10: ev.ToastText = r.Str(); break;
                            case 11: ev.Camp = r.U16(); break;
                            case 4:
                            {
                                var res = new WireResult { Outcome = r.U8(), Place = r.U8(), Players = r.U8(), Kills = r.U16() };
                                if (r.Flag()) res.KilledBy = r.Str();
                                res.GvTotal = r.U32(); res.GvWin = r.U32(); res.DurationSec = r.U32();
                                res.WinBonusWithheld = r.U8() != 0;
                                ev.Result = res;
                                break;
                            }
                            default: throw new DecodeException(DecodeError.BadTag, tag.ToString());
                        }
                        e.List.Add(ev);
                    }
                    msg = e;
                    break;
                }
                case TFx:
                {
                    var f = new Fx { Tick = r.Varint() };
                    uint n = r.Varint();
                    if (n > ushort.MaxValue) throw new DecodeException(DecodeError.TooMany, n.ToString());
                    for (uint i = 0; i < n; i++)
                    {
                        byte tag = r.U8();
                        switch (tag)
                        {
                            case 1:
                            {
                                var sw = new WireFx { Kind = FxKind.Swing, By = r.Varint(), Weapon = r.U8() };
                                sw.X = Quant.DequantizePos(r.U16()); sw.Y = Quant.DequantizePos(r.U16()); sw.A = Quant.DequantizeFacing(r.U16());
                                f.List.Add(sw);
                                break;
                            }
                            case 3: f.List.Add(new WireFx { Kind = FxKind.Block, Id = r.Varint(), By = r.Varint(), Weapon = r.U8() }); break;
                            case 4: f.List.Add(new WireFx { Kind = FxKind.Stagger, Id = r.Varint() }); break;
                            case 5: f.List.Add(new WireFx { Kind = FxKind.Teleport }); break;
                            case 2:
                            {
                                uint victim = r.Varint(), by = r.Varint(); float amount = r.F32();
                                byte flags = r.Code(3, "hit flags");
                                f.List.Add(new WireFx { Kind = FxKind.Hit, Victim = victim, By = by, Amount = amount, Passive = (flags & 1) != 0, Critical = (flags & 2) != 0 });
                                break;
                            }
                            case 9:
                            {
                                var wu = new WireFx { Kind = FxKind.Windup, By = r.Varint(), Weapon = r.Code(Wire.MaxKitCode, "windup weapon") };
                                wu.A = Quant.DequantizeFacing(r.U16()); wu.Ms = r.U16();
                                f.List.Add(wu);
                                break;
                            }
                            case 10: f.List.Add(new WireFx { Kind = FxKind.Cancel, Id = r.Varint() }); break;
                            case 11:
                            {
                                uint victim = r.Varint(), by = r.Varint();
                                byte kind = r.U8();
                                if (!WireFx.HitKindValid(kind)) throw new DecodeException(DecodeError.BadField, "hit kind");
                                f.List.Add(new WireFx { Kind = FxKind.SigHit, Victim = victim, By = by, HitKind = kind, Amount = r.F32() });
                                break;
                            }
                            case 12: { uint id = r.Varint(); f.List.Add(new WireFx { Kind = FxKind.Heal, Id = id, Amount = r.F32() }); break; }
                            case 6:
                            {
                                var fx = new WireFx { Kind = FxKind.WishStarted, EventTime = r.F64(), Caster = Fx.ReadRequiredRef(ref r), Ability = r.Code(WishWire.MaxAbility, "wish ability"), Duration = r.F64() };
                                fx.Target = Fx.ReadOptionalRef(ref r);
                                fx.HasAnchor = r.Flag(); if (fx.HasAnchor) WishWire.ReadPoint(ref r, out fx.AnchorX, out fx.AnchorY);
                                Fx.ValidateStarted(fx, false); f.List.Add(fx); break;
                            }
                            case 7:
                            {
                                var fx = new WireFx { Kind = FxKind.WishLanded, EventTime = r.F64(), Ability = r.Code(WishWire.MaxAbility, "wish ability") };
                                fx.Caster = Fx.ReadOptionalRef(ref r); fx.Target = Fx.ReadOptionalRef(ref r);
                                fx.HasAnchor = r.Flag(); if (fx.HasAnchor) WishWire.ReadPoint(ref r, out fx.AnchorX, out fx.AnchorY);
                                fx.HasDestination = r.Flag(); if (fx.HasDestination) WishWire.ReadPoint(ref r, out fx.DestinationX, out fx.DestinationY);
                                Fx.ValidateLanded(fx, false); f.List.Add(fx); break;
                            }
                            case 8:
                            {
                                var fx = new WireFx { Kind = FxKind.WishEnded, EventTime = r.F64(), Caster = Fx.ReadRequiredRef(ref r), Ability = r.Code(WishWire.MaxAbility, "wish ability"), Reason = r.U8() };
                                Fx.ValidateEnded(fx, false); f.List.Add(fx); break;
                            }
                            default: throw new DecodeException(DecodeError.BadTag, tag.ToString());
                        }
                    }
                    msg = f;
                    break;
                }
                case TPong: msg = new Pong { Nonce = r.U32(), ClientMs = r.F64(), ServerMs = r.F64() }; break;
                case TState:
                {
                    var st = new State { Tick = r.Varint() };
                    st.Items = StateItem.ReadAll(ref r);
                    msg = st;
                    break;
                }
                default: throw new DecodeException(DecodeError.BadTag);
            }
            r.End();
            return msg;
        }

        public sealed class Welcome : ServerMsg
        {
            public ushort Protocol;
            public ulong ContentHash;
            public byte[] MatchId, Session;
            public uint PlayerId;
            public byte TickRate;
            public uint Tick;
            public double ServerMs;
            /// <summary>The hitbox archetype table (protocol 16, ADR0024): between the header and the map. Rows by ascending id.</summary>
            public WireArchetype[] Archetypes = Array.Empty<WireArchetype>();
            /// <summary>The vision rule's parameters (protocol 16): after the archetype table, before the map.</summary>
            public WireVision Vision = new WireVision();
            /// <summary>The weapon table (protocol 16): each weapon's hit shape from the server's Rust content, after the vision
            /// block, before the map. Rows by ascending id.</summary>
            public WireWeapon[] Weapons = Array.Empty<WireWeapon>();
            /// <summary>The HUD content block (protocol 17, P-WIRE17-HUD-2b): weapon scaling rows and public HUD constants,
            /// after the weapon table, before the map.</summary>
            public WireHudContent Hud = new WireHudContent();
            public MapBlob Map;

            public override byte[] Encode()
            {
                var table = Archetypes ?? Array.Empty<WireArchetype>();
                var vision = Vision ?? new WireVision();
                var weapons = Weapons ?? Array.Empty<WireWeapon>();
                var hud = Hud ?? new WireHudContent();
                var w = new Writer(60 + WireArchetype.TableLength(table) + vision.Length() + WireWeapon.TableLength(weapons) + hud.Length() + Map.EncodedLength);
                w.U8(TWelcome); w.U16(Protocol); w.U64(ContentHash); w.Bytes(MatchId); w.U32(PlayerId); w.Bytes(Session);
                w.U8(TickRate); w.U32(Tick); w.F64(ServerMs);
                WireArchetype.WriteTable(w, table);
                vision.Write(w);
                WireWeapon.WriteTable(w, weapons);
                hud.Write(w);
                Map.Write(w);
                return w.Finish();
            }
        }

        public sealed class Reject : ServerMsg
        {
            public RejectCode Code;
            public string Reason = "";
            public override byte[] Encode() { var w = new Writer(); w.U8(TReject); w.U16((ushort)Code); w.Str(Reason); return w.Finish(); }
        }
    }
}
