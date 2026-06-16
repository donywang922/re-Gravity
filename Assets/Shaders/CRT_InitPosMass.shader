Shader "re-Gravity/CRT_InitPosMass"
{
    // ========================================================================
    // 初始化位置与质量
    // 天体在生成半径球体内均匀分布（使用立方根分布确保体积均匀），
    // 质量在碎片大小范围内随机。
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
                uint id = GetIDFromUV(IN.texcoord.xy);
                uint seed = id * 10u + (uint)_Udon_RandomSeed;

                // 立方根分布确保球体内体积均匀
                float r = pow(hash(seed + 0u), 0.3333333) * _Udon_SpawnRadius;
                float theta = hash(seed + 1u) * TWO_PI;
                float phi = acos(2.0 * hash(seed + 2u) - 1.0);

                float3 pos = float3(
                    r * sin(phi) * cos(theta),
                    r * cos(phi),
                    r * sin(phi) * sin(theta)
                );

                float mass = lerp(_Udon_InitialBodySizeRange.x, _Udon_InitialBodySizeRange.y,
                    hash(seed + 3u));

                return float4(pos, mass);
            }
            ENDCG
        }
    }
}
