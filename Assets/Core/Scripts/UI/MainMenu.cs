using UnityEngine;
using UnityEngine.UIElements;
using Managers;
using System.Collections.Generic;
using Unity.Entities;

public class MainMenu : MonoBehaviour
{
    [SerializeField] UIDocument uiDocument;
    [SerializeField] GameObject _chat;
    private VisualElement root;


    public void Start()
    {

        root = uiDocument.rootVisualElement;

        var playButton = root.Q<VisualElement>("PlayButton");
        var exitButton = root.Q<VisualElement>("ExitButton");

        var hostButton = root.Q<VisualElement>("HostButton");
        var joinButton = root.Q<VisualElement>("JoinButton");
        var backToMainButton = root.Q<VisualElement>("BackToMainButton");
        var backHostPopupButton = root.Q<VisualElement>("BackHostPopupButton");
        var createLobbyButton = root.Q<VisualElement>("CreateLobbyButton");


        var joinCodeButton = root.Q<VisualElement>("JoinCodeButton");
        var joinLobbyCodeButton = root.Q<VisualElement>("JoinLobbyCodeButton");
        //var joinAdressButton = root.Q<VisualElement>("JoinAdressButton");
        //var joinLobbyAdressButton = root.Q<VisualElement>("JoinLobbyAdressButton");
        var backPopupButtons = root.Query<VisualElement>("BackPopupButton").ToList();
        var refreshButton = root.Q<VisualElement>("RefreshButton");
        var backToPlayButton = root.Q<VisualElement>("BackToPlayButton");


        var leaveLobbyButton = root.Q<VisualElement>("LeaveLobbyButton");
        var startGameButton = root.Q<VisualElement>("StartGameButton");


        playButton.RegisterCallback<ClickEvent>(ToPlayMenu);
        exitButton.RegisterCallback<ClickEvent>(QuitGame);

        hostButton.RegisterCallback<ClickEvent>(HostPopup);
        joinButton.RegisterCallback<ClickEvent>(ToJoinMenu);
        backToMainButton.RegisterCallback<ClickEvent>(BackToMainMenu);
        backHostPopupButton.RegisterCallback<ClickEvent>(BackHostPopup);
        createLobbyButton.RegisterCallback<ClickEvent>(CreateLobby);

        joinCodeButton.RegisterCallback<ClickEvent>(CodePopup);
        joinLobbyCodeButton.RegisterCallback<ClickEvent>(JoinLobbyCode);
        //joinAdressButton.RegisterCallback<ClickEvent>(AdressPopup);
        //joinLobbyAdressButton.RegisterCallback<ClickEvent>(JoinLobbyAdress);
        foreach (var backPopupButton in backPopupButtons)
            backPopupButton.RegisterCallback<ClickEvent>(BackPopup);
        refreshButton.RegisterCallback<ClickEvent>(Refresh);
        backToPlayButton.RegisterCallback<ClickEvent>(BackToPlayMenu);

        leaveLobbyButton.RegisterCallback<ClickEvent>(LeaveLobby);
        startGameButton.RegisterCallback<ClickEvent>(StartGame);
    }

    private void ToPlayMenu(ClickEvent evt)
    {
        var mainMenu = root.Q<VisualElement>("MainMenu");
        var playMenu = root.Q<VisualElement>("PlayMenu");
        mainMenu.style.display = DisplayStyle.None;
        playMenu.style.display = DisplayStyle.Flex;
    }
    private void BackToMainMenu(ClickEvent evt)
    {
        var mainMenu = root.Q<VisualElement>("MainMenu");
        var playMenu = root.Q<VisualElement>("PlayMenu");
        playMenu.style.display = DisplayStyle.None;
        mainMenu.style.display = DisplayStyle.Flex;
    }
    private async void ToJoinMenu(ClickEvent evt)
    {
        var joinMenu = root.Q<VisualElement>("JoinMenu");
        var playMenu = root.Q<VisualElement>("PlayMenu");
        joinMenu.style.display = DisplayStyle.Flex;
        playMenu.style.display = DisplayStyle.None;

        await LobbyManager.instance.ListLobbies();
    }
    private void BackToPlayMenu(ClickEvent evt)
    {
        var playMenu = root.Q<VisualElement>("PlayMenu");
        var joinMenu = root.Q<VisualElement>("JoinMenu");
        playMenu.style.display = DisplayStyle.Flex;
        joinMenu.style.display = DisplayStyle.None;
    }
    private void AdressPopup(ClickEvent evt)
    {
        var adressPopup = root.Q<VisualElement>("AdressPopup");
        adressPopup.style.display = DisplayStyle.Flex;
    }
    private void CodePopup(ClickEvent evt)
    {
        var codePopup = root.Q<VisualElement>("CodePopup");
        codePopup.style.display = DisplayStyle.Flex;
    }
    private void BackPopup(ClickEvent evt)
    {
        var adressPopup = root.Q<VisualElement>("AdressPopup");
        var codePopup = root.Q<VisualElement>("CodePopup");
        adressPopup.style.display = DisplayStyle.None;
        codePopup.style.display = DisplayStyle.None;
    }
    private void HostPopup(ClickEvent evt)
    {
        var hostPopup = root.Q<VisualElement>("HostPopup");
        hostPopup.style.display = DisplayStyle.Flex;
    }
    private void BackHostPopup(ClickEvent evt)
    {
        var hostPopup = root.Q<VisualElement>("HostPopup");
        hostPopup.style.display = DisplayStyle.None;
    }



    private void CreateLobby(ClickEvent evt)
    {
        var lobbyNameInput = root.Q<TextField>("LobbyNameInput");
        var name = lobbyNameInput.text;

        if (name != "")
        {
            //Creates Lobby
            LobbyManager.instance.CreateLobby(name);

        }
        else
        {
            Debug.Log("Invalid lobby name.");
        }
    }

    private void JoinLobbyCode(ClickEvent evt)
    {
        var codeField = root.Q<TextField>("CodeField");
        var lobbyCode = codeField.text;

        if (lobbyCode != "")
        {
            //Joins Lobby
            LobbyManager.instance.JoinLobbyByCode(lobbyCode);
        }
    }

    private void LeaveLobby(ClickEvent evt)
    {
        //Leaves Lobby
        LobbyManager.instance.LeaveLobby();

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
        World.DefaultGameObjectInjectionWorld = null;

        var playMenu = root.Q<VisualElement>("PlayMenu");
        var lobby = root.Q<VisualElement>("Lobby");
        playMenu.style.display = DisplayStyle.Flex;
        lobby.style.display = DisplayStyle.None;

        //Disables Chat
        _chat.SetActive(false);
        Debug.Log("Chat Disabled.");
    }

    private void StartGame(ClickEvent evt)
    {
        Debug.Log("Starting game...");

        StartGameRpcServerSystem.BroadcastStartGameRpc(WorldManager.GetServerWorld().EntityManager);
    }

    private async void Refresh(ClickEvent evt)
    {
        await LobbyManager.instance.ListLobbies();
    }

    private void QuitGame(ClickEvent evt)
    {
        Debug.Log("Closing game...");
        Application.Quit();
    }

}



// Interesting code, leaving it here for reference
/* 
    private void CopySystemsToServer()
    {
        var clientEntityManager = WorldManager.GetClientWorld().EntityManager;
        var serverEntityManager = WorldManager.GetServerWorld().EntityManager;

        // Copy the singleton entity from client to server
        if (clientEntityManager.CreateEntityQuery(typeof(EntitiesReferences)).CalculateEntityCount() == 1)
        {
            var clientEntity = clientEntityManager.CreateEntityQuery(typeof(EntitiesReferences)).GetSingletonEntity();
            var data = clientEntityManager.GetComponentData<EntitiesReferences>(clientEntity);

            var serverEntity = serverEntityManager.CreateEntity();
            serverEntityManager.AddComponentData(serverEntity, data);
        }
        else
        {
            Debug.LogError("Could not find EntitiesReferences in client world to copy to server.");
        }
    }

*/

//maybe maybe maybe
/*
private void JoinLobbyAdress(ClickEvent evt)
{
    var joinMenu = root.Q<VisualElement>("JoinMenu");
    var lobby = root.Q<VisualElement>("Lobby");
    joinMenu.style.display = DisplayStyle.None;
    lobby.style.display = DisplayStyle.Flex;

    //Disables popup
    var adressPopup = root.Q<VisualElement>("AdressPopup");
    adressPopup.style.display = DisplayStyle.None;

    //Enables Chat
    _chat.SetActive(true);
}
*/
