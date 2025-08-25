using UnityEngine;

public class TintTarget : MonoBehaviour
{
    public Renderer rendererRef;

    void Reset() => rendererRef = GetComponent<Renderer>();
}