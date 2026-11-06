using System;
using System.Collections.Generic;
using System.IO;

namespace PlayroomsCoop
{
    internal static class HostSave
    {
        private const string HelperT = "TwoPixels.Utilities.TwoPixelsEasyAutoSaveHelper";
        private const string MarkerExt = ".coopmarker";
        private const string BackupExt = ".coopbackup";
        private const int ChunkSize = 100 * 1024;
        private const int MaxSave = 32 * 1024 * 1024;

        private static byte[] _outData;
        private static uint _outXfer;
        private static readonly Queue<int> _outQueue = new Queue<int>();
        private static float _nextPump;
        private static uint _inXfer;
        private static float _lastChunkAt;

        public static bool Enabled => Plugin.UseHostSave.Value;

        private static bool _saveRequested;
        private static IntPtr _sentFor, _requestFor;
        private static float _sendAt;

        private static byte[] _incoming;
        private static bool[] _got;
        private static int _gotCount;
        private static byte[] _pendingLoad;
        private static bool _returnRequested;
        private static float _menuSince = -1f;
        public static bool GuestLoaded { get; private set; }
        private static bool _sessionActive;
        private static float _outOfWorldSince = -1f;

        public static void OnConnected()
        {
            _sentFor = IntPtr.Zero; _requestFor = IntPtr.Zero; _saveRequested = false;
            _incoming = null; _got = null; _gotCount = 0; _pendingLoad = null; _returnRequested = false;
            _outQueue.Clear(); _outData = null;
            _sessionActive = true;
        }

        public static void OnHostStartGame()
        {
            _sentFor = IntPtr.Zero; _saveRequested = false;
        }

        public static void OnClientBackInMenu()
        {
            _returnRequested = false; _menuSince = -1f;
        }

        public static void OnDisconnected()
        {
            _sessionActive = false;
            _pendingLoad = null; _incoming = null;
        }

        private static Type Helper => R.T(HelperT);

        private static string SlotPath(int slot)
        {
            var settings = R.CallStatic(Helper, "GetSlotSettings", slot);
            return settings == null ? null : R.Get<string>(settings, "FullPath");
        }

        private static object GameManager()
        {
            foreach (var g in R.FindAll(R.T("TwoPixels.BackroomCleaner.GameManager"))) return g;
            return null;
        }

        public static void HostTick(float now, bool playing, ulong peer)
        {
            PumpChunks(now, peer);
            if (!Enabled || !playing || !World.InGameWorld()) return;
            IntPtr world = World.WorldId();
            if (world == IntPtr.Zero || world == _sentFor) return;
            if (_saveRequested && world != _requestFor) _saveRequested = false;
            if (!_saveRequested)
            {
                _saveRequested = true; _requestFor = world; _sendAt = now + 1.5f;
                var inst = R.GetStatic(Helper, "Instance");
                if (inst != null) R.Call(inst, "SaveGame");
                return;
            }
            if (now < _sendAt) return;
            _sentFor = world; _saveRequested = false;

            var helper = R.GetStatic(Helper, "Instance");
            int slot = helper != null ? R.Get<int>(helper, "ActiveSlot") : -1;
            string path = slot >= 0 ? SlotPath(slot) : null;
            if (path == null || !File.Exists(path))
            {
                Plugin.Log.LogWarning($"[HostSave] couldn't find the host's save file (slot {slot}, path {path})");
                CoopBehaviour.Notify("Couldn't send your save: your partner keeps their own world");
                return;
            }
            byte[] data = File.ReadAllBytes(path);
            int chunks = (data.Length + ChunkSize - 1) / ChunkSize;
            _outData = data; _outXfer = (uint)new Random().Next(1, int.MaxValue);
            _outQueue.Clear();
            for (int i = 0; i < chunks; i++) _outQueue.Enqueue(i);
            var w = Protocol.W.Begin(Msg.SaveOffer); w.I32(data.Length); w.I32(chunks); w.U32(_outXfer);
            w.SendTo(peer, SteamNet.SendReliable);
            Plugin.Log.LogInfo($"[HostSave] sending the host's save (slot {slot}, {data.Length / 1024} KB in {chunks} parts)");
        }

        private static void PumpChunks(float now, ulong peer)
        {
            if (_outQueue.Count == 0 || _outData == null || now < _nextPump) return;
            for (int k = 0; k < 2 && _outQueue.Count > 0; k++)
            {
                int i = _outQueue.Peek();
                int chunks = (_outData.Length + ChunkSize - 1) / ChunkSize;
                int off = i * ChunkSize, n = Math.Min(ChunkSize, _outData.Length - off);
                var c = Protocol.W.Begin(Msg.SaveChunk); c.U32(_outXfer); c.I32(i); c.I32(n);
                c.Bytes(_outData.AsSpan(off, n).ToArray(), n);
                if (!c.SendTo(peer, SteamNet.SendReliable)) { _nextPump = now + 0.2f; return; }
                _outQueue.Dequeue();
                if (_outQueue.Count == 0) Plugin.Log.LogInfo($"[HostSave] sent the host's save ({chunks} parts)");
            }
        }

        public static void HandleResend(byte[] buf, int len)
        {
            var r = new Reader(buf, len, Protocol.HeaderSize);
            uint xfer = r.U32(); int n = r.U16();
            if (xfer != _outXfer || _outData == null) return;
            var have = new HashSet<int>(_outQueue);
            for (int i = 0; i < n; i++) { int idx = r.I32(); if (have.Add(idx)) _outQueue.Enqueue(idx); }
            Plugin.Log.LogInfo($"[HostSave] resending {n} missing parts");
        }

        public static void ClientResendTick(float now, ulong peer)
        {
            if (_incoming == null || _got == null || now - _lastChunkAt < 3f) return;
            _lastChunkAt = now;
            var missing = new List<int>();
            for (int i = 0; i < _got.Length; i++) if (!_got[i]) missing.Add(i);
            if (missing.Count == 0) return;
            var w = Protocol.W.Begin(Msg.SaveResend); w.U32(_inXfer); w.U16((ushort)missing.Count);
            foreach (int i in missing) w.I32(i);
            w.SendTo(peer, SteamNet.SendReliable);
            Plugin.Log.LogInfo($"[HostSave] asking the host for {missing.Count} missing parts");
        }

        public static void Handle(Msg type, byte[] buf, int len)
        {
            if (!Enabled) return;
            var r = new Reader(buf, len, Protocol.HeaderSize);
            if (type == Msg.SaveOffer)
            {
                int size = r.I32(), chunks = r.I32();
                _inXfer = r.Remaining >= 4 ? r.U32() : 0;
                if (size <= 0 || size > MaxSave || chunks != (size + ChunkSize - 1) / ChunkSize) { Plugin.Log.LogWarning($"[HostSave] ignored a save offer with a bad size ({size} bytes, {chunks} parts)"); return; }
                _incoming = new byte[size]; _got = new bool[chunks]; _gotCount = 0;
                _lastChunkAt = UnityEngine.Time.realtimeSinceStartup;
                Plugin.Log.LogInfo($"[HostSave] receiving host save: {size / 1024} KB");
                return;
            }
            if (_incoming == null) return;
            uint xfer = r.U32();
            if (xfer != _inXfer) return;
            int idx = r.I32(), n = r.I32();
            if (idx < 0 || idx >= _got.Length || _got[idx] || n <= 0 || n > ChunkSize) return;
            var bytes = r.Bytes(n);
            int off = idx * ChunkSize;
            if (off + n > _incoming.Length) return;
            bytes.CopyTo(new Span<byte>(_incoming, off, n));
            _got[idx] = true; _gotCount++;
            _lastChunkAt = UnityEngine.Time.realtimeSinceStartup;
            if (_gotCount == _got.Length)
            {
                _pendingLoad = _incoming; _incoming = null;
                Plugin.Log.LogInfo("[HostSave] host save received");
            }
        }

        private static bool _selfChecked;
        private static float _selfCheckAt = -1f;

        public static void SelfCheck(float now)
        {
            if (_selfChecked || !Plugin.CheckHostSave.Value || !World.InGameWorld()) { if (!World.InGameWorld()) _selfCheckAt = -1f; return; }
            if (_selfCheckAt < 0f) { _selfCheckAt = now + 10f; return; }
            if (now < _selfCheckAt) return;
            _selfChecked = true;
            try
            {
                var helper = R.GetStatic(Helper, "Instance");
                int slot = helper != null ? R.Get<int>(helper, "ActiveSlot") : -1;
                string path = slot >= 0 ? SlotPath(slot) : null;
                if (path == null || !File.Exists(path)) { Plugin.Log.LogInfo("[SaveCheck] no save file yet to test"); return; }
                object settings = null;
                try { settings = R.CallStatic(Helper, "GetSlotSettings", slot); } catch { }
                string why = SaveGuard.Check(File.ReadAllBytes(path), settings);
                if (why == null) Plugin.Log.LogInfo($"[SaveCheck] OK: your own save (slot {slot}) passes the host save check");
                else Plugin.Log.LogWarning($"[SaveCheck] FAILED on your own save (slot {slot}): {why}. Please report this; set CheckHostSave = false meanwhile");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[SaveCheck] couldn't run: " + e.Message); }
        }

        public static void ClientTick(float now)
        {
            bool inWorld = World.InGameWorld();

            if (_pendingLoad != null && Enabled)
            {
                if (inWorld)
                {
                    if (!_returnRequested)
                    {
                        _returnRequested = true;
                        CoopBehaviour.Notify("Switching to the host's world...");
                        try { R.Call(GameManager(), "ReturnToMainMenu"); }
                        catch (Exception e) { Plugin.Log.LogWarning("[HostSave] couldn't return to the main menu: " + e.Message); }
                    }
                    _menuSince = -1f;
                    return;
                }
                if (R.FindAll(R.T("TwoPixels.UserInterfaces.MainMenuCanvas")).Count == 0) { _menuSince = -1f; return; }
                if (_menuSince < 0f) { _menuSince = now; return; }
                if (now - _menuSince < 1.5f) return;
                var data = _pendingLoad; _pendingLoad = null; _returnRequested = false;
                LoadHostCopy(data);
                return;
            }

            if (!inWorld) { if (_outOfWorldSince < 0f) _outOfWorldSince = now; }
            else _outOfWorldSince = -1f;
            if (!_sessionActive && GuestLoaded && !inWorld && now - _outOfWorldSince > 2f)
            {
                RestoreAll();
                GuestLoaded = false;
            }
        }

        private static void LoadHostCopy(byte[] data)
        {
            try
            {
                var gm = GameManager();
                int slot = 0;
                try { if (gm != null && Convert.ToBoolean(R.Call(gm, "HasAnySave"))) slot = Math.Max(0, Convert.ToInt32(R.Call(gm, "GetContinueSlot"))); } catch { }
                string path = SlotPath(slot);
                if (string.IsNullOrEmpty(path)) throw new Exception("no save path for slot " + slot);

                if (Plugin.CheckHostSave.Value)
                {
                    object settings = null;
                    try { settings = R.CallStatic(Helper, "GetSlotSettings", slot); } catch { }
                    string why = SaveGuard.Check(data, settings);
                    if (why != null)
                    {
                        Plugin.Log.LogError("[HostSave] refused the host's save: " + why);
                        CoopBehaviour.Notify("The host's save was refused (see the log). Your own save is untouched.");
                        return;
                    }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                string marker = path + MarkerExt, backup = path + BackupExt;
                if (!File.Exists(marker))
                {
                    bool existed = File.Exists(path);
                    if (existed) File.Copy(path, backup, true);
                    File.WriteAllText(marker, (existed ? "1" : "0") + "\n" + path);
                }
                File.WriteAllBytes(path, data);
                RefreshCache(path);
                GuestLoaded = true;
                Plugin.Log.LogInfo($"[HostSave] your own save is backed up; loading the host's world in slot {slot}");
                CoopBehaviour.Notify("Loading the host's world (your own save is safe)");

                object canvas = null;
                foreach (var c in R.FindAll(R.T("TwoPixels.UserInterfaces.MainMenuCanvas"))) { canvas = c; break; }
                try { R.Call(canvas, "StartGame", slot, false, null); }
                catch { R.Call(gm, "ActivateSlot", slot, false, null); }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[HostSave] couldn't load the host's save: " + ((e as System.Reflection.TargetInvocationException)?.InnerException ?? e).Message);
                CoopBehaviour.Notify("Couldn't load the host's world. Your own save is untouched.");
                RestoreAll();
            }
        }

        public static void RestoreAll()
        {
            try
            {
                string root = null;
                try { root = Convert.ToString(R.GetStatic(typeof(UnityEngine.Application), "persistentDataPath")); } catch { }
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
                foreach (var marker in Directory.GetFiles(root, "*" + MarkerExt, SearchOption.AllDirectories))
                {
                    try
                    {
                        string path = marker.Substring(0, marker.Length - MarkerExt.Length);
                        string backup = path + BackupExt;
                        bool existed = File.ReadAllText(marker).StartsWith("1");
                        if (existed && File.Exists(backup)) File.Copy(backup, path, true);
                        else if (!existed && File.Exists(path)) File.Delete(path);
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Delete(marker);
                        RefreshCache(path);
                        Plugin.Log.LogInfo($"[HostSave] your own save is back: {Path.GetFileName(path)}");
                    }
                    catch (Exception e) { Plugin.Log.LogError($"[HostSave] couldn't restore {marker}: {e.Message} (your save is in the .coopbackup file next to it)"); }
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("[HostSave] restore check failed: " + e.Message); }
        }

        private static void RefreshCache(string path)
        {
            try { var es3 = R.T("ES3"); if (es3 != null && File.Exists(path)) R.CallStatic(es3, "CacheFile", path); } catch { }
        }
    }
}
