using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ComfortCode.Playrooms.ShiftHud
{
    // Optional, explicitly requested save-modifying hotkeys. No network packets,
    // scene-light changes, shift changes, or ball spot mutations.
    internal static class CheatHotkeys
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

        private const string CurrencyId = "balls";
        private const int TenThousandEurosCents = 1000000;
        private static readonly bool[] WasDown = new bool[4];
        private static float _nextError;
        internal static string Message { get; private set; } = "";
        internal static float MessageUntil { get; private set; }

        internal static void Tick()
        {
            if (Plugin.EnableAltCheats == null || !Plugin.EnableAltCheats.Value)
            {
                Array.Clear(WasDown, 0, WasDown.Length);
                return;
            }
            try
            {
                uint pid;
                var foreground = GetForegroundWindow();
                if (foreground == IntPtr.Zero ||
                    GetWindowThreadProcessId(foreground, out pid) == 0 ||
                    pid != (uint)Process.GetCurrentProcess().Id)
                {
                    Array.Clear(WasDown, 0, WasDown.Length);
                    return;
                }

                bool alt = Down(0x12); // VK_MENU, either Alt
                for (int i = 0; i < WasDown.Length; i++)
                {
                    bool pressed = alt && Down(0x34 + i); // Alt+4...Alt+7
                    if (pressed && !WasDown[i])
                    {
                        switch (i)
                        {
                            case 0: BestVacuum(); break;
                            case 1: AllSkills(); break;
                            case 2: AddMoney(); break;
                            case 3: AllKeys(); break;
                        }
                    }
                    WasDown[i] = pressed;
                }
            }
            catch (Exception e)
            {
                if (Time.realtimeSinceStartup < _nextError) return;
                _nextError = Time.realtimeSinceStartup + 15f;
                Plugin.Logger?.LogWarning("[ShiftHUD] ALT hotkeys unavailable: " + e);
            }
        }

        private static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

        private static void Status(string text)
        {
            Message = text;
            MessageUntil = Time.realtimeSinceStartup + 5f;
            Plugin.Logger?.LogInfo("[ShiftHUD] " + text);
        }

        private static void Failure(string action, Exception e)
        {
            Status(action + ": niet volledig uitgevoerd; zie BepInEx-log.");
            Plugin.Logger?.LogWarning("[ShiftHUD] " + action + ": " + e);
        }

        private static object Ready(string type)
        {
            var service = GameBridge.First(type);
            if (service == null || !GameBridge.Bool(service, "IsInitialized", false))
                throw new InvalidOperationException(type + " is nog niet geïnitialiseerd; laad een savegame.");
            return service;
        }

        private static object Call(object owner, string name, params object[] args)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var method in owner.GetType().GetMethods(flags))
            {
                if (method.Name != name || method.GetParameters().Length != args.Length) continue;
                var parameters = method.GetParameters();
                var converted = new object[args.Length];
                bool compatible = true;
                for (int i = 0; i < args.Length; i++)
                {
                    object value = args[i];
                    Type target = parameters[i].ParameterType;
                    try
                    {
                        if (target.IsEnum && value is IConvertible)
                            value = Enum.ToObject(target, Convert.ToInt32(value));
                        else if (value != null && !target.IsInstanceOfType(value))
                            value = Convert.ChangeType(value, target);
                        converted[i] = value;
                    }
                    catch { compatible = false; break; }
                }
                if (compatible) return method.Invoke(owner, converted);
            }
            throw new MissingMethodException(owner.GetType().Name, name + "/" + args.Length);
        }

        private static string StaticId(string className, string name)
        {
            Type type = GameBridge.Type(className) ?? throw new TypeLoadException(className);
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            object value = type.GetProperty(name, flags)?.GetValue(null) ??
                           type.GetField(name, flags)?.GetValue(null);
            string id = value as string;
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException(name + " ID ontbreekt.");
            return id;
        }

        private static IEnumerable<(object key, object value)> Pairs(object map)
        {
            if (map == null) yield break;
            if (map is IDictionary plain)
            {
                foreach (DictionaryEntry item in plain) yield return (item.Key, item.Value);
                yield break;
            }
            // IL2CPP generic dictionary is not a CLR IDictionary.
            object enumerator = Call(map, "GetEnumerator");
            int safety = 5000;
            while (--safety > 0 && Convert.ToBoolean(Call(enumerator, "MoveNext")))
            {
                object pair = GameBridge.Read(enumerator, "Current");
                yield return (GameBridge.Read(pair, "Key"), GameBridge.Read(pair, "Value"));
            }
        }

        private static IEnumerable<object> Items(object collection)
        {
            if (collection == null) yield break;
            if (collection is IEnumerable plain)
            {
                foreach (object item in plain) yield return item;
                yield break;
            }
            int count = GameBridge.Int(collection, "Count", GameBridge.Int(collection, "Length", -1));
            if (count < 0 || count > 10000) yield break;
            for (int i = 0; i < count; i++) yield return Call(collection, "get_Item", i);
        }

        private static int MaxLevels(bool vacuumOnly)
        {
            object skills = Ready("TwoPixels.Services.Skill.SkillService");
            object definitions = GameBridge.Read(skills, "Skills");
            object saveData = GameBridge.Read(skills, "SaveData");
            object levels = GameBridge.Read(saveData, "SkillLevels");
            if (definitions == null || levels == null)
                throw new InvalidOperationException("Skills/SaveData.SkillLevels ontbreken.");

            var catalog = new List<(string id, object def, int current, int max)>();
            foreach (var item in Pairs(definitions))
            {
                string id = item.key?.ToString();
                if (string.IsNullOrEmpty(id) || item.value == null) continue;
                int max = GameBridge.Int(item.value, "MaxLevel", -1);
                if (max <= 0 || max > 1000) continue;
                int current = Convert.ToInt32(Call(skills, "GetSkillLevel", id));
                catalog.Add((id, item.value, current, max));
            }
            if (catalog.Count == 0) throw new InvalidOperationException("Geen bekende skilldefinities.");
            var selected = new List<(string id, int current, int max)>();
            foreach (var item in catalog)
            {
                if (vacuumOnly &&
                    item.id.IndexOf("vacuum", StringComparison.OrdinalIgnoreCase) < 0 &&
                    item.id.IndexOf("suction", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (item.current < item.max) selected.Add((item.id, item.current, item.max));
            }
            if (selected.Count == 0) return 0;

            // Rebuild from definitions once. If any step fails, restore the
            // previous levels and replay original effects where possible.
            try
            {
                foreach (var item in catalog)
                    if (item.current > 0) Call(skills, "RemoveSkillTemplates", item.def);
                foreach (var item in selected)
                    Call(levels, "set_Item", item.id, item.max);
                Call(skills, "ReplayUnlockedSkills");
                return selected.Count;
            }
            catch (Exception failed)
            {
                try
                {
                    foreach (var item in selected)
                        Call(levels, "set_Item", item.id, item.current);
                    Call(skills, "ReplayUnlockedSkills");
                    Plugin.Logger?.LogWarning("[ShiftHUD] Skill-level changes rolled back after an exception.");
                }
                catch (Exception rollback)
                {
                    Plugin.Logger?.LogError("[ShiftHUD] SKILL ROLLBACK FAILED. Restore your save backup: " + rollback);
                }
                throw new InvalidOperationException("Skill upgrade was not completed; original values restored where possible.", failed);
            }
        }

        private static void AllSkills()
        {
            try
            {
                int upgraded = MaxLevels(false);
                Status("ALT+5: " + upgraded + " skills tot maximaal niveau bijgewerkt.");
            }
            catch (Exception e) { Failure("ALT+5 alle skills", e); }
        }

        private static void BestVacuum()
        {
            try
            {
                object stats = Ready("TwoPixels.Services.Statistic.StatisticService");
                // This ID is a real property of TwoPixels.Player.ToolIds.
                string purchase = StaticId("TwoPixels.Player.ToolIds", "HandVacuumPurchaseStatistic");
                Call(stats, "SetStatistic", purchase, 1);

                bool equipped = false;
                // ToolBehaviour is a managed gameplay behaviour inside the player
                // controller, not necessarily a discoverable Unity MonoBehaviour.
                object player = GameBridge.First("TwoPixels.Player.PersistentPlayer");
                object controller = GameBridge.Read(player, "PlayerBehaviourController");
                foreach (var behaviour in Items(GameBridge.Read(controller, "Behaviours")))
                {
                    if (behaviour == null ||
                        !behaviour.GetType().Name.Contains("ToolBehaviour")) continue;
                    Call(behaviour, "RebuildAvailableTools");
                    Call(behaviour, "EquipToolForStatistic", purchase);
                    equipped = true;
                    break;
                }
                int upgrades = MaxLevels(true);
                Status(equipped
                    ? "ALT+4: handstofzuiger uitgerust; " + upgrades + " vacuumskills geüpgraded."
                    : "ALT+4: handstofzuiger ontgrendeld; uitrusten niet bevestigd.");
            }
            catch (Exception e) { Failure("ALT+4 handstofzuiger", e); }
        }

        private static void AddMoney()
        {
            try
            {
                object skills = Ready("TwoPixels.Services.Skill.SkillService");
                int before = Convert.ToInt32(Call(skills, "GetCurrencyBalance", CurrencyId));
                if (before > int.MaxValue - TenThousandEurosCents)
                    throw new OverflowException("Geldsaldo zou de maximumwaarde overschrijden.");
                // Playrooms uses 'balls', with amounts in cents.
                Call(skills, "GrantCurrency", CurrencyId, TenThousandEurosCents, 0);
                int after = Convert.ToInt32(Call(skills, "GetCurrencyBalance", CurrencyId));
                if (after - before != TenThousandEurosCents)
                    throw new InvalidOperationException("Saldo is niet met exact 1.000.000 cent gestegen.");
                Status("ALT+6: €10.000,00 bijgeschreven.");
            }
            catch (Exception e) { Failure("ALT+6 geld", e); }
        }

        private static void AllKeys()
        {
            try
            {
                object stats = Ready("TwoPixels.Services.Statistic.StatisticService");
                const string type = "TwoPixels.Interactables.DoorKeyIds";
                string[] keyFields = {
                    "PlayroomLeft1", "PlayroomLeft2", "PlayroomLeft3",
                    "PlayroomRight1", "PlayroomRight2", "PlayroomRight3"
                };
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string field in keyFields) ids.Add(StaticId(type, field));
                if (ids.Count != 6) throw new InvalidOperationException("Niet alle zes kamersleutels gevonden.");
                foreach (string id in ids) Call(stats, "SetStatistic", id, 1);
                Status("ALT+7: zes kamersleutels ontgrendeld.");
            }
            catch (Exception e) { Failure("ALT+7 kamersleutels", e); }
        }
    }
}
