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
    public float impactDamagePerUnit = 3f;

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

    [Header("Interaction Lock (runtime)")]
    [Tooltip("If true, the player cannot pick up this item (e.g., items sealed by completed drop zones).")]
    public bool pickupLocked;


    public bool IsCarrierItem => definition != null && definition.isCarrier;

    // ─────────────────────────────────────────────
    // ✅ Self-throw break ignore (only for thrower, temporary)
    // ─────────────────────────────────────────────
    [Header("Throw Self-Ignore (runtime)")]
    [SerializeField] private Transform throwerRoot;
    [SerializeField] private float ignoreThrowerBreakUntil;


[Header("Throw Terrain Grace (optional)")]
[Tooltip("If enabled, collisions with terrain/ground layers shortly after a throw will NOT cause break or durability damage. Useful on bumpy/uneven ground.")]
public bool ignoreTerrainDamageAfterThrow = true;

[Tooltip("Seconds after ArmIgnoreBreakForThrower() during which terrain collisions are ignored for damage/break.")]
public float ignoreTerrainDamageSeconds = 0.12f;

[Tooltip("Layers treated as terrain/ground for the grace window. Exclude enemies/props you DO want to damage/break against.")]
public LayerMask terrainMask = ~0;

[SerializeField] private float ignoreTerrainDamageUntil;

[Header("Impact Clamp (optional)")]
[Tooltip("Clamp collision speed used for break/impact damage. Helps prevent rare physics spikes from instantly breaking items.")]
public bool clampImpactSpeed = false;

[Tooltip("Maximum collision speed considered for break/impact damage when clamping is enabled.")]
public float maxImpactSpeed = 25f;


[Header("Throw Hit Damage (optional)")]
[Tooltip("If enabled, when this item was thrown and hits a target with Health, it deals the SAME damage that the item takes from that impact (durability damage).")]
public bool enableThrowDamageToHealth = true;

[Tooltip("Seconds after a throw during which the item can deal hit damage.")]
public float throwDamageWindowSeconds = 2.0f;

[Tooltip("If true, the item can deal hit damage only once per throw (first valid hit).")]
public bool consumeThrowDamageOnHit = true;

[Tooltip("Layer mask to restrict what can receive throw hit damage (e.g., Monster). If set to Everything, any Health can be damaged (except the thrower).")]
public LayerMask throwDamageTargetMask = ~0;

[SerializeField] private float throwDamageUntil;
[SerializeField] private bool throwDamageArmed;


    /// <summary>
    /// Call right when the item is thrown, to prevent immediate self-collision from breaking it.
    /// This does NOT disable physics collision. It only skips break-impulse check vs the thrower for a short window.
    /// </summary>
    public void ArmIgnoreBreakForThrower(Transform throwerRootTransform, float seconds)
    {
        throwerRoot = throwerRootTransform;
        ignoreThrowerBreakUntil = Time.time + Mathf.Max(0f, seconds);
        if (ignoreTerrainDamageAfterThrow)
            ignoreTerrainDamageUntil = Time.time + Mathf.Max(0f, ignoreTerrainDamageSeconds);
        if (enableThrowDamageToHealth)
        {
            throwDamageArmed = true;
            throwDamageUntil = Time.time + Mathf.Max(0f, throwDamageWindowSeconds);
        }



    }


    /// <summary>
    /// Lock/unlock player pickup interaction for this item.
    /// When locked, PickupInteractable (if present) is disabled and Inventory pickup rejects it.
    /// </summary>
    public void SetPickupLocked(bool locked)
    {
        pickupLocked = locked;

        // Disable pickup interaction component if present (keeps physics/colliders).
        var pi = GetComponent<PickupInteractable>();
        if (!pi) pi = GetComponentInChildren<PickupInteractable>(true);
        if (!pi) pi = GetComponentInParent<PickupInteractable>();
        if (pi) pi.enabled = !locked;
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

    

private void TryDealThrowHitDamage(Collision col, int dmg)
{
    if (!enableThrowDamageToHealth) return;
    if (!throwDamageArmed) return;
    if (Time.time > throwDamageUntil) return;
    if (dmg <= 0) return;

    if (col == null || col.collider == null) return;

    int layer = col.collider.gameObject.layer;
    if ((throwDamageTargetMask.value & (1 << layer)) == 0) return;

    Health targetHealth = col.collider.GetComponentInParent<Health>();
    if (targetHealth == null) return;

    // Don't damage the thrower (self).
    if (throwerRoot != null && targetHealth.transform.root == throwerRoot) return;

    targetHealth.ApplyDamage(dmg);

    if (consumeThrowDamageOnHit)
    {
        throwDamageArmed = false;
        throwDamageUntil = 0f;
    }
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

    // Impact damage (integrated). Instant-break impulse is disabled.
    private void OnCollisionEnter(Collision col)
    {
        if (!definition) return;

// ✅ Ignore all collision-based damage/break when colliding with the thrower (self), for a short window.
// This prevents "instant break" caused by immediate self-collision right after a throw,
// even for items that are not definition.breakable but still use durability impact damage.
if (throwerRoot != null && Time.time < ignoreThrowerBreakUntil)
{
    Transform otherRoot = col.collider != null ? col.collider.transform.root : null;
    if (otherRoot == throwerRoot)
        return; // skip break impulse + impact damage against self
}


// Optional: ignore terrain/ground collisions for a short grace window after throw.
// On bumpy/uneven surfaces, collision resolution can spike relativeVelocity and cause large durability damage.
if (ignoreTerrainDamageAfterThrow && Time.time < ignoreTerrainDamageUntil && col.collider != null)
{
    int layer = col.collider.gameObject.layer;
    if ((terrainMask.value & (1 << layer)) != 0)
        return; // skip durability impact damage against terrain during grace window
}


// Break impulse instant-break is DISABLED.
// Items only break when durability reaches 0 (see ApplyDamage -> OnBroken).


        
// Impact-to-durability damage (this also drives throw hit damage)
int impactDmg = 0;

if (enableImpactDamage && useDurability)
{
    float speed = col.relativeVelocity.magnitude;
    if (clampImpactSpeed) speed = Mathf.Min(speed, Mathf.Max(0f, maxImpactSpeed));

    if (speed >= impactMinSpeed)
    {
        impactDmg = Mathf.RoundToInt((speed - impactMinSpeed) * impactDamagePerUnit);
        if (impactDmg > 0)
            ApplyDamage(impactDmg);
    }
}

// If thrown and we hit something with Health, deal the SAME damage as the item took from this impact.
if (impactDmg > 0)
    TryDealThrowHitDamage(col, impactDmg);
}
}
