using System;
using System.Collections.Generic;
using HarmonyLib;

namespace WindTotem
{
    internal static class QuestSave
    {
        private const string Key = "DogEggz.WindTotem.Quest.v1";
        internal static QuestProgress Progress = new QuestProgress();
        internal static bool Valid = true;
        internal static void Reset() { Progress = new QuestProgress(); Valid = true; }
        internal static void Load()
        {
            Reset();
            try
            {
                string data = null;
                GameState.modData?.TryGetValue(Key, out data);
                var states = Quests.instance ? Quests.instance.currentQuests : null;
                Progress = QuestProgress.Reconcile(data, states != null && states.Length > 1 ? states[1] : 0);
                SyncVanilla();
            }
            catch (Exception e) { Valid = false; WindTotemPlugin.Error(e.Message); }
        }
        internal static void Store()
        {
            if (!Valid) return;
            if (GameState.modData == null) GameState.modData = new Dictionary<string, string>();
            GameState.modData[Key] = Progress.Encode();
            SyncVanilla();
        }
        private static void SyncVanilla()
        {
            if (!Valid || !Quests.instance) return;
            var states = Quests.instance.currentQuests ?? new int[0];
            if (states.Length < 2) Array.Resize(ref states, 2);
            states[1] = Progress.Stage == QuestStages.Completed ? -5 : Progress.Stage == QuestStages.Returning ? -2 : Progress.Stage == QuestStages.Outbound ? -1 : 0;
            Quests.instance.currentQuests = states;
        }
    }

    [HarmonyPatch(typeof(SaveLoadManager), "Awake")]
    internal static class QuestNewSession { private static void Prefix() { QuestSave.Reset(); } }
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadGame))]
    internal static class QuestLoading
    {
        private static void Prefix()
        {
            QuestSave.Reset();
            // Vanilla skips assigning modData for old saves with a null dictionary.
            GameState.modData?.Remove("DogEggz.WindTotem.Quest.v1");
        }
    }
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.LoadModData))]
    internal static class QuestLoad { private static void Postfix() { QuestSave.Load(); } }
    [HarmonyPatch(typeof(SaveLoadManager), nameof(SaveLoadManager.SaveModData))]
    internal static class QuestStore { private static void Prefix() { QuestSave.Store(); } }

}
