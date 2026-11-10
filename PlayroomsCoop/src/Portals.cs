using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private const string PortalT = "TwoPixels.Portals.Portal";
        private static readonly Dictionary<string, string> _coupledDoor = new Dictionary<string, string>();
        private static readonly List<object> _keptPairs = new List<object>();

        private static void PortalHooks()
        {
            R.Patch(R.T(PortalT), "ClaimOpenPair", 0, typeof(Hooks), nameof(Hooks.ClaimOpenPairPrefix), null);
        }

        internal static void OnClaimOpenPair(object portal)
        {
            if (!_connected || !Plugin.KeepDoorsOpen.Value) return;
            Doors.Try(() =>
            {
                var t = R.T(PortalT);
                var old = R.GetStatic(t, "_openPair");
                if (old == null || R.Same(old, portal)) return;
                var oldLink = R.Get(old, "linkedPortal");
                if (R.Same(oldLink, portal)) return;
                if (!InUse(old) && !InUse(oldLink)) return;
                R.SetStatic(t, "_openPair", null);
                if (!_keptPairs.Exists(k => R.Same(k, old))) _keptPairs.Add(old);
            }, "portal pair");
        }

        private static void KeptPairsTick()
        {
            if (_keptPairs.Count == 0) return;
            var current = R.GetStatic(R.T(PortalT), "_openPair");
            for (int i = _keptPairs.Count - 1; i >= 0; i--)
            {
                var p = _keptPairs[i];
                if (!Alive(p)) { _keptPairs.RemoveAt(i); continue; }
                var link = R.Get(p, "linkedPortal");
                if (R.Same(p, current) || R.Same(link, current)) { _keptPairs.RemoveAt(i); continue; }
                if (InUse(p) || InUse(link)) continue;
                _keptPairs.RemoveAt(i);
                R.Call(p, "RequestClose");
                if (link != null && Alive(link)) R.Call(link, "RequestClose");
            }
        }

        private static bool InUse(object portal)
        {
            if (portal == null || !Alive(portal)) return false;
            if (Near(portal, LocalHead)) return true;
            foreach (var p in Net.Peers.Values) if (p.InWorld && Near(portal, p.Head)) return true;
            return false;
        }

        private static bool Near(object portal, Vector3? head)
        {
            if (head == null) return false;
            var p = head.Value;
            try { if (Convert.ToBoolean(R.Call(portal, "IsWithinActivationRadius", p))) return true; } catch { }
            try
            {
                var c = (Vector3)R.Get(portal, "OpeningWorldCenter");
                return HDist(c, p) < 4f && Math.Abs(c.y - p.y) < 3f;
            }
            catch { return false; }
        }

        private static void ScanPortals()
        {
            _coupledDoor.Clear();
            Doors.Try(() =>
            {
                foreach (var p in R.FindAll(R.T(PortalT)))
                {
                    var door = R.Get(p, "door");
                    var link = R.Get(p, "linkedPortal");
                    var other = link == null ? null : R.Get(link, "door");
                    if (door == null || other == null || !Alive(door) || !Alive(other)) continue;
                    _coupledDoor[DoorKey(door)] = DoorKey(other);
                }
            }, "scan portals");
        }
    }
}
