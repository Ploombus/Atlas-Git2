using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using UnityEngine;
using Extension;

namespace Managers {
    public class RelayInitializer : MonoBehaviour {
        private static RelayServerData? _relayServerData, _relayClientData;
        private static Action OnConnectionComplete;
        public static string _joinCode;
    
        // Singleton (without duplicate destroy logic) for access to the class instance from within the static methods
        private static RelayInitializer Instance { get; set; }
        protected void Awake() => Instance = this;
        
        // HOSTING

        public static void StartHost() => Instance.StartCoroutine(InitializeHost());

        private static IEnumerator InitializeHost() {
            var initializeTask = UnityServices.InitializeAsync();
            while (!initializeTask.IsCompleted)
                yield return null;
            if (ProcessTaskFail(initializeTask, nameof(initializeTask)))
                yield break;

            var signInTask = Task.CompletedTask;
            if (!AuthenticationService.Instance.IsSignedIn) {
                signInTask = AuthenticationService.Instance.SignInAnonymouslyAsync();
                while (!signInTask.IsCompleted)
                    yield return null; 
            }
            if (ProcessTaskFail(signInTask, nameof(signInTask)))
                yield break;

            var allocationTask = RelayService.Instance.CreateAllocationAsync(5);
            while (!allocationTask.IsCompleted)
                yield return null;
            if (ProcessTaskFail(allocationTask, nameof(allocationTask)))
                yield break;

            var joinCodeTask = RelayService.Instance.GetJoinCodeAsync(allocationTask.Result.AllocationId);
            while (!joinCodeTask.IsCompleted)
                yield return null;
            if (ProcessTaskFail(joinCodeTask, nameof(joinCodeTask)))
                yield break;

            _joinCode = joinCodeTask.Result;

            try {
                Debug.Log("Hosting relay data");
                _relayServerData = RelayServerDataHelper.RelayData(allocationTask.Result);
            } catch (Exception e) {
                Debug.LogException(e);
                _relayServerData = null;
                yield break;
            }

            Debug.Log("Success, players may now connect");
            while (_relayServerData == null || (!_relayServerData?.Endpoint.IsValid ?? false))
                yield return null;
                
            yield return JoinUsingCode(_joinCode);
            yield return WaitRelayConnection();
            SetupRelayHostedServerAndConnect();
        }

        private static void SetupRelayHostedServerAndConnect() {
            if (ClientServerBootstrap.RequestedPlayType != ClientServerBootstrap.PlayType.ClientAndServer) {
                UnityEngine.Debug.LogError(
                    $"Creating client/server worlds is not allowed if playmode is set to {ClientServerBootstrap.RequestedPlayType}");
                 return;
             }

            var relayServerData = _relayServerData.GetValueOrDefault();
            var relayClientData = _relayClientData.GetValueOrDefault();

            var oldConstructor = NetworkStreamReceiveSystem.DriverConstructor;
            NetworkStreamReceiveSystem.DriverConstructor = new RelayDriverConstructor(relayServerData, relayClientData);
            var server = ClientServerBootstrap.CreateServerWorld("ServerWorld");
            WorldManager.RegisterServerWorld(server);
            var client = ClientServerBootstrap.CreateClientWorld("ClientWorld");
            WorldManager.RegisterClientWorld(client);
            NetworkStreamReceiveSystem.DriverConstructor = oldConstructor;

            

            WorldManager.DestroyLocalSimulationWorld();
            World.DefaultGameObjectInjectionWorld ??= server;

            // Load scene here if you want to.

            var networkStreamEntity =
                server.EntityManager.CreateEntity(ComponentType.ReadWrite<NetworkStreamRequestListen>());
            server.EntityManager.SetName(networkStreamEntity, "NetworkStreamRequestListen");
            server.EntityManager.SetComponentData(networkStreamEntity,
                new NetworkStreamRequestListen { Endpoint = NetworkEndpoint.AnyIpv4 });

            networkStreamEntity =
                client.EntityManager.CreateEntity(ComponentType.ReadWrite<NetworkStreamRequestConnect>());
            client.EntityManager.SetName(networkStreamEntity, "NetworkStreamRequestConnect");
            client.EntityManager.SetComponentData(networkStreamEntity,
                new NetworkStreamRequestConnect { Endpoint = relayClientData.Endpoint });
            
            ProcessConnectionComplete();
        }

        // CONNECTING
    
        public static void ConnectByCode(string joinCode) => Instance.StartCoroutine(ProcessCodeConnection(joinCode));
    
        private static IEnumerator ProcessCodeConnection(string joinCode) {
            Instance.StartCoroutine(JoinExternalServer(joinCode));
            yield return WaitRelayConnection();
            ConnectToRelayServer();
        }

        private static IEnumerator WaitRelayConnection() {
            while (_relayClientData == null || (!_relayClientData?.Endpoint.IsValid ?? false))
                yield return null;
        }
        
        private static IEnumerator JoinExternalServer(string joinCode) {
            Debug.Log("Waiting for relay response");
            var setupTask = UnityServices.InitializeAsync();

            while (!setupTask.IsCompleted)
                yield return null;
            
            var signInTask = Task.CompletedTask;
            if (!AuthenticationService.Instance.IsSignedIn) {
                signInTask = AuthenticationService.Instance.SignInAnonymouslyAsync();
                while (!signInTask.IsCompleted)
                    yield return null;
            }
            if (ProcessTaskFail(signInTask, nameof(signInTask)))
                yield break;

            yield return JoinUsingCode(joinCode);
        }

        private static IEnumerator JoinUsingCode(string joinCode) {
            Debug.Log($"Joining relay with code: {joinCode}");

            var joinTask = RelayService.Instance.JoinAllocationAsync(joinCode);

            float timeout = 10f;
            while (!joinTask.IsCompleted && timeout > 0f) {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (timeout <= 0f) {
                Debug.LogError("Relay join timed out.");
                yield break;
            }

            if (ProcessTaskFail(joinTask, nameof(joinTask))) {
                Debug.LogError("JoinAllocationAsync failed.");
                yield break;
            }

            Debug.Log("Relay JoinAllocation successful");

            try {
                _relayClientData = RelayServerDataHelper.RelayData(joinTask.Result);
            } catch (Exception e) {
                Debug.LogException(e);
                _relayClientData = null;
            }

            _joinCode = joinCode;
        }
    
        private static void ConnectToRelayServer() {
            var relayClientData = _relayClientData.GetValueOrDefault();
            
            var oldConstructor = NetworkStreamReceiveSystem.DriverConstructor;
            NetworkStreamReceiveSystem.DriverConstructor =
                new RelayDriverConstructor(new RelayServerData(), relayClientData);
            var client = ClientServerBootstrap.CreateClientWorld("ClientWorld");
            WorldManager.RegisterClientWorld(client);
            NetworkStreamReceiveSystem.DriverConstructor = oldConstructor;

            WorldManager.DestroyLocalSimulationWorld();
            World.DefaultGameObjectInjectionWorld ??= client;
        
            var networkStreamEntity =
                client.EntityManager.CreateEntity(ComponentType.ReadWrite<NetworkStreamRequestConnect>());
            client.EntityManager.SetName(networkStreamEntity, "NetworkStreamRequestConnect");
            client.EntityManager.SetComponentData(networkStreamEntity,
                new NetworkStreamRequestConnect { Endpoint = relayClientData.Endpoint });
            Debug.Log("Connected to Relay server!");
            ProcessConnectionComplete();
        }

        // COMMON
    
        private static bool ProcessTaskFail(Task task, string taskName) {
            if (!task.IsFaulted) return false;
            Debug.LogError($"Task {taskName} failed.");
            Debug.LogException(task.Exception);
            return true;
        }

        public static void SubscribeToConnectionComplete(Action handler) => OnConnectionComplete += handler;

        public static void ProcessConnectionComplete() {
            if (OnConnectionComplete == null)
                return;
            OnConnectionComplete?.Invoke();
            foreach (var handler in OnConnectionComplete?.GetInvocationList()!)
                OnConnectionComplete -= (Action)handler;
        }
    }

    public class RelayDriverConstructor : INetworkStreamDriverConstructor {
        private RelayServerData _relayServerData, _relayClientData;
    
        public RelayDriverConstructor(RelayServerData serverData, RelayServerData clientData) {
            _relayServerData = serverData;
            _relayClientData = clientData;
        }

        public void CreateClientDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug) {
            var settings = DefaultDriverBuilder.GetNetworkSettings();
            settings.WithRelayParameters(ref _relayClientData);
            DefaultDriverBuilder.RegisterClientUdpDriver(world, ref driverStore, netDebug, settings);
        }

        public void CreateServerDriver(World world, ref NetworkDriverStore driverStore, NetDebug netDebug) =>
            DefaultDriverBuilder.RegisterServerDriver(world, ref driverStore, netDebug, ref _relayServerData);
    }
}