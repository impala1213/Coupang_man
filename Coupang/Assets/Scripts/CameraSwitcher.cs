using UnityEngine;

public class CameraSwitcher : MonoBehaviour
{
    public Camera firstPersonCam;
    public Camera thirdPersonCam;
    public Transform thirdPersonTarget; // usually the player root or a head/upper-body empty

    [Header("Third Person Defaults")]
    public float tpDistance = 3.2f;     // Z back distance
    public float tpHeight = 1.6f;       // Y height above target
    public float tpSideOffset = 0.45f;  // X shoulder offset (right shoulder)
    public float tpPitch = 10f;         // slight downward tilt

    private bool useThird = false;

    void Start()
    {
        SetThirdPerson(false);
    }

    public void SetThirdPerson(bool enabled)
    {
        useThird = enabled;

        if (firstPersonCam) firstPersonCam.enabled = !enabled;
        if (thirdPersonCam)
        {
            thirdPersonCam.enabled = enabled;

            // Keep only one AudioListener active
            var fpListener = firstPersonCam ? firstPersonCam.GetComponent<AudioListener>() : null;
            var tpListener = thirdPersonCam.GetComponent<AudioListener>();
            if (fpListener) fpListener.enabled = !enabled;
            if (tpListener) tpListener.enabled = enabled;

            // Snap TPP camera when enabling third-person
            if (enabled) SnapThirdPersonToDefault();
        }
    }

    public void SnapThirdPersonToDefault()
    {
        if (!thirdPersonCam || !thirdPersonTarget) return;

        // Compute world position from local offsets relative to target
        Vector3 local = new Vector3(tpSideOffset, tpHeight, -tpDistance);
        Vector3 worldPos = thirdPersonTarget.TransformPoint(local);
        thirdPersonCam.transform.position = worldPos;

        // Look at a point slightly above the target
        Vector3 lookAt = thirdPersonTarget.position + Vector3.up * (tpHeight * 0.6f);
        thirdPersonCam.transform.rotation = Quaternion.LookRotation(lookAt - worldPos, Vector3.up);

        // Apply fixed pitch (PUBG-like slight downward tilt)
        if (tpPitch != 0f)
        {
            Vector3 e = thirdPersonCam.transform.eulerAngles;
            e.x = tpPitch;
            thirdPersonCam.transform.eulerAngles = e;
        }
    }

    public Camera GetActiveCamera() => useThird ? thirdPersonCam : firstPersonCam;
}
