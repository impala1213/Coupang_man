// Assets/Scripts/Player/CarrierController.cs
using System;
using System.Collections.Generic;
using System.Text;
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
    public Transform stackPivot;         // parent for all loaded items / slot pivots (lean target)
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

    // ───────────── State ─────────────
    [Header("State")]
    public bool isDroppedWorldCarrier = false;  // true면 플레이어가 메고 있는 게 아니라 바닥에 떨어진 상태

    // ───────────── Data ─────────────
    [Header("Derived (read-only)")]
    public float totalWeight;
    public float stackTotalHeight;

    // 지게에 올라간 실제 WorldItem 리스트
    private readonly List<WorldItem> mounted = new List<WorldItem>();

    public IReadOnlyList<WorldItem> MountedItems => mounted;

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
    /// Optional external knock (e.g., enemy hit). If speed is big enough, spill.
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
    /// Try to mount a world item onto the carrier.
    /// 실제 WorldItem을 지게 위로 옮기고, Carrier mount 모드로 전환.
    /// </summary>
    public bool TryMount(WorldItem world)
    {
        if (!world || !world.definition) return false;
        if (world.definition.isCarrier) return false;

        EnsureStackPivot();

        float currentY = 0f;
        for (int i = 0; i < mounted.Count; i++)
        {
            var w = mounted[i];
            if (w && w.definition)
                currentY += Mathf.Max(0.01f, w.definition.stackSize.y);
        }

        var def = world.definition;
        Vector3 sz = def.stackSize;
        float h = Mathf.Max(0.01f, sz.y);
        float centerY = currentY + h * 0.5f;

        int slotIndex = mounted.Count;
        GameObject slotGO = new GameObject($"CarrierSlot_{slotIndex}");
        Transform slotPivot = slotGO.transform;
        slotPivot.SetParent(stackPivot, false);
        slotPivot.localPosition = new Vector3(0f, centerY, -0.1f);
        slotPivot.localRotation = Quaternion.identity;
        slotPivot.localScale = Vector3.one;

        // 실제 월드 아이템을 지게 슬롯에 장착
        world.EnterCarrierMountMode(this, slotIndex, slotPivot);

        mounted.Add(world);
        _lastChildCount = -1; // bend chain 재빌드
        return true;
    }

    /// <summary>
    /// 인벤토리에서 '지게 아이템'을 떨어뜨릴 때, 짐까지 통째로 한 덩어리로 드롭.
    /// </summary>
    // CarrierController.cs 안에 있는 기존 DropAsBundle() 를 이걸로 교체

    public void DropAsBundle(Vector3 worldPos, Vector3 forward)
    {
        if (mounted.Count == 0)
            return;

        // 앞으로 밀 방향 (플레이어 바라보는 방향)
        Vector3 flatF = new Vector3(forward.x, 0f, forward.z);
        if (flatF.sqrMagnitude < 0.0001f)
            flatF = transform.forward;
        flatF.y = 0f;
        flatF = flatF.sqrMagnitude > 0.0001f ? flatF.normalized : Vector3.forward;

        // 안전하게 복사본 사용
        var list = new List<WorldItem>(mounted);

        foreach (var wi in list)
        {
            if (!wi) continue;

            // 현재 캐리어 위에서의 월드 위치
            Vector3 pos = wi.transform.position;

            // 약간 플레이어 앞/아래로 밀어서 겹침 방지
            pos += flatF * 0.2f + Vector3.down * 0.05f;

            // 살짝 앞으로 튀어나가는 정도의 속도
            Vector3 v = flatF * Mathf.Max(0f, cargoForwardSpeed);

            // 캐리어 장착 상태 해제 + 물리 복구
            wi.OnDropped(pos, v);
        }

        mounted.Clear();

        // stackPivot 아래에 있던 슬롯 피벗들 정리해서
        // 플레이어 등에서 짐 외형 사라지도록
        Transform t = stackPivot ? stackPivot : carrierCargoRoot;
        if (t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                Destroy(t.GetChild(i).gameObject);
            }
        }

        _bendAngles.Clear();
        _bendVels.Clear();
        _lastChildCount = -1;
    }


    /// <summary>
    /// Called by InventorySystem when player drops the carrier item. All mounted cargo spill too.
    /// (통짜 드롭이 아닌 기존 스필용으로 유지)
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

    /// <summary>
    /// 슬롯 인덱스/아이템 이름을 문자열로 만들어 UI에 뿌릴 때 사용.
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

        if (!enableImpactSpill || isDroppedWorldCarrier) return;

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
    /// 실제로 짐을 월드에 떨어뜨리고, 플레이어 속도 방향으로 튀어나가게 하는 부분.
    /// WorldItem 인스턴스를 재사용한다 (Instantiate/Destroy 안 함).
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

        // WorldItem을 Carrier mount 상태에서 월드로 되돌림
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

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
            rb.angularVelocity = UnityEngine.Random.onUnitSphere * cargoAngularVel;
        }
    }
}
