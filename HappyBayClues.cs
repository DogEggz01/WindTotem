using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace WindTotem
{
    internal static class HappyBayClues
    {
        internal static bool Eligible => QuestSave.Valid && QuestSave.Progress.Stage == QuestStages.Outbound &&
            !GameState.currentlyLoading && !GameState.justStarted && !GameState.sleeping && !GameState.recovering;
        internal static bool IsLee(CargoTransportDude dude) => dude && dude.carrierIndex == 18 && dude.name == "transport dude";
        internal static void Attach(GameObject npc, CargoTransportDude transport = null)
        {
            var clue = npc.GetComponent<HappyBayClue>();
            if (!clue) clue = npc.AddComponent<HappyBayClue>();
            clue.Transport = transport;
        }
    }

    [HarmonyPatch(typeof(PortDude), "Awake")]
    internal static class HappyBayPortInstall
    {
        private static void Postfix(PortDude __instance)
        {
            if (__instance.GetPort() && __instance.GetPort().portIndex == 20) HappyBayClues.Attach(__instance.gameObject);
        }
    }
    [HarmonyPatch(typeof(IslandSceneryScene), "Update")]
    internal static class HappyBayLeeInstall
    {
        private static void Postfix(IslandSceneryScene __instance)
        {
            if (__instance.parentIslandIndex != 18) return;
            var status = __instance.GetComponent<HappyBayInstalled>();
            if (!status) status = __instance.gameObject.AddComponent<HappyBayInstalled>();
            if (status.Complete || Time.unscaledTime < status.NextAttempt) return;
            status.NextAttempt = Time.unscaledTime + 1f;
            foreach (var dude in __instance.GetComponentsInChildren<CargoTransportDude>(true))
                if (HappyBayClues.IsLee(dude))
                {
                    HappyBayClues.Attach(dude.gameObject, dude);
                    status.Complete = true;
                    return;
                }
        }
    }
    public sealed class HappyBayInstalled : MonoBehaviour { public bool Complete; public float NextAttempt; }

    // The original short transport trigger must not close the extended 4m/5m clue.
    [HarmonyPatch(typeof(CargoTransportDude), "OnTriggerEnter")]
    internal static class HappyBayTransportEnter
    {
        private static bool Prefix(CargoTransportDude __instance) => !HappyBayClues.IsLee(__instance) || !HappyBayClues.Eligible;
    }
    [HarmonyPatch(typeof(CargoTransportDude), "OnTriggerExit")]
    internal static class HappyBayTransportExit
    {
        private static bool Prefix(CargoTransportDude __instance) => !HappyBayClues.IsLee(__instance) || !HappyBayClues.Eligible;
    }

    public sealed class HappyBayClue : MonoBehaviour
    {
        public CargoTransportDude Transport;
        private QuestDialogue dialogue;
        private bool open;
        private float retry;
        private void Update()
        {
            float distance = Refs.observerMirror ? Vector3.Distance(transform.position, Refs.observerMirror.transform.position) : float.PositiveInfinity;
            bool next = HappyBayClues.Eligible && distance <= (open ? 5f : Transport ? 4f : 3f);
            if (!next)
            {
                if (open && Transport && CargoCarrierUI.instance) CargoCarrierUI.instance.HideUI();
                open = false;
                dialogue?.Hide();
                return;
            }
            if (Transport)
            {
                if (!open && CargoCarrierUI.instance) CargoCarrierUI.instance.ShowUI(transform, Transport.carrierIndex);
                open = CargoCarrierUI.instance;
            }
            else
            {
                open = true;
                if (dialogue == null && Time.unscaledTime >= retry)
                {
                    retry = Time.unscaledTime + .5f;
                    dialogue = QuestDialogue.Create(transform, QuestText.PortLine, null);
                }
                dialogue?.Show(transform, true);
            }
        }
        private void OnDisable()
        {
            if (open && Transport && CargoCarrierUI.instance) CargoCarrierUI.instance.HideUI();
            open = false;
            dialogue?.Hide();
        }
    }

    [HarmonyPatch(typeof(CargoCarrierUI), nameof(CargoCarrierUI.ShowUI))]
    internal static class HappyBayServiceShow
    {
        private static void Prefix(CargoCarrierUI __instance) { __instance.GetComponent<HappyBayServiceClue>()?.Restore(); }
        private static void Postfix(CargoCarrierUI __instance, Transform transportDude, int carrierIndex)
        {
            if (carrierIndex != 18 || !HappyBayClues.Eligible || !transportDude || transportDude.name != "transport dude") return;
            var clue = __instance.GetComponent<HappyBayServiceClue>();
            if (!clue) clue = __instance.gameObject.AddComponent<HappyBayServiceClue>();
            clue.Source = transportDude;
        }
    }
    [HarmonyPatch(typeof(CargoCarrierUI), nameof(CargoCarrierUI.HideUI))]
    internal static class HappyBayServiceHide
    {
        private static void Prefix(CargoCarrierUI __instance) { __instance.GetComponent<HappyBayServiceClue>()?.Restore(); }
    }

    public sealed class HappyBayServiceClue : MonoBehaviour
    {
        public Transform Source;
        private QuestDialogue dialogue;
        private Transform button;
        private Vector3 buttonPosition;
        private Quaternion buttonRotation;
        private Quaternion buttonFaceRotation;
        private Renderer[] buttonRenderers;
        private readonly Dictionary<Renderer, bool> originalRenderers = new Dictionary<Renderer, bool>();
        private void LateUpdate()
        {
            if (!Source) return;
            if (!HappyBayClues.Eligible) { Restore(); return; }
            if (dialogue == null)
            {
                var ui = GetComponent<CargoCarrierUI>();
                var pointer = GetComponentInChildren<CargoCarrierButton>(true);
                if (!pointer || !ui.buttonText) return;
                // Locate the original button group including its label and collider.
                button = pointer.transform;
                while (button != transform && !ui.buttonText.transform.IsChildOf(button)) button = button.parent;
                if (button == transform) { button = null; return; }
                var nativeRenderers = GetComponentsInChildren<Renderer>(true);
                dialogue = QuestDialogue.Create(transform, QuestText.LeeLine, null);
                if (dialogue == null) { button = null; return; }
                buttonPosition = button.localPosition;
                buttonRotation = button.localRotation;
                buttonFaceRotation = Quaternion.Inverse(button.rotation) * ui.buttonText.transform.rotation;
                buttonRenderers = button.GetComponentsInChildren<Renderer>();
                foreach (var renderer in nativeRenderers)
                    if (!renderer.transform.IsChildOf(button))
                    {
                        // Only native renderers: the dialogue clone is not part of this snapshot.
                        originalRenderers[renderer] = renderer.enabled;
                        renderer.enabled = false;
                    }
            }
            dialogue.Show(Source, true);
            dialogue.PlaceServiceButton(button, buttonFaceRotation, buttonRenderers);
        }
        public void Restore()
        {
            Source = null;
            if (button) { button.localPosition = buttonPosition; button.localRotation = buttonRotation; }
            foreach (var pair in originalRenderers) if (pair.Key) pair.Key.enabled = pair.Value;
            originalRenderers.Clear();
            button = null;
            buttonRenderers = null;
            dialogue?.Hide();
            dialogue?.Dispose();
            dialogue = null;
        }
        private void OnDisable() { Restore(); }
    }
}
