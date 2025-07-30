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
    [BepInPlugin("Pleb.WeaponsInLevel", "Weapons In Level", "1.5.30")]
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

        internal static ConfigEntry<bool> SpawnPowerCrystals;
        internal static ConfigEntry<float> PowerCrystalSpawnChance;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo("WeaponsInLevel plugin loaded.");

            _harmony = new Harmony("Pleb.WeaponsInLevel");
            _harmony.PatchAll(typeof(Plugin));

            // Guns
            SpawnGuns = Config.Bind("Weapons", "SpawnGuns", true, "Whether guns can spawn in levels.");
            GunSpawnChance = Config.Bind("Weapons", "GunSpawnChance", 1.5f,
                new ConfigDescription("Chance (0-100) for a gun to spawn per medium volume.",
                new AcceptableValueRange<float>(0f, 100f)));

            // Grenades
            SpawnGrenades = Config.Bind("Grenades", "SpawnGrenades", true, "Whether grenades can spawn in levels.");
            GrenadeSpawnChance = Config.Bind("Grenades", "GrenadeSpawnChance", 0.75f,
                new ConfigDescription("Chance (0-100) for a grenade to spawn per tiny volume.",
                new AcceptableValueRange<float>(0f, 100f)));

            // Melee
            SpawnMelee = Config.Bind("Melee", "SpawnMelee", true, "Whether melee weapons can spawn in levels.");
            MeleeSpawnChance = Config.Bind("Melee", "MeleeSpawnChance", 1.0f,
                new ConfigDescription("Chance (0-100) for a melee weapon to spawn per small volume.",
                new AcceptableValueRange<float>(0f, 100f)));

            // Mines
            SpawnMines = Config.Bind("Mines", "SpawnMines", false, "Whether mines can spawn in levels (default off).");
            MineSpawnChance = Config.Bind("Mines", "MineSpawnChance", 0.5f,
                new ConfigDescription("Chance (0-100) for a mine to spawn per tiny volume.",
                new AcceptableValueRange<float>(0f, 100f)));

            // Power Crystals
            SpawnPowerCrystals = Config.Bind("PowerCrystals", "SpawnPowerCrystals", true,
                "Whether power crystals can spawn in levels.");
            PowerCrystalSpawnChance = Config.Bind("PowerCrystals", "PowerCrystalSpawnChance", 2.0f,
                new ConfigDescription("Chance (0-100) for a power crystal to spawn per small volume.",
                new AcceptableValueRange<float>(0f, 100f)));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(ValuableDirector), "VolumesAndSwitchSetup")]
        [HarmonyAfter("REPO_Shop_Items_in_Level")]
        public static void ValuableDirector_VolumesAndSwitchSetup_Postfix(ValuableDirector __instance)
        {
            if (!SemiFunc.RunIsLevel()) return;

            var allVolumes = Object.FindObjectsOfType<ValuableVolume>(false)
                .Where(v => v.gameObject.GetComponent<UsedVolumeTracker>() == null)
                .Where(v => !HasValuablePropSwitch(v))
                .ToList();

            var tinyVolumes = allVolumes.Where(v => (int)v.VolumeType == 0).ToList();
            var smallVolumes = allVolumes.Where(v => (int)v.VolumeType == 1).ToList();
            var mediumVolumes = allVolumes.Where(v => (int)v.VolumeType == 2).ToList();
            var largeVolumes = allVolumes.Where(v => (int)v.VolumeType == 3).ToList();

            Instance.Logger.LogInfo($"Volumes detected - Tiny: {tinyVolumes.Count}, Small: {smallVolumes.Count}, Medium: {mediumVolumes.Count}, Large: {largeVolumes.Count}");

            int totalSpawned = 0, gunsSpawned = 0, crystalsSpawned = 0;
            bool cartCannonSpawned = false;

            // Tiny volumes
            foreach (var volume in tinyVolumes)
            {
                if (SpawnGrenades.Value && Random.Range(0f, 100f) <= GrenadeSpawnChance.Value &&
                    TryGetRandomItem(SemiFunc.itemType.grenade, out Item grenade))
                {
                    SpawnItem(grenade, volume.transform.position, volume.transform.rotation);
                    totalSpawned++;
                }

                if (SpawnMines.Value && Random.Range(0f, 100f) <= MineSpawnChance.Value &&
                    TryGetRandomItem(SemiFunc.itemType.mine, out Item mine))
                {
                    SpawnItem(mine, volume.transform.position, volume.transform.rotation);
                    totalSpawned++;
                }
            }

            // Small volumes (melee + power crystals)
            foreach (var volume in smallVolumes)
            {
                if (SpawnMelee.Value && Random.Range(0f, 100f) <= MeleeSpawnChance.Value &&
                    TryGetRandomItem(SemiFunc.itemType.melee, out Item melee))
                {
                    SpawnItem(melee, volume.transform.position, volume.transform.rotation);
                    totalSpawned++;
                }

                if (RunManager.instance.levelsCompleted > 0 &&
                    SpawnPowerCrystals.Value && Random.Range(0f, 100f) <= PowerCrystalSpawnChance.Value &&
                    TryGetRandomItem(SemiFunc.itemType.power_crystal, out Item crystal))
                {
                    SpawnItem(crystal, volume.transform.position, volume.transform.rotation);
                    totalSpawned++;
                    crystalsSpawned++;
                }
            }

            // Medium volumes (guns only, with Cart Cannon redirect)
            foreach (var volume in mediumVolumes)
            {
                if (!SpawnGuns.Value || Random.Range(0f, 100f) > GunSpawnChance.Value) continue;

                Item gun;
                int attempts = 0;

                do
                {
                    attempts++;
                    if (!TryGetRandomItem(SemiFunc.itemType.gun, out gun))
                        break;

                    // If Cart Cannon is picked
                    if (gun.name == "Item Cart Cannon")
                    {
                        if (cartCannonSpawned)
                        {
                            // Already spawned one, try another gun
                            continue;
                        }

                        if (largeVolumes.Any())
                        {
                            // Place Cart Cannon in large volume
                            var largeVolume = largeVolumes[Random.Range(0, largeVolumes.Count)];
                            SpawnItem(gun, largeVolume.transform.position, largeVolume.transform.rotation);
                            cartCannonSpawned = true;
                            gunsSpawned++;
                            totalSpawned++;
                        }
                        // If no large volumes, skip and pick another gun
                        continue;
                    }

                    // Regular gun spawn
                    SpawnItem(gun, volume.transform.position, volume.transform.rotation);
                    gunsSpawned++;
                    totalSpawned++;
                    break;

                } while (attempts < 5);
            }

            Instance.Logger.LogInfo($"Spawned {totalSpawned} items (Guns: {gunsSpawned}, Crystals: {crystalsSpawned}, Cart Cannon: {(cartCannonSpawned ? 1 : 0)}).");
        }

        [HarmonyPatch(typeof(ExtractionPoint), "DestroyAllPhysObjectsInHaulList")]
        [HarmonyPostfix]
        public static void ExtractionPoint_DestroyAllPhysObjectsInHaulList_Postfix(ExtractionPoint __instance)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;

            var spawnedItemGameObjects = Object.FindObjectsOfType<SpawnedItemTracker>(false)
                .Select(tracker => tracker.gameObject)
                .ToList();

            foreach (var gameObject in spawnedItemGameObjects)
            {
                var roomVolumeCheck = gameObject.GetComponent<RoomVolumeCheck>();
                if (roomVolumeCheck == null) continue;

                if (roomVolumeCheck.CurrentRooms.Any(room => room.Extraction))
                {
                    var itemAttr = gameObject.GetComponent<ItemAttributes>();
                    if (itemAttr == null) continue;

                    if (itemAttr.item.itemType == SemiFunc.itemType.power_crystal && ChargingStation.instance != null)
                    {
                        AddChargeToStation();
                    }

                    StatsManager.instance.ItemPurchase(itemAttr.item.itemAssetName);
                    Instance.Logger.LogInfo($"Extracted {gameObject.name} at {__instance.name}.");
                    gameObject.GetComponent<PhysGrabObject>()?.DestroyPhysGrabObject();
                }
            }
        }

        private static void AddChargeToStation()
        {
            var cs = ChargingStation.instance;
            if (cs == null) return;

            var type = typeof(ChargingStation);
            var chargeIntField = type.GetField("chargeInt", BindingFlags.NonPublic | BindingFlags.Instance);
            var chargeTotalField = type.GetField("chargeTotal", BindingFlags.NonPublic | BindingFlags.Instance);
            var maxCrystalsField = type.GetField("maxCrystals", BindingFlags.NonPublic | BindingFlags.Instance);
            var energyPerCrystalField = type.GetField("energyPerCrystal", BindingFlags.NonPublic | BindingFlags.Instance);
            var photonViewField = type.GetField("photonView", BindingFlags.NonPublic | BindingFlags.Instance);
            var chargeFloatField = type.GetField("chargeFloat", BindingFlags.NonPublic | BindingFlags.Instance);
            var chargeSegmentField = type.GetField("chargeSegmentCurrent", BindingFlags.NonPublic | BindingFlags.Instance);
            var chargeSegmentsField = type.GetField("chargeSegments", BindingFlags.NonPublic | BindingFlags.Instance);
            var chargeScaleTargetField = type.GetField("chargeScaleTarget", BindingFlags.NonPublic | BindingFlags.Instance);

            if (chargeIntField == null || chargeTotalField == null || maxCrystalsField == null || energyPerCrystalField == null)
                return;

            int chargeInt = (int)chargeIntField.GetValue(cs);
            int chargeTotal = (int)chargeTotalField.GetValue(cs);
            int maxCrystals = (int)maxCrystalsField.GetValue(cs);
            int energyPerCrystal = (int)energyPerCrystalField.GetValue(cs);

            chargeInt = Mathf.Clamp(chargeInt + 1, 0, maxCrystals);
            chargeTotal = Mathf.Clamp(chargeTotal + energyPerCrystal, 0, maxCrystals * energyPerCrystal);

            chargeIntField.SetValue(cs, chargeInt);
            chargeTotalField.SetValue(cs, chargeTotal);
            StatsManager.instance.runStats["chargingStationCharge"] = chargeInt;
            StatsManager.instance.runStats["chargingStationChargeTotal"] = chargeTotal;

            int chargeSegments = (int)chargeSegmentsField.GetValue(cs);
            float chargeFloat = (float)chargeTotal / 100f;
            int chargeSegmentCurrent = Mathf.RoundToInt(chargeFloat * chargeSegments);
            chargeFloatField.SetValue(cs, chargeFloat);
            chargeSegmentField.SetValue(cs, chargeSegmentCurrent);
            chargeScaleTargetField.SetValue(cs, (float)chargeSegmentCurrent / chargeSegments);

            PhotonView photonView = photonViewField?.GetValue(cs) as PhotonView;
            if (SemiFunc.IsMultiplayer() && photonView != null)
                photonView.RPC("ChargingStationSegmentChangedRPC", RpcTarget.AllBuffered, (byte)chargeSegmentCurrent);

            Instance.Logger.LogInfo($"ChargingStation charge updated: {chargeTotal}/{maxCrystals * energyPerCrystal} (Crystals: {chargeInt}/{maxCrystals}).");
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
            GameObject go = SemiFunc.IsMultiplayer()
                ? PhotonNetwork.Instantiate("Items/" + item.name, position, rotation, 0, null)
                : Object.Instantiate(item.prefab, position, rotation);

            go.AddComponent<SpawnedItemTracker>();

            if (item.itemType == SemiFunc.itemType.power_crystal)
                Instance.Logger.LogInfo("[WeaponsInLevel] Spawned power crystal.");

            return go;
        }
    }
}
