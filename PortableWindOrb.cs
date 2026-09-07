using UnityEngine;

namespace WindTotem
{
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
}
