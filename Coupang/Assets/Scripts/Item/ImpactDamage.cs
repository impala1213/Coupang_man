// Assets/Scripts/Item/ImpactDamage.cs
using UnityEngine;

[DisallowMultipleComponent]
public class ImpactDamage : MonoBehaviour
{
    [Header("Damage by collision")]
    public float minSpeed = 6f;         // 이 속도 이상부터 데미지 시작
    public float damagePerUnit = 5f;    // (상대속도 - minSpeed) * 이 값

    private Durability dur;

    void Awake() { dur = GetComponent<Durability>(); }

    void OnCollisionEnter(Collision col)
    {
        if (!dur) return;
        float speed = col.relativeVelocity.magnitude;
        if (speed < minSpeed) return;

        int dmg = Mathf.RoundToInt((speed - minSpeed) * damagePerUnit);
        dur.ApplyDamage(dmg);
    }
}
