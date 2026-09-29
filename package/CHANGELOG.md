# Changelog

## 0.7.1
- Fixed: if another mod that changes the map (for example SKKSailingMapReveal) caused an error during a reveal, Valkyrie's Vision stopped silently and never tried again. It now logs a clear error, recovers, and retries when your progress or settings change.
- If another mod's hook breaks the game's map-reveal function, the mod now switches to marking the map directly so reveals still work, and logs a warning explaining why.
- Thanks to firehawkx for the report.

## 0.7.0
**Defaults changed:** read before updating.
- New setting **RevealStyle**. Default **Translucent**: revealed areas now look like map data shared by other players (a light haze) instead of fully clear. Set to `Clear` for the old behavior.
- New setting **ProgressionSource**. Default **Player**: each player only gets reveals for bosses they were present for, matching the player-based raids world modifier. Set to `World` for the old behavior (reveals for every boss anyone on the world has defeated).
- Anything already revealed stays revealed. Areas you explore yourself always become fully clear, as normal.

## 0.6.1
- Added a GitHub link for bug reports and feature requests, shown on the Thunderstore listing and in the README.
- No gameplay changes.

## 0.6.0
- Initial public release.
- Biome and Rings reveal modes.
- Ocean reveal at the Queen, or earlier once all three of Hildir's mini-bosses are defeated.
- Valkyrie messages and the biome-discovery sound when new areas are revealed.
- Works client-only; installing on a server is optional and syncs its settings to players.
