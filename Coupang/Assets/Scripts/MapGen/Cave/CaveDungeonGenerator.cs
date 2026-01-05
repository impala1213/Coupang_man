using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Modular cave generator (connector-based).
///
/// Connector rule:
/// - Connector outward direction = +Z axis (Transform.forward).
/// - Two connectors connect when their connectorTag matches.
/// - Corridors may have 2+ connectors (junctions).
/// - Every unused connector (isUsed == false) on PLACED pieces will be closed by a cap (guaranteed).
///
/// Critical fix:
/// - Attempt pieces (instantiated and later Destroyed) must NEVER be treated as valid expansion/cap targets.
/// - We mark ONLY successfully placed pieces with CavePlacedPiece(token).
/// - Queue processing + final cap pass only considers connectors belonging to placed pieces with matching token.
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

    private enum NodeKind { Room = 0, Corridor = 1};

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

    // Alignment validation tolerances
    private const float SNAP_EPSILON = 0.02f;     // 2cm
    private const float ANGLE_EPSILON_DEG = 2.0f; // 2 degrees

    public static Result Generate(
        CaveFeatureDefinition def,
        Rng rng,
        Vector3 interiorOrigin,
        Transform parent,
        string instanceName = "Cave")
    {
        Result res = new Result();
        if (def == null) return res;
        if (rng == null) rng = new Rng(12345);

        GameObject rootGO = new GameObject(instanceName);
        rootGO.transform.SetParent(parent, true);
        rootGO.transform.position = interiorOrigin;
        rootGO.transform.rotation = Quaternion.identity;

        // Dungeon token (unique per run)
        int dungeonToken = rootGO.GetInstanceID();

        CaveInstance inst = rootGO.AddComponent<CaveInstance>();

        List<OccupiedEntry> occupied = new List<OccupiedEntry>(64);
        Queue<OpenSocket> open = new Queue<OpenSocket>(64);
        HashSet<CaveConnector> enqueued = new HashSet<CaveConnector>();

        // 1) Start piece
        bool startedFromExitPrefab = def.useExitPrefabAsStartPiece && def.exitPrefab != null;
        if (startedFromExitPrefab)
        {
            var exitConns = def.exitPrefab.GetComponentsInChildren<CaveConnector>(true);
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

        // Mark placed
        MarkPiecePlaced(startPiece.transform, dungeonToken);

        res.startPieceRoot = startPiece.transform;

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

        if (def.preventOverlaps)
        {
            if (TryComputeLocalBounds(rootGO.transform, startPiece, out Bounds b0))
            {
                NormalizeBounds(ref b0, def);
                occupied.Add(new OccupiedEntry(b0, startPiece.transform));
            }
        }

        EnqueuePieceConnectors(startPiece, open, enqueued, dungeonToken, NodeKind.Room, 0);

        int roomsPlaced = 1;

        // 2) Expand
        int safetyIterations = 100000;
        int iter = 0;

        while (open.Count > 0 && iter++ < safetyIterations)
        {
            OpenSocket s = open.Dequeue();
            CaveConnector from = s.connector;

            if (from == null) continue;
            if (from.isUsed) continue;
            if (!from.enabledForGeneration) continue;

            // ✅ Hard gate: only process connectors belonging to a placed piece for this dungeon token
            if (!IsConnectorFromPlacedPiece(rootGO.transform, from, dungeonToken))
                continue;

            if (s.kind == NodeKind.Room && roomsPlaced >= Mathf.Max(1, def.maxRooms))
            {
                PlaceCapForce(def, rng.Split(91000 + iter), rootGO.transform, dungeonToken, from, occupied);
                continue;
            }

            if (s.kind == NodeKind.Room)
            {
                if (rng.NextFloat(0f, 1f) > Mathf.Clamp01(def.roomToCorridorProbability))
                {
                    PlaceCapForce(def, rng.Split(92000 + iter), rootGO.transform, dungeonToken, from, occupied);
                    continue;
                }

                if (!TryAttachCorridor(def, rng.Split(10000 + iter), rootGO.transform, dungeonToken, from, occupied,
                        out List<CaveConnector> outs))
                {
                    PlaceCapForce(def, rng.Split(93000 + iter), rootGO.transform, dungeonToken, from, occupied);
                    continue;
                }

                if (outs != null)
                {
                    for (int k = 0; k < outs.Count; k++)
                        EnqueueSocket(open, enqueued, rootGO.transform, dungeonToken, outs[k], NodeKind.Corridor, 1);
                }

                continue;
            }

            // Corridor socket: continue corridor vs spawn room vs cap
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
                if (!TryAttachCorridor(def, rng.Split(20000 + iter), rootGO.transform, dungeonToken, from, occupied,
                        out List<CaveConnector> outs))
                {
                    PlaceCapForce(def, rng.Split(94000 + iter), rootGO.transform, dungeonToken, from, occupied);
                    continue;
                }

                if (outs != null)
                {
                    int nextDepth = s.corridorChainDepth + 1;
                    for (int k = 0; k < outs.Count; k++)
                        EnqueueSocket(open, enqueued, rootGO.transform, dungeonToken, outs[k], NodeKind.Corridor, nextDepth);
                }

                continue;
            }

            if (roll < contP + toRoomP && roomsPlaced < Mathf.Max(1, def.maxRooms))
            {
                if (!TryAttachRoom(def, rng.Split(30000 + iter), rootGO.transform, dungeonToken, from, occupied, out GameObject roomGO))
                {
                    PlaceCapForce(def, rng.Split(95000 + iter), rootGO.transform, dungeonToken, from, occupied);
                    continue;
                }

                roomsPlaced++;
                EnqueuePieceConnectors(roomGO, open, enqueued, dungeonToken, NodeKind.Room, 0);
                continue;
            }

            PlaceCapForce(def, rng.Split(96000 + iter), rootGO.transform, dungeonToken, from, occupied);
        }

        // 3) FINAL GUARANTEE PASS:
        // Cap all unused connectors that belong to placed pieces.
        CaveConnector[] all = rootGO.GetComponentsInChildren<CaveConnector>(true);
        if (all != null)
        {
            for (int i = 0; i < all.Length; i++)
            {
                CaveConnector c = all[i];
                if (c == null) continue;
                if (!c.enabledForGeneration) continue;
                if (c.isUsed) continue;

                if (!IsConnectorFromPlacedPiece(rootGO.transform, c, dungeonToken))
                    continue;

                PlaceCapForce(def, rng.Split(97000 + i), rootGO.transform, dungeonToken, c, occupied);
            }
        }

        res.instance = inst;
        res.root = rootGO.transform;
        res.insideEntryPoint = inst.insideEntryPoint;
        return res;
    }

    // ─────────────────────────────────────────────────────────────
    // Placed piece marking / filtering
    // ─────────────────────────────────────────────────────────────

    private static void MarkPiecePlaced(Transform pieceRoot, int token)
    {
        if (pieceRoot == null) return;

        CavePlacedPiece marker = pieceRoot.GetComponent<CavePlacedPiece>();
        if (marker == null) marker = pieceRoot.gameObject.AddComponent<CavePlacedPiece>();
        marker.MarkPlaced(token);
    }

    private static bool IsConnectorFromPlacedPiece(Transform dungeonRoot, CaveConnector c, int token)
    {
        if (dungeonRoot == null || c == null) return false;

        Transform pieceRoot = GetTopLevelPieceRoot(dungeonRoot, c.transform);
        if (pieceRoot == null) return false;

        CavePlacedPiece marker = pieceRoot.GetComponent<CavePlacedPiece>();
        return marker != null && marker.IsPlacedFor(token);
    }

    // ─────────────────────────────────────────────────────────────
    // Frontier enqueue (dedupe + placed filter)
    // ─────────────────────────────────────────────────────────────

    private static void EnqueueSocket(
        Queue<OpenSocket> open,
        HashSet<CaveConnector> enqueued,
        Transform dungeonRoot,
        int token,
        CaveConnector c,
        NodeKind kind,
        int corridorDepth)
    {
        if (c == null) return;
        if (c.isUsed) return;
        if (!c.enabledForGeneration) return;

        // ✅ Only enqueue connectors from placed pieces
        if (!IsConnectorFromPlacedPiece(dungeonRoot, c, token))
            return;

        if (!enqueued.Add(c)) return;
        open.Enqueue(new OpenSocket(c, kind, corridorDepth));
    }

    private static void EnqueuePieceConnectors(
        GameObject piece,
        Queue<OpenSocket> open,
        HashSet<CaveConnector> enqueued,
        int token,
        NodeKind kind,
        int corridorDepth)
    {
        if (piece == null) return;

        CaveConnector[] connectors = piece.GetComponentsInChildren<CaveConnector>(true);
        if (connectors == null) return;

        for (int i = 0; i < connectors.Length; i++)
            EnqueueSocket(open, enqueued, piece.transform.parent, token, connectors[i], kind, corridorDepth);
    }

    // ─────────────────────────────────────────────────────────────
    // Weighted selection
    // ─────────────────────────────────────────────────────────────

    private static GameObject PickWeighted(CaveFeatureDefinition.WeightedPrefab[] list, Rng rng)
    {
        if (list == null || list.Length == 0 || rng == null) return null;

        float total = 0f;
        for (int i = 0; i < list.Length; i++)
            if (list[i].prefab != null)
                total += Mathf.Max(0f, list[i].weight);

        if (total <= 0f)
        {
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

    private static List<GameObject> BuildWeightedPrefabOrder(CaveFeatureDefinition.WeightedPrefab[] list, Rng rng)
    {
        List<GameObject> order = new List<GameObject>();
        if (list == null || list.Length == 0 || rng == null) return order;

        List<(float key, GameObject prefab)> tmp = new List<(float, GameObject)>(list.Length);

        for (int i = 0; i < list.Length; i++)
        {
            GameObject p = list[i].prefab;
            if (p == null) continue;

            float w = Mathf.Max(0.0001f, list[i].weight);
            float u = Mathf.Clamp(rng.NextFloat(0.000001f, 0.999999f), 0.000001f, 0.999999f);
            float key = -Mathf.Log(u) / w;
            tmp.Add((key, p));
        }

        tmp.Sort((a, b) => a.key.CompareTo(b.key));
        for (int i = 0; i < tmp.Count; i++)
            order.Add(tmp[i].prefab);

        return order;
    }

    private static void ShuffleInPlace<T>(List<T> list, Rng rng)
    {
        if (list == null || rng == null) return;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(0, i + 1);
            T tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Attach corridor / room
    // ─────────────────────────────────────────────────────────────

    private static bool TryAttachCorridor(
        CaveFeatureDefinition def,
        Rng rng,
        Transform dungeonRoot,
        int token,
        CaveConnector from,
        List<OccupiedEntry> occupied,
        out List<CaveConnector> outConnectors)
    {
        outConnectors = null;
        if (def == null || rng == null || dungeonRoot == null || from == null) return false;

        List<GameObject> prefabOrder = BuildWeightedPrefabOrder(def.corridors, rng.Split(1001));
        if (prefabOrder.Count == 0) return false;

        int maxPrefabTries = Mathf.Max(1, def.placementRetriesPerConnector);
        int prefabTries = Mathf.Min(maxPrefabTries, prefabOrder.Count);

        for (int p = 0; p < prefabTries; p++)
        {
            GameObject corridorPrefab = prefabOrder[p];
            if (corridorPrefab == null) continue;

            GameObject corridor = Object.Instantiate(corridorPrefab, dungeonRoot);
            corridor.name = $"Corridor_Attempt_{p}";

            CaveConnector[] allConns = corridor.GetComponentsInChildren<CaveConnector>(true);
            if (allConns == null || allConns.Length < 1)
            {
                Object.Destroy(corridor);
                continue;
            }

            List<CaveConnector> inboundCandidates = new List<CaveConnector>(allConns.Length);
            for (int i = 0; i < allConns.Length; i++)
            {
                CaveConnector c = allConns[i];
                if (c == null) continue;
                if (!c.enabledForGeneration) continue;
                if (c.isUsed) continue;
                if (c.connectorTag != from.connectorTag) continue;
                inboundCandidates.Add(c);
            }

            if (inboundCandidates.Count == 0)
            {
                Object.Destroy(corridor);
                continue;
            }

            ShuffleInPlace(inboundCandidates, rng.Split(2001 + p * 13));

            bool success = false;
            CaveConnector inConn = null;

            for (int k = 0; k < inboundCandidates.Count; k++)
            {
                inConn = inboundCandidates[k];
                if (inConn == null) continue;

                corridor.transform.localPosition = Vector3.zero;
                corridor.transform.localRotation = Quaternion.identity;

                if (!TryAlignConnectorTo(inConn.transform, corridor.transform, from.transform))
                    continue;

                if (def.preventOverlaps && WouldOverlapOrMeet(def, dungeonRoot, corridor, from, occupied))
                    continue;

                success = true;
                break;
            }

            if (!success)
            {
                Object.Destroy(corridor);
                continue;
            }

            // ✅ Now it's truly placed
            MarkPiecePlaced(corridor.transform, token);

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

            // Return all remaining connectors (junction support)
            List<CaveConnector> outs = new List<CaveConnector>(allConns.Length);
            for (int i = 0; i < allConns.Length; i++)
            {
                CaveConnector c = allConns[i];
                if (c == null) continue;
                if (c == inConn) continue;
                if (!c.enabledForGeneration) continue;
                if (c.isUsed) continue;
                outs.Add(c);
            }

            outConnectors = outs;
            return true;
        }

        return false;
    }

    private static bool TryAttachRoom(
        CaveFeatureDefinition def,
        Rng rng,
        Transform dungeonRoot,
        int token,
        CaveConnector from,
        List<OccupiedEntry> occupied,
        out GameObject roomGO)
    {
        roomGO = null;
        if (def == null || rng == null || dungeonRoot == null || from == null) return false;

        List<GameObject> prefabOrder = BuildWeightedPrefabOrder(def.rooms, rng.Split(3001));
        if (prefabOrder.Count == 0) return false;

        int maxPrefabTries = Mathf.Max(1, def.placementRetriesPerConnector);
        int prefabTries = Mathf.Min(maxPrefabTries, prefabOrder.Count);

        for (int p = 0; p < prefabTries; p++)
        {
            GameObject roomPrefab = prefabOrder[p];
            if (roomPrefab == null) continue;

            GameObject room = Object.Instantiate(roomPrefab, dungeonRoot);
            room.name = $"Room_Attempt_{p}";

            CaveConnector[] conns = room.GetComponentsInChildren<CaveConnector>(true);
            if (conns == null || conns.Length == 0)
            {
                Object.Destroy(room);
                continue;
            }

            List<CaveConnector> candidates = new List<CaveConnector>(conns.Length);
            for (int i = 0; i < conns.Length; i++)
            {
                CaveConnector c = conns[i];
                if (c == null) continue;
                if (!c.enabledForGeneration) continue;
                if (c.isUsed) continue;
                if (c.connectorTag != from.connectorTag) continue;
                candidates.Add(c);
            }

            if (candidates.Count == 0)
            {
                Object.Destroy(room);
                continue;
            }

            ShuffleInPlace(candidates, rng.Split(4001 + p * 17));

            bool success = false;
            CaveConnector inConn = null;

            for (int k = 0; k < candidates.Count; k++)
            {
                inConn = candidates[k];
                if (inConn == null) continue;

                room.transform.localPosition = Vector3.zero;
                room.transform.localRotation = Quaternion.identity;

                if (!TryAlignConnectorTo(inConn.transform, room.transform, from.transform))
                    continue;

                if (def.preventOverlaps && WouldOverlapOrMeet(def, dungeonRoot, room, from, occupied))
                    continue;

                success = true;
                break;
            }

            if (!success)
            {
                Object.Destroy(room);
                continue;
            }

            // ✅ Now it's truly placed
            MarkPiecePlaced(room.transform, token);

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

    // ─────────────────────────────────────────────────────────────
    // CAP (guaranteed close) - only on placed connectors
    // ─────────────────────────────────────────────────────────────

    private static void PlaceCapForce(
        CaveFeatureDefinition def,
        Rng rng,
        Transform dungeonRoot,
        int token,
        CaveConnector target,
        List<OccupiedEntry> occupied)
    {
        if (def == null || rng == null || dungeonRoot == null || target == null) return;
        if (target.isUsed) return;

        // ✅ Only cap placed connectors
        if (!IsConnectorFromPlacedPiece(dungeonRoot, target, token))
            return;

        if (TryPlaceCapInternal(def, rng.Split(70000), dungeonRoot, token, target, occupied, ignoreOverlapAndMeeting: false))
            return;

        if (TryPlaceCapInternal(def, rng.Split(70100), dungeonRoot, token, target, occupied, ignoreOverlapAndMeeting: true))
            return;

        // Fallback: block it anyway (still only for placed connectors)
        CreateFallbackCap(dungeonRoot, target);
        target.MarkUsed();
    }

    private static bool TryPlaceCapInternal(
        CaveFeatureDefinition def,
        Rng rng,
        Transform dungeonRoot,
        int token,
        CaveConnector target,
        List<OccupiedEntry> occupied,
        bool ignoreOverlapAndMeeting)
    {
        List<GameObject> prefabOrder = BuildWeightedPrefabOrder(def.caps, rng.Split(7001));
        if (prefabOrder.Count == 0) return false;

        int maxPrefabTries = Mathf.Max(1, def.placementRetriesPerConnector);
        int prefabTries = Mathf.Min(maxPrefabTries, prefabOrder.Count);

        for (int p = 0; p < prefabTries; p++)
        {
            GameObject capPrefab = prefabOrder[p];
            if (capPrefab == null) continue;

            GameObject cap = Object.Instantiate(capPrefab, dungeonRoot);
            cap.name = ignoreOverlapAndMeeting ? $"Cap_Force_Attempt_{p}" : $"Cap_Attempt_{p}";

            CaveConnector[] conns = cap.GetComponentsInChildren<CaveConnector>(true);
            if (conns == null || conns.Length == 0)
            {
                Object.Destroy(cap);
                continue;
            }

            List<CaveConnector> candidates = new List<CaveConnector>(conns.Length);
            for (int i = 0; i < conns.Length; i++)
            {
                CaveConnector c = conns[i];
                if (c == null) continue;
                if (!c.enabledForGeneration) continue;
                if (c.isUsed) continue;
                if (c.connectorTag != target.connectorTag) continue;
                candidates.Add(c);
            }

            if (candidates.Count == 0)
            {
                Object.Destroy(cap);
                continue;
            }

            ShuffleInPlace(candidates, rng.Split(7101 + p * 11));

            CaveConnector capConn = null;
            bool aligned = false;

            for (int k = 0; k < candidates.Count; k++)
            {
                capConn = candidates[k];
                if (capConn == null) continue;

                cap.transform.localPosition = Vector3.zero;
                cap.transform.localRotation = Quaternion.identity;

                if (!TryAlignConnectorTo(capConn.transform, cap.transform, target.transform))
                    continue;

                if (!ignoreOverlapAndMeeting && def.preventOverlaps)
                {
                    if (WouldOverlapOrMeet(def, dungeonRoot, cap, target, occupied))
                        continue;
                }

                aligned = true;
                break;
            }

            if (!aligned)
            {
                Object.Destroy(cap);
                continue;
            }

            // ✅ Now it's truly placed
            MarkPiecePlaced(cap.transform, token);

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

            // Close all connectors on cap to prevent cap creating more caps
            CloseAllConnectorsOnPiece(cap.transform);
            cap.name = ignoreOverlapAndMeeting ? $"Cap_Force_{p}" : $"Cap_{p}";
            return true;
        }

        return false;
    }

    private static void CloseAllConnectorsOnPiece(Transform pieceRoot)
    {
        if (pieceRoot == null) return;

        CaveConnector[] all = pieceRoot.GetComponentsInChildren<CaveConnector>(true);
        if (all == null) return;

        for (int i = 0; i < all.Length; i++)
        {
            CaveConnector c = all[i];
            if (c == null) continue;
            if (!c.enabledForGeneration) continue;
            if (!c.isUsed) c.MarkUsed();
        }
    }

    private static void CreateFallbackCap(Transform dungeonRoot, CaveConnector target)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "GeneratedFallbackCap";
        go.transform.SetParent(dungeonRoot, true);

        Vector3 outward = target.transform.forward.normalized;
        Quaternion rot = Quaternion.LookRotation(-outward, target.transform.up);
        go.transform.SetPositionAndRotation(target.transform.position + (-outward) * 0.05f, rot);
        go.transform.localScale = new Vector3(1.2f, 1.2f, 0.2f);
    }

    // ─────────────────────────────────────────────────────────────
    // ✅ Alignment (+Z outward) - scale-safe
    // ─────────────────────────────────────────────────────────────

    private static bool TryAlignConnectorTo(Transform sourceConnector, Transform pieceRoot, Transform targetConnector)
    {
        if (sourceConnector == null || pieceRoot == null || targetConnector == null)
            return false;

        Vector3 oldPos = pieceRoot.position;
        Quaternion oldRot = pieceRoot.rotation;

        Vector3 desiredForward = (-targetConnector.forward).normalized;
        Vector3 desiredUp = Vector3.ProjectOnPlane(targetConnector.up, desiredForward).normalized;
        if (desiredUp.sqrMagnitude < 1e-6f) desiredUp = Vector3.up;

        Quaternion desiredConnRot = Quaternion.LookRotation(desiredForward, desiredUp);

        Quaternion deltaRot = desiredConnRot * Quaternion.Inverse(sourceConnector.rotation);
        pieceRoot.rotation = deltaRot * pieceRoot.rotation;

        Vector3 deltaPos = targetConnector.position - sourceConnector.position;
        pieceRoot.position += deltaPos;

        float dist = Vector3.Distance(sourceConnector.position, targetConnector.position);
        float ang = Vector3.Angle(sourceConnector.forward, -targetConnector.forward);

        if (dist > SNAP_EPSILON || ang > ANGLE_EPSILON_DEG)
        {
            pieceRoot.SetPositionAndRotation(oldPos, oldRot);
            return false;
        }

        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Overlap / meeting checks
    // ─────────────────────────────────────────────────────────────

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

        for (int i = 0; i < occupied.Count; i++)
        {
            OccupiedEntry e = occupied[i];
            if (e.pieceRoot == null) continue;
            if (ignoreRoot != null && e.pieceRoot == ignoreRoot) continue;
            if (candidateBounds.Intersects(e.localBounds)) return true;
        }

        if (def.minConnectorSeparation > 0f)
        {
            CaveConnector[] newConns = candidatePiece.GetComponentsInChildren<CaveConnector>(true);
            CaveConnector[] allConns = dungeonRoot.GetComponentsInChildren<CaveConnector>(true);

            float minDistSq = def.minConnectorSeparation * def.minConnectorSeparation;

            for (int i = 0; i < newConns.Length; i++)
            {
                CaveConnector a = newConns[i];
                if (a == null) continue;

                if (connectingTo != null)
                {
                    if (a.transform == connectingTo.transform) continue;
                    if ((a.transform.position - connectingTo.transform.position).sqrMagnitude < 0.0001f) continue;
                }

                for (int j = 0; j < allConns.Length; j++)
                {
                    CaveConnector b = allConns[j];
                    if (b == null) continue;
                    if (b.isUsed) continue;
                    if (connectingTo != null && b == connectingTo) continue;
                    if (b.transform.IsChildOf(candidatePiece.transform)) continue;

                    if ((a.transform.position - b.transform.position).sqrMagnitude < minDistSq)
                        return true;
                }
            }
        }

        return false;
    }

    private static Transform GetTopLevelPieceRoot(Transform dungeonRoot, Transform anyChild)
    {
        if (dungeonRoot == null || anyChild == null) return null;

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

    // ─────────────────────────────────────────────────────────────
    // Bounds utils
    // ─────────────────────────────────────────────────────────────

    private static bool TryComputeLocalBounds(Transform dungeonRoot, GameObject go, out Bounds localBounds)
    {
        localBounds = default;
        if (dungeonRoot == null || go == null) return false;

        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers != null && renderers.Length > 0)
            return ComputeLocalBoundsFromWorldBounds(dungeonRoot, renderers, out localBounds);

        Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
        if (colliders != null && colliders.Length > 0)
            return ComputeLocalBoundsFromWorldBounds(dungeonRoot, colliders, out localBounds);

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
            if (comps[i] is Renderer r) wb = r.bounds;
            else if (comps[i] is Collider c) wb = c.bounds;
            else continue;

            Vector3 wmin = wb.min;
            Vector3 wmax = wb.max;

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
                    min = p; max = p; has = true;
                }
                else
                {
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
            }
        }

        if (!has) return false;

        Vector3 size = max - min;
        localBounds = new Bounds((min + max) * 0.5f, size);
        return true;
    }
}
