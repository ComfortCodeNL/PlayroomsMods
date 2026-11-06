using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static class Boards
    {
        private const string BoardT = "TwoPixels.Whiteboard.Whiteboard";
        private const int MaxPng = 480 * 1024; // Steam reliable limit is 512 KB

        public static Feature Feature;
        private static bool _connected, _isHost, _snapshotSent;
        private static ulong _peer;
        private static readonly Dictionary<string, object> _boards = new Dictionary<string, object>();
        private static readonly Dictionary<string, bool> _wasOpen = new Dictionary<string, bool>();
        private static readonly Dictionary<string, int> _lastHash = new Dictionary<string, int>();
        private static readonly Dictionary<string, byte[]> _pending = new Dictionary<string, byte[]>();
        private static float _nextPoll, _lastScan = -99f;

        public static void Init() => Feature = new Feature("Whiteboards", Plugin.SyncBoards.Value);

        public static void OnConnected(ulong peer, bool isHost)
        {
            _connected = true; _peer = peer; _isHost = isHost; _snapshotSent = false;
            _wasOpen.Clear(); _lastHash.Clear(); _pending.Clear();
        }

        public static void OnDisconnected() { _connected = false; _peer = 0; }

        private static void Scan(float now)
        {
            _lastScan = now;
            _boards.Clear();
            foreach (var b in R.FindAll(R.T(BoardT)))
            {
                string id = null;
                try { id = R.Get<string>(b, "boardId"); } catch { }
                if (string.IsNullOrEmpty(id)) id = ((UnityEngine.Object)b).name;
                _boards[id] = b;
            }
        }

        private static byte[] Encode(object board)
        {
            R.Call(board, "CacheEncodedPng");
            var arr = R.Get(board, "_cachedPng");
            if (arr == null) return null;
            var bytes = new List<byte>(64 * 1024);
            foreach (var x in (IEnumerable)arr) bytes.Add(Convert.ToByte(x));
            return bytes.ToArray();
        }

        private static int Hash(byte[] b)
        {
            unchecked
            {
                int h = (int)2166136261;
                for (int i = 0; i < b.Length; i += 7) h = (h ^ b[i]) * 16777619;
                return h ^ b.Length;
            }
        }

        private static void Send(string id, byte[] png)
        {
            if (png == null || png.Length == 0) return;
            if (png.Length > MaxPng) { Plugin.Log.LogWarning($"[Whiteboards] board '{id}' picture too large to send ({png.Length / 1024} KB)"); return; }
            var w = Protocol.W.Begin(Msg.Whiteboard);
            w.Str(id); w.I32(png.Length); w.Bytes(png, png.Length);
            w.SendTo(_peer, SteamNet.SendReliable);
        }

        public static void Tick(float now)
        {
            if (!_connected || !Feature.Enabled || now < _nextPoll) return;
            _nextPoll = now + 1.2f;
            Feature.Try(() =>
            {
                if (now - _lastScan > 10f || _boards.Count == 0) Scan(now);
                if (_boards.Count == 0) return;

                if (!_snapshotSent)
                {
                    _snapshotSent = true;
                    if (_isHost)
                        foreach (var kv in _boards)
                        {
                            var png = Encode(kv.Value);
                            if (png == null) continue;
                            _lastHash[kv.Key] = Hash(png);
                            Send(kv.Key, png);
                        }
                }

                foreach (var kv in _boards)
                {
                    if (!World.Alive(kv.Value)) continue;
                    bool open = Convert.ToBoolean(R.Get(kv.Value, "IsOpen"));
                    _wasOpen.TryGetValue(kv.Key, out bool was);
                    _wasOpen[kv.Key] = open;

                    if (open || was)
                    {
                        var png = Encode(kv.Value);
                        if (png == null) continue;
                        int h = Hash(png);
                        if (!_lastHash.TryGetValue(kv.Key, out int last) || last != h)
                        {
                            _lastHash[kv.Key] = h;
                            Send(kv.Key, png);
                        }
                    }
                    if (!open && _pending.TryGetValue(kv.Key, out var waiting))
                    {
                        _pending.Remove(kv.Key);
                        Apply(kv.Key, kv.Value, waiting);
                    }
                }
            }, "whiteboards");
        }

        private static void Apply(string id, object board, byte[] png)
        {
            var mi = board.GetType().GetMethod("ApplyPng");
            if (mi == null) throw new MissingMethodException(board.GetType().Name, "ApplyPng");
            var arrType = mi.GetParameters()[0].ParameterType; // Il2CppStructArray<byte>
            object il2cppBytes = Activator.CreateInstance(arrType, new object[] { png });
            mi.Invoke(board, new[] { il2cppBytes });
            try { R.Call(board, "FlushTexture"); } catch { }
            _lastHash[id] = Hash(png);
        }

        private static bool LooksLikeBoardPng(byte[] b)
        {
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            if (b.Length < 33) return false;
            for (int i = 0; i < 8; i++) if (b[i] != sig[i]) return false;
            if (b[12] != (byte)'I' || b[13] != (byte)'H' || b[14] != (byte)'D' || b[15] != (byte)'R') return false;
            uint w = (uint)(b[16] << 24 | b[17] << 16 | b[18] << 8 | b[19]);
            uint h = (uint)(b[20] << 24 | b[21] << 16 | b[22] << 8 | b[23]);
            return w > 0 && h > 0 && w <= 4096 && h <= 4096;
        }

        public static void Handle(byte[] buf, int len)
        {
            if (!Feature.Enabled) return;
            var r = new Reader(buf, len, Protocol.HeaderSize);
            string id = r.Str();
            int n = r.I32();
            if (n <= 0 || n > MaxPng) return;
            byte[] png = r.Bytes(n).ToArray();
            if (!LooksLikeBoardPng(png)) { Plugin.Log.LogWarning($"[Whiteboards] ignored a board picture that isn't a normal PNG ({n} bytes)"); return; }
            Feature.Try(() =>
            {
                if (!_boards.TryGetValue(id, out var board) || !World.Alive(board))
                {
                    Scan(Time.realtimeSinceStartup);
                    if (!_boards.TryGetValue(id, out board)) return;
                }
                if (Convert.ToBoolean(R.Get(board, "IsOpen"))) { _pending[id] = png; return; }
                Apply(id, board, png);
            }, "apply whiteboard");
        }
    }
}
