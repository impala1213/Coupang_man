// Assets/Scripts/Player/CarrierController.cs
using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CarrierController : MonoBehaviour
{
    // 式式式式式式式式式式式式式 State (selection-driven) 式式式式式式式式式式式式式
    [Header("State (selection-driven)")]
    [SerializeField] private bool active;              // true when carrier is the active hotbar item
    public bool IsActive => active;
    public event Action<bool> OnActiveChanged;

    // 式式式式式式式式式式式式式 References 式式式式式式式式式式式式式
    [Header("References")]
    [Tooltip("Switches 1P/3P view based on IsActive.")]
    public CameraSwitcher cameraSwitcher;
    [Tooltip("Where cargo proxy boxes are stacked.")]
    public Transform carrierCargoRoot;
    [Tooltip("Optional visual pivot to apply Z tilt feedback.")]
    public Transform carrierVisualPivot;

    [Header("Inventory Drop (optional)")]
    [Tooltip("Inventory system to drop the carrier item from when unequipping.")]
    public InventorySystem inventory;
    [Tooltip("Origin transform for dropping the carrier to the ground.")]
    public Transform dropOrigin;

    // 式式式式式式式式式式式式式 Back Visual (third-person) 式式式式式式式式式式式式式
    [Header("Back Visual")]
    public bool showBackVisual = true;
    public Transform backSocket;              // parent on player's back; if null, uses this transform
    public GameObject backVisualPrefab;       // visual-only prefab (no physics)
    public Vector3 backLocalPosition = Vector3.zero;
    public Vector3 backLocalEuler = Vector3.zero;
    public Vector3 backLocalScale = Vector3.one;
    public bool hideOnDeactivate = true;

    private GameObject backVisualInstance;

    // 式式式式式式式式式式式式式 Balance 式式式式式式式式式式式式式
    [Header("Balance")]
    [Tooltip("Left(-)/Right(+) tilt angle in degrees (debug).")]
    public float tilt;
    [Tooltip("Natural drift strength (deg/sec).")]
    public float tiltDrift = 5f;
    [Tooltip("How fast clicks pull tilt back to center (deg/sec).")]
    public float tiltRecoverFactor = 30f;
    [Tooltip("Fall-over threshold (deg). If |tilt| exceeds, cargo spills.")]
    public float fallThreshold = 25f;

    // 式式式式式式式式式式式式式 Spill Physics 式式式式式式式式式式式式式
    [Header("Spill Physics")]
    public float spillExplosionForce = 6f;
    public float spillExplosionRadius = 2.5f;

    // 式式式式式式式式式式式式式 Derived (read-only) 式式式式式式式式式式式式式
    [Header("Derived (read-only)")]
    [Tooltip("Total mass of mounted items (kg).")]
    public float totalWeight;
    [Tooltip("Weighted average center height from stack bottom (m).")]
    public float comHeight;

    public bool HasMountedCargo => mounted.Count > 0;
    private readonly List<WorldItem> mounted = new List<WorldItem>();

    // 式式式式式式式式式式式式式 Unequip hold (weight-scaled) 式式式式式式式式式式式式式
    [Header("Unequip Hold Time (optional)")]
    [Tooltip("Hold time when no cargo is mounted (seconds).")]
    public float unequipHoldMin = 0.06f;   // almost instant when empty
    [Tooltip("Maximum hold time when very heavy (seconds).")]
    public float unequipHoldMax = 1.25f;
    [Tooltip("Total weight at which hold time reaches 'unequipHoldMax'.")]
    public float weightAtMaxHold = 30f;

    // Runtime hold state (read-only to UI)
    [Header("Unequip Hold State (read-only)")]
    [SerializeField] private bool unequipHoldActive;
    [SerializeField] private float unequipHoldElapsed;
    [SerializeField] private float unequipHoldRequired;

    /// <summary>Normalized progress in [0,1] while holding G to unequip.</summary>
    public float UnequipHoldProgress01 =>
        unequipHoldRequired > 0f ? Mathf.Clamp01(unequipHoldElapsed / unequipHoldRequired) : 0f;

    /// <summary>Is the hold-to-unequip currently active?</summary>
    public bool IsUnequipHoldActive => unequipHoldActive;

    /// <summary>Compute required G-hold seconds based on current totalWeight.</summary>
    public float GetUnequipHoldSeconds()
    {
        float t = Mathf.Clamp01(totalWeight / Mathf.Max(0.01f, weightAtMaxHold));
        return Mathf.Lerp(unequipHoldMin, unequipHoldMax, t);
    }

    /// <summary>
    /// Called on G key down while in carrier mode.
    /// If empty ⊥ instant unequip & drop; else start timed hold using current weight.
    /// Returns true if unequipped instantly (and dropped), false if a timed hold started.
    /// </summary>
    public bool StartUnequipHoldOrInstant()
    {
        if (!active) return false; // nothing to do if not in carrier mode

        if (totalWeight <= 0.01f)
        {
            // empty: instant 1) spill none, 2) deactivate & 1P, 3) drop carrier item
            DoUnequipAndDrop();
            ResetUnequipHold();
            return true;
        }

        unequipHoldRequired = GetUnequipHoldSeconds();
        unequipHoldElapsed = 0f;
        unequipHoldActive = true;
        return false;
    }

    /// <summary>
    /// Tick the hold (call every frame while G is held). Returns true when unequip+drop has completed this frame.
    /// </summary>
    public bool TickUnequipHold(float deltaTime, bool keyStillHeld)
    {
        if (!unequipHoldActive) return false;
        if (!keyStillHeld)
        {
            // abort
            ResetUnequipHold();
            return false;
        }

        unequipHoldElapsed += deltaTime;
        if (unequipHoldElapsed >= unequipHoldRequired)
        {
            // finish: also drop carrier item
            DoUnequipAndDrop();
            ResetUnequipHold();
            return true;
        }
        return false;
    }

    /// <summary>Abort any ongoing hold (e.g., on G key up).</summary>
    public void CancelUnequipHold() => ResetUnequipHold();

    private void ResetUnequipHold()
    {
        unequipHoldActive = false;
        unequipHoldElapsed = 0f;
        unequipHoldRequired = 0f;
    }

    // 式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式式

    void Update()
    {
        if (!active) return;

        UpdateDerived();
        SimulateBalance(Time.deltaTime);
        UpdateVisual();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Keep visuals consistent when editing inspector values
        SyncVisuals();
    }
#endif

    // 式式式式式式式式式式式式式 Selection-driven API 式式式式式式式式式式式式式
    public void SetActiveMode(bool value)
    {
        if (active == value) { SyncVisuals(); return; }
        active = value;
        SyncVisuals();
        OnActiveChanged?.Invoke(active);

        if (!active)
            ResetUnequipHold(); // make sure hold state is cleared when leaving carrier mode
    }

    private void SyncVisuals()
    {
        if (cameraSwitcher) cameraSwitcher.SetThirdPerson(active); // respect current state
        UpdateBackVisual();
    }

    private void UpdateBackVisual()
    {
        if (!showBackVisual)
        {
            if (backVisualInstance) backVisualInstance.SetActive(false);
            return;
        }

        if (active)
        {
            Transform parent = backSocket ? backSocket : transform;

            if (!backVisualInstance)
            {
                backVisualInstance = backVisualPrefab
                    ? Instantiate(backVisualPrefab, parent, false)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);

                if (!backVisualPrefab)
                    backVisualInstance.name = "CarrierBackVisual_Placeholder";

                // Ensure no physics on the attached visual
                foreach (var c in backVisualInstance.GetComponentsInChildren<Collider>(true))
                    Destroy(c);
                foreach (var r in backVisualInstance.GetComponentsInChildren<Rigidbody>(true))
                    Destroy(r);

                backVisualInstance.transform.SetParent(parent, false);
            }

            backVisualInstance.transform.localPosition = backLocalPosition;
            backVisualInstance.transform.localRotation = Quaternion.Euler(backLocalEuler);
            backVisualInstance.transform.localScale = backLocalScale;
            backVisualInstance.SetActive(true);
        }
        else
        {
            if (backVisualInstance && hideOnDeactivate)
                backVisualInstance.SetActive(false);
        }
    }

    // 式式式式式式式式式式式式式 Balance / Visual 式式式式式式式式式式式式式
    private void UpdateDerived()
    {
        totalWeight = 0f;
        float weightedCenter = 0f;
        float cumulativeY = 0f;

        for (int i = 0; i < mounted.Count; i++)
        {
            var w = mounted[i];
            if (!w || !w.definition) continue;

            float mass = Mathf.Max(0.01f, w.definition.weight);
            Vector3 sz = w.definition.stackSize;
            float h = Mathf.Max(0.01f, sz.y);

            totalWeight += mass;

            float centerY = cumulativeY + h * 0.5f;
            weightedCenter += mass * centerY;

            cumulativeY += h;
        }

        comHeight = (totalWeight > 0f) ? (weightedCenter / totalWeight) : 0.1f;
    }

    private void SimulateBalance(float dt)
    {
        float difficulty = totalWeight * (0.5f + comHeight * 1.5f);
        float drift = (UnityEngine.Random.value - 0.5f) * 2f * tiltDrift * (1f + difficulty * 0.2f);
        tilt += drift * dt;
        tilt = Mathf.Clamp(tilt, -90f, 90f);

        if (Mathf.Abs(tilt) > fallThreshold)
            SpillAll();
    }

    private void UpdateVisual()
    {
        if (carrierVisualPivot)
            carrierVisualPivot.localRotation = Quaternion.Euler(0f, 0f, -tilt);
    }

    /// <summary>
    /// Feed balance input while LMB/RMB are held. Pass left=1 when LMB is held, right=1 when RMB is held.
    /// </summary>
    public void ApplyBalanceInput(float left, float right)
    {
        if (!active) return;
        float input = (-left + right); // left pulls negative, right pulls positive
        tilt = Mathf.MoveTowards(tilt, 0f, Mathf.Abs(input) * tiltRecoverFactor * Time.deltaTime);
    }

    // 式式式式式式式式式式式式式 Mount / Unload / Spill 式式式式式式式式式式式式式
    /// <summary>Mount a world item onto the carrier (only when active). Returns true if mounted.</summary>
    public bool TryMount(WorldItem world)
    {
        if (!active) return false;
        if (!world || !world.definition) return false;
        if (world.definition.isCarrier) return false;

        world.OnPickedUp();

        if (!carrierCargoRoot)
        {
            mounted.Add(world);
            return true;
        }

        // compute current stack height
        float currentY = 0f;
        for (int i = 0; i < mounted.Count; i++)
            currentY += Mathf.Max(0.01f, mounted[i].definition.stackSize.y);

        var def = world.definition;
        Vector3 sz = def.stackSize;
        float h = Mathf.Max(0.01f, sz.y);

        // visual on-carrier proxy (prefab or primitive)
        if (def.stackVisualPrefab)
        {
            var go = Instantiate(def.stackVisualPrefab, carrierCargoRoot);
            go.transform.localPosition = new Vector3(0f, currentY + h * 0.5f, -0.1f);
            go.transform.localRotation = Quaternion.identity;

            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);
        }
        else
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"CargoProxy_{def.displayName}";
            go.transform.SetParent(carrierCargoRoot);
            go.transform.localPosition = new Vector3(0f, currentY + h * 0.5f, -0.1f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(Mathf.Max(0.01f, sz.x), h, Mathf.Max(0.01f, sz.z));

            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            var r = go.GetComponent<Renderer>(); if (r && r.material) r.material.color = def.stackColor;
        }

        mounted.Add(world);
        return true;
    }

    /// <summary>Unload the topmost cargo back to the world with a small impulse.</summary>
    public void UnloadTop(Vector3 dropPos, Vector3 forward)
    {
        if (mounted.Count == 0) return;

        var top = mounted[mounted.Count - 1];
        mounted.RemoveAt(mounted.Count - 1);

        if (carrierCargoRoot && carrierCargoRoot.childCount > 0)
        {
            var last = carrierCargoRoot.GetChild(carrierCargoRoot.childCount - 1);
            Destroy(last.gameObject);
        }

        top.OnDropped(dropPos, forward * 3f + Vector3.up * 1f);
    }

    private void SpillAll()
    {
        Vector3 origin = carrierCargoRoot ? carrierCargoRoot.position : transform.position + Vector3.up * 1.2f;

        for (int i = 0; i < mounted.Count; i++)
        {
            var m = mounted[i];
            if (!m) continue;

            Vector3 pos = origin + UnityEngine.Random.insideUnitSphere * 0.3f + Vector3.up * 0.5f;
            m.OnDropped(pos, Vector3.zero);

            if (!m.rb) m.rb = m.GetComponent<Rigidbody>();
            if (m.rb) m.rb.AddExplosionForce(spillExplosionForce, origin, spillExplosionRadius, 0.2f, ForceMode.Impulse);
        }

        mounted.Clear();

        // clear stack visuals
        if (carrierCargoRoot)
        {
            for (int i = carrierCargoRoot.childCount - 1; i >= 0; i--)
                Destroy(carrierCargoRoot.GetChild(i).gameObject);
        }

        tilt = 0f;
        // keep active mode; the player exits by changing the active slot or via unequip
    }

    // 式式式式式式式式式式式式式 Internal: do unequip + drop from inventory 式式式式式式式式式式式式式
    private void DoUnequipAndDrop()
    {
        // If there is cargo stacked, spill them before unequip to avoid dangling visuals/state
        if (HasMountedCargo)
            SpillAll();

        // 1) switch camera & back visual off
        SetActiveMode(false);

        // 2) drop the carrier item from inventory into the world
        if (inventory)
        {
            Transform origin = dropOrigin ? dropOrigin : transform;
            Vector3 forward = transform.forward;
            bool dropped = inventory.DropActiveItem(origin, forward);
            if (!dropped)
            {
                Debug.LogWarning("[CarrierController] Failed to drop carrier from inventory (active slot empty?).");
            }
            else
            {
                // 3) optional: auto-select a non-carrier slot so we do not re-enter carrier mode
                inventory.SelectFirstNonCarrierSlot();
            }
        }
        else
        {
            Debug.LogWarning("[CarrierController] Inventory reference not set; cannot drop carrier item on unequip.");
        }
    }
}
