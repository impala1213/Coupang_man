using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CarrierController : MonoBehaviour
{
    [Header("State (selection-driven)")]
    [SerializeField] private bool active;
    public bool IsActive => active;
    public event Action<bool> OnActiveChanged;

    [Header("References")]
    public CameraSwitcher cameraSwitcher;
    public Transform carrierCargoRoot;
    public Transform carrierVisualPivot;

    [Header("Back Visual")]
    public bool showBackVisual = true;
    public Transform backSocket;
    public GameObject backVisualPrefab;
    public Vector3 backLocalPosition = Vector3.zero;
    public Vector3 backLocalEuler = Vector3.zero;
    public Vector3 backLocalScale = Vector3.one;
    public bool hideOnDeactivate = true;

    private GameObject backVisualInstance;

    [Header("Balance")]
    public float tilt;
    public float tiltDrift = 5f;
    public float tiltRecoverFactor = 30f;
    public float fallThreshold = 25f;

    [Header("Spill Physics")]
    public float spillExplosionForce = 6f;
    public float spillExplosionRadius = 2.5f;

    [Header("Derived (read-only)")]
    public float totalWeight;
    public float comHeight;

    public bool HasMountedCargo => mounted.Count > 0;
    private readonly List<WorldItem> mounted = new List<WorldItem>();

    void Update()
    {
        if (!active) return;
        UpdateDerived();
        SimulateBalance(Time.deltaTime);
        UpdateVisual();
    }

#if UNITY_EDITOR
    void OnValidate() { SyncVisuals(); }
#endif

    public void SetActiveMode(bool value)
    {
        if (active == value) { SyncVisuals(); return; }
        active = value;
        SyncVisuals();
        OnActiveChanged?.Invoke(active);
    }

    private void SyncVisuals()
    {
        if (cameraSwitcher) cameraSwitcher.SetThirdPerson(active);
        UpdateBackVisual();
    }

    private void UpdateBackVisual()
    {
        if (!showBackVisual)
        {
            if (backVisualInstance) backVisualInstance.SetActive(false);
            return;
        }

        if (active)
        {
            Transform parent = backSocket ? backSocket : transform;
            if (!backVisualInstance)
            {
                backVisualInstance = backVisualPrefab
                    ? Instantiate(backVisualPrefab, parent, false)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);

                if (!backVisualPrefab)
                    backVisualInstance.name = "CarrierBackVisual_Placeholder";

                foreach (var c in backVisualInstance.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var r in backVisualInstance.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);

                backVisualInstance.transform.SetParent(parent, false);
            }

            backVisualInstance.transform.localPosition = backLocalPosition;
            backVisualInstance.transform.localRotation = Quaternion.Euler(backLocalEuler);
            backVisualInstance.transform.localScale = backLocalScale;
            backVisualInstance.SetActive(true);
        }
        else
        {
            if (backVisualInstance && hideOnDeactivate) backVisualInstance.SetActive(false);
        }
    }

    private void UpdateDerived()
    {
        totalWeight = 0f;
        float weightedCenter = 0f;
        float cumulativeY = 0f;

        for (int i = 0; i < mounted.Count; i++)
        {
            var w = mounted[i];
            if (!w || !w.definition) continue;

            float mass = Mathf.Max(0.01f, w.definition.weight);
            Vector3 sz = w.definition.stackSize;
            float h = Mathf.Max(0.01f, sz.y);

            totalWeight += mass;
            float centerY = cumulativeY + h * 0.5f;
            weightedCenter += mass * centerY;
            cumulativeY += h;
        }
        comHeight = (totalWeight > 0f) ? (weightedCenter / totalWeight) : 0.1f;
    }

    private void SimulateBalance(float dt)
    {
        float difficulty = totalWeight * (0.5f + comHeight * 1.5f);
        float drift = (UnityEngine.Random.value - 0.5f) * 2f * tiltDrift * (1f + difficulty * 0.2f);
        tilt += drift * dt;
        tilt = Mathf.Clamp(tilt, -90f, 90f);
        if (Mathf.Abs(tilt) > fallThreshold) SpillAll(); // tip-over spill near back
    }

    private void UpdateVisual()
    {
        if (carrierVisualPivot)
            carrierVisualPivot.localRotation = Quaternion.Euler(0f, 0f, -tilt);
    }

    public void ApplyBalanceInput(float left, float right)
    {
        if (!active) return;
        float input = (-left + right);
        tilt = Mathf.MoveTowards(tilt, 0f, Mathf.Abs(input) * tiltRecoverFactor * Time.deltaTime);
    }

    public bool TryMount(WorldItem world)
    {
        if (!active) return false;
        if (!world || !world.definition) return false;
        if (world.definition.isCarrier) return false;

        world.OnPickedUp();

        if (!carrierCargoRoot)
        {
            mounted.Add(world);
            return true;
        }

        float currentY = 0f;
        for (int i = 0; i < mounted.Count; i++)
            currentY += Mathf.Max(0.01f, mounted[i].definition.stackSize.y);

        var def = world.definition;
        Vector3 sz = def.stackSize;
        float h = Mathf.Max(0.01f, sz.y);

        if (def.stackVisualPrefab)
        {
            var go = Instantiate(def.stackVisualPrefab, carrierCargoRoot);
            go.transform.localPosition = new Vector3(0f, currentY + h * 0.5f, -0.1f);
            go.transform.localRotation = Quaternion.identity;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(r);
        }
        else
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"CargoProxy_{def.displayName}";
            go.transform.SetParent(carrierCargoRoot);
            go.transform.localPosition = new Vector3(0f, currentY + h * 0.5f, -0.1f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(Mathf.Max(0.01f, sz.x), h, Mathf.Max(0.01f, sz.z));
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            var r = go.GetComponent<Renderer>(); if (r && r.material) r.material.color = def.stackColor;
        }

        mounted.Add(world);
        return true;
    }

    public void UnloadTop(Vector3 dropPos, Vector3 forward)
    {
        if (mounted.Count == 0) return;

        var top = mounted[mounted.Count - 1];
        mounted.RemoveAt(mounted.Count - 1);

        if (carrierCargoRoot && carrierCargoRoot.childCount > 0)
        {
            var last = carrierCargoRoot.GetChild(carrierCargoRoot.childCount - 1);
            Destroy(last.gameObject);
        }

        top.OnDropped(dropPos, forward * 3f + Vector3.up * 1f);
    }

    /// <summary>
    /// Spill everything at a given origin (used when dropping the carrier itself).
    /// Applies forward impulse + explosion for nice scattering.
    /// </summary>
    public void SpillAllAt(Vector3 origin, Vector3 forward)
    {
        for (int i = 0; i < mounted.Count; i++)
        {
            var m = mounted[i];
            if (!m) continue;

            Vector3 pos = origin + UnityEngine.Random.insideUnitSphere * 0.2f + Vector3.up * 0.25f;
            Vector3 impulse = forward * 3f + Vector3.up * 1f;

            m.OnDropped(pos, impulse);

            if (!m.rb) m.rb = m.GetComponent<Rigidbody>();
            if (m.rb) m.rb.AddExplosionForce(spillExplosionForce, origin, spillExplosionRadius, 0.2f, ForceMode.Impulse);
        }

        mounted.Clear();

        if (carrierCargoRoot)
        {
            for (int i = carrierCargoRoot.childCount - 1; i >= 0; i--)
                Destroy(carrierCargoRoot.GetChild(i).gameObject);
        }

        tilt = 0f;
    }

    // tip-over path uses the back/shoulder origin
    private void SpillAll()
    {
        Vector3 origin = carrierCargoRoot ? carrierCargoRoot.position : transform.position + Vector3.up * 1.2f;
        SpillAllAt(origin, Vector3.zero);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (carrierCargoRoot)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(carrierCargoRoot.position + Vector3.up * 0.25f, new Vector3(0.4f, 0.5f, 0.3f));
        }
    }
#endif
}
