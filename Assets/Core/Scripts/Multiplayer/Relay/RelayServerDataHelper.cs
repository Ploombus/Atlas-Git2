using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Relay.Models;

namespace Extension {
	public static class RelayServerDataHelper {
		private static RelayServerData GetRelayData(List<RelayServerEndpoint> endpoints, byte[] allocationIdBytes, 
			byte[] connectionDataBytes, byte[] hostConnectionDataBytes, byte[] keyBytes) {
			var endpoint = endpoints.FirstOrDefault(e => e.ConnectionType == "dtls")
			               ?? throw new InvalidOperationException($"endpoint for connectionType dtls not found");
       
			var server = NetworkEndpoint.Parse(endpoint.Host, (ushort)endpoint.Port);
    
			var allocationId = RelayAllocationId.FromByteArray(allocationIdBytes);
			var connData = RelayConnectionData.FromByteArray(connectionDataBytes);
			var hostData = RelayConnectionData.FromByteArray(hostConnectionDataBytes);
			var key = RelayHMACKey.FromByteArray(keyBytes);

			return new RelayServerData(ref server, 0, ref allocationId, ref connData, ref hostData, ref key, true); 
		}

		public static RelayServerData RelayData(JoinAllocation a) =>
			GetRelayData(a.ServerEndpoints, a.AllocationIdBytes, a.ConnectionData, a.HostConnectionData, a.Key);

		public static RelayServerData RelayData(Allocation a) =>
			GetRelayData(a.ServerEndpoints, a.AllocationIdBytes, a.ConnectionData, a.ConnectionData, a.Key);
	}
}