using UnityEngine;

[DisallowMultipleComponent]
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;               // Player root or an upper-body empty

    [Header("PUBG-like Offsets")]
    public float distance = 3.2f;          // backward distance (Z-)
    public float height = 1.6f;            // height above target (Y)
    public float sideOffset = 0.45f;       // shoulder offset (X, right side)
    public float startPitch = 10f;         // starting pitch on enable (deg)

    [Header("Mouse Pitch Control")]
    public bool controlPitchWithMouse = true;
    public float mouseSensitivity = 2.5f;  // adjust to taste
    public bool invertY = false;
    public float minPitch = -35f;          // allow looking down
    public float maxPitch = 45f;           // allow looking up

    [Header("Behavior")]
    public bool detachOnEnable = true;     // detach from parent on enable to avoid FP drift
    public bool snapOnEnable = true;       // snap to default pose on enable
    public bool followYaw = true;          // follow target yaw each frame
    public bool clampCollision = false;    // optional simple collision clamp

    [Header("Smoothing (optional)")]
    public bool smoothFollow = true;
    public float posSmoothTime = 0.06f;    // lower is snappier
    public float rotSmoothSpeed = 20f;     // deg/sec for rotation slerp

    private Transform originalParent;
    private Vector3 originalLocalPos;
    private Quaternion originalLocalRot;
    private Vector3 posVel;

    private float currentPitch;            // runtime pitch (deg)

    void OnEnable()
    {
        if (detachOnEnable)
        {
            originalParent = transform.parent;
            originalLocalPos = transform.localPosition;
            originalLocalRot = transform.localRotation;
            transform.SetParent(null, true); // keep world pose
        }

        // initialize pitch
        currentPitch = Mathf.Clamp(startPitch, minPitch, maxPitch);

        if (snapOnEnable)
        {
            SnapToDefault();
        }
    }

    void OnDisable()
    {
        if (detachOnEnable && originalParent)
        {
            transform.SetParent(originalParent, false);
            transform.localPosition = originalLocalPos;
            transform.localRotation = originalLocalRot;
        }
    }

    void LateUpdate()
    {
        if (!target) return;

        // 1) Yaw from target (yaw-only follow)
        float yaw = followYaw ? target.eulerAngles.y : transform.eulerAngles.y;

        // 2) Pitch from mouse (optional)
        if (controlPitchWithMouse)
        {
            float my = Input.GetAxis("Mouse Y"); // DO NOT multiply by deltaTime
            if (invertY) my = -my;
            currentPitch = Mathf.Clamp(currentPitch - my * mouseSensitivity, minPitch, maxPitch);
        }

        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);

        // 3) Desired position in yaw space
        Vector3 offset = new Vector3(sideOffset, height, -distance);
        Vector3 desiredPos = target.position + yawRot * offset;

        // 4) Optional collision clamp
        if (clampCollision)
        {
            Vector3 head = target.position + Vector3.up * height;
            Vector3 toCam = desiredPos - head;
            float dist = toCam.magnitude;
            if (dist > 0.001f && Physics.SphereCast(head, 0.2f, toCam.normalized, out RaycastHit hit, dist, ~0, QueryTriggerInteraction.Ignore))
            {
                desiredPos = hit.point + hit.normal * 0.2f;
            }
        }

        // 5) Move
        if (smoothFollow)
            transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref posVel, posSmoothTime);
        else
            transform.position = desiredPos;

        // 6) Rotate: currentPitch + target yaw, zero roll
        Quaternion targetRot = Quaternion.Euler(currentPitch, yaw, 0f);
        if (smoothFollow)
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Mathf.Clamp01(rotSmoothSpeed * Time.deltaTime));
        else
            transform.rotation = targetRot;
    }

    public void SnapToDefault()
    {
        if (!target) return;

        float yaw = target.eulerAngles.y;
        Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
        Vector3 offset = new Vector3(sideOffset, height, -distance);
        Vector3 desiredPos = target.position + yawRot * offset;

        transform.position = desiredPos;
        transform.rotation = Quaternion.Euler(currentPitch, yaw, 0f);
    }
}
