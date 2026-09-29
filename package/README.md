# Valkyrie's Vision

> **AI disclosure:** Valkyrie's Vision is a vibe-coded project. The idea, the design decisions, and all of the in-game testing came from Drakexi. The code was written by Claude under Drakexi's direction. Every feature was tested in-game before release. If you'd rather avoid AI-written mods, you can make that choice knowingly.

Exploring Valheim leaves your map looking like ant tunnels through the fog. Revealing the whole map with console commands feels like cheating. Valkyrie's Vision sits in between: **every Forsaken you slay earns you part of the map.**

When a boss falls, the Valkyrie's vision sweeps across the land, the fog lifts, and the game's "new biome discovered" sound plays.

By default, revealed areas appear with the same light haze as map data shared by other players, so you can still tell where you've actually been, and each player earns reveals only for the bosses they were present for. Both can be changed in the settings.

## Reveal modes

**Biome mode (default).** Each boss reveals every patch of its own biome, anywhere in the world, including tiny islands at the far edge. Those distant specks become landing spots and base sites worth sailing to.

| Boss | Reveals |
|---|---|
| Eikthyr | Meadows |
| The Elder | Black Forest |
| Bonemass | Swamp |
| Moder | Mountains |
| Yagluth | Plains |
| The Queen | Mistlands + the ocean |
| Fader | Ashlands |
| Kall Fimbulbringer | Deep North |

**The ocean** is revealed at the Queen, or earlier as a reward for completing Hildir's quests: once Brenna, Geirrhafa, and Zil & Thungr have all fallen, the seas are revealed.

**Rings mode.** Each boss reveals a larger circle outward from the world center. With `EqualArea` scaling, each kill reveals another 1/8 of the map's area. With `EqualRadius`, each kill adds 1/8 of the radius. Defeating Kall reveals the entire world.

## Good to know

- Reveals only ever add to your map. Nothing you explored yourself is hidden.
- Reveals are permanent and saved to your character, just like normal exploration.
- Installing mid-playthrough is fine. On first load, everything you've already earned is revealed at once.
- By default, reveals follow **your own** boss kills, like the player-based raids world modifier: a newcomer to a server doesn't inherit everyone else's progress. Switch `ProgressionSource` to `World` to give every player the reveals for every boss the world has seen.
- Walking through a hazy (translucent) area yourself makes it fully clear, as normal.
- **Changing settings mid-game:** loosening them (`Player` to `World`, or `Translucent` to `Clear`) catches up at your next login. Tightening them (`World` to `Player`, or `Clear` to `Translucent`) only affects new reveals; nothing already revealed is fogged again. For a clean slate, the `resetmap` console command wipes that character's map for the world, and the mod then re-applies only what the current settings allow.

## Installation

Install with r2modman, Thunderstore Mod Manager, or Gale (available on Thunderstore and Hexium). Requires BepInEx and Jötunn.

**Client only:** works on any server, including vanilla ones. The mod reads boss progress that the game already shares with every player, and uses your own config.

**On a server too (optional):** install it on the server if you want everyone to play with the same settings. The server's settings then apply to every player who has the mod, and they're locked while connected. Players without the mod can still join; they just don't get the reveals.

## Settings

Found in `BepInEx/config/com.drakexi.valkyriesvision.cfg`.

**Server-controlled** (if the server also has the mod, its values apply to everyone and are locked):

| Setting | Default | Description |
|---|---|---|
| RevealMode | Biome | `Biome` or `Rings` |
| RevealStyle | Translucent | `Translucent` (light haze, like shared map data) or `Clear` (fully revealed) |
| ProgressionSource | Player | `Player` (only bosses you were present for) or `World` (every boss defeated on the world) |
| RingScaling | EqualArea | Rings mode only: `EqualArea` or `EqualRadius` |
| OceanReveal | Queen | Biome mode only: which boss reveals the ocean (`Never` disables it) |
| OceanRevealEarlyKeys | BossHildir1,BossHildir2,BossHildir3 | Biome mode only: keys that reveal the ocean early once all are set. Blank disables the early reveal. |
| BossKeys (section) | vanilla keys | The world key each boss sets when defeated. Change only for compatibility with other mods. |

**Personal** (each player chooses):

| Setting | Default | Description |
|---|---|---|
| ShowMessages | true | Show the Valkyrie message when new areas are revealed |
| MessageSeconds | 5 | How long the message stays fully visible |
| PlayStinger | true | Play the "new biome discovered" sound with the message |
| DebugLogging | false | Log every boss and keyed creature on world load |

## Bugs & feature requests

Report bugs or suggest features on [GitHub Issues](https://github.com/Drake1973/ValkyriesVision/issues).
