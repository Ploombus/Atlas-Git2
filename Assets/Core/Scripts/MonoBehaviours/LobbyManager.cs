using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Collections;
using System.Threading.Tasks;
using UnityEngine.UIElements;
using System.Collections.Generic;
using Unity.NetCode;
using Managers;
using Unity.Entities;
using System;
using System.Threading;


public class LobbyManager : MonoBehaviour
{
    [SerializeField] UIDocument uiDocument;
    [SerializeField] GameObject _chat;

    
    public static LobbyManager instance;

    private Unity.Services.Lobbies.Models.Lobby hostLobby;
    private Unity.Services.Lobbies.Models.Lobby joinedLobby;
    private float heartbeatTimer;
    private float lobbyUpdateTimer;
    private string playerName;


    private void Start()
    {
        instance = this;
        playerName = "Barrel" + UnityEngine.Random.Range(10, 99);
    }

    private void Update()
    {
        LobbyHeartbeat();
        LobbyPollForUpdates();
    }

    private async void LobbyPollForUpdates()
    {
        if (joinedLobby != null)
        {
            lobbyUpdateTimer -= Time.deltaTime;
            if (lobbyUpdateTimer < 0f)
            {
                float lobbyUpdateTimerMax = 2.2f;
                lobbyUpdateTimer = lobbyUpdateTimerMax;

                Unity.Services.Lobbies.Models.Lobby lobby = await LobbyService.Instance.GetLobbyAsync(joinedLobby.Id);
                joinedLobby = lobby;

                ListPlayers();
            }
        }
    }
 
    private async void LobbyHeartbeat()
    {
        if (hostLobby != null)
        {
            heartbeatTimer -= Time.deltaTime;
            if (heartbeatTimer < 0f)
            {
                float heartbeatTimerMax = 15;
                heartbeatTimer = heartbeatTimerMax;

                await LobbyService.Instance.SendHeartbeatPingAsync(hostLobby.Id);
                //Debug.Log("tick beat...");
            }
        }
    }

    public async void CreateLobby(string name)
    {
        try
        {
            int maxPlayers = 4;
            CreateLobbyOptions createLobbyOptions = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = GetPlayer()
            };

            var lobby = await LobbyService.Instance.CreateLobbyAsync(name, maxPlayers, createLobbyOptions);

            hostLobby = lobby;
            joinedLobby = hostLobby;

            Debug.Log("Created Lobby :: " + lobby.Name + " :: Max players: " + lobby.MaxPlayers + " :: Code: " + lobby.LobbyCode);


            //UI Init - had to move it here cause "reasons"
            VisualElement root;
            root = uiDocument.rootVisualElement;
            var labelCode = root.Q<Label>("LobbyCodeLabel");
            labelCode.text = lobby.LobbyCode;
            var lobbyNameLabel = uiDocument.rootVisualElement.Q<Label>("LobbyName");
            lobbyNameLabel.text = $"Lobby ({lobby.Name})";
            var playMenu = root.Q<VisualElement>("PlayMenu");
            var hostPopup = root.Q<VisualElement>("HostPopup");
            var lobbyMenu = root.Q<VisualElement>("Lobby");
            playMenu.style.display = DisplayStyle.None;
            hostPopup.style.display = DisplayStyle.None;
            lobbyMenu.style.display = DisplayStyle.Flex;

            
            var startGameButton = root.Q<VisualElement>("StartGameButton");
            startGameButton.style.display = DisplayStyle.None;

            // Delete original worlds
            DisposeWorlds();

            //RELAY
            RelayInitializer.SubscribeToConnectionComplete(RelayAutoConnect);
            RelayInitializer.StartHost();

            //Enables Chat
            _chat.SetActive(true);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    /*public async void TestGame(string name)
    {
        try
        {
            int maxPlayers = 4;
            CreateLobbyOptions createLobbyOptions = new CreateLobbyOptions
            {
                IsPrivate = false,
                Player = GetPlayer()
            };

            var lobby = await LobbyService.Instance.CreateLobbyAsync(name, maxPlayers, createLobbyOptions);

            hostLobby = lobby;
            joinedLobby = hostLobby;

            Debug.Log("Created Lobby :: " + lobby.Name + " :: Max players: " + lobby.MaxPlayers + " :: Code: " + lobby.LobbyCode);

            // Delete original worlds
            DisposeWorlds();

            //RELAY
            RelayInitializer.SubscribeToConnectionComplete(RelayTestAutoConnect);
            RelayInitializer.StartHost();

            //Enables Chat
            _chat.SetActive(true);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }*/

    public async void JoinLobbyByCode(string lobbyCode)
    {
        try
        {
            JoinLobbyByCodeOptions joinLobbyByCodeOptions = new JoinLobbyByCodeOptions
            {
                Player = GetPlayer()
            };
            Unity.Services.Lobbies.Models.Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode, joinLobbyByCodeOptions);

            joinedLobby = lobby;

            // Delete original worlds
            DisposeWorlds();

            // RELAY
            if (joinedLobby.Data.TryGetValue("joinCode", out var relayCodeData))
            {
                var relayJoinCode = relayCodeData.Value;
                RelayInitializer.ConnectByCode(relayJoinCode);
            }
            else
            {
                Debug.LogWarning("Relay join code not found in lobby data.");
            }


            //UI Init - had to move it here cause "reasons"
            VisualElement root;
            root = uiDocument.rootVisualElement;
            var joinMenu = root.Q<VisualElement>("JoinMenu");
            var lobbyT = root.Q<VisualElement>("Lobby");
            joinMenu.style.display = DisplayStyle.None;
            lobbyT.style.display = DisplayStyle.Flex;
            var labelCode = root.Q<Label>("LobbyCodeLabel");
            labelCode.text = lobby.LobbyCode;
            var labelName = root.Q<Label>("LobbyName");
            labelName.text = $"Lobby ({lobby.Name})";
            var startGameButton = root.Q<VisualElement>("StartGameButton");
            startGameButton.style.display = DisplayStyle.None;
            var codePopup = root.Q<VisualElement>("CodePopup");
            codePopup.style.display = DisplayStyle.None;


            //Enables Chat
            _chat.SetActive(true);
        }
        catch (LobbyServiceException)
        {
            Debug.Log("Unable to join lobby.");
        }
    }

    public async void JoinLobbyByClick(Lobby lobby)
    {
        try
        {
            Unity.Services.Lobbies.Models.Player player = GetPlayer();
            joinedLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobby.Id, new JoinLobbyByIdOptions
            {
                Player = player
            });

            // Delete original worlds
            DisposeWorlds();

            // RELAY
            if (joinedLobby.Data.TryGetValue("joinCode", out var relayCodeData))
            {
                var relayJoinCode = relayCodeData.Value;
                RelayInitializer.ConnectByCode(relayJoinCode);
            }
            else
            {
                Debug.LogWarning("Relay join code not found in lobby data.");
            }


            //UI Init - had to move it here cause "reasons"
            VisualElement root;
            root = uiDocument.rootVisualElement;
            var joinMenu = root.Q<VisualElement>("JoinMenu");
            var lobbyT = root.Q<VisualElement>("Lobby");
            joinMenu.style.display = DisplayStyle.None;
            lobbyT.style.display = DisplayStyle.Flex;
            var labelCode = root.Q<Label>("LobbyCodeLabel");
            labelCode.text = joinedLobby.LobbyCode;
            var labelName = root.Q<Label>("LobbyName");
            labelName.text = $"Lobby ({joinedLobby.Name})";

            var startGameButton = root.Q<VisualElement>("StartGameButton");
            startGameButton.style.display = DisplayStyle.None;

            //Enables Chat
            _chat.SetActive(true);
        }
        catch (LobbyServiceException)
        {
            Debug.Log("Unable to join lobby.");
        }
    }

    private Unity.Services.Lobbies.Models.Player GetPlayer()
    {
        return new Unity.Services.Lobbies.Models.Player
        {
            Data = new Dictionary<string, PlayerDataObject>
            {
                {"PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName)}
            }
        };
    }

    private void ListPlayers()
    {
        ListPlayers(joinedLobby);
    }
    private void ListPlayers(Unity.Services.Lobbies.Models.Lobby lobby)
    {
        try
        {
            VisualElement root;
            root = uiDocument.rootVisualElement;
            var color = new Color(0.1333333f, 0.5450981f, 0.1333333f, 1f);
            var playerList = root.Q<ScrollView>("JoinedPlayersList");

            playerList.Clear();

            if (lobby.Players != null)
            {
                foreach (Unity.Services.Lobbies.Models.Player player in lobby.Players)
                {
                    var label = new Label { text = $" {player.Data["PlayerName"].Value}" };
                    label.style.backgroundColor = color;
                    playerList.Add(label);
                }
            }
        }
        catch (Exception)
        {
            
        }
    }
    public async Task ListLobbies()
    {
        try
        {
            VisualElement root;
            root = uiDocument.rootVisualElement;
            var color = new Color(0.1333333f, 0.5450981f, 0.1333333f, 1f);
            var lobbyList = root.Q<ScrollView>("LobbyList");
            var queryResponse = await LobbyService.Instance.QueryLobbiesAsync();

            lobbyList.Clear();

            if (queryResponse.Results != null)
            {
                foreach (Unity.Services.Lobbies.Models.Lobby lobby in queryResponse.Results)
                {
                    var label = new Label { text = $"  {lobby.Name} :: {lobby.MaxPlayers - lobby.AvailableSlots}/{lobby.MaxPlayers} Players" };
                    label.style.backgroundColor = color;
                    label.RegisterCallback<ClickEvent>(evt => JoinLobbyByClick(lobby));
                    lobbyList.Add(label);
                }
            }
        }
        catch (Exception e)
        {
            Debug.Log(e);
        }
    }
    public async void LeaveLobby()
    {
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId);
            hostLobby = null;
            joinedLobby = null;
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private async void RelayAutoConnect()
    {
        //This function ensures that players get the Relay code and join it automaticaly on joining the Lobby (maybe)
        try
        {
            if (string.IsNullOrEmpty(RelayInitializer._joinCode))
            {
                Debug.LogError("Join code is null or empty.");
                return;
            }

            var data = new Dictionary<string, DataObject>
        {
            {
                "joinCode",
                new DataObject(DataObject.VisibilityOptions.Member, RelayInitializer._joinCode)
            }
        };

            var lobby = await LobbyService.Instance.UpdateLobbyAsync(hostLobby.Id, new UpdateLobbyOptions { Data = data });
            hostLobby = lobby;
            joinedLobby = lobby;

            Debug.Log($"Lobby updated with join code: {RelayInitializer._joinCode}");
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to update lobby with join code: {e.Message}");
        }
        VisualElement root;
        root = uiDocument.rootVisualElement;
        var startGameButton = root.Q<VisualElement>("StartGameButton");
        startGameButton.style.display = DisplayStyle.Flex;
    }
    private async void RelayTestAutoConnect()
    {
        //This function ensures that players get the Relay code and join it automaticaly on joining the Lobby (maybe)
        try
        {
            if (string.IsNullOrEmpty(RelayInitializer._joinCode))
            {
                Debug.LogError("Join code is null or empty.");
                return;
            }

            var data = new Dictionary<string, DataObject>
        {
            {
                "joinCode",
                new DataObject(DataObject.VisibilityOptions.Member, RelayInitializer._joinCode)
            }
        };

            var lobby = await LobbyService.Instance.UpdateLobbyAsync(hostLobby.Id, new UpdateLobbyOptions { Data = data });
            hostLobby = lobby;
            joinedLobby = lobby;

            Debug.Log($"Lobby updated with join code: {RelayInitializer._joinCode}");
        }
        catch (LobbyServiceException e)
        {
            Debug.LogError($"Failed to update lobby with join code: {e.Message}");
        }
    }

    private async Task UpdatePlayerName(string newPlayerName)
    {
        try
        {
            playerName = newPlayerName;
            await LobbyService.Instance.UpdatePlayerAsync(joinedLobby.Id, AuthenticationService.Instance.PlayerId, new UpdatePlayerOptions
            {
                Data = new Dictionary<string, PlayerDataObject>
                {
                    { "PlayerName", new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, playerName) }
                }
            });
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private async void KickPlayer()
    {
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(joinedLobby.Id, joinedLobby.Players[1].Id);
        }
        catch (LobbyServiceException e)
        {
            Debug.Log(e);
        }
    }

    private void DisposeWorlds()
    {
        var worlds = new List<World>();
        foreach (var world in World.All)
        {
            if (world.Flags.HasFlag(WorldFlags.GameClient) || world.Flags.HasFlag(WorldFlags.GameServer))
            {
                worlds.Add(world);
            }
        }
        foreach (var world in worlds)
        {
            world.Dispose();
        }
    }
    
}
