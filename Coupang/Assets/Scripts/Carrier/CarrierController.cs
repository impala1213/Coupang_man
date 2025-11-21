// Assets/Scripts/Carrier/CarrierController.cs
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Unity 6 기준: PhysicsMaterial 사용
using PhysicsMaterial = UnityEngine.PhysicsMaterial;

[DisallowMultipleComponent]
public class CarrierController : MonoBehaviour
{
    // ───────────── References ─────────────
    [Header("References")]
    [Tooltip("Visual parent for the carrier frame and its cargo. If null, defaults to this transform.")]
    public Transform carrierCargoRoot;   // visual stack parent (on back)
    [Tooltip("Parent under which mounted item slot pivots are created.")]
    public Transform stackPivot;         // parent for all loaded items / slot pivots
    [Tooltip("Transform used for velocity sampling (usually the player root).")]
    public Transform wobbleReference;    // usually player root (for velocity sampling)

    private Rigidbody _rb;
    private Collider[] _colliders;

    // ───────────── Visual sway (auto recovers when stopping) ─────────────
    [Header("Sway (auto-recover)")]
    [Tooltip("Lateral accel (m/s^2) to degrees for visual sway.")]
    public float swayAccToDeg = 8f;
    [Tooltip("How fast sway goes back to zero when stopping (deg/s).")]
    public float swayRecoverSpeed = 80f;
    [Tooltip("Max sway angle (deg).")]
    public float swayMaxDeg = 10f;
    [Tooltip("Tiny lateral slide per degree (meters/deg).")]
    public float swayXOffsetPerDeg = 0.0008f;

    private float _swayDeg;          // current visual sway angle (deg, + = right)
    private float _swayVel;          // reserved

    // ───────────── Elastic bend (bottom→top inertia) ─────────────
    [Header("Elastic Bend (bottom→top)")]
    public bool useElasticBend = true;
    public float bendStiffness = 14f;      // spring K (deg/s^2)
    public float bendDamping = 3.5f;       // damping
    public float bendAccToDrive = 4f;      // extra drive from lateral accel
    public float bendUpAmplify = 1.1f;     // upper layers sway slightly more
    public float bendMaxDegPerLayer = 8f;  // clamp
    public float bendXOffsetPerDeg = 0.0006f;

    private readonly List<float> _bendAngles = new List<float>();
    private readonly List<float> _bendVels = new List<float>();
    private readonly List<Transform> _tmpChildren = new List<Transform>();
    private int _lastChildCount = -1;

    // ───────────── Impact spill (auto drop on fall/knock) ─────────────
    [Header("Impact Spill")]
    [Tooltip("Enable automatic spill on hard landing / knock.")]
    public bool enableImpactSpill = true;

    [Tooltip("Vertical drop height (m) to trigger spill on landing.")]
    public float fallHeightTrigger = 3.0f;

    [Tooltip("Downward impact speed (m/s) to trigger spill on landing.")]
    public float impactSpeedTrigger = 8.0f;

    [Tooltip("Sudden horizontal speed (m/s) considered a knock (absolute speed).")]
    public float knockSpeedTrigger = 10.0f;

    [Tooltip("Change of horizontal speed (m/s) between frames considered a sudden move.")]
    public float horizontalSpeedDeltaTrigger = 12.0f;

    [Tooltip("Minimum seconds between automatic spills (impact/knock/velocity spike).")]
    public float minTimeBetweenAutoSpills = 0.4f;

    [Header("Velocity Inherit")]
    [Tooltip("How much of the player's horizontal velocity is inherited by spilled cargo.")]
    public float inheritVelocityFactor = 0.8f;

    // grounded state is reported by PlayerController
    private bool _lastGrounded = true;
    private bool _airborne = false;
    private float _fallStartY;
    private Vector3 _lastPos;
    private Vector3 _lastVel;

    public Vector3 sampledVelocity { get; private set; }
    public Vector3 sampledAcceleration { get; private set; }

    private float _lastSpillTime;
    private Vector3 _lastHorizVel;

    // ───────────── Spill physics (realistic) ─────────────
    [Header("Spill Physics")]
    public Vector2 cargoSideSpeedRange = new Vector2(0.25f, 0.8f);
    public float cargoForwardSpeed = 0.2f;
    public float cargoUpBias = -0.1f;
    public float cargoAngularVel = 1.5f;
    public PhysicsMaterial cargoFrictionMaterial;
    public bool configureCargoRigidbodies = true;

    // ───────────── State ─────────────
    [Header("State")]
    [Tooltip("True when this carrier is dropped in the world (not worn on player).")]
    public bool isDroppedWorldCarrier = false;  // world bundle state

    // ───────────── Data ─────────────
    [Header("Derived (read-only)")]
    public float totalWeight;
    public float stackTotalHeight;

    // actual loaded WorldItem list
    private readonly List<WorldItem> mounted = new List<WorldItem>();
    public IReadOnlyList<WorldItem> MountedItems => mounted;

    // ───────────── Unity lifecycle ─────────────
    void Awake()
    {
        if (!carrierCargoRoot) carrierCargoRoot = transform;

        EnsureStackPivot();

        _lastPos = wobbleReference ? wobbleReference.position : transform.position;
        _lastHorizVel = Vector3.zero;
        _lastSpillTime = -999f;

        _rb = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>(true);
    }

    void Start()
    {
        if (!carrierCargoRoot) carrierCargoRoot = transform;
        if (!stackPivot)
        {
            var child = carrierCargoRoot.Find("StackPivot");
            if (child) stackPivot = child;
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;

        UpdateTelemetry(dt);
        UpdateDerived();
        UpdateSway(dt);
        UpdateElasticBend(dt);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EnsureStackPivot();
    }
#endif

    // ───────────── Public API ─────────────

    /// <summary>
    /// PlayerController should call this each frame to report grounded and position.
    /// Used for impact-based auto spill.
    /// </summary>
    public void ReportGroundedState(bool grounded, Vector3 worldPos, Vector3 controllerVelocity)
    {
        if (!enableImpactSpill || isDroppedWorldCarrier)
        {
            _lastGrounded = grounded;
            return;
        }

        if (!grounded && _lastGrounded)
        {
            _airborne = true;
            _fallStartY = worldPos.y;
        }
        else if (grounded && !_lastGrounded)
        {
            if (_airborne)
            {
                float fallHeight = _fallStartY - worldPos.y;
                float downSpeed = Mathf.Max(0f, -controllerVelocity.y);

                if (fallHeight >= fallHeightTrigger || downSpeed >= impactSpeedTrigger)
                {
                    Vector3 origin = stackPivot
                        ? stackPivot.position
                        : transform.position + Vector3.up * 1.0f;

                    Vector3 horizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
                    Vector3 dir = horizVel.sqrMagnitude > 0.01f
                        ? horizVel.normalized
                        : transform.forward;

                    SpillAllAt(origin, dir);
                }
            }

            _airborne = false;
        }

        _lastGrounded = grounded;
    }

    /// <summary>
    /// Called when an external system (e.g., enemy hit) notifies a knock.
    /// If speed is big enough, spill.
    /// </summary>
    public void NotifyExternalKnock(float speedMagnitude)
    {
        if (!enableImpactSpill || isDroppedWorldCarrier) return;

        if (speedMagnitude >= knockSpeedTrigger &&
            Time.time >= _lastSpillTime + minTimeBetweenAutoSpills)
        {
            Vector3 origin = stackPivot
                ? stackPivot.position
                : transform.position + Vector3.up * 1.0f;

            Vector3 horizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
            Vector3 dir = horizVel.sqrMagnitude > 0.01f
                ? horizVel.normalized
                : transform.forward;

            SpillAllAt(origin, dir);
        }
    }

    /// <summary>
    /// Try to mount a world item onto the carrier (top stacking).
    /// Uses ItemDefinition.stackSize.y for spacing so items do not overlap.
    /// </summary>
    public bool TryMount(WorldItem world)
    {
        if (!world || !world.definition) return false;
        if (world.definition.isCarrier) return false; // do not load carrier into carrier

        EnsureStackPivot();

        // Sum of all existing stack heights using ItemDefinition.stackSize.y
        float currentY = 0f;
        for (int i = 0; i < mounted.Count; i++)
        {
            var w = mounted[i];
            if (w && w.definition)
            {
                var sz = w.definition.stackSize;
                float h = Mathf.Max(0.01f, sz.y);
                currentY += h;
            }
        }

        var def = world.definition;
        Vector3 newSize = def.stackSize;
        float newHeight = Mathf.Max(0.01f, newSize.y);

        // Place new item so its center is at (current total height + half of its own height)
        float centerY = currentY + newHeight * 0.5f;

        int slotIndex = mounted.Count;
        GameObject slotGO = new GameObject($"CarrierSlot_{slotIndex}");
        Transform slotPivot = slotGO.transform;
        slotPivot.SetParent(stackPivot, false);
        slotPivot.localPosition = new Vector3(0f, centerY, -0.1f);
        slotPivot.localRotation = Quaternion.identity;
        slotPivot.localScale = Vector3.one;

        // Mount world item to carrier slot
        world.EnterCarrierMountMode(this, slotIndex, slotPivot);

        mounted.Add(world);
        _lastChildCount = -1; // rebuild bend chain
        return true;
    }

    /// <summary>
    /// Drop the entire carrier (frame + mounted cargo) into the world as a single bundle.
    /// Items remain mounted on the carrier and do not spill.
    /// </summary>
    public void DropAsBundle(Vector3 worldPos, Vector3 forward)
    {
        // Detach the carrier root from its current parent (usually the player).
        Transform root = transform;
        root.SetParent(null, true);

        // Compute a flat forward direction based on the given forward.
        Vector3 flatF = new Vector3(forward.x, 0f, forward.z);
        if (flatF.sqrMagnitude < 0.0001f)
            flatF = root.forward;

        flatF.y = 0f;
        if (flatF.sqrMagnitude > 0.0001f)
            flatF.Normalize();
        else
            flatF = Vector3.forward;

        // Place the carrier bundle in the world.
        root.position = worldPos;
        root.rotation = Quaternion.LookRotation(flatF, Vector3.up);

        // Enable physics so gravity applies after dropping.
        if (_rb == null)
            _rb = GetComponent<Rigidbody>();

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.useGravity = true;
        }

        // Enable all colliders so carrier can collide with world after drop.
        if (_colliders == null || _colliders.Length == 0)
            _colliders = GetComponentsInChildren<Collider>(true);

        if (_colliders != null)
        {
            foreach (var c in _colliders)
            {
                if (!c) continue;
                c.enabled = true;
            }
        }

        // Mark this carrier as a dropped world object so it no longer reacts
        // to player-based impact spill or sway/bend updates (only physics).
        isDroppedWorldCarrier = true;

        // Reset bend state so visuals can rebuild cleanly if needed.
        _bendAngles.Clear();
        _bendVels.Clear();
        _lastChildCount = -1;
    }

    /// <summary>
    /// Called by InventorySystem when player drops the carrier item (quick drop).
    /// All mounted cargo spill separately in this path.
    /// </summary>
    public void SpillAllOnCarrierDrop(Vector3 origin, Vector3 forward)
    {
        SpillAllAt(origin, forward);
    }

    /// <summary>
    /// Spill all current mounted cargo to world.
    /// forwardDir is used as fallback direction when no velocity is present.
    /// </summary>
    public void SpillAllAt(Vector3 origin, Vector3 forwardDir)
    {
        for (int i = 0; i < mounted.Count; i++)
        {
            var wi = mounted[i];
            if (!wi) continue;

            Vector3 pos = origin + Vector3.up * 0.1f + UnityEngine.Random.insideUnitSphere * 0.05f;
            ApplyRealisticDrop(wi, pos, forwardDir);
        }

        mounted.Clear();
        ClearVisuals();
        _bendAngles.Clear();
        _bendVels.Clear();
        _lastChildCount = -1;

        _lastSpillTime = Time.time;
    }

    public bool HasAnyMounted() => mounted.Count > 0;

    /// <summary>
    /// Build debug string listing mounted items and slot indices.
    /// Used by CarrierSlotUI.
    /// </summary>
    public string GetSlotDebugString()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Carrier Slots:");

        if (mounted.Count == 0)
        {
            sb.AppendLine("(empty)");
            return sb.ToString();
        }

        for (int i = 0; i < mounted.Count; i++)
        {
            var wi = mounted[i];
            if (!wi || wi.definition == null) continue;

            int slotIdx = wi.carrierSlotIndex >= 0 ? wi.carrierSlotIndex : i;
            sb.Append('[').Append(slotIdx).Append("] ").AppendLine(wi.definition.displayName);
        }

        return sb.ToString();
    }

    // ───────────── Internals ─────────────

    private void UpdateTelemetry(float dt)
    {
        var t = wobbleReference ? wobbleReference : transform;
        Vector3 p = t.position;

        if (dt > 0f)
        {
            Vector3 v = (p - _lastPos) / dt;
            Vector3 a = (v - _lastVel) / dt;

            sampledVelocity = Vector3.Lerp(sampledVelocity, v, 0.2f);
            sampledAcceleration = Vector3.Lerp(sampledAcceleration, a, 0.2f);

            _lastVel = v;
        }

        _lastPos = p;

        if (!enableImpactSpill || isDroppedWorldCarrier)
            return;

        Vector3 curHorizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
        float curSpeed = curHorizVel.magnitude;
        float lastSpeed = _lastHorizVel.magnitude;
        float deltaSpeed = curSpeed - lastSpeed;

        if (deltaSpeed >= horizontalSpeedDeltaTrigger &&
            Time.time >= _lastSpillTime + minTimeBetweenAutoSpills)
        {
            Vector3 origin = stackPivot
                ? stackPivot.position
                : transform.position + Vector3.up * 1.0f;

            Vector3 dir = curHorizVel.sqrMagnitude > 0.01f
                ? curHorizVel.normalized
                : transform.forward;

            SpillAllAt(origin, dir);
        }

        _lastHorizVel = curHorizVel;
    }

    private void UpdateDerived()
    {
        totalWeight = 0f;
        stackTotalHeight = 0f;

        for (int i = 0; i < mounted.Count; i++)
        {
            var w = mounted[i];
            if (!w || !w.definition) continue;

            // Use ItemDefinition.stackSize for all derived info
            float wWeight = Mathf.Max(0.01f, w.definition.weight);
            float h = Mathf.Max(0.01f, w.definition.stackSize.y);

            totalWeight += wWeight;
            stackTotalHeight += h;
        }
    }

    private void UpdateSway(float dt)
    {
        if (!stackPivot) return;
        if (isDroppedWorldCarrier) return;

        float accX = transform.InverseTransformVector(sampledAcceleration).x;
        float target = accX * swayAccToDeg;

        Vector2 horizV = new Vector2(sampledVelocity.x, sampledVelocity.z);
        if (horizV.magnitude < 0.2f) target = 0f;

        _swayDeg = Mathf.MoveTowards(_swayDeg, target, swayRecoverSpeed * dt);
        _swayDeg = Mathf.Clamp(_swayDeg, -swayMaxDeg, swayMaxDeg);

        stackPivot.localRotation = Quaternion.Euler(0f, 0f, -_swayDeg);
        Vector3 lp = stackPivot.localPosition;
        lp.x = _swayDeg * swayXOffsetPerDeg;
        stackPivot.localPosition = lp;
    }

    private void UpdateElasticBend(float dt)
    {
        if (!useElasticBend || !stackPivot) return;
        if (isDroppedWorldCarrier) return;

        int n = stackPivot.childCount;
        if (n <= 0)
        {
            _bendAngles.Clear();
            _bendVels.Clear();
            _lastChildCount = 0;
            return;
        }

        if (n != _lastChildCount || _bendAngles.Count != n)
        {
            EnsureBendState(n);
            _lastChildCount = n;
        }

        _tmpChildren.Clear();
        for (int i = 0; i < n; i++)
        {
            var child = stackPivot.GetChild(i);
            if (child != null)
                _tmpChildren.Add(child);
        }

        float accX = transform.InverseTransformVector(sampledAcceleration).x;
        float baseDrive = accX * bendAccToDrive;

        for (int i = 0; i < _tmpChildren.Count; i++)
        {
            float target = (i == 0) ? baseDrive : _bendAngles[i - 1];
            float angle = _bendAngles[i];
            float vel = _bendVels[i];

            vel += (target - angle) * Mathf.Max(0.1f, bendStiffness) * dt;
            vel *= 1f / (1f + Mathf.Max(0f, bendDamping) * dt);
            angle += vel * dt;

            angle = Mathf.Clamp(angle, -bendMaxDegPerLayer, bendMaxDegPerLayer);

            _bendAngles[i] = angle;
            _bendVels[i] = vel;

            float amp = Mathf.Pow(Mathf.Max(1f, bendUpAmplify), i);
            float visDeg = Mathf.Clamp(angle * amp, -bendMaxDegPerLayer * 2f, bendMaxDegPerLayer * 2f);

            var t = _tmpChildren[i];
            if (t == null) continue;

            var e = t.localEulerAngles;
            e.x = 0f;
            e.y = 0f;
            e.z = -visDeg;
            t.localEulerAngles = e;

            Vector3 lp = t.localPosition;
            lp.x = visDeg * bendXOffsetPerDeg;
            t.localPosition = lp;
        }
    }

    private void EnsureBendState(int n)
    {
        _bendAngles.Clear();
        _bendVels.Clear();
        for (int i = 0; i < n; i++)
        {
            _bendAngles.Add(0f);
            _bendVels.Add(0f);
        }
    }

    private void EnsureStackPivot()
    {
        if (!carrierCargoRoot) carrierCargoRoot = transform;

        if (!stackPivot)
        {
            var child = carrierCargoRoot.Find("StackPivot");
            if (child)
            {
                stackPivot = child;
            }
            else
            {
                var go = new GameObject("StackPivot");
                stackPivot = go.transform;
                stackPivot.SetParent(carrierCargoRoot, false);
                stackPivot.localPosition = Vector3.zero;
                stackPivot.localRotation = Quaternion.identity;
                stackPivot.localScale = Vector3.one;
            }
        }
    }

    private void ClearVisuals()
    {
        Transform t = stackPivot ? stackPivot : carrierCargoRoot;
        if (!t) return;

        for (int i = t.childCount - 1; i >= 0; i--)
        {
            Destroy(t.GetChild(i).gameObject);
        }
    }

    /// <summary>
    /// Actually returns the item to the world and applies realistic velocity
    /// based on sampled player movement.
    /// </summary>
    private void ApplyRealisticDrop(WorldItem wi, Vector3 pos, Vector3 forwardDir)
    {
        if (wi == null) return;

        Vector3 horizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
        float horizSpeed = horizVel.magnitude;

        Vector3 mainDir;
        float mainSpeed;

        if (horizSpeed > 0.1f)
        {
            mainDir = horizVel.normalized;
            mainSpeed = horizSpeed * Mathf.Max(0f, inheritVelocityFactor);
        }
        else
        {
            Vector3 fwd = forwardDir.sqrMagnitude > 0.0001f
                ? forwardDir.normalized
                : transform.forward;

            mainDir = fwd;
            mainSpeed = Mathf.Max(0f, cargoForwardSpeed);
        }

        Vector3 side = Vector3.zero;
        if (cargoSideSpeedRange.y > 0f)
        {
            Vector3 sideBase = Vector3.Cross(mainDir, Vector3.up);
            if (sideBase.sqrMagnitude > 0.0001f)
            {
                sideBase.Normalize();
                if (UnityEngine.Random.value < 0.5f)
                    sideBase = -sideBase;

                float sideSpeed = UnityEngine.Random.Range(cargoSideSpeedRange.x, cargoSideSpeedRange.y);
                side = sideBase * sideSpeed;
            }
        }

        Vector3 v =
            mainDir * mainSpeed +
            side +
            Vector3.up * cargoUpBias;

        // Return WorldItem from carrier mount mode back to world physics.
        wi.OnDropped(pos, v);

        if (!wi.rb) wi.rb = wi.GetComponent<Rigidbody>();
        var rb = wi.rb;
        if (rb != null)
        {
            if (configureCargoRigidbodies)
            {
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.angularDamping = Mathf.Max(0.2f, rb.angularDamping);
            }

            if (cargoFrictionMaterial)
            {
                var cols = wi.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < cols.Length; c++)
                    cols[c].sharedMaterial = cargoFrictionMaterial;
            }

            rb.linearVelocity = v;
            rb.angularVelocity = UnityEngine.Random.onUnitSphere * cargoAngularVel;
        }
    }
}
