using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace PlayroomsCoop
{
    internal static partial class World
    {
        private const float IslandMaxArea = 25f, IslandSearch = 6f, IslandCell = 2f;

        private sealed class NavPart { public float Area, SumY; public int Tris; public bool Visited; public float MeanY => Tris > 0 ? SumY / Tris : 0f; }

        private static MethodInfo _triangulate;
        private static Vector3[] _navVerts;
        private static int[] _navTris, _triPart;
        private static readonly List<NavPart> _parts = new List<NavPart>();
        private static readonly Dictionary<long, List<int>> _triGrid = new Dictionary<long, List<int>>();
        private static readonly Dictionary<long, List<int>> _vertGrid = new Dictionary<long, List<int>>();
        private static int[] _vertPart;
        private static int _navVertCount = -1;
        private static float _nextVisit;
        private static bool _islandsBroken;

        // Build
        private static void RefreshIslands()
        {
            if (_islandsBroken) return;
            try
            {
                if (_triangulate == null) _triangulate = R.T("UnityEngine.AI.NavMesh").GetMethod("CalculateTriangulation", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                var tri = _triangulate.Invoke(null, null);
                var verts = ToArray<Vector3>(tri.GetType().GetProperty("vertices").GetValue(tri));
                var idx = ToArray<int>(tri.GetType().GetProperty("indices").GetValue(tri));
                if (verts == null || idx == null) { _islandsBroken = true; return; }
                if (verts.Length == _navVertCount) return;
                BuildParts(verts, idx);
                Plugin.Log.LogInfo($"[Strays] walkable map: {_parts.Count} areas, {CountIslands()} small cut-off ones");
            }
            catch (Exception e)
            {
                _islandsBroken = true;
                Plugin.Log.LogWarning("[Strays] couldn't read the walkable map: " + e.Message);
            }
        }

        private static T[] ToArray<T>(object il2cppArray)
        {
            if (il2cppArray == null) return null;
            if (il2cppArray is T[] direct) return direct;
            foreach (var m in il2cppArray.GetType().GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "op_Implicit" && m.ReturnType == typeof(T[])) return (T[])m.Invoke(null, new[] { il2cppArray });
            int n = Convert.ToInt32(il2cppArray.GetType().GetProperty("Length").GetValue(il2cppArray));
            var item = il2cppArray.GetType().GetProperty("Item");
            var a = new T[n];
            for (int i = 0; i < n; i++) a[i] = (T)item.GetValue(il2cppArray, new object[] { i });
            return a;
        }

        private static void BuildParts(Vector3[] verts, int[] idx)
        {
            _navVertCount = verts.Length;
            var welded = new int[verts.Length];
            var weldKeys = new Dictionary<(int, int, int), int>();
            for (int i = 0; i < verts.Length; i++)
            {
                var k = ((int)Math.Round(verts[i].x * 50f), (int)Math.Round(verts[i].y * 50f), (int)Math.Round(verts[i].z * 50f));
                if (!weldKeys.TryGetValue(k, out int w)) { w = weldKeys.Count; weldKeys[k] = w; }
                welded[i] = w;
            }
            var parent = new int[weldKeys.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            int triCount = idx.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                Union(welded[idx[t * 3]], welded[idx[t * 3 + 1]]);
                Union(welded[idx[t * 3]], welded[idx[t * 3 + 2]]);
            }

            _parts.Clear(); _triGrid.Clear(); _vertGrid.Clear();
            var partOf = new Dictionary<int, int>();
            _triPart = new int[triCount];
            for (int t = 0; t < triCount; t++)
            {
                int root = Find(welded[idx[t * 3]]);
                if (!partOf.TryGetValue(root, out int p)) { p = _parts.Count; partOf[root] = p; _parts.Add(new NavPart()); }
                _triPart[t] = p;
                Vector3 a = verts[idx[t * 3]], b = verts[idx[t * 3 + 1]], c = verts[idx[t * 3 + 2]];
                float ux = b.x - a.x, uz = b.z - a.z, vx = c.x - a.x, vz = c.z - a.z;
                _parts[p].Area += Math.Abs(ux * vz - uz * vx) * 0.5f;
                _parts[p].SumY += (a.y + b.y + c.y) / 3f;
                _parts[p].Tris++;
                int x0 = Cell(Math.Min(a.x, Math.Min(b.x, c.x))), x1 = Cell(Math.Max(a.x, Math.Max(b.x, c.x)));
                int z0 = Cell(Math.Min(a.z, Math.Min(b.z, c.z))), z1 = Cell(Math.Max(a.z, Math.Max(b.z, c.z)));
                for (int x = x0; x <= x1; x++)
                    for (int z = z0; z <= z1; z++) AddTo(_triGrid, Key(x, z), t);
            }
            _vertPart = new int[verts.Length];
            for (int t = 0; t < triCount; t++)
                for (int j = 0; j < 3; j++) _vertPart[idx[t * 3 + j]] = _triPart[t];
            for (int i = 0; i < verts.Length; i++) AddTo(_vertGrid, Key(Cell(verts[i].x), Cell(verts[i].z)), i);
            _navVerts = verts; _navTris = idx;
        }

        private static int CountIslands()
        {
            int n = 0;
            foreach (var p in _parts) if (p.Area < IslandMaxArea) n++;
            return n;
        }

        private static int Cell(float v) => (int)Math.Floor(v / IslandCell);
        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        private static void AddTo(Dictionary<long, List<int>> grid, long key, int v)
        {
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
            list.Add(v);
        }

        // Lookup
        private static int PartAt(Vector3 p)
        {
            if (_navTris == null || !_triGrid.TryGetValue(Key(Cell(p.x), Cell(p.z)), out var tris)) return -1;
            int best = -1; float bestDy = 0.6f;
            foreach (int t in tris)
            {
                Vector3 a = _navVerts[_navTris[t * 3]], b = _navVerts[_navTris[t * 3 + 1]], c = _navVerts[_navTris[t * 3 + 2]];
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Math.Abs(d) < 1e-8f) continue;
                float l1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d;
                float l2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d;
                float l3 = 1f - l1 - l2;
                if (l1 < -0.01f || l2 < -0.01f || l3 < -0.01f) continue;
                float dy = Math.Abs(l1 * a.y + l2 * b.y + l3 * c.y - p.y);
                if (dy < bestDy) { bestDy = dy; best = _triPart[t]; }
            }
            return best;
        }

        private static bool IsPlaySpace(int part) => part < 0 || _parts[part].Visited || _parts[part].Area >= IslandMaxArea;

        private static Vector3? NearestPlaySpace(Vector3 p, int island)
        {
            if (_navVerts == null) return null;
            Vector3? best = null; float bestD = IslandSearch * IslandSearch;
            int cx = Cell(p.x), cz = Cell(p.z), r = (int)Math.Ceiling(IslandSearch / IslandCell);
            for (int x = cx - r; x <= cx + r; x++)
                for (int z = cz - r; z <= cz + r; z++)
                {
                    if (!_vertGrid.TryGetValue(Key(x, z), out var vs)) continue;
                    foreach (int v in vs)
                    {
                        int part = _vertPart[v];
                        if (part == island || !IsPlaySpace(part)) continue;
                        var q = _navVerts[v];
                        float dx = q.x - p.x, dz = q.z - p.z, d = dx * dx + dz * dz;
                        if (d < bestD) { bestD = d; best = q; }
                    }
                }
            return best;
        }

        private static bool OnRaisedIsland(Vector3 nav, out Vector3? playTo)
        {
            playTo = null;
            int part = PartAt(nav);
            if (IsPlaySpace(part)) return false;
            var below = NearestPlaySpace(nav, part);
            if (below == null) return false;
            var landed = NavPoint(below.Value);
            playTo = landed ?? below;
            return true;
        }

        // Visits
        private static void MarkVisitedTick(float now)
        {
            if (_navTris == null || now < _nextVisit) return;
            _nextVisit = now + 2f;
            var heads = new List<Vector3>();
            if (LocalHead.HasValue) heads.Add(LocalHead.Value);
            foreach (var p in Net.Peers.Values) if (p.InWorld && p.Head.HasValue) heads.Add(p.Head.Value);
            foreach (var h in heads)
            {
                var feet = NavPoint(h - new Vector3(0f, 1.4f, 0f));
                if (!feet.HasValue) continue;
                int part = PartAt(feet.Value);
                if (part >= 0) _parts[part].Visited = true;
            }
        }
    }
}
