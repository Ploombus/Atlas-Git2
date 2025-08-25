/*
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using Managers;

[UpdateInGroup(typeof(PredictedSimulationSystemGroup))]
partial struct MovementSystem : ISystem
{

    //[BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        //System check
        bool isInGame = CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld());
        if(isInGame == false) return;

        foreach ((
            RefRO<NetcodePlayerInput> netcodePlayerInput,
            RefRO<UnitMover> unitMover,
            RefRW<LocalTransform> localTransform)
            in SystemAPI.Query<
                RefRO<NetcodePlayerInput>,
                RefRO<UnitMover>,
                RefRW<LocalTransform>>().WithAll<Simulate>())
        {
            float moveSpeed = unitMover.ValueRO.moveSpeed / 2;
            float3 moveVector = new float3(netcodePlayerInput.ValueRO.inputVector.x, 0, netcodePlayerInput.ValueRO.inputVector.y);
            localTransform.ValueRW.Position += moveVector * moveSpeed * SystemAPI.Time.DeltaTime;
        }
    }
}
*/