Shader "re-Gravity/Render_BodyImpostor"
{
    // ========================================================================
    // 球体伪装者 (Sphere Impostor)
    // 使用包含 65536 个独立 Quad 的单 DrawCall 网格渲染天体。
    // 片元着色器计算虚拟球面 3D 凸出高度并覆写深度缓冲 (SV_Depth)，
    // 实现完美的体积前后遮挡和天体相交时的弧面穿插。
    //
    // 插值：利用 PosMass_Prev 和当前 PosMass 进行帧间插值，
    //       使分批物理的低频更新映射为平滑的视觉运动。
    // ========================================================================
    Properties {}
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "Queue"="Geometry"
        }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0

            #include "UnityCG.cginc"
            #include "PhysicsCore.cginc"

            // --- 渲染专用 uniform（非物理管线共享）---

            uniform float _Udon_InterpolationRatio; // 帧间插值比例 [0,1]
            uniform float _Udon_FlashBrightness; // 闪光最大亮度
            uniform float _Udon_BodyBrightness; // 天体基础亮度
            uniform float _Udon_MinGlowMass; // 自发光最小质量
            uniform float _Udon_FadeStartDistance; // 远距离淡化起始距离
            uniform sampler2D _Udon_PosMass_Prev; // 上一帧 PosMass（用于插值）

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0; // Quad 局部 UV [0,1]
                float2 uv2 : TEXCOORD1; // 天体 ID 映射 UV
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0; // Quad UV，用于球面计算
                float3 viewPos : TEXCOORD1; // 视空间中心位置
                float radius : TEXCOORD3; // 视觉半径
                float4 color : COLOR; // 最终颜色（含闪光和发光）
            };

            v2f vert(appdata v)
            {
                v2f o;

                // --- ID 与边界检查 ---
                uint id = GetIDFromUV(v.uv2);
                if (id >= (uint)_Udon_MaxBodies)
                {
                    o.pos = float4(0, 0, 0, 0);
                    o.uv = v.uv;
                    o.viewPos = float3(0, 0, 0);
                    o.radius = 0;
                    o.color = float4(0, 0, 0, 0);
                    return o;
                }

                // --- 帧间插值 ---
                float4 prevPosMass = tex2Dlod(_Udon_PosMass_Prev, float4(v.uv2, 0, 0));
                float4 currPosMass = tex2Dlod(_Udon_PosMass, float4(v.uv2, 0, 0));
                float mass = lerp(prevPosMass.w, currPosMass.w, _Udon_InterpolationRatio);
                float3 worldPos = lerp(prevPosMass.xyz, currPosMass.xyz, _Udon_InterpolationRatio);

                // --- 事件解码与可见性判定 ---
                float4 velMisc = tex2Dlod(_Udon_VelMisc, float4(v.uv2, 0, 0));
                int eventType;
                float eventData;
                DecodeEvent(velMisc.w, eventType, eventData);

                if (eventType == EVENT_DEAD || eventType == EVENT_RESPAWN)
                {
                    o.pos = float4(0, 0, 0, 0);
                    o.uv = v.uv;
                    o.viewPos = float3(0, 0, 0);
                    o.radius = 0;
                    o.color = float4(0, 0, 0, 0);
                    return o;
                }

                // --- Billboard 设置 ---
                float radius = GetRadius(mass, _Udon_InnerDensity, _Udon_OuterDensity, _Udon_InnerRatio);
                float3 viewCenter = mul(UNITY_MATRIX_V, float4(worldPos, 1.0)).xyz;
                o.radius = radius;
                float3 viewPos = viewCenter + float3(v.vertex.x * radius * 2.0, v.vertex.y * radius * 2.0, 0.0);
                o.viewPos = viewPos;
                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                o.uv = v.uv;

                // --- 颜色采样与距离衰减 ---
                float3 baseColor = tex2Dlod(_Udon_Color, float4(v.uv2, 0, 0)).rgb;
                float dist = length(worldPos);
                float fadeT = saturate((dist - _Udon_FadeStartDistance) /
                    max(0.1, _Udon_SpawnRadius - _Udon_FadeStartDistance));
                float fadeFactor = lerp(1.0, 0.5, fadeT);

                float gray = dot(baseColor, float3(0.299, 0.587, 0.114));
                baseColor = lerp(float3(gray, gray, gray), baseColor, fadeFactor);
                baseColor *= fadeFactor;

                // --- 闪光与自发光 ---
                float flash = 0.0;
                if (eventType == EVENT_NONE && eventData > 0.0)
                {
                    flash = saturate(eventData / 300.0) * _Udon_FlashBrightness;
                }

                float glow = 0.0;
                if (mass > _Udon_MinGlowMass && _Udon_MinGlowMass > 0.0)
                {
                    glow = _Udon_BodyBrightness * log2(mass / _Udon_MinGlowMass);
                }

                // 最终颜色
                float3 finalRGB = baseColor * (1.0 + glow) * (1.0 + flash);

                o.color = float4(finalRGB, 1.0);
                return o;
            }

            // --- 片元着色器：球面伪装 + 深度覆写 + Toon Shading ---
            struct fragOut
            {
                float4 color : SV_Target;
                float depth : SV_Depth;
            };

            fragOut frag(v2f i)
            {
                fragOut o;
                if (i.radius <= 0) discard;

                // 将 Quad UV 映射到 [-1,1]，计算球面交点
                float2 uv = i.uv * 2.0 - 1.0;
                float r2 = dot(uv, uv);
                if (r2 > 1.0) discard; // 圆外丢弃

                // 球面 Z 高度
                float z = sqrt(1.0 - r2);

                // 覆写深度缓冲（视空间 Z 负方向）
                float4 clipPos = mul(UNITY_MATRIX_P, float4(i.viewPos.xy, i.viewPos.z + z * i.radius, 1.0));
                o.depth = clipPos.z / clipPos.w;

                // Toon Shading（适配 VRChat 动画风格）
                float3 normal = float3(uv.x, uv.y, z);
                float3 lightDir = normalize(float3(-0.1, 0.2, 0.8));
                float NdotL = dot(normal, lightDir);
                float toon = smoothstep(0.3, 0.6, NdotL);
                float shading = lerp(0.75, 1.2, toon);

                o.color = float4(i.color.rgb * shading, 1.0);
                return o;
            }
            ENDCG
        }
    }
}