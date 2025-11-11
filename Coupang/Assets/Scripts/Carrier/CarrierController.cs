// Assets/Scripts/Player/CarrierController.cs
using System;
using System.Collections.Generic;
using UnityEngine;

// Unity 6+ rename compatibility (PhysicMaterial -> PhysicsMaterial)
#if UNITY_6000_0_OR_NEWER
using PhysicsMaterial = UnityEngine.PhysicsMaterial;
#else
using PhysicsMaterial = UnityEngine.PhysicMaterial;
#endif

[DisallowMultipleComponent]
public class CarrierController : MonoBehaviour
{
    // ───────────── References ─────────────
    [Header("References")]
    public Transform carrierCargoRoot;   // visual stack parent (static on back)
    public Transform stackPivot;         // parent for all loaded visuals (lean target)
    public Transform wobbleReference;    // usually player root (for velocity sampling)

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
    private float _swayVel;          // not used for dynamics, reserved

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
    private int _lastChildCount = -1;
    private static readonly List<Transform> _tmpChildren = new List<Transform>();

    // ───────────── Spill triggers (impacts/knock) ─────────────
    [Header("Spill Triggers")]
    [Tooltip("If true, falling from height or impact can spill all cargo.")]
    public bool enableImpactSpill = true;
    [Tooltip("Vertical drop height (m) to trigger spill on landing.")]
    public float fallHeightTrigger = 3.0f;
    [Tooltip("Downward impact speed (m/s) to trigger spill on landing.")]
    public float impactSpeedTrigger = 8.0f;
    [Tooltip("Sudden horizontal speed (m/s) considered a knock.")]
    public float knockSpeedTrigger = 10.0f;

    // grounded state is reported by PlayerController
    private bool _lastGrounded = true;
    private bool _airborne = false;
    private float _fallStartY;
    private Vector3 _lastPos, _lastVel;   // sampled from wobbleReference
    public Vector3 sampledVelocity { get; private set; }
    public Vector3 sampledAcceleration { get; private set; }

    // ───────────── Spill physics (realistic) ─────────────
    [Header("Spill Physics")]
    public Vector2 cargoSideSpeedRange = new Vector2(0.25f, 0.8f);
    public float cargoForwardSpeed = 0.2f;
    public float cargoUpBias = -0.1f;
    public float cargoAngularVel = 1.5f;
    public PhysicsMaterial cargoFrictionMaterial;
    public bool configureCargoRigidbodies = true;

    // ───────────── Data ─────────────
    [Header("Derived (read-only)")]
    public float totalWeight;
    public float stackTotalHeight;

    private readonly List<WorldItem> mounted = new List<WorldItem>();

    // ───────────── Unity ─────────────
    void Awake()
    {
        EnsureStackPivot();
        _lastPos = wobbleReference ? wobbleReference.position : transform.position;
    }

    void Update()
    {
        UpdateTelemetry(Time.deltaTime);
        UpdateDerived();

        UpdateSway(Time.deltaTime);
        UpdateElasticBend(Time.deltaTime);
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
    /// </summary>
    public void ReportGroundedState(bool grounded, Vector3 worldPos, Vector3 controllerVelocity)
    {
        if (!enableImpactSpill) { _lastGrounded = grounded; return; }

        if (!grounded && _lastGrounded)
        {
            // take-off
            _airborne = true;
            _fallStartY = worldPos.y;
        }
        else if (grounded && !_lastGrounded)
        {
            // landing
            if (_airborne)
            {
                float fallHeight = _fallStartY - worldPos.y; // meters
                float downSpeed = Mathf.Max(0f, -controllerVelocity.y); // m/s
                if (fallHeight >= fallHeightTrigger || downSpeed >= impactSpeedTrigger)
                {
                    Vector3 origin = stackPivot ? stackPivot.position : transform.position + Vector3.up * 1.0f;
                    SpillAllAt(origin, transform.forward);
                }
            }
            _airborne = false;
        }

        _lastGrounded = grounded;
    }

    /// <summary>
    /// Optional external knock (e.g., enemy hit). If speed is big enough, spill.
    /// </summary>
    public void NotifyExternalKnock(float speedMagnitude)
    {
        if (!enableImpactSpill) return;
        if (speedMagnitude >= knockSpeedTrigger)
        {
            Vector3 origin = stackPivot ? stackPivot.position : transform.position + Vector3.up * 1.0f;
            SpillAllAt(origin, transform.forward);
        }
    }

    /// <summary>
    /// Try to mount a world item onto the carrier (always allowed if called by Inventory when inventory has no space).
    /// </summary>
    public bool TryMount(WorldItem world)
    {
        if (!world || !world.definition) return false;
        if (world.definition.isCarrier) return false;

        world.OnPickedUp(false);
        EnsureStackPivot();

        float currentY = 0f;
        for (int i = 0; i < mounted.Count; i++)
            currentY += Mathf.Max(0.01f, mounted[i].definition.stackSize.y);

        var def = world.definition;
        Vector3 sz = def.stackSize;
        float h = Mathf.Max(0.01f, sz.y);
        float centerY = currentY + h * 0.5f;

        // spawn visual under stackPivot
        GameObject go;
        if (def.stackVisualPrefab)
        {
            go = Instantiate(def.stackVisualPrefab, stackPivot);
            go.transform.localPosition = new Vector3(0f, centerY, -0.1f);
            go.transform.localRotation = Quaternion.identity;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"CargoProxy_{def.displayName}";
            go.transform.SetParent(stackPivot, false);
            go.transform.localPosition = new Vector3(0f, centerY, -0.1f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(Mathf.Max(0.01f, sz.x), h, Mathf.Max(0.01f, sz.z));
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            var rend = go.GetComponent<Renderer>(); if (rend && rend.material) rend.material.color = def.stackColor;
        }

        mounted.Add(world);
        _lastChildCount = -1; // rebuild bend chain
        return true;
    }

    /// <summary>
    /// Called by InventorySystem when player drops the carrier item. All mounted cargo spill too.
    /// </summary>
    public void SpillAllOnCarrierDrop(Vector3 origin, Vector3 forward)
    {
        SpillAllAt(origin, forward);
    }

    /// <summary>
    /// Spill all current mounted cargo to world.
    /// </summary>
    public void SpillAllAt(Vector3 origin, Vector3 forward)
    {
        for (int i = 0; i < mounted.Count; i++)
        {
            var wi = mounted[i];
            if (!wi) continue;

            Vector3 pos = origin + Vector3.up * 0.1f + UnityEngine.Random.insideUnitSphere * 0.05f;
            ApplyRealisticDrop(wi, pos, forward);
        }

        mounted.Clear();
        ClearVisuals();
        _bendAngles.Clear(); _bendVels.Clear(); _lastChildCount = -1;
    }

    public bool HasAnyMounted() => mounted.Count > 0;

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

        // auto knock detect (optional)
        if (enableImpactSpill)
        {
            float horiz = new Vector2(sampledVelocity.x, sampledVelocity.z).magnitude;
            if (!_airborne && horiz >= knockSpeedTrigger * 1.15f) // small bias
            {
                Vector3 origin = stackPivot ? stackPivot.position : transform.position + Vector3.up * 1.0f;
                SpillAllAt(origin, transform.forward);
            }
        }
    }

    private void UpdateDerived()
    {
        totalWeight = 0f;
        stackTotalHeight = 0f;
        for (int i = 0; i < mounted.Count; i++)
        {
            var w = mounted[i];
            if (!w || !w.definition) continue;
            totalWeight += Mathf.Max(0.01f, w.definition.weight);
            stackTotalHeight += Mathf.Max(0.01f, w.definition.stackSize.y);
        }
    }

    private void UpdateSway(float dt)
    {
        if (!stackPivot) return;

        // visual sway from lateral acceleration
        float accX = transform.InverseTransformVector(sampledAcceleration).x;
        float target = accX * swayAccToDeg;

        // when almost stopping, recover to zero
        Vector2 horizV = new Vector2(sampledVelocity.x, sampledVelocity.z);
        if (horizV.magnitude < 0.2f) target = 0f;

        // smooth toward target
        _swayDeg = Mathf.MoveTowards(_swayDeg, target, swayRecoverSpeed * dt);
        _swayDeg = Mathf.Clamp(_swayDeg, -swayMaxDeg, swayMaxDeg);

        // apply to base pivot (Z-rotation) + slight x offset
        stackPivot.localRotation = Quaternion.Euler(0f, 0f, -_swayDeg);
        Vector3 lp = stackPivot.localPosition;
        lp.x = _swayDeg * swayXOffsetPerDeg;
        stackPivot.localPosition = lp;
    }

    private void UpdateElasticBend(float dt)
    {
        if (!useElasticBend || !stackPivot) return;

        int n = stackPivot.childCount;
        if (n <= 0) { _bendAngles.Clear(); _bendVels.Clear(); _lastChildCount = 0; return; }

        if (n != _lastChildCount || _bendAngles.Count != n)
        {
            EnsureBendState(n);
            _lastChildCount = n;
        }

        _tmpChildren.Clear();
        for (int i = 0; i < n; i++) _tmpChildren.Add(stackPivot.GetChild(i));
        _tmpChildren.Sort((a, b) => a.localPosition.y.CompareTo(b.localPosition.y)); // bottom→top

        float accX = transform.InverseTransformVector(sampledAcceleration).x;
        float baseDrive = _swayDeg + accX * bendAccToDrive;

        for (int i = 0; i < n; i++)
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

            // visual amplify up the stack
            float amp = Mathf.Pow(Mathf.Max(1f, bendUpAmplify), i);
            float visDeg = Mathf.Clamp(angle * amp, -bendMaxDegPerLayer * 2f, bendMaxDegPerLayer * 2f);

            var t = _tmpChildren[i];
            var e = t.localEulerAngles;
            e.x = 0f; e.y = 0f; e.z = -visDeg;
            t.localEulerAngles = e;

            Vector3 lp = t.localPosition;
            lp.x = visDeg * bendXOffsetPerDeg;
            t.localPosition = lp;
        }
    }

    private void EnsureBendState(int count)
    {
        _bendAngles.Clear(); _bendVels.Clear();
        for (int i = 0; i < count; i++)
        {
            _bendAngles.Add(_swayDeg);
            _bendVels.Add(0f);
        }
    }

    private void EnsureStackPivot()
    {
        if (!carrierCargoRoot) return;
        if (!stackPivot)
        {
            var go = new GameObject("StackPivot");
            stackPivot = go.transform;
            stackPivot.SetParent(carrierCargoRoot, false);
            stackPivot.localPosition = Vector3.zero;
            stackPivot.localRotation = Quaternion.identity;
            stackPivot.localScale = Vector3.one;
        }
    }

    private void ClearVisuals()
    {
        Transform t = stackPivot ? stackPivot : carrierCargoRoot;
        if (!t) return;
        for (int i = t.childCount - 1; i >= 0; i--)
            Destroy(t.GetChild(i).gameObject);
    }

    private void ApplyRealisticDrop(WorldItem wi, Vector3 pos, Vector3 forwardDir)
    {
        wi.OnDropped(pos, Vector3.zero);

        if (!wi.rb) wi.rb = wi.GetComponent<Rigidbody>();
        var rb = wi.rb;
        if (!rb) return;

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

        Vector3 side = (UnityEngine.Random.value < 0.5f ? -transform.right : transform.right);
        float sideSpeed = UnityEngine.Random.Range(cargoSideSpeedRange.x, cargoSideSpeedRange.y);
        Vector3 v = side * sideSpeed + forwardDir.normalized * Mathf.Max(0f, cargoForwardSpeed) + Vector3.up * cargoUpBias;

        rb.linearVelocity = v;
        rb.angularVelocity = UnityEngine.Random.onUnitSphere * cargoAngularVel;
    }
}
