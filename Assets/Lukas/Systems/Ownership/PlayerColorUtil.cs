using Unity.Mathematics;
using static Unity.Mathematics.math;

public static class PlayerColorUtil
{
    const float PHI = 0.61803398875f; // golden ratio conjugate
    public static readonly float4 NeutralColor = new float4(1f, 1f, 1f, 1f); // white
    public static float4 FromId(int id, float s = 0.75f, float v = 0.9f)
    {
        if (id == -1) // reserve this for neutral units
            return NeutralColor;

        float h = frac(id * PHI);      // 0..1
        float3 rgb = HsvToRgb(h, s, v);
        return new float4(rgb, 1f);
    }

    static float3 HsvToRgb(float h, float s, float v)
    {
        float3 K = new float3(1f, 2f/3f, 1f/3f);
        float3 p = abs(frac(new float3(h) + K) * 6f - 3f);
        return v * lerp(new float3(1f), saturate(p - 1f), s);
    }
}