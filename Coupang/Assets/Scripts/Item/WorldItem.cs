// Assets/Scripts/Item/WorldItem.cs
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class WorldItem : MonoBehaviour
{
    // ─────────────────────────────────────────────
    // Global registry for all active WorldItems
    // ─────────────────────────────────────────────
    private static readonly List<WorldItem> s_allWorldItems = new List<WorldItem>();
    public static IReadOnlyList<WorldItem> AllWorldItems => s_allWorldItems;

    [Header("Definition")]
    public ItemDefinition definition;

    [Header("Runtime")]
    [HideInInspector] public Rigidbody rb;

    private Collider[] _colliders;
    private Renderer[] _renderers;

    // ── Carrier meta info ─────────────────────────────
    [HideInInspector] public bool isOnCarrier;
    [HideInInspector] public CarrierController carrierOwner;
    [HideInInspector] public int carrierSlotIndex = -1;
    [HideInInspector] public Transform carrierSlotPivot;

    /// <summary>
    /// If true, ContainerAutoParent should not touch this item.
    /// Used for equipped carriers on player.
    /// </summary>
    [HideInInspector] public bool ignoreContainerAutoParent;

    /// <summary>Convenience: true if this item is a carrier item.</summary>
    public bool IsCarrierItem => definition != null && definition.isCarrier;

    // ─────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────
    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        EnsureCaches();
    }

    void OnEnable()
    {
        if (!s_allWorldItems.Contains(this))
            s_allWorldItems.Add(this);
    }

    void OnDisable()
    {
        s_allWorldItems.Remove(this);
    }

    void OnDestroy()
    {
        s_allWorldItems.Remove(this);
    }

    // ─────────────────────────────────────────────
    // Internals
    // ─────────────────────────────────────────────
    private void EnsureCaches()
    {
        if (_colliders == null || _colliders.Length == 0)
            _colliders = GetComponentsInChildren<Collider>(true);

        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>
    /// Inventory pickup.
    /// - destroyInstance = true  → destroy world instance (normal items)
    /// - destroyInstance = false → only disable physics/colliders/renderers
    /// Carrier items should use destroyInstance = false and be reused.
    /// </summary>
    public void OnPickedUp(bool destroyInstance)
    {
        EnsureCaches();

        bool isCarrier = IsCarrierItem;

        // Normal items can be destroyed when picked up.
        if (destroyInstance && !isCarrier)
        {
            Destroy(gameObject);
            return;
        }

        // Disable physics and interaction in world.
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
            {
                if (c) c.enabled = false;
            }
        }

        if (_renderers != null)
        {
            foreach (var r in _renderers)
            {
                if (r) r.enabled = false;
            }
        }
    }

    /// <summary>
    /// Called when this item is mounted onto a carrier slot.
    /// Parent becomes the slot pivot, physics disabled, colliders disabled.
    /// Scale is kept as it was in world; only position/rotation are adjusted.
    /// </summary>
    public void EnterCarrierMountMode(CarrierController carrier, int slotIndex, Transform slotPivot)
    {
        EnsureCaches();

        carrierOwner = carrier;
        carrierSlotIndex = slotIndex;
        carrierSlotPivot = slotPivot;
        isOnCarrier = true;

        // While mounted on a carrier, parenting is owned by the carrier.
        ignoreContainerAutoParent = false;

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
            {
                if (c) c.enabled = false;
            }
        }

        if (_renderers != null)
        {
            foreach (var r in _renderers)
            {
                if (r) r.enabled = true;
            }
        }

        // Attach under slot pivot, but keep current world scale.
        // SetParent(worldPositionStays: false) converts world scale → local scale
        // so the visual scale remains exactly the same.
        transform.SetParent(slotPivot, false);

        if (definition != null)
        {
            Vector3 pos = definition.carrierLocalPosition;
            Vector3 euler = definition.carrierLocalEuler;

            // Only adjust local position and rotation.
            // DO NOT change localScale → keep item scale as it was in world.
            transform.localPosition = pos;
            transform.localEulerAngles = euler;
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
        EnsureCaches();

        if (isOnCarrier)
        {
            transform.SetParent(null, true);
            isOnCarrier = false;
            carrierOwner = null;
            carrierSlotIndex = -1;
            carrierSlotPivot = null;
        }

        // Once dropped to world, allow container/ship parenting again.
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
            {
                if (c) c.enabled = true;
            }
        }

        if (_renderers != null)
        {
            foreach (var r in _renderers)
            {
                if (r) r.enabled = true;
            }
        }

        if (definition && definition.worldPrefab)
            name = definition.worldPrefab.name;
    }

    /// <summary>Durability snapshot read. Returns false if no Durability component.</summary>
    public bool TryGetDurability(out int current, out int max)
    {
        current = 0;
        max = 0;

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
