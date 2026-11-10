using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Il2CppInterop.Common;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Runners;
using Microsoft.Extensions.Logging;

namespace PlayroomsPatcher
{
    internal static class InteropBuild
    {
        private static readonly Type Manager = Type.GetType("BepInEx.Unity.IL2CPP.Il2CppInteropManager, BepInEx.Unity.IL2CPP");

        private static T Get<T>(string name, T fallback)
        {
            const BindingFlags all = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                var p = Manager?.GetProperty(name, all);
                if (p != null) return (T)p.GetValue(null);
            }
            catch { }
            return fallback;
        }

        public static string InteropDir => Get("IL2CPPInteropAssemblyPath", Path.Combine(Paths.BepInExRootPath, "interop"));
        public static string UnityLibsDir => Get("UnityBaseLibsDirectory", Path.Combine(Paths.BepInExRootPath, "unity-libs"));
        public static string GameAssembly => Get("GameAssemblyPath", Path.Combine(Paths.GameRootPath, "GameAssembly.dll"));
        private static string StampPath => Path.Combine(InteropDir, "playrooms-patcher-stamp.txt");
        private static string BepInExHashPath => Path.Combine(InteropDir, "assembly-hash.txt");

        public static bool IsUpToDate() =>
            Directory.Exists(InteropDir) && Directory.EnumerateFiles(InteropDir, "*.dll").Any() &&
            File.Exists(StampPath) && File.ReadAllText(StampPath).Trim() == Stamp();

        private static string Stamp()
        {
            using var md5 = MD5.Create();
            void File(string path)
            {
                if (!System.IO.File.Exists(path)) return;
                using var fs = System.IO.File.OpenRead(path);
                var buf = new byte[81920];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0) md5.TransformBlock(buf, 0, n, null, 0);
            }
            void Text(string s) { var b = Encoding.UTF8.GetBytes(s); md5.TransformBlock(b, 0, b.Length, null, 0); }
            File(GameAssembly);
            File(Path.Combine(Paths.GameDataPath, "il2cpp_data", "Metadata", "global-metadata.dat"));
            Text(Entry.Version);
            Text(typeof(InteropAssemblyGenerator).Assembly.GetName().Version.ToString());
            Text(typeof(AsmResolver.DotNet.AssemblyDefinition).Assembly.GetName().Version.ToString());
            md5.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(md5.Hash).Replace("-", "");
        }

        public static void Build(ManualLogSource log, bool writeDummies)
        {
            var total = Stopwatch.StartNew();
            EnsureUnityLibs(log);

            log.LogMessage("Reading the game's types from the running game...");
            var sw = Stopwatch.StartNew();
            var dumper = new Dumper(log);
            var assemblies = dumper.Dump();
            log.LogInfo($"Read {assemblies.Count} assemblies, {dumper.TypeCount} types, {dumper.MethodCount} methods in {sw.Elapsed.TotalSeconds:F1}s" +
                        (dumper.Skipped > 0 ? $" ({dumper.Skipped} members skipped, see playrooms-patcher.log)" : ""));
            WriteProblems(dumper.Problems);
            if (writeDummies) WriteDummies(log, assemblies);

            string staging = InteropDir + ".staging";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            log.LogMessage("Generating interop assemblies...");
            sw.Restart();
            var opts = new GeneratorOptions
            {
                Source = assemblies,
                OutputDir = staging,
                UnityBaseLibsDir = Directory.Exists(UnityLibsDir) && Directory.EnumerateFiles(UnityLibsDir, "*.dll").Any() ? UnityLibsDir : null,
                GameAssemblyPath = GameAssembly,
                NoXrefCache = true,
            };
            Il2CppInteropGenerator.Create(opts).AddLogger(new Forward(log)).AddInteropAssemblyGenerator().Run();
            log.LogInfo($"Generated in {sw.Elapsed.TotalSeconds:F1}s");

            SwapIn(log, staging);
            File.WriteAllText(StampPath, Stamp());
            WriteBepInExHash(log);
            log.LogMessage($"Interop assemblies ready ({total.Elapsed.TotalSeconds:F1}s)");
        }

        private static void EnsureUnityLibs(ManualLogSource log)
        {
            if (Directory.Exists(UnityLibsDir) && Directory.EnumerateFiles(UnityLibsDir, "*.dll").Any()) return;
            try
            {
                log.LogInfo("Unity base libraries missing, asking BepInEx to fetch them");
                Manager?.GetMethod("DownloadUnityAssemblies", BindingFlags.Static | BindingFlags.NonPublic)?.Invoke(null, null);
            }
            catch (Exception e)
            {
                log.LogWarning("Couldn't get Unity's base libraries: " + (e.InnerException ?? e).Message +
                               ". Interop will be built without them; some mods may hit MissingMethodException.");
            }
        }

        private static void SwapIn(ManualLogSource log, string staging)
        {
            Directory.CreateDirectory(InteropDir);
            int renamed = 0;
            foreach (var old in Directory.EnumerateFiles(InteropDir, "*.dll").ToList())
            {
                try { File.Delete(old); }
                catch
                {
                    try { File.Move(old, old + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".old"); renamed++; }
                    catch { }
                }
            }
            foreach (var f in Directory.EnumerateFiles(staging))
                File.Copy(f, Path.Combine(InteropDir, Path.GetFileName(f)), true);
            Directory.Delete(staging, true);
            if (renamed > 0) log.LogWarning($"{renamed} old interop files were in use; restart the game once so everything uses the new ones");
        }

        public static void CleanupOld()
        {
            try
            {
                if (!Directory.Exists(InteropDir)) return;
                foreach (var f in Directory.EnumerateFiles(InteropDir, "*.old")) { try { File.Delete(f); } catch { } }
            }
            catch { }
        }

        private static void WriteBepInExHash(ManualLogSource log)
        {
            try
            {
                var compute = Manager?.GetMethod("ComputeHash", BindingFlags.Static | BindingFlags.NonPublic);
                if (compute == null) { log.LogWarning("Couldn't find BepInEx's interop hash; it may try to rebuild on the next start"); return; }
                File.WriteAllText(BepInExHashPath, (string)compute.Invoke(null, null));
            }
            catch (Exception e) { log.LogWarning("Couldn't write BepInEx's interop hash: " + (e.InnerException ?? e).Message); }
        }

        private static void WriteProblems(List<string> problems)
        {
            try
            {
                var path = Path.Combine(Paths.BepInExRootPath, "playrooms-patcher.log");
                File.WriteAllLines(path, problems.Count == 0 ? new[] { "No problems." } : problems);
            }
            catch { }
        }

        private static void WriteDummies(ManualLogSource log, List<AsmResolver.DotNet.AssemblyDefinition> assemblies)
        {
            var dir = Path.Combine(Paths.BepInExRootPath, "dummy");
            Directory.CreateDirectory(dir);
            foreach (var a in assemblies)
            {
                try { a.ManifestModule.Write(Path.Combine(dir, a.ManifestModule.Name)); }
                catch (Exception e) { log.LogWarning($"[dummy] {a.Name}: {e.Message}"); }
            }
            log.LogInfo("Dummy assemblies written to " + dir);
        }

        private sealed class Forward : ILogger
        {
            private readonly ManualLogSource _log;
            public Forward(ManualLogSource log) { _log = log; }
            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => level >= Microsoft.Extensions.Logging.LogLevel.Information;
            public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, EventId id, TState state, Exception e, Func<TState, Exception, string> fmt)
            {
                if (!IsEnabled(level)) return;
                string msg = fmt(state, e);
                if (level >= Microsoft.Extensions.Logging.LogLevel.Error) _log.LogError(msg);
                else if (level == Microsoft.Extensions.Logging.LogLevel.Warning) _log.LogWarning(msg);
                else _log.LogDebug(msg);
            }
        }
    }
}
