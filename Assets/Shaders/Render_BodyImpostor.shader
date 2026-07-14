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
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "PhysicsCore.cginc"

            // --- 渲染专用 uniform（非物理管线共享）---

            uniform float _Udon_InterpolationRatio; // 帧间插值比例 [0,1]
            uniform float _Udon_FlashBrightness; // 闪光最大亮度
            uniform float _Udon_BodyBrightness;

            uniform float _Udon_MinGlowMass; // 自发光最小质量
            uniform float _Udon_FadeStartDistance; // 远距离淡化起始距离
            uniform sampler2D _Udon_PosMass_Prev; // 上一帧 PosMass（用于插值）

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0; // Quad 局部 UV [0,1]
                float2 uv2 : TEXCOORD1; // 天体 ID 映射 UV
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0; // Quad UV，用于球面计算
                float3 viewPos : TEXCOORD1; // 视空间顶点位置
                float3 viewCenter : TEXCOORD2; // 视空间天体中心位置
                float radius : TEXCOORD3; // 视觉半径
                float4 color : COLOR; // 最终颜色（含闪光和发光）
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // --- ID 与边界检查 ---
                uint id = GetIDFromUV(v.uv2);
                if (id >= (uint)_Udon_MaxBodies)
                {
                    o.pos = float4(0, 0, 0, 0);
                    o.uv = v.uv;
                    o.viewPos = float3(0, 0, 0);
                    o.viewCenter = float3(0, 0, 0);
                    o.radius = 0;
                    o.color = float4(0, 0, 0, 0);
                    return o;
                }
                // --- 事件解码与可见性判定 ---
                //TODO 刚复活的天体EVENT_RESPAWN是不显示且不插值的 (已修复: 提前判定)
                float4 velMisc = tex2Dlod(_Udon_VelMisc, float4(v.uv2, 0, 0));
                int eventType;
                float eventData;
                DecodeEvent(velMisc.w, eventType, eventData);

                if (eventType == EVENT_DEAD || eventType == EVENT_RESPAWN)
                {
                    o.pos = float4(0, 0, 0, 0);
                    o.uv = v.uv;
                    o.viewPos = float3(0, 0, 0);
                    o.viewCenter = float3(0, 0, 0);
                    o.radius = 0;
                    o.color = float4(0, 0, 0, 0);
                    return o;
                }

                // --- 帧间插值 ---
                float4 prevPosMass = tex2Dlod(_Udon_PosMass_Prev, float4(v.uv2, 0, 0));
                float4 currPosMass = tex2Dlod(_Udon_PosMass, float4(v.uv2, 0, 0));
                float mass = lerp(prevPosMass.w, currPosMass.w, _Udon_InterpolationRatio);
                float3 worldPos = lerp(prevPosMass.xyz, currPosMass.xyz, _Udon_InterpolationRatio);

                // 修复：刚从 EVENT_RESPAWN 转换过来的天体不要插值，直接使用新位置，防止乱飞闪烁
                if (eventType == EVENT_NONE && eventData > 299.5)
                {
                    mass = currPosMass.w;
                    worldPos = currPosMass.xyz;
                }

                // --- 缩放处理 ---
                float scale = _Udon_SimScale > 0.00001 ? _Udon_SimScale : 1.0;
                worldPos *= scale;
                worldPos.y += 1;

                // --- Billboard 设置 ---
                float radius = GetRadius(mass, _Udon_InnerDensity, _Udon_OuterDensity, _Udon_InnerRatio) * scale;
                float3 viewCenter = mul(UNITY_MATRIX_V, float4(worldPos, 1.0)).xyz;
                o.radius = radius;
                o.viewCenter = viewCenter;

                // 使 Quad 朝向相机位置 (Spherical Billboard)，修复 VR 边缘扁平拉伸
                float3 forward = normalize(viewCenter);
                float3 right = normalize(cross(forward, float3(0, 1, 0)));
                float3 up = cross(right, forward);

                float3 viewPos = viewCenter + right * (v.vertex.x * radius * 2.0) + up * (v.vertex.y * radius * 2.0);
                o.viewPos = viewPos;
                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));
                o.uv = v.uv;

                // --- 颜色采样与距离衰减 ---
                float3 baseColor = tex2Dlod(_Udon_Color, float4(v.uv2, 0, 0)).rgb;
                float dist = length(worldPos); // 还原实际距离计算 fade
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
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                fragOut o;
                if (i.radius <= 0) discard;

                // 将 Quad UV 映射到 [-1,1]，计算球面交点
                float2 uv = i.uv * 2.0 - 1.0;
                float r2 = dot(uv, uv);
                if (r2 > 1.0) discard; // 圆外丢弃

                // 球面 Z 高度
                float z = sqrt(1.0 - r2);

                // 覆写深度缓冲（沿视线方向凸出，修复边缘深度误差）
                float3 forward = normalize(i.viewCenter);
                float3 sphereViewPos = i.viewPos - forward * (z * i.radius);

                float4 clipPos = mul(UNITY_MATRIX_P, float4(sphereViewPos, 1.0));
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