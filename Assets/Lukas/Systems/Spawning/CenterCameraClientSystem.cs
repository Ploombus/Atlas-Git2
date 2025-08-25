using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
partial struct CenterCameraClientSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        foreach (var (rpc, req, e) in
                 SystemAPI.Query<RefRO<CenterCameraRpc>, RefRO<ReceiveRpcCommandRequest>>().WithEntityAccess())
        {
            var pos = rpc.ValueRO.position;

            var rig = Object.FindFirstObjectByType<CameraRig>();

            if (rig != null)
            {
                var p = rig.transform.position;
                rig.transform.position = new Vector3(pos.x, p.y, pos.z);
            }

            ecb.DestroyEntity(e);
        }

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}