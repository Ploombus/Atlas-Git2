using UnityEngine;

public class Billboard : MonoBehaviour
{
    Camera cam;

    void LateUpdate()
    {
        if (cam == null)
            cam = Camera.main;

        if (cam != null)
            transform.rotation = Quaternion.LookRotation(cam.transform.forward);
    }
}