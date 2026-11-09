using System;
using System.IO;
using System.Buffers.Binary;
using System.Text;

namespace PlayroomsCoop
{
    internal enum Msg : byte
    {
        Beacon = 1,
        Hello = 2,
        Welcome = 3,
        State = 4,
        Bye = 5,
        BallsTaken = 6,
        PitAmount = 7,
        Snapshot = 8,
        Enemy = 9,
        EnemyCaught = 10,
        DayTime = 11,
        SkillUp = 12,
        ShopBuy = 13,
        ObjState = 14,
        ObjClaim = 15,
        ObjRelease = 16,
        ObjFlag = 17,
        ObjDelta = 18,
        WalletDelta = 19,
        UpgradeSnapshot = 20,
        DoorState = 21,
        DayState = 22,
        BallsMoved = 23,
        StatSet = 24,
        BallsLive = 25,
        Whiteboard = 26,
        PropsLive = 27,
        PropsRest = 28,
        EnemyNotice = 29,
        Invite = 30,
        InviteDecline = 31,
        Ready = 32,
        Kick = 33,
        ModHello = 34,
        HostStarting = 35,
        SaveOffer = 36,
        SaveChunk = 37,
        WorldReady = 38,
        BroomSweep = 39,
        BroomCaps = 40,
        SusanneClaim = 41,
        SusanneRelease = 42,
        SusanneState = 43,
        VentFeed = 44,
        PlayerDied = 45,
        PlayerRespawned = 46,
        SafeState = 47,
        WalletState = 48,
        SaveResend = 49,
        RobotSay = 50,
        ObjOwner = 51,
        Roster = 52,
        LobbyState = 53,
        ObjFinal = 54,
        ModCounterTake = 55,
        ModCounterResult = 56,
        DevicesHome = 57,
    }

    internal struct PoseState
    {
        public uint Seq;
        public float X, Y, Z;
        public float Yaw, Pitch;
        public float EyeHeight;
        public bool InWorld;
        public bool Sweeping;
    }

    internal sealed class Writer
    {
        public byte[] Buf = new byte[256];
        public int Len;

        public Writer Begin(Msg type)
        {
            Len = 0;
            U32(Protocol.Magic); U8((byte)type); U8(Protocol.Version);
            return this;
        }

        private void Need(int n)
        {
            if (Len + n <= Buf.Length) return;
            int size = Buf.Length;
            while (size < Len + n) size *= 2;
            Array.Resize(ref Buf, size);
        }

        public void U8(byte v) { Need(1); Buf[Len++] = v; }
        public void U16(ushort v) { Need(2); BinaryPrimitives.WriteUInt16LittleEndian(Buf.AsSpan(Len), v); Len += 2; }
        public void U32(uint v) { Need(4); BinaryPrimitives.WriteUInt32LittleEndian(Buf.AsSpan(Len), v); Len += 4; }
        public void I32(int v) { Need(4); BinaryPrimitives.WriteInt32LittleEndian(Buf.AsSpan(Len), v); Len += 4; }
        public void F32(float v) => I32(BitConverter.SingleToInt32Bits(v));
        public void Str(string s)
        {
            s ??= "";
            int n = Encoding.UTF8.GetByteCount(s);
            if (n > 0xFFFF) throw new ArgumentException("string too long");
            U16((ushort)n); Need(n);
            Encoding.UTF8.GetBytes(s, 0, s.Length, Buf, Len); Len += n;
        }
        public void Bytes(byte[] b, int count) { Need(count); Buffer.BlockCopy(b, 0, Buf, Len, count); Len += count; }

        public void U64(ulong v) { U32((uint)v); U32((uint)(v >> 32)); }

        public bool SendTo(ulong peer, int flags) => SteamNet.Send(peer, Buf, Len, flags);
        public void SendAll(int flags) => Net.SendAll(this, flags);
        public bool SendHost(int flags) => Net.SendHost(this, flags);
    }

    internal ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _b;
        private int _p;
        public Reader(byte[] buf, int len, int start) { _b = buf.AsSpan(0, len); _p = start; }
        public int Remaining => _b.Length - _p;
        private void Need(int n) { if (n < 0 || _p + n > _b.Length) throw new IndexOutOfRangeException("truncated packet"); }
        public byte U8() { Need(1); return _b[_p++]; }
        public ushort U16() { Need(2); var v = BinaryPrimitives.ReadUInt16LittleEndian(_b.Slice(_p)); _p += 2; return v; }
        public uint U32() { Need(4); var v = BinaryPrimitives.ReadUInt32LittleEndian(_b.Slice(_p)); _p += 4; return v; }
        public int I32() { Need(4); var v = BinaryPrimitives.ReadInt32LittleEndian(_b.Slice(_p)); _p += 4; return v; }
        public float F32() { float f = BitConverter.Int32BitsToSingle(I32()); if (float.IsNaN(f) || float.IsInfinity(f) || Math.Abs(f) > 1e7f) throw new InvalidDataException("bad number"); return f; }
        public ulong U64() { ulong lo = U32(), hi = U32(); return lo | (hi << 32); }
        public string Str()
        {
            int n = U16(); Need(n); var s = Encoding.UTF8.GetString(_b.Slice(_p, n)); _p += n;
            for (int i = 0; i < s.Length; i++) if (char.IsControl(s[i])) { var sb = new StringBuilder(s.Length); foreach (char c in s) if (!char.IsControl(c)) sb.Append(c); return sb.ToString(); }
            return s;
        }
        public ReadOnlySpan<byte> Bytes(int n) { Need(n); var s = _b.Slice(_p, n); _p += n; return s; }
    }

    internal static class Protocol
    {
        public const uint Magic = 0x31435250;
        public const byte Version = 41;
        public const int HeaderSize = 6;
        public const int MaxPacket = 1 << 20;

        public static readonly byte[] RecvBuffer = new byte[MaxPacket];
        public static readonly Writer W = new Writer();

        public static bool TryReadHeader(byte[] buf, int len, out Msg type)
        {
            type = 0;
            if (len < HeaderSize) return false;
            if (BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(0)) != Magic) return false;
            if (buf[5] != Version) return false;
            type = (Msg)buf[4];
            return true;
        }

        public static bool SendSimple(ulong to, Msg type, int flags) => W.Begin(type).SendTo(to, flags);

        public static bool IsModHello(byte[] buf, int len, out byte version)
        {
            version = 0;
            if (len < HeaderSize) return false;
            if (BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(0)) != Magic) return false;
            if (buf[4] != (byte)Msg.ModHello) return false;
            version = buf[5];
            return true;
        }

        public static bool SendWelcome(ulong to, bool accepted, uint nonce, int slot)
        {
            var w = W.Begin(Msg.Welcome); w.U8(accepted ? (byte)1 : (byte)0); w.U32(nonce); w.U8((byte)slot);
            return W.SendTo(to, SteamNet.SendReliable);
        }

        public static bool ReadWelcome(byte[] buf, int len, out uint nonce, out int slot)
        {
            var r = new Reader(buf, len, HeaderSize);
            bool ok = r.U8() == 1; nonce = r.U32(); slot = r.Remaining > 0 ? r.U8() : 1;
            return ok;
        }

        public static void SendState(in PoseState s)
        {
            var w = W.Begin(Msg.State);
            w.U32(s.Seq); w.F32(s.X); w.F32(s.Y); w.F32(s.Z); w.F32(s.Yaw); w.F32(s.Pitch); w.F32(s.EyeHeight);
            w.U8((byte)((s.InWorld ? 1 : 0) | (s.Sweeping ? 2 : 0)));
            w.SendAll(SteamNet.SendUnreliable);
        }

        public static bool ReadState(byte[] buf, int len, out PoseState s)
        {
            var r = new Reader(buf, len, HeaderSize);
            s = new PoseState { Seq = r.U32(), X = r.F32(), Y = r.F32(), Z = r.F32(), Yaw = r.F32(), Pitch = r.F32(), EyeHeight = r.F32() };
            int flags = r.Remaining > 0 ? r.U8() : 0;
            s.InWorld = (flags & 1) != 0; s.Sweeping = (flags & 2) != 0;
            return !(float.IsNaN(s.X) || float.IsNaN(s.Y) || float.IsNaN(s.Z) || float.IsInfinity(s.X));
        }
    }
}
