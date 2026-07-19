Shader "re-Gravity/CRT_InitVelMisc_TestCollision"
{
    // ========================================================================
    // 测试用初始化：小天体以极快速度向大天体（原点）撞去
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
                    // 巨大天体初始静止
                    return float4(0.0, 0.0, -50.0, 0.0);
                }
                if (id == 1u)
                {
                    float speed = 200.0;
                    return float4(0.0, 0.0, 50.0, 0.0);
                }

                // 其他天体静止
                return float4(0.0, 0.0, 0.0, EncodeEvent(EVENT_DEAD, 0));
            }
            ENDCG
        }
    }
}