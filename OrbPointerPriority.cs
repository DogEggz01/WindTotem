using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace WindTotem
{
    [HarmonyPatch(typeof(GoPointer), "DoRaycast")]
    internal static class OrbPointerPriority
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var raycast = AccessTools.Method(typeof(Physics), nameof(Physics.Raycast),
                new[] { typeof(Ray), typeof(RaycastHit).MakeByRefType(), typeof(float), typeof(int) });
            var replacement = AccessTools.Method(typeof(OrbPointerPriority), nameof(Raycast));
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, raycast))
                {
                    instruction.operand = replacement;
                    replaced++;
                }
                yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Expected one world-pointer raycast; found " + replaced);
        }

        internal static bool Raycast(Ray ray, out RaycastHit hit, float distance, int mask)
        {
            if (!Physics.Raycast(ray, out hit, distance, mask)) return false;
            var totem = FindTotem(hit.collider);
            if (!totem) return true;
            var orb = totem.GetComponentInChildren<PortableWindOrb>();
            if (!orb || !orb.CanPoint || (mask & (1 << orb.gameObject.layer)) == 0) return true;
            if (!orb.PointerCollider.Raycast(ray, out RaycastHit orbHit, distance)) return true;
            // Only this base may be bypassed. Walls, boats and other cargo still block.
            foreach (var obstruction in Physics.RaycastAll(ray, orbHit.distance, mask))
                if (obstruction.collider != orb.PointerCollider && FindTotem(obstruction.collider) != totem)
                    return true;
            hit = orbHit;
            return true;
        }

        private static PortableTotem FindTotem(Collider collider)
        {
            var totem = collider.GetComponentInParent<PortableTotem>();
            if (totem) return totem;
            var proxy = collider.GetComponentInParent<ItemRigidbody>();
            var item = proxy ? proxy.GetShipItem() : null;
            return item ? item.GetComponent<PortableTotem>() : null;
        }
    }
}
