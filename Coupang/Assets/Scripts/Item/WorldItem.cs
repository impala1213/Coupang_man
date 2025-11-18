// Assets/Scripts/Item/WorldItem.cs
using UnityEngine;

[DisallowMultipleComponent]
public class WorldItem : MonoBehaviour
{
    public ItemDefinition definition;
    [HideInInspector] public Rigidbody rb;

    private Collider[] _colliders;
    private Renderer[] _renderers;

    // ── Carrier 메타 정보 ─────────────────────────────
    [HideInInspector] public bool isOnCarrier;
    [HideInInspector] public CarrierController carrierOwner;
    [HideInInspector] public int carrierSlotIndex = -1;
    [HideInInspector] public Transform carrierSlotPivot;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>(true);
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>
    /// 인벤토리 픽업: destroyInstance=true → 원본 파괴
    /// 캐리어 적재:   지금은 사용하지 않음(EnterCarrierMountMode 사용).
    /// </summary>
    public void OnPickedUp(bool destroyInstance)
    {
        if (destroyInstance)
        {
            Destroy(gameObject);
            return;
        }

        // 옛날 캐리어 숨김 방식 유지(호환용).
        if (rb)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        if (_colliders != null) foreach (var c in _colliders) if (c) c.enabled = false;
        if (_renderers != null) foreach (var r in _renderers) if (r) r.enabled = false;
    }

    /// <summary>
    /// 캐리어에 실릴 때 호출. CarrierController.TryMount에서 사용.
    /// 지게 슬롯 피벗 아래로 붙이고, 콜라이더는 꺼 둠(지게 전체 콜라이더만 사용).
    /// </summary>
    public void EnterCarrierMountMode(CarrierController carrier, int slotIndex, Transform slotPivot)
    {
        carrierOwner = carrier;
        carrierSlotIndex = slotIndex;
        carrierSlotPivot = slotPivot;
        isOnCarrier = true;

        if (!rb) rb = GetComponent<Rigidbody>();
        if (rb)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
            rb.velocity = Vector3.zero;
#endif
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
            rb.useGravity = false;
        }

        if (_colliders == null) _colliders = GetComponentsInChildren<Collider>(true);
        // 적재 상태에서는 짐 콜라이더는 비활성화(지게 전체 콜라이더로 충돌 처리)
        foreach (var c in _colliders) if (c) c.enabled = false;

        if (_renderers == null) _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in _renderers) if (r) r.enabled = true;

        // 슬롯 피벗 아래로 붙이기
        transform.SetParent(slotPivot, false);

        if (definition != null)
        {
            Vector3 pos = definition.carrierLocalPosition;
            Vector3 euler = definition.carrierLocalEuler;
            Vector3 scale = definition.carrierLocalScale;
            if (scale == Vector3.zero) scale = Vector3.one;

            transform.localPosition = pos;
            transform.localEulerAngles = euler;
            transform.localScale = scale;
        }
        else
        {
            transform.localPosition = Vector3.zero;
            transform.localEulerAngles = Vector3.zero;
        }
    }

    /// <summary>
    /// 캐리어 경로에서 같은 인스턴스를 다시 월드에 되살릴 때 사용.
    /// (지게 슬롯에서 탈출 + 물리 복구)
    /// </summary>
    public void OnDropped(Vector3 worldPos, Vector3 initialVelocity)
    {
        // 캐리어에 실려 있었으면 탈출 처리
        if (isOnCarrier)
        {
            transform.SetParent(null, true);
            isOnCarrier = false;
            carrierOwner = null;
            carrierSlotIndex = -1;
            carrierSlotPivot = null;
        }

        transform.position = worldPos;

        if (!rb) rb = GetComponent<Rigidbody>();
        if (!rb) rb = gameObject.AddComponent<Rigidbody>();

        rb.isKinematic = false;
        rb.useGravity = true;
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = initialVelocity;
#else
        rb.velocity = initialVelocity;
#endif

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
