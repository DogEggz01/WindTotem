using System;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace WindTotem
{
    [BepInPlugin("DogEggz.WindTotem", "Wind Totem", "1.0.6")]
    public sealed class WindTotemPlugin : BaseUnityPlugin
    {
        internal const int PrefabIndex = 609;
        internal const int QuestPrefabIndex = 608;
        private static WindTotemPlugin instance;
        private AssetBundle bundle;
        private Harmony patches;
        private GameObject prefab;
        private GameObject templateHolder;
        private GameObject questPrefab;
        internal static void Log(string text) { instance.Logger.LogInfo(text); }
        internal static void Error(string text) { instance.Logger.LogError(text); }
        internal static bool Ready => instance && instance.prefab && instance.questPrefab &&
            PrefabsDirectory.instance && PrefabsDirectory.instance.directory.Length > PrefabIndex &&
            PrefabsDirectory.instance.directory[PrefabIndex] == instance.prefab &&
            PrefabsDirectory.instance.directory[QuestPrefabIndex] == instance.questPrefab;

        private void Awake()
        {
            instance = this;
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Info.Location), "windtotem_assets");
                bundle = AssetBundle.LoadFromFile(path);
                if (!bundle) throw new InvalidOperationException("Cannot load " + path);
                var visual = bundle.LoadAsset<GameObject>("Assets/Generated/WindTotem.prefab");
                if (!visual) throw new InvalidOperationException("Wind Totem visual prefab is missing.");
                templateHolder = new GameObject("Wind Totem prefab storage");
                templateHolder.SetActive(false);
                DontDestroyOnLoad(templateHolder);
                // ActiveSelf stays true so normal Instantiate calls activate spawned items.
                // The inactive holder prevents ShipItem/Awake from running on the template.
                prefab = Instantiate(visual, templateHolder.transform);
                prefab.name = "609 Wind Totem";
                var item = prefab.AddComponent<ShipItem>();
                item.name = "Wind Totem";
                item.description = "";
                item.big = true;
                item.mass = 3f;
                item.value = 300000;
                item.sold = true;
                item.health = 100f;
                item.holdDistance = 1.3f;
                item.floaterHeight = 0.2f;
                item.inventoryScale = 1f;
                var saveable = prefab.AddComponent<SaveablePrefab>();
                saveable.prefabIndex = PrefabIndex;
                saveable.SetParentObject(-1);
                prefab.AddComponent<PortableTotem>();
                prefab.AddComponent<ItemOutlineSanitizer>();
                var orb = prefab.transform.Find("Visuals/wind_totem_wind/wind_totem_target");
                if (!orb) throw new InvalidOperationException("Wind Totem orb is missing.");
                orb.gameObject.AddComponent<PortableWindOrb>();
                orb.gameObject.AddComponent<ItemOutlineSanitizer>();
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (!material || !material.shader) continue;
                        var shaderName = material.name == "Particle Storm Lightning"
                            ? "Particles/Priority Alpha Blended" : material.shader.name;
                        var shader = Shader.Find(shaderName);
                        if (shader) material.shader = shader;
                    }
                }
                patches = new Harmony("DogEggz.WindTotem");
                patches.PatchAll(typeof(WindTotemPlugin).Assembly);
                Logger.LogInfo("Wind Totem 1.0.6 loaded; quest 608, totem 609; return quest and 60-second base wind hold active.");
            }
            catch (Exception exception)
            {
                Logger.LogError("Wind Totem initialization failed: " + exception);
                try { patches?.UnpatchSelf(); }
                catch (Exception rollback) { Logger.LogError("Patch rollback failed: " + rollback); }
                if (templateHolder) Destroy(templateHolder);
                prefab = null;
            }
        }

        private void CreateQuestPrefab(PrefabsDirectory directory)
        {
            if (questPrefab) return;
            if (directory.directory.Length <= 5 || !directory.directory[5])
                throw new InvalidOperationException("Vanilla tea crate prefab 5 is unavailable.");
            questPrefab = Instantiate(directory.directory[5], templateHolder.transform);
            questPrefab.name = "608 WindTotemQuest";
            // The donor is a trade Good, not an openable storage crate. Retain its
            // visual/ShipItem contract but remove economy and mission behavior.
            var good = questPrefab.GetComponent<Good>();
            if (good) DestroyImmediate(good);
            questPrefab.tag = "Untagged";
            var item = questPrefab.GetComponent<ShipItem>();
            item.name = "WindTotemQuest";
            item.description = "";
            item.lookText = "";
            item.sold = true;
            item.nailed = false;
            item.value = 0;
            var saveable = questPrefab.GetComponent<SaveablePrefab>();
            saveable.prefabIndex = QuestPrefabIndex;
            saveable.instanceId = 0;
            saveable.currentCrateId = 0;
            saveable.SetParentObject(-1);
            questPrefab.AddComponent<ItemOutlineSanitizer>();
        }

        [HarmonyPatch(typeof(PrefabsDirectory), "Start")]
        private static class RegisterPrefab
        {
            [HarmonyPrefix]
            private static void Prefix(PrefabsDirectory __instance)
            {
                if (!instance || !instance.prefab || __instance.directory == null) return;
                var directory = __instance.directory;
                if (directory.Length <= PrefabIndex) Array.Resize(ref directory, PrefabIndex + 1);
                if ((directory[PrefabIndex] && directory[PrefabIndex] != instance.prefab) ||
                    (directory[QuestPrefabIndex] && directory[QuestPrefabIndex] != instance.questPrefab))
                {
                    instance.Logger.LogError("Prefab 608 or 609 is occupied; Wind Totem registration refused.");
                    return;
                }
                try { instance.CreateQuestPrefab(__instance); }
                catch (Exception e)
                {
                    instance.Logger.LogError(e);
                    if (instance.questPrefab) Destroy(instance.questPrefab);
                    instance.questPrefab = null;
                    return;
                }
                directory[PrefabIndex] = instance.prefab;
                directory[QuestPrefabIndex] = instance.questPrefab;
                __instance.directory = directory;
                instance.Logger.LogInfo("Registered WindTotemQuest 608 and Wind Totem 609.");
            }
        }
    }
}
