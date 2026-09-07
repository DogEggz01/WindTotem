using HarmonyLib;
using UnityEngine;

namespace WindTotem
{
    // Global wind has one owner: the most recently operated portable orb.
    internal static class WindHold
    {
        private static PortableWindOrb owner;
        private static Vector3 commanded;
        private static float remaining;
        internal static bool Active => remaining > 0f;
        internal static void Command(PortableWindOrb orb, Vector3 wind)
        {
            owner = orb;
            commanded = wind;
            remaining = 0f;
            Wind.instance.ForceNewWind(wind);
        }
        internal static void Release(PortableWindOrb orb)
        {
            if (owner != orb || !Wind.instance) return;
            owner = null;
            if (GameState.currentlyLoading || GameState.sleeping || GameState.recovering) return;
            remaining = 60f;
            // SetNewWindTarget is intercepted before ForceNewWind can randomize
            // the commanded base. Vanilla then chooses a gust around that base.
            Wind.instance.ForceNewWind(commanded);
        }
        internal static void Reset() { owner = null; remaining = 0f; }
        internal static void Advance(float realSeconds, bool paused)
        {
            if (!Active || paused) return;
            remaining = Mathf.Max(0f, remaining - realSeconds);
            if (Active || !Wind.instance) return;
            Vector3 gust = Wind.currentWind;
            Wind.instance.ForceNewWind(commanded);
            Wind.currentWind = gust;
        }

        [HarmonyPatch(typeof(Wind), "Update")]
        private static class Tick
        {
            private static void Prefix(Wind __instance)
            {
                if (!Active) return;
                if (GameState.currentlyLoading) { Reset(); return; }
                // Real seconds, independent of accelerated game time; pause freezes it.
                Advance(Time.unscaledDeltaTime, Time.timeScale <= 0f);
            }
        }

        [HarmonyPatch(typeof(Wind), "SetNewWindTarget")]
        private static class LockBase
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(Wind __instance, ref Vector3 ___currentWindTarget)
            {
                if (!Active) return true;
                Wind.currentBaseWind = commanded;
                __instance.outCurrentBaseWind = commanded;
                ___currentWindTarget = commanded;
                return false;
            }
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Wind __instance, ref Vector3 ___currentWindTarget)
            {
                if (!Active) return;
                Wind.currentBaseWind = commanded;
                __instance.outCurrentBaseWind = commanded;
                ___currentWindTarget = commanded;
            }
        }

        [HarmonyPatch(typeof(Wind), "Awake")]
        private static class NewSession { private static void Prefix() { Reset(); } }
        [HarmonyPatch(typeof(Wind), nameof(Wind.LoadWind))]
        private static class Load { private static void Prefix() { Reset(); } }
    }
}
