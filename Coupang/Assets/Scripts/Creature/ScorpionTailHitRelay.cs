using UnityEngine;

public class ScorpionTailHitRelay : MonoBehaviour
{
    private ScorpionTail tail;

    void Awake()
    {
        tail = GetComponentInParent<ScorpionTail>();
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    
    public void ApplyDamage(int damage)
    {
        if (tail == null) return;
        tail.ApplyDamage(damage);
    }
}
