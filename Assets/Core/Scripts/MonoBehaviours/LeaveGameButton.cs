using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using Unity.Entities;

public class LeaveGameButton : MonoBehaviour
{

    [SerializeField] private Button leaveButton;

    private void Awake()
    {
        leaveButton.onClick.AddListener(LeaveGame); //maybe the listener on button gives object null exception od disconnect???
    }
    private void LeaveGame()
    {
        Debug.Log("Leaving game...");
        foreach (var world in World.All)
        {
            if (world.IsCreated && (world.Flags & WorldFlags.Game) != 0)
            {
                CheckGameplayStateAccess.SetGameplayState(world, false); //Or maybe even this??
            }
        }
        DisposeWorlds();
        SceneManager.LoadScene("MenuScene", LoadSceneMode.Additive);
        SceneManager.UnloadSceneAsync("GameScene");
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
        World.DefaultGameObjectInjectionWorld = null;
    }

    private void OnDestroy()
    {
        leaveButton.onClick.RemoveListener(LeaveGame);
    }

}
