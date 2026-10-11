using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VoidTyrant.DebugPanel
{
    internal sealed class CatalogEntry<T>
    {
        public T Id;
        public string Name;
        public string Detail;
        public int Stage;
        public bool Matches(string query) => string.IsNullOrWhiteSpace(query)
            || (Name + " " + Id + " " + Detail).IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal sealed class Stat
    {
        public string Name;
        public int Min, Max;
        public Func<Hero, int> Read;
        public Action<Hero, int> Write;

        public Stat(string name, Func<Hero, int> read, Action<Hero, int> write, int min = 0, int max = 99999)
        {
            Name = name; Read = read; Write = write; Min = min; Max = max;
        }
    }

    // Keep mutations here so the UI cannot bypass readiness checks or the game's reward methods.
    internal static class DebugActions
    {
        public static readonly Stat[] Stats =
        {
            new Stat("Max health", h => h.maxHealth, (h, v) => h.maxHealth = v, 1),
            new Stat("Max energy", h => h.maxEnergy, (h, v) => h.maxEnergy = v, 1),
            new Stat("Melee damage", h => h.meleeDamage, (h, v) => h.meleeDamage = v),
            new Stat("Ranged damage", h => h.rangedDamage, (h, v) => h.rangedDamage = v),
            new Stat("Magic damage", h => h.magicDamage, (h, v) => h.magicDamage = v),
            new Stat("Armor", h => h.armor, (h, v) => h.armor = v),
            new Stat("Defense", h => h.defense, (h, v) => h.defense = v, 0, 11),
            new Stat("Luck", h => h.luck, (h, v) => h.luck = v),
            new Stat("Gold", h => h.money, (h, v) => h.AddMoney(v - h.money, save: false), 0, 999999),
            new Stat("Weapon speed bonus", h => h.weaponSpeedBonus, (h, v) => h.weaponSpeedBonus = v, 0, 20)
        };

        // Do not use Reg.Hero / Reg.GameManager for readiness: their getters spawn objects.
        public static Hero ActiveHero()
        {
            if (SceneManager.GetActiveScene().name != "Game" || Reg.instance == null) return null;
            var manager = UnityEngine.Object.FindObjectOfType<GameManager>();
            var hero = manager != null ? manager.hero : null;
            return manager != null && manager.enabled && hero != null && hero.characterInitialized && !hero.dead && hero.weapon != null
                && hero.deck != null && hero.HandCardDeck != null && manager.hand != null ? hero : null;
        }

        public static Hero RequireHero()
        {
            var hero = ActiveHero();
            if (hero == null) throw new InvalidOperationException("Start or continue a run, and wait for the hero to be ready.");
            return hero;
        }

        public static string Humanize(string value) => Regex.Replace(value ?? "", "(?<=[a-z0-9])([A-Z])", " $1");

        public static List<CatalogEntry<SpecialCard.CardType>> Cards()
        {
            var entries = new List<CatalogEntry<SpecialCard.CardType>>();
            foreach (SpecialCard.CardType type in Enum.GetValues(typeof(SpecialCard.CardType)))
            {
                if (type == SpecialCard.CardType.None) continue;
                var prefab = Resources.Load<SpecialCard>("sc_" + type);
                if (prefab == null) continue;
                string name = SpecialCard.GetName(type);
                entries.Add(new CatalogEntry<SpecialCard.CardType> { Id = type,
                    Name = string.IsNullOrEmpty(name) ? Humanize(type.ToString()) : name,
                    Detail = Humanize(prefab.group.ToString()) + "  /  " + prefab.cost + " energy" });
            }
            return entries.OrderBy(e => e.Name).ToList();
        }

        public static List<CatalogEntry<Perk.PerkType>> Perks()
        {
            var entries = new List<CatalogEntry<Perk.PerkType>>();
            foreach (Perk.PerkType type in Enum.GetValues(typeof(Perk.PerkType)))
            {
                if (type == Perk.PerkType.None) continue;
                var prefab = Resources.Load<Perk>("pe_" + type);
                if (prefab == null) continue;
                string name = Perk.GetPerkName(type);
                entries.Add(new CatalogEntry<Perk.PerkType> { Id = type,
                    Name = string.IsNullOrEmpty(name) ? Humanize(type.ToString()) : name,
                    Detail = Humanize(prefab.perkGroup.ToString()) });
            }
            return entries.OrderBy(e => e.Name).ToList();
        }

        public static List<CatalogEntry<string>> Worlds()
        {
            var manager = Reg.instance.DungeonManager;
            var stages = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            var fallbackNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in typeof(DungeonManager).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(List<string>) || !field.Name.Contains("Dungeons")) continue;
                var names = (List<string>)field.GetValue(manager);
                if (names == null) continue;
                int stage = field.Name.Contains("Tier3") ? 3 : field.Name.Contains("Tier2") ? 2 : field.Name.Contains("Tier1") ? 1 : 0;
                foreach (string name in names)
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    if (stage == 0) { fallbackNames.Add(name); continue; }
                    if (!stages.ContainsKey(name)) stages[name] = new HashSet<int>();
                    stages[name].Add(stage);
                }
            }
            foreach (string name in fallbackNames)
                if (!stages.ContainsKey(name)) stages[name] = new HashSet<int> { 1 };
            stages["dun_ChaosPyramid"] = new HashSet<int> { 4 };
            stages["dun_ChaosEndless"] = new HashSet<int> { 4 };
            stages["dun_ChaosEndlessBone"] = new HashSet<int> { 4 };
            stages["dun_ChaosEndlessThorn"] = new HashSet<int> { 4 };
            var entries = new List<CatalogEntry<string>>();
            foreach (var pair in stages)
            {
                var prefab = Resources.Load<Dungeon>(pair.Key);
                if (prefab == null) continue;
                string name = manager.GetSectionDescription("PlanetName", prefab.name);
                foreach (int stage in pair.Value)
                    entries.Add(new CatalogEntry<string> { Id = pair.Key, Stage = stage,
                        Name = string.IsNullOrEmpty(name) ? Humanize(pair.Key.Replace("dun_", "")) : name,
                        Detail = prefab.isChaosEndless ? "Endless" : prefab.isChaosPyramid ? "Chaos Pyramid" : "World " + stage });
            }
            return entries.OrderBy(e => e.Stage).ThenBy(e => e.Name).ToList();
        }

        public static void Refresh(Hero hero)
        {
            hero.health = Mathf.Clamp(hero.health, 0, hero.GetNormalizedMaxHealth());
            hero.energy = Mathf.Clamp(hero.energy, 0, hero.GetNormalizedMaxEnergy());
            hero.SetSecondaryAttributes();
            hero.UpdateHealthTextfields();
            hero.UpdateEnergyTextfields();
        }

        public static void SetStat(Stat stat, int value)
        {
            var hero = RequireHero();
            if (value < stat.Min || value > stat.Max)
                throw new ArgumentOutOfRangeException(stat.Name, "Enter a value from " + stat.Min + " to " + stat.Max + ".");
            stat.Write(hero, value);
            Refresh(hero);
        }

        public static void Restore(bool health, bool energy)
        {
            var hero = RequireHero();
            if (health) hero.health = hero.GetNormalizedMaxHealth();
            if (energy) hero.energy = hero.GetNormalizedMaxEnergy();
            Refresh(hero);
        }

        public static void GiveCard(SpecialCard.CardType type, int count, bool drawNext)
        {
            var hero = RequireHero();
            if (type == SpecialCard.CardType.None || Resources.Load<SpecialCard>("sc_" + type) == null)
                throw new InvalidOperationException("This card has no usable prefab.");
            if (count < 1 || count > 99) throw new ArgumentOutOfRangeException(nameof(count), "Quantity must be 1 to 99.");
            for (int i = 0; i < count; i++)
                hero.AddCardToDeck(type, addToCurrentHandCardDeck: true, randomPosition: false,
                    showContextMessage: false, addToTopOfHandcardDeck: drawNext);
        }

        public static void GivePerk(Perk.PerkType type)
        {
            var hero = RequireHero();
            if (hero.activePerks.Contains(type)) throw new InvalidOperationException("This perk is already active.");
            if (type == Perk.PerkType.None || Resources.Load<Perk>("pe_" + type) == null)
                throw new InvalidOperationException("This perk has no usable prefab.");
            if (type == Perk.PerkType.QuickWitted && hero.handSize >= 5)
                throw new InvalidOperationException("The game supports at most 5 cards in hand.");
            Perk.ApplyPerk(hero, type);
            Refresh(hero);
        }

        public static void KillEnemy()
        {
            var enemy = RequireHero().gameManager.enemy;
            if (enemy == null || enemy.dead) throw new InvalidOperationException("There is no living enemy in this room.");
            enemy.health = 0;
            enemy.UpdateHealthTextfields();
        }

        public static void ClearConditions()
        {
            var hero = RequireHero();
            foreach (var effect in hero.conditions.ToArray()) hero.RemoveCondition(effect);
            Refresh(hero);
        }

        public static void SaveRun()
        {
            var hero = RequireHero();
            if (Reg.instance.isTutorialRun || !Reg.instance.isNotTestRun)
                throw new InvalidOperationException("The game does not save tutorial or internal test runs.");
            hero.SaveHero();
        }

        public static void Teleport(CatalogEntry<string> world)
        {
            var hero = RequireHero();
            var prefab = Resources.Load<Dungeon>(world.Id);
            if (prefab == null) throw new InvalidOperationException("The selected world is unavailable.");
            var registry = Reg.instance;
            var manager = registry.DungeonManager;
            // A scene reload removes combat objects/timers and runs the normal dungeon setup.
            // The hero is persistent; keep its equipment, cards, perks and edited attributes.
            ClearConditions();
            hero.savedDuringCombat = false;
            hero.justLoaded = false;
            hero.justKilledEnemy = false;
            hero.savedLootString = "";
            hero.savedLootContainerString = "";
            hero.savedLootBlueprintString = "";
            hero.savedLootValue = 0;
            hero.savedLootCardPiece = -1;
            hero.completedMainDungeons = world.Stage - 1;
            hero.chaosRun = prefab.isChaosPyramid || prefab.isChaosEndless;
            hero.endlessRun = prefab.isChaosEndless;
            hero.completedEndlessSegments = 0;
            registry.isContinue = false;
            registry.isEndlessContinue = false;
            registry.isTutorialRun = false;
            registry.isNotTestRun = true;
            // Save before clearing the dungeon. SaveHero normally inspects the current room
            // and writes enemy data during combat, which must not follow us to the destination.
            var previousRoomType = hero.gameManager.currentRoomType;
            hero.gameManager.currentRoomType = Dungeon.RoomType.Empty;
            try { hero.SaveHero(); }
            finally { hero.gameManager.currentRoomType = previousRoomType; }
            hero.CancelInvoke();
            hero.gameManager.CancelInvoke();
            hero.gameManager.StopAllCoroutines();
            hero.gameManager.currentState = GameManager.GAME_STATE.GAME_STATE_WAIT;
            hero.gameManager.enabled = false;
            var oldDungeons = new[] { manager.dungeon, manager.mainQuestDungeon, manager.sideQuestDungeon };
            foreach (var old in oldDungeons.Where(d => d != null).Distinct())
                if (old.gameObject.scene.IsValid()) UnityEngine.Object.Destroy(old.gameObject);
            manager.dungeon = null;
            manager.sideQuestDungeon = null;
            manager.mainQuestDungeonSave = null;
            manager.sideQuestDungeonSave = null;
            manager.mainQuestDungeon = prefab;
            manager.selectedDungeon = prefab.isChaosPyramid ? 666 : world.Stage == 1 ? 0 : world.Stage == 2 ? 2 : 4;
            foreach (string key in new[] { "dungeonState", "sideQuestState", "endlessState" })
                if (registry.SaveExists(key)) registry.DeleteSave(key);
            if (Time.timeScale <= 0) Time.timeScale = 1;
            registry.Dialog.LoadScene("Game", "Debug travel: " + world.Name);
        }
    }
}
