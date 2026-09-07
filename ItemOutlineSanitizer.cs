using cakeslice;
using HarmonyLib;
using UnityEngine;

namespace WindTotem
{
    public sealed class ItemOutlineSanitizer : MonoBehaviour
    {
        private void Awake()
        {
            // Same lifecycle fix as PostalMailOutlineSanitizer: a copied outline
            // has no controlling GoPointerButton and can remain permanently red.
            foreach (var outline in GetComponents<Outline>()) DestroyImmediate(outline);
        }
    }

    [HarmonyPatch(typeof(ShipItem), nameof(ShipItem.UpdateLookText))]
    internal static class TotemHoverText
    {
        private static void Postfix(ShipItem __instance)
        {
            if (__instance.GetComponent<PortableTotem>()) __instance.lookText = "";
        }
    }
}
