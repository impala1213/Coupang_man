// Assets/Scripts/Item/WorldItem.cs
using UnityEngine;

[DisallowMultipleComponent]
public class WorldItem : MonoBehaviour
{
    public ItemDefinition definition;
    [HideInInspector] public Rigidbody rb;

    private Collider[] _colliders;
    private Renderer[] _renderers;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>(true);
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>
    /// 인벤토리 픽업: destroyInstance=true → 원본 파괴
    /// 캐리어 적재:   destroyInstance=false → 비주얼/콜라이더 숨김(나중에 재사용)
    /// </summary>
    public void OnPickedUp(bool destroyInstance)
    {
        if (destroyInstance)
        {
            Destroy(gameObject);
            return;
        }

        if (rb)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        if (_colliders != null) foreach (var c in _colliders) if (c) c.enabled = false;
        if (_renderers != null) foreach (var r in _renderers) if (r) r.enabled = false;
    }

    /// <summary>캐리어 경로에서 같은 인스턴스를 다시 월드에 되살릴 때 사용.</summary>
    public void OnDropped(Vector3 worldPos, Vector3 initialVelocity)
    {
        transform.position = worldPos;

        if (!rb) rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearVelocity = initialVelocity;

        if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
        foreach (var c in _colliders) if (c) c.enabled = true;

        if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers) if (r) r.enabled = true;

        // 깔끔한 이름
        if (definition && definition.worldPrefab)
            name = definition.worldPrefab.name;
    }

    /// <summary>Durability 스냅샷 읽기. 없으면 false.</summary>
    public bool TryGetDurability(out int current, out int max)
    {
        current = 0; max = 0;
        var d = GetComponent<Durability>();
        if (!d) return false;
        current = d.current;
        max = d.max;
        return true;
    }

    /// <summary>드롭으로 새로 생성된 프리팹에 내구도 반영.</summary>
    public void ApplyDurability(int current, int max, bool clamp = true)
    {
        var d = GetComponent<Durability>();
        if (!d) d = gameObject.AddComponent<Durability>();

        if (max > 0) d.max = max;
        if (clamp) current = Mathf.Clamp(current, 0, d.max);
        d.current = current;
    }
}
