using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoidTyrant.ModKit;

namespace VoidTyrant.DebugPanel
{
    [BepInPlugin(Guid, "Void Tyrant Debug Panel", "1.0.0")]
    [BepInDependency(ModKitPlugin.Guid)]
    public sealed class DebugPanelPlugin : BaseUnityPlugin
    {
        public const string Guid = "voidtyrant.debugpanel";
        const string AchievementReason = "debug panel was turned on";
        static readonly string[] Tabs = { "Worlds", "Stats", "Cards", "Perks", "Combat" };
        readonly Queue<Action> actions = new Queue<Action>();
        readonly Dictionary<string, string> statInputs = new Dictionary<string, string>();
        ConfigEntry<KeyboardShortcut> toggleKey;
        ConfigEntry<bool> panelEnabled;
        bool visible, catalogReady, failed, drawNext = true, ownedOnly;
        float previousTimeScale, inputReleaseAt;
        bool previousCursorVisible;
        CursorLockMode previousCursorLock;
        int tab;
        Rect window = new Rect(20, 20, 720, 560);
        Vector2 scroll;
        string search = "", quantity = "1", message = "Start a run to use debug actions.";
        CatalogEntry<string> pendingWorld;
        List<CatalogEntry<string>> worlds = new List<CatalogEntry<string>>();
        List<CatalogEntry<SpecialCard.CardType>> cards = new List<CatalogEntry<SpecialCard.CardType>>();
        List<CatalogEntry<Perk.PerkType>> perks = new List<CatalogEntry<Perk.PerkType>>();

        void Awake()
        {
            toggleKey = Config.Bind("Panel", "ToggleKey", new KeyboardShortcut(KeyCode.F2), "Open or close the debug panel. Escape also closes it.");
            panelEnabled = Config.Bind("Panel", "Enabled", false,
                "Allow the debug panel to open. Turning it on disables Steam achievements until the game restarts.");
            ModKitPlugin.AddToggle("Debug panel (" + toggleKey.Value + ") - cheats, turns achievements off", panelEnabled);
            panelEnabled.SettingChanged += (_, __) => ApplyEnabled();
            InputGuard.AddBlocker(() => visible || Time.realtimeSinceStartup < inputReleaseAt);
            SceneManager.sceneLoaded += SceneLoaded;
            ApplyEnabled();
        }

        void ApplyEnabled()
        {
            if (panelEnabled.Value)
            {
                Achievements.Block(AchievementReason);
                Logger.LogInfo("Debug panel enabled. Press " + toggleKey.Value + " to open.");
            }
            else
            {
                Close();
                Logger.LogInfo("Debug panel disabled. Turn it on in the ModKit menu (F1).");
            }
        }

        void SceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Close();
            pendingWorld = null;
            statInputs.Clear();
        }

        void Update()
        {
            if (!panelEnabled.Value) return;
            if (toggleKey.Value.IsDown())
            {
                if (visible) Close(); else Open();
            }
            else if (visible && Input.GetKeyDown(KeyCode.Escape)) Close();
            while (actions.Count > 0) actions.Dequeue()();
        }

        void Open()
        {
            statInputs.Clear();
            previousTimeScale = Time.timeScale;
            previousCursorVisible = Cursor.visible;
            previousCursorLock = Cursor.lockState;
            visible = true;
            Time.timeScale = 0;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (!catalogReady && Reg.instance != null)
                Run("Catalog loaded", () =>
                {
                    cards = DebugActions.Cards();
                    perks = DebugActions.Perks();
                    worlds = DebugActions.Worlds();
                    catalogReady = true;
                    Logger.LogInfo("Catalog: " + worlds.Count + " worlds, " + cards.Count + " cards, " + perks.Count + " perks.");
                });
        }

        void Close()
        {
            if (!visible) return;
            visible = false;
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            inputReleaseAt = Time.realtimeSinceStartup + 0.2f;
        }

        void OnDisable() { Close(); }
        void OnDestroy()
        {
            Close();
            SceneManager.sceneLoaded -= SceneLoaded;
        }

        void Run(string success, Action action)
        {
            try { action(); message = success; failed = false; Logger.LogInfo(success); }
            catch (Exception e) { message = e.Message; failed = true; Logger.LogError(e); }
        }

        void Queue(string success, Action action) => actions.Enqueue(() => Run(success, action));

        void OnGUI()
        {
            if (!visible) return;
            int previousDepth = GUI.depth;
            try
            {
                GUI.depth = -1000;
                window.width = Mathf.Min(720, Screen.width - 20);
                window.height = Mathf.Min(560, Screen.height - 20);
                window.x = Mathf.Clamp(window.x, 0, Mathf.Max(0, Screen.width - window.width));
                window.y = Mathf.Clamp(window.y, 0, Mathf.Max(0, Screen.height - window.height));
                // Same default GUILayout.Window / GUI.skin as the ModKit's F1 overlay.
                window = GUILayout.Window(GetInstanceID(), window, DrawWindow, "Void Tyrant Debug Panel");
            }
            finally { GUI.depth = previousDepth; }
        }

        void DrawWindow(int id)
        {
            var hero = DebugActions.ActiveHero();
            GUILayout.BeginHorizontal();
            GUILayout.Label(hero == null ? "No active run. Start or continue a run to enable actions."
                : "Paused | " + hero.characterName + " | HP " + hero.health + "/" + hero.GetNormalizedMaxHealth()
                + " | EP " + hero.energy + "/" + hero.GetNormalizedMaxEnergy() + " | Gold " + hero.money + " | Level " + hero.level);
            if (GUILayout.Button("Close", GUILayout.Width(60))) actions.Enqueue(Close);
            GUILayout.EndHorizontal();
            int nextTab = GUILayout.Toolbar(tab, Tabs);
            if (nextTab != tab)
            {
                tab = nextTab; search = ""; scroll = Vector2.zero; pendingWorld = null; ownedOnly = false;
                GUI.FocusControl(null);
            }
            GUILayout.Label(TabDescription());
            bool searchable = tab == 0 || tab == 2 || tab == 3;
            if (searchable) DrawSearch();
            float extra = searchable ? (tab == 2 ? 65 : 35) : 0;
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Max(100, window.height - 160 - extra)));
            switch (tab)
            {
                case 0: DrawWorlds(hero); break;
                case 1: DrawStats(hero); break;
                case 2: DrawCards(hero); break;
                case 3: DrawPerks(hero); break;
                case 4: DrawCombat(hero); break;
            }
            GUILayout.EndScrollView();
            GUILayout.Label((failed ? "Error: " : "") + message);
            GUILayout.Label("Changes may be saved by the game. " + toggleKey.Value + " / Escape to close. Drag to move.");
            GUI.DragWindow();
        }

        string TabDescription()
        {
            switch (tab)
            {
                case 0: return "Travel to any world, including later tiers and Chaos destinations.";
                case 1: return "Edit base attributes; equipment and status effects still contribute bonuses.";
                case 2: return "Add cards to your deck and current draw pile.";
                case 3: return "Apply perks with their full effects. Active perks cannot be added twice.";
                default: return "Restore resources, clear effects, and control the current encounter.";
            }
        }

        void DrawSearch()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(45));
            search = GUILayout.TextField(search);
            if (tab == 2 || tab == 3) ownedOnly = GUILayout.Toggle(ownedOnly, tab == 2 ? "In deck" : "Active", GUILayout.Width(75));
            if (GUILayout.Button("Clear", GUILayout.Width(50))) search = "";
            GUILayout.EndHorizontal();
            if (tab == 2)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Copies (1-99)", GUILayout.Width(90));
                quantity = GUILayout.TextField(quantity, 2, GUILayout.Width(45));
                drawNext = GUILayout.Toggle(drawNext, "Draw next", GUILayout.Width(95));
                GUILayout.Label("Available on the next hand refill.");
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(5);
        }

        void DrawWorlds(Hero hero)
        {
            if (pendingWorld != null)
            {
                var world = pendingWorld;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Travel to " + world.Name + "?");
                GUILayout.Label("Replaces this encounter and sets progression to " + world.Detail + ". Stats, deck, equipment and perks carry over; health refills on arrival.");
                GUILayout.BeginHorizontal();
                ActionButton("Teleport", hero != null, () =>
                {
                    Close();
                    DebugActions.Teleport(world);
                    pendingWorld = null;
                }, "Traveled to " + world.Name);
                if (GUILayout.Button("Cancel")) pendingWorld = null;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            int found = 0;
            foreach (var world in worlds.Where(w => w.Matches(search)))
            {
                found++;
                GUILayout.BeginHorizontal(GUI.skin.box);
                EntryLabel(world.Name, world.Detail + " | " + world.Id);
                bool enabled = GUI.enabled; GUI.enabled = enabled && hero != null;
                if (GUILayout.Button("Travel", GUILayout.Width(65))) pendingWorld = world;
                GUI.enabled = enabled;
                GUILayout.EndHorizontal();
            }
            Empty(found);
        }

        void DrawStats(Hero hero)
        {
            GUILayout.BeginHorizontal();
            ActionButton("Restore HP + EP", hero != null, () => DebugActions.Restore(true, true), "Health and energy restored");
            ActionButton("Save run", hero != null && !Reg.instance.isTutorialRun, DebugActions.SaveRun, "Run saved");
            GUILayout.EndHorizontal();
            GUILayout.Space(5);
            foreach (var stat in DebugActions.Stats)
            {
                int current = hero != null ? stat.Read(hero) : 0;
                if (!statInputs.ContainsKey(stat.Name)) statInputs[stat.Name] = current.ToString(CultureInfo.InvariantCulture);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(stat.Name, GUILayout.Width(155));
                GUILayout.Label(hero == null ? "--" : current.ToString(), GUILayout.Width(55));
                statInputs[stat.Name] = GUILayout.TextField(statInputs[stat.Name], 6, GUILayout.Width(80));
                string input = statInputs[stat.Name];
                ActionButton("Set", hero != null, () =>
                {
                    if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                        throw new InvalidOperationException("Enter a whole number for " + stat.Name + ".");
                    DebugActions.SetStat(stat, value);
                }, stat.Name + " updated", 50);
                ActionButton("+1", hero != null, () => IncreaseStat(stat, 1), stat.Name + " increased", 40);
                ActionButton("+10", hero != null, () => IncreaseStat(stat, 10), stat.Name + " increased", 45);
                GUILayout.EndHorizontal();
            }
        }

        void IncreaseStat(Stat stat, int amount)
        {
            int value = Mathf.Min(stat.Max, stat.Read(DebugActions.RequireHero()) + amount);
            DebugActions.SetStat(stat, value);
            statInputs[stat.Name] = value.ToString(CultureInfo.InvariantCulture);
        }

        void DrawCards(Hero hero)
        {
            int found = 0;
            var owned = hero == null ? new Dictionary<SpecialCard.CardType, int>()
                : hero.deck.Where(c => c != null).GroupBy(c => c.type).ToDictionary(g => g.Key, g => g.Count());
            foreach (var card in cards.Where(c => c.Matches(search)))
            {
                owned.TryGetValue(card.Id, out int count);
                if (ownedOnly && count == 0) continue;
                found++;
                GUILayout.BeginHorizontal(GUI.skin.box);
                EntryLabel(card.Name, card.Detail + " | " + card.Id);
                GUILayout.Label(count + " in deck", GUILayout.Width(70));
                string copies = quantity; bool next = drawNext;
                ActionButton("Give", hero != null, () =>
                {
                    if (!int.TryParse(copies, out int n)) throw new InvalidOperationException("Enter a quantity from 1 to 99.");
                    DebugActions.GiveCard(card.Id, n, next);
                }, "Added " + copies + " x " + card.Name, 60);
                GUILayout.EndHorizontal();
            }
            Empty(found);
        }

        void DrawPerks(Hero hero)
        {
            int found = 0;
            foreach (var perk in perks.Where(p => p.Matches(search)))
            {
                bool active = hero != null && hero.activePerks.Contains(perk.Id);
                if (ownedOnly && !active) continue;
                found++;
                GUILayout.BeginHorizontal(GUI.skin.box);
                EntryLabel(perk.Name, perk.Detail + " | " + perk.Id);
                bool canAdd = hero != null && !active && (perk.Id != Perk.PerkType.QuickWitted || hero.handSize < 5);
                ActionButton(active ? "Active" : "Apply", canAdd, () => DebugActions.GivePerk(perk.Id), "Applied " + perk.Name, 65);
                GUILayout.EndHorizontal();
            }
            Empty(found);
        }

        void DrawCombat(Hero hero)
        {
            var enemy = hero != null ? hero.gameManager.enemy : null;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(enemy == null ? "No enemy in this room" : "Enemy: " + DebugActions.Humanize(enemy.name.Replace("(Clone)", "")));
            if (hero != null) GUILayout.Label("State: " + hero.gameManager.currentState.ToString().Replace("GAME_STATE_", "")
                + (enemy == null ? "" : " | HP " + enemy.health + "/" + enemy.GetNormalizedMaxHealth()));
            ActionButton("Defeat current enemy", enemy != null && !enemy.dead, DebugActions.KillEnemy, "Enemy health set to zero; close the panel to continue");
            GUILayout.EndVertical();
            GUILayout.Space(5);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Hero resources");
            GUILayout.BeginHorizontal();
            ActionButton("Full health", hero != null, () => DebugActions.Restore(true, false), "Health restored");
            ActionButton("Full energy", hero != null, () => DebugActions.Restore(false, true), "Energy restored");
            GUILayout.EndHorizontal();
            ActionButton("Clear all status effects", hero != null, DebugActions.ClearConditions, "Hero status effects cleared");
            GUILayout.EndVertical();
            GUILayout.Space(5);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Test boost: +100 max HP / EP, +10 melee / ranged / magic, +1,000 gold.");
            ActionButton("Boost hero", hero != null, () =>
            {
                foreach (var stat in DebugActions.Stats.Take(5)) IncreaseStat(stat, stat.Name.StartsWith("Max") ? 100 : 10);
                IncreaseStat(DebugActions.Stats.First(s => s.Name == "Gold"), 1000);
                DebugActions.Restore(true, true);
            }, "Test stats applied and resources restored");
            GUILayout.EndVertical();
        }

        void EntryLabel(string name, string detail)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(name);
            GUILayout.Label(detail);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
        }

        void Empty(int count)
        {
            if (count == 0) GUILayout.Label(catalogReady ? "No matching entries. Try a different search." : "Reopen the panel after the game finishes loading.");
        }

        void ActionButton(string label, bool enabled, Action action, string success, float width = 0)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool pressed = width > 0 ? GUILayout.Button(label, GUILayout.Width(width)) : GUILayout.Button(label);
            GUI.enabled = previous;
            if (pressed) Queue(success, action);
        }
    }
}
