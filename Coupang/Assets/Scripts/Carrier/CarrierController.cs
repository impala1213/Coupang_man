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
    // ───────────────────────────── State ─────────────────────────────
    [Header("State (selection-driven)")]
    [SerializeField] private bool active;
    public bool IsActive => active;
    public event Action<bool> OnActiveChanged;

    // ─────────────────────────── References ──────────────────────────
    [Header("References")]
    public CameraSwitcher cameraSwitcher;
    public Transform carrierCargoRoot;   // visual stack parent (static)
    public Transform carrierVisualPivot; // Z-tilt visual feedback (player body lean)

    // ────────────────────────── Back Visual ─────────────────────────
    [Header("Back Visual")]
    public bool showBackVisual = true;
    public Transform backSocket;
    public GameObject backVisualPrefab;
    public Vector3 backLocalPosition = Vector3.zero;
    public Vector3 backLocalEuler = Vector3.zero;
    public Vector3 backLocalScale = Vector3.one;
    public bool hideOnDeactivate = true;
    private GameObject backVisualInstance;

    // ─────────────────────────── Balance ────────────────────────────
    [Header("Balance")]
    [Tooltip("Current balance angle (+ = right, - = left).")]
    public float tilt;
    [Tooltip("Base random drift strength (deg/s).")]
    public float tiltDrift = 5f;
    [Tooltip("Legacy (kept for compatibility, not used for manual centering).")]
    public float tiltRecoverFactor = 30f;
    [Tooltip("If |tilt| exceeds this angle (deg), the load collapses to the tilted side.")]
    public float fallThreshold = 25f;

    [Header("Balance Input (mouse)")]
    [Tooltip("Mouse push speed (deg/s). Higher = stronger hand force.")]
    public float balancePushSpeed = 55f;
    [Tooltip("Higher difficulty reduces your effective push. 0.5 means /(1 + diff*0.5).")]
    public float balanceDifficultyPenalty = 0.5f;

    [Header("Instability & Coupling")]
    [Tooltip("How much difficulty multiplies drift. e.g., 0.6 -> drift*(1 + diff*0.6).")]
    public float driftDifficultyGain = 0.6f;
    [Tooltip("Acceleration (local X) to tilt coupling (deg per m/s^2).")]
    public float moveToTilt = 1.8f;
    [Tooltip("How much difficulty amplifies move-to-tilt coupling.")]
    public float moveToTiltDifficultyGain = 1.2f;

    [Header("Movement→Tilt filter")]
    [Tooltip("Max |a_x| used for tilt coupling (m/s^2).")]
    public float moveAccClamp = 0.25f;
    [Tooltip("Soft saturation factor (higher = softer).")]
    public float moveAccSoftness = 1.2f;
    [Tooltip("Low-pass rate (1/s), 0 = off.")]
    public float moveAccSmoothing = 12f;
    [Tooltip("Enable coupling only when horizontal speed is above this (m/s).")]
    public float minSpeedForCoupling = 0.4f;
    [Tooltip("Speed factor exponent; 1.0 = linear.")]
    public float speedFactorExponent = 1.0f;
    [Tooltip("Hard limit for how fast tilt can change (deg/s).")]
    public float tiltRateLimit = 60f;

    private float _accX; // filtered local X-acceleration

    // ─────────── Realistic spill / drop (no explosions) ────────────
    [Header("Spill Physics (realistic)")]
    public Vector2 cargoSideSpeedRange = new Vector2(0.25f, 0.8f);
    public float cargoForwardSpeed = 0.2f;
    public float cargoUpBias = -0.1f;
    public float cargoAngularVel = 1.5f;
    public PhysicsMaterial cargoFrictionMaterial;
    public bool configureCargoRigidbodies = true;

    // ───────────────────── Collapse tuning ──────────────────────────
    [Header("Collapse (tilt-driven)")]
    public float collapseSideMultiplier = 1.4f;
    public float collapseForwardMultiplier = 1.1f;
    public float collapseDownBias = -0.15f;
    public float collapseAngular = 2.2f;

    // ─────────────── Stack Lean: BAR MODE (single pivot) ───────────────
    [Header("Stack Lean (bar mode)")]
    [Tooltip("All loaded visuals will be parented to this pivot and leaned together.")]
    public Transform stackPivot;              // auto-created under carrierCargoRoot
    public float stackLeanStiffness = 8f;     // spring K (used in fallback mode)
    public float stackLeanDamping = 2.0f;    // damping (used in fallback mode)
    public float stackTiltToLean = 0.6f;    // tilt(deg) → Z-lean (fallback)
    public float stackMoveToLean = 0.35f;   // lateral acceleration → Z-lean (fallback)
    public float stackLeanMaxDeg = 12f;     // clamp (fallback)
    public float stackLeanMaxOffset = 0.04f;   // local X slide (meters)
    private float _stackLean, _stackLeanVel;
    private Vector3 _stackBaseLocalPos;

    [Header("Stack Lean Lock (follow tilt)")]
    [Tooltip("If true, the visual stack pivot follows the Tilt angle almost 1:1.")]
    public bool stackFollowTilt = true;
    [Tooltip("How quickly the visual catches the Tilt (1/s). 0 = instant.")]
    public float stackFollowSmooth = 10f;
    [Tooltip("Degrees at which lateral X offset saturates (for visual slide).")]
    public float offsetSaturationDeg = 25f;

    // ───────────────────── Wobble telemetry (for lean) ─────────────────────
    [Header("Wobble Telemetry (read-only)")]
    public Transform wobbleReference;
    public Vector3 wobbleVelocity;
    public Vector3 wobbleAcceleration;
    public float tiltVelocity; // d(tilt)/dt
    private Vector3 _lastWobblePos, _lastVel;
    private float _lastTilt;

    // ───────────────────── Derived (read-only) ──────────────────────
    [Header("Derived (read-only)")]
    public float totalWeight;
    public float comHeight;
    public bool HasMountedCargo => mounted.Count > 0;
    private readonly List<WorldItem> mounted = new List<WorldItem>();

    // ─────────────────────────── Unity ──────────────────────────────
    void Awake()
    {
        EnsureStackPivot();
    }

    void Update()
    {
        if (!active) return;

        UpdateDerived();
        SimulateBalance(Time.deltaTime);
        UpdateVisual();
        UpdateWobbleTelemetry(Time.deltaTime);
        UpdateAndApplyStackLean(Time.deltaTime);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        EnsureStackPivot();
        SyncVisuals();
    }
#endif

    // ───────────────────────── Public API ───────────────────────────
    public void SetActiveMode(bool value)
    {
        if (active == value) { SyncVisuals(); return; }
        active = value;
        SyncVisuals();
        OnActiveChanged?.Invoke(active);
    }

    /// <summary>
    /// Mouse balance control:
    /// left=1 makes tilt more negative (leans left), right=1 makes tilt more positive (leans right).
    /// If the load is leaning right (tilt>0), press LEFT to push it back toward center; pressing RIGHT accelerates the fall.
    /// Call every frame while buttons are held.
    /// </summary>
    public void ApplyBalanceInput(float left, float right)
    {
        if (!active) return;

        float dir = Mathf.Clamp(right - left, -1f, 1f); // left=-1, right=+1
        if (Mathf.Approximately(dir, 0f)) return;

        // Heavier/taller → player's hand force is less effective
        float difficulty = totalWeight * (0.5f + comHeight * 1.5f);
        float effPush = balancePushSpeed / (1f + Mathf.Max(0f, difficulty) * Mathf.Max(0f, balanceDifficultyPenalty));

        tilt += dir * effPush * Time.deltaTime;
        tilt = Mathf.Clamp(tilt, -90f, 90f);
    }

    public bool TryMount(WorldItem world)
    {
        if (!active) return false;
        if (!world || !world.definition) return false;
        if (world.definition.isCarrier) return false;

        world.OnPickedUp();

        EnsureStackPivot();

        // compute current stack top height
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

        // ensure NO per-item wobble component attached

        mounted.Add(world);
        return true;
    }

    public void UnloadTop(Vector3 dropPos, Vector3 forward)
    {
        if (mounted.Count == 0) return;

        var top = mounted[mounted.Count - 1];
        mounted.RemoveAt(mounted.Count - 1);

        if (stackPivot && stackPivot.childCount > 0)
        {
            var last = stackPivot.GetChild(stackPivot.childCount - 1);
            Destroy(last.gameObject);
        }

        ApplyRealisticDrop(top, dropPos, forward);
    }

    /// <summary>Spill all mounted cargo at a given origin (e.g., dropping carrier).</summary>
    public void SpillAllAt(Vector3 origin, Vector3 forward)
    {
        for (int i = 0; i < mounted.Count; i++)
        {
            var m = mounted[i];
            if (!m) continue;

            Vector3 pos = origin + Vector3.up * 0.1f + UnityEngine.Random.insideUnitSphere * 0.05f;
            ApplyRealisticDrop(m, pos, forward);
        }

        mounted.Clear();
        ClearVisuals();
        ResetStackLeanInstant();
        tilt = 0f;
    }

    // ───────────────────────── Internals ───────────────────────────
    private void SyncVisuals()
    {
        if (cameraSwitcher) cameraSwitcher.SetThirdPerson(active);
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

                foreach (var c in backVisualInstance.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var r in backVisualInstance.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);

                backVisualInstance.transform.SetParent(parent, false);
            }

            backVisualInstance.transform.localPosition = backLocalPosition;
            backVisualInstance.transform.localRotation = Quaternion.Euler(backLocalEuler);
            backVisualInstance.transform.localScale = backLocalScale;
            backVisualInstance.SetActive(true);
        }
        else
        {
            if (backVisualInstance && hideOnDeactivate) backVisualInstance.SetActive(false);
        }
    }

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
        // difficulty grows with mass and COM height
        float difficulty = totalWeight * (0.5f + comHeight * 1.5f);

        // 1) random drift — scales with difficulty
        float baseRand = (UnityEngine.Random.value - 0.5f) * 2f; // [-1,1]
        float drift = baseRand * tiltDrift * (1f + Mathf.Max(0f, difficulty) * Mathf.Max(0f, driftDifficultyGain));

        // 2) movement→tilt coupling with filtering
        float rawAccX = transform.InverseTransformVector(wobbleAcceleration).x;

        // low-pass filter (exponential form)
        float alpha = Mathf.Clamp01(moveAccSmoothing * dt);
        _accX = Mathf.Lerp(_accX, rawAccX, alpha);

        // clamp & soft-saturate
        float acc = Mathf.Clamp(_accX, -moveAccClamp, moveAccClamp);
        acc = acc / (1f + Mathf.Abs(acc) * Mathf.Max(0.0001f, moveAccSoftness)); // soft limit

        // speed gating
        Vector3 horizVel = new Vector3(wobbleVelocity.x, 0f, wobbleVelocity.z);
        float speed = horizVel.magnitude;
        float speedFactor = Mathf.InverseLerp(minSpeedForCoupling, minSpeedForCoupling + 2f, speed);
        if (speedFactorExponent != 1f) speedFactor = Mathf.Pow(speedFactor, Mathf.Max(0.1f, speedFactorExponent));

        float moveDrive = acc * moveToTilt * (1f + Mathf.Max(0f, difficulty) * Mathf.Max(0f, moveToTiltDifficultyGain)) * speedFactor;

        // integrate with rate limit
        float delta = (drift + moveDrive) * dt;
        float maxDelta = Mathf.Max(0f, tiltRateLimit) * dt;
        if (maxDelta > 0f) delta = Mathf.Clamp(delta, -maxDelta, maxDelta);

        tilt += delta;
        tilt = Mathf.Clamp(tilt, -90f, 90f);

        if (Mathf.Abs(tilt) > fallThreshold)
            CollapseByTilt();
    }

    private void UpdateVisual()
    {
        if (carrierVisualPivot)
            carrierVisualPivot.localRotation = Quaternion.Euler(0f, 0f, -tilt);
    }

    private void UpdateWobbleTelemetry(float dt)
    {
        var refT = wobbleReference ? wobbleReference : transform;
        Vector3 pos = refT.position;

        if (dt > 0f)
        {
            Vector3 vel = (pos - _lastWobblePos) / dt;
            Vector3 acc = (vel - _lastVel) / dt;
            float tvel = (tilt - _lastTilt) / dt;

            wobbleVelocity = Vector3.Lerp(wobbleVelocity, vel, 0.2f);
            wobbleAcceleration = Vector3.Lerp(wobbleAcceleration, acc, 0.2f);
            tiltVelocity = Mathf.Lerp(tiltVelocity, tvel, 0.2f);

            _lastVel = vel;
        }

        _lastWobblePos = pos;
        _lastTilt = tilt;
    }

    // ───────────── Stack Lean (follow-tilt or fallback) ─────────────
    private void UpdateAndApplyStackLean(float dt)
    {
        if (!stackPivot) return;

        // No cargo → relax to neutral
        if (stackPivot.childCount == 0)
        {
            _stackLean = Mathf.MoveTowards(_stackLean, 0f, 120f * dt);
            _stackLeanVel = 0f;
            stackPivot.localRotation = Quaternion.identity;
            stackPivot.localPosition = _stackBaseLocalPos;
            return;
        }

        // New mode: visual follows Tilt nearly 1:1
        if (stackFollowTilt)
        {
            float targetDeg = tilt; // visual = logic

            if (stackFollowSmooth > 0f)
            {
                float s = Mathf.Clamp01(stackFollowSmooth * dt);
                _stackLean = Mathf.Lerp(_stackLean, targetDeg, s);
            }
            else
            {
                _stackLean = targetDeg;
            }

            stackPivot.localRotation = Quaternion.Euler(0f, 0f, -_stackLean);

            float sat = Mathf.Max(1e-3f, offsetSaturationDeg);
            float slideT = Mathf.Clamp(_stackLean / sat, -1f, 1f); // -1..1
            float offX = slideT * stackLeanMaxOffset;
            stackPivot.localPosition = _stackBaseLocalPos + new Vector3(offX, 0f, 0f);
            return;
        }

        // Fallback: springy visual mode (tilt + lateral accel)
        Vector3 localAcc = transform.InverseTransformVector(wobbleAcceleration);
        float target = tilt * stackTiltToLean
                     + localAcc.x * stackMoveToLean * 12f;

        float heightFactor = Mathf.Clamp(0.9f + comHeight * 0.5f, 0.9f, 1.75f);
        target *= heightFactor;
        target = Mathf.Clamp(target, -stackLeanMaxDeg, stackLeanMaxDeg);

        float K = Mathf.Max(0.1f, stackLeanStiffness);
        float D = Mathf.Max(0f, stackLeanDamping);
        _stackLeanVel += (target - _stackLean) * K * dt;
        _stackLeanVel *= 1f / (1f + D * dt);
        _stackLean += _stackLeanVel * dt;
        _stackLean = Mathf.Clamp(_stackLean, -stackLeanMaxDeg, stackLeanMaxDeg);

        stackPivot.localRotation = Quaternion.Euler(0f, 0f, -_stackLean);
        float offX2 = (_stackLean / stackLeanMaxDeg) * stackLeanMaxOffset;
        stackPivot.localPosition = _stackBaseLocalPos + new Vector3(offX2, 0f, 0f);
    }

    private void ResetStackLeanInstant()
    {
        _stackLean = 0f; _stackLeanVel = 0f;
        if (stackPivot)
        {
            stackPivot.localRotation = Quaternion.identity;
            stackPivot.localPosition = _stackBaseLocalPos;
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
            _stackBaseLocalPos = stackPivot.localPosition;

            // migrate existing children (if any) under pivot
            var toMove = new List<Transform>();
            for (int i = 0; i < carrierCargoRoot.childCount; i++)
            {
                var c = carrierCargoRoot.GetChild(i);
                if (c == stackPivot) continue;
                toMove.Add(c);
            }
            foreach (var c in toMove)
                c.SetParent(stackPivot, true);
        }
        else
        {
            _stackBaseLocalPos = stackPivot.localPosition;
        }
    }

    private void ClearVisuals()
    {
        Transform t = stackPivot ? stackPivot : carrierCargoRoot;
        if (!t) return;
        for (int i = t.childCount - 1; i >= 0; i--)
            Destroy(t.GetChild(i).gameObject);
    }

    private void CollapseByTilt()
    {
        if (mounted.Count == 0) { tilt = 0f; return; }

        Vector3 lateralDir = (tilt >= 0f) ? transform.right : -transform.right;
        float t = Mathf.InverseLerp(fallThreshold, fallThreshold + 20f, Mathf.Abs(tilt));
        float sideMul = Mathf.Lerp(1f, collapseSideMultiplier, t);
        float fwdMul = Mathf.Lerp(1f, collapseForwardMultiplier, t);
        float angMag = Mathf.Lerp(cargoAngularVel, collapseAngular, t);

        Vector3 origin = stackPivot
            ? stackPivot.position + Vector3.up * 0.1f
            : (carrierCargoRoot ? carrierCargoRoot.position : transform.position + Vector3.up * 1.1f);

        for (int i = 0; i < mounted.Count; i++)
        {
            var m = mounted[i];
            if (!m) continue;

            float sideSpeed = UnityEngine.Random.Range(cargoSideSpeedRange.x, cargoSideSpeedRange.y) * sideMul;
            float fwdSpeed = Mathf.Max(0f, cargoForwardSpeed) * fwdMul;
            float upBias = collapseDownBias;

            Vector3 pos = origin + lateralDir * 0.05f * i + UnityEngine.Random.insideUnitSphere * 0.02f;
            ApplyDirectionalDrop(m, pos, lateralDir, sideSpeed, fwdSpeed, upBias, angMag);
        }

        mounted.Clear();
        ClearVisuals();
        ResetStackLeanInstant();
        tilt = 0f;
    }

    private void SpillAll()
    {
        Vector3 origin = stackPivot
            ? stackPivot.position
            : (carrierCargoRoot ? carrierCargoRoot.position : transform.position + Vector3.up * 1.2f);

        Vector3 fwd = transform.forward * 0.2f;
        SpillAllAt(origin, fwd);
        ResetStackLeanInstant();
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

    private void ApplyDirectionalDrop(WorldItem wi, Vector3 pos, Vector3 lateralDir, float sideSpeed, float forwardSpeed, float upBias, float angularMag)
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

        Vector3 v = lateralDir.normalized * sideSpeed + transform.forward * Mathf.Max(0f, forwardSpeed) + Vector3.up * upBias;
        rb.linearVelocity = v;
        rb.angularVelocity = UnityEngine.Random.onUnitSphere * angularMag;
    }

#if UNITY_EDITOR
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
