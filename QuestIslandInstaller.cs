using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WindTotem
{
    // The actual island's update is authoritative: it also covers islands which
    // were already loaded before a scene-event subscription was established.
    [HarmonyPatch(typeof(IslandSceneryScene), "Update")]
    internal static class QuestIslandInstaller
    {
        [HarmonyPostfix]
        internal static void Postfix(IslandSceneryScene __instance)
        {
            int index = __instance.parentIslandIndex;
            if (index != 37 && index != 30) return;
            var status = __instance.GetComponent<QuestIslandStatus>();
            if (!status) status = __instance.gameObject.AddComponent<QuestIslandStatus>();
            if (status.Complete || Time.unscaledTime < status.NextAttempt) return;
            status.NextAttempt = Time.unscaledTime + 1f;
            GameObject area = null;
            try
            {
                Transform source = FindTarget(__instance.transform, index);
                if (!source)
                {
                    if (!status.ReportedMissing)
                    {
                        status.ReportedMissing = true;
                        WindTotemPlugin.Error("Quest anchor not found on island " + index + " under " + __instance.name + "; retrying.");
                    }
                    return;
                }
                var marker = source.GetComponent<QuestAnchorMarker>();
                if (marker && marker.Area) { status.Complete = true; return; }
                area = new GameObject(index == 37 ? "Wind Totem quest giver" : "Wind Totem quest delivery");
                area.SetActive(false);
                SceneManager.MoveGameObjectToScene(area, source.gameObject.scene);
                area.transform.position = source.position;
                var body = area.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                // Trigger volume should not intercept the player's pointing ray.
                area.layer = 2;
                Collider trigger;
                if (index == 37) { var sphere = area.AddComponent<SphereCollider>(); sphere.radius = 4f; trigger = sphere; }
                else { var box = area.AddComponent<BoxCollider>(); box.size = Vector3.one * 3f; trigger = box; }
                trigger.isTrigger = true;
                var quest = area.AddComponent<WindTotemQuestArea>();
                quest.Source = source;
                quest.IsGiver = index == 37;
                area.SetActive(true);
                if (!marker) marker = source.gameObject.AddComponent<QuestAnchorMarker>();
                marker.Area = area;
                status.Complete = true;
                WindTotemPlugin.Log("Installed " + area.name + " on island " + index + " at " + source.name +
                    "; local=" + __instance.transform.InverseTransformPoint(source.position) +
                    (index == 37 ? "; radius=4m." : "; size=3x3x3m."));
            }
            catch (Exception e)
            {
                if (area) { area.SetActive(false); UnityEngine.Object.Destroy(area); }
                status.Complete = false;
                status.NextAttempt = Time.unscaledTime + 10f;
                WindTotemPlugin.Error("Quest trigger installation failed on island " + index + ": " + e);
            }
        }

        internal static Transform FindTarget(Transform island, int index)
        {
            if (index != 37 && index != 30) return null;
            foreach (var candidate in island.GetComponentsInChildren<Transform>(true))
            {
                if (index == 37 && candidate.name == "Modular NPC (1)" &&
                    Vector3.Distance(island.InverseTransformPoint(candidate.position),
                        new Vector3(-12.699f, 45.577f, 23.885f)) < 0.5f) return candidate;
                if (index == 30 && candidate.name == "building_arab_obelisk_small") return candidate;
            }
            return null;
        }
    }

    public sealed class QuestIslandStatus : MonoBehaviour
    {
        public bool Complete;
        public bool ReportedMissing;
        public float NextAttempt;
    }
    public sealed class QuestAnchorMarker : MonoBehaviour { public GameObject Area; }
}
