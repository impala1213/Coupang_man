using System;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Reusable death motion lock (NO extra component needed).
/// Usage:
///  - Create as a field in your AI script.
///  - Bind() in Awake().
///  - Call Tick() at the top of Update(); if it returns true => return.
///  - Optionally call LateTick() in LateUpdate() for stronger XZ lock.
/// 
/// Features:
///  - Stops AI logic when dead (by early-return in Update).
///  - Keeps gravity active via CharacterController (corpse falls/settles).
///  - Locks XZ to prevent "dead follows" drift while allowing Y (fall).
///  - Disables NavMeshAgent / freezes Rigidbody / disables extra colliders (optional).
///  - Optional callback on first dead frame (for triggers like WakeUp).
/// </summary>
[Serializable]
public class EnemyDeath
{
    [Header("Gravity")]
    public bool gravityWhenDead = true;
    public float gravity = -19.62f;

    [Header("XZ Lock (prevents dead drifting/following)")]
    public bool lockXZOnDeath = true;
    public float xzCorrectionSpeed = 120f;
    public float lockTolerance = 0.0005f;

    [Header("Disable movers (optional)")]
    public bool disableNavMeshAgent = true;
    public bool freezeRigidbody = true;
    public bool disableNonCharacterControllerColliders = true;

    [Header("Animator (optional)")]
    public Animator animator;
    public string isDeadParam = "IsDead";
    public bool isDeadParamIsTrigger = false;
    public string speedParam = "Speed";

    // Callback: fired once when death is handled (even if OnDeath event didn't fire).
    public Action onDeadFirstTime;

    // Runtime
    private Transform self;
    private Health health;
    private CharacterController cc;
    private NavMeshAgent agent;
    private Rigidbody rb;
    private Collider[] colliders;

    private bool handled;
    private Vector3 anchorPos;
    private float verticalVel;

    public void Bind(
        Transform self,
        Health health,
        CharacterController characterController,
        Animator animator,
        NavMeshAgent agent = null,
        Rigidbody rb = null,
        Collider[] colliders = null)
    {
        this.self = self;
        this.health = health;
        this.cc = characterController;
        this.animator = animator;
        this.agent = agent;
        this.rb = rb;
        this.colliders = colliders;

        if (this.animator != null)
            this.animator.applyRootMotion = false;
    }

    public bool IsDeadNow()
    {
        if (health == null) return false;
        // Covers both proper death flow and "HP set to 0" debug cases.
        return health.IsDead() || health.currentHealth <= 0;
    }

    /// <summary>
    /// Call this at the TOP of Update(). Returns true if dead-handling ran (then caller should return).
    /// </summary>
    public bool Tick()
    {
        if (!IsDeadNow()) return false;

        EnsureHandledOnce();
        ForceDeadAnimatorState();

        if (gravityWhenDead)
            ApplyGravity();

        if (lockXZOnDeath)
            CorrectDeadXZ();

        return true;
    }

    /// <summary>
    /// Call this in LateUpdate() if something still tries to move XZ after Update().
    /// </summary>
    public void LateTick()
    {
        if (!handled) return;
        if (!lockXZOnDeath) return;

        CorrectDeadXZ();
    }

    private void EnsureHandledOnce()
    {
        if (handled) return;
        handled = true;

        anchorPos = self != null ? self.position : Vector3.zero;
        verticalVel = 0f;

        onDeadFirstTime?.Invoke();

        if (disableNavMeshAgent && agent != null)
            agent.enabled = false;

        if (freezeRigidbody && rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // Keep CharacterController enabled if we want gravity.
        // Do not disable cc here.

        if (disableNonCharacterControllerColliders && colliders != null)
        {
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null) continue;

                // Do not disable CharacterController (not a Collider type), but keep this safe anyway.
                c.enabled = false;
            }
        }
    }

    private void ForceDeadAnimatorState()
    {
        if (animator == null) return;

        if (!string.IsNullOrEmpty(isDeadParam))
        {
            if (isDeadParamIsTrigger)
            {
                animator.ResetTrigger(isDeadParam);
                animator.SetTrigger(isDeadParam);
            }
            else
            {
                animator.SetBool(isDeadParam, true);
            }
        }

        if (!string.IsNullOrEmpty(speedParam))
            animator.SetFloat(speedParam, 0f);
    }

    private void ApplyGravity()
    {
        if (cc == null || !cc.enabled) return;

        if (cc.isGrounded && verticalVel < 0f)
            verticalVel = -2f;

        verticalVel += gravity * Time.deltaTime;
        cc.Move(Vector3.up * verticalVel * Time.deltaTime);
    }

    private void CorrectDeadXZ()
    {
        if (self == null) return;

        Vector3 p = self.position;
        Vector3 target = anchorPos;
        target.y = p.y; // allow vertical movement

        Vector3 delta = target - p;
        delta.y = 0f;

        if (delta.sqrMagnitude <= lockTolerance * lockTolerance)
            return;

        // Prefer CC.Move to respect collisions if CC exists/enabled
        if (cc != null && cc.enabled)
        {
            Vector3 step = Vector3.ClampMagnitude(delta, xzCorrectionSpeed * Time.deltaTime);
            cc.Move(step);
        }
        else
        {
            // Fallback (no CC)
            self.position = new Vector3(anchorPos.x, p.y, anchorPos.z);
        }
    }
}
