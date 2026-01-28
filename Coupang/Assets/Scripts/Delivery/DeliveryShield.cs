using UnityEngine;

/// <summary>
/// Optional helper for the shield prefab.
/// Attach this to the shield root and assign a SphereCollider to block players/monsters.
/// </summary>
[DisallowMultipleComponent]
public class DeliveryShield : MonoBehaviour
{
    [Header("Blocker")]
    public SphereCollider solidCollider;

    [Header("Visual (optional)")]
    [Tooltip("Optional ground ring transform (unit circle mesh) that will be scaled to the radius.")]
    public Transform groundRing;

    [Tooltip("Small Y offset for ring to avoid z-fighting.")]
    public float ringYOffset = 0.02f;

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
    }
}
