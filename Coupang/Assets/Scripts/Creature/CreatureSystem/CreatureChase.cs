// Assets/Scripts/Creature/CreatureChase.cs
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class CreatureChase : MonoBehaviour
{
    public enum State
    {
        Patrol,
        Chase,        // Only when target is visible
        Investigate,  // Move to last seen position
        Search,       // Wander around last seen position
        Disabled
    }

    [Header("Refs")]
    public CharacterController controller;
    public Animator animator;

    [Header("Movement")]
    public float walkSpeed = 2f;
    public float runSpeed = 6f;
    public float rotationSpeed = 10f;
    public float gravity = -19.62f;

    [Header("Patrol")]
    public float patrolRadius = 25f;
    public float arriveDistance = 1.5f;
    public float patrolIdleMin = 1.5f;
    public float patrolIdleMax = 4f;

    [Header("Detection (FOV + Multi-LOS)")]
    public LayerMask playerMask; // Set to Player layer
    public bool requirePlayerTag = false;
    public string playerTag = "Player";

    public float detectionRadius = 30f;
    public float viewAngle = 120f;

    public float eyeHeight = 1.5f;
    public float forwardYawOffset = 0f;

    public bool requireLineOfSight = true;

    [Header("LOS Sampling")]
    [Range(1, 5)] public int losSampleCount = 3;
    public float fallbackTargetHeight = 1.8f;

    // Top/Mid/Low sampling along target height (0..1)
    public float sample01_A = 0.85f; // head-ish
    public float sample01_B = 0.55f; // chest-ish
    public float sample01_C = 0.25f; // waist-ish

    [Header("Lost Target")]
    public float investigateArriveDistance = 1.5f;

    [Tooltip("After reaching last seen position, keep moving forward for this many seconds to avoid sharp turning.")]
    public float postInvestigateForwardSeconds = 3f;

    public float searchDuration = 6f;
    public float searchRadius = 8f;
    public float searchIdleMin = 0.5f;
    public float searchIdleMax = 1.5f;

    [Header("Animation")]
    public string speedParam = "Speed";
    public float speedDamp = 10f;

    [Header("Debug")]
    public bool debugGizmos = true;
    public bool debugDrawLOS;
    public bool debugLogState;

    public State CurrentState { get; private set; } = State.Patrol;
    public Transform VisibleTarget { get; private set; }
    public Vector3 LastSeenPosition { get; private set; }
    public float LastSeenTime { get; private set; }

    private Vector3 homePosition;
    private Vector3 patrolCenter;

    private Vector3? moveTarget;
    private float idleTimer;
    private float searchTimer;

    private float forwardDriftTimer;
    private Vector3 forwardDriftDir;

    private float verticalVel;
    private Vector3 lastPos;
    private float animSpeed;

    private Vector3 lastMoveDir = Vector3.forward;

    private Transform cachedPlayerForGizmo;

    private static readonly RaycastHit[] s_Hits = new RaycastHit[32];
    private static readonly Collider[] s_Overlap = new Collider[32];

    void Reset()
    {
        int m = LayerMask.GetMask("Player");
        if (m != 0) playerMask = m;
    }

    void Awake()
    {
        if (controller == null) controller = GetComponent<CharacterController>();

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        if (animator != null) animator.applyRootMotion = false;

        homePosition = transform.position;
        patrolCenter = homePosition;
        lastPos = transform.position;

        if (playerMask.value == 0)
        {
            int m = LayerMask.GetMask("Player");
            if (m != 0) playerMask = m;
        }

        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) cachedPlayerForGizmo = p.transform;

        ChooseNewWanderTarget(patrolCenter, patrolRadius);
    }

    void Update()
    {
        if (CurrentState == State.Disabled) return;

        // Update visible target each frame
        VisibleTarget = AcquireVisibleTarget();
        if (VisibleTarget != null)
        {
            LastSeenPosition = VisibleTarget.position;
            LastSeenTime = Time.time;
        }

        switch (CurrentState)
        {
            case State.Patrol:
                UpdatePatrol();
                break;
            case State.Chase:
                UpdateChase();
                break;
            case State.Investigate:
                UpdateInvestigate();
                break;
            case State.Search:
                UpdateSearch();
                break;
        }

        ApplyGravity();
        UpdateAnimatorSpeed();

        lastPos = transform.position;
    }

    public void Disable()
    {
        CurrentState = State.Disabled;
        if (debugLogState) Debug.Log($"[{name}] CreatureChase => Disabled");
    }

    // -----------------------------
    // State Logic
    // -----------------------------

    private void UpdatePatrol()
    {
        if (VisibleTarget != null)
        {
            SetState(State.Chase);
            return;
        }

        if (idleTimer > 0f)
        {
            idleTimer -= Time.deltaTime;
            return;
        }

        if (!moveTarget.HasValue)
            ChooseNewWanderTarget(patrolCenter, patrolRadius);

        MoveTowards(moveTarget.Value, walkSpeed);

        if (HorizontalDistance(transform.position, moveTarget.Value) <= arriveDistance)
        {
            moveTarget = null;
            idleTimer = Random.Range(patrolIdleMin, patrolIdleMax);
        }
    }

    private void UpdateChase()
    {
        // Must not chase invisible targets
        if (VisibleTarget == null)
        {
            moveTarget = LastSeenPosition;
            SetState(State.Investigate);
            return;
        }

        MoveTowards(VisibleTarget.position, runSpeed);
    }

    private void UpdateInvestigate()
    {
        if (VisibleTarget != null)
        {
            SetState(State.Chase);
            return;
        }

        MoveTowards(LastSeenPosition, runSpeed);

        if (HorizontalDistance(transform.position, LastSeenPosition) <= investigateArriveDistance)
        {
            patrolCenter = LastSeenPosition;

            // Start forward drift to avoid abrupt turning
            forwardDriftTimer = Mathf.Max(0f, postInvestigateForwardSeconds);
            forwardDriftDir = lastMoveDir;
            if (forwardDriftDir.sqrMagnitude < 0.0001f) forwardDriftDir = transform.forward;
            forwardDriftDir.y = 0f;
            if (forwardDriftDir.sqrMagnitude < 0.0001f) forwardDriftDir = Vector3.forward;
            forwardDriftDir.Normalize();

            searchTimer = Mathf.Max(0.1f, searchDuration);
            moveTarget = null;
            idleTimer = 0f;
            SetState(State.Search);
        }
    }

    private void UpdateSearch()
    {
        if (VisibleTarget != null)
        {
            SetState(State.Chase);
            return;
        }

        searchTimer -= Time.deltaTime;
        if (searchTimer <= 0f)
        {
            patrolCenter = homePosition;
            moveTarget = null;
            idleTimer = 0f;
            SetState(State.Patrol);
            return;
        }

        // Forward drift phase (3 seconds by default)
        if (forwardDriftTimer > 0f)
        {
            forwardDriftTimer -= Time.deltaTime;
            MoveAlongDirection(forwardDriftDir, walkSpeed);
            return;
        }

        if (idleTimer > 0f)
        {
            idleTimer -= Time.deltaTime;
            return;
        }

        if (!moveTarget.HasValue)
            ChooseNewWanderTarget(patrolCenter, searchRadius);

        MoveTowards(moveTarget.Value, walkSpeed);

        if (HorizontalDistance(transform.position, moveTarget.Value) <= arriveDistance)
        {
            moveTarget = null;
            idleTimer = Random.Range(searchIdleMin, searchIdleMax);
        }
    }

    private void SetState(State s)
    {
        if (CurrentState == s) return;
        CurrentState = s;
        if (debugLogState) Debug.Log($"[{name}] CreatureChase => {s}");
    }

    // -----------------------------
    // Detection
    // -----------------------------

    private Transform AcquireVisibleTarget()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, detectionRadius, s_Overlap, playerMask, QueryTriggerInteraction.Collide);
        if (count <= 0) return null;

        Vector3 fwd = Quaternion.Euler(0f, forwardYawOffset, 0f) * transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 eye = transform.position + Vector3.up * eyeHeight;

        Transform best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            Collider c = s_Overlap[i];
            if (c == null) continue;

            Transform root = c.transform.root;
            if (root == null) continue;

            if (requirePlayerTag && !root.CompareTag(playerTag))
                continue;

            // FOV check in XZ plane
            Vector3 to = root.position - transform.position;
            Vector3 flat = to; flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return root;

            float angle = Vector3.Angle(fwd, flat.normalized);
            if (angle > viewAngle * 0.5f) continue;

            if (requireLineOfSight)
            {
                if (!HasMultiLineOfSight(root, eye))
                    continue;
            }

            float sqr = flat.sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = root;
            }
        }

        return best;
    }

    private bool HasMultiLineOfSight(Transform targetRoot, Vector3 eye)
    {
        Bounds b;
        bool hasBounds = TryGetTargetBounds(targetRoot, out b);

        float h = hasBounds ? Mathf.Max(0.3f, b.size.y) : Mathf.Max(0.3f, fallbackTargetHeight);
        Vector3 center = hasBounds ? b.center : targetRoot.position + Vector3.up * (h * 0.5f);

        float[] t01 = GetSample01Array();

        for (int i = 0; i < losSampleCount; i++)
        {
            float s = t01[Mathf.Min(i, t01.Length - 1)];
            Vector3 targetPoint = center + Vector3.up * ((s - 0.5f) * h);

            if (HasSingleLineOfSight(targetRoot, eye, targetPoint))
                return true;
        }

        return false;
    }

    private float[] GetSample01Array()
    {
        if (losSampleCount <= 1) return new float[] { sample01_B };
        if (losSampleCount == 2) return new float[] { sample01_A, sample01_C };
        if (losSampleCount == 3) return new float[] { sample01_A, sample01_B, sample01_C };
        if (losSampleCount == 4) return new float[] { 0.9f, 0.7f, 0.5f, 0.3f };
        return new float[] { 0.92f, 0.78f, 0.6f, 0.42f, 0.25f };
    }

    // Visible if the nearest solid hit belongs to the target root.
    private bool HasSingleLineOfSight(Transform targetRoot, Vector3 origin, Vector3 targetPoint)
    {
        Vector3 dir = targetPoint - origin;
        float dist = dir.magnitude;
        if (dist <= 0.001f) return true;
        dir /= dist;

        int hitCount = Physics.RaycastNonAlloc(origin, dir, s_Hits, dist, ~0, QueryTriggerInteraction.Ignore);
        if (hitCount <= 0)
        {
            if (debugDrawLOS) Debug.DrawLine(origin, targetPoint, Color.green);
            return true;
        }

        float bestDist = float.MaxValue;
        Collider best = null;

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = s_Hits[i].collider;
            if (col == null) continue;

            Transform t = col.transform;

            if (t == transform || t.IsChildOf(transform)) continue;
            if (col.isTrigger) continue;

            float d = s_Hits[i].distance;
            if (d < bestDist)
            {
                bestDist = d;
                best = col;
            }
        }

        if (best == null)
        {
            if (debugDrawLOS) Debug.DrawLine(origin, targetPoint, Color.green);
            return true;
        }

        bool visible = (best.transform.root == targetRoot);
        if (debugDrawLOS) Debug.DrawLine(origin, targetPoint, visible ? Color.green : Color.red);
        return visible;
    }

    private bool TryGetTargetBounds(Transform targetRoot, out Bounds bounds)
    {
        CharacterController cc = targetRoot.GetComponent<CharacterController>();
        if (cc != null)
        {
            Vector3 worldCenter = targetRoot.TransformPoint(cc.center);
            bounds = new Bounds(worldCenter, new Vector3(cc.radius * 2f, cc.height, cc.radius * 2f));
            return true;
        }

        Collider col = targetRoot.GetComponentInChildren<Collider>();
        if (col != null)
        {
            bounds = col.bounds;
            return true;
        }

        bounds = default;
        return false;
    }

    // -----------------------------
    // Movement / Gravity / Anim
    // -----------------------------

    private void MoveTowards(Vector3 target, float speed)
    {
        if (controller == null || !controller.enabled) return;

        Vector3 dir = target - transform.position;
        dir.y = 0f;

        float mag = dir.magnitude;
        if (mag < 0.001f) return;

        Vector3 dn = dir / mag;
        lastMoveDir = dn;

        Quaternion targetRot = Quaternion.LookRotation(dn, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);

        controller.Move(dn * speed * Time.deltaTime);
    }

    private void MoveAlongDirection(Vector3 direction, float speed)
    {
        if (controller == null || !controller.enabled) return;

        Vector3 dn = direction;
        dn.y = 0f;
        if (dn.sqrMagnitude < 0.0001f) return;
        dn.Normalize();

        lastMoveDir = dn;

        Quaternion targetRot = Quaternion.LookRotation(dn, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);

        controller.Move(dn * speed * Time.deltaTime);
    }

    private void ApplyGravity()
    {
        if (controller == null || !controller.enabled) return;

        if (controller.isGrounded && verticalVel < 0f)
            verticalVel = -2f;

        verticalVel += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVel * Time.deltaTime);
    }

    private void UpdateAnimatorSpeed()
    {
        if (animator == null || string.IsNullOrEmpty(speedParam)) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - lastPos;
        delta.y = 0f;

        float spd = delta.magnitude / dt;
        if (spd < 0.2f) spd = 0f;

        animSpeed = Mathf.Lerp(animSpeed, spd, dt * speedDamp);
        animator.SetFloat(speedParam, animSpeed);
    }

    private void ChooseNewWanderTarget(Vector3 center, float radius)
    {
        Vector2 r = Random.insideUnitCircle * radius;
        moveTarget = center + new Vector3(r.x, 0f, r.y);
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // -----------------------------
    // Gizmos
    // -----------------------------

    void OnDrawGizmosSelected()
    {
        if (!debugGizmos) return;

        Color c = Color.yellow;

        if (Application.isPlaying)
        {
            if (cachedPlayerForGizmo == null)
            {
                GameObject p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) cachedPlayerForGizmo = p.transform;
            }

            if (cachedPlayerForGizmo != null)
            {
                float d = HorizontalDistance(transform.position, cachedPlayerForGizmo.position);

                // In range but not visible => red
                if (d <= detectionRadius)
                    c = (VisibleTarget != null) ? Color.green : Color.red;
                else
                    c = new Color(1f, 1f, 0f, 1f);
            }
        }

        Gizmos.color = c;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        if (Application.isPlaying)
        {
            // Last seen point
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(LastSeenPosition + Vector3.up * 0.1f, 0.25f);

            // Search area
            if (CurrentState == State.Investigate || CurrentState == State.Search)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
                Gizmos.DrawWireSphere(LastSeenPosition, searchRadius);

                Gizmos.color = Color.white;
                Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, LastSeenPosition + Vector3.up * 0.2f);
            }
        }
    }
}
