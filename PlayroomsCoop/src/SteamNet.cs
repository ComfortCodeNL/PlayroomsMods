using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayroomsCoop
{
    internal static unsafe class SteamNet
    {
        private const string Lib = "steam_api64";

        public const int Channel = 47;
        public const int SendUnreliable = 0 | 1 | 32;
        public const int SendReliable = 8 | 32;

        private const int FriendFlagImmediate = 0x04;
        private const int IdentityTypeSteamID = 16;
        private const int EResultOK = 1;

        private static IntPtr _friends, _user, _utils, _msgs, _netUtils;
        private static int _failures;

        public static bool Ready { get; private set; }
        public static ulong MySteamId { get; private set; }
        public static uint AppId { get; private set; }
        public static string MyName { get; private set; } = "?";

        static SteamNet()
        {
            try
            {
                var nl = Type.GetType("System.Runtime.InteropServices.NativeLibrary");
                var resolverType = Type.GetType("System.Runtime.InteropServices.DllImportResolver");
                var set = nl?.GetMethod("SetDllImportResolver");
                if (set != null && resolverType != null)
                {
                    var del = Delegate.CreateDelegate(resolverType, typeof(SteamNet).GetMethod(nameof(Resolve),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
                    set.Invoke(null, new object[] { typeof(SteamNet).Assembly, del });
                }
            }
            catch (Exception) { /* fall back to normal DLL lookup, which also finds the loaded module */ }
        }

        private static IntPtr Resolve(string name, System.Reflection.Assembly asm, DllImportSearchPath? path)
        {
            if (name.StartsWith("steam_api64", StringComparison.OrdinalIgnoreCase))
            {
                IntPtr h = GetModuleHandleW("steam_api64.dll");
                if (h != IntPtr.Zero) return h;
            }
            return IntPtr.Zero;
        }

        public static bool TryInit()
        {
            if (Ready) return true;
            try
            {
                _friends = SteamAPI_SteamFriends_v017();
                _user = SteamAPI_SteamUser_v023();
                _utils = SteamAPI_SteamUtils_v010();
                _msgs = SteamAPI_SteamNetworkingMessages_SteamAPI_v002();
                if (_friends == IntPtr.Zero || _user == IntPtr.Zero || _utils == IntPtr.Zero || _msgs == IntPtr.Zero)
                    return false;

                MySteamId = SteamAPI_ISteamUser_GetSteamID(_user);
                AppId = SteamAPI_ISteamUtils_GetAppID(_utils);
                MyName = Utf8(SteamAPI_ISteamFriends_GetPersonaName(_friends)) ?? "Player";
                if (MySteamId == 0) return false;

                Ready = true;
                Plugin.Log.LogInfo($"Steam ready. Me={MyName} ({MySteamId}), AppID={AppId}");
                try
                {
                    _netUtils = SteamAPI_SteamNetworkingUtils_SteamAPI_v004();
                    if (_netUtils != IntPtr.Zero && Plugin.ForceSteamRelay.Value)
                    {
                        bool ok = SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueInt32(_netUtils, ConfigIceEnable, 0);
                        Plugin.Log.LogInfo(ok ? "[Net] direct connections off, everything goes through Steam's relay (ForceSteamRelay)" : "[Net] couldn't turn off direct connections");
                    }
                    if (_netUtils != IntPtr.Zero) SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(_netUtils);
                }
                catch (Exception e) { _netUtils = IntPtr.Zero; Plugin.Log.LogInfo("[Net] relay status not available: " + e.Message); }
                return true;
            }
            catch (Exception e)
            {
                _failures++;
                if (_failures == 1) Plugin.Log.LogInfo("Waiting for the game to start Steam...");
                if (_failures == 600) Plugin.Log.LogError("Steam still not available (is the game running through Steam?): " + e.Message);
                return false;
            }
        }

        public struct Friend
        {
            public ulong Id;
            public string Name;
        }

        public static List<Friend> GetFriendsInThisGame()
        {
            var list = new List<Friend>();
            if (!Ready) return list;
            int n = SteamAPI_ISteamFriends_GetFriendCount(_friends, FriendFlagImmediate);
            for (int i = 0; i < n; i++)
            {
                ulong id = SteamAPI_ISteamFriends_GetFriendByIndex(_friends, i, FriendFlagImmediate);
                if (id == 0) continue;
                if (!SteamAPI_ISteamFriends_GetFriendGamePlayed(_friends, id, out FriendGameInfo info)) continue;
                if ((uint)(info.GameId & 0xFFFFFF) != AppId) continue;
                list.Add(new Friend { Id = id, Name = Utf8(SteamAPI_ISteamFriends_GetFriendPersonaName(_friends, id)) ?? id.ToString() });
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static string NameOf(ulong id)
        {
            if (!Ready || id == 0) return "?";
            return Utf8(SteamAPI_ISteamFriends_GetFriendPersonaName(_friends, id)) ?? id.ToString();
        }

        public static bool Send(ulong to, byte[] data, int len, int flags)
        {
            if (!Ready || to == 0) return false;
            var ident = MakeIdentity(to);
            fixed (byte* p = data)
            {
                int r = SteamAPI_ISteamNetworkingMessages_SendMessageToUser(_msgs, ref ident, p, (uint)len, flags, Channel);
                return r == EResultOK;
            }
        }

        public static void Accept(ulong from)
        {
            if (!Ready || from == 0) return;
            var ident = MakeIdentity(from);
            SteamAPI_ISteamNetworkingMessages_AcceptSessionWithUser(_msgs, ref ident);
        }

        public static void Close(ulong with)
        {
            if (!Ready || with == 0) return;
            var ident = MakeIdentity(with);
            SteamAPI_ISteamNetworkingMessages_CloseSessionWithUser(_msgs, ref ident);
        }

        public struct Session
        {
            public int State, EndReason;
            public bool Relayed;
            public string Debug;
        }

        private const int ConfigIceEnable = 104;
        public const int StateNone = 0, StateConnecting = 1, StateFindingRoute = 2, StateConnected = 3, StateClosedByPeer = 4, StateProblem = 5;

        private const int InfoState = 176, InfoEndReason = 180, InfoEndDebug = 184, InfoFlags = 440, FlagRelayed = 16;
        private static readonly byte[] _info = new byte[1024];

        public static Session GetSession(ulong with)
        {
            var r = new Session { Debug = "" };
            if (!Ready || with == 0) return r;
            var ident = MakeIdentity(with);
            Array.Clear(_info, 0, _info.Length);
            fixed (byte* p = _info)
            {
                r.State = SteamAPI_ISteamNetworkingMessages_GetSessionConnectionInfo(_msgs, ref ident, (IntPtr)p, IntPtr.Zero);
                if (r.State != StateNone && BitConverter.ToInt32(_info, InfoState) == r.State)
                {
                    r.EndReason = BitConverter.ToInt32(_info, InfoEndReason);
                    r.Relayed = (BitConverter.ToInt32(_info, InfoFlags) & FlagRelayed) != 0;
                    int n = 0;
                    while (n < 128 && _info[InfoEndDebug + n] != 0) n++;
                    r.Debug = Encoding.UTF8.GetString(_info, InfoEndDebug, n);
                }
            }
            return r;
        }

        public static int RelayStatus()
        {
            if (!Ready || _netUtils == IntPtr.Zero) return 0;
            try { return SteamAPI_ISteamNetworkingUtils_GetRelayNetworkStatus(_netUtils, IntPtr.Zero); }
            catch { _netUtils = IntPtr.Zero; return 0; }
        }

        private static readonly IntPtr[] _inbox = new IntPtr[64];

        public static void Receive(Action<ulong, byte[], int> onMessage)
        {
            if (!Ready) return;
            for (int guard = 0; guard < 8; guard++)
            {
                int count;
                fixed (IntPtr* box = _inbox)
                    count = SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(_msgs, Channel, box, _inbox.Length);
                if (count <= 0) return;

                for (int i = 0; i < count; i++)
                {
                    IntPtr m = _inbox[i];
                    try
                    {
                        byte* basePtr = (byte*)m;
                        IntPtr data = *(IntPtr*)(basePtr + 0);
                        int size = *(int*)(basePtr + 8);
                        int idType = *(int*)(basePtr + 16);
                        ulong sender = *(ulong*)(basePtr + 24);
                        if (idType != IdentityTypeSteamID || size <= 0 || size > Protocol.MaxPacket) continue;

                        var buf = Protocol.RecvBuffer;
                        Marshal.Copy(data, buf, 0, size);
                        onMessage(sender, buf, size);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning("Bad packet: " + e.Message);
                    }
                    finally
                    {
                        SteamAPI_SteamNetworkingMessage_t_Release(m);
                    }
                }
                if (count < _inbox.Length) return;
            }
        }

        private static NetIdentity MakeIdentity(ulong steamId)
        {
            var ident = new NetIdentity();
            SteamAPI_SteamNetworkingIdentity_SetSteamID64(ref ident, steamId);
            return ident;
        }

        private static string Utf8(IntPtr p) => p == IntPtr.Zero ? null : Plain(Marshal.PtrToStringUTF8(p));

        public static string Plain(string s)
        {
            if (s == null) return null;
            var sb = new StringBuilder(Math.Min(s.Length, 64));
            foreach (char c in s)
            {
                if (sb.Length >= 64) break;
                if (char.IsControl(c)) continue;
                sb.Append(c == '<' ? '\uFF1C' : c == '>' ? '\uFF1E' : c);
            }
            return sb.ToString();
        }

        [StructLayout(LayoutKind.Explicit, Size = 136)]
        private struct NetIdentity
        {
            [FieldOffset(0)] public int Type;
            [FieldOffset(4)] public int Size;
            [FieldOffset(8)] public ulong SteamId64;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct FriendGameInfo
        {
            [FieldOffset(0)] public ulong GameId;
            [FieldOffset(8)] public uint GameIP;
            [FieldOffset(12)] public ushort GamePort;
            [FieldOffset(14)] public ushort QueryPort;
            [FieldOffset(16)] public ulong LobbyId;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string name);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SteamAPI_SteamFriends_v017();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SteamAPI_SteamUser_v023();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SteamAPI_SteamUtils_v010();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SteamAPI_SteamNetworkingMessages_SteamAPI_v002();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern ulong SteamAPI_ISteamUser_GetSteamID(IntPtr self);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern uint SteamAPI_ISteamUtils_GetAppID(IntPtr self);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SteamAPI_ISteamFriends_GetPersonaName(IntPtr self);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern int SteamAPI_ISteamFriends_GetFriendCount(IntPtr self, int flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern ulong SteamAPI_ISteamFriends_GetFriendByIndex(IntPtr self, int index, int flags);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SteamAPI_ISteamFriends_GetFriendPersonaName(IntPtr self, ulong id);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SteamAPI_ISteamFriends_GetFriendGamePlayed(IntPtr self, ulong id, out FriendGameInfo info);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SteamAPI_ISteamNetworkingMessages_SendMessageToUser(IntPtr self, ref NetIdentity remote, byte* data, uint size, int flags, int channel);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SteamAPI_ISteamNetworkingMessages_ReceiveMessagesOnChannel(IntPtr self, int channel, IntPtr* outMessages, int max);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SteamAPI_ISteamNetworkingMessages_AcceptSessionWithUser(IntPtr self, ref NetIdentity remote);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SteamAPI_ISteamNetworkingMessages_CloseSessionWithUser(IntPtr self, ref NetIdentity remote);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        private static extern int SteamAPI_ISteamNetworkingMessages_GetSessionConnectionInfo(IntPtr self, ref NetIdentity remote, IntPtr info, IntPtr quickStatus);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr SteamAPI_SteamNetworkingUtils_SteamAPI_v004();
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern void SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess(IntPtr self);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SteamAPI_ISteamNetworkingUtils_SetGlobalConfigValueInt32(IntPtr self, int setting, int value);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern int SteamAPI_ISteamNetworkingUtils_GetRelayNetworkStatus(IntPtr self, IntPtr details);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern void SteamAPI_SteamNetworkingIdentity_SetSteamID64(ref NetIdentity self, ulong id);
        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] private static extern void SteamAPI_SteamNetworkingMessage_t_Release(IntPtr msg);
    }
}
