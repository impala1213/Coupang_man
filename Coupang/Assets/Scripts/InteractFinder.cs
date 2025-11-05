using UnityEngine;

public static class InteractFinder
{
    const int MaxRayHits = 4;
    const int MaxOverlap = 32;
    static readonly RaycastHit[] _rayHits = new RaycastHit[MaxRayHits];
    static readonly Collider[] _overlap = new Collider[MaxOverlap];

    /// <summary>
    /// Find a WorldItem candidate consistently: center ray first, then Z-align assist along player forward.
    /// </summary>
    public static bool FindCandidate(
        Camera cam,
        Transform player,
        float maxDistance,
        LayerMask interactMask,      // Interactable layer
        LayerMask losBlockMask,      // blockers (should exclude Interactable & Player)
        bool enableZAlign,
        bool onlyInThirdPerson,
        bool isThirdPerson,
        float zAlignAngleDeg,
        float zAlignStep,
        int zAlignSamples,
        float zAlignRadius,
        out WorldItem best)
    {
        best = null;
        if (!cam || !player) return false;

        // 1) center ray (NonAlloc)
        int rh = Physics.RaycastNonAlloc(
            cam.transform.position, cam.transform.forward,
            _rayHits, maxDistance, interactMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < rh; i++)
        {
            var wi = _rayHits[i].collider ? _rayHits[i].collider.GetComponentInParent<WorldItem>() : null;
            if (wi) { best = wi; return true; }
        }

        // 2) Z-align assist (player forward)
        if (!enableZAlign) return false;
        if (onlyInThirdPerson && !isThirdPerson) return false;

        Vector3 origin = player.position + Vector3.up * 1.4f; // chest-ish
        Vector3 fwd = player.forward;
        float cosThresh = Mathf.Cos(zAlignAngleDeg * Mathf.Deg2Rad);

        float bestScore = float.PositiveInfinity;
        int samples = Mathf.Max(1, zAlignSamples);
        float step = Mathf.Max(0.1f, zAlignStep);

        for (int s = 1; s <= samples; s++)
        {
            float d = Mathf.Min(maxDistance, s * step);
            Vector3 c = origin + fwd * d;

            int cnt = Physics.OverlapSphereNonAlloc(c, zAlignRadius, _overlap, interactMask, QueryTriggerInteraction.Ignore);
            if (cnt == 0) continue;

            for (int i = 0; i < cnt; i++)
            {
                var col = _overlap[i];
                if (!col) continue;

                var wi = col.GetComponentInParent<WorldItem>();
                if (!wi) continue;

                Vector3 itemPos = wi.transform.position;
                Vector3 toItem = itemPos - origin;
                float dist = toItem.magnitude;
                if (dist <= 0.001f || dist > maxDistance) continue;

                Vector3 dir = toItem / dist;
                float dot = Vector3.Dot(fwd, dir);
                if (dot < cosThresh) continue; // not aligned enough with player forward

                // Line of sight check
                if (Physics.Linecast(origin, itemPos, out RaycastHit block, losBlockMask, QueryTriggerInteraction.Ignore))
                {
                    var hitWi = block.transform ? block.transform.GetComponentInParent<WorldItem>() : null;
                    if (hitWi != wi) continue; // blocked by something else
                }

                // score: prefer alignment, then closeness
                float angleScore = 1f - dot;        // 0 = perfectly aligned
                float distScore = dist / maxDistance;
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
}
