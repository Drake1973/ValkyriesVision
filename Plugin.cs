using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using Jotunn;
using Jotunn.Utils;
using UnityEngine;

namespace ValkyriesVision
{
    public enum RevealMode { Biome, Rings }
    public enum RingScaling { EqualRadius, EqualArea }
    public enum OceanOption { Never, Eikthyr, Elder, Bonemass, Moder, Yagluth, Queen, Fader, Kall }
    public enum RevealStyle { Translucent, Clear }
    public enum ProgressionSource { Player, World }

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.VersionCheckOnly, VersionStrictness.Minor)]
    [SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.drakexi.valkyriesvision";
        public const string PluginName = "Valkyrie's Vision";
        public const string PluginVersion = "0.7.1";

        private const float WorldRadius = 10000f;
        private const float StartupDelay = 10f;      // seconds after spawning before first check (lets server config sync arrive)
        private const float CheckInterval = 5f;      // seconds between boss-key checks
        private const int PixelsPerFrame = 50000;    // reveal work per frame
        private const int BiomePixelsPerFrame = 20000; // biome lookups per frame

        // Boss order: Eikthyr, Elder, Bonemass, Moder, Yagluth, Queen, Fader, Kall
        private static readonly string[] BossNames = { "Eikthyr", "Elder", "Bonemass", "Moder", "Yagluth", "Queen", "Fader", "Kall" };
        private static readonly string[] DefaultKeys =
        {
            "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon",
            "defeated_goblinking", "defeated_queen", "defeated_fader", "defeated_frozenking_p3"
        };
        private static readonly Heightmap.Biome[] BossBiomes =
        {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain,
            Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth
        };

        private ConfigEntry<RevealMode> _mode;
        private ConfigEntry<RingScaling> _scaling;
        private ConfigEntry<OceanOption> _ocean;
        private ConfigEntry<string> _oceanEarlyKeys;
        private ConfigEntry<bool> _showMessages;
        private ConfigEntry<bool> _debugLogging;
        private ConfigEntry<float> _messageSeconds;
        private ConfigEntry<bool> _playStinger;

        private static FieldInfo _centerTextField;
        private static FieldInfo _stingerField;
        private readonly ConfigEntry<string>[] _bossKeys = new ConfigEntry<string>[8];

        private static FieldInfo _fogField;
        private static Func<Minimap, int, int, bool> _explore;
        private static Func<Minimap, int, int, bool> _exploreOthers;
        private static FieldInfo _exploredField;
        private static FieldInfo _exploredOthersField;
        private static bool _useDirectMarking;
        private string _failedSignature;
        private ConfigEntry<RevealStyle> _style;
        private ConfigEntry<ProgressionSource> _source;

        private bool _inWorld;
        private bool _busy;
        private float _worldReadyTime;
        private float _nextCheck;
        private string _appliedSignature;
        private Heightmap.Biome[] _biomeCache;
        private Heightmap.Biome _appliedMask;

        private static readonly Heightmap.Biome[] MessageOrder =
        {
            Heightmap.Biome.Meadows, Heightmap.Biome.BlackForest, Heightmap.Biome.Swamp, Heightmap.Biome.Mountain,
            Heightmap.Biome.Plains, Heightmap.Biome.Mistlands, Heightmap.Biome.AshLands, Heightmap.Biome.DeepNorth,
            Heightmap.Biome.Ocean
        };

        private void Awake()
        {
            _mode = Config.Bind("General", "RevealMode", RevealMode.Biome,
                Desc("Biome: each boss reveals its own biome everywhere. Rings: each boss reveals a larger circle from the world center."));
            _scaling = Config.Bind("General", "RingScaling", RingScaling.EqualArea,
                Desc("Rings mode only. EqualRadius: each boss adds 1/8 of the radius. EqualArea: each boss adds 1/8 of the map area."));
            _ocean = Config.Bind("General", "OceanReveal", OceanOption.Queen,
                Desc("Biome mode only. Which boss kill reveals the ocean. Never = only the early keys below can reveal it."));
            _style = Config.Bind("General", "RevealStyle", RevealStyle.Translucent,
                Desc("Translucent: revealed areas look like map data shared by other players (a light haze). Clear: fully revealed, like your own exploration."));
            _source = Config.Bind("General", "ProgressionSource", ProgressionSource.Player,
                Desc("Player: each player only gets reveals for bosses they were present for (like the player-based raids world modifier). World: reveals for every boss anyone on the world has defeated."));
            _oceanEarlyKeys = Config.Bind("General", "OceanRevealEarlyKeys", "BossHildir1,BossHildir2,BossHildir3",
                Desc("Biome mode only. Comma-separated world keys that reveal the ocean early once ALL are set (default: Hildir's three mini-bosses). Blank = no early reveal."));

            // Personal (not server-synced) settings
            _showMessages = Config.Bind("Personal", "ShowMessages", true,
                new ConfigDescription("Show an on-screen message when new areas are revealed. Personal setting, not controlled by the server."));
            _messageSeconds = Config.Bind("Personal", "MessageSeconds", 5f,
                new ConfigDescription("How many seconds the reveal message stays fully visible before fading.", new AcceptableValueRange<float>(1f, 20f)));
            _playStinger = Config.Bind("Personal", "PlayStinger", true,
                new ConfigDescription("Play the game's 'new biome discovered' sound with the reveal message."));
            _debugLogging = Config.Bind("Personal", "DebugLogging", false,
                new ConfigDescription("Write the list of bosses and keyed creatures to the BepInEx log on world load."));

            for (int i = 0; i < 8; i++)
            {
                _bossKeys[i] = Config.Bind("BossKeys", BossNames[i], DefaultKeys[i],
                    Desc($"World key set when {BossNames[i]} is defeated. Blank = ignored."));
            }

            _fogField = typeof(Minimap).GetField("m_fogTexture", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo exploreMethod = typeof(Minimap).GetMethod("Explore", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(int), typeof(int) }, null);

            MethodInfo exploreOthersMethod = typeof(Minimap).GetMethod("ExploreOthers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(int), typeof(int) }, null);
            _exploredField = typeof(Minimap).GetField("m_explored", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            _exploredOthersField = typeof(Minimap).GetField("m_exploredOthers", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (_fogField == null || exploreMethod == null || exploreOthersMethod == null || _exploredField == null || _exploredOthersField == null)
            {
                this.Logger.LogError("Could not find Minimap internals (m_fogTexture / m_explored / Explore / ExploreOthers). Mod disabled.");
                enabled = false;
                return;
            }

            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _centerTextField = typeof(MessageHud).GetField("m_messageCenterText", any);
            _stingerField = typeof(MessageHud).GetField("m_biomeFoundStinger", any);

            _explore = (Func<Minimap, int, int, bool>)Delegate.CreateDelegate(typeof(Func<Minimap, int, int, bool>), exploreMethod);
            _exploreOthers = (Func<Minimap, int, int, bool>)Delegate.CreateDelegate(typeof(Func<Minimap, int, int, bool>), exploreOthersMethod);
            this.Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private static ConfigDescription Desc(string text)
        {
            return new ConfigDescription(text, null, new ConfigurationManagerAttributes { IsAdminOnly = true });
        }

        private void Update()
        {
            bool ready = Player.m_localPlayer != null && Minimap.instance != null
                         && WorldGenerator.instance != null && ZoneSystem.instance != null;

            if (!ready)
            {
                if (_inWorld) ResetSession();
                return;
            }

            if (!_inWorld)
            {
                _inWorld = true;
                _worldReadyTime = Time.time;
                if (_debugLogging.Value) DumpBosses();
            }

            if (_busy || Time.time - _worldReadyTime < StartupDelay || Time.time < _nextCheck) return;
            _nextCheck = Time.time + CheckInterval;

            bool[] defeated = new bool[8];
            for (int i = 0; i < 8; i++)
            {
                string key = _bossKeys[i].Value;
                defeated[i] = !string.IsNullOrEmpty(key) && KeySet(key);
            }

            int oceanIndex = (int)_ocean.Value - 1; // Never = -1
            bool ocean = (oceanIndex >= 0 && defeated[oceanIndex]) || AllKeysSet(_oceanEarlyKeys.Value);

            string signature = $"{_mode.Value}|{_scaling.Value}|{_style.Value}|{_source.Value}|{ocean}|{string.Join(",", Array.ConvertAll(defeated, d => d ? "1" : "0"))}";
            if (signature == _appliedSignature || signature == _failedSignature) return;

            StartCoroutine(RevealRoutine(signature, defeated, ocean));
        }

        private static string BiomeLine(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows: return "The Valkyrie's vision sweeps across the Meadows.";
                case Heightmap.Biome.BlackForest: return "The Valkyrie's vision pierces the canopy of the Black Forest.";
                case Heightmap.Biome.Swamp: return "The Valkyrie's vision cuts through the gloom of the Swamp.";
                case Heightmap.Biome.Mountain: return "The Valkyrie's vision soars over the peaks of the Mountains.";
                case Heightmap.Biome.Plains: return "The Valkyrie's vision stretches across the Plains.";
                case Heightmap.Biome.Mistlands: return "The Valkyrie's vision parts the fog of the Mistlands.";
                case Heightmap.Biome.AshLands: return "The Valkyrie's vision burns through the smoke of the Ashlands.";
                case Heightmap.Biome.DeepNorth: return "The Valkyrie's vision falls upon the frozen Deep North.";
                case Heightmap.Biome.Ocean: return "The Valkyrie's vision glides across the open seas.";
                default: return null;
            }
        }

        private static string BiomeMessage(Heightmap.Biome newBiomes)
        {
            const string catchUp = "The Valkyrie's vision reveals the lands of your fallen foes.";
            if (newBiomes == (Heightmap.Biome.Mistlands | Heightmap.Biome.Ocean))
                return "The Valkyrie's vision parts the fog of the Mistlands and glides across the open seas.";

            int found = 0;
            Heightmap.Biome single = Heightmap.Biome.None;
            foreach (Heightmap.Biome b in MessageOrder)
            {
                if ((newBiomes & b) != Heightmap.Biome.None) { found++; single = b; }
            }
            return found == 1 ? BiomeLine(single) : catchUp;
        }

        private void ShowCenterMessage(string text)
        {
            MessageHud hud = MessageHud.instance;
            if (string.IsNullOrEmpty(text) || hud == null) return;

            // Sound: spawn the same stinger the game uses for "new biome discovered"
            if (_playStinger.Value && _stingerField != null)
            {
                UnityEngine.Object stinger = _stingerField.GetValue(hud) as UnityEngine.Object;
                if (stinger != null) UnityEngine.Object.Instantiate(stinger);
            }

            if (Hud.IsUserHidden()) return;

            // Text: drive the game's center text box ourselves so it can stay up longer
            UnityEngine.UI.Graphic centerText = _centerTextField != null ? _centerTextField.GetValue(hud) as UnityEngine.UI.Graphic : null;
            PropertyInfo textProp = centerText != null ? centerText.GetType().GetProperty("text") : null;
            if (centerText == null || textProp == null)
            {
                hud.ShowMessage(MessageHud.MessageType.Center, text);
                return;
            }
            StartCoroutine(HoldCenterText(centerText, textProp, text));
        }

        private IEnumerator HoldCenterText(UnityEngine.UI.Graphic centerText, PropertyInfo textProp, string text)
        {
            textProp.SetValue(centerText, text, null);
            centerText.CrossFadeAlpha(1f, 0f, true);

            float end = Time.unscaledTime + _messageSeconds.Value;
            while (Time.unscaledTime < end)
            {
                yield return null;
                // Stop holding if the game replaced the text with its own message
                if (centerText == null || (string)textProp.GetValue(centerText, null) != text) yield break;
            }
            centerText.CrossFadeAlpha(0f, 1.5f, true);
        }

        private bool KeySet(string key)
        {
            return _source.Value == ProgressionSource.Player
                ? Player.m_localPlayer != null && Player.m_localPlayer.HaveUniqueKey(key)
                : ZoneSystem.instance.GetGlobalKey(key);
        }

        private bool AllKeysSet(string list)
        {
            if (string.IsNullOrWhiteSpace(list)) return false;
            bool any = false;
            foreach (string raw in list.Split(','))
            {
                string key = raw.Trim();
                if (key.Length == 0) continue;
                any = true;
                if (!KeySet(key)) return false;
            }
            return any;
        }

        private void ResetSession()
        {
            StopAllCoroutines();
            _inWorld = false;
            _busy = false;
            _appliedSignature = null;
            _failedSignature = null;
            _biomeCache = null;
            _appliedMask = Heightmap.Biome.None;
        }

        private void DumpBosses()
        {
            if (ZNetScene.instance == null) return;
            foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
            {
                if (prefab == null) continue;
                Character character = prefab.GetComponent<Character>();
                if (character == null) continue;
                if (character.m_boss)
                {
                    this.Logger.LogInfo($"Boss prefab: {prefab.name} | defeat key: '{character.m_defeatSetGlobalKey}'");
                }
                else if (!string.IsNullOrEmpty(character.m_defeatSetGlobalKey))
                {
                    this.Logger.LogInfo($"Keyed creature: {prefab.name} | defeat key: '{character.m_defeatSetGlobalKey}'");
                }
            }
        }

        private IEnumerator BuildBiomeCache(Minimap map, int size, float pixel)
        {
            var cache = new Heightmap.Biome[size * size];
            int half = size / 2;
            float halfPixel = pixel / 2f;
            int work = 0;

            for (int y = 0; y < size; y++)
            {
                float wy = (y - half) * pixel + halfPixel;
                for (int x = 0; x < size; x++)
                {
                    float wx = (x - half) * pixel + halfPixel;
                    cache[y * size + x] = WorldGenerator.instance.GetBiome(wx, wy);
                    if (++work >= BiomePixelsPerFrame)
                    {
                        work = 0;
                        yield return null;
                        if (Minimap.instance != map) yield break;
                    }
                }
            }

            _biomeCache = cache;
        }

        // Runs the reveal, but catches any error (usually another mod patching the same map code),
        // logs it clearly, and always releases the busy flag so the mod never gets stuck.
        private IEnumerator RevealRoutine(string signature, bool[] defeated, bool ocean)
        {
            _busy = true;
            IEnumerator inner = RevealRoutineInner(signature, defeated, ocean);
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) break;
                    current = inner.Current;
                }
                catch (Exception ex)
                {
                    _failedSignature = signature;
                    this.Logger.LogError("Reveal failed. This is usually a conflict with another mod that changes the map " +
                                         "(map reveal or exploration mods). It will be retried when your progress or settings change. Details: " + ex);
                    break;
                }
                yield return current;
            }
            _busy = false;
        }

        private IEnumerator RevealRoutineInner(string signature, bool[] defeated, bool ocean)
        {
            Minimap map = Minimap.instance;
            bool translucent = _style.Value == RevealStyle.Translucent;
            Func<Minimap, int, int, bool> mark = translucent ? _exploreOthers : _explore;
            System.Collections.BitArray exploredOthers = (System.Collections.BitArray)_exploredOthersField.GetValue(map);
            Texture2D fog = (Texture2D)_fogField.GetValue(map);
            System.Collections.BitArray explored = (System.Collections.BitArray)_exploredField.GetValue(map);
            int size = map.m_textureSize;
            float pixel = map.m_pixelSize;
            int half = size / 2;
            float halfPixel = pixel / 2f;

            int count = 0;
            foreach (bool d in defeated) if (d) count++;

            RevealMode mode = _mode.Value;
            Heightmap.Biome mask = Heightmap.Biome.None;
            float radiusSq = 0f;
            bool revealAll = count >= 8;

            if (mode == RevealMode.Biome)
            {
                for (int i = 0; i < 8; i++) if (defeated[i]) mask |= BossBiomes[i];
                if (ocean) mask |= Heightmap.Biome.Ocean;

                if (mask != Heightmap.Biome.None && _biomeCache == null)
                {
                    this.Logger.LogInfo("Building biome map...");
                    IEnumerator build = BuildBiomeCache(map, size, pixel);
                    while (build.MoveNext()) yield return build.Current;
                    if (_biomeCache == null) { _busy = false; yield break; }
                }
            }
            else
            {
                float fraction = count / 8f;
                float radius = _scaling.Value == RingScaling.EqualRadius
                    ? WorldRadius * fraction
                    : WorldRadius * Mathf.Sqrt(fraction);
                radiusSq = radius * radius;
            }

            int revealed = 0;
            int work = 0;
            bool anything = mode == RevealMode.Biome ? mask != Heightmap.Biome.None : count > 0;

            if (anything)
            {
                for (int y = 0; y < size; y++)
                {
                    float wy = (y - half) * pixel + halfPixel;
                    for (int x = 0; x < size; x++)
                    {
                        bool reveal;
                        if (mode == RevealMode.Biome)
                        {
                            reveal = (_biomeCache[y * size + x] & mask) != Heightmap.Biome.None;
                        }
                        else
                        {
                            float wx = (x - half) * pixel + halfPixel;
                            reveal = revealAll || (wx * wx + wy * wy) <= radiusSq;
                        }

                        // In translucent mode, skip pixels the player already explored themselves (already fully clear)
                        if (reveal && !(translucent && explored[y * size + x]) && Mark(mark, map, x, y, translucent, explored, exploredOthers, fog, size)) revealed++;

                        if (++work >= PixelsPerFrame)
                        {
                            work = 0;
                            yield return null;
                            if (Minimap.instance != map) { _busy = false; yield break; }
                        }
                    }
                }
            }

            if (revealed > 0)
            {
                fog.Apply();
            }

            this.Logger.LogInfo($"Reveal pass done: mode={mode}, bosses={count}, ocean={ocean}, newly revealed pixels={revealed}");

            if (revealed > 0 && _showMessages.Value)
            {
                string text = mode == RevealMode.Biome
                    ? BiomeMessage(mask & ~_appliedMask)
                    : (revealAll ? "The Valkyrie's vision now spans the entire world."
                                 : "The Valkyrie's vision reaches farther across the world.");
                ShowCenterMessage(text);
            }

            if (mode == RevealMode.Biome) _appliedMask = mask;
            _appliedSignature = signature;
        }

        // Marks one map pixel using the game's own function. If another mod has hooked that function and it throws,
        // switch (for the rest of the session) to marking the pixel directly, exactly the way the game's function does.
        private bool Mark(Func<Minimap, int, int, bool> gameMark, Minimap map, int x, int y, bool translucent,
                          System.Collections.BitArray explored, System.Collections.BitArray exploredOthers, Texture2D fog, int size)
        {
            if (!_useDirectMarking)
            {
                try
                {
                    return gameMark(map, x, y);
                }
                catch (Exception ex)
                {
                    _useDirectMarking = true;
                    this.Logger.LogWarning("The game's map-reveal function threw an error, most likely because another map mod hooks it. " +
                                           "Switching to direct map marking so reveals still work. Details: " + ex);
                }
            }

            System.Collections.BitArray target = translucent ? exploredOthers : explored;
            int index = y * size + x;
            if (target[index]) return false;
            Color pixel = fog.GetPixel(x, y);
            if (translucent) pixel.g = 0f; else pixel.r = 0f;
            fog.SetPixel(x, y, pixel);
            target[index] = true;
            return true;
        }
    }
}
