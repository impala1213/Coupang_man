using UnityEngine;

[DisallowMultipleComponent]
public class CameraSwitcher : MonoBehaviour
{
    [Header("Cameras")]
    public Camera firstPersonCam;
    public Camera thirdPersonCam;
    public Transform thirdPersonTarget;

    [Header("Third Person Defaults")]
    public float tpDistance = 3.2f;
    public float tpHeight = 1.6f;
    public float tpSideOffset = 0.45f;
    public float tpPitch = 10f;

    private bool useThird = false;

    void Start() => SetThirdPerson(false);

    public bool IsThirdPerson() => useThird;
    public Camera GetActiveCamera() => useThird ? thirdPersonCam : firstPersonCam;

    public void SetThirdPerson(bool enabled)
    {
        if (useThird == enabled) return; // guard
        useThird = enabled;

        if (firstPersonCam) firstPersonCam.enabled = !enabled;
        if (thirdPersonCam) thirdPersonCam.enabled = enabled;

        var fpListener = firstPersonCam ? firstPersonCam.GetComponent<AudioListener>() : null;
        var tpListener = thirdPersonCam ? thirdPersonCam.GetComponent<AudioListener>() : null;
        if (fpListener) fpListener.enabled = !enabled;
        if (tpListener) tpListener.enabled = enabled;

        if (enabled) SnapThirdPersonToDefault();
    }

    public void SnapThirdPersonToDefault()
    {
        if (!thirdPersonCam || !thirdPersonTarget) return;

        Vector3 local = new Vector3(tpSideOffset, tpHeight, -tpDistance);
        Vector3 worldPos = thirdPersonTarget.TransformPoint(local);
        thirdPersonCam.transform.position = worldPos;

        Vector3 lookAt = thirdPersonTarget.position + Vector3.up * (tpHeight * 0.6f);
        thirdPersonCam.transform.rotation = Quaternion.LookRotation(lookAt - worldPos, Vector3.up);

        if (tpPitch != 0f)
        {
            Vector3 e = thirdPersonCam.transform.eulerAngles;
            e.x = tpPitch;
            thirdPersonCam.transform.eulerAngles = e;
        }
    }
}
