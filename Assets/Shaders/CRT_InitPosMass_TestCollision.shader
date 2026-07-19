Shader "re-Gravity/CRT_InitPosMass_TestCollision"
{
    // ========================================================================
    // 测试用初始化：生成一个巨大天体和一个高速撞击的小天体
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
            #pragma fragmentoption ARB_precision_hint_nicest

            #include "UnityCustomRenderTexture.cginc"
            #include "PhysicsCore.cginc"

            float4 frag(v2f_init_customrendertexture IN) : SV_Target
            {
                uint id = GetIDFromUV(IN.texcoord.xy);

                if (id == 0u)
                {
                    // 巨大天体位于原点
                    return float4(300.0, 0.0, 0.0, 10000000.0);
                }
                if (id == 1u)
                {
                    return float4(-200.0, 0.0, 0.0, 20000000.0);
                }

                // 其他天体质量为 0（死亡状态），并移到视野外防止渲染干扰
                return float4(0.0, -99999.0, 0.0, 0.0);
            }
            ENDCG
        }
    }
}