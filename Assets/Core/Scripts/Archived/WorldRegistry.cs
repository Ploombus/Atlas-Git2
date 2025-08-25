/*

using UnityEngine;
using Unity.Entities;

public static class WorldRegistry
{
    public static World ServerWorld;
    public static World ClientWorld;

    public static void Register(World world)
    {
        if (world.Flags.HasFlag(WorldFlags.GameServer))
            ServerWorld = world;

        if (world.Flags.HasFlag(WorldFlags.GameClient))
            ClientWorld = world;
    }
}

*/