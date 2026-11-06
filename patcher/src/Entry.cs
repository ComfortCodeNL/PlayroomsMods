using System;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Preloader.Core.Patching;
using BepInEx.Unity.Common;

namespace PlayroomsPatcher
{
    [PatcherPluginInfo("com.playroomscoop.patcher", "Playrooms Patcher", Version)]
    public class Entry : BasePatcher
    {
        public const string Version = "1.0.0";

        // runs before BepInEx loads the interop assemblies, so a fresh build is used straight away
        public Entry()
        {
            var force = Config.Bind("Interop", "ForceRegenerate", false, "Rebuild the interop assemblies on the next start. Turns itself off afterwards.");
            var dummies = Config.Bind("Interop", "WriteDummyAssemblies", false, "Also write the metadata-only assemblies to BepInEx\\dummy (for reading the game's types in ILSpy).");
            try
            {
                if (UnityInfo.Version.Major < 6000) { Log.LogInfo("Unity " + UnityInfo.Version + ": nothing to do"); return; }
                string why = ClassLayout.Verify();
                if (why != null) { Log.LogError("Il2CppClass layout check failed (" + why + "); not changing anything. Is this a different game version?"); return; }
                ClassHandler.Install();
                Log.LogInfo("Il2CppClass layout for Unity " + UnityInfo.Version + " installed");

                InteropBuild.CleanupOld();
                if (!force.Value && InteropBuild.IsUpToDate()) { Log.LogInfo("Interop assemblies are up to date"); return; }
                force.Value = false;
                InteropBuild.Build(Log, dummies.Value);
            }
            catch (Exception e)
            {
                Log.LogError("Failed: " + e);
            }
        }
    }
}
