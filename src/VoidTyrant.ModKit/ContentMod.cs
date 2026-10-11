using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VoidTyrant.ModKit
{
    /// <summary>
    /// A folder under &lt;GameDir&gt;\Mods. Content mods need no code: a mod.json manifest
    /// plus optional data files (cards.json, art) that the ModKit applies to the game.
    /// </summary>
    public sealed class ContentMod
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Version { get; private set; } = "0.0.0";
        public string Author { get; private set; } = "";
        public string Description { get; private set; } = "";
        public bool Enabled { get; private set; } = true;

        /// <summary>Absolute path of the mod folder. Code mods can use this to find their own assets.</summary>
        public string Directory { get; private set; }

        /// <summary>Card name -> field overrides, as parsed from cards.json.</summary>
        internal Dictionary<string, Dictionary<string, object>> CardOverrides { get; } =
            new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);

        public List<string> Errors { get; } = new List<string>();
        public int CardsPatched { get; internal set; }

        /// <summary>True once any card field other than its art has been changed. Turns achievements off.</summary>
        public bool ChangesGameplay { get; internal set; }

        /// <summary>
        /// Loads every mod folder in <paramref name="modsDir"/>, in alphabetical order.
        /// Later mods win when two mods change the same field. Folders starting with '_' or '.' are skipped.
        /// </summary>
        public static List<ContentMod> LoadAll(string modsDir)
        {
            return System.IO.Directory.GetDirectories(modsDir)
                .Where(d => !Path.GetFileName(d).StartsWith("_") && !Path.GetFileName(d).StartsWith("."))
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                .Select(Load)
                .ToList();
        }

        static ContentMod Load(string dir)
        {
            string folder = Path.GetFileName(dir);
            var mod = new ContentMod { Id = folder, Name = folder, Directory = dir };

            string manifestPath = Path.Combine(dir, "mod.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var m = AsObject(Json.Parse(File.ReadAllText(manifestPath)), "mod.json");
                    mod.Id = GetString(m, "id") ?? mod.Id;
                    mod.Name = GetString(m, "name") ?? mod.Name;
                    mod.Version = GetString(m, "version") ?? mod.Version;
                    mod.Author = GetString(m, "author") ?? mod.Author;
                    mod.Description = GetString(m, "description") ?? mod.Description;
                    if (m.TryGetValue("enabled", out var en) && en is bool b) mod.Enabled = b;
                }
                catch (Exception e)
                {
                    mod.Errors.Add($"mod.json: {e.Message}");
                }
            }

            if (!mod.Enabled) return mod;

            string cardsPath = Path.Combine(dir, "cards.json");
            if (File.Exists(cardsPath))
            {
                try
                {
                    var cards = AsObject(Json.Parse(File.ReadAllText(cardsPath)), "cards.json");
                    foreach (var kv in cards)
                    {
                        if (kv.Key.StartsWith("$")) continue; // "$comment" etc.
                        if (kv.Value is Dictionary<string, object> fields)
                            mod.CardOverrides[kv.Key] = fields;
                        else
                            mod.Errors.Add($"cards.json: '{kv.Key}' must be an object of field overrides");
                    }
                }
                catch (Exception e)
                {
                    mod.Errors.Add($"cards.json: {e.Message}");
                }
            }

            return mod;
        }

        static Dictionary<string, object> AsObject(object json, string file) =>
            json as Dictionary<string, object> ?? throw new FormatException($"{file} must contain a JSON object");

        static string GetString(Dictionary<string, object> obj, string key) =>
            obj.TryGetValue(key, out var v) && v is string s ? s : null;

        public override string ToString() => $"{Name} {Version} ({Id})";
    }
}
