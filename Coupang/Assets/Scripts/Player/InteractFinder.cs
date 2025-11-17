using UnityEngine;

public static class InteractFinder
{
    const int MaxRayHits = 16;
    const int MaxOverlap = 48;

    static readonly RaycastHit[] _rayHits = new RaycastHit[MaxRayHits];
    static readonly Collider[] _overlap = new Collider[MaxOverlap];

    /// <summary>
    /// Consistent pickup candidate search using first-person camera:
    /// 1) Thin ray (precise). If it hits any WorldItem, return immediately (carrier allowed).
    /// 2) If miss, forgiving sphere-cast + overlap samples, optionally excluding carriers.
    /// </summary>
    public static bool FindCandidate(
        Camera rayCam,                 // use FIRST-PERSON camera transform
        Transform player,              // for LOS origin fallback
        float maxDistance,             // max pickup distance
        float rayRadius,               // forgiving radius for step 2 (0.3~0.6 typical)
        LayerMask interactMask,        // Interactable layers
        LayerMask losBlockMask,        // blockers (exclude Interactable & Player before passing)
        bool excludeCarrierInAutoSelect, // true: skip carrier in step 2 (forgiving)
        out WorldItem best)
    {
        best = null;
        if (!rayCam || !player) return false;

        Vector3 origin = rayCam.transform.position;
        Vector3 dir = rayCam.transform.forward;

        // 式式 Step 1: precise thin ray (carrier allowed)
        int rCount = Physics.RaycastNonAlloc(
            origin, dir, _rayHits, maxDistance,
            interactMask, QueryTriggerInteraction.Collide
        );
        for (int i = 0; i < rCount; i++)
        {
            var col = _rayHits[i].collider;
            if (!col) continue;

            var wi = col.GetComponentInParent<WorldItem>();
            if (!wi) continue;

            // LOS check (thin ray already but keep consistency)
            if (Physics.Linecast(origin, wi.transform.position, out RaycastHit block, losBlockMask, QueryTriggerInteraction.Collide))
            {
                var hitWi = block.transform ? block.transform.GetComponentInParent<WorldItem>() : null;
                if (hitWi != wi) continue; // blocked
            }

            best = wi;
            return true; // direct aim wins, even for carrier
        }

        // 式式 Step 2: forgiving sphere-cast (carrier can be excluded)
        int sCount = Physics.SphereCastNonAlloc(
            origin, rayRadius, dir, _rayHits, maxDistance,
            interactMask, QueryTriggerInteraction.Collide
        );

        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < sCount; i++)
        {
            var h = _rayHits[i];
            var wi = h.collider ? h.collider.GetComponentInParent<WorldItem>() : null;
            if (!wi) continue;

            if (excludeCarrierInAutoSelect && wi.definition && wi.definition.isCarrier)
                continue; // skip carrier in forgiving phase

            if (Physics.Linecast(origin, wi.transform.position, out RaycastHit block, losBlockMask, QueryTriggerInteraction.Collide))
            {
                var hitWi = block.transform ? block.transform.GetComponentInParent<WorldItem>() : null;
                if (hitWi != wi) continue;
            }

            float distAlong = h.distance;
            float dist3D = Vector3.Distance(origin, wi.transform.position);
            float score = distAlong * 0.7f + dist3D * 0.3f;

            if (score < bestScore)
            {
                bestScore = score;
                best = wi;
            }
        }
        if (best) return true;

        // 式式 Step 2b: overlap samples along the ray (for odd shapes)
        const int samples = 4;
        float step = maxDistance / samples;
        float bestScore2 = float.PositiveInfinity;

        for (int s = 1; s <= samples; s++)
        {
            Vector3 center = origin + dir * (s * step);
            int cnt = Physics.OverlapSphereNonAlloc(center, rayRadius, _overlap, interactMask, QueryTriggerInteraction.Collide);
            if (cnt == 0) continue;

            for (int i = 0; i < cnt; i++)
            {
                var col = _overlap[i];
                if (!col) continue;

                var wi = col.GetComponentInParent<WorldItem>();
                if (!wi) continue;

                if (excludeCarrierInAutoSelect && wi.definition && wi.definition.isCarrier)
                    continue; // skip carrier in forgiving phase

                if (Physics.Linecast(origin, wi.transform.position, out RaycastHit block, losBlockMask, QueryTriggerInteraction.Collide))
                {
                    var hitWi = block.transform ? block.transform.GetComponentInParent<WorldItem>() : null;
                    if (hitWi != wi) continue;
                }

                Vector3 p = wi.transform.position;
                float dR = DistancePointToRay(p, origin, dir);            // distance to ray
                float dC = Vector3.Distance(origin, p);                   // distance to cam
                float sc = dR * 2.0f + dC * 0.5f;

                if (sc < bestScore2)
                {
                    bestScore2 = sc;
                    best = wi;
                }
            }
        }

        return best != null;
    }

    static float DistancePointToRay(Vector3 point, Vector3 rayOrigin, Vector3 rayDirNormalized)
    {
        Vector3 toP = point - rayOrigin;
        float t = Mathf.Max(0f, Vector3.Dot(toP, rayDirNormalized));
        Vector3 proj = rayOrigin + rayDirNormalized * t;
        return Vector3.Distance(point, proj);
    }
}
