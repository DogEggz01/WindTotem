using System.Collections;
using HarmonyLib;
using UnityEngine;
using cakeslice;

namespace WindTotem
{
    // Composition preserves all private ShipItem lifecycle methods and boat/save behavior.
    public sealed class PortableTotem : MonoBehaviour
    {
        private IEnumerator Start()
        {
            var item = GetComponent<ShipItem>();
            var saveable = GetComponent<SaveablePrefab>();
            // Allow vanilla load restoration to populate the instance ID and boat parent first.
            while (!SaveLoadManager.instance || GameState.currentlyLoading || GameState.justStarted)
                yield return null;
            yield return new WaitForEndOfFrame();
            if (item.sold) saveable.RegisterToSave();

            var lod = GetComponent<LODGroup>();
            if (lod)
            {
                var levels = lod.GetLODs();
                if (levels.Length > 0)
                {
                    levels[0].renderers = GetComponentsInChildren<Renderer>(true);
                    lod.SetLODs(levels);
                    lod.RecalculateBounds();
                }
            }
        }
    }

    public sealed class PortableWindOrb : PickupableItem
    {
        // Preserve the original 8-metre interaction reach; visual size is independent.
        private const float MaxCarryDistance = 8f;
        private ShipItem owner;
        private Transform pivot;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private ParticleSystem particles;
        private AudioSource orbAudio;
        private Collider orbCollider;
        private bool effectsPlaying;
        internal Collider PointerCollider => orbCollider;
        internal bool CanPoint => owner && !owner.held && !held && orbCollider && orbCollider.enabled &&
            gameObject.activeInHierarchy && !unclickable && owner.gameObject.layer != 5 && owner.gameObject.layer != 26 &&
            !GameState.currentlyLoading && !GameState.sleeping && !GameState.recovering;

        private void Awake()
        {
            owner = GetComponentInParent<ShipItem>();
            pivot = transform.parent;
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            particles = GetComponentInChildren<ParticleSystem>(true);
            orbAudio = GetComponent<AudioSource>();
            orbCollider = GetComponent<Collider>();
            lookText = "";
            description = "";
            holdDistance = 1.15f;
            // Rotating the pointer must not rotate the held orb's world position with it.
            transform.SetParent(owner.transform, true);
            StopEffects();
        }

        public override void OnDrop()
        {
            StopEffects();
            WindHold.Release(this);
        }

        public override void ExtraLateUpdate()
        {
            if (!owner || !pivot) return;
            bool stored = owner.gameObject.layer == 5 || owner.gameObject.layer == 26;
            bool available = !owner.held && !stored && !GameState.currentlyLoading &&
                !GameState.sleeping && !GameState.recovering;
            orbCollider.enabled = available;
            if (held && (!available || Vector3.Distance(transform.position, pivot.position) > MaxCarryDistance))
            {
                OnDrop();
                held.DropItem();
            }
            if (held && Wind.instance)
            {
                float distance = Vector3.Distance(transform.position, pivot.position);
                Vector3 direction = transform.position - pivot.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.000001f)
                    pivot.rotation = Quaternion.Slerp(pivot.rotation,
                        Quaternion.LookRotation(direction, Vector3.up), Time.deltaTime * 2f);
                Vector3 horizontal = Vector3.ProjectOnPlane(pivot.forward, Vector3.up).normalized;
                if (horizontal.sqrMagnitude > 0.000001f)
                {
                    float strength = Mathf.Lerp(Wind.instance.minimumMagnitude,
                        Wind.instance.maximumMagnitude, distance / MaxCarryDistance);
                    WindHold.Command(this, horizontal * strength);
                }
                var emission = particles.emission;
                emission.enabled = true;
                emission.rateOverTime = distance * 20f;
                var main = particles.main;
                main.startSize = Mathf.Lerp(0.08f, 0.25f, distance / MaxCarryDistance) * 0.4f;
                orbAudio.volume = distance / MaxCarryDistance;
                orbAudio.pitch = Mathf.Lerp(1.21f, 1.53f, distance / MaxCarryDistance);
                if (!orbAudio.isPlaying)
                {
                    orbAudio.enabled = true;
                    orbAudio.Play();
                }
                effectsPlaying = true;
            }
            else
            {
                if (effectsPlaying) StopEffects();
                transform.position = Vector3.Lerp(transform.position, pivot.TransformPoint(homePosition), Time.deltaTime * 5f);
                transform.rotation = pivot.rotation * homeRotation;
                gameObject.layer = available ? 0 : owner.gameObject.layer;
            }
        }

        private void StopEffects()
        {
            if (particles)
            {
                var emission = particles.emission;
                emission.enabled = false;
            }
            if (orbAudio)
            {
                orbAudio.volume = 0f;
                orbAudio.Stop();
                orbAudio.enabled = false;
            }
            effectsPlaying = false;
        }

        private void OnDisable()
        {
            if (held) held.DropItem();
            StopEffects();
        }
    }

    // A postfix rather than a transpiler: other mods (Keevi Fixes) replace the
    // world-pointer raycast call, so the orb is retargeted after DoRaycast instead.
    [HarmonyPatch(typeof(GoPointer), "DoRaycast")]
    internal static class OrbPointerPriority
    {
        // Vanilla DoRaycast reach and layer mask.
        private const float Reach = 1.8f;
        private const int Mask = -604165;

        private static AccessTools.FieldRef<GoPointer, RaycastHit> hit;
        private static AccessTools.FieldRef<GoPointer, Ray> raycastRay;
        private static AccessTools.FieldRef<GoPointer, GoPointerButton> pointedAtButton;
        private static AccessTools.FieldRef<GoPointer, float> currentLookDistance;

        private static bool Prepare()
        {
            if (!HasField("hit", typeof(RaycastHit)) || !HasField("raycastRay", typeof(Ray)) ||
                !HasField("pointedAtButton", typeof(GoPointerButton)) || !HasField("currentLookDistance", typeof(float)))
            {
                WindTotemPlugin.Error("GoPointer fields changed; the orb cannot be pointed at through the totem base.");
                return false;
            }
            hit = AccessTools.FieldRefAccess<GoPointer, RaycastHit>("hit");
            raycastRay = AccessTools.FieldRefAccess<GoPointer, Ray>("raycastRay");
            pointedAtButton = AccessTools.FieldRefAccess<GoPointer, GoPointerButton>("pointedAtButton");
            currentLookDistance = AccessTools.FieldRefAccess<GoPointer, float>("currentLookDistance");
            return true;
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void Postfix(GoPointer __instance)
        {
            // Same early outs as DoRaycast; there hit is stale or ignored.
            if (__instance.type == GoPointer.PointerType.crosshairMouse && GameState.inCursorMenu) return;
            if (__instance.debugEditorPointer || GameState.sleeping || GameState.inBed || BoatCamera.on) return;
            ref RaycastHit pointerHit = ref hit(__instance);
            if (!pointerHit.collider) return;
            var totem = FindTotem(pointerHit.collider);
            if (!totem) return;
            var orb = totem.GetComponentInChildren<PortableWindOrb>();
            if (!orb || !orb.CanPoint || pointerHit.collider == orb.PointerCollider) return;
            if ((Mask & (1 << orb.gameObject.layer)) == 0) return;
            var ray = raycastRay(__instance);
            if (!orb.PointerCollider.Raycast(ray, out RaycastHit orbHit, Reach)) return;
            // Only this base may be bypassed. Walls, boats and other cargo still block.
            foreach (var obstruction in Physics.RaycastAll(ray, orbHit.distance, Mask))
                if (obstruction.collider != orb.PointerCollider && FindTotem(obstruction.collider) != totem)
                    return;
            // DoRaycast already looked at the base; only the orb may stay outlined.
            var previous = pointedAtButton(__instance);
            if (previous && previous != orb && previous.gameObject == totem.gameObject) previous.ForceUnlook();
            // The orb has no ShipItem, so DoRaycast would always select it.
            pointerHit = orbHit;
            pointedAtButton(__instance) = orb;
            orb.Look(__instance);
            currentLookDistance(__instance) = orbHit.distance;
            __instance.pointer.position = orbHit.point;
        }

        private static bool HasField(string name, System.Type type)
        {
            var field = AccessTools.Field(typeof(GoPointer), name);
            return field != null && field.FieldType == type;
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
