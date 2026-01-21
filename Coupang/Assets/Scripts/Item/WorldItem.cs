using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class WorldItem : MonoBehaviour
{
    // Global registry (kept)
    private static readonly List<WorldItem> s_allWorldItems = new List<WorldItem>();
    public static IReadOnlyList<WorldItem> AllWorldItems => s_allWorldItems;

    [Header("Definition")]
    public ItemDefinition definition;

    [Header("Runtime State")]
    public string instanceId;
    public int stackCount = 1;

    [Header("Durability (integrated)")]
    public bool useDurability = true;
    public int maxDurability = 100;
    public int currentDurability = 100;

    [Header("Impact Damage (integrated)")]
    public bool enableImpactDamage = true;
    public float impactMinSpeed = 6f;
    public float impactDamagePerUnit = 5f;

    [Header("Runtime")]
    [HideInInspector] public Rigidbody rb;

    private Collider[] _colliders;
    private Renderer[] _renderers;

    // Carrier meta
    [HideInInspector] public bool isOnCarrier;
    [HideInInspector] public CarrierController carrierOwner;
    [HideInInspector] public int carrierSlotIndex = -1;
    [HideInInspector] public Transform carrierSlotPivot;

    [HideInInspector] public bool ignoreContainerAutoParent;

    public bool IsCarrierItem => definition != null && definition.isCarrier;

    // ─────────────────────────────────────────────
    // ✅ Self-throw break ignore (only for thrower, temporary)
    // ─────────────────────────────────────────────
    [Header("Throw Self-Ignore (runtime)")]
    [SerializeField] private Transform throwerRoot;
    [SerializeField] private float ignoreThrowerBreakUntil;

    /// <summary>
    /// Call right when the item is thrown, to prevent immediate self-collision from breaking it.
    /// This does NOT disable physics collision. It only skips break-impulse check vs the thrower for a short window.
    /// </summary>
    public void ArmIgnoreBreakForThrower(Transform throwerRootTransform, float seconds)
    {
        throwerRoot = throwerRootTransform;
        ignoreThrowerBreakUntil = Time.time + Mathf.Max(0f, seconds);
    }

    private void Awake()
    {
        if (string.IsNullOrEmpty(instanceId))
            instanceId = System.Guid.NewGuid().ToString("N");

        if (!rb) rb = GetComponent<Rigidbody>();
        EnsureCaches();

        if (useDurability)
        {
            maxDurability = Mathf.Max(1, maxDurability);
            currentDurability = Mathf.Clamp(currentDurability, 0, maxDurability);
        }
    }

    private void OnEnable()
    {
        if (!s_allWorldItems.Contains(this))
            s_allWorldItems.Add(this);
    }

    private void OnDisable()
    {
        s_allWorldItems.Remove(this);
    }

    private void OnDestroy()
    {
        s_allWorldItems.Remove(this);
    }

    private void EnsureCaches()
    {
        if (_colliders == null || _colliders.Length == 0)
            _colliders = GetComponentsInChildren<Collider>(true);

        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);
    }

    // Pickup / Drop
    public void OnPickedUp(bool destroyInstance)
    {
        EnsureCaches();

        // Reset throw ignore state on pickup
        throwerRoot = null;
        ignoreThrowerBreakUntil = 0f;

        bool isCarrier = IsCarrierItem;

        if (destroyInstance && !isCarrier)
        {
            Destroy(gameObject);
            return;
        }

        if (!rb) rb = GetComponent<Rigidbody>();
        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (_colliders != null)
        {
            foreach (var c in _colliders)
                if (c) c.enabled = false;
        }

        if (_renderers != null)
        {
            foreach (var r in _renderers)
                if (r) r.enabled = false;
        }
    }

    public void OnDropped(Vector3 worldPos, Vector3 initialVelocity)
    {
        EnsureCaches();

        if (isOnCarrier)
        {
            transform.SetParent(null, true);
            isOnCarrier = false;
            carrierOwner = null;
            carrierSlotIndex = -1;
            carrierSlotPivot = null;
        }

        ignoreContainerAutoParent = false;

        transform.position = worldPos;

        if (!rb) rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearVelocity = initialVelocity;

        if (_colliders != null)
        {
            foreach (var c in _colliders)
                if (c) c.enabled = true;
        }

        if (_renderers != null)
        {
            foreach (var r in _renderers)
                if (r) r.enabled = true;
        }

        if (definition && definition.worldPrefab)
            name = definition.worldPrefab.name;
    }

    // Durability API
    public bool TryGetDurability(out int current, out int max)
    {
        current = currentDurability;
        max = maxDurability;
        return useDurability;
    }

    public void ApplyDurability(int current, int max, bool clamp = true)
    {
        useDurability = true;
        maxDurability = Mathf.Max(1, max > 0 ? max : maxDurability);
        currentDurability = clamp ? Mathf.Clamp(current, 0, maxDurability) : current;
    }

    public void ApplyDamage(int amount)
    {
        if (!useDurability) return;
        if (amount <= 0) return;

        currentDurability = Mathf.Clamp(currentDurability - amount, 0, maxDurability);
        if (currentDurability <= 0)
            OnBroken();
    }

    private void OnBroken()
    {
        if (definition != null && definition.breakable)
        {
            if (definition.brokenPrefab)
            {
                Instantiate(definition.brokenPrefab, transform.position, transform.rotation);
                Destroy(gameObject);
                return;
            }

            gameObject.SetActive(false);
            return;
        }

        Destroy(gameObject);
    }

    // Impact damage + break impulse (integrated)
    private void OnCollisionEnter(Collision col)
    {
        if (!definition) return;

        // ✅ Ignore break-impulse ONLY when colliding with the thrower (self), for a short window.
        // Other players still count as valid collisions (teamkill allowed).
        if (throwerRoot != null && Time.time < ignoreThrowerBreakUntil)
        {
            Transform otherRoot = col.collider != null ? col.collider.transform.root : null;
            if (otherRoot == throwerRoot)
            {
                // We skip only the "break impulse" logic against self.
                // You can also skip impact damage against self if you want absolute safety:
                // return;
            }
            else
            {
                // not self
            }
        }

        // Break impulse check (definition-based)
        if (definition.breakable)
        {
            // If self-collision in ignore window, skip this block
            if (throwerRoot != null && Time.time < ignoreThrowerBreakUntil)
            {
                Transform otherRoot = col.collider != null ? col.collider.transform.root : null;
                if (otherRoot == throwerRoot)
                    return;
            }

            float mass = (rb != null) ? rb.mass : 1f;
            float impulse = col.relativeVelocity.magnitude * mass;
            if (impulse >= definition.breakImpulseThreshold)
            {
                OnBroken();
                return;
            }
        }

        // Impact-to-durability damage
        if (enableImpactDamage && useDurability)
        {
            float speed = col.relativeVelocity.magnitude;
            if (speed >= impactMinSpeed)
            {
                int dmg = Mathf.RoundToInt((speed - impactMinSpeed) * impactDamagePerUnit);
                if (dmg > 0) ApplyDamage(dmg);
            }
        }
    }
}
