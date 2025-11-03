using UnityEngine;


[RequireComponent(typeof(Collider))]
public class BreakableItem : MonoBehaviour
{
    public ItemDefinition definition; // breakable=true, 임계값/파편 프리팹 포함


    void OnCollisionEnter(Collision c)
    {
        if (definition == null || !definition.breakable) return;
        float impulse = c.relativeVelocity.magnitude * (GetComponent<Rigidbody>() ? GetComponent<Rigidbody>().mass : 1f);
        if (impulse >= definition.breakImpulseThreshold)
        {
            Break();
        }
    }


    void Break()
    {
        if (definition.brokenPrefab)
        {
            Instantiate(definition.brokenPrefab, transform.position, transform.rotation);
            Destroy(gameObject);
        }
        else
        {
            // 간단히 비활성화로 대체
            gameObject.SetActive(false);
        }
    }
}