using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static class Net
    {
        public const int MaxPlayers = 4;

        internal sealed class Peer
        {
            public ulong Id;
            public int Slot;
            public string Name = "?";
            public bool Ready;
            public int Color = -1;
            public float LastRecv;
            public bool InWorld;
            public Vector3? Head;
            public bool Dead, Safe;
            public bool BroomReplay;
            public float Offset; public bool HasOffset;
        }

        public static readonly Dictionary<ulong, Peer> Peers = new Dictionary<ulong, Peer>();
        public static ulong HostId;
        public static bool IsHost;
        public static int MySlot;
        public static int MyColor = -1;
        public static ulong Sender;

        public static ulong Me => SteamNet.MySteamId;
        public static int Count => Peers.Count + 1;

        public static void Reset()
        {
            Peers.Clear(); HostId = 0; IsHost = false; MySlot = 0; Sender = 0;
        }

        public static Peer Get(ulong id) => Peers.TryGetValue(id, out var p) ? p : null;

        public static int SlotOf(ulong id) => id == Me ? MySlot : (Peers.TryGetValue(id, out var p) ? p.Slot : 99);

        public static string NameOf(ulong id) => id == Me ? SteamNet.MyName : (Peers.TryGetValue(id, out var p) ? p.Name : SteamNet.NameOf(id));

        public static string Label(int slot) => "P" + (slot + 1);

        public static void SendAll(Writer w, int flags)
        {
            foreach (var p in Peers.Values) SteamNet.Send(p.Id, w.Buf, w.Len, flags);
        }

        public static bool SendHost(Writer w, int flags) => !IsHost && HostId != 0 && SteamNet.Send(HostId, w.Buf, w.Len, flags);

        public static bool SendTo(ulong id, Writer w, int flags) => id != 0 && id != Me && SteamNet.Send(id, w.Buf, w.Len, flags);

        public static float Offset(ulong from, float sentAt)
        {
            float now = Time.realtimeSinceStartup;
            var p = Get(from);
            if (p == null) return now - sentAt;
            float off = now - sentAt;
            if (!p.HasOffset || off < p.Offset) { p.Offset = off; p.HasOffset = true; }
            else p.Offset += (off - p.Offset) * 0.01f;
            return p.Offset;
        }

        public static Vector3? HeadOf(ulong id)
        {
            if (id == Me) return World.LocalHead;
            var p = Get(id);
            return p != null && p.InWorld ? p.Head : null;
        }

        public static bool OtherCloser(Vector3 at, float myDist)
        {
            float best2 = myDist * myDist;
            foreach (var p in Peers.Values)
            {
                if (!p.InWorld || !p.Head.HasValue) continue;
                var d = p.Head.Value - at;
                if (d.x * d.x + d.y * d.y + d.z * d.z < best2) return true;
            }
            return false;
        }

        public static Peer NearestOther(Vector3 at, out float dist)
        {
            Peer best = null; float best2 = float.MaxValue;
            foreach (var p in Peers.Values)
            {
                if (!p.InWorld || !p.Head.HasValue) continue;
                var d = p.Head.Value - at;
                float d2 = d.x * d.x + d.y * d.y + d.z * d.z;
                if (d2 < best2) { best2 = d2; best = p; }
            }
            dist = best != null ? Mathf.Sqrt(best2) : float.MaxValue;
            return best;
        }

        public static List<ulong> WorkSplit()
        {
            var ids = new List<ulong> { Me };
            foreach (var p in Peers.Values) if (p.InWorld) ids.Add(p.Id);
            ids.Sort((a, b) => SlotOf(a).CompareTo(SlotOf(b)));
            return ids;
        }

        public static readonly Color[] Palette =
        {
            new Color(0.84f, 0.23f, 0.23f),
            new Color(0.93f, 0.49f, 0.16f),
            new Color(0.95f, 0.80f, 0.20f),
            new Color(0.60f, 0.85f, 0.22f),
            new Color(0.20f, 0.70f, 0.32f),
            new Color(0.15f, 0.70f, 0.65f),
            new Color(0.25f, 0.78f, 0.92f),
            new Color(0.23f, 0.42f, 0.91f),
            new Color(0.48f, 0.32f, 0.88f),
            new Color(0.78f, 0.30f, 0.85f),
            new Color(0.93f, 0.40f, 0.68f),
            new Color(0.92f, 0.92f, 0.92f),
            new Color(0.45f, 0.45f, 0.48f),
            new Color(0.50f, 0.32f, 0.18f),
        };

        private static readonly int[] SlotDefault = { 0, 7, 4, 2 };

        public static Color ColorFor(int slot, int index)
        {
            if (index >= 0 && index < Palette.Length) return Palette[index];
            if (slot == 0) return Plugin.ParseColor(Plugin.Player1Color.Value, Palette[0]);
            if (slot == 1) return Plugin.ParseColor(Plugin.Player2Color.Value, Palette[7]);
            return Palette[SlotDefault[Math.Max(0, Math.Min(3, slot))]];
        }

        public static int DefaultIndex(int slot) => SlotDefault[Math.Max(0, Math.Min(3, slot))];
    }
}
