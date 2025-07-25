using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using REPO_Shop_Items_in_Level;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using Photon.Pun;
using System.Reflection;

namespace WeaponsInLevel
{
    [BepInPlugin("Pleb.WeaponsInLevel", "Weapons In Level", "1.5.1")]
    [BepInDependency("REPO_Shop_Items_in_Level")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }

        internal static ConfigEntry<bool> SpawnGuns;
        internal static ConfigEntry<float> GunSpawnChance;

        internal static ConfigEntry<bool> SpawnGrenades;
        internal static ConfigEntry<float> GrenadeSpawnChance;

        internal static ConfigEntry<bool> SpawnMelee;
        internal static ConfigEntry<float> MeleeSpawnChance;

        internal static ConfigEntry<bool> SpawnMines;
        internal static ConfigEntry<float> MineSpawnChance;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo("WeaponsInLevel plugin loaded.");

            _harmony = new Harmony("Pleb.WeaponsInLevel");
            _harmony.PatchAll(typeof(Plugin));

            // Guns
            SpawnGuns = Config.Bind(
                "Weapons",
                "SpawnGuns",
                true,
                "Whether guns can spawn in levels."
            );

            GunSpawnChance = Config.Bind(
                "Weapons",
                "GunSpawnChance",
                1.5f,
                new ConfigDescription("Chance (0-100) for a gun to spawn per medium volume.",
                    new AcceptableValueRange<float>(0f, 100f))
            );

            // Grenades
            SpawnGrenades = Config.Bind(
                "Grenades",
                "SpawnGrenades",
                true,
                "Whether grenades can spawn in levels."
            );

            GrenadeSpawnChance = Config.Bind(
                "Grenades",
                "GrenadeSpawnChance",
                0.75f,
                new ConfigDescription("Chance (0-100) for a grenade to spawn per tiny volume.",
                    new AcceptableValueRange<float>(0f, 100f))
            );

            // Melee
            SpawnMelee = Config.Bind(
                "Melee",
                "SpawnMelee",
                true,
                "Whether melee weapons can spawn in levels."
            );

            MeleeSpawnChance = Config.Bind(
                "Melee",
                "MeleeSpawnChance",
                1.0f,
                new ConfigDescription("Chance (0-100) for a melee weapon to spawn per tiny volume.",
                    new AcceptableValueRange<float>(0f, 100f))
            );

            // Mines
            SpawnMines = Config.Bind(
                "Mines",
                "SpawnMines",
                false,
                "Whether mines can spawn in levels (default off)."
            );

            MineSpawnChance = Config.Bind(
                "Mines",
                "MineSpawnChance",
                0.5f,
                new ConfigDescription("Chance (0-100) for a mine to spawn per tiny volume.",
                    new AcceptableValueRange<float>(0f, 100f))
            );
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ValuableDirector), "VolumesAndSwitchSetup")]
        public static void ValuableDirector_VolumesAndSwitchSetup_Postfix(ValuableDirector __instance)
        {
            if (!SemiFunc.RunIsLevel()) return;

            // Volume groups
            var tinyVolumes = Object.FindObjectsOfType<ValuableVolume>(false)
                .Where(v => (int)v.VolumeType == 0)
                .Where(v => v.gameObject.GetComponent<UsedVolumeTracker>() == null)
                .Where(v => !HasValuablePropSwitch(v))
                .ToList();

            var mediumVolumes = Object.FindObjectsOfType<ValuableVolume>(false)
                .Where(v => (int)v.VolumeType == 2)
                .Where(v => v.gameObject.GetComponent<UsedVolumeTracker>() == null)
                .Where(v => !HasValuablePropSwitch(v))
                .ToList();

            // Log shop-style summary
            Plugin.Instance.Logger.LogInfo($"Grenade spawn chance: {GrenadeSpawnChance.Value}% on {tinyVolumes.Count} tiny volumes");
            Plugin.Instance.Logger.LogInfo($"Melee spawn chance: {MeleeSpawnChance.Value}% on {tinyVolumes.Count} tiny volumes");
            Plugin.Instance.Logger.LogInfo($"Mine spawn chance: {MineSpawnChance.Value}% on {tinyVolumes.Count} tiny volumes");
            Plugin.Instance.Logger.LogInfo($"Gun spawn chance: {GunSpawnChance.Value}% on {mediumVolumes.Count} medium volumes");

            int totalSpawned = 0;

            // Tiny volumes
            foreach (var volume in tinyVolumes)
            {
                if (SpawnGrenades.Value && Random.Range(0f, 100f) <= GrenadeSpawnChance.Value &&
                    TryGetRandomItem(SemiFunc.itemType.grenade, out Item grenade))
                {
                    SpawnItem(grenade, volume.transform.position, volume.transform.rotation);
                    totalSpawned++;
                }

                if (SpawnMelee.Value && Random.Range(0f, 100f) <= MeleeSpawnChance.Value &&
                    TryGetRandomItem(SemiFunc.itemType.melee, out Item melee))
                {
                    SpawnItem(melee, volume.transform.position, volume.transform.rotation);
                    totalSpawned++;
                }

                if (SpawnMines.Value && Random.Range(0f, 100f) <= MineSpawnChance.Value &&
                    TryGetRandomItem(SemiFunc.itemType.mine, out Item mine))
                {
                    SpawnItem(mine, volume.transform.position, volume.transform.rotation);
                    totalSpawned++;
                }
            }

            // Medium volumes
            foreach (var volume in mediumVolumes)
            {
                if (SpawnGuns.Value && Random.Range(0f, 100f) <= GunSpawnChance.Value &&
                    TryGetRandomItem(SemiFunc.itemType.gun, out Item gun))
                {
                    SpawnItem(gun, volume.transform.position, volume.transform.rotation);
                    totalSpawned++;
                }
            }

            Plugin.Instance.Logger.LogInfo($"WeaponsInLevel: Spawned {totalSpawned} items (guns, grenades, melee, mines).");
        }

        private static bool HasValuablePropSwitch(ValuableVolume volume)
        {
            return volume.transform.GetComponentInParent<ValuablePropSwitch>() != null;
        }

        private static bool TryGetRandomItem(SemiFunc.itemType type, out Item item)
        {
            item = null;
            var items = StatsManager.instance.itemDictionary.Values
                .Where(i => i.itemType == type)
                .ToList();

            if (items.Count == 0) return false;
            item = items[Random.Range(0, items.Count)];
            return true;
        }

        private static GameObject SpawnItem(Item item, Vector3 position, Quaternion rotation)
        {
            GameObject go;
            if (SemiFunc.IsMultiplayer())
            {
                go = PhotonNetwork.Instantiate("Items/" + item.name, position, rotation, 0, null);
            }
            else
            {
                go = Object.Instantiate(item.prefab, position, rotation);
            }

            go.AddComponent<SpawnedItemTracker>();
            return go;
        }
    }
}
