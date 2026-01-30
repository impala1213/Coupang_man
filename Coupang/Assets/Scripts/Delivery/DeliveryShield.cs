using UnityEngine;

/// <summary>
/// Optional helper for the shield prefab.
/// Attach this to the shield root and assign a SphereCollider.
///
/// Behavior:
/// - Blocks enemies (physics), but does NOT block the player.
/// </summary>
[DisallowMultipleComponent]
public class DeliveryShield : MonoBehaviour
{
    [Header("Blocker")]
    public SphereCollider solidCollider;

    [Header("Collision Filtering")]
    [Tooltip("If true, the shield will ignore collisions with the player so only enemies are blocked.")]
    public bool blockEnemiesOnly = true;

    [Tooltip("Player tag used to find the player collider(s) to ignore.")]
    public string playerTag = "Player";

    [Header("Visual (optional)")]
    [Tooltip("Optional ground ring transform (unit circle mesh) that will be scaled to the radius.")]
    public Transform groundRing;

    [Tooltip("Small Y offset for ring to avoid z-fighting.")]
    public float ringYOffset = 0.02f;

    private bool _ignoredPlayerOnce;

    public void Setup(float radius)
    {
        float r = Mathf.Max(0.1f, radius);

        if (!solidCollider)
            solidCollider = GetComponentInChildren<SphereCollider>(true);

        if (solidCollider)
        {
            solidCollider.isTrigger = false;
            solidCollider.radius = r;
            solidCollider.center = Vector3.zero;
        }

        if (groundRing)
        {
            groundRing.localPosition = new Vector3(0f, ringYOffset, 0f);
            groundRing.localScale = new Vector3(r * 2f, 1f, r * 2f);
        }

        if (blockEnemiesOnly)
            IgnorePlayerCollisions();
    }

    private void OnEnable()
    {
        if (blockEnemiesOnly && !_ignoredPlayerOnce)
            IgnorePlayerCollisions();
    }

    private void IgnorePlayerCollisions()
    {
        if (_ignoredPlayerOnce) return;

        GameObject player = null;
        if (!string.IsNullOrEmpty(playerTag))
            player = GameObject.FindGameObjectWithTag(playerTag);

        if (!player) return;

        // All colliders in this shield (some prefabs have multiple).
        var shieldColliders = GetComponentsInChildren<Collider>(true);
        var playerColliders = player.GetComponentsInChildren<Collider>(true);

        if (shieldColliders == null || playerColliders == null) return;

        for (int i = 0; i < shieldColliders.Length; i++)
        {
            var sc = shieldColliders[i];
            if (!sc) continue;

            for (int j = 0; j < playerColliders.Length; j++)
            {
                var pc = playerColliders[j];
                if (!pc) continue;

                Physics.IgnoreCollision(sc, pc, true);
            }
        }

        _ignoredPlayerOnce = true;
    }
}
