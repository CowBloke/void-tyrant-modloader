Void Tyrant ModKit {VERSION}
Made for Void Tyrant {GAME_VERSION} (Steam, Windows).

INSTALL
  1. In Steam, right-click Void Tyrant > Manage > Browse local files.
  2. Extract everything in this zip into that folder, next to "Void Tyrant.exe".
  3. Start the game. The first launch takes a little longer while BepInEx sets up.
  4. Press F1 in game to open the ModKit menu.

CONTENT MODS
  Put mod folders in the "Mods" folder. "Mods\ExampleMod" shows the format and is
  turned off; set "enabled" to true in its mod.json to try it. Every card and its
  current values are written to Mods\_reference\cards.json each launch.

DEBUG PANEL (CHEATS)
  Off by default. Turn it on in the F1 menu, then press F2 in a run. It can give
  cards, perks, gold and stats, and teleport between worlds.
  Teleporting rewrites your current run's save. Back up your saves first:
    %USERPROFILE%\AppData\LocalLow\Quite Fresh\Void Tyrant

STEAM ACHIEVEMENTS
  Achievements and Steam stats are turned off for the rest of the session once the
  debug panel is switched on or any enabled mod changes card stats (art-only mods
  are fine). You can also turn them off yourself in the F1 menu. Progress you make
  while cheating is still saved in your profile, so use a separate run for testing.

UNINSTALL
  Delete winhttp.dll, doorstop_config.ini, .doorstop_version, changelog.txt and the
  BepInEx folder from the game folder. Your Mods folder is left alone.

Source, documentation and issues: {REPO_URL}
BepInEx (https://github.com/BepInEx/BepInEx) is included under the MIT license (see BepInEx\LICENSE.txt).
