/*using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using Unity.NetCode;
using System.Collections.Generic;

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
[UpdateBefore(typeof(MovementSystem))]
partial struct TemporaryTargetSystem : ISystem
{
    struct Grid
    {
        public float cellSize;

        public int2 Cell(float3 pos)
        {
            return new int2(
                (int)math.floor(pos.x / cellSize),
                (int)math.floor(pos.z / cellSize));
        }

        public float3 Center(int2 cell, float y)
        {
            return new float3((cell.x + 0.5f) * cellSize, y, (cell.y + 0.5f) * cellSize);
        }

        public static int Hash(int2 cell) => (cell.x * 73856093) ^ (cell.y * 19349663);
    }

    struct Candidate
    {
        public Entity entity;
        public float3 destination;
        public float  distanceToDestination;
        public bool   hasTemporaryTarget;
        public float3 currentTemporaryTarget;
    }

    public void OnUpdate(ref SystemState state)
    {
        // Tunables — tweak to taste
        const float unitDiameter                 = 0.6f;  // ≈ collider width
        const float resolveDistanceThreshold     = 5.0f;  // only resolve when within this distance
        const float preclaimDistanceThreshold    = 6.5f;  // if farther than this, drop old claim
        const float destinationShiftReassignDist = 2.0f;  // if destination moved this far from temp slot, re-resolve
        const int   maxRings                     = 6;     // search radius in cells

        var grid = new Grid { cellSize = unitDiameter };

        // Per-frame claimed set (unique cell hashes)
        var claimedCells = new NativeParallelHashSet<int>(1024, Allocator.Temp);

        // Gather candidates
        var candidates = new NativeList<Candidate>(Allocator.Temp);

        foreach (var (lt, targets, e) in SystemAPI
                     .Query<RefRO<LocalTransform>, RefRO<UnitTargets>>()
                     .WithAll<Simulate>()
                     .WithEntityAccess())
        {
            float3 dst = targets.ValueRO.destinationPosition;
            float  dist = math.distance(lt.ValueRO.Position, dst);

            // Only bother when within resolve threshold (others follow raw destination)
            if (dist > resolveDistanceThreshold)
            {
                // Clear temp slot so movement uses raw destination
                var t = targets.ValueRO;
                t.hasTemporaryTarget      = false;
                t.temporaryTargetPosition = dst;
                state.EntityManager.SetComponentData(e, t);
                continue;
            }

            candidates.Add(new Candidate
            {
                entity = e,
                destination = dst,
                distanceToDestination = dist,
                hasTemporaryTarget = targets.ValueRO.hasTemporaryTarget,
                currentTemporaryTarget = targets.ValueRO.temporaryTargetPosition
            });
        }

        // Phase 1: pre-claim existing slots (prevents churn)
        for (int i = 0; i < candidates.Length; i++)
        {
            var c = candidates[i];
            if (!c.hasTemporaryTarget) continue;

            // Drop claim if destination moved too far OR unit wandered far away
            if (math.distance(c.currentTemporaryTarget, c.destination) > destinationShiftReassignDist ||
                c.distanceToDestination > preclaimDistanceThreshold)
            {
                // mark as needing re-resolve
                c.hasTemporaryTarget = false;
                candidates[i] = c;
                continue;
            }

            int2 cell = grid.Cell(c.currentTemporaryTarget);
            claimedCells.Add(Grid.Hash(cell));
        }

        // Phase 2: sort candidates for deterministic resolution
        // Priority: nearer units first; tie-breaker: Entity.Index (stable)
        for (int i = 0; i < candidates.Length - 1; i++)
        {
            int best = i;
            for (int j = i + 1; j < candidates.Length; j++)
            {
                // Primary: nearer destination first
                if (candidates[j].distanceToDestination < candidates[best].distanceToDestination)
                {
                    best = j;
                    continue;
                }
                if (candidates[j].distanceToDestination > candidates[best].distanceToDestination)
                    continue;

                // Tie-breaker: stable by Entity.Index (and Version if ever equal)
                var ej = candidates[j].entity;
                var eb = candidates[best].entity;
                if (ej.Index < eb.Index || (ej.Index == eb.Index && ej.Version < eb.Version))
                    best = j;
            }
            if (best != i)
            {
                var tmp = candidates[i];
                candidates[i] = candidates[best];
                candidates[best] = tmp;
            }
        }

        // Phase 3: assign for those who need a slot
        for (int i = 0; i < candidates.Length; i++)
        {
            var c = candidates[i];
            if (c.hasTemporaryTarget)
                continue;

            int2 dstCell = grid.Cell(c.destination);
            float y = c.destination.y;

            bool   found = false;
            float3 chosen = grid.Center(dstCell, y);

            if (claimedCells.Add(Grid.Hash(dstCell)))
            {
                chosen = grid.Center(dstCell, y);
                found = true;
            }
            else
            {
                for (int ring = 1; ring <= maxRings && !found; ring++)
                {
                    // top & bottom rows
                    for (int dx = -ring; dx <= ring && !found; dx++)
                    {
                        int2 top = new int2(dstCell.x + dx, dstCell.y + ring);
                        if (claimedCells.Add(Grid.Hash(top))) { chosen = grid.Center(top, y); found = true; break; }
                        int2 bottom = new int2(dstCell.x + dx, dstCell.y - ring);
                        if (claimedCells.Add(Grid.Hash(bottom))) { chosen = grid.Center(bottom, y); found = true; break; }
                    }
                    // left & right columns (skip corners already checked)
                    for (int dz = -ring + 1; dz <= ring - 1 && !found; dz++)
                    {
                        int2 right = new int2(dstCell.x + ring, dstCell.y + dz);
                        if (claimedCells.Add(Grid.Hash(right))) { chosen = grid.Center(right, y); found = true; break; }
                        int2 left = new int2(dstCell.x - ring, dstCell.y + dz);
                        if (claimedCells.Add(Grid.Hash(left))) { chosen = grid.Center(left, y); found = true; break; }
                    }
                }
            }

            // Write back
            var t = SystemAPI.GetComponent<UnitTargets>(c.entity);
            t.temporaryTargetPosition = found ? chosen : c.destination;
            t.hasTemporaryTarget      = true;
            state.EntityManager.SetComponentData(c.entity, t);
        }

        claimedCells.Dispose();
        candidates.Dispose();
    }
}*/