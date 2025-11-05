using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;
    public float jumpForce = 5f;
    public float gravity = -19.62f;

    [Header("Interaction")]
    public float interactDistance = 3f;
    public LayerMask interactMask;

    [Header("Third-Person Pickup Assist")]
    public bool tpAssistEnabled = true;

    [Tooltip("Sphere radius used to collect candidates around the camera forward ray (meters).")]
    [Range(0.1f, 2f)] public float tpAssistRadius = 0.6f;
    [Tooltip("Distance step between sphere samples along camera forward (meters).")]
    [Range(0.2f, 2.0f)] public float tpAssistStep = 1.0f;
    [Tooltip("How many sphere samples to take along the forward ray.")]
    [Range(1, 6)] public int tpAssistSamples = 3;
    [Tooltip("Max angle from camera forward to item direction (degrees).")]
    [Range(0f, 30f)] public float tpAlignAngleDeg = 12f;
    [Tooltip("Max viewport distance from screen center (0..~0.7). 0.2 ≈ near center.")]
    [Range(0f, 0.5f)] public float tpScreenMaxOffset = 0.22f;
    [Tooltip("Layers that can block line of sight. Usually Everything except Interactable.")]
    public LayerMask tpLosBlockMask = ~0;

    [Header("Z-Align Pickup (player forward)")]
    public bool zAlignPickupEnabled = true;
    [Tooltip("Apply Z-align assist only in third person.")]
    public bool zAlignOnlyInThird = true;
    [Tooltip("Max pickup distance along player forward (m).")]
    [Range(0.5f, 6f)] public float zAlignMaxDistance = 3.0f;
    [Tooltip("Max angle from player forward to item (deg).")]
    [Range(0f, 25f)] public float zAlignMaxAngleDeg = 10f;
    [Tooltip("Overlap sphere radius around the forward path (m).")]
    [Range(0.15f, 1.0f)] public float zAlignSphereRadius = 0.45f;
    [Tooltip("Spacing between sample spheres along forward (m).")]
    [Range(0.25f, 1.5f)] public float zAlignStep = 0.8f;
    [Tooltip("How many samples along forward.")]
    [Range(1, 6)] public int zAlignSamples = 3;
    [Tooltip("Layers that block line of sight from player to item.")]

    public LayerMask zAlignLosBlockMask = ~0; // set in Inspector (e.g., Everything except Interactable)

    [Header("Refs")]
    public CameraSwitcher cameraSwitcher;
    public InventorySystem inventory;
    public CarrierController carrier;
    public Transform dropOrigin;

    private CharacterController controller;
    private float verticalVel;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        Move();
        HandleHotbar();
        HandleActions();

        // While carrier mode is active, feed balance input each frame
        if (carrier && carrier.IsActive)
        {
            float left = Input.GetMouseButton(0) ? 1f : 0f;
            float right = Input.GetMouseButton(1) ? 1f : 0f;
            carrier.ApplyBalanceInput(left, right);
        }
    }

    void Move()
    {
        bool grounded = controller.isGrounded;

        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        Vector3 move = (transform.right * h + transform.forward * v).normalized;

        float speed = Input.GetKey(KeyCode.LeftShift) ? sprintSpeed : walkSpeed;
        controller.Move(move * speed * Time.deltaTime);

        if (grounded && verticalVel < 0f) verticalVel = -2f;
        if (Input.GetKeyDown(KeyCode.Space) && grounded) verticalVel = jumpForce;

        verticalVel += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVel * Time.deltaTime);
    }

    void HandleHotbar()
    {
        if (!inventory) return;
        if (Input.GetKeyDown(KeyCode.Alpha1)) inventory.SetActiveIndex(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) inventory.SetActiveIndex(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) inventory.SetActiveIndex(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) inventory.SetActiveIndex(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) inventory.SetActiveIndex(4);
    }

    void HandleActions()
    {
        // E: interact (pickup or mount)
        if (Input.GetKeyDown(KeyCode.E))
        {
            Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;

            WorldItem worldItem;
            if (TryFindWorldItemRay(cam, out worldItem) || TryFindWorldItemZAlign(out worldItem))
            {
                if (carrier && carrier.IsActive)
                {
                    if (worldItem.definition && worldItem.definition.isCarrier) return;
                    carrier.TryMount(worldItem);
                }
                else
                {
                    inventory?.TryPickupWorldItem(worldItem);
                }
            }
        }


        // G: ALWAYS drop. If carrier active → drop the carrier item and exit carrier mode in same press.
        if (Input.GetKeyDown(KeyCode.G))
        {
            Transform origin = dropOrigin ? dropOrigin : transform;
            Vector3 fwd = transform.forward;

            if (carrier && carrier.IsActive)
            {
                if (inventory != null)
                {
                    int carrierIdx = inventory.FindFirstCarrierIndex();
                    if (carrierIdx >= 0) inventory.SetActiveIndex(carrierIdx);

                    bool ok = inventory.DropActiveItem(origin, fwd);
                    carrier.SetActiveMode(false);
                    inventory.SelectFirstNonCarrierSlot();

                    if (!ok) Debug.LogWarning("[PlayerController] DropActiveItem failed for carrier.");
                }
            }
            else
            {
                inventory?.DropActiveItem(origin, fwd);
            }
        }

        // LMB: use active item only when not in carrier mode
        if (Input.GetMouseButtonDown(0) && !(carrier && carrier.IsActive))
        {
            inventory?.UseActiveItem(this);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Smart interact: raycast first; if 3rd-person and miss, do alignment-assisted pickup
    // ─────────────────────────────────────────────────────────────

    bool TryFindWorldItemSmart(Camera cam, out WorldItem worldItem)
    {
        worldItem = null;
        if (!cam) cam = Camera.main;
        if (!cam) return false;

        // 1) Straight ray from camera center
        if (Physics.Raycast(cam.transform.position, cam.transform.forward,
                            out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Ignore))
        {
            worldItem = hit.collider.GetComponentInParent<WorldItem>();
            if (worldItem) return true;
        }

        // 2) Third-person assist (alignment with camera Z axis)
        bool isThird = cameraSwitcher ? cameraSwitcher.IsThirdPerson() : true;
        if (!isThird || !tpAssistEnabled) return false;

        return TryAlignedAssistWorldItem(cam, out worldItem);
    }

    bool TryAlignedAssistWorldItem(Camera cam, out WorldItem best)
    {
        best = null;
        var camPos = cam.transform.position;
        var camFwd = cam.transform.forward;

        float maxDist = interactDistance;
        float cosThresh = Mathf.Cos(tpAlignAngleDeg * Mathf.Deg2Rad);
        float bestScore = float.PositiveInfinity;

        // sample spheres along the camera forward
        int samples = Mathf.Max(1, tpAssistSamples);
        float step = Mathf.Max(0.1f, tpAssistStep);

        for (int s = 1; s <= samples; s++)
        {
            float dist = Mathf.Min(maxDist, s * step);
            Vector3 center = camPos + camFwd * dist;

            Collider[] hits = Physics.OverlapSphere(center, tpAssistRadius, interactMask, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) continue;

            for (int i = 0; i < hits.Length; i++)
            {
                var wi = hits[i].GetComponentInParent<WorldItem>();
                if (!wi) continue;

                Vector3 itemPos = wi.transform.position;
                Vector3 toItem = itemPos - camPos;
                float d = toItem.magnitude;
                if (d > maxDist || d <= 0.001f) continue;

                Vector3 dir = toItem / d;
                float dot = Vector3.Dot(camFwd, dir);
                if (dot < cosThresh) continue; // not aligned enough with camera Z axis

                // screen-space proximity to center
                Vector3 vp = cam.WorldToViewportPoint(itemPos);
                if (vp.z <= 0f) continue; // behind camera
                float cx = vp.x - 0.5f;
                float cy = vp.y - 0.5f;
                float screenOffset = Mathf.Sqrt(cx * cx + cy * cy);
                if (screenOffset > tpScreenMaxOffset) continue;

                // LOS check: ensure nothing blocks between camera and item
                if (Physics.Linecast(camPos, itemPos, out RaycastHit blockHit, tpLosBlockMask, QueryTriggerInteraction.Ignore))
                {
                    if (!blockHit.transform || blockHit.transform.GetComponentInParent<WorldItem>() != wi)
                        continue;
                }

                // score: smaller angle + closer to screen center + closer distance is better
                // angle term from dot → angle ~ acos(dot), but we can use (1-dot) as proxy
                float angleScore = 1f - dot;
                float distScore = d / maxDist;
                float screenScore = screenOffset / Mathf.Max(0.001f, tpScreenMaxOffset);
                float score = angleScore * 2.0f + screenScore * 1.5f + distScore * 0.5f;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = wi;
                }
            }
        }

        return best != null;
    }
    // 1) plain center ray (kept simple)
    bool TryFindWorldItemRay(Camera cam, out WorldItem worldItem)
    {
        worldItem = null;
        if (!cam) cam = Camera.main;
        if (!cam) return false;

        if (Physics.Raycast(cam.transform.position, cam.transform.forward,
                            out RaycastHit hit, interactDistance, interactMask, QueryTriggerInteraction.Ignore))
        {
            worldItem = hit.collider.GetComponentInParent<WorldItem>();
            return worldItem != null;
        }
        return false;
    }

    // 2) Z-align assist: items near the player forward axis, within distance/angle limits
    bool TryFindWorldItemZAlign(out WorldItem best)
    {
        best = null;

        if (!zAlignPickupEnabled) return false;
        if (zAlignOnlyInThird && !(cameraSwitcher && cameraSwitcher.IsThirdPerson())) return false;

        // Origin at chest height for better LOS
        Vector3 origin = transform.position + Vector3.up * 1.4f;
        Vector3 fwd = transform.forward;

        float maxDist = zAlignMaxDistance;
        float cosThresh = Mathf.Cos(zAlignMaxAngleDeg * Mathf.Deg2Rad);
        float bestScore = float.PositiveInfinity;

        int samples = Mathf.Max(1, zAlignSamples);
        float step = Mathf.Max(0.1f, zAlignStep);

        for (int s = 1; s <= samples; s++)
        {
            float dist = Mathf.Min(maxDist, s * step);
            Vector3 center = origin + fwd * dist;

            Collider[] hits = Physics.OverlapSphere(
                center, zAlignSphereRadius, interactMask, QueryTriggerInteraction.Ignore
            );
            if (hits == null || hits.Length == 0) continue;

            for (int i = 0; i < hits.Length; i++)
            {
                var wi = hits[i].GetComponentInParent<WorldItem>();
                if (!wi) continue;

                Vector3 itemPos = wi.transform.position;
                Vector3 toItem = itemPos - origin;
                float d = toItem.magnitude;
                if (d <= 0.001f || d > maxDist) continue;

                Vector3 dir = toItem / d;
                float dot = Vector3.Dot(fwd, dir);
                if (dot < cosThresh) continue; // not aligned enough with player forward (Z axis)

                // line-of-sight check from player chest to item
                if (Physics.Linecast(origin, itemPos, out RaycastHit block, zAlignLosBlockMask, QueryTriggerInteraction.Ignore))
                {
                    if (!block.transform || block.transform.GetComponentInParent<WorldItem>() != wi)
                        continue; // blocked
                }

                // score by angle (1-dot) and distance (closer is slightly preferred)
                float angleScore = 1f - dot;         // 0 = perfect alignment
                float distScore = d / maxDist;       // 0 = close
                float score = angleScore * 2.0f + distScore * 0.5f;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = wi;
                }
            }
        }

        return best != null;
    }
    // Find the same candidate WorldItem that E-key would use (ray OR Z-align assist)
    public bool FindInteractCandidate(out WorldItem item)
    {
        Camera cam = cameraSwitcher ? cameraSwitcher.GetActiveCamera() : Camera.main;
        return TryFindWorldItemRay(cam, out item) || TryFindWorldItemZAlign(out item);
    }


}
