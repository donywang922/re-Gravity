Shader "re-Gravity/CRT_InitColor"
{
    // ========================================================================
    // 初始化天体颜色
    // 仅在模拟启动或更换主题时执行一次。根据 HSL 范围参数为每个固定 UV
    // 槽位生成随机颜色。物理系统复活天体时直接复用对应槽位的颜色。
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

            uniform float2 _Udon_HSL_H; // 色相范围 (min, max)
            uniform float2 _Udon_HSL_S; // 饱和度范围 (min, max)
            uniform float2 _Udon_HSL_L; // 明度范围 (min, max)

            // HSL → RGB 转换
            float3 HSLToRGB(float h, float s, float l) {
                float3 rgb = saturate(float3(
                    abs(h * 6.0 - 3.0) - 1.0,
                    2.0 - abs(h * 6.0 - 2.0),
                    2.0 - abs(h * 6.0 - 4.0)
                ));
                float c = (1.0 - abs(2.0 * l - 1.0)) * s;
                return (rgb - 0.5) * c + l;
            }

            float4 frag(v2f_init_customrendertexture IN) : SV_Target
            {
                uint id = GetIDFromUV(IN.texcoord.xy);
                uint seed = id * 10u + (uint)_Udon_RandomSeed;

                float targetH = _Udon_HSL_H.y;
                if (_Udon_HSL_H.x > _Udon_HSL_H.y) {
                    targetH += 1.0;
                }
                
                float h = frac(lerp(_Udon_HSL_H.x, targetH, hash(seed + 10u)));
                float s = lerp(_Udon_HSL_S.x, _Udon_HSL_S.y, hash(seed + 11u));
                float l = lerp(_Udon_HSL_L.x, _Udon_HSL_L.y, hash(seed + 12u));

                return float4(HSLToRGB(h, s, l), 1.0);
            }
            ENDCG
        }
    }
}
