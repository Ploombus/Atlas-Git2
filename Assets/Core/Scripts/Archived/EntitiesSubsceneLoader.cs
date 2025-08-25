/*
using UnityEngine;
using Unity.Entities;
using Unity.Scenes;

public class EntitiesSubsceneLoader : MonoBehaviour
{
    [SerializeField] private SubScene entitiesSubscene;

    public void LoadSubsceneIntoAllWorlds()
    {
        if (entitiesSubscene == null)
        {
            Debug.LogError("[EntitiesSubsceneLoader] Subscene reference is missing!");
            return;
        }

        var sceneGUID = entitiesSubscene.SceneGUID;

        foreach (var world in World.All)
        {
            var loadParams = new SceneSystem.LoadParameters { AutoLoad = true };
        
            try
            {
                SceneSystem.LoadSceneAsync(world.Unmanaged, sceneGUID, loadParams);
                Debug.Log($"[EntitiesSubsceneLoader] EntitiesSubscene loaded in world: {world.Name}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[EntitiesSubsceneLoader] LoadSceneAsync failed: {e.Message}");
            }
        }
    }
}
*/

// Just if I ever need to check if its loaded:
//var sceneSystemHandle = world.Unmanaged.GetExistingUnmanagedSystem<SceneSystem>();
//var sceneSystem = world.Unmanaged.GetUnsafeSystemRef<SceneSystem>(sceneSystemHandle);