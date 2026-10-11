# Void Tyrant ModKit

A mod loader for **Void Tyrant** (Steam, Unity 2019.4 / Mono).

```
Void Tyrant.exe
 └─ BepInEx 5 (winhttp.dll proxy)    injects .NET code, ships Harmony for patching
     └─ VoidTyrant.ModKit            this repo: loads content mods from <GameDir>\Mods
         ├─ Mods\<YourMod>\         data-only mods: JSON + PNG, no compiling
         └─ VoidTyrant.DebugPanel   optional in-game cheats for testing runs
```

Tested with Void Tyrant **1.3.4.11** (Steam, Windows). Game updates can break mods; the game version
is shown in the F1 menu and in `BepInEx\LogOutput.log`.

## For players

1. Download `VoidTyrantModKit-<version>.zip` from the Releases page.
2. In Steam, right-click Void Tyrant → **Manage** → **Browse local files**.
3. Extract the zip into that folder, next to `Void Tyrant.exe`.
4. Start the game and press **F1** for the ModKit menu.

Put content mods in the `Mods` folder. `Mods\ExampleMod` ships turned off; set `"enabled": true` in
its `mod.json` to try it.

### Steam achievements

Achievements and Steam stats are blocked for the rest of the session once:

- the debug panel is switched on, or
- an enabled content mod changes card stats (mods that only replace `art` don't count).

You can also turn them off yourself with **Disable Steam achievements** in the F1 menu. The menu
shows whether achievements are on and why not. Restart the game after turning the debug panel
off to get them back. Progress made while cheating is still saved in your game profile and
current run, so use a separate run for testing.

### Saves

Saves are in `%USERPROFILE%\AppData\LocalLow\Quite Fresh\Void Tyrant`. Back that folder up
before using the debug panel's teleport, which rewrites the current run's save.

## Building from source

Requirements: the .NET SDK (8+), and Void Tyrant installed via Steam.

```powershell
.\scripts\install-bepinex.ps1          # add -GameDir "D:\...\Void Tyrant" if not in the default Steam library
dotnet build -c Release                # builds and copies everything into the game
```

Launch the game from Steam and press **F1** for the ModKit menu, which lists loaded mods and
has the settings, including the switch that turns on the **F2** debug panel.

> **Don't click inside the BepInEx console.** Windows "QuickEdit" freezes any program writing to a
> console while text is selected, so the game will hang until you press Esc in the console.
> Turn the console off in `BepInEx\config\BepInEx.cfg` (`[Logging.Console] Enabled = false`);
> the log is always written to `BepInEx\LogOutput.log` either way.

Uninstall: `.\scripts\install-bepinex.ps1 -Uninstall` (leaves your `Mods` folder alone).

## Content mods (no code)

Make a folder in `<GameDir>\Mods\`:

```
Mods\MyMod\
  mod.json      { "id": "my-mod", "name": "My Mod", "version": "1.0.0", "author": "me", "enabled": true }
  cards.json    card overrides (below)
  sword.png     optional art
```

`cards.json` maps card names to the fields you want to change:

```jsonc
{
  "Potion":     { "value": 10 },                          // shop price
  "Repair":     { "cost": 5 },                            // energy cost
  "Shortsword": { "art": "sword.png", "rareCard": true }  // replace the card picture
}
```

- **Every card and its current values** are written to `Mods\_reference\cards.json` each launch. Start there.
- Editable fields include `cost`, `value`, `duration`, `rareCard`, `perishable`, `upgradable`, `group`,
  `category`, `executeRequirements` … (enums take their names as strings). Typos are reported in
  the log and the F1 overlay.
- Mods load alphabetically by folder name; a later mod wins if two change the same field.
  Folders starting with `_` or `.` are ignored.
- **Limits:** damage numbers and effects are hardcoded in `SpecialCard.GetDynamicValue` and the
  game's `switch` statements, not on the prefabs. Changing those needs a code mod.

## Code mods

A code mod is a normal [BepInEx 5 plugin](https://docs.bepinex.dev/articles/dev_guide/plugin_tutorial/index.html).
Create a project under `src/` and add it to the solution (`dotnet sln add ...`). The shared
`Directory.Build.props` already references the game, Unity, BepInEx and Harmony assemblies, and
`dotnet build` deploys to `BepInEx\plugins\<AssemblyName>\`.

## Debug panel

The panel is **off by default**. Turn it on with the **Debug panel** switch in the F1 menu (or set
`Enabled = true` in `BepInEx/config/voidtyrant.debugpanel.cfg`). Turning it on disables Steam
achievements until the game restarts.

Press **F2** to open; **F2**, **Escape**, or **Close** returns to the game. The panel pauses the
game and blocks underlying game input while editing. It uses the same default Unity IMGUI
controls as the F1 ModKit overlay, with tabs and scrolling lists. Drag to move the window.
The key is configurable in `BepInEx/config/voidtyrant.debugpanel.cfg`.

Start or continue a run before using actions. The panel does not spawn a hero on the main menu.

- **Worlds:** search destinations across all progression tiers, including crystal worlds,
  the Chaos Pyramid and endless variants. Click **Travel**, then **Teleport**. This replaces
  the current encounter, clears status effects and moves progression to that world's tier.
  Stats, equipment, deck and perks carry over; the normal world-entry code refills health.
- **Stats:** set or increase max health/energy, melee/ranged/magic damage, armor, defense,
  luck, gold and weapon speed bonus. Values are base attributes; equipment and status effects
  still contribute bonuses. Restore resources or save the run explicitly.
- **Cards:** search by localized name, internal ID or group. Give 1–99 copies, optionally
  placing them at the front of the draw pile with **Draw next**. They enter the permanent
  deck and current draw pile, becoming available on the next hand refill. **In deck** filters
  to owned cards and each row shows its current count.
- **Perks:** search and apply perks through the game's `Perk.ApplyPerk` method. **Active** filters
  to owned perks. Duplicate perks and hand-size increases beyond the game's five-card limit
  are blocked.
- **Combat:** defeat the current enemy, restore HP/EP, remove hero status effects, or apply a
  test boost (+100 max HP/EP, +10 damage attributes, +1,000 gold). Close the panel for the
  combat loop to process a defeated enemy.

Changes can persist through the game's normal saves. Teleporting updates the current run's
save and replaces its saved dungeon. Use a disposable run when testing progression.
Debug actions only run when you select them.

Each plugin's settings appear in `BepInEx\config\<guid>.cfg` after the first launch.

Code mods can use the ModKit too: add `[BepInDependency(VoidTyrant.ModKit.ModKitPlugin.Guid)]`, then:

- read `ModKitPlugin.Mods` (each has a `Directory` for your own assets) or subscribe to `ModKitPlugin.ModsApplied`;
- call `Achievements.Block("reason")` if your mod gives the player an advantage;
- call `ModKitPlugin.AddToggle(label, configEntry)` to put an on/off setting in the F1 menu;
- call `InputGuard.AddBlocker(() => ...)` to stop clicks on your IMGUI window reaching the game.

### Finding things to patch

Decompile `Void Tyrant_Data\Managed\Assembly-CSharp.dll` with [ILSpy](https://github.com/icsharpcode/ILSpy)
or dnSpyEx (or use an AssetRipper export). Useful starting points:

- `Reg.instance`: global singleton (`Reg.instance.Hero`, `.PlayerProfile`, `.GameManager`, …)
- `GameManager`: combat loop; `Hero` / `Participant`: health, energy, money, status effects
- `SpecialCard`: card data (`sc_<CardType>` prefabs) and the big per-card `switch` logic
- `LootManager`, `Shop`, `DungeonManager`: rewards, shops, map generation

Game assemblies are referenced from your install and never committed. Keep it that way: don't
redistribute the game's DLLs or decompiled source.

## Releasing

```powershell
.\scripts\package-release.ps1     # -GameVersion "x.y.z" when you have tested a new game version
```

This builds Release, downloads BepInEx, and writes `dist\VoidTyrantModKit-<version>.zip` with BepInEx,
both plugins, the disabled example mod and a player readme. Bump `ModKitPlugin.Version` and
`CHANGELOG.md` first, then attach the zip to a GitHub release.

## How the card loader works

The game reads card data with `Resources.Load<SpecialCard>("sc_" + type)` on demand. `Resources.Load`
is a native call and can't be reliably patched. Instead, ModKit loads each targeted prefab once at startup,
edits its fields in memory, and marks it `DontUnloadUnusedAsset`. Every later `Resources.Load` returns
that same edited asset, so changes show up everywhere the card is used. Nothing on disk is modified.

## License

[MIT](LICENSE). Release zips include [BepInEx](https://github.com/BepInEx/BepInEx) (MIT) and
HarmonyX (MIT). Void Tyrant is © its developers; this project is not affiliated with them.
