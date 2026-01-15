using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Health))]
public class ScorpionAI : MonoBehaviour
{
    public enum State
    {
        Burrowed,
        Appearing,
        Attacking,
        Shaking,
        Recover,
        Dead
    }

    [Header("Detection")]
    public float detectionRadius = 10f;
    public LayerMask playerMask = ~0;

    [Header("Grab (Cylinder Volume)")]
    [Tooltip("Circumference of the grab cylinder. Radius = C / (2π).")]
    public float grabCircumference = 4.85f;

    [Tooltip("Height of the grab cylinder (Y size). Lower this to avoid grabbing too high/low.")]
    public float grabHeight = 1.6f;

    [Tooltip("Y offset of the grab cylinder center from transform.position.")]
    public float grabCenterYOffset = 0.9f;

    [Tooltip("Delay from Attack start to actual grab attempt (seconds).")]
    public float grabDelay = 1.2f;

    [Tooltip("Where the grabbed player will be held.")]
    public Transform grabSocket;

    [Header("Timings")]
    public float appearDuration = 1.0f;
    public float recoverDuration = 2.0f;
    public float rearmCooldownAfterRecover = 2.5f;

    [Header("Shake Damage")]
    public int shakeDamagePerTick = 6;
    public float shakeTickInterval = 0.45f;

    [Header("Tail Weak Point")]
    public ScorpionTail scorpionTail;
    public bool tailVulnerableOnlyWhileShaking = true;
    public bool resetTailHpOnRecover = true;

    [Header("Gravity")]
    public float gravity = -19.62f;

    [Header("Burrow Visual (Optional)")]
    public GameObject[] hideWhileBurrowed;

    [Header("Animation")]
    public Animator animator;
    public string appearTriggerParam = "Appear";
    public string attackTriggerParam = "Attack";
    public string isShakingBoolParam = "IsShaking";
    public string isDeadBoolParam = "IsDead";

    [Header("Death Helper (Object)")]
    public EnemyDeath death = new EnemyDeath();

    [Header("Debug Gizmos (Grab Cylinder)")]
    public bool debugGizmos = true;
    public bool showGrabGizmoAlways = true;
    public bool showGrabGizmoWhenSelected = true;
    public Color grabWireColor = new Color(1f, 0.2f, 0.2f, 1f);
    [Range(8, 64)] public int gizmoSegments = 28;

    [Header("Debug")]
    public State currentState = State.Burrowed;
    public bool debugLogs;

    private CharacterController cc;
    private Health health;

    private float verticalVel;

    private Transform grabbedPlayer;
    private Health grabbedPlayerHealth;
    private CharacterController grabbedPlayerCC;
    private Rigidbody grabbedPlayerRB;
    private bool grabbedPlayerRBWasKinematic;

    private float stateTimer;
    private float shakeTickTimer;

    private bool pendingGrab;
    private float grabTimer;
    private float rearmTimer;

    private float GrabRadius
    {
        get
        {
            float c = Mathf.Max(0.01f, grabCircumference);
            return Mathf.Max(0.01f, c / (2f * Mathf.PI));
        }
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        health = GetComponent<Health>();

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        // Bind EnemyDeath
        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        Rigidbody rb = GetComponent<Rigidbody>();
        Collider[] cols = null;

        death.gravity = gravity;
        death.animator = animator;
        death.isDeadParam = isDeadBoolParam;
        death.speedParam = "";
        death.Bind(transform, health, cc, animator, agent, rb, cols);

        death.onDeadFirstTime = () =>
        {
            currentState = State.Dead;
            ReleasePlayer(force: true);
            SetBurrowVisual(false);
            SetShakingAnim(false);

            if (debugLogs) Debug.Log("[ScorpionAI] Dead.");
        };

        if (scorpionTail != null)
        {
            scorpionTail.OnBroken += OnTailBroken;
            scorpionTail.SetVulnerable(false);
        }

        EnterBurrowed();
    }

    void OnDestroy()
    {
        if (scorpionTail != null)
            scorpionTail.OnBroken -= OnTailBroken;
    }

    void Update()
    {
        if (death.Tick())
        {
            currentState = State.Dead;
            ReleasePlayer(force: true);
            return;
        }

        ApplyGravity();

        if (rearmTimer > 0f)
            rearmTimer -= Time.deltaTime;

        switch (currentState)
        {
            case State.Burrowed:
                UpdateBurrowed();
                break;
            case State.Appearing:
                UpdateAppearing();
                break;
            case State.Attacking:
                UpdateAttacking();
                break;
            case State.Shaking:
                UpdateShaking();
                break;
            case State.Recover:
                UpdateRecover();
                break;
        }
    }

    void LateUpdate()
    {
        if (death.IsDeadNow())
            death.LateTick();

        if (currentState == State.Shaking)
            KeepGrabbedPlayerOnSocket();
    }

    private void UpdateBurrowed()
    {
        if (rearmTimer > 0f) return;

        Transform target = FindClosestPlayerInRadius(detectionRadius);
        if (target == null) return;

        EnterAppearing();
    }

    private void UpdateAppearing()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
            EnterAttacking();
    }

    private void UpdateAttacking()
    {
        if (pendingGrab)
        {
            grabTimer -= Time.deltaTime;
            if (grabTimer <= 0f)
            {
                pendingGrab = false;
                TryGrabClosestPlayerNow();
            }
        }

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f && currentState == State.Attacking)
            EnterRecover();
    }

    private void UpdateShaking()
    {
        if (grabbedPlayer == null || grabbedPlayerHealth == null)
        {
            ReleasePlayer(force: true);
            EnterRecover();
            return;
        }

        shakeTickTimer -= Time.deltaTime;
        if (shakeTickTimer <= 0f)
        {
            shakeTickTimer = Mathf.Max(0.05f, shakeTickInterval);
            grabbedPlayerHealth.ApplyDamage(shakeDamagePerTick);
        }

        // Your ScorpionTail uses 'vulnerable' field (lowercase)
        if (scorpionTail != null && tailVulnerableOnlyWhileShaking && !scorpionTail.vulnerable)
            scorpionTail.SetVulnerable(true);
    }

    private void UpdateRecover()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            EnterBurrowed();
            rearmTimer = Mathf.Max(0f, rearmCooldownAfterRecover);
        }
    }

    private void EnterBurrowed()
    {
        currentState = State.Burrowed;

        ReleasePlayer(force: true);
        SetShakingAnim(false);

        if (scorpionTail != null)
        {
            scorpionTail.SetVulnerable(false);
            if (resetTailHpOnRecover)
                scorpionTail.ResetToFull();
        }

        SetBurrowVisual(true);

        if (debugLogs) Debug.Log("[ScorpionAI] EnterBurrowed");
    }

    private void EnterAppearing()
    {
        currentState = State.Appearing;

        SetBurrowVisual(false);

        stateTimer = Mathf.Max(0.01f, appearDuration);

        if (animator != null && !string.IsNullOrEmpty(appearTriggerParam))
        {
            animator.ResetTrigger(appearTriggerParam);
            animator.SetTrigger(appearTriggerParam);
        }

        if (debugLogs) Debug.Log("[ScorpionAI] EnterAppearing");
    }

    private void EnterAttacking()
    {
        currentState = State.Attacking;

        if (animator != null && !string.IsNullOrEmpty(attackTriggerParam))
        {
            animator.ResetTrigger(attackTriggerParam);
            animator.SetTrigger(attackTriggerParam);
        }

        pendingGrab = true;
        grabTimer = Mathf.Max(0.01f, grabDelay);

        // Safety timeout
        stateTimer = Mathf.Max(0.25f, grabDelay + 0.8f);

        if (debugLogs) Debug.Log("[ScorpionAI] EnterAttacking");
    }

    private void EnterShaking()
    {
        currentState = State.Shaking;

        shakeTickTimer = Mathf.Max(0.05f, shakeTickInterval);

        if (scorpionTail != null && tailVulnerableOnlyWhileShaking)
            scorpionTail.SetVulnerable(true);

        SetShakingAnim(true);

        if (debugLogs) Debug.Log("[ScorpionAI] EnterShaking");
    }

    private void EnterRecover()
    {
        currentState = State.Recover;

        pendingGrab = false;

        if (scorpionTail != null && tailVulnerableOnlyWhileShaking)
            scorpionTail.SetVulnerable(false);

        SetShakingAnim(false);

        ReleasePlayer(force: false);

        stateTimer = Mathf.Max(0.01f, recoverDuration);

        if (debugLogs) Debug.Log("[ScorpionAI] EnterRecover");
    }

    private void TryGrabClosestPlayerNow()
    {
        if (grabbedPlayer != null) return;

        Transform target = FindClosestPlayerInGrabCylinder();
        if (target == null)
        {
            if (debugLogs) Debug.Log("[ScorpionAI] Grab failed (no player in cylinder).");
            EnterRecover();
            return;
        }

        GrabPlayer(target);
        EnterShaking();
    }

    private void GrabPlayer(Transform target)
    {
        grabbedPlayer = target;
        grabbedPlayerHealth = target.GetComponent<Health>();
        grabbedPlayerCC = target.GetComponent<CharacterController>();
        grabbedPlayerRB = target.GetComponent<Rigidbody>();

        if (grabbedPlayerRB != null)
        {
            grabbedPlayerRBWasKinematic = grabbedPlayerRB.isKinematic;
            grabbedPlayerRB.linearVelocity = Vector3.zero;
            grabbedPlayerRB.angularVelocity = Vector3.zero;
            grabbedPlayerRB.isKinematic = true;
        }

        target.gameObject.SendMessage("OnGrabbed", true, SendMessageOptions.DontRequireReceiver);

        KeepGrabbedPlayerOnSocket();

        if (debugLogs) Debug.Log($"[ScorpionAI] Grabbed {target.name}");
    }

    private void ReleasePlayer(bool force)
    {
        if (grabbedPlayer == null) return;

        grabbedPlayer.gameObject.SendMessage("OnGrabbed", false, SendMessageOptions.DontRequireReceiver);

        if (grabbedPlayerRB != null)
            grabbedPlayerRB.isKinematic = grabbedPlayerRBWasKinematic;

        grabbedPlayer = null;
        grabbedPlayerHealth = null;
        grabbedPlayerCC = null;
        grabbedPlayerRB = null;
    }

    private void KeepGrabbedPlayerOnSocket()
    {
        if (grabbedPlayer == null || grabSocket == null) return;

        if (grabbedPlayerCC != null)
        {
            bool was = grabbedPlayerCC.enabled;
            grabbedPlayerCC.enabled = false;
            grabbedPlayer.SetPositionAndRotation(grabSocket.position, grabSocket.rotation);
            grabbedPlayerCC.enabled = was;
        }
        else
        {
            grabbedPlayer.SetPositionAndRotation(grabSocket.position, grabSocket.rotation);
        }
    }

    private Transform FindClosestPlayerInRadius(float radius)
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, radius, playerMask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            if (!t.CompareTag("Player")) continue;

            Vector3 d = t.position - transform.position;
            d.y = 0f;
            float sqr = d.sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = t;
            }
        }

        return best;
    }

    // Cylinder-like volume using OverlapCapsule (better than sphere for limiting Y)
    private Transform FindClosestPlayerInGrabCylinder()
    {
        float r = GrabRadius;
        float h = Mathf.Max(0.01f, grabHeight);

        Vector3 center = transform.position + Vector3.up * grabCenterYOffset;

        // Build capsule endpoints so it approximates a cylinder with rounded ends
        float halfSegment = Mathf.Max(0f, (h * 0.5f) - r);
        Vector3 bottom = center - Vector3.up * halfSegment;
        Vector3 top = center + Vector3.up * halfSegment;

        Collider[] hits = Physics.OverlapCapsule(bottom, top, r, playerMask, QueryTriggerInteraction.Ignore);

        Transform best = null;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < hits.Length; i++)
        {
            Transform t = hits[i].transform;
            if (!t.CompareTag("Player")) continue;

            // Prefer closest in XZ plane
            Vector3 d = t.position - center;
            d.y = 0f;
            float sqr = d.sqrMagnitude;

            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = t;
            }
        }

        return best;
    }

    private void OnTailBroken(ScorpionTail tail)
    {
        if (debugLogs) Debug.Log("[ScorpionAI] Tail broken => rescue!");

        if (scorpionTail != null && tailVulnerableOnlyWhileShaking)
            scorpionTail.SetVulnerable(false);

        SetShakingAnim(false);
        ReleasePlayer(force: false);

        EnterRecover();
    }

    private void ApplyGravity()
    {
        if (cc == null || !cc.enabled) return;

        if (cc.isGrounded && verticalVel < 0f)
            verticalVel = -2f;

        verticalVel += gravity * Time.deltaTime;
        cc.Move(Vector3.up * verticalVel * Time.deltaTime);
    }

    private void SetBurrowVisual(bool burrowed)
    {
        if (hideWhileBurrowed == null) return;

        for (int i = 0; i < hideWhileBurrowed.Length; i++)
        {
            if (hideWhileBurrowed[i] != null)
                hideWhileBurrowed[i].SetActive(!burrowed);
        }
    }

    private void SetShakingAnim(bool on)
    {
        if (animator == null) return;
        if (string.IsNullOrEmpty(isShakingBoolParam)) return;

        animator.SetBool(isShakingBoolParam, on);
    }

    // ----------------------------
    // Gizmos (Cylinder)
    // ----------------------------

    void OnDrawGizmos()
    {
        if (!debugGizmos) return;
        if (!showGrabGizmoAlways) return;
        DrawGrabCylinderGizmo();
    }

    void OnDrawGizmosSelected()
    {
        if (!debugGizmos) return;
        if (!showGrabGizmoWhenSelected) return;
        DrawGrabCylinderGizmo();
    }

    private void DrawGrabCylinderGizmo()
    {
        float r = Mathf.Max(0.01f, grabCircumference / (2f * Mathf.PI));
        float h = Mathf.Max(0.01f, grabHeight);

        Vector3 center = transform.position + Vector3.up * grabCenterYOffset;
        Vector3 up = Vector3.up;

        Vector3 top = center + up * (h * 0.5f);
        Vector3 bottom = center - up * (h * 0.5f);

        Gizmos.color = grabWireColor;

        int seg = Mathf.Clamp(gizmoSegments, 8, 64);
        float step = 360f / seg;

        Vector3 prevTop = top + new Vector3(r, 0f, 0f);
        Vector3 prevBottom = bottom + new Vector3(r, 0f, 0f);

        for (int i = 1; i <= seg; i++)
        {
            float a = step * i * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);

            Vector3 curTop = top + offset;
            Vector3 curBottom = bottom + offset;

            Gizmos.DrawLine(prevTop, curTop);
            Gizmos.DrawLine(prevBottom, curBottom);

            // vertical edges (a few are enough, but drawing all looks fine for debug)
            Gizmos.DrawLine(curBottom, curTop);

            prevTop = curTop;
            prevBottom = curBottom;
        }
    }

    // ----------------------------
    // Animation Events (Optional)
    // ----------------------------

    public void AnimEvent_AppearFinished()
    {
        if (currentState != State.Appearing) return;
        EnterAttacking();
    }

    public void AnimEvent_AttemptGrabNow()
    {
        if (currentState != State.Attacking) return;
        pendingGrab = false;
        TryGrabClosestPlayerNow();
    }
}
