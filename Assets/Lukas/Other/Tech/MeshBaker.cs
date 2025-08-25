#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[ExecuteInEditMode]
public class MultiMeshBaker : MonoBehaviour
{
    [ContextMenu("Bake All Skinned Meshes Under This Object")]
    void BakeAll()
    {
        var skinnedMeshes = GetComponentsInChildren<SkinnedMeshRenderer>();
        var root = new GameObject(name + "_Static");

#if UNITY_EDITOR
        if (!AssetDatabase.IsValidFolder("Assets/BakedMeshes"))
            AssetDatabase.CreateFolder("Assets", "BakedMeshes");
#endif

        foreach (var smr in skinnedMeshes)
        {
            var bakedMesh = new Mesh();
            smr.BakeMesh(bakedMesh);

#if UNITY_EDITOR
            string path = $"Assets/BakedMeshes/{smr.name}_Baked.asset";
            AssetDatabase.CreateAsset(bakedMesh, path);
            AssetDatabase.SaveAssets();
#endif

            var part = new GameObject(smr.name + "_Static");
            var mf = part.AddComponent<MeshFilter>();
            var mr = part.AddComponent<MeshRenderer>();

            mf.sharedMesh = bakedMesh;
            mr.sharedMaterials = smr.sharedMaterials;

            part.transform.SetParent(root.transform, false);
        }

        Debug.Log("All meshes baked and saved as assets.");
    }
}