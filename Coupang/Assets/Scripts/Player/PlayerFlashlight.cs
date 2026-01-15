// Assets/Scripts/Player/PlayerFlashlight.cs
using UnityEngine;

/// <summary>
/// Simple flashlight that emits light from two eye points.
/// Toggle is handled by PlayerController (default key: F).
/// </summary>
[DisallowMultipleComponent]
public class PlayerFlashlight : MonoBehaviour
{
    [Header("Eye Anchors (optional)")]
    public Transform leftEye;
    public Transform rightEye;

    [Header("Light Settings")]
    public LightType lightType = LightType.Spot;

    [Min(0.01f)]
    public float range = 8f;

    [Min(0f)]
    public float intensity = 3f;

    [Range(1f, 179f)]
    public float spotAngle = 60f;

    [Tooltip("If true, lights will be aligned to the player forward every frame.")]
    public bool alignToPlayerForward = true;

    [Header("State")]
    public bool startOn = false;

    [Header("Runtime (auto-created if null)")]
    public Light leftLight;
    public Light rightLight;

    public bool IsOn => isOn;

    private bool isOn;

    void Awake()
    {
        if (!leftEye || !rightEye)
            AutoFindEyes();

        SetupLight(ref leftLight, leftEye, "Flashlight_L");
        SetupLight(ref rightLight, rightEye, "Flashlight_R");

        SetOn(startOn);
    }

    void LateUpdate()
    {
        if (!isOn) return;
        if (!alignToPlayerForward) return;

        Quaternion rot = Quaternion.LookRotation(transform.forward, Vector3.up);

        if (leftLight) leftLight.transform.rotation = rot;
        if (rightLight) rightLight.transform.rotation = rot;
    }

    public void Toggle()
    {
        SetOn(!isOn);
    }

    public void SetOn(bool on)
    {
        isOn = on;

        if (leftLight) leftLight.enabled = isOn;
        if (rightLight) rightLight.enabled = isOn;
    }

    private void SetupLight(ref Light lightComp, Transform anchor, string name)
    {
        if (!anchor)
        {
            // Fallback: create an anchor at the player transform.
            GameObject fallback = new GameObject(name + "_Anchor");
            fallback.transform.SetParent(transform);
            fallback.transform.localPosition = new Vector3(0f, 1.6f, 0.2f);
            fallback.transform.localRotation = Quaternion.identity;
            anchor = fallback.transform;
        }

        if (!lightComp)
        {
            // Try to reuse existing Light on anchor.
            lightComp = anchor.GetComponentInChildren<Light>(true);
        }

        if (!lightComp)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(anchor);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            lightComp = go.AddComponent<Light>();
        }

        lightComp.type = lightType;
        lightComp.range = range;
        lightComp.intensity = intensity;

        if (lightType == LightType.Spot)
            lightComp.spotAngle = spotAngle;

        lightComp.enabled = false;
    }

    private void AutoFindEyes()
    {
        // Common naming patterns.
        leftEye = leftEye ? leftEye : FindChildByNameContains(transform, "eye_l", "left_eye", "lefteye", "eyeleft", "eye1");
        rightEye = rightEye ? rightEye : FindChildByNameContains(transform, "eye_r", "right_eye", "righteye", "eyeright", "eye2");

        // If not found, create simple anchors near the head height.
        if (!leftEye)
        {
            GameObject go = new GameObject("Eye_L_Anchor");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(-0.08f, 1.6f, 0.2f);
            go.transform.localRotation = Quaternion.identity;
            leftEye = go.transform;
        }

        if (!rightEye)
        {
            GameObject go = new GameObject("Eye_R_Anchor");
            go.transform.SetParent(transform);
            go.transform.localPosition = new Vector3(0.08f, 1.6f, 0.2f);
            go.transform.localRotation = Quaternion.identity;
            rightEye = go.transform;
        }
    }

    private Transform FindChildByNameContains(Transform root, params string[] tokens)
    {
        if (!root) return null;

        Transform[] all = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            string n = all[i].name.ToLowerInvariant();
            for (int t = 0; t < tokens.Length; t++)
            {
                if (n.Contains(tokens[t]))
                    return all[i];
            }
        }
        return null;
    }
}
