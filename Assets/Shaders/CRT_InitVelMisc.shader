Shader "re-Gravity/CRT_InitVelMisc"
{
    // ========================================================================
    // 初始化速度与事件信号
    // 所有天体以近似轨道速度初始化（绕 Y 轴顺时针），事件信号清零。
    // 使用与 CRT_InitPosMass 相同的 hash 种子确保位置-速度一致性。
    // ========================================================================
    Properties {}
    SubShader
    {
        Lighting Off Blend One Zero Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex InitCustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCustomRenderTexture.cginc"
            #include "PhysicsCore.cginc"

            float4 frag(v2f_init_customrendertexture IN) : SV_Target
            {
                // return float4(0, 0, 0, 0);
                uint id = GetIDFromUV(IN.texcoord.xy);
                uint seed = id * 10u + (uint)_Udon_RandomSeed;

                // 与 CRT_InitPosMass 使用相同种子重建位置，以推导轨道切线方向
                float r = pow(hash(seed + 0u), 0.3333333) * _Udon_SpawnRadius;
                float theta = hash(seed + 1u) * TWO_PI;
                float phi = acos(2.0 * hash(seed + 2u) - 1.0);
                float3 spawnPos = float3(
                    r * sin(phi) * cos(theta),
                    r * cos(phi),
                    r * sin(phi) * sin(theta)
                );

                float3 vel = ComputeOrbitalVelocity(seed, spawnPos, _Udon_SpawnRadius,
                    _Udon_GravitationalConstant);

                return float4(vel, 0.0); // 事件信号初始化为 0
            }
            ENDCG
        }
    }
}