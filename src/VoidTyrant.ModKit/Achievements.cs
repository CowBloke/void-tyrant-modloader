using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;

namespace VoidTyrant.ModKit
{
    /// <summary>
    /// Stops Steam achievements and stats from updating while cheats or gameplay-changing mods are active.
    ///
    /// The game unlocks achievements through ServicesManager.ReportAchievement and uploads lifetime stats
    /// (some achievements are stat-based) from PlayerProfile.SyncSteamStats. Both end in Steamworks'
    /// SteamUserStats, so that is the one place we patch.
    /// </summary>
    public static class Achievements
    {
        static readonly List<string> reasons = new List<string>();
        static ConfigEntry<bool> disableAll;

        /// <summary>True when Steam achievements and stats are currently being blocked.</summary>
        public static bool Blocked => disableAll.Value || reasons.Count > 0;

        /// <summary>Why achievements are blocked for this session (not including the manual setting).</summary>
        public static IReadOnlyList<string> Reasons => reasons;

        internal static ConfigEntry<bool> DisableSetting => disableAll;

        /// <summary>
        /// Blocks achievements until the game is restarted. Code mods that give the player an
        /// advantage should call this from Awake, or when their cheat is switched on.
        /// </summary>
        public static void Block(string reason)
        {
            if (reasons.Contains(reason)) return;
            reasons.Add(reason);
            ModKitPlugin.Log.LogInfo($"Steam achievements disabled until restart: {reason}");
        }

        internal static void Init(ConfigFile config, Harmony harmony)
        {
            disableAll = config.Bind("Achievements", "DisableAchievements", false,
                "Never unlock Steam achievements or update Steam stats while the ModKit is installed. " +
                "Achievements are always blocked while the debug panel or a gameplay-changing mod is active.");

            var prefix = new HarmonyMethod(typeof(Achievements), nameof(Prefix));
            int patched = 0;
            foreach (var method in typeof(SteamUserStats).GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.Name == nameof(SteamUserStats.SetAchievement) || method.Name == nameof(SteamUserStats.SetStat)
                    || method.Name == nameof(SteamUserStats.IndicateAchievementProgress)
                    || method.Name == nameof(SteamUserStats.UpdateAvgRateStat))
                {
                    harmony.Patch(method, prefix);
                    patched++;
                }
            }
            ModKitPlugin.Log.LogInfo($"Achievement guard active on {patched} Steam methods; achievements {(Blocked ? "off" : "on")}");
        }

        // Pretend the call failed; the game ignores these return values.
        static bool Prefix(MethodBase __originalMethod, object[] __args, ref bool __result)
        {
            if (!Blocked) return true;
            if (__originalMethod.Name == nameof(SteamUserStats.SetAchievement))
                ModKitPlugin.Log.LogInfo($"Blocked Steam achievement '{__args[0]}' ({Describe()})");
            __result = false;
            return false;
        }

        internal static string Describe() =>
            reasons.Count > 0 ? string.Join("; ", reasons) : disableAll.Value ? "disabled in settings" : "allowed";
    }
}
