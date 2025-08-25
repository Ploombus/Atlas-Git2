using System.Diagnostics.CodeAnalysis;
using Unity.Entities;

namespace Managers
{
	public static class WorldManager
	{
		private static World _clientWorld, _serverWorld;

		[SuppressMessage("ReSharper", "ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator")]
		public static void DestroyLocalSimulationWorld()
		{
			foreach (var world in World.All)
			{
				if (world.Flags != WorldFlags.Game) continue;
				world.Dispose();
				break;
			}
		}

		// Legacy-style registration
		public static void Register(World world)
		{
			if (world.Flags.HasFlag(WorldFlags.GameClient))
				_clientWorld = world;

			if (world.Flags.HasFlag(WorldFlags.GameServer))
				_serverWorld = world;
		}

		// Explicit methods (for Relay setup)
		public static void RegisterServerWorld(World world) => _serverWorld = world;
		public static void RegisterClientWorld(World world) => _clientWorld = world;

		// Accessors
		public static World GetServerWorld()
		{
			if (_serverWorld == null || !_serverWorld.IsCreated)
				return null;

			return _serverWorld;
		}

		public static World GetClientWorld()
		{
			if (_clientWorld == null || !_clientWorld.IsCreated)
				return null;

			return _clientWorld;
		}

		public static void Clear()
		{
			_clientWorld = null;
			_serverWorld = null;
		}
	}
}