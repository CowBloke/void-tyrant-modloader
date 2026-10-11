# Changelog

## 0.1.0

First public release. Tested with Void Tyrant 1.3.4.11 on Steam (Windows).

- ModKit: loads content mods from `<GameDir>\Mods` (card stats and art via `cards.json`),
  writes `Mods\_reference\cards.json`, and adds an in-game menu on F1.
- Debug panel (F2): worlds, stats, cards, perks and combat tools. Off by default; turn it on
  in the F1 menu.
- Steam achievements and stats are blocked while the debug panel or a gameplay-changing mod
  is active, and can be turned off entirely in the F1 menu.
