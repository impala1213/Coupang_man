// Assets/Scripts/Player/WraithVictimLock.cs
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class WraithVictimLock : MonoBehaviour
{
    [Header("Auto References")]
    public CharacterController characterController;
    public MonoBehaviour playerControllerBehaviour;

    [Header("Disable During Capture")]
    [Tooltip("ProCamera2D, CinemachineBrain, 마우스룩, 카메라 추적 스크립트 등")]
    public Behaviour[] cameraDriversToDisable;

    [Tooltip("플레이어 입력(마우스 룩/카메라 회전 등) 스크립트들")]
    public Behaviour[] playerInputToDisable;

    [Header("Captured State")]
    public bool isCaptured;
    public Transform holdPoint;
    public Transform wraithFacePoint;

    [Header("Hold Offset")]
    public Vector3 extraHoldOffset = Vector3.zero;

    [Header("Victim Rotation")]
    public bool rotateVictimToLookAtWraith = true;

    [Header("Camera Override")]
    public bool controlMainCamera = true;
    public bool hardLockCamera = true;  // 흔들리면 true 유지
    public float cameraLerpSpeed = 10f;

    public float faceDistanceFar = 0.9f;
    public float faceDistanceNear = 0.25f;
    public float fovFar = 55f;
    public float fovNear = 28f;

    public float faceUpOffset = 0.05f;
    public float faceSideOffset = 0.0f;

    [Range(0f, 1f)] public float zoom01;

    [Header("Temporary Camera")]
    [Tooltip("true면 Capture 시 임시 카메라 생성해서 그걸로 줌/고정. Release 시 삭제 후 원래 카메라 복구.")]
    public bool useTemporaryCamera = true;

    [Tooltip("임시 카메라 오브젝트 이름")]
    public string tempCameraName = "WraithTempCamera";


    [Header("First Person Camera Anchor")]
    [Tooltip("If assigned, this transform is used as the camera anchor during capture (temp camera spawns here).")]
    public Transform firstPersonCam;
    [Tooltip("If firstPersonCam is null, tries to find a child transform with this name under the player root.")]
    public string firstPersonCamName = "FirstPersonCam";

    [Header("Camera Lock Mode")]
    [Tooltip("If true, keep camera position at the anchor and only rotate to look at the wraith face.")]
    public bool rotateOnlyFromAnchor = true;


    [Header("Camera Shake (Tremble)")]
    [Tooltip("Enable trembling camera shake during capture/zoom.")]
    public bool enableCameraShake = true;

    [Tooltip("If true, shake strength ramps in using zoom01 (typically used for post-attack zoom-in).")]
    public bool shakeOnlyDuringZoom = true;

    [Range(0f, 1f)]
    [Tooltip("zoom01 value at which shaking starts. Set to 0 to start shaking from the beginning of the zoom-in.")]
    public float shakeZoomStart01 = 0.0f;

    [Range(0f, 1f)]
    [Tooltip("zoom01 value at which shaking reaches full strength.")]
    public float shakeZoomEnd01 = 1.0f;

    [Tooltip("Exponent for shake strength ramp (higher = more shake near the end).")]
    public float shakeRampPower = 2.0f;

    [Range(0f, 1f)]
    [Tooltip("Minimum shake strength at the start of the ramp (0 = no shake at start, 0.2 = subtle shake at start).")]
    public float shakeStartStrength01 = 0.15f;

    [Tooltip("Shake frequency (noise speed).")]
    public float shakeFrequency = 22f;

    [Tooltip("Position shake amplitude in meters.")]
    public float shakePosAmplitude = 0.015f;

    [Tooltip("Rotation shake amplitude in degrees.")]
    public float shakeRotAmplitude = 0.9f;

    [Tooltip("If true, do not move camera position in rotateOnlyFromAnchor mode (rotation-only tremble).")]
    public bool shakeRotationOnlyWhenAnchorLocked = true;

    [Tooltip("Randomize shake seed every capture.")]
    public bool randomizeShakeSeedEachCapture = true;


    [Header("Restore")]
    [Tooltip("Release 시 카메라 드라이버를 '다음 프레임'에 켜서 복구 충돌을 막음")]
    public bool enableDriversNextFrame = true;

    private bool cachedPcEnabled;
    private bool cachedCcEnabled;

    // pivot->mid offset
    private float pivotToMidY = 0.9f;

    // Original camera cache
    private Camera originalCam;
    private Vector3 camPos0;
    private Quaternion camRot0;
    private Vector3 camLocalPos0;
    private Quaternion camLocalRot0;
    private Transform camParent0;
    private float camFov0;
    private bool originalCameraEnabled;
    private string originalTag;


    // Camera anchor (first-person cam preferred)
    private Transform cameraAnchor;

    // Camera shake seed (randomized on capture)
    private float shakeSeed;


    // Temporary camera
    private GameObject tempCamGO;
    private Camera tempCam;

    private bool[] camDriversEnabled0;
    private bool[] playerInputsEnabled0;

    private Coroutine restoreCo;

    void Awake()
    {
        if (characterController == null) characterController = GetComponent<CharacterController>();

        if (playerControllerBehaviour == null)
        {
            var pc = GetComponent<PlayerController>();
            if (pc != null) playerControllerBehaviour = pc;
        }
    
        // Auto find first-person camera anchor by name (optional)
        if (firstPersonCam == null && !string.IsNullOrEmpty(firstPersonCamName))
            firstPersonCam = FindDeepChild(transform.root, firstPersonCamName);
}

    void LateUpdate()
    {
        if (!isCaptured) return;

        // Victim follow hand (grab middle, not feet)
        if (holdPoint != null)
            transform.position = holdPoint.position - Vector3.up * pivotToMidY + extraHoldOffset;

        // Victim faces wraith (optional)
        if (rotateVictimToLookAtWraith && wraithFacePoint != null)
        {
            Vector3 to = wraithFacePoint.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0004f)
                transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        // Camera looks at wraith face (temp cam preferred)
        Camera activeCam = (useTemporaryCamera && tempCam != null) ? tempCam : originalCam;
        if (!controlMainCamera || activeCam == null || wraithFacePoint == null) return;

        float fov = Mathf.Lerp(fovFar, fovNear, zoom01);

        Vector3 facePos = wraithFacePoint.position;

        if (rotateOnlyFromAnchor)
        {
            // Keep camera position at the anchor (first-person camera) and only rotate to look at the wraith face.
            Vector3 camPos = (cameraAnchor != null) ? cameraAnchor.position : activeCam.transform.position;
            Vector3 lookDir = facePos - camPos;
            Quaternion desiredRot = activeCam.transform.rotation;
            if (lookDir.sqrMagnitude > 0.0004f)
                desiredRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

            // Tremble during zoom (optional)
            ApplyCameraShake(ref camPos, ref desiredRot, anchorLocked: true);

            if (hardLockCamera)
            {
                activeCam.transform.SetPositionAndRotation(camPos, desiredRot);
                activeCam.fieldOfView = fov;
            }
            else
            {
                activeCam.transform.position = Vector3.Lerp(activeCam.transform.position, camPos, Time.deltaTime * cameraLerpSpeed);
                activeCam.transform.rotation = Quaternion.Slerp(activeCam.transform.rotation, desiredRot, Time.deltaTime * cameraLerpSpeed);
                activeCam.fieldOfView = Mathf.Lerp(activeCam.fieldOfView, fov, Time.deltaTime * cameraLerpSpeed);
            }
            return;
        }

        // Orbit mode (legacy): move camera around wraith face
        float dist = Mathf.Lerp(faceDistanceFar, faceDistanceNear, zoom01);
        Vector3 desiredPos =
            facePos + wraithFacePoint.forward * dist
            + Vector3.up * faceUpOffset
            + wraithFacePoint.right * faceSideOffset;

        Vector3 orbitLookDir = facePos - desiredPos;
        Quaternion orbitRot = activeCam.transform.rotation;
        if (orbitLookDir.sqrMagnitude > 0.0004f)
            orbitRot = Quaternion.LookRotation(orbitLookDir.normalized, Vector3.up);

        // Tremble during zoom (optional)
        ApplyCameraShake(ref desiredPos, ref orbitRot, anchorLocked: false);

        if (hardLockCamera)
        {
            activeCam.transform.SetPositionAndRotation(desiredPos, orbitRot);
            activeCam.fieldOfView = fov;
        }
        else
        {
            activeCam.transform.position = Vector3.Lerp(activeCam.transform.position, desiredPos, Time.deltaTime * cameraLerpSpeed);
            activeCam.transform.rotation = Quaternion.Slerp(activeCam.transform.rotation, orbitRot, Time.deltaTime * cameraLerpSpeed);
            activeCam.fieldOfView = Mathf.Lerp(activeCam.fieldOfView, fov, Time.deltaTime * cameraLerpSpeed);
        }
    }


    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    /// <summary>
    /// Tries to resolve the player's "original" camera to cache/disable during capture.
    /// Priority:
    /// 1) Camera on/near firstPersonCam anchor
    /// 2) Camera.main
    /// 3) Any enabled Camera in scene
    /// </summary>
    private Camera ResolveOriginalCamera()
    {
        if (firstPersonCam != null)
        {
            Camera c = firstPersonCam.GetComponent<Camera>();
            if (c != null) return c;

            c = firstPersonCam.GetComponentInChildren<Camera>(true);
            if (c != null) return c;

            c = firstPersonCam.GetComponentInParent<Camera>(true);
            if (c != null) return c;
        }

        if (Camera.main != null) return Camera.main;

        Camera[] cams = GameObject.FindObjectsOfType<Camera>();
        if (cams != null && cams.Length > 0)
        {
            for (int i = 0; i < cams.Length; i++)
            {
                if (cams[i] != null && cams[i].enabled) return cams[i];
            }
            return cams[0];
        }

        return null;
    }

    /// <summary>
    /// Camera anchor transform used for temp-cam spawning and "rotate only" mode.
    /// Prefer firstPersonCam anchor if present, otherwise use the resolved original camera transform.
    /// </summary>
    private Transform ResolveCameraAnchor(Camera resolvedOriginalCam)
    {
        if (firstPersonCam != null) return firstPersonCam;
        if (resolvedOriginalCam != null) return resolvedOriginalCam.transform;
        return null;
    }

    /// <summary>
    /// Recursively searches for a child transform by name.
    /// </summary>
    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName)) return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform c = root.GetChild(i);
            if (c.name == childName) return c;

            Transform found = FindDeepChild(c, childName);
            if (found != null) return found;
        }

        return null;
    }

    public void Capture(Transform hold, Transform face)
    {
        if (isCaptured) return;

        if (restoreCo != null)
        {
            StopCoroutine(restoreCo);
            restoreCo = null;
        }

        holdPoint = hold;
        wraithFacePoint = face;
        isCaptured = true;

        pivotToMidY = ComputePivotToMidY();
        zoom01 = 0f;

        if (randomizeShakeSeedEachCapture)
            shakeSeed = Random.Range(0f, 1000f);

        // Disable player movement / controller
        if (playerControllerBehaviour != null)
        {
            cachedPcEnabled = playerControllerBehaviour.enabled;
            playerControllerBehaviour.enabled = false;
        }

        if (characterController != null)
        {
            cachedCcEnabled = characterController.enabled;
            characterController.enabled = false;
        }

        // Cache original camera state (EVERY capture)
        if (controlMainCamera)
        {
            originalCam = ResolveOriginalCamera();
            if (originalCam != null)
            {
                camPos0 = originalCam.transform.position;
                camRot0 = originalCam.transform.rotation;
                camLocalPos0 = originalCam.transform.localPosition;
                camLocalRot0 = originalCam.transform.localRotation;
                camParent0 = originalCam.transform.parent;
                camFov0 = originalCam.fieldOfView;
                originalCameraEnabled = originalCam.enabled;
                originalTag = originalCam.gameObject.tag;

                cameraAnchor = ResolveCameraAnchor(originalCam);
            }
        }

        // Disable camera drivers & inputs so mouse can't move camera
        DisableBehaviours(cameraDriversToDisable, ref camDriversEnabled0);
        DisableBehaviours(playerInputToDisable, ref playerInputsEnabled0);

        // Create temp camera and switch rendering to it
        if (useTemporaryCamera && originalCam != null)
        {
            // Disable original camera rendering (keep object alive)
            originalCam.enabled = false;

            // Swap tags to ensure Camera.main points to temp while captured
            originalCam.gameObject.tag = "Untagged";

            tempCamGO = new GameObject(tempCameraName);
                        Vector3 spawnPos = (cameraAnchor != null) ? cameraAnchor.position : camPos0;
            Quaternion spawnRot = (cameraAnchor != null) ? cameraAnchor.rotation : camRot0;
            tempCamGO.transform.position = spawnPos;
                        tempCamGO.transform.rotation = spawnRot;
            tempCamGO.tag = "MainCamera";

            tempCam = tempCamGO.AddComponent<Camera>();

            // Copy important camera settings
            tempCam.clearFlags = originalCam.clearFlags;
            tempCam.backgroundColor = originalCam.backgroundColor;
            tempCam.cullingMask = originalCam.cullingMask;
            tempCam.orthographic = originalCam.orthographic;
            tempCam.orthographicSize = originalCam.orthographicSize;
            tempCam.nearClipPlane = originalCam.nearClipPlane;
            tempCam.farClipPlane = originalCam.farClipPlane;
            tempCam.allowHDR = originalCam.allowHDR;
            tempCam.allowMSAA = originalCam.allowMSAA;
            tempCam.depth = originalCam.depth + 10f; // ensure it renders on top
            tempCam.fieldOfView = camFov0;
        }
    }

    public void Release()
    {
        if (!isCaptured) return;

        isCaptured = false;

        // Restore player control
        if (playerControllerBehaviour != null)
            playerControllerBehaviour.enabled = cachedPcEnabled;

        if (characterController != null)
            characterController.enabled = cachedCcEnabled;

        // Destroy temp camera and restore original camera
        if (useTemporaryCamera && tempCamGO != null)
        {
            Destroy(tempCamGO);
            tempCamGO = null;
            tempCam = null;
        }

        if (controlMainCamera && originalCam != null)
        {
            // Restore tag
            originalCam.gameObject.tag = string.IsNullOrEmpty(originalTag) ? "MainCamera" : originalTag;

            // Restore original camera transform/FOV
// NOTE: When using a temporary camera, we NEVER touch the original camera transform,
// so restoring it here can cause a visible "jump" (especially if the camera is parented to the player).
if (!useTemporaryCamera)
{
    if (originalCam.transform.parent == camParent0)
    {
        originalCam.transform.localPosition = camLocalPos0;
        originalCam.transform.localRotation = camLocalRot0;
    }
    else
    {
        originalCam.transform.SetPositionAndRotation(camPos0, camRot0);
    }

    originalCam.fieldOfView = camFov0;
}
            // Re-enable original camera rendering
            originalCam.enabled = originalCameraEnabled;

            cameraAnchor = null;
        }

        // Re-enable drivers (same frame or next frame)
        if (enableDriversNextFrame)
        {
            restoreCo = StartCoroutine(EnableDriversNextFrame());
        }
        else
        {
            RestoreBehaviours(cameraDriversToDisable, camDriversEnabled0);
            RestoreBehaviours(playerInputToDisable, playerInputsEnabled0);
        }

        // cleanup
        holdPoint = null;
        wraithFacePoint = null;
        zoom01 = 0f;
    }

    public void SetCinematicZoom01(float t) => zoom01 = Mathf.Clamp01(t);

    /// <summary>
    /// Applies a small trembling offset based on Perlin noise.
    /// Designed to be used during the post-attack zoom-in phase.
    /// </summary>
    private void ApplyCameraShake(ref Vector3 camPos, ref Quaternion camRot, bool anchorLocked)
    {
        if (!enableCameraShake) return;

        float strength = 1f;
        if (shakeOnlyDuringZoom)
        {
            // Don't shake before the zoom-in begins.
            if (zoom01 < shakeZoomStart01) return;

            float denom = Mathf.Max(0.0001f, shakeZoomEnd01 - shakeZoomStart01);
            float u = Mathf.Clamp01((zoom01 - shakeZoomStart01) / denom);

            // Ramp (0..1) and then map to [startStrength..1].
            u = Mathf.Pow(u, Mathf.Max(0.01f, shakeRampPower));
            float start = Mathf.Clamp01(shakeStartStrength01);
            strength = Mathf.Lerp(start, 1f, u);
        }

        float freq = Mathf.Max(0.01f, shakeFrequency);
        float t = Time.unscaledTime * freq;

        // 0..1 -> -1..1
        float nx = Mathf.PerlinNoise(shakeSeed + 1.17f, t) * 2f - 1f;
        float ny = Mathf.PerlinNoise(shakeSeed + 2.33f, t) * 2f - 1f;
        float nr1 = Mathf.PerlinNoise(shakeSeed + 3.71f, t) * 2f - 1f;
        float nr2 = Mathf.PerlinNoise(shakeSeed + 4.91f, t) * 2f - 1f;

        if (!(anchorLocked && shakeRotationOnlyWhenAnchorLocked))
        {
            Vector3 right = camRot * Vector3.right;
            Vector3 up = camRot * Vector3.up;
            camPos += (right * nx + up * ny) * (shakePosAmplitude * strength);
        }

        // Small rotation jitter (degrees)
        float pitch = nr1 * (shakeRotAmplitude * strength);
        float yaw = nr2 * (shakeRotAmplitude * strength);
        camRot = camRot * Quaternion.Euler(pitch, yaw, 0f);
    }

    private IEnumerator EnableDriversNextFrame()
    {
        yield return null;

        RestoreBehaviours(cameraDriversToDisable, camDriversEnabled0);
        RestoreBehaviours(playerInputToDisable, playerInputsEnabled0);

        restoreCo = null;
    }

    private void DisableBehaviours(Behaviour[] list, ref bool[] cache)
    {
        if (list == null || list.Length == 0) { cache = null; return; }

        cache = new bool[list.Length];
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i] == null) continue;
            cache[i] = list[i].enabled;
            list[i].enabled = false;
        }
    }

    private void RestoreBehaviours(Behaviour[] list, bool[] cache)
    {
        if (list == null || cache == null) return;

        for (int i = 0; i < list.Length && i < cache.Length; i++)
        {
            if (list[i] == null) continue;
            list[i].enabled = cache[i];
        }
    }

    private float ComputePivotToMidY()
    {
        if (characterController != null)
        {
            float y = Mathf.Abs(characterController.center.y);
            if (y > 0.05f) return y;
            return Mathf.Max(0.2f, characterController.height * 0.5f);
        }

        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            float midY = col.bounds.center.y;
            return Mathf.Max(0.2f, midY - transform.position.y);
        }

        Renderer r = GetComponentInChildren<Renderer>();
        if (r != null)
        {
            float midY = r.bounds.center.y;
            return Mathf.Max(0.2f, midY - transform.position.y);
        }

        return 0.9f;
    }
}
