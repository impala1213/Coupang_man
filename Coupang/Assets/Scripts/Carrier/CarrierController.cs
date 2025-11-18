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
    private int _lastChildCount = -1;
    private static readonly List<Transform> _tmpChildren = new List<Transform>();

    // ───────────── Spill triggers (impacts/knock/velocity spike) ─────────────
    [Header("Spill Triggers")]
    [Tooltip("If true, falling from height or impact can spill all cargo.")]
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
        _lastHorizVel = Vector3.zero;
        _lastSpillTime = -999f;
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
    /// </summary>
    public void ReportGroundedState(bool grounded, Vector3 worldPos, Vector3 controllerVelocity)
    {
        if (!enableImpactSpill)
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

                    // 착지 충격으로 스필 → 현재 이동 방향 기준
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
    /// Optional external knock (e.g., enemy hit). If speed is big enough, spill.
    /// </summary>
    public void NotifyExternalKnock(float speedMagnitude)
    {
        if (!enableImpactSpill) return;

        if (speedMagnitude >= knockSpeedTrigger &&
            Time.time >= _lastSpillTime + minTimeBetweenAutoSpills)
        {
            Vector3 origin = stackPivot
                ? stackPivot.position
                : transform.position + Vector3.up * 1.0f;

            // 넉백일 때는 플레이어 수평 속도 방향으로 튀어나가게
            Vector3 horizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
            Vector3 dir = horizVel.sqrMagnitude > 0.01f
                ? horizVel.normalized
                : transform.forward;

            SpillAllAt(origin, dir);
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
            go.transform.localScale = new Vector3(
                Mathf.Max(0.01f, sz.x),
                h,
                Mathf.Max(0.01f, sz.z)
            );

            var col = go.GetComponent<Collider>();
            if (col) Destroy(col);

            var rend = go.GetComponent<Renderer>();
            if (rend && rend.material)
            {
                rend.material.color = def.stackColor;
            }
        }

        mounted.Add(world);
        _lastChildCount = -1;
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
    /// forwardDir는 fallback 방향(velocity가 없는 경우용).
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

        if (!enableImpactSpill) return;

        float now = Time.time;
        bool canSpill = now >= _lastSpillTime + minTimeBetweenAutoSpills;

        Vector3 curHorizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
        float horizSpeed = curHorizVel.magnitude;

        Vector3 prevHorizVel = _lastHorizVel;
        float horizDelta = (curHorizVel - prevHorizVel).magnitude;

        // 1) sudden horizontal speed spike (velocity jump)
        if (canSpill &&
            !_airborne &&
            horizDelta >= horizontalSpeedDeltaTrigger)
        {
            Vector3 origin = stackPivot
                ? stackPivot.position
                : transform.position + Vector3.up * 1.0f;

            Vector3 dir = curHorizVel.sqrMagnitude > 0.01f
                ? curHorizVel.normalized
                : transform.forward;

            SpillAllAt(origin, dir);
            canSpill = false;
        }

        // 2) high absolute horizontal speed (legacy knockSpeedTrigger)
        if (canSpill &&
            !_airborne &&
            horizSpeed >= knockSpeedTrigger * 1.15f)
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

            totalWeight += Mathf.Max(0.01f, w.definition.weight);
            stackTotalHeight += Mathf.Max(0.01f, w.definition.stackSize.y);
        }
    }

    private void UpdateSway(float dt)
    {
        if (!stackPivot) return;

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
            _tmpChildren.Add(stackPivot.GetChild(i));

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

            float amp = Mathf.Pow(Mathf.Max(1f, bendUpAmplify), i);
            float visDeg = Mathf.Clamp(angle * amp, -bendMaxDegPerLayer * 2f, bendMaxDegPerLayer * 2f);

            var t = _tmpChildren[i];
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

    private void EnsureBendState(int count)
    {
        _bendAngles.Clear();
        _bendVels.Clear();

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

    /// <summary>
    /// 실제로 짐을 월드에 생성하고, 플레이어 속도 방향으로 튀어나가게 하는 부분.
    /// </summary>
    private void ApplyRealisticDrop(WorldItem wi, Vector3 pos, Vector3 forwardDir)
    {
        if (wi == null) return;

        ItemDefinition def = wi.definition;

        // 1) 내구도 스냅샷
        int curDur = 0;
        int maxDur = 0;
        bool hasDur = wi.TryGetDurability(out curDur, out maxDur);

        // 2) 드롭 프리팹 결정
        GameObject prefab = (def != null && def.worldPrefab != null)
            ? def.worldPrefab
            : wi.gameObject;

        // 3) 새로운 인스턴스 생성
        GameObject inst = Instantiate(prefab, pos, Quaternion.identity);
        WorldItem newWI = inst.GetComponent<WorldItem>();

        if (newWI != null)
        {
            // 내구도 복원
            if (hasDur && maxDur > 0)
            {
                newWI.ApplyDurability(curDur, maxDur, true);
            }

            if (!newWI.rb) newWI.rb = newWI.GetComponent<Rigidbody>();
            var rb = newWI.rb;
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
                    var cols = newWI.GetComponentsInChildren<Collider>(true);
                    for (int c = 0; c < cols.Length; c++)
                        cols[c].sharedMaterial = cargoFrictionMaterial;
                }

                // ── 여기서 "플레이어가 날아가는 방향"으로 속도 설정 ──
                Vector3 horizVel = new Vector3(sampledVelocity.x, 0f, sampledVelocity.z);
                float horizSpeed = horizVel.magnitude;

                Vector3 mainDir;
                float mainSpeed;

                if (horizSpeed > 0.1f)
                {
                    // 플레이어 실제 이동 방향 + 속도 상속
                    mainDir = horizVel.normalized;
                    mainSpeed = horizSpeed * Mathf.Max(0f, inheritVelocityFactor);
                }
                else
                {
                    // 거의 안 움직이는 경우에는 forwardDir 기준으로 약하게 튀어나감
                    Vector3 fwd = forwardDir.sqrMagnitude > 0.0001f
                        ? forwardDir.normalized
                        : transform.forward;

                    mainDir = fwd;
                    mainSpeed = Mathf.Max(0f, cargoForwardSpeed);
                }

                // 약간의 좌우 랜덤 튕김
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

#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = v;
#else
                rb.velocity = v;
#endif

                rb.angularVelocity = UnityEngine.Random.onUnitSphere * cargoAngularVel;
            }
        }

        // 4) 캐리어 안에 숨겨져 있던 원본 제거
        Destroy(wi.gameObject);
    }
}
