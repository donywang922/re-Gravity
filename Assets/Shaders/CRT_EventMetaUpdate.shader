Shader "re-Gravity/CRT_EventMetaUpdate"
{
    Properties {}
    SubShader
    {
        Lighting Off Blend One Zero Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex CustomRenderTextureVertexShader
            #pragma fragment frag
            #pragma target 5.0
            #pragma fragmentoption ARB_precision_hint_nicest

            #include "UnityCustomRenderTexture.cginc"
            #include "PhysicsCore.cginc"

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                uint id = GetIDFromUV(IN.localTexcoord.xy);
                if (id >= (uint)_Udon_MaxBodies) return float4(0, 0, 0, 0);

                float4 velMisc = tex2D(_Udon_VelMisc, IN.localTexcoord.xy);
                int eventType;
                float eventData;
                DecodeEvent(velMisc.w, eventType, eventData);

                if (eventType == EVENT_SHATTER || eventType == EVENT_TEAR || eventType == EVENT_SWALLOWED)
                {
                    return float4((float)eventType, eventData, 1.0, 0.0);
                }

                return float4(0, 0, 0, 0);
            }
            ENDCG
        }
    }
}
