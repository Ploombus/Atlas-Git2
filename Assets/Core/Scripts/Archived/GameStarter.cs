//comletely useless script at this point

/*
using Unity.Entities;
using UnityEngine;
using Unity.Scenes;
using UnityEngine.SceneManagement;
using System.Collections;

public class GameStarter : MonoBehaviour
{
    public static GameStarter Instance { get; private set; }

    [SerializeField] private SubScene gameSubscene;

    private void Awake()
    {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void StartGame()
    {
        StartCoroutine(StartGameCoroutine());
    }

    private IEnumerator StartGameCoroutine()
    {
        // Load Game Scene (additive)
        var asyncOp = SceneManager.LoadSceneAsync("GameScene", LoadSceneMode.Additive);
        while (!asyncOp.isDone)
            yield return null;

        // Load DOTS subscene into worlds
        LoadSubsceneInAllWorlds(gameSubscene.SceneGUID);
    }

    private void LoadSubsceneInAllWorlds(Unity.Entities.Hash128 sceneGUID)
    {
        foreach (var world in World.All)
        {
            if ((world.Flags & WorldFlags.Game) == 0)
                continue;

            var sceneSystemHandle = world.Unmanaged.GetExistingUnmanagedSystem<SceneSystem>();
            var sceneSystem = world.Unmanaged.GetUnsafeSystemRef<SceneSystem>(sceneSystemHandle);

            var loadParams = new SceneSystem.LoadParameters
            {
                AutoLoad = true
            };

            SceneSystem.LoadSceneAsync(world.Unmanaged, sceneGUID, loadParams);
        }
    }
}
*/