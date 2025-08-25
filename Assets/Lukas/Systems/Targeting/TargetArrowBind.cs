using Unity.Entities;
using Unity.Mathematics;

public struct TargetArrowBind : IComponentData
{
    public Entity target;   // the entity this arrow follows
}