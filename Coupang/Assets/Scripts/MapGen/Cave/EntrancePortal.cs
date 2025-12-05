using UnityEngine;

[RequireComponent(typeof(Collider))]
public class EntrancePortal : MonoBehaviour
{
    [Header("Interaction")]
    public KeyCode interactKey = KeyCode.E;
    public float holdDuration = 1.5f;

    [Tooltip("Optional: if null, the player will be found by tag 'Player'.")]
    public Transform playerOverride;

    [Header("Teleport Target")]
    [Tooltip("If useTargetTransform is true, targetTransform is used. Otherwise targetPosition is used.")]
    public bool useTargetTransform = false;
    public Transform targetTransform;
    public Vector3 targetPosition;
    public float targetYOffset = 1.0f;

    [Header("Rotation")]
    [Tooltip("If true, player rotation will be set to this transform's rotation.")]
    public bool alignPlayerRotation = false;
    public Transform lookDirection;

    private Transform _player;
    private bool _playerInside;
    private float _holdTimer;

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void Start()
    {
        if (playerOverride != null)
        {
            _player = playerOverride;
        }
        else
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                _player = playerObj.transform;
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_player == null)
            return;

        if (other.transform == _player || other.CompareTag("Player"))
        {
            _playerInside = true;
            _holdTimer = 0f;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (_player == null)
            return;

        if (other.transform == _player || other.CompareTag("Player"))
        {
            _playerInside = false;
            _holdTimer = 0f;
        }
    }

    private void Update()
    {
        if (_player == null || !_playerInside)
            return;

        if (Input.GetKey(interactKey))
        {
            _holdTimer += Time.deltaTime;
            if (_holdTimer >= holdDuration)
            {
                TeleportPlayer();
                _holdTimer = 0f;
            }
        }
        else
        {
            if (_holdTimer > 0f)
                _holdTimer = Mathf.Max(0f, _holdTimer - Time.deltaTime);
        }
    }

    private void TeleportPlayer()
    {
        if (_player == null)
            return;

        Vector3 dest;
        if (useTargetTransform && targetTransform != null)
        {
            dest = targetTransform.position;
        }
        else
        {
            dest = targetPosition;
        }

        dest += Vector3.up * targetYOffset;

        CharacterController cc = _player.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            _player.position = dest;
            cc.enabled = true;
        }
        else
        {
            _player.position = dest;
        }

        if (alignPlayerRotation)
        {
            if (lookDirection != null)
            {
                _player.rotation = Quaternion.Euler(0f, lookDirection.eulerAngles.y, 0f);
            }
            else
            {
                _player.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            }
        }
    }
}
