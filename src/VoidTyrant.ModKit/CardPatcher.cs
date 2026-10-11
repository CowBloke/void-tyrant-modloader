using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace VoidTyrant.ModKit
{
    /// <summary>
    /// Applies cards.json overrides to the game's card prefabs.
    ///
    /// Every card's static data lives on a prefab at Resources/sc_&lt;CardType&gt; with a SpecialCard
    /// component, and the game reads it back with Resources.Load every time it needs a cost, rarity,
    /// picture, etc. Resources.Load hands out the same in-memory asset each time, so editing that
    /// asset once (and pinning it so Unity never unloads and reloads a fresh copy) changes the card
    /// everywhere: shops, decks, enemy hands and tooltips.
    /// </summary>
    internal static class CardPatcher
    {
        const string ArtKey = "art";

        // Keeps patched prefabs (and the sprites we made for them) alive for the whole session.
        static readonly List<UnityEngine.Object> Pinned = new List<UnityEngine.Object>();

        static readonly Dictionary<string, FieldInfo> Fields = typeof(SpecialCard)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Where(f => f.Name != nameof(SpecialCard.type)) // the card's identity; changing it breaks lookups
            .Where(f => IsSimple(f.FieldType) || IsSimpleList(f.FieldType))
            .ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

        public static SpecialCard LoadCard(SpecialCard.CardType type) => Resources.Load<SpecialCard>("sc_" + type);

        public static void Apply(IEnumerable<ContentMod> mods)
        {
            foreach (var mod in mods.Where(m => m.Enabled))
            {
                foreach (var entry in mod.CardOverrides)
                {
                    if (!TryParseCardType(entry.Key, out var type))
                    {
                        mod.Errors.Add($"cards.json: unknown card '{entry.Key}' (see Mods/_reference/cards.json for names)");
                        continue;
                    }

                    var card = LoadCard(type);
                    if (card == null)
                    {
                        mod.Errors.Add($"cards.json: '{type}' has no card prefab, nothing to patch");
                        continue;
                    }

                    Pin(card.gameObject);
                    bool any = false;
                    foreach (var field in entry.Value)
                    {
                        try
                        {
                            if (string.Equals(field.Key, ArtKey, StringComparison.OrdinalIgnoreCase))
                                ApplyArt(card, mod, field.Value as string ?? throw new FormatException("must be a file path string"));
                            else
                            {
                                SetField(card, field.Key, field.Value);
                                mod.ChangesGameplay = true;
                            }
                            any = true;
                        }
                        catch (Exception e)
                        {
                            mod.Errors.Add($"cards.json: {type}.{field.Key}: {e.Message}");
                        }
                    }
                    if (any) mod.CardsPatched++;
                }
            }
        }

        /// <summary>Writes every card's current (unmodded) data so mod authors know what they can change.</summary>
        public static int DumpReference(string path)
        {
            var root = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (SpecialCard.CardType type in Enum.GetValues(typeof(SpecialCard.CardType)))
            {
                SpecialCard card;
                try { card = LoadCard(type); }
                catch { continue; }
                if (card == null) continue;

                var data = new SortedDictionary<string, object>(StringComparer.Ordinal);
                foreach (var f in Fields.Values) data[f.Name] = f.GetValue(card);
                root[type.ToString()] = data;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, Json.Write(root));
            return root.Count;
        }

        public static IEnumerable<string> EditableFieldNames => Fields.Keys.OrderBy(k => k);

        static bool TryParseCardType(string name, out SpecialCard.CardType type)
        {
            type = default;
            if (!Enum.IsDefined(typeof(SpecialCard.CardType), name))
            {
                // Accept any casing, but never numbers: "12" silently mapping to a card would be confusing.
                var match = Enum.GetNames(typeof(SpecialCard.CardType))
                    .FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
                if (match == null) return false;
                name = match;
            }
            type = (SpecialCard.CardType)Enum.Parse(typeof(SpecialCard.CardType), name);
            return true;
        }

        static void SetField(SpecialCard card, string name, object json)
        {
            if (!Fields.TryGetValue(name, out var field))
                throw new ArgumentException($"not an editable field. Editable: {string.Join(", ", EditableFieldNames)}");
            field.SetValue(card, Convert(json, field.FieldType));
        }

        static object Convert(object json, Type target)
        {
            if (IsSimpleList(target))
            {
                if (!(json is List<object> items)) throw new FormatException($"expected an array for {Describe(target)}");
                var elementType = target.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(target);
                foreach (var item in items) list.Add(Convert(item, elementType));
                return list;
            }

            if (target.IsEnum)
            {
                if (json is string s)
                {
                    var match = Enum.GetNames(target).FirstOrDefault(n => string.Equals(n, s, StringComparison.OrdinalIgnoreCase));
                    if (match == null) throw new FormatException($"'{s}' is not a {target.Name}. Options: {string.Join(", ", Enum.GetNames(target))}");
                    return Enum.Parse(target, match);
                }
                if (json is double d)
                {
                    if (d != Math.Floor(d) || !Enum.IsDefined(target, (int)d))
                        throw new FormatException($"{d} is not a {target.Name}. Options: {string.Join(", ", Enum.GetNames(target))}");
                    return Enum.ToObject(target, (int)d);
                }
                throw new FormatException($"expected a {target.Name} name");
            }

            if (target == typeof(bool))
                return json is bool b ? b : throw new FormatException("expected true or false");
            if (target == typeof(string))
                return json as string ?? throw new FormatException("expected a string");
            if (json is double num)
            {
                if (target == typeof(int))
                {
                    if (num != Math.Floor(num)) throw new FormatException("expected a whole number");
                    return (int)num;
                }
                return System.Convert.ChangeType(num, target, CultureInfo.InvariantCulture);
            }
            throw new FormatException($"expected a number for {Describe(target)}");
        }

        static void ApplyArt(SpecialCard card, ContentMod mod, string relativePath)
        {
            string file = Path.GetFullPath(Path.Combine(mod.Directory, relativePath));
            if (!File.Exists(file)) throw new FileNotFoundException($"'{relativePath}' not found in {mod.Directory}");

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileNameWithoutExtension(file) };
            if (!tex.LoadImage(File.ReadAllBytes(file))) throw new FormatException($"'{relativePath}' is not a valid PNG/JPG");
            Pin(tex);

            // The game draws the same art at three sizes; each gets a sprite scaled to match the original's footprint.
            card.cardPic = Replace(card.cardPic, tex);
            card.cardPicBig = Replace(card.cardPicBig, tex);
            card.cardPicSmall = Replace(card.cardPicSmall, tex);
        }

        static Sprite Replace(Sprite original, Texture2D tex)
        {
            if (original == null) return null;
            var rect = original.rect;
            var pivot = new Vector2(original.pivot.x / rect.width, original.pivot.y / rect.height);
            float ppu = original.pixelsPerUnit * (tex.width / rect.width);
            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), pivot, ppu);
            sprite.name = original.name + "_modded";
            Pin(sprite);
            return sprite;
        }

        static void Pin(UnityEngine.Object obj)
        {
            obj.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            if (!Pinned.Contains(obj)) Pinned.Add(obj);
        }

        static bool IsSimple(Type t) =>
            t == typeof(int) || t == typeof(float) || t == typeof(bool) || t == typeof(string) || t.IsEnum;

        static bool IsSimpleList(Type t) =>
            t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>) && IsSimple(t.GetGenericArguments()[0]);

        static string Describe(Type t) =>
            IsSimpleList(t) ? $"list of {t.GetGenericArguments()[0].Name}" : t.Name;
    }
}
