using UnityEngine;
using UnityEngine.UI;
using Managers;
using Unity.Entities;
//using UnityEngine.SceneManagement;

public class SinglePlayerTesterButton : MonoBehaviour
{

    [SerializeField] private Button testerButton;

    private void Awake()
    {
        testerButton.onClick.AddListener(TestGame);
    }

    private void TestGame()
    {
        Debug.Log("Testing game...");

        //SceneManager.LoadScene("GameScene", LoadSceneMode.Additive);z

        foreach (var world in World.All)
        {
            WorldManager.Register(world);
            CheckGameplayStateAccess.SetGameplayState(world, true);
        }
    }
}