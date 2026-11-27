using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Knochenbrecher
{
    /// <summary>
    /// Fügt den "Knochenbrecher" hinzu: einen riesigen Zweihand-Hammer mit Wucht- und Geisterschaden,
    /// der gegen den Knochenwanst (Bonemass) zusätzlichen Schaden macht.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class KnochenbrecherPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ady.valheim.knochenbrecher";
        public const string PluginName = "Knochenbrecher";
        public const string PluginVersion = "1.1.1";

        internal const string PrefabName = "SledgeKnochenbrecher";
        internal const string ItemToken = "$item_knochenbrecher";
        private const string BasePrefab = "SledgeIron";

        internal static KnochenbrecherPlugin Instance;

        // --- Konfiguration (wird im Koop vom Host/Server an alle Spieler synchronisiert) ---
        private ConfigEntry<float> _blunt;
        private ConfigEntry<float> _spirit;
        private ConfigEntry<float> _bluntPerLevel;
        private ConfigEntry<float> _spiritPerLevel;
        private ConfigEntry<int> _maxQuality;
        private ConfigEntry<float> _knockback;
        private ConfigEntry<float> _stagger;
        private ConfigEntry<float> _sizeMultiplier;
        private ConfigEntry<float> _rangeMultiplier;
        internal static ConfigEntry<float> BonemassMultiplier;
        internal static ConfigEntry<bool> BonkMessage;
        private ConfigEntry<string> _recipe;
        private ConfigEntry<string> _upgradeRecipe;
        private ConfigEntry<int> _forgeLevel;
        private ConfigEntry<bool> _customModel;
        private ConfigEntry<bool> _flipModel;

        private CustomItem _item;
        private Vector3 _originalAttachScale = Vector3.one;
        private float _originalRange = -1f;
        private float _originalRayWidth = -1f;

        private void Awake()
        {
            Instance = this;
            CreateConfig();
            AddTranslations();

            PrefabManager.OnVanillaPrefabsAvailable += CreateHammer;
            SynchronizationManager.OnConfigurationSynchronized += (_, __) => ApplySettings(true);

            new Harmony(PluginGuid).PatchAll();
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} geladen – bereit zum Bonk.");
        }

        private void CreateConfig()
        {
            var admin = new ConfigurationManagerAttributes { IsAdminOnly = true };

            _blunt = Config.Bind("1 - Schaden", "Wucht", 75f,
                new ConfigDescription("Wuchtschaden auf Stufe 1 (Eisenvorschlaghammer: 35).", null, admin));
            _spirit = Config.Bind("1 - Schaden", "Geist", 25f,
                new ConfigDescription("Geisterschaden auf Stufe 1 – der Knochenwanst ist dagegen empfindlich.", null, admin));
            _bluntPerLevel = Config.Bind("1 - Schaden", "Wucht pro Stufe", 8f,
                new ConfigDescription("Zusätzlicher Wuchtschaden pro Aufwertung.", null, admin));
            _spiritPerLevel = Config.Bind("1 - Schaden", "Geist pro Stufe", 5f,
                new ConfigDescription("Zusätzlicher Geisterschaden pro Aufwertung.", null, admin));
            _maxQuality = Config.Bind("1 - Schaden", "Max. Stufe", 4,
                new ConfigDescription("Höchste Aufwertungsstufe.", new AcceptableValueRange<int>(1, 10), admin));
            BonemassMultiplier = Config.Bind("1 - Schaden", "Knochenwanst-Bonus", 1.5f,
                new ConfigDescription("Schadens-Multiplikator gegen den Knochenwanst (1 = kein Bonus).",
                    new AcceptableValueRange<float>(1f, 10f), admin));

            _knockback = Config.Bind("2 - Wumms", "Rückstoß", 180f,
                new ConfigDescription("Rückstoßkraft des Schlags.", null, admin));
            _stagger = Config.Bind("2 - Wumms", "Taumel-Multiplikator", 2f,
                new ConfigDescription("Wie stark Gegner ins Taumeln geraten.", null, admin));
            _sizeMultiplier = Config.Bind("2 - Wumms", "Größe", 1.6f,
                new ConfigDescription("Wie viel größer der Hammer ist als ein normaler Vorschlaghammer.",
                    new AcceptableValueRange<float>(0.5f, 4f), admin));
            _rangeMultiplier = Config.Bind("2 - Wumms", "Reichweite", 1.3f,
                new ConfigDescription("Multiplikator für den Wirkungsradius des Schlags.",
                    new AcceptableValueRange<float>(0.5f, 3f), admin));
            BonkMessage = Config.Bind("2 - Wumms", "BONK-Meldung", true,
                "Zeigt 'BONK!' an, wenn du den Knochenwanst triffst (nur für dich, nicht synchronisiert).");

            _recipe = Config.Bind("3 - Rezept", "Zutaten", "Iron:20,ElderBark:10,Guck:5,TrophyDraugr:1",
                new ConfigDescription("Zutaten als Liste 'Prefab:Anzahl,…'.", null, admin));
            _upgradeRecipe = Config.Bind("3 - Rezept", "Zutaten pro Aufwertung", "Iron:10,ElderBark:5",
                new ConfigDescription("Zutaten pro Aufwertung als Liste 'Prefab:Anzahl,…'.", null, admin));
            _customModel = Config.Bind("4 - Aussehen", "Eigenes Modell", true,
                "Verwendet das verzierte Silber-Gold-Modell. Aus = Aussehen des Eisenvorschlaghammers. Wirkt nach Neustart.");
            _flipModel = Config.Bind("4 - Aussehen", "Modell umdrehen", false,
                "Einschalten, falls der Hammer verkehrt herum in der Hand liegt (Kopf unten). Wirkt nach Neustart.");

            _forgeLevel = Config.Bind("3 - Rezept", "Schmiede-Stufe", 2,
                new ConfigDescription("Benötigte Stufe der Schmiede.", new AcceptableValueRange<int>(1, 7), admin));
        }

        private static void AddTranslations()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("German", new Dictionary<string, string>
            {
                { "item_knochenbrecher", "Knochenbrecher" },
                { "item_knochenbrecher_desc", "Ein gewaltiger Hammer, wie geschaffen, um dem Knochenwanst eins auf den Kopp zu geben." },
                { "knochenbrecher_bonk", "BONK!" },
            });
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "item_knochenbrecher", "Bonebreaker" },
                { "item_knochenbrecher_desc", "A massive hammer, made for giving Bonemass a good whack on the head." },
                { "knochenbrecher_bonk", "BONK!" },
            });
        }

        private void CreateHammer()
        {
            try
            {
                var config = new ItemConfig
                {
                    Name = ItemToken,
                    Description = ItemToken + "_desc",
                    CraftingStation = CraftingStations.Forge,
                    MinStationLevel = _forgeLevel.Value,
                    Requirements = BuildRequirementConfigs(),
                };

                var icon = LoadIcon();
                if (icon != null)
                {
                    config.Icons = new[] { icon };
                }

                _item = new CustomItem(PrefabName, BasePrefab, config);

                if (_customModel.Value)
                {
                    try
                    {
                        HammerModel.Apply(_item.ItemPrefab, _flipModel.Value);
                    }
                    catch (Exception e)
                    {
                        Jotunn.Logger.LogWarning($"Eigenes Modell konnte nicht geladen werden, der Hammer behält das alte Aussehen: {e}");
                    }
                }

                var attach = _item.ItemPrefab.transform.Find("attach");
                if (attach != null)
                {
                    _originalAttachScale = attach.localScale;
                }
                _originalRange = _item.ItemDrop.m_itemData.m_shared.m_attack.m_attackRange;
                _originalRayWidth = _item.ItemDrop.m_itemData.m_shared.m_attack.m_attackRayWidth;

                ItemManager.Instance.AddItem(_item);
                // Das Rezept steckt schon in der ItemConfig; es wird nur nach einer Synchronisierung neu gesetzt
                ApplySettings(false);
            }
            catch (Exception e)
            {
                Jotunn.Logger.LogError($"Knochenbrecher konnte nicht erstellt werden: {e}");
            }
            finally
            {
                PrefabManager.OnVanillaPrefabsAvailable -= CreateHammer;
            }
        }

        private Sprite LoadIcon()
        {
            var path = Path.Combine(Path.GetDirectoryName(Info.Location) ?? string.Empty, "knochenbrecher_icon.png");
            if (!File.Exists(path))
            {
                Jotunn.Logger.LogInfo("knochenbrecher_icon.png nicht gefunden – verwende das Symbol des Eisenvorschlaghammers.");
                return null;
            }
            return AssetUtils.LoadSpriteFromFile(path);
        }

        /// <summary>Überträgt die (ggf. vom Server synchronisierten) Einstellungen auf den Hammer.</summary>
        private void ApplySettings(bool includeRecipe)
        {
            if (_item?.ItemDrop == null)
            {
                return;
            }

            var shared = _item.ItemDrop.m_itemData.m_shared;
            shared.m_damages = new HitData.DamageTypes { m_blunt = _blunt.Value, m_spirit = _spirit.Value };
            shared.m_damagesPerLevel = new HitData.DamageTypes { m_blunt = _bluntPerLevel.Value, m_spirit = _spiritPerLevel.Value };
            shared.m_maxQuality = _maxQuality.Value;
            shared.m_attackForce = _knockback.Value;
            shared.m_attack.m_staggerMultiplier = _stagger.Value;
            if (_originalRange > 0f)
            {
                shared.m_attack.m_attackRange = _originalRange * _rangeMultiplier.Value;
            }
            if (_originalRayWidth > 0f)
            {
                shared.m_attack.m_attackRayWidth = _originalRayWidth * _rangeMultiplier.Value;
            }

            var attach = _item.ItemPrefab.transform.Find("attach");
            if (attach != null)
            {
                attach.localScale = _originalAttachScale * _sizeMultiplier.Value;
            }

            if (includeRecipe)
            {
                ApplyRecipe();
            }
        }

        private void ApplyRecipe()
        {
            var recipe = _item?.Recipe?.Recipe;
            if (recipe == null || ObjectDB.instance == null)
            {
                return;
            }

            var resources = new List<Piece.Requirement>();
            foreach (var req in BuildRequirementConfigs())
            {
                var prefab = ObjectDB.instance.GetItemPrefab(req.Item);
                var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null)
                {
                    Jotunn.Logger.LogWarning($"Unbekannte Zutat im Rezept: '{req.Item}' – wird ignoriert.");
                    continue;
                }

                resources.Add(new Piece.Requirement
                {
                    m_resItem = drop,
                    m_amount = req.Amount,
                    m_amountPerLevel = req.AmountPerLevel,
                    m_recover = true,
                });
            }

            if (resources.Count > 0)
            {
                recipe.m_resources = resources.ToArray();
            }
            recipe.m_minStationLevel = _forgeLevel.Value;
        }

        private RequirementConfig[] BuildRequirementConfigs()
        {
            var baseCosts = ParseCosts(_recipe.Value);
            var upgradeCosts = ParseCosts(_upgradeRecipe.Value);

            var result = new List<RequirementConfig>();
            foreach (var cost in baseCosts)
            {
                upgradeCosts.TryGetValue(cost.Key, out var perLevel);
                result.Add(new RequirementConfig(cost.Key, cost.Value, perLevel, true));
            }
            foreach (var cost in upgradeCosts)
            {
                if (!baseCosts.ContainsKey(cost.Key))
                {
                    result.Add(new RequirementConfig(cost.Key, 0, cost.Value, true));
                }
            }
            return result.ToArray();
        }

        internal static Dictionary<string, int> ParseCosts(string text)
        {
            var costs = new Dictionary<string, int>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return costs;
            }

            foreach (var part in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var pieces = part.Split(':');
                if (pieces.Length == 2 && int.TryParse(pieces[1].Trim(), out var amount) && amount > 0)
                {
                    var name = pieces[0].Trim();
                    costs[name] = costs.TryGetValue(name, out var existing) ? existing + amount : amount;
                }
                else
                {
                    Jotunn.Logger.LogWarning($"Rezept-Eintrag '{part}' nicht verstanden (Format: Prefab:Anzahl).");
                }
            }
            return costs;
        }
    }
}
