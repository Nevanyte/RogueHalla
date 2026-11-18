using System;
using System.Collections.Generic;

namespace Roguehalla.Net.Protocol
{
    /// <summary>Wire format: append only. A decoder accepts any code above 0: a value this enum does not name decodes as itself
    /// (<c>(RejectCode)n</c>, so <c>Enum.IsDefined</c> is false) and ends the session as Rejected with the reason text.</summary>
    public enum RejectCode : ushort
    {
        ProtocolMismatch = 1, ContentMismatch = 2, BadCredentials = 3, ProfileInUse = 4, MatchFull = 5, RateLimited = 6,
        Malformed = 7, TooSlow = 8, ServerDraining = 9, NoSuchSession = 10, Kicked = 11,
    }

    public enum CommandKind : byte { Move = 1, Stop = 2, Aim = 3, Attack = 4, Release = 5, Swap = 6, Pickup = 7, Choose = 8, UseConsumable = 9, Target = 10, AttackMove = 11, HuntTarget = 12, ConsumeCorpse = 13, Plan = 14, Train = 15, Cast = 16, QuestAccept = 17, Trade = 18 }

    /// <summary>A client command. Only the fields of its Kind are meaningful.</summary>
    public sealed class WireCommand
    {
        public CommandKind Kind;
        public float X, Y;
        /// <summary>Aim angle (Aim, Attack, Release).</summary>
        public float A;
        /// <summary>Order sequence echoed in Snapshot.me.move_seq (Move, AttackMove, Stop).</summary>
        public byte? Seq;
        public byte? Slot;
        public PickupHandle PickupRef;
        public RewardChestHandle RewardRef;
        public QuestHandle QuestRef;
        public sbyte Pick;
        public TradeKind TradeKind;
        public uint ActorRef;
        /// <summary>Client render time in server ms (Attack, Release); lag compensation, clamped by the server.</summary>
        public double? T;
        /// <summary>Planning option indexes (Plan): 0 or 1, indexes into this player's own private plan.</summary>
        public byte? Spawn, Boon;
        /// <summary>Plan ready latch (true freezes the picks).</summary>
        public bool? Ready;
        /// <summary>The practice console command byte (Train): 1 card, 2 stat, 3 camp, 4 chest, 5 clear.</summary>
        public byte TrainCommand;
        /// <summary>A card or mob-type catalog position (Train).</summary>
        public uint TrainId;
        /// <summary>The requested copy/stack count (Train); the server clamps it.</summary>
        public ulong TrainCount;
        /// <summary>The chest's chosen card positions (Train, chest only; at most three).</summary>
        public byte[] TrainOptions = Array.Empty<byte>();

        /// <summary>A user-initiated cast request. Slot is a client command slot; server-owned ability and outcome are never sent.</summary>
        public static WireCommand Cast(byte slot, float x, float y, uint? actor = null, double? t = null, byte? seq = null)
        {
            if (slot > 2) throw new ArgumentOutOfRangeException(nameof(slot));
            if (actor == 0) throw new ArgumentOutOfRangeException(nameof(actor));
            return new WireCommand { Kind = CommandKind.Cast, Slot = slot, X = x, Y = y, ActorRef = actor ?? 0, T = t, Seq = seq };
        }

        public static WireCommand Train(byte command, uint id, ulong count, byte[] options = null)
        {
            return new WireCommand { Kind = CommandKind.Train, TrainCommand = command, TrainId = id, TrainCount = count, TrainOptions = options ?? Array.Empty<byte>() };
        }

        public static WireCommand Swap(byte? slot = null) { return new WireCommand { Kind = CommandKind.Swap, Slot = slot }; }
        public static WireCommand Pickup(PickupHandle handle) { return new WireCommand { Kind = CommandKind.Pickup, PickupRef = handle }; }
        public static WireCommand Choose(RewardChestHandle handle, sbyte pick) { return new WireCommand { Kind = CommandKind.Choose, RewardRef = handle, Pick = pick }; }
        public static WireCommand QuestAccept(QuestHandle handle) { return new WireCommand { Kind = CommandKind.QuestAccept, QuestRef = handle }; }
        public static WireCommand Trade(TradeKind kind, byte pick)
        {
            if ((byte)kind > (byte)TradeKind.Stats) throw new ArgumentOutOfRangeException(nameof(kind));
            if (pick > 2) throw new ArgumentOutOfRangeException(nameof(pick));
            return new WireCommand { Kind = CommandKind.Trade, TradeKind = kind, Pick = (sbyte)pick };
        }
        public static WireCommand Move(float x, float y, byte? seq) { return new WireCommand { Kind = CommandKind.Move, X = x, Y = y, Seq = seq }; }
        public static WireCommand UseConsumable() { return new WireCommand { Kind = CommandKind.UseConsumable }; }
        public static WireCommand Target(uint actorRef) { if (actorRef == 0) throw new ArgumentException("actor reference"); return new WireCommand { Kind = CommandKind.Target, ActorRef = actorRef }; }
        public static WireCommand HuntTarget(uint actorRef) { if (actorRef == 0) throw new ArgumentException("hunt target"); return new WireCommand { Kind = CommandKind.HuntTarget, ActorRef = actorRef }; }
        public static WireCommand ConsumeCorpse(PickupHandle handle) { if (!handle.IsValid) throw new ArgumentException("corpse handle"); return new WireCommand { Kind = CommandKind.ConsumeCorpse, PickupRef = handle }; }
        public static WireCommand AttackMove(float x, float y, byte? seq) { return new WireCommand { Kind = CommandKind.AttackMove, X = x, Y = y, Seq = seq }; }
        public static WireCommand Stop(byte? seq) { return new WireCommand { Kind = CommandKind.Stop, Seq = seq }; }
        public static WireCommand Aim(float a) { return new WireCommand { Kind = CommandKind.Aim, A = a }; }
        public static WireCommand Attack(float a, double? t) { return new WireCommand { Kind = CommandKind.Attack, A = a, T = t }; }
        public static WireCommand Release(float a, double? t) { return new WireCommand { Kind = CommandKind.Release, A = a, T = t }; }
        public static WireCommand Plan(byte? spawn = null, byte? boon = null, bool? ready = null) { return new WireCommand { Kind = CommandKind.Plan, Spawn = spawn, Boon = boon, Ready = ready }; }
    }

    public abstract class ClientMsg
    {
        const byte THello = 0x01, TInput = 0x02, TPing = 0x03, TLeave = 0x04, TResync = 0x05;

        public abstract byte[] Encode();

        /// <summary>Decodes an untrusted client frame (size limit, finite floats, list bounds, no trailing bytes).</summary>
        public static ClientMsg Decode(byte[] frame)
        {
            if (frame.Length > Wire.MaxClientFrame) throw new DecodeException(DecodeError.TooLarge, frame.Length.ToString());
            var r = new Reader(frame) { FiniteOnly = true };
            ClientMsg msg;
            switch (r.U8())
            {
                case THello:
                {
                    var h = new Hello { Protocol = r.U16(), ContentHash = r.U64(), Build = r.Str(), Profile = r.Str(), Ticket = r.Blob(Wire.MaxTicket) };
                    if (r.Flag()) h.Faction = r.U8();
                    if (r.Flag()) h.Resume = r.Array16();
                    msg = h;
                    break;
                }
                case TInput:
                {
                    var inp = new Input { Seq = r.U32() };
                    int n = r.U16();
                    if (n > Wire.MaxCommandsPerInput) throw new DecodeException(DecodeError.TooMany, n.ToString());
                    for (int i = 0; i < n; i++)
                    {
                        byte tag = r.U8();
                        switch (tag)
                        {
                            case 6: { byte? slot = r.Flag() ? r.U8() : (byte?)null; if (slot > 1) throw new DecodeException(DecodeError.BadField, "slot"); inp.Commands.Add(WireCommand.Swap(slot)); break; }
                            case 7: { uint id = r.U32(); if (id == 0) throw new DecodeException(DecodeError.BadField, "container"); inp.Commands.Add(WireCommand.Pickup(new PickupHandle(id))); break; }
                            case 8: { uint id=r.U32(); sbyte pick=unchecked((sbyte)r.U8()); if(id==0 || pick < -1 || pick > 2) throw new DecodeException(DecodeError.BadField,"reward choice"); inp.Commands.Add(WireCommand.Choose(new RewardChestHandle(id),pick)); break; }
                            case 9: inp.Commands.Add(WireCommand.UseConsumable()); break;
                            case 10: { uint id=r.U32(); if(id==0) throw new DecodeException(DecodeError.BadField,"actor reference"); inp.Commands.Add(WireCommand.Target(id)); break; }
                            case 11: { float x=r.F32(),y=r.F32(); inp.Commands.Add(WireCommand.AttackMove(x,y,r.Flag()?r.U8():(byte?)null)); break; }
                            case 12: { uint id=r.U32(); if(id==0) throw new DecodeException(DecodeError.BadField,"hunt target"); inp.Commands.Add(WireCommand.HuntTarget(id)); break; }
                            case 13: { uint id=r.U32(); if(id==0) throw new DecodeException(DecodeError.BadField,"corpse handle"); inp.Commands.Add(WireCommand.ConsumeCorpse(new PickupHandle(id))); break; }
                            case 14:
                            {
                                byte flags = r.U8();
                                if ((flags & 0xf8) != 0) throw new DecodeException(DecodeError.BadField, "plan flags");
                                byte? spawn = null, boon = null; bool? ready = null;
                                if ((flags & 1) != 0) { spawn = r.U8(); if (spawn > 1) throw new DecodeException(DecodeError.BadField, "plan spawn"); }
                                if ((flags & 2) != 0) { boon = r.U8(); if (boon > 1) throw new DecodeException(DecodeError.BadField, "plan boon"); }
                                if ((flags & 4) != 0) ready = r.U8() != 0;
                                inp.Commands.Add(WireCommand.Plan(spawn, boon, ready));
                                break;
                            }
                            case 15:
                            {
                                byte cmd = r.U8();
                                if (cmd < 1 || cmd > 5) throw new DecodeException(DecodeError.BadField, "train command");
                                uint id = r.Varint();
                                ulong count = r.U64();
                                int on = r.U8();
                                if (on > 3) throw new DecodeException(DecodeError.TooMany, on.ToString());
                                var opts = on == 0 ? Array.Empty<byte>() : new byte[on];
                                for (int k = 0; k < on; k++) opts[k] = r.U8();
                                inp.Commands.Add(WireCommand.Train(cmd, id, count, opts));
                                break;
                            }
                            case 16:
                            {
                                byte slot = r.U8(); if (slot > 2) throw new DecodeException(DecodeError.BadField, "cast slot");
                                float x = r.F32(), y = r.F32();
                                uint? actor = null; if (r.Flag()) { uint id = r.U32(); if (id == 0) throw new DecodeException(DecodeError.BadField, "cast actor reference"); actor = id; }
                                double? t = r.Flag() ? r.F64() : (double?)null;
                                byte? seq = r.Flag() ? r.U8() : (byte?)null;
                                inp.Commands.Add(WireCommand.Cast(slot, x, y, actor, t, seq));
                                break;
                            }
                            case 17: inp.Commands.Add(WireCommand.QuestAccept(StateItem.ReadQuestHandle(ref r))); break;
                            case 18:
                            {
                                byte kind = r.U8();
                                if (kind > (byte)TradeKind.Stats) throw new DecodeException(DecodeError.BadField, "trade kind");
                                byte pick = r.U8();
                                if (pick > 2) throw new DecodeException(DecodeError.BadField, "trade pick");
                                inp.Commands.Add(WireCommand.Trade((TradeKind)kind, pick));
                                break;
                            }
                            case 1: { float x = r.F32(), y = r.F32(); inp.Commands.Add(WireCommand.Move(x, y, r.Flag() ? r.U8() : (byte?)null)); break; }
                            case 2: inp.Commands.Add(WireCommand.Stop(r.Flag() ? r.U8() : (byte?)null)); break;
                            case 3: inp.Commands.Add(WireCommand.Aim(r.F32())); break;
                            case 4: { float a = r.F32(); inp.Commands.Add(WireCommand.Attack(a, r.Flag() ? r.F64() : (double?)null)); break; }
                            case 5: { float a = r.F32(); inp.Commands.Add(WireCommand.Release(a, r.Flag() ? r.F64() : (double?)null)); break; }
                            default: throw new DecodeException(DecodeError.BadTag, tag.ToString());
                        }
                    }
                    msg = inp;
                    break;
                }
                case TPing: msg = new Ping { Nonce = r.U32(), ClientMs = r.F64() }; break;
                case TLeave: msg = new Leave(); break;
                case TResync: msg = new Resync(); break;
                default: throw new DecodeException(DecodeError.BadTag);
            }
            r.End();
            return msg;
        }

        /// <summary>First frame of every connection. Resume is the 16-byte session token of a dropped connection.</summary>
        public sealed class Hello : ClientMsg
        {
            public ushort Protocol = Wire.ProtocolVersion;
            public ulong ContentHash;
            public string Build = "", Profile = "";
            /// <summary>Opaque admission credential (at most <see cref="Wire.MaxTicket"/> bytes); P-HOST-1 defines its contents.</summary>
            public byte[] Ticket = new byte[0];
            public byte? Faction;
            public byte[] Resume;

            public override byte[] Encode()
            {
                var w = new Writer();
                if (Ticket != null && Ticket.Length > Wire.MaxTicket) throw new EncodeException("ticket of " + Ticket.Length + " bytes does not fit the wire (max " + Wire.MaxTicket + ")");
                w.U8(THello); w.U16(Protocol); w.U64(ContentHash); w.Str(Build); w.Str(Profile); w.Blob(Ticket);
                if (Faction.HasValue) { w.U8(1); w.U8(Faction.Value); } else w.U8(0);
                if (Resume != null)
                {
                    if (Resume.Length != 16) throw new ArgumentException("resume token must be 16 bytes");
                    w.U8(1); w.Bytes(Resume);
                }
                else w.U8(0);
                return w.Finish();
            }
        }

        public sealed class Input : ClientMsg
        {
            public uint Seq;
            public List<WireCommand> Commands = new List<WireCommand>();

            public override byte[] Encode()
            {
                if (Commands.Count > Wire.MaxCommandsPerInput) throw new ArgumentException("too many commands");
                int len = 7;
                foreach (var c in Commands) len += CommandLength(c);
                var w = new Writer(len);
                w.U8(TInput); w.U32(Seq); w.U16((ushort)Commands.Count);
                foreach (var c in Commands)
                {
                    w.U8((byte)c.Kind);
                    switch (c.Kind)
                    {
                        case CommandKind.Swap: if (c.Slot > 1) throw new ArgumentException("slot"); Opt(w, c.Slot); break;
                        case CommandKind.Pickup: if (!c.PickupRef.IsValid) throw new ArgumentException("pickup handle"); w.U32(c.PickupRef.Value); break;
                        case CommandKind.Choose: if(!c.RewardRef.IsValid || c.Pick < -1 || c.Pick > 2) throw new ArgumentException("reward choice"); w.U32(c.RewardRef.Value);w.U8(unchecked((byte)c.Pick));break;
                        case CommandKind.UseConsumable: break;
                        case CommandKind.Plan:
                            if (c.Spawn > 1 || c.Boon > 1) throw new ArgumentException("plan option");
                            w.U8((byte)((c.Spawn.HasValue ? 1 : 0) | (c.Boon.HasValue ? 2 : 0) | (c.Ready.HasValue ? 4 : 0)));
                            if (c.Spawn.HasValue) w.U8(c.Spawn.Value);
                            if (c.Boon.HasValue) w.U8(c.Boon.Value);
                            if (c.Ready.HasValue) w.U8((byte)(c.Ready.Value ? 1 : 0));
                            break;
                        case CommandKind.Target: if(c.ActorRef==0) throw new ArgumentException("actor reference"); w.U32(c.ActorRef); break;
                        case CommandKind.HuntTarget: if(c.ActorRef==0) throw new ArgumentException("hunt target"); w.U32(c.ActorRef); break;
                        case CommandKind.ConsumeCorpse: if(!c.PickupRef.IsValid) throw new ArgumentException("corpse handle"); w.U32(c.PickupRef.Value); break;
                        case CommandKind.AttackMove:
                        case CommandKind.Move: w.F32(c.X); w.F32(c.Y); Opt(w, c.Seq); break;
                        case CommandKind.Stop: Opt(w, c.Seq); break;
                        case CommandKind.Aim: w.F32(c.A); break;
                        case CommandKind.Attack:
                        case CommandKind.Release:
                            w.F32(c.A);
                            if (c.T.HasValue) { w.U8(1); w.F64(c.T.Value); } else w.U8(0);
                            break;
                        case CommandKind.Train:
                            if (c.TrainCommand < 1 || c.TrainCommand > 5) throw new ArgumentException("train command");
                            if (c.TrainOptions.Length > 3) throw new ArgumentException("train options");
                            w.U8(c.TrainCommand); w.Varint(c.TrainId); w.U64(c.TrainCount); w.U8((byte)c.TrainOptions.Length);
                            foreach (byte o in c.TrainOptions) w.U8(o);
                            break;
                        case CommandKind.Cast:
                            if (!c.Slot.HasValue || c.Slot.Value > 2) throw new ArgumentException("cast slot");
                            if (float.IsNaN(c.X) || float.IsInfinity(c.X) || float.IsNaN(c.Y) || float.IsInfinity(c.Y) || (c.T.HasValue && (double.IsNaN(c.T.Value) || double.IsInfinity(c.T.Value)))) throw new ArgumentException("cast values must be finite");
                            w.U8(c.Slot ?? 0); w.F32(c.X); w.F32(c.Y);
                            w.U8(c.ActorRef != 0 ? (byte)1 : (byte)0); if (c.ActorRef != 0) w.U32(c.ActorRef);
                            if (c.T.HasValue) { w.U8(1); w.F64(c.T.Value); } else w.U8(0);
                            Opt(w, c.Seq);
                            break;
                        case CommandKind.QuestAccept:
                            if (!c.QuestRef.IsValid) throw new ArgumentException("quest handle");
                            w.Varint(c.QuestRef.Value);
                            break;
                        case CommandKind.Trade:
                            if ((byte)c.TradeKind > (byte)TradeKind.Stats || c.Pick < 0 || c.Pick > 2) throw new ArgumentException("trade choice");
                            w.U8((byte)c.TradeKind); w.U8((byte)c.Pick);
                            break;
                    }
                }
                return w.Finish();
            }

            static void Opt(Writer w, byte? v) { if (v.HasValue) { w.U8(1); w.U8(v.Value); } else w.U8(0); }

            static int CommandLength(WireCommand c)
            {
                switch (c.Kind)
                {
                    case CommandKind.Swap: return c.Slot.HasValue ? 3 : 2;
                    case CommandKind.Pickup: return 5;
                    case CommandKind.Choose: return 6;
                    case CommandKind.Plan: return 2 + (c.Spawn.HasValue ? 1 : 0) + (c.Boon.HasValue ? 1 : 0) + (c.Ready.HasValue ? 1 : 0);
                    case CommandKind.UseConsumable: return 1;
                    case CommandKind.Target: return 5;
                    case CommandKind.HuntTarget: return 5;
                    case CommandKind.ConsumeCorpse: return 5;
                    case CommandKind.AttackMove:
                    case CommandKind.Move: return 9 + (c.Seq.HasValue ? 2 : 1);
                    case CommandKind.Stop: return 1 + (c.Seq.HasValue ? 2 : 1);
                    case CommandKind.Aim: return 5;
                    case CommandKind.Attack:
                    case CommandKind.Release: return 5 + (c.T.HasValue ? 9 : 1);
                    case CommandKind.Train: return 3 + Writer.VarintLength(c.TrainId) + 8 + c.TrainOptions.Length;
                    case CommandKind.Cast: return 13 + (c.ActorRef != 0 ? 4 : 0) + (c.T.HasValue ? 8 : 0) + (c.Seq.HasValue ? 1 : 0);
                    case CommandKind.QuestAccept: return 1 + Writer.VarintLength(c.QuestRef.Value);
                    case CommandKind.Trade: return 3;
                    default: throw new ArgumentException("command kind");
                }
            }
        }

        public sealed class Ping : ClientMsg
        {
            public uint Nonce;
            public double ClientMs;
            public override byte[] Encode() { var w = new Writer(13); w.U8(TPing); w.U32(Nonce); w.F64(ClientMs); return w.Finish(); }
        }

        public sealed class Leave : ClientMsg
        {
            public override byte[] Encode() { return new[] { TLeave }; }
        }

        /// <summary>"Send me a full snapshot": the client refused a delta it could not apply (v3). The server honours one per second.</summary>
        public sealed class Resync : ClientMsg
        {
            public override byte[] Encode() { return new[] { TResync }; }
        }
    }
}
