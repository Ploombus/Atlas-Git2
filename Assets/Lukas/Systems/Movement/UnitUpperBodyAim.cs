using UnityEngine;



public sealed class UnitUpperBodyAim : MonoBehaviour
{
    [Header("Knobs")]
    [Range(0f, 1f)] public float bodyWeight = 1.0f;   // chest twist
    [Range(0f, 1f)] public float headWeight = 0.8f;   // head follow
    [Range(0f, 179f)] public float clampYawDeg = 160f;  // max left/right twist
    public float blendSeconds = 0.10f;                // in/out ramp
    public float targetDistance = 8f;                 // meters ahead from chest

    [Header("Big-angle handoff (IK → legs)")]
    [Range(0f,179f)] public float bigAngleStartDeg = 80f;   // start fading IK
    [Range(0f,179f)] public float bigAngleEndDeg   = 130f;  // fully faded by here
    [Range(0f,1f)]   public float ikWeightAtMax    = 0.40f; // residual IK weight at max angle

    Animator _anim;
    Vector3 _aimWorldPos;
    float _weight;          // smoothed enable
    bool _enable;

    void Awake() => _anim = GetComponent<Animator>();

    // Called from ECS each frame
    public void SetAimTarget(Vector3 worldPos, bool enable)
    {
        _aimWorldPos = worldPos;
        _enable = enable;
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (_anim == null) return;

        // Smooth weight
        float step = (blendSeconds <= 0.0001f) ? 1f : Time.deltaTime / blendSeconds;
        _weight = Mathf.MoveTowards(_weight, _enable ? 1f : 0f, step);

        if (_weight <= 0f)
        {
            _anim.SetLookAtWeight(0f);
            return;
        }

        // Chest origin (roughly)
        Vector3 origin = transform.position + Vector3.up * 1.2f;

        // Clamp yaw relative to current forward
        Vector3 flatTo = _aimWorldPos - origin; flatTo.y = 0f;
        if (flatTo.sqrMagnitude < 1e-6f) { _anim.SetLookAtWeight(0f); return; }

        float fwdYaw = Mathf.Atan2(transform.forward.x, transform.forward.z);
        float tgtYaw = Mathf.Atan2(flatTo.x, flatTo.z);
        float dyaw = Mathf.Atan2(Mathf.Sin(tgtYaw - fwdYaw), Mathf.Cos(tgtYaw - fwdYaw));
        float clamp = Mathf.Clamp(dyaw, -Mathf.Deg2Rad * clampYawDeg, Mathf.Deg2Rad * clampYawDeg);
        Vector3 dir = new Vector3(Mathf.Sin(fwdYaw + clamp), 0f, Mathf.Cos(fwdYaw + clamp));
        Vector3 look = origin + dir * Mathf.Min(targetDistance, flatTo.magnitude);

        // yaw between body forward and aim
        float adyaw  = Mathf.Abs(dyaw);

        // remap to 0..1 handoff
        float t = 0f;
        if (bigAngleEndDeg > bigAngleStartDeg)
        {
            float a0 = Mathf.Deg2Rad * bigAngleStartDeg;
            float a1 = Mathf.Deg2Rad * bigAngleEndDeg;
            t = Mathf.InverseLerp(a0, a1, adyaw); // 0 below start, 1 at/above end
        }

        // scale overall IK weight: 1 → ikWeightAtMax across big angles
        float weightScale = Mathf.Lerp(1f, Mathf.Clamp01(ikWeightAtMax), t);
        float finalWeight = _weight * weightScale;

        // remove LookAt clamp (max range), keep body/head weights as you like
        _anim.SetLookAtPosition(look);
        _anim.SetLookAtWeight(finalWeight, bodyWeight, headWeight, 0f, 0f);
    }
}