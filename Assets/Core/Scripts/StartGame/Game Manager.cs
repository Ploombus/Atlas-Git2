using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.NetCode;
using Unity.Entities;

public class GameManager : MonoBehaviour
{
    //public static GameManager Instance { get; set; }

    void Start()
    {
        SceneManager.LoadScene("MenuScene", LoadSceneMode.Additive);
    }
}



