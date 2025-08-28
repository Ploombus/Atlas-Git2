using UnityEngine;
using UnityEngine.EventSystems;

public class CameraRig : MonoBehaviour
{
    [Header("Input")]
    public KeyCode dragKey = KeyCode.Mouse2;

    [Header("Tuning")]
    public float dragSpeed = 0.5f;
    public float dragMultiplyier = 1f; // 0=constant, 1=linear, 2=stronger
    public float zoomSpeed = 50f;
    public float zoomMultiplyier = 1f; // 0=constant, 1=linear, 2=stronger
    public float minY = 50f;
    public float maxY = 150f;

    [Header("Ground Plane")]
    public float planeHeight = 0f;

    [Header("Optional Bounds")]
    public bool useBounds = false;
    public Vector2 xLimits = new Vector2(-100f, 100f);
    public Vector2 zLimits = new Vector2(-100f, 100f);

    void Update()
    {
        Vector3 newRigPosition = transform.position;

        // --- Drag pan, scaled by height ---
        if (Input.GetKey(dragKey))
        {
            float dx = Input.GetAxisRaw("Mouse X");
            float dy = Input.GetAxisRaw("Mouse Y");
            var cam = Camera.main;
            if (cam != null)
            {
                var right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized;
                var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;

                float camHeight = Mathf.Max(0.001f, cam.transform.position.y - planeHeight);
                float refHeight = Mathf.Max(0.001f, minY - planeHeight);
                float heightScale = Mathf.Pow(camHeight / refHeight, dragMultiplyier);

                newRigPosition -= (right * dx + fwd * dy) * dragSpeed * heightScale;
            }
        }

        // --- Mouse wheel zoom that preserves the ground point under the crosshair ---
        if (IsMouseInGameView() && !IsOverUI())
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    float camHeight = Mathf.Max(0.001f, cam.transform.position.y - planeHeight);
                    float refHeight = Mathf.Max(0.001f, minY - planeHeight); // zoomSpeed applies at minY
                    float zoomHeightScale = Mathf.Pow(camHeight / refHeight, zoomMultiplyier);

                    float deltaY = -scroll * zoomSpeed * zoomHeightScale;
                    float targetCamY = Mathf.Clamp(cam.transform.position.y + deltaY, minY, maxY);

                    var plane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));
                    Ray centerRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

                    if (plane.Raycast(centerRay, out float enter) && Mathf.Abs(cam.transform.forward.y) > 1e-4f)
                    {
                        Vector3 anchor = centerRay.GetPoint(enter);

                        float s = (anchor.y - targetCamY) / cam.transform.forward.y;
                        Vector3 targetCamPos = anchor - cam.transform.forward * s;

                        Vector3 camDelta = targetCamPos - cam.transform.position;
                        newRigPosition += camDelta; // move rig so camera ends up at targetCamPos
                    }
                    else
                    {
                        newRigPosition.y = Mathf.Clamp(newRigPosition.y + deltaY, minY, maxY);
                    }
                }
            }
        }

        // --- Anchor-based bounds: clamp the ground point ---
        if (useBounds)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 camPosAfterMove = cam.transform.position + (newRigPosition - transform.position);
                Vector3 camForward = cam.transform.forward;

                var plane = new Plane(Vector3.up, new Vector3(0f, planeHeight, 0f));
                Ray rayAfter = new Ray(camPosAfterMove, camForward);

                if (plane.Raycast(rayAfter, out float t) && Mathf.Abs(camForward.y) > 1e-4f)
                {
                    Vector3 anchor = rayAfter.GetPoint(t);

                    Vector3 clampedAnchor = new Vector3(
                        Mathf.Clamp(anchor.x, xLimits.x, xLimits.y),
                        planeHeight,
                        Mathf.Clamp(anchor.z, zLimits.x, zLimits.y)
                    );

                    if ((clampedAnchor - anchor).sqrMagnitude > 1e-6f)
                    {
                        float s = (clampedAnchor.y - camPosAfterMove.y) / camForward.y;
                        Vector3 desiredCamPos = clampedAnchor - camForward * s;
                        Vector3 adjust = desiredCamPos - camPosAfterMove;
                        newRigPosition += adjust;
                    }
                }
                else
                {
                    newRigPosition.x = Mathf.Clamp(newRigPosition.x, xLimits.x, xLimits.y);
                    newRigPosition.z = Mathf.Clamp(newRigPosition.z, zLimits.x, zLimits.y);
                }
            }
        }

        transform.position = newRigPosition;
    }

    bool IsMouseInGameView()
    {
        var mp = Input.mousePosition;
        return mp.x >= 0f && mp.x <= Screen.width &&
            mp.y >= 0f && mp.y <= Screen.height;
    }
    
    bool IsOverUI() =>
    EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
}