using System.Collections.Generic;

namespace PlayroomsCoop
{
    internal static class NetCheck
    {
        private static readonly Dictionary<ulong, string> _logged = new Dictionary<ulong, string>();
        private static int _loggedRelay = int.MinValue;

        public static string Label(ulong id)
        {
            var s = SteamNet.GetSession(id);
            switch (s.State)
            {
                case SteamNet.StateConnected: return "no mod reply";
                case SteamNet.StateConnecting:
                case SteamNet.StateFindingRoute: return "connecting...";
                case SteamNet.StateClosedByPeer:
                case SteamNet.StateProblem: return "can't connect (" + ShortReason(s.EndReason) + ")";
                default: return "no reply";
            }
        }

        public static string Status(string name, ulong id)
        {
            var s = SteamNet.GetSession(id);
            if (s.State == SteamNet.StateConnected) return $"Connected to {name}, but their game didn't answer. They may not have the co-op mod, or BepInEx isn't loading for them.";
            if (s.State == SteamNet.StateClosedByPeer || s.State == SteamNet.StateProblem) return $"Can't connect to {name}: {Reason(s.EndReason)}.";
            return $"No reply from {name} yet. Check that both of you allow Playrooms through the firewall.";
        }

        private static string ShortReason(int code)
        {
            if (code >= 3000 && code < 4000) return "your network";
            if (code >= 4000 && code < 5000) return "their network";
            if (code == 5008 || code == 5009) return "router or firewall";
            return "Steam";
        }

        public static string Reason(int code)
        {
            if (code >= 3000 && code < 4000) return "a network problem on your side (firewall, VPN or Steam offline)";
            if (code >= 4000 && code < 5000) return "a network problem on their side (firewall, VPN or Steam offline)";
            if (code == 5008 || code == 5009) return "your networks can't reach each other (router or firewall)";
            if (code >= 5000 && code < 6000) return "Steam couldn't route the connection";
            return "Steam closed the connection";
        }

        public static void Tick(List<SteamNet.Friend> friends)
        {
            int relay = SteamNet.RelayStatus();
            if (relay != _loggedRelay)
            {
                _loggedRelay = relay;
                Plugin.Log.LogInfo((object)$"[Net] Steam relay network: {RelayName(relay)}");
            }
            foreach (var f in friends)
            {
                var s = SteamNet.GetSession(f.Id);
                string line = s.State == SteamNet.StateConnected ? (s.Relayed ? "connected through Steam's relay" : "connected directly")
                    : s.State == SteamNet.StateConnecting || s.State == SteamNet.StateFindingRoute ? "connecting"
                    : s.State == SteamNet.StateNone ? "no session"
                    : $"failed, reason {s.EndReason} ({s.Debug})";
                if (_logged.TryGetValue(f.Id, out var old) && old == line) continue;
                _logged[f.Id] = line;
                Plugin.Log.LogInfo((object)$"[Net] {SteamNet.Plain(f.Name)}: {line}");
            }
        }

        private static string RelayName(int v)
        {
            switch (v)
            {
                case 100: return "working";
                case 3: case 2: case 1: return "starting";
                case -10: return "retrying";
                case -100: return "worked before, now failing";
                case -101: return "failed";
                case -102: return "can't try (Steam offline?)";
                case 0: return "unknown";
                default: return v.ToString();
            }
        }
    }
}
