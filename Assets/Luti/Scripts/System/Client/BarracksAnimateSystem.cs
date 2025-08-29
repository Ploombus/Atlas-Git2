using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Mathematics;
using Unity.Physics;
using Unity.NetCode;
using Managers;

[UpdateInGroup(typeof(PresentationSystemGroup), OrderFirst = true)]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial struct BarracksAnimateSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()) == false) return;

        var buffer = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);

        // Init animator
        foreach (var (BarracksGameObjectPrefab, localTransform, entity) in
                 SystemAPI.Query<BarracksGameObjectPrefab, LocalTransform>()
                          .WithNone<BarracksAnimatorReference>()
                          .WithEntityAccess())
        {
            var barracksMesh = Object.Instantiate(BarracksGameObjectPrefab.Value);

            // Temporarily hide visuals (fix for 0,0,0 Tpose)
            var renderers = barracksMesh.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false;

            barracksMesh.transform.SetPositionAndRotation(localTransform.Position, localTransform.Rotation);
            var anim = barracksMesh.GetComponent<Animator>();
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Update(0f);

            // Reveal visuals
            for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = true;

            buffer.AddComponent(entity, new BarracksAnimatorReference { Value = anim });
        }

        buffer.Playback(state.EntityManager);
        buffer.Dispose();
    }
}
