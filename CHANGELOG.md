# Changelog

What changed in each version of Project Diablo II Single Player Companion, newest first. The editor shows this list in
Settings, under About, and once after an update.

Each release is a `## [version] - date` heading, an optional line about it, and `###` groups of `-` lines
(Added, Changed, Fixed, Removed). Work not yet released goes under `## [Unreleased]

## [1.0.0] - 2026-11-16

The first public release, for Project Diablo 2 Season 14, single player.

### Added
- Characters: open, edit attributes, skills, quests, waypoints and the map seed; respec and refit; create, clone,
  rename, delete and revive characters; import one from the PD2 armory. Find an item across every character.
- Character sheet: the game's Advanced Stats worked out from the gear, skills and auras, the mercenary's too; each
  skill's tooltip as the game words it.
- Mercenary: its name, variant and level within its type, revived when dead, and its gear as the character's.
- Items: edit any item - affixes, properties, sockets, rare names, superior bonuses, automods and staff mods,
  ethereal, durability and item level; corrupt, desecrate, craft, upgrade bases, reroll with the Demonic Cube, and
  build runewords in place. Socketed jewels are edited where they lie.
- Forge (Free mode): make any item from nothing - uniques, sets, runewords, magic and rare items, maps and
  materials - or paste one copied from the game with Ctrl + C.
- Items page: move items between the inventory, cube, stash, shared stash pages, materials tab, belt, worn slots,
  the mercenary and a corpse; drag several at once; cut, copy and paste; stacks join as in game; lock pages you do
  not want touched; sort and close gaps.
- Vault: keep items outside the game in folders, sorted for you by rules written like PD2's loot filter; crafting
  bases kept apart; folder locks, quick access, vault gold, and a backup for each session that changed it.
- Search: one search for the whole program (Ctrl + F) - by name, base, stats or a pattern, as the game's stash
  search takes them - over the open character, the shared stash and the vault.
- Compendium: every unique, set piece and runeword, what the game has discovered, and the copies you hold; store a
  copy in the game's Compendium or take one out.
- Virtual trading: buy and sell from PD2 trade listings at your own prices, with a record of every trade.
- Play modes: Legit, Casual and Free set how far an edit may go, and Custom sets each rule by itself. A check marks
  any item the game could not have made.
- Safety: autosave, undo and redo for the whole session, a backup before every write, and nothing written while
  the game is running or after a PD2 patch until your saves check out.
- Seasons: the program reads PD2's tables from your install, so a patch needs no update here; each season's tables
  are kept, items of the season before stay as they were, and characters and vaults can be migrated.
- Extra: a rune wizard and a crafting wizard; drop rates and monster density for testing on the Experimental page.
- The window: UI scale, sounds, keyboard shortcuts on one sheet (F1), language packs, and Report a problem.
- pd2spc, a command-line tool: read, check and change saves, stashes and the game's tables.
