using Unity.Entities;
using UnityEngine;


public class UnitSpawnProgressManager : MonoBehaviour
{
    public static UnitSpawnProgressManager Instance { get; private set; }

    private System.Collections.Generic.Dictionary<Entity, GameObject> textObjects =
        new System.Collections.Generic.Dictionary<Entity, GameObject>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void ShowText(Entity entity, Vector3 worldPosition, string text)
    {
        if (!textObjects.TryGetValue(entity, out GameObject textObj) || textObj == null)
        {
            // Create new 3D text
            textObj = new GameObject($"ProgressText_{entity}");

            var textMesh = textObj.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.characterSize = 0.2f;
            textMesh.fontSize = 50;
            textMesh.color = Color.black;  // BLACK TEXT
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.fontStyle = FontStyle.Bold;

            // Use proper font and material
            textMesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var renderer = textObj.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material = new Material(Shader.Find("GUI/Text Shader"));
                renderer.material.color = Color.black;  // BLACK TEXT
                renderer.material.mainTexture = textMesh.font.material.mainTexture;
            }

            textObjects[entity] = textObj;
        }

        // Update position and text
        textObj.transform.position = worldPosition + Vector3.up * 3f;
        textObj.GetComponent<TextMesh>().text = text;
        textObj.SetActive(true);

        // Face camera
        var camera = Camera.main;
        if (camera != null)
        {
            textObj.transform.LookAt(camera.transform);
            textObj.transform.Rotate(0, 180, 0);
        }
    }

    public void HideText(Entity entity)
    {
        if (textObjects.TryGetValue(entity, out GameObject textObj) && textObj != null)
        {
            textObj.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        foreach (var textObj in textObjects.Values)
        {
            if (textObj != null) Destroy(textObj);
        }
        textObjects.Clear();

        if (Instance == this) Instance = null;
    }
}