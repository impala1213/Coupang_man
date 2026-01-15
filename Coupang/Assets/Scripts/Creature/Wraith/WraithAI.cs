// Assets/Scripts/Creature/WraithAI.cs
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Health))]
public class WraithAI : MonoBehaviour
{
    private enum State { Patrol, Idle, Roar, Chase, GrabAnim, Attack, Dead }

    [Header("Refs")]
    public Animator animator;
    public CharacterController controller;
    public Health health;

    [Header("Target Rules")]
    public int minKillsToTarget = 3;
    public bool allowProvokedTargets = true;

    [Header("Detection")]
    public float detectionRadius = 25f;
    [Range(1f, 360f)] public float viewAngle = 140f;
    public bool requireLineOfSight = true;
    public LayerMask occlusionMask = ~0;
    public float eyeHeight = 1.6f;

    [Header("Movement")]
    public float walkSpeed = 2f;
    public float runSpeed = 7f;
    public float turnSpeed = 10f;
    public float gravity = -19.62f;

    [Header("Patrol")]
    public float wanderRadius = 10f;
    public float wanderPointTolerance = 1.2f;
    public float idleTimeMin = 0.8f;
    public float idleTimeMax = 2.0f;


    [Header("Idle After Action")]
    [Tooltip("How long the wraith stays in Idle after a grab (non-target) or after an attack completes.")]
    public float postActionIdleDuration = 5f;

    [Tooltip("Animator state name for idle. If empty, no forced jump is performed.")]
    public string idleStateName = "Wraith_idle";

    private float postActionIdleTimer = 0f;

    [Header("Roar -> Chase")]
    public string roarTriggerParam = "Roar";
    public float roarDuration = 1.0f;

    [Header("Grab -> Attack (REBUILT)")]
    [Tooltip("When chase target gets closer than this, the wraith starts grab.")]
    public float grabStartRange = 2.8f;

    [Tooltip("Attack bool parameter name (true = play Attack animation).")]
    public string attackBoolParam = "Attack";

    [Tooltip("Grab trigger parameter name.")]
    public string grabTriggerParam = "Grab";

    [Tooltip("Animator state tag or name used to detect the end of Grab.")]
    public string grabStateTag = "Grab";
    public string grabStateName = "Grab";

    [Tooltip("Animator state tag or name used to detect the end of Attack.")]
    public string attackStateTag = "Attack";
    public string attackStateName = "Attack";

    [Tooltip("If Attack never starts within this time, abort and return to movement.")]
    public float attackStartTimeout = 1.0f;

    [Header("Post-Attack (after Attack animation ends)")]
    [Tooltip("Camera zoom-in duration after Attack animation ends.")]
    public float postAttackZoomDuration = 5.0f;

    [Tooltip("Delay after Attack animation ends to apply damage (seconds).")]
    public float postAttackDamageDelay = 5.0f;

    public int executeDamage = 999999;

    [Header("Animation")]
    public string speedParam = "Speed";
    public float speedDamp = 12f;
    public string isDeadParam = "IsDead";

    [Header("Animator State Jump")]
    [Tooltip("Animator state to force-jump to when returning to walking/movement.")]
    public string walkStateName = "Wraith_walk";

    [Header("Victim Hold / Face")]
    public Transform victimHoldPoint;
    public Transform wraithFacePoint;

    [Header("Scan")]
    public float scanInterval = 0.4f;

    [Header("Debug")]
    public bool debugLogs;

    private State state = State.Patrol;

    private readonly HashSet<Transform> provokedTargets = new HashSet<Transform>();

    // Target we are chasing (kept from existing logic)
    private Transform chaseTarget;

    // Victim we currently grabbed/captured (can be different from chaseTarget)
    private Transform victimTarget;

    private Vector3 homePos;
    private Vector3? patrolTarget;
    private float idleTimer;

    private float roarTimer;
    private float verticalVelocity;

    private Vector3 lastPos;
    private float smoothedSpeed;

    private float scanTimer;

    // Grab/Attack flow (rebuilt)
    private bool pendingAttack;
    private bool grabStateSeen;

    private bool attackStateSeen;
    private bool attackAnimFinished;
    private float attackStartTimer;

    private float postAttackTimer;

    private WraithVictimLock victimLock;

    // Rotation lock (prevents spinning feedback)
    private bool lockFacingActive;
    private Quaternion lockedFacingRotation;

    void Awake()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (health == null) health = GetComponent<Health>();

        if (animator == null)
        {
            animator = GetComponent<Animator>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        if (animator != null) animator.applyRootMotion = false;

        // If CreatureChase exists, disable it (wraith uses this AI).
        var chase = GetComponent<CreatureChase>();
        if (chase != null && chase.enabled) chase.enabled = false;

        homePos = transform.position;
        lastPos = transform.position;

        health.OnDeath += OnDeath;
    }

    void Update()
    {
        if (health.IsDead())
            state = State.Dead;

        // Victim died while captured -> hard abort.
        if (victimTarget != null)
        {
            var vh = victimTarget.GetComponentInChildren<Health>();
            if (vh != null && vh.IsDead())
            {
                if (debugLogs) Debug.Log("[WraithAI] Victim died -> abort capture.");
                victimTarget = null;
                SetAttackParam(false);
                pendingAttack = false;
                ReleaseVictimAndCamera();
                lockFacingActive = false;
                state = State.Patrol;
            }
        }

        // Chase target died -> clear chase target only (do not touch victim unless it is the same).
        if (chaseTarget != null)
        {
            var th = chaseTarget.GetComponentInChildren<Health>();
            if (th != null && th.IsDead())
                chaseTarget = null;
        }

        // Acquire only when we are not in grab/attack.
        if (state != State.GrabAnim && state != State.Attack && state != State.Idle && state != State.Dead)
        {
            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = Mathf.Max(0.05f, scanInterval);
                TryAcquireTargetIfNeeded();
            }
        }

        switch (state)
        {
            case State.Patrol: TickPatrol(); break;
            case State.Idle: TickIdle(); break;
            case State.Roar: TickRoar(); break;
            case State.Chase: TickChase(); break;
            case State.GrabAnim: TickGrabAnim(); break;
            case State.Attack: TickAttack(); break;
            case State.Dead: TickDead(); break;
        }

        ApplyGravity();
        UpdateAnimatorSpeed();
        lastPos = transform.position;
    }

    public void NotifyDamagedBy(Transform attacker)
    {
        if (!allowProvokedTargets) return;
        if (state == State.Dead) return;
        if (attacker == null) return;

        Transform root = attacker.root;
        provokedTargets.Add(root);

        if (chaseTarget == null && state != State.GrabAnim && state != State.Attack)
        {
            chaseTarget = root;
            EnterRoar();
        }
    }

    private void TryAcquireTargetIfNeeded()
    {
        if (state == State.Idle) return;
        if (chaseTarget != null) return;

        Transform best = FindBestValidDetectableTargetRoot();
        if (best != null)
        {
            chaseTarget = best;
            EnterRoar();
        }
    }

    // Returns a PLAYER ROOT (mark/target rules apply)
    private Transform FindBestValidDetectableTargetRoot()
    {
        Transform best = null;
        float bestDist = float.MaxValue;

        var marks = FindObjectsOfType<WraithVengeanceMark>();
        for (int i = 0; i < marks.Length; i++)
        {
            var m = marks[i];
            if (m == null) continue;
            if (m.creatureKills < minKillsToTarget) continue;

            Transform root = m.transform.root;
            if (!IsDetectable(root)) continue;

            float d = HorizontalDistance(transform.position, root.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = root;
            }
        }

        if (allowProvokedTargets)
        {
            foreach (var t in provokedTargets)
            {
                if (t == null) continue;
                if (!IsDetectable(t)) continue;

                float d = HorizontalDistance(transform.position, t.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = t;
                }
            }
        }

        return best;
    }

    // "Chase validity" (kept from existing logic)
    private bool IsTargetValid(Transform targetRoot)
    {
        if (targetRoot == null) return false;

        if (allowProvokedTargets && provokedTargets.Contains(targetRoot))
            return true;

        var mark = targetRoot.GetComponentInChildren<WraithVengeanceMark>();
        if (mark == null) return false;

        return mark.creatureKills >= minKillsToTarget;
    }

    // "Execute target" (ONLY kill-count target; provoked does not count)
    private bool IsExecuteTarget(Transform targetRoot)
    {
        if (targetRoot == null) return false;
        var mark = targetRoot.GetComponentInChildren<WraithVengeanceMark>();
        return mark != null && mark.creatureKills >= minKillsToTarget;
    }

    private bool IsDetectable(Transform targetRoot)
    {
        if (!IsTargetValid(targetRoot)) return false;

        Vector3 to = targetRoot.position - transform.position;
        float dist = new Vector3(to.x, 0f, to.z).magnitude;
        if (dist > detectionRadius) return false;

        to.y = 0f;
        if (to.sqrMagnitude > 0.0001f)
        {
            float ang = Vector3.Angle(transform.forward, to.normalized);
            if (ang > viewAngle * 0.5f) return false;
        }

        if (!requireLineOfSight) return true;

        Vector3 origin = transform.position + Vector3.up * eyeHeight;
        Vector3 p0 = targetRoot.position + Vector3.up * 0.6f;
        Vector3 p1 = targetRoot.position + Vector3.up * 1.2f;
        Vector3 p2 = targetRoot.position + Vector3.up * 1.8f;

        return HasLine(origin, p0, targetRoot) || HasLine(origin, p1, targetRoot) || HasLine(origin, p2, targetRoot);
    }

    private bool HasLine(Vector3 origin, Vector3 targetPoint, Transform targetRoot)
    {
        Vector3 dir = targetPoint - origin;
        float dist = dir.magnitude;
        if (dist <= 0.001f) return true;
        dir /= dist;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, occlusionMask, QueryTriggerInteraction.Ignore))
            return hit.collider != null && hit.collider.transform.root == targetRoot;

        return true;
    }

    private void TickPatrol()
    {
        ReleaseVictimAndCamera();
        lockFacingActive = false;

        if (idleTimer <= 0f && !patrolTarget.HasValue)
            idleTimer = Random.Range(idleTimeMin, idleTimeMax);

        if (idleTimer > 0f)
        {
            idleTimer -= Time.deltaTime;
            return;
        }

        if (!patrolTarget.HasValue)
        {
            Vector2 r = Random.insideUnitCircle * wanderRadius;
            patrolTarget = homePos + new Vector3(r.x, 0f, r.y);
        }

        MoveTowards(patrolTarget.Value, walkSpeed);

        if (HorizontalDistance(transform.position, patrolTarget.Value) <= wanderPointTolerance)
        {
            patrolTarget = null;
            idleTimer = Random.Range(idleTimeMin, idleTimeMax);
        }
    }

    private void EnterRoar()
    {
        if (state == State.Dead) return;
        if (chaseTarget == null) return;
        if (!IsTargetValid(chaseTarget))
        {
            chaseTarget = null;
            state = State.Patrol;
            return;
        }

        if (state == State.GrabAnim || state == State.Attack) return;

        state = State.Roar;
        roarTimer = Mathf.Max(0.01f, roarDuration);

        FaceTowards(chaseTarget.position);

        if (animator != null && !string.IsNullOrEmpty(roarTriggerParam))
        {
            animator.speed = 1f;
            animator.ResetTrigger(roarTriggerParam);
            animator.SetTrigger(roarTriggerParam);
        }
    }

    private void TickRoar()
    {
        if (chaseTarget == null || !IsTargetValid(chaseTarget))
        {
            chaseTarget = null;
            state = State.Patrol;
            return;
        }

        FaceTowards(chaseTarget.position);

        roarTimer -= Time.deltaTime;
        if (roarTimer <= 0f)
            state = State.Chase;
    }

    private void TickChase()
    {
        ReleaseVictimAndCamera();
        lockFacingActive = false;

        if (chaseTarget == null || !IsTargetValid(chaseTarget))
        {
            chaseTarget = null;
            state = State.Patrol;
            return;
        }

        if (!IsDetectable(chaseTarget))
        {
            chaseTarget = null;
            state = State.Patrol;
            return;
        }

        float d = HorizontalDistance(transform.position, chaseTarget.position);
        if (d <= grabStartRange)
        {
            StartGrab();
            return;
        }

        MoveTowards(chaseTarget.position, runSpeed);
    }

    private void StartGrab()
    {
        if (chaseTarget == null)
        {
            state = State.Patrol;
            return;
        }

        // Lock facing once (prevents spin feedback)
        FaceTowards(chaseTarget.position);
        lockedFacingRotation = transform.rotation;
        lockFacingActive = true;

        // Pick the closest player within range (can be someone else, enabling "wrong victim" grab gimmick).
        victimTarget = FindClosestPlayerRootInRange(grabStartRange);
        if (victimTarget == null)
            victimTarget = chaseTarget;

        pendingAttack = IsExecuteTarget(victimTarget);

        // Set attack param immediately (bool-based). Attack animation should only begin AFTER grab ends via animator transitions.
        SetAttackParam(pendingAttack);

        CaptureVictimAndStartCamera(victimTarget);

        if (animator != null && !string.IsNullOrEmpty(grabTriggerParam))
        {
            animator.speed = 1f;
            animator.ResetTrigger(grabTriggerParam);
            animator.SetTrigger(grabTriggerParam);
        }

        // Ensure no zoom until Attack animation ends.
        if (victimLock != null) victimLock.SetCinematicZoom01(0f);

        grabStateSeen = false;
        state = State.GrabAnim;
    }

    private void TickGrabAnim()
    {
        if (victimTarget == null)
        {
            AbortGrabToMovement();
            return;
        }

        if (lockFacingActive) transform.rotation = lockedFacingRotation;

        // If we are already in Attack state (due to animator transition setup), treat grab as ended.
        if (animator != null && IsInAttackState(animator.GetCurrentAnimatorStateInfo(0)))
        {
            if (pendingAttack) EnterAttack();
            else AbortGrabToMovement();
            return;
        }

        bool grabEnded = false;

        if (animator != null)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (IsInGrabState(info))
            {
                grabStateSeen = true;
                if (info.normalizedTime >= 0.98f) grabEnded = true;
            }
        }
        else
        {
            // No animator -> treat as instantly ended.
            grabEnded = true;
        }

        if (!grabEnded) return;

        // Grab finished -> if victim is an execute target, go to Attack; otherwise release and resume movement.
        if (pendingAttack)
        {
            EnterAttack();
        }
        else
        {
            AbortGrabToMovement();
        }
    }

    private void EnterAttack()
    {
        if (victimTarget == null)
        {
            AbortGrabToMovement();
            return;
        }

        // Reset state flags for Attack.
        attackStateSeen = false;
        attackAnimFinished = false;
        attackStartTimer = 0f;
        postAttackTimer = 0f;

        // Keep attack bool true while we wait for Attack animation to play.
        SetAttackParam(true);

        // No zoom during Attack animation.
        if (victimLock != null) victimLock.SetCinematicZoom01(0f);

        state = State.Attack;
    }

    private void TickAttack()
    {
        if (victimTarget == null)
        {
            EndAttackToMovement(abortToChase: true);
            return;
        }

        if (lockFacingActive) transform.rotation = lockedFacingRotation;

        // Still captured: no movement.
        if (victimLock != null && !attackAnimFinished)
            victimLock.SetCinematicZoom01(0f);

        if (animator == null)
        {
            // If no animator, skip directly to post-attack phase.
            attackAnimFinished = true;
            SetAttackParam(false);
        }

        // 1) Wait until Attack animation actually starts. If it never starts, abort.
        if (!attackStateSeen && animator != null)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (IsInAttackState(info))
            {
                attackStateSeen = true;
            }
            else
            {
                attackStartTimer += Time.deltaTime;
                if (attackStartTimer >= Mathf.Max(0.05f, attackStartTimeout))
                {
                    if (debugLogs) Debug.LogWarning("[WraithAI] Attack did not start -> abort to movement.");
                    EndAttackToMovement(abortToChase: true);
                }
                return;
            }
        }

        // 2) Detect Attack animation end.
        if (!attackAnimFinished && animator != null)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (IsInAttackState(info) && info.normalizedTime >= 0.98f)
            {
                attackAnimFinished = true;
                postAttackTimer = 0f;

                // Allow animator to transition out after this (if needed).
                SetAttackParam(false);

                // Start zoom after attack animation ends (zoom driven by postAttackTimer below).
                if (victimLock != null) victimLock.SetCinematicZoom01(0f);
            }
        }

        if (!attackAnimFinished) return;

        // 3) Post-attack: zoom-in for 5 seconds, and apply damage after 5 seconds.
        postAttackTimer += Time.deltaTime;

        if (victimLock != null)
        {
            float t = (postAttackZoomDuration <= 0.001f) ? 1f : Mathf.Clamp01(postAttackTimer / postAttackZoomDuration);
            victimLock.SetCinematicZoom01(t);
        }

        if (postAttackTimer < Mathf.Max(0.0f, postAttackDamageDelay)) return;

        ApplyExecuteDamage(victimTarget);
        EndAttackToPatrol();
    }

    private void ApplyExecuteDamage(Transform targetRoot)
    {
        if (targetRoot == null) return;

        // Damage (execute).
        Health th = targetRoot.GetComponentInChildren<Health>();
        if (th != null && !th.IsDead())
            th.ApplyDamage(executeDamage);

        // Reset kill count on hit (as requested).
        var mark = targetRoot.GetComponentInChildren<WraithVengeanceMark>();
        if (mark != null)
            mark.creatureKills = 0;

        // Also remove provocation so we don't instantly re-lock the same victim.
        provokedTargets.Remove(targetRoot);
    }

    private void JumpToWalkState()
    {
        if (animator == null || string.IsNullOrEmpty(walkStateName)) return;

        animator.speed = 1f;

        // Clear action params to avoid getting stuck
        SetAttackParam(false);
        if (!string.IsNullOrEmpty(grabTriggerParam)) animator.ResetTrigger(grabTriggerParam);

        const int layer = 0;
        int hash = Animator.StringToHash(walkStateName);
        if (animator.HasState(layer, hash))
        {
            animator.Play(hash, layer, 0f);
        }
        else
        {
            int fullHash = Animator.StringToHash("Base Layer." + walkStateName);
            if (animator.HasState(layer, fullHash))
                animator.Play(fullHash, layer, 0f);
            else
                animator.Play(walkStateName, layer, 0f);
        }

        // Force immediate evaluation so the next frame is already in Walk
        animator.Update(0f);
    }

    private void JumpToIdleState()
    {
        if (animator == null || string.IsNullOrEmpty(idleStateName)) return;

        animator.speed = 1f;

        // Clear action params to avoid getting stuck
        SetAttackParam(false);
        if (!string.IsNullOrEmpty(grabTriggerParam)) animator.ResetTrigger(grabTriggerParam);
        if (!string.IsNullOrEmpty(roarTriggerParam)) animator.ResetTrigger(roarTriggerParam);

        const int layer = 0;
        int hash = Animator.StringToHash(idleStateName);
        animator.Play(hash, layer, 0f);
        animator.Update(0f);
    }

    private void EnterPostActionIdle(bool clearChaseTarget)
    {
        if (animator != null) animator.speed = 1f;

        // Stop any pending action flags.
        SetAttackParam(false);
        pendingAttack = false;
        attackStateSeen = false;
        attackAnimFinished = false;
        grabStateSeen = false;
        attackStartTimer = 0f;
        postAttackTimer = 0f;

        // Release victim/camera lock.
        ReleaseVictimAndCamera();
        victimTarget = null;
        lockFacingActive = false;

        if (clearChaseTarget) chaseTarget = null;

        // Force Idle animation (if provided) and lock AI in Idle state.
        postActionIdleTimer = Mathf.Max(0f, postActionIdleDuration);
        state = State.Idle;

        // Force animator speed param to 0 immediately (no smoothing tail).
        smoothedSpeed = 0f;
        if (animator != null && !string.IsNullOrEmpty(speedParam))
            animator.SetFloat(speedParam, 0f);

        JumpToIdleState();
    }

    private void TickIdle()
    {
        // Do not move. Hold for the configured duration.
        postActionIdleTimer -= Time.deltaTime;

        // Keep Speed parameter at 0 while idling (avoid drift from smoothing).
        if (animator != null && !string.IsNullOrEmpty(speedParam))
        {
            smoothedSpeed = 0f;
            animator.SetFloat(speedParam, 0f);
        }

        if (postActionIdleTimer > 0f) return;

        // After idle, resume normal behavior.
        state = State.Patrol;
        patrolTarget = null;
        idleTimer = Random.Range(idleTimeMin, idleTimeMax);
    }



    private void AbortGrabToMovement()
    {
        // Grab ended but no attack (or victim invalid) -> release victim and go Idle for a bit.
        EnterPostActionIdle(clearChaseTarget: false);
    }

    private void EndAttackToMovement(bool abortToChase)
    {
        // Aborted attack -> still go Idle briefly to avoid instant re-engage.
        EnterPostActionIdle(clearChaseTarget: !abortToChase);
    }

    private void EndAttackToPatrol()
    {
        // Attack completed -> go Idle for a bit before resuming.
        EnterPostActionIdle(clearChaseTarget: true);
    }

    private void TickDead()
    {
        if (animator != null && !string.IsNullOrEmpty(isDeadParam))
            animator.SetBool(isDeadParam, true);

        if (animator != null) animator.speed = 1f;

        SetAttackParam(false);
        ReleaseVictimAndCamera();
        lockFacingActive = false;

        chaseTarget = null;
        victimTarget = null;
        pendingAttack = false;
    }

    private void OnDeath(Health h)
    {
        state = State.Dead;
    }

    private void CaptureVictimAndStartCamera(Transform victimRoot)
    {
        if (victimRoot == null) return;

        victimLock = victimRoot.GetComponent<WraithVictimLock>();
        if (victimLock == null) victimLock = victimRoot.gameObject.AddComponent<WraithVictimLock>();

        Transform hold = victimHoldPoint != null ? victimHoldPoint : transform;
        Transform face = wraithFacePoint != null ? wraithFacePoint : transform;

        victimLock.Capture(hold, face);
        victimLock.SetCinematicZoom01(0f);
    }

    private void ReleaseVictimAndCamera()
    {
        if (victimLock != null)
        {
            victimLock.Release();
            victimLock = null;
        }
    }

    private Transform FindClosestPlayerRootInRange(float range)
    {
        float best = float.MaxValue;
        Transform bestRoot = null;

        var players = FindObjectsOfType<PlayerController>();
        for (int i = 0; i < players.Length; i++)
        {
            var pc = players[i];
            if (pc == null) continue;

            Transform root = pc.transform.root;
            if (root == null) continue;
            if (!root.gameObject.activeInHierarchy) continue;

            var ph = root.GetComponentInChildren<Health>();
            if (ph != null && ph.IsDead()) continue;

            float d = HorizontalDistance(transform.position, root.position);
            if (d <= range && d < best)
            {
                best = d;
                bestRoot = root;
            }
        }

        return bestRoot;
    }

    private bool IsInGrabState(AnimatorStateInfo info)
    {
        if (!string.IsNullOrEmpty(grabStateTag) && info.IsTag(grabStateTag)) return true;
        if (!string.IsNullOrEmpty(grabStateName) && info.IsName(grabStateName)) return true;
        return false;
    }

    private bool IsInAttackState(AnimatorStateInfo info)
    {
        if (!string.IsNullOrEmpty(attackStateTag) && info.IsTag(attackStateTag)) return true;
        if (!string.IsNullOrEmpty(attackStateName) && info.IsName(attackStateName)) return true;
        return false;
    }

    private void SetAttackParam(bool value)
    {
        if (animator == null || string.IsNullOrEmpty(attackBoolParam)) return;

        // If the parameter exists as bool, set it. If not, this call is harmless.
        animator.SetBool(attackBoolParam, value);
    }

    private void ApplyGravity()
    {
        if (controller == null || !controller.enabled) return;
        if (state == State.Dead) return;

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
    }

    private void MoveTowards(Vector3 target, float speed)
    {
        if (controller == null || !controller.enabled) return;
        if (state == State.Roar || state == State.Idle || state == State.GrabAnim || state == State.Attack || state == State.Dead) return;

        Vector3 dir = target - transform.position;
        dir.y = 0f;

        float dist = dir.magnitude;
        if (dist < 0.001f) return;

        dir /= dist;

        Quaternion q = Quaternion.LookRotation(dir, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, q, turnSpeed * Time.deltaTime);

        controller.Move(dir * speed * Time.deltaTime);
    }

    private void FaceTowards(Vector3 target)
    {
        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        Quaternion q = Quaternion.LookRotation(dir.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, q, turnSpeed * Time.deltaTime);
    }

    private float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void UpdateAnimatorSpeed()
    {
        if (animator == null || string.IsNullOrEmpty(speedParam)) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 delta = transform.position - lastPos;
        delta.y = 0f;

        float raw = delta.magnitude / dt;
        if (raw < 0.15f) raw = 0f;

        smoothedSpeed = Mathf.Lerp(smoothedSpeed, raw, dt * Mathf.Max(0.1f, speedDamp));
        animator.SetFloat(speedParam, smoothedSpeed);
    }
}
