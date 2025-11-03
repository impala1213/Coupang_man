using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CarrierController : MonoBehaviour
{
    // === State ===
    [Header("State")]
    [SerializeField] private bool equipped;   // Visible in inspector (also during Play)
    public bool IsEquipped => equipped;

    // External systems (UI/Animator/etc) can subscribe to this
    public event Action<bool> OnEquipChanged;

    // Internal change detector for inspector-driven changes
    private bool lastAppliedEquipped;

    // === References ===
    [Header("References")]
    [Tooltip("Camera switcher: enables third-person when equipped, first-person when unequipped.")]
    public CameraSwitcher cameraSwitcher;

    [Header("Mount")]
    [Tooltip("Root transform where visual cargo dummies are stacked (behind the player).")]
    public Transform carrierCargoRoot;
    [Tooltip("Visual pivot used to rotate Z for tilt feedback.")]
    public Transform carrierVisualPivot;

    // === Balance ===
    [Header("Balance")]
    [Tooltip("Current tilt angle in degrees. Left is negative, right is positive.")]
    public float tilt;
    [Tooltip("Natural drift strength (deg/sec). Higher means harder to keep balance.")]
    public float tiltDrift = 5f;
    [Tooltip("Rate at which LMB/RMB pulls tilt back to center (deg/sec).")]
    public float tiltRecoverFactor = 30f;
    [Tooltip("Absolute tilt angle at which the player falls and spills cargo (deg).")]
    public float fallThreshold = 25f;

    // === Physics on spill ===
    [Header("Physics")]
    [Tooltip("Impulse applied to cargo when spilling.")]
    public float spillExplosionForce = 6f;
    [Tooltip("Radius for the spill impulse.")]
    public float spillExplosionRadius = 2.5f;

    // === Derived (read-only-ish) ===
    [Header("Derived (read-only)")]
    [Tooltip("Total weight of mounted cargo (sum of item definitions' weight).")]
    public float totalWeight;
    [Tooltip("Weighted average cargo height used for difficulty calculation.")]
    public float comHeight;

    private readonly List<WorldItem> mounted = new List<WorldItem>();

    // ------------------------------------------------------------------------

    void Start()
    {
        // Force initial sync so cameras reflect the starting value of 'equipped'
        lastAppliedEquipped = !equipped; // ensure first sync happens
        SyncEquipVisuals();
    }

    void Update()
    {
        if (!equipped) return;

        UpdateDerived();
        SimulateBalance(Time.deltaTime);
        UpdateVisual();
    }

    // Use LateUpdate so camera toggles happen after other systems this frame
    void LateUpdate()
    {
        if (lastAppliedEquipped != equipped)
        {
            SyncEquipVisuals();
        }
    }

#if UNITY_EDITOR
    // When you flip the checkbox in the Inspector during Play, this also runs.
    void OnValidate()
    {
        if (!Application.isPlaying) return;
        SyncEquipVisuals();
    }
#endif

    // -------------------- Equip API --------------------

    public void ToggleEquip(CameraSwitcher cam = null)
    {
        if (cam != null) cameraSwitcher = cam;
        SetEquip(!equipped);
    }

    public void Equip(CameraSwitcher cam = null)
    {
        if (cam != null) cameraSwitcher = cam;
        SetEquip(true);
    }

    public void Unequip(CameraSwitcher cam = null)
    {
        if (cam != null) cameraSwitcher = cam;
        SetEquip(false);
    }

    private void SetEquip(bool value)
    {
        equipped = value;
        SyncEquipVisuals();
    }

    // Centralized sync so every path (Inspector/code) updates cameras + event
    private void SyncEquipVisuals()
    {
        ApplyEquipVisuals();
        OnEquipChanged?.Invoke(equipped);
        lastAppliedEquipped = equipped;
    }

    private void ApplyEquipVisuals()
    {
        // Third-person when equipped, first-person when unequipped
        if (cameraSwitcher) cameraSwitcher.SetThirdPerson(equipped);
    }

    // -------------------- Balance / Visual --------------------

    private void UpdateDerived()
    {
        totalWeight = 0f;
        comHeight = 0f;

        foreach (var w in mounted)
        {
            if (!w || w.definition == null) continue;
            float wgt = Mathf.Max(0.01f, w.definition.weight);
            totalWeight += wgt;
            comHeight += wgt * Mathf.Max(0.01f, w.definition.height);
        }
        comHeight = (totalWeight > 0f) ? (comHeight / totalWeight) : 0.1f;
    }

    private void SimulateBalance(float dt)
    {
        // Difficulty grows with total weight and height
        float difficulty = totalWeight * (0.5f + comHeight * 1.5f);

        // Random drift
        float drift = (UnityEngine.Random.value - 0.5f) * 2f * tiltDrift * (1f + difficulty * 0.2f);
        tilt += drift * dt;
        tilt = Mathf.Clamp(tilt, -90f, 90f);

        if (Mathf.Abs(tilt) > fallThreshold)
        {
            FallOver();
        }
    }

    private void UpdateVisual()
    {
        if (carrierVisualPivot)
            carrierVisualPivot.localRotation = Quaternion.Euler(0f, 0f, -tilt);
    }

    /// <summary>
    /// Apply balance input from mouse buttons.
    /// Pass left=1 when LMB is down, right=1 when RMB is down.
    /// Example: ApplyBalanceInput(Input.GetMouseButton(0) ? 1f : 0f, Input.GetMouseButton(1) ? 1f : 0f);
    /// </summary>
    public void ApplyBalanceInput(float left, float right)
    {
        if (!equipped) return;
        float input = (-left + right); // left pulls negative, right pulls positive
        tilt = Mathf.MoveTowards(tilt, 0f, Mathf.Abs(input) * tiltRecoverFactor * Time.deltaTime);
    }

    // -------------------- Mount / Unload / Spill --------------------

    public bool TryMount(WorldItem world)
    {
        if (!equipped) return false;
        if (!world || world.definition == null) return false;
        if (world.definition.itemType != ItemType.Cargo) return false;

        // Remove the world object (as if picked up)
        world.OnPickedUp();

        // Create a simple visual dummy box and stack it under the cargo root
        if (!carrierCargoRoot)
        {
            Debug.LogWarning("[CarrierController] CarrierCargoRoot is null. Visual stacking will be skipped.");
        }
        else
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = $"Cargo_{world.definition.displayName}";
            box.transform.SetParent(carrierCargoRoot);

            float y = 0f;
            foreach (var m in mounted) y += m.definition.height;

            box.transform.localPosition = new Vector3(0f, y + world.definition.height * 0.5f, -0.1f);
            box.transform.localScale = new Vector3(0.4f, world.definition.height, 0.3f);

            var col = box.GetComponent<Collider>();
            if (col) Destroy(col); // remove collider on visual dummy
        }

        mounted.Add(world);
        return true;
    }

    public void UnloadTop(Vector3 dropPos, Vector3 forward)
    {
        if (mounted.Count == 0) return;

        var top = mounted[mounted.Count - 1];
        mounted.RemoveAt(mounted.Count - 1);

        // Remove the last visual dummy
        if (carrierCargoRoot && carrierCargoRoot.childCount > 0)
        {
            var last = carrierCargoRoot.GetChild(carrierCargoRoot.childCount - 1);
            Destroy(last.gameObject);
        }

        // Drop into the world with a small impulse
        top.OnDropped(dropPos, forward * 3f + Vector3.up * 1f);
    }

    private void FallOver()
    {
        // Spill all mounted items into the world and apply an impulse
        Vector3 origin = carrierCargoRoot ? carrierCargoRoot.position : transform.position + Vector3.up * 1.2f;

        foreach (var m in mounted)
        {
            if (!m) continue;

            Vector3 pos = origin + UnityEngine.Random.insideUnitSphere * 0.3f + Vector3.up * 0.5f;
            m.OnDropped(pos, Vector3.zero);

            // Ensure we have a Rigidbody reference, then add explosion force
            if (!m.rb) m.rb = m.GetComponent<Rigidbody>();
            if (m.rb) m.rb.AddExplosionForce(spillExplosionForce, origin, spillExplosionRadius, 0.2f, ForceMode.Impulse);
        }

        mounted.Clear();

        // Clear all visual dummies
        if (carrierCargoRoot)
        {
            foreach (Transform c in carrierCargoRoot) Destroy(c.gameObject);
        }

        // Reset state and switch back to first-person
        tilt = 0f;
        SetEquip(false);
    }

#if UNITY_EDITOR
    // Debug gizmos for editor
    void OnDrawGizmosSelected()
    {
        if (carrierCargoRoot)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(
                carrierCargoRoot.position + Vector3.up * 0.25f,
                new Vector3(0.4f, 0.5f, 0.3f)
            );
        }
    }
#endif
}
