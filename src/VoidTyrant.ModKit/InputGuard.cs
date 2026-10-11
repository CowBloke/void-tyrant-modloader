using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine.EventSystems;

namespace VoidTyrant.ModKit
{
    /// <summary>
    /// IMGUI windows do not consume uGUI or the game's Input polling, so clicks on a mod window
    /// also reach the game underneath. Mods register a condition here to stop the game's input
    /// handling while it is true.
    /// </summary>
    public static class InputGuard
    {
        static readonly List<Func<bool>> blockers = new List<Func<bool>>();

        public static void AddBlocker(Func<bool> isBlocking) => blockers.Add(isBlocking);

        public static bool Blocking => blockers.Any(b => b());

        internal static void Init(Harmony harmony)
        {
            var prefix = new HarmonyMethod(typeof(InputGuard), nameof(Prefix));
            foreach (var type in new[] { typeof(EventSystem), typeof(GameManager), typeof(Dialog),
                typeof(TraverseManager), typeof(ControllerManager), typeof(Map), typeof(Hub) })
            {
                var update = type.GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (update != null) harmony.Patch(update, prefix);
            }
        }

        static bool Prefix() => !Blocking;
    }
}
