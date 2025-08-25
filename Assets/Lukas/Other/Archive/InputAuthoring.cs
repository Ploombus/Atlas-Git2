/*
using UnityEngine;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Entities;

public class InputAuthoring : MonoBehaviour
{
    public class Baker : Baker<InputAuthoring>
    {
        public override void Bake(InputAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new NetcodePlayerInput());
        }
    }
}

public struct NetcodePlayerInput : IInputComponentData
{
    [GhostField]
    public float3 targetPosition;

    [GhostField]
    public bool targetSet;
}
*/