// Assets/Scripts/Item/WorldItem.cs
using UnityEngine;

[DisallowMultipleComponent]
public class WorldItem : MonoBehaviour
{
    public ItemDefinition definition;
    [HideInInspector] public Rigidbody rb;

    private Collider[] _colliders;
    private Renderer[] _renderers;

    // ── Carrier meta info ─────────────────────────────
    [HideInInspector] public bool isOnCarrier;
    [HideInInspector] public CarrierController carrierOwner;
    [HideInInspector] public int carrierSlotIndex = -1;
    [HideInInspector] public Transform carrierSlotPivot;

    // Used only for special cases (e.g., equipped carrier on player)
    [HideInInspector] public bool ignoreContainerAutoParent;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>(true);
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>
    /// Inventory pickup.
    /// - destroyInstance = true → destroy world instance (normal items only)
    /// - destroyInstance = false → only disable physics/colliders/renderers
    /// Carrier items are never destroyed here.
    /// </summary>
    public void OnPickedUp(bool destroyInstance)
    {
        bool isCarrierItem = (definition != null && definition.isCarrier);

        // Normal items can be destroyed when picked up.
        if (destroyInstance && !isCarrierItem)
        {
            Destroy(gameObject);
            return;
        }

        // Carrier items (and non-destroy path): just disable physics/colliders.
        if (rb)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
        foreach (var c in _colliders) if (c) c.enabled = false;

        if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers) if (r) r.enabled = false;
    }

    /// <summary>
    /// Called when this item is mounted onto a carrier slot.
    /// Parent becomes the slot pivot, physics disabled, colliders disabled.
    /// </summary>
    public void EnterCarrierMountMode(CarrierController carrier, int slotIndex, Transform slotPivot)
    {
        carrierOwner = carrier;
        carrierSlotIndex = slotIndex;
        carrierSlotPivot = slotPivot;
        isOnCarrier = true;

        // While mounted on a carrier, parenting is owned by the carrier.
        ignoreContainerAutoParent = false; // only carriers use this flag

        if (!rb) rb = GetComponent<Rigidbody>();
        if (rb)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
        foreach (var c in _colliders) if (c) c.enabled = false;

        if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers) if (r) r.enabled = true;

        // Attach under slot pivot.
        transform.SetParent(slotPivot, false);

        if (definition != null)
        {
            Vector3 pos = definition.carrierLocalPosition;
            Vector3 euler = definition.carrierLocalEuler;
            Vector3 scale = definition.carrierLocalScale;
            if (scale == Vector3.zero) scale = Vector3.one;

            transform.localPosition = pos;
            transform.localEulerAngles = euler;
            transform.localScale = scale;
        }
        else
        {
            transform.localPosition = Vector3.zero;
            transform.localEulerAngles = Vector3.zero;
        }
    }

    /// <summary>
    /// Called when this item is released back into the world.
    /// If it was mounted on a carrier, detach from the slot and restore physics.
    /// </summary>
    public void OnDropped(Vector3 worldPos, Vector3 initialVelocity)
    {
        if (isOnCarrier)
        {
            transform.SetParent(null, true);
            isOnCarrier = false;
            carrierOwner = null;
            carrierSlotIndex = -1;
            carrierSlotPivot = null;
        }

        transform.position = worldPos;

        if (!rb) rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = initialVelocity;
#else
        rb.velocity = initialVelocity;
#endif

        if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
        foreach (var c in _colliders) if (c) c.enabled = true;

        if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers) if (r) r.enabled = true;

        if (definition && definition.worldPrefab)
            name = definition.worldPrefab.name;
    }

    /// <summary>Durability snapshot read. Returns false if no Durability component.</summary>
    public bool TryGetDurability(out int current, out int max)
    {
        current = 0; max = 0;
        var d = GetComponent<Durability>();
        if (!d) return false;
        current = d.current;
        max = d.max;
        return true;
    }

    /// <summary>Apply durability to a newly created drop prefab.</summary>
    public void ApplyDurability(int current, int max, bool clamp = true)
    {
        var d = GetComponent<Durability>();
        if (!d) d = gameObject.AddComponent<Durability>();

        if (max > 0) d.max = max;
        if (clamp) current = Mathf.Clamp(current, 0, d.max);
        d.current = current;
    }
}
