using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a modular cave interior by stitching together pre-authored room/corridor/cap prefabs.
///
/// Authoring expectations:
/// - Each prefab should have CaveConnector components on child transforms.
/// - Connector transforms' +Z axis (Transform.forward, blue arrow) should point OUTWARD from the piece.
/// - Two connectors connect when their connectorTag matches.
/// - Corridors may have 2+ connectors (junctions). After attaching, ALL unused connectors (except the inbound) will be enqueued.
/// - Caps should have 1 connector (wall).
/// </summary>
public static class CaveDungeonGenerator
{
    public struct Result
    {
        public CaveInstance instance;
        public Transform root;
        public Transform startPieceRoot;
        public Transform insideEntryPoint;
    }

    private enum NodeKind
    {
        Room = 0,
        Corridor = 1,
    }

    private struct OpenSocket
    {
        public CaveConnector connector;
        public NodeKind kind;
        public int corridorChainDepth;

        public OpenSocket(CaveConnector c, NodeKind k, int depth)
        {
            connector = c;
            kind = k;
            corridorChainDepth = depth;
        }
    }

    private struct OccupiedEntry
    {
        public Bounds localBounds;
        public Transform pieceRoot;

        public OccupiedEntry(Bounds b, Transform root)
        {
            localBounds = b;
            pieceRoot = root;
        }
    }

    public static Result Generate(
        CaveFeatureDefinition def,
        Rng rng,
        Vector3 interiorOrigin,
        Transform parent,
        string instanceName = "Cave")
    {
        Result res = new Result();

        if (def == null)
            return res;

        if (rng == null)
            rng = new Rng(12345);

        GameObject rootGO = new GameObject(instanceName);
        rootGO.transform.SetParent(parent, true);
        rootGO.transform.position = interiorOrigin;
        rootGO.transform.rotation = Quaternion.identity;

        CaveInstance inst = rootGO.AddComponent<CaveInstance>();

        // Occupancy for overlap avoidance
        List<OccupiedEntry> occupied = new List<OccupiedEntry>(64);

        // Open connectors to expand from
        Queue<OpenSocket> open = new Queue<OpenSocket>(64);

        // 1) Spawn start piece (optionally uses exitPrefab as the start)
        bool startedFromExitPrefab = def.useExitPrefabAsStartPiece && def.exitPrefab != null;

        // If the exitPrefab has no connectors, fall back to a normal room start.
        if (startedFromExitPrefab)
        {
            CaveConnector[] exitConns = def.exitPrefab.GetComponentsInChildren<CaveConnector>(true);
            if (exitConns == null || exitConns.Length == 0)
            {
                Debug.LogWarning("[CaveDungeonGenerator] exitPrefab is set as start piece, but it has no CaveConnector children. Falling back to random room start.");
                startedFromExitPrefab = false;
            }
        }

        GameObject startPrefab = startedFromExitPrefab ? def.exitPrefab : PickWeighted(def.rooms, rng);
        if (startPrefab == null)
        {
            Debug.LogWarning("[CaveDungeonGenerator] No valid start prefab assigned (exitPrefab or rooms).");
            Object.Destroy(rootGO);
            return res;
        }

        GameObject startPiece = Object.Instantiate(startPrefab, rootGO.transform);
        startPiece.name = startedFromExitPrefab ? "ExitStart_0" : "Room_0";
        startPiece.transform.localPosition = Vector3.zero;
        startPiece.transform.localRotation = Quaternion.identity;

        res.startPieceRoot = startPiece.transform;

        // Entry point: if a child named "EntryPoint" exists, use it.
        Transform entry = startPiece.transform.Find("EntryPoint");
        if (entry == null)
        {
            GameObject ep = new GameObject("EntryPoint");
            ep.transform.SetParent(startPiece.transform, false);
            ep.transform.localPosition = Vector3.zero;
            ep.transform.localRotation = Quaternion.identity;
            entry = ep.transform;
        }

        inst.insideEntryPoint = entry;

        // Add start piece bounds
        if (def.preventOverlaps)
        {
            if (TryComputeLocalBounds(rootGO.transform, startPiece, out Bounds b0))
            {
                NormalizeBounds(ref b0, def);
                occupied.Add(new OccupiedEntry(b0, startPiece.transform));
            }
        }

        EnqueuePieceConnectors(startPiece, open, NodeKind.Room, 0);

        int roomsPlaced = 1;

        // 2) Expand
        int safetyIterations = 100000;
        int iter = 0;

        while (open.Count > 0 && iter++ < safetyIterations)
        {
            OpenSocket s = open.Dequeue();
            CaveConnector from = s.connector;
            if (from == null)
                continue;

            if (from.isUsed)
                continue;

            if (!from.enabledForGeneration)
                continue;

            // If we've hit max rooms, only allow corridor continuation that doesn't create new rooms,
            // otherwise cap.
            if (roomsPlaced >= Mathf.Max(1, def.maxRooms) && s.kind != NodeKind.Corridor)
            {
                PlaceCap(def, rng.Split(91000 + iter), rootGO.transform, from, occupied);
                continue;
            }

            if (s.kind == NodeKind.Room)
            {
                // Start corridor chain from a room connector
                if (rng.NextFloat(0f, 1f) > Mathf.Clamp01(def.roomToCorridorProbability))
                {
                    PlaceCap(def, rng.Split(92000 + iter), rootGO.transform, from, occupied);
                    continue;
                }

                if (!TryAttachCorridor(def, rng.Split(10000 + iter), rootGO.transform, from, occupied,
                        out List<CaveConnector> outs, out Transform corridorRoot))
                {
                    PlaceCap(def, rng.Split(93000 + iter), rootGO.transform, from, occupied);
                    continue;
                }

                // ★ Junction support: enqueue ALL remaining connectors as corridor sockets
                if (outs != null)
                {
                    for (int k = 0; k < outs.Count; k++)
                    {
                        CaveConnector oc = outs[k];
                        if (oc != null && !oc.isUsed && oc.enabledForGeneration)
                            open.Enqueue(new OpenSocket(oc, NodeKind.Corridor, 1));
                    }
                }

                continue;
            }

            // Corridor socket: decide continue corridor vs spawn room vs cap
            float contP = Mathf.Clamp01(def.corridorContinueProbability);
            float toRoomP = Mathf.Clamp01(def.corridorToRoomProbability);
            if (contP + toRoomP > 1f)
            {
                float sum = contP + toRoomP;
                contP /= sum;
                toRoomP /= sum;
            }

            float roll = rng.NextFloat(0f, 1f);
            bool canChain = s.corridorChainDepth < Mathf.Max(0, def.maxCorridorChain);

            if (canChain && roll < contP)
            {
                if (!TryAttachCorridor(def, rng.Split(20000 + iter), rootGO.transform, from, occupied,
                        out List<CaveConnector> outs, out Transform corridorRoot))
                {
                    PlaceCap(def, rng.Split(94000 + iter), rootGO.transform, from, occupied);
                    continue;
                }

                // ★ Enqueue all branches; each branch continues with nextDepth
                if (outs != null)
                {
                    int nextDepth = s.corridorChainDepth + 1;
                    for (int k = 0; k < outs.Count; k++)
                    {
                        CaveConnector oc = outs[k];
                        if (oc != null && !oc.isUsed && oc.enabledForGeneration)
                            open.Enqueue(new OpenSocket(oc, NodeKind.Corridor, nextDepth));
                    }
                }

                continue;
            }

            // Try spawn a room at the end of corridor
            if (roll < contP + toRoomP && roomsPlaced < Mathf.Max(1, def.maxRooms))
            {
                if (!TryAttachRoom(def, rng.Split(30000 + iter), rootGO.transform, from, occupied, out GameObject roomGO))
                {
                    PlaceCap(def, rng.Split(95000 + iter), rootGO.transform, from, occupied);
                    continue;
                }

                roomsPlaced++;
                EnqueuePieceConnectors(roomGO, open, NodeKind.Room, 0);
                continue;
            }

            // Otherwise, cap
            PlaceCap(def, rng.Split(96000 + iter), rootGO.transform, from, occupied);
        }

        // 3) Cap leftover connectors
        if (def.capUnusedConnectors)
        {
            CaveConnector[] all = rootGO.GetComponentsInChildren<CaveConnector>(true);
            for (int i = 0; i < all.Length; i++)
            {
                CaveConnector c = all[i];
                if (c == null) continue;
                if (c.isUsed) continue;
                if (!c.enabledForGeneration) continue;
                if (c.allowOpenEnd) continue;

                PlaceCap(def, rng.Split(97000 + i), rootGO.transform, c, occupied);
            }
        }

        res.instance = inst;
        res.root = rootGO.transform;
        res.insideEntryPoint = inst.insideEntryPoint;
        return res;
    }

    private static void EnqueuePieceConnectors(GameObject piece, Queue<OpenSocket> open, NodeKind kind, int corridorDepth)
    {
        if (piece == null)
            return;

        CaveConnector[] connectors = piece.GetComponentsInChildren<CaveConnector>(true);
        if (connectors == null)
            return;

        for (int i = 0; i < connectors.Length; i++)
        {
            CaveConnector c = connectors[i];
            if (c == null) continue;
            if (c.isUsed) continue;
            if (!c.enabledForGeneration) continue;

            open.Enqueue(new OpenSocket(c, kind, corridorDepth));
        }
    }

    private static GameObject PickWeighted(CaveFeatureDefinition.WeightedPrefab[] list, Rng rng)
    {
        if (list == null || list.Length == 0 || rng == null)
            return null;

        float total = 0f;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i].prefab == null)
                continue;

            total += Mathf.Max(0f, list[i].weight);
        }

        if (total <= 0f)
        {
            // Fallback: first valid
            for (int i = 0; i < list.Length; i++)
                if (list[i].prefab != null)
                    return list[i].prefab;

            return null;
        }

        float pick = rng.NextFloat(0f, total);
        float accum = 0f;

        for (int i = 0; i < list.Length; i++)
        {
            GameObject p = list[i].prefab;
            if (p == null) continue;

            float w = Mathf.Max(0f, list[i].weight);
            accum += w;
            if (pick <= accum)
                return p;
        }

        for (int i = 0; i < list.Length; i++)
            if (list[i].prefab != null)
                return list[i].prefab;

        return null;
    }

    private static bool TryAttachCorridor(
        CaveFeatureDefinition def,
        Rng rng,
        Transform dungeonRoot,
        CaveConnector from,
        List<OccupiedEntry> occupied,
        out List<CaveConnector> outConnectors,
        out Transform corridorRoot)
    {
        outConnectors = null;
        corridorRoot = null;

        if (def == null || rng == null || dungeonRoot == null || from == null)
            return false;

        int tries = Mathf.Max(1, def.placementRetriesPerConnector);

        for (int attempt = 0; attempt < tries; attempt++)
        {
            GameObject corridorPrefab = PickWeighted(def.corridors, rng.Split(1100 + attempt * 17));
            if (corridorPrefab == null)
                return false;

            GameObject corridor = Object.Instantiate(corridorPrefab, dungeonRoot);
            corridor.name = $"Corridor_Attempt_{attempt}";

            // Pick inbound connector from tag-matching candidates (corridor can have 2+ connectors)
            CaveConnector inConn;
            if (!TryPickRoomConnector(corridor, from.connectorTag, rng.Split(2100 + attempt * 19), out inConn))
            {
                Object.Destroy(corridor);
                continue;
            }

            AlignConnectorTo(inConn.transform, corridor.transform, from.transform);

            if (def.preventOverlaps)
            {
                if (WouldOverlapOrMeet(def, dungeonRoot, corridor, from, occupied))
                {
                    Object.Destroy(corridor);
                    continue;
                }
            }

            // Mark used endpoints (connection)
            from.MarkUsed();
            inConn.MarkUsed();

            if (def.preventOverlaps)
            {
                if (TryComputeLocalBounds(dungeonRoot, corridor, out Bounds b))
                {
                    NormalizeBounds(ref b, def);
                    occupied.Add(new OccupiedEntry(b, corridor.transform));
                }
            }

            // ★ Junction support: return ALL unused connectors except inbound
            CaveConnector[] all = corridor.GetComponentsInChildren<CaveConnector>(true);
            List<CaveConnector> outs = new List<CaveConnector>(all != null ? all.Length : 0);

            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    CaveConnector c = all[i];
                    if (c == null) continue;
                    if (c == inConn) continue;
                    if (!c.enabledForGeneration) continue;
                    if (c.isUsed) continue;

                    outs.Add(c);
                }
            }

            outConnectors = outs;
            corridorRoot = corridor.transform;
            return true;
        }

        return false;
    }

    private static bool TryAttachRoom(
        CaveFeatureDefinition def,
        Rng rng,
        Transform dungeonRoot,
        CaveConnector from,
        List<OccupiedEntry> occupied,
        out GameObject roomGO)
    {
        roomGO = null;

        if (def == null || rng == null || dungeonRoot == null || from == null)
            return false;

        int tries = Mathf.Max(1, def.placementRetriesPerConnector);

        for (int attempt = 0; attempt < tries; attempt++)
        {
            GameObject roomPrefab = PickWeighted(def.rooms, rng.Split(3100 + attempt * 23));
            if (roomPrefab == null)
                return false;

            GameObject room = Object.Instantiate(roomPrefab, dungeonRoot);
            room.name = $"Room_Attempt_{attempt}";

            CaveConnector inConn;
            if (!TryPickRoomConnector(room, from.connectorTag, rng.Split(4100 + attempt * 29), out inConn))
            {
                Object.Destroy(room);
                continue;
            }

            AlignConnectorTo(inConn.transform, room.transform, from.transform);

            if (def.preventOverlaps)
            {
                if (WouldOverlapOrMeet(def, dungeonRoot, room, from, occupied))
                {
                    Object.Destroy(room);
                    continue;
                }
            }

            from.MarkUsed();
            inConn.MarkUsed();

            if (def.preventOverlaps)
            {
                if (TryComputeLocalBounds(dungeonRoot, room, out Bounds b))
                {
                    NormalizeBounds(ref b, def);
                    occupied.Add(new OccupiedEntry(b, room.transform));
                }
            }

            roomGO = room;
            return true;
        }

        return false;
    }

    private static bool WouldOverlapOrMeet(
        CaveFeatureDefinition def,
        Transform dungeonRoot,
        GameObject candidatePiece,
        CaveConnector connectingTo,
        List<OccupiedEntry> occupied)
    {
        if (def == null || dungeonRoot == null || candidatePiece == null)
            return false;

        if (!TryComputeLocalBounds(dungeonRoot, candidatePiece, out Bounds candidateBounds))
            return false;

        NormalizeBounds(ref candidateBounds, def);

        Transform ignoreRoot = GetTopLevelPieceRoot(dungeonRoot, connectingTo != null ? connectingTo.transform : null);

        // Overlap check
        for (int i = 0; i < occupied.Count; i++)
        {
            OccupiedEntry e = occupied[i];
            if (e.pieceRoot == null)
                continue;

            if (ignoreRoot != null && e.pieceRoot == ignoreRoot)
                continue;

            if (candidateBounds.Intersects(e.localBounds))
                return true;
        }

        // "Meeting" check: avoid getting too close to existing connectors (optional)
        if (def.minConnectorSeparation > 0f)
        {
            CaveConnector[] newConns = candidatePiece.GetComponentsInChildren<CaveConnector>(true);
            CaveConnector[] allConns = dungeonRoot.GetComponentsInChildren<CaveConnector>(true);

            float minDistSq = def.minConnectorSeparation * def.minConnectorSeparation;

            for (int i = 0; i < newConns.Length; i++)
            {
                CaveConnector a = newConns[i];
                if (a == null) continue;

                // Ignore inbound connector(s) that sit on the connection target.
                if (connectingTo != null)
                {
                    if (a.transform == connectingTo.transform)
                        continue;

                    if ((a.transform.position - connectingTo.transform.position).sqrMagnitude < 0.0001f)
                        continue;
                }

                for (int j = 0; j < allConns.Length; j++)
                {
                    CaveConnector b = allConns[j];
                    if (b == null) continue;
                    if (b.isUsed) continue;
                    if (connectingTo != null && b == connectingTo) continue;

                    // Skip connectors inside the candidate itself
                    if (b.transform.IsChildOf(candidatePiece.transform))
                        continue;

                    Vector3 pa = a.transform.position;
                    Vector3 pb = b.transform.position;

                    if ((pa - pb).sqrMagnitude < minDistSq)
                        return true;
                }
            }
        }

        return false;
    }

    private static Transform GetTopLevelPieceRoot(Transform dungeonRoot, Transform anyChild)
    {
        if (dungeonRoot == null || anyChild == null)
            return null;

        Transform t = anyChild;
        while (t != null && t.parent != null && t.parent != dungeonRoot)
            t = t.parent;

        if (t != null && t.parent == dungeonRoot)
            return t;

        return null;
    }

    private static void NormalizeBounds(ref Bounds b, CaveFeatureDefinition def)
    {
        float touch = Mathf.Max(0f, def.touchEpsilon);
        float pad = Mathf.Max(0f, def.overlapPadding);

        // Shrink a bit to allow face-to-face touching
        if (touch > 0f)
        {
            Vector3 s = b.size;
            s.x = Mathf.Max(0f, s.x - touch * 2f);
            s.y = Mathf.Max(0f, s.y - touch * 2f);
            s.z = Mathf.Max(0f, s.z - touch * 2f);
            b.size = s;
        }

        if (pad > 0f)
            b.Expand(pad * 2f);
    }

    private static bool TryPickRoomConnector(GameObject piece, string tag, Rng rng, out CaveConnector inConn)
    {
        inConn = null;

        if (piece == null)
            return false;

        CaveConnector[] conns = piece.GetComponentsInChildren<CaveConnector>(true);
        if (conns == null || conns.Length == 0)
            return false;

        List<CaveConnector> candidates = new List<CaveConnector>(conns.Length);
        for (int i = 0; i < conns.Length; i++)
        {
            CaveConnector c = conns[i];
            if (c == null) continue;
            if (!c.enabledForGeneration) continue;
            if (c.isUsed) continue;
            if (c.connectorTag != tag) continue;
            candidates.Add(c);
        }

        if (candidates.Count == 0)
            return false;

        inConn = candidates[rng.NextInt(0, candidates.Count)];
        return true;
    }

    private static void PlaceCap(
        CaveFeatureDefinition def,
        Rng rng,
        Transform dungeonRoot,
        CaveConnector target,
        List<OccupiedEntry> occupied)
    {
        if (def == null || rng == null || dungeonRoot == null || target == null)
            return;

        if (target.isUsed)
            return;

        GameObject capPrefab = PickWeighted(def.caps, rng);
        if (capPrefab == null)
        {
            target.MarkUsed();
            return;
        }

        int tries = Mathf.Max(1, def.placementRetriesPerConnector);

        for (int attempt = 0; attempt < tries; attempt++)
        {
            GameObject cap = Object.Instantiate(capPrefab, dungeonRoot);
            cap.name = $"Cap_{attempt}";

            CaveConnector capConn;
            if (!TryPickRoomConnector(cap, target.connectorTag, rng.Split(5000 + attempt * 7), out capConn))
            {
                Object.Destroy(cap);
                continue;
            }

            AlignConnectorTo(capConn.transform, cap.transform, target.transform);

            if (def.preventOverlaps)
            {
                if (WouldOverlapOrMeet(def, dungeonRoot, cap, target, occupied))
                {
                    Object.Destroy(cap);
                    continue;
                }
            }

            target.MarkUsed();
            capConn.MarkUsed();

            if (def.preventOverlaps)
            {
                if (TryComputeLocalBounds(dungeonRoot, cap, out Bounds b))
                {
                    NormalizeBounds(ref b, def);
                    occupied.Add(new OccupiedEntry(b, cap.transform));
                }
            }

            return;
        }

        // Fallback: just mark used to avoid infinite loops
        target.MarkUsed();
    }

    private static void AlignConnectorTo(Transform sourceConnector, Transform pieceRoot, Transform targetConnector)
    {
        if (sourceConnector == null || pieceRoot == null || targetConnector == null)
            return;

        // OUT direction is +Z (forward)
        Vector3 targetOut = targetConnector.forward;
        Vector3 desiredForward = (-targetOut).normalized; // source.forward should face -target.forward

        // Lock roll to world up so corridors/doors don't twist unpredictably
        Vector3 desiredUp = Vector3.ProjectOnPlane(Vector3.up, desiredForward).normalized;
        if (desiredUp.sqrMagnitude < 1e-6f)
            desiredUp = Vector3.up;

        Quaternion desiredConnRot = Quaternion.LookRotation(desiredForward, desiredUp);

        // Solve pieceRoot pose so that sourceConnector matches desired connector pose
        Vector3 connLocalPos = pieceRoot.InverseTransformPoint(sourceConnector.position);
        Quaternion connLocalRot = Quaternion.Inverse(pieceRoot.rotation) * sourceConnector.rotation;

        Quaternion newRootRot = desiredConnRot * Quaternion.Inverse(connLocalRot);
        Vector3 newRootPos = targetConnector.position - (newRootRot * connLocalPos);

        pieceRoot.SetPositionAndRotation(newRootPos, newRootRot);
    }

    private static bool TryComputeLocalBounds(Transform dungeonRoot, GameObject go, out Bounds localBounds)
    {
        localBounds = default;

        if (dungeonRoot == null || go == null)
            return false;

        // Prefer renderers
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
        {
            return ComputeLocalBoundsFromWorldBounds(dungeonRoot, renderers, out localBounds);
        }

        // Fallback colliders
        Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
        if (colliders != null && colliders.Length > 0)
        {
            return ComputeLocalBoundsFromWorldBounds(dungeonRoot, colliders, out localBounds);
        }

        return false;
    }

    private static bool ComputeLocalBoundsFromWorldBounds<T>(Transform dungeonRoot, T[] comps, out Bounds localBounds) where T : Component
    {
        localBounds = default;

        bool has = false;
        Vector3 min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int i = 0; i < comps.Length; i++)
        {
            if (comps[i] == null) continue;

            Bounds wb;
            if (comps[i] is Renderer r)
                wb = r.bounds;
            else if (comps[i] is Collider c)
                wb = c.bounds;
            else
                continue;

            Vector3 wmin = wb.min;
            Vector3 wmax = wb.max;

            // 8 corners of world AABB
            Vector3[] corners = new Vector3[8]
            {
                new Vector3(wmin.x, wmin.y, wmin.z),
                new Vector3(wmax.x, wmin.y, wmin.z),
                new Vector3(wmin.x, wmax.y, wmin.z),
                new Vector3(wmax.x, wmax.y, wmin.z),
                new Vector3(wmin.x, wmin.y, wmax.z),
                new Vector3(wmax.x, wmin.y, wmax.z),
                new Vector3(wmin.x, wmax.y, wmax.z),
                new Vector3(wmax.x, wmax.y, wmax.z),
            };

            for (int k = 0; k < 8; k++)
            {
                Vector3 p = dungeonRoot.InverseTransformPoint(corners[k]);
                if (!has)
                {
                    min = p;
                    max = p;
                    has = true;
                }
                else
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
        }

        if (!has)
            return false;

        Vector3 size = max - min;
        localBounds = new Bounds((min + max) * 0.5f, size);
        return true;
    }
}
