using Unity.Entities;
using Unity.NetCode;

[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ServerSimulation)]
public partial struct GameplayStateInitSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        var em = state.EntityManager;
        if (!em.CreateEntityQuery(typeof(CheckGameplayState)).IsEmpty)
            return;

        var entity = em.CreateEntity();
        em.AddComponentData(entity, new CheckGameplayState { GameplayState = false });
    }
}
public struct CheckGameplayState : IComponentData
{
    public bool GameplayState;
}