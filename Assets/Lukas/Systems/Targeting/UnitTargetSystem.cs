using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;
using Managers;
using Unity.Collections;
using Unity.Transforms;
using Unity.Physics;

[UpdateInGroup(typeof(GhostInputSystemGroup))]
partial struct UnitTargetSystem : ISystem
{
    float lastClickTime;
    float angleOffset;
    //float doubleClickThreshold;
    int arrayLength;
    float3 cursorPosition;
    float3 targetPosition;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<NetworkStreamInGame>();
        state.RequireForUpdate<UnitTargetsNetcode>();
        state.RequireForUpdate<EntitiesReferencesLukas>();

        //doubleClickThreshold = 0.3f;
    }

    public void OnUpdate(ref SystemState state)
    {

        // In-Game check
        if (!CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld())) return;

        
        if (Input.GetMouseButtonDown(1))
        {
            arrayLength = 0;
            foreach (var unitSelected in SystemAPI.Query<RefRO<Unit>>().WithAll<GhostOwnerIsLocal, Selected>())
                arrayLength++;

            cursorPosition = MouseWorldPosition.Instance.GetPosition();
            targetPosition = cursorPosition;
        }

        if (arrayLength == 0) return;

        // While holding: update drag state + preview arrows
        if (Input.GetMouseButton(1))
        {
            cursorPosition = MouseWorldPosition.Instance.GetPosition();

            // Compute angle offset from drag vector
            float3 offsetPosition = cursorPosition;
            float3 dragVector = offsetPosition - targetPosition;
            if (math.lengthsq(dragVector.xz) < 0.5f)
            {
                angleOffset = 0f;
            }
            else
            {
                float3 direction = math.normalize(dragVector);
                angleOffset = math.atan2(direction.z, direction.x);
                if (!math.isfinite(angleOffset)) angleOffset = 0f;
            }

            float formationWidth = math.length(cursorPosition - targetPosition);

            var buffer = new EntityCommandBuffer(Allocator.Temp);
            var references = SystemAPI.GetSingleton<EntitiesReferencesLukas>();

            if (formationWidth > 2.5f)
            {
                // Generate formation preview
                Formations selectedFormation = FormationUIState.SelectedFormation;
                FormationGenerator generateFormation = FormationLibrary.Get(selectedFormation);
                NativeArray<float3> emptyCurrentPositions = new NativeArray<float3>(0, Allocator.Temp);
                NativeArray<float3> targetPositionArray = generateFormation(
                    targetPosition,
                    cursorPosition,
                    emptyCurrentPositions,
                    arrayLength,
                    angleOffset,
                    Allocator.Temp
                );

                // Remove existing arrows
                foreach (var (existingArrow, entity) in SystemAPI.Query<RefRO<FormationArrow>>().WithEntityAccess())
                    buffer.DestroyEntity(entity);

                // Add new arrows
                foreach (var slotPos in targetPositionArray)
                {
                    var targetEntity = buffer.Instantiate(references.formationArrowPrefabEntity);
                    buffer.SetComponent(targetEntity,
                        LocalTransform.FromPositionRotationScale(
                            slotPos,
                            quaternion.RotateY(-angleOffset + math.PI),
                            0.1f));
                }
                
                targetPositionArray.Dispose();
                emptyCurrentPositions.Dispose();
            }
            else
            {
                // Remove existing arrows when not dragging enough to form a line
                foreach (var (existingArrow, entity) in SystemAPI.Query<RefRO<FormationArrow>>().WithEntityAccess())
                    buffer.DestroyEntity(entity);
            }

            buffer.Playback(state.EntityManager);
            buffer.Dispose();
        }

        // Release - orders
        if (Input.GetMouseButtonUp(1))
        {
            // ADDED: If we actually drew a formation, commit it FIRST and bail out,
            //        so releasing over a unit doesn't convert the action into a target-click.
            float formationWidthAtRelease = math.length(cursorPosition - targetPosition);
            if (formationWidthAtRelease > 2.5f)
            {
                Formations selectedFormation = FormationUIState.SelectedFormation;
                FormationGenerator generateFormation = FormationLibrary.Get(selectedFormation);
                NativeArray<float3> emptyCurrentPositions = new NativeArray<float3>(0, Allocator.Temp);

                NativeArray<float3> targetPositionArray = generateFormation(
                    targetPosition,
                    cursorPosition,
                    emptyCurrentPositions,
                    arrayLength,
                    angleOffset,
                    Allocator.Temp
                );

                var bufferEarly = new EntityCommandBuffer(Allocator.Temp);

                // Remove existing arrows before committing
                foreach (var (existingArrow, entity) in SystemAPI.Query<RefRO<FormationArrow>>().WithEntityAccess())
                    bufferEarly.DestroyEntity(entity);

                SetDestinations(targetPositionArray, angleOffset, ref state);

                bufferEarly.Playback(state.EntityManager);
                bufferEarly.Dispose();
                emptyCurrentPositions.Dispose();

                return; // <-- ADDED: stop here; don't run target raycast
            }

            //Raycast Target
            var entityManager = WorldManager.GetClientWorld().EntityManager;
            var entityQuery = entityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
            var physicsWorldSingleton = entityQuery.GetSingleton<PhysicsWorldSingleton>();
            var collisionWorld = physicsWorldSingleton.CollisionWorld;

            UnityEngine.Ray cameraRay = Camera.main.ScreenPointToRay(Input.mousePosition);

            int targetableLayer = 6; //Sixth layer is for targetable
            RaycastInput raycastInput = new RaycastInput
            {
                Start = cameraRay.GetPoint(0f),
                End = cameraRay.GetPoint(9999f), //Just a long ray
                Filter = new CollisionFilter
                {
                    BelongsTo = ~0u,
                    CollidesWith = 1u << targetableLayer, //Bitmask bit-shift layer
                    GroupIndex = 0,
                }
            };

            if (collisionWorld.CastRay(raycastInput, out Unity.Physics.RaycastHit raycastHit))
            {
                var hitTarget = collisionWorld.Bodies[raycastHit.RigidBodyIndex];
                Entity hitEntity = hitTarget.Entity;
                
                var buffer = new EntityCommandBuffer(Allocator.Temp);

                // ADDED: Clear any formation arrows if a target click wins.
                foreach (var (existingArrow, entity) in SystemAPI.Query<RefRO<FormationArrow>>().WithEntityAccess())
                buffer.DestroyEntity(entity);

                foreach (var (unitTargetsNetcode, dotRef) in SystemAPI.Query<RefRW<UnitTargetsNetcode>, RefRW<MovementDotRef>>().WithAll<GhostOwnerIsLocal, Selected>())
                {

                    if (dotRef.ValueRO.Dot != Entity.Null)
                    {
                        buffer.DestroyEntity(dotRef.ValueRO.Dot);
                    }

                    unitTargetsNetcode.ValueRW.requestTargetEntity = hitEntity;
                    unitTargetsNetcode.ValueRW.requestActiveTargetSet = true;
                    unitTargetsNetcode.ValueRW.requestAttackMove = false;

                    unitTargetsNetcode.ValueRW.requestLastAppliedSequence++;
                }

                buffer.Playback(state.EntityManager);
                buffer.Dispose();
            }

            else
            {
                foreach (var unitTargetsNetcode in SystemAPI.Query<RefRW<UnitTargetsNetcode>>().WithAll<GhostOwnerIsLocal, Selected>())
                {
                    unitTargetsNetcode.ValueRW.requestTargetEntity = Entity.Null;
                }

                float formationWidth = math.length(cursorPosition - targetPosition);

                if (formationWidth < 2.5f)
                {
                    // === LOCKED vs AUTO-MIMIC formation ===

                    NativeList<float3> currentPositions = new NativeList<float3>(Allocator.Temp);
                    foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<GhostOwnerIsLocal, Selected>())
                        currentPositions.Add(transform.ValueRO.Position);

                    float3 center = float3.zero;
                    for (int i = 0; i < currentPositions.Length; i++) center += currentPositions[i];
                    center /= math.max(1, currentPositions.Length);

                    if (FormationUIState.IsLocked)
                    {
                        FormationGenerator generateFormation = FormationLibrary.Get(Formations.Locked);
                        NativeArray<float3> targetPositionArray = generateFormation(
                            targetPosition,
                            cursorPosition,
                            currentPositions.AsArray(),
                            arrayLength,
                            angleOffset,
                            Allocator.Temp
                        );

                        // Average facing of selected units
                        float3 summedForward = float3.zero;
                        int count = 0;
                        foreach (var transform in SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Unit, GhostOwnerIsLocal, Selected>())
                        {
                            summedForward += math.forward(transform.ValueRO.Rotation);
                            count++;
                        }
                        float3 averageForward = math.normalizesafe(summedForward / math.max(count, 1));
                        angleOffset = -math.atan2(averageForward.x, averageForward.z);

                        SetDestinations(targetPositionArray, angleOffset, ref state);
                    }
                    else
                    {
                        // AUTO-MIMIC: face perpendicular to the center->click direction
                        float3 direction = math.normalize(center - targetPosition);
                        angleOffset = math.atan2(direction.z, direction.x) + (math.PI * 0.5f);

                        NativeArray<float3> targetPositionArray = FormationLibrary.GenerateAutoMimic(
                            FormationUIState.SelectedFormation,
                            targetPosition,
                            arrayLength,
                            angleOffset,
                            Allocator.Temp
                        );

                        SetDestinations(targetPositionArray, angleOffset, ref state);
                    }
                    currentPositions.Dispose();
                }
                else
                {
                    // Manual (drag) formation
                    Formations selectedFormation = FormationUIState.SelectedFormation;
                    FormationGenerator generateFormation = FormationLibrary.Get(selectedFormation);
                    NativeArray<float3> emptyCurrentPositions = new NativeArray<float3>(0, Allocator.Temp);

                    NativeArray<float3> targetPositionArray = generateFormation(
                        targetPosition,
                        cursorPosition,
                        emptyCurrentPositions,
                        arrayLength,
                        angleOffset,
                        Allocator.Temp
                    );

                    var buffer = new EntityCommandBuffer(Allocator.Temp);

                    // Remove existing arrows
                    foreach (var (existingArrow, entity) in SystemAPI.Query<RefRO<FormationArrow>>().WithEntityAccess())
                        buffer.DestroyEntity(entity);

                    SetDestinations(targetPositionArray, angleOffset, ref state);

                    buffer.Playback(state.EntityManager);
                    buffer.Dispose();
                    emptyCurrentPositions.Dispose();
                }
            }
            entityQuery.Dispose();
        }
    }

    public void SetDestinations(NativeArray<float3> targetPositionArray, float angleOffset, ref SystemState state)
    {
        var buffer = new EntityCommandBuffer(Allocator.Temp);
        var references = SystemAPI.GetSingleton<EntitiesReferencesLukas>();
        int unitNumber = 0;

        foreach (var (unitTargetsNetcode, dotRef, unitEntity) in
                 SystemAPI.Query<RefRW<UnitTargetsNetcode>, RefRW<MovementDotRef>>()
                          .WithAll<GhostOwnerIsLocal, Selected>()
                          .WithEntityAccess())
        {
            if (unitNumber >= targetPositionArray.Length) break;

            float3 position = targetPositionArray[unitNumber];

            // === PLAYER INTENT (destination) ===
            unitTargetsNetcode.ValueRW.requestDestinationPosition = position;
            unitTargetsNetcode.ValueRW.requestDestinationRotation = -angleOffset;
            unitTargetsNetcode.ValueRW.requestActiveTargetSet = true;

            //AttackMove reads from UI
            unitTargetsNetcode.ValueRW.requestAttackMove = AttackMoveUIState.IsAttackMove;

            // Bump sequence
            unitTargetsNetcode.ValueRW.requestLastAppliedSequence++;

            // === Dots ===
            if (dotRef.ValueRO.Dot != Entity.Null)
                buffer.DestroyEntity(dotRef.ValueRO.Dot);

            Entity dotEntity = buffer.Instantiate(references.dotPrefabEntity);
            buffer.SetComponent(dotEntity, LocalTransform.FromPositionRotationScale(position, quaternion.identity, 0.2f));
            buffer.AddComponent(dotEntity, new MovementDot { owner = unitEntity });
            buffer.SetComponent(unitEntity, new MovementDotRef { Dot = dotEntity });

            // Sprint stub preserved for future:
            //unitTargetNetcode.ValueRW.requestIsRunning = (Time.time - lastClickTime) <= doubleClickThreshold;

            unitNumber++;
        }

        lastClickTime = Time.time;

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
        targetPositionArray.Dispose();
    }
}