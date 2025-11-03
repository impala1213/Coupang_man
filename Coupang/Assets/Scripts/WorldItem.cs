using UnityEngine;


[RequireComponent(typeof(Collider))]
public class WorldItem : MonoBehaviour
{
    public ItemDefinition definition;
    public Rigidbody rb;


    void Reset()
    {
        rb = GetComponent<Rigidbody>();
        var col = GetComponent<Collider>();
        if (col) col.tag = "WorldItem";
    }


    public void OnPickedUp()
    {
        // ���� ������Ʈ�� ��Ȱ��ȭ (�κ��丮/���Է� �̵�)
        gameObject.SetActive(false);
    }


    public void OnDropped(Vector3 position, Vector3 impulse)
    {
        if (definition.worldPrefab != null && definition.worldPrefab != gameObject)
        {
            // ���ǵ� ���� �������� ���� ������ �װ��� ����
            Instantiate(definition.worldPrefab, position, Quaternion.identity);
            Destroy(gameObject);
            return;
        }


        transform.position = position;
        transform.rotation = Quaternion.identity;
        gameObject.SetActive(true);
        if (!rb) rb = GetComponent<Rigidbody>();
        if (rb)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero;
            rb.AddForce(impulse, ForceMode.VelocityChange);
        }
    }
}