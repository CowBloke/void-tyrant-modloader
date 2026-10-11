using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace VoidTyrant.ModKit
{
    /// <summary>
    /// The Void Tyrant mod loader. Runs as a BepInEx plugin and loads content mods from &lt;GameDir&gt;\Mods.
    /// Code mods are ordinary BepInEx plugins; they can depend on this one to read <see cref="Mods"/>.
    /// </summary>
    [BepInPlugin(Guid, "Void Tyrant ModKit", Version)]
    public class ModKitPlugin : BaseUnityPlugin
    {
        public const string Guid = "voidtyrant.modkit";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        /// <summary>Folder that content mods live in.</summary>
        public static string ModsDirectory { get; private set; }

        /// <summary>Every content mod found, in load order (including disabled ones).</summary>
        public static IReadOnlyList<ContentMod> Mods { get; private set; } = new List<ContentMod>();

        /// <summary>Raised once all content mods have been applied.</summary>
        public static event Action ModsApplied;

        sealed class Toggle { public string Label; public ConfigEntry<bool> Entry; }
        static readonly List<Toggle> toggles = new List<Toggle>();

        /// <summary>Shows an on/off setting in the Settings section of the F1 menu.</summary>
        public static void AddToggle(string label, ConfigEntry<bool> entry) =>
            toggles.Add(new Toggle { Label = label, Entry = entry });

        ConfigEntry<KeyboardShortcut> overlayKey;
        ConfigEntry<bool> writeReference;

        bool showOverlay;
        Vector2 scroll;
        Rect window = new Rect(20, 20, 520, 420);

        void Awake()
        {
            Log = Logger;
            overlayKey = Config.Bind("General", "OverlayKey", new KeyboardShortcut(KeyCode.F1),
                "Toggles the in-game list of loaded mods.");
            writeReference = Config.Bind("General", "WriteReferenceFiles", true,
                "On launch, write Mods/_reference/cards.json listing every card's unmodded data.");

            ModsDirectory = Path.Combine(Paths.GameRootPath, "Mods");
            Directory.CreateDirectory(ModsDirectory);
            Log.LogInfo($"Void Tyrant {Application.version}, ModKit {Version}");

            var harmony = new Harmony(Guid);
            Achievements.Init(Config, harmony);
            AddToggle("Disable Steam achievements", Achievements.DisableSetting);
            InputGuard.Init(harmony);
            InputGuard.AddBlocker(() => showOverlay && window.Contains(
                new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)));
        }

        // Start (not Awake) so the game's own startup has run before we touch its assets.
        void Start()
        {
            if (writeReference.Value)
            {
                try
                {
                    string path = Path.Combine(ModsDirectory, "_reference", "cards.json");
                    int count = CardPatcher.DumpReference(path);
                    Log.LogInfo($"Wrote reference data for {count} cards to {path}");
                }
                catch (Exception e)
                {
                    Log.LogWarning($"Could not write card reference: {e}");
                }
            }

            var mods = ContentMod.LoadAll(ModsDirectory);
            CardPatcher.Apply(mods);
            Mods = mods;
            foreach (var mod in mods.Where(m => m.ChangesGameplay))
                Achievements.Block($"{mod.Name} changes card stats");

            Log.LogInfo($"Loaded {mods.Count(m => m.Enabled)} content mod(s) from {ModsDirectory}");
            foreach (var mod in mods)
            {
                string state = mod.Enabled ? $"{mod.CardsPatched} card(s) patched" : "disabled";
                Log.LogInfo($"  {mod} - {state}");
                foreach (var err in mod.Errors) Log.LogError($"  [{mod.Id}] {err}");
            }

            try { ModsApplied?.Invoke(); }
            catch (Exception e) { Log.LogError($"ModsApplied handler threw: {e}"); }
        }

        void Update()
        {
            if (overlayKey.Value.IsDown()) showOverlay = !showOverlay;
        }

        void OnGUI()
        {
            if (!showOverlay) return;
            window = GUILayout.Window(GetHashCode(), window, DrawWindow, $"Void Tyrant ModKit {Version}  (game {Application.version})");
        }

        void DrawWindow(int id)
        {
            var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos.Values
                .Where(p => p.Metadata.GUID != Guid)
                .OrderBy(p => p.Metadata.Name)
                .ToList();

            scroll = GUILayout.BeginScrollView(scroll);

            GUILayout.Label("<b>Settings</b>");
            foreach (var t in toggles)
            {
                bool value = GUILayout.Toggle(t.Entry.Value, "  " + t.Label);
                if (value != t.Entry.Value) t.Entry.Value = value;
            }
            GUILayout.Label(Achievements.Blocked
                ? $"  Achievements: <color=orange>off</color> ({Achievements.Describe()})"
                : "  Achievements: <color=lime>on</color>");
            if (Achievements.Reasons.Count > 0)
                GUILayout.Label("  <i>Restart the game to turn achievements back on.</i>");

            GUILayout.Space(8);
            GUILayout.Label($"<b>Content mods</b>  ({ModsDirectory})");
            if (Mods.Count == 0) GUILayout.Label("  none - add a folder to Mods/");
            foreach (var mod in Mods)
            {
                string state = !mod.Enabled ? "<color=grey>disabled</color>"
                    : mod.Errors.Count > 0 ? $"<color=orange>{mod.Errors.Count} error(s)</color>"
                    : "<color=lime>ok</color>";
                GUILayout.Label($"  {mod.Name} <i>{mod.Version}</i> by {mod.Author} - {state}, {mod.CardsPatched} card(s)");
                foreach (var err in mod.Errors) GUILayout.Label($"      <color=orange>{err}</color>");
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Code mods</b> (BepInEx plugins)");
            if (plugins.Count == 0) GUILayout.Label("  none");
            foreach (var p in plugins) GUILayout.Label($"  {p.Metadata.Name} <i>{p.Metadata.Version}</i>");

            GUILayout.EndScrollView();
            GUILayout.Label($"<i>{overlayKey.Value} to close. Drag to move.</i>");
            GUI.DragWindow();
        }
    }
}
