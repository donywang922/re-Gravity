Shader "re-Gravity/Render_TrailLine"
{
    // ========================================================================
    // 轨迹线渲染
    // 单 DrawCall 绘制 64 条 × 256 个顶点的面片。
    // 顶点着色器从 TrailHistory CRT 读取坐标矩阵进行连线，
    // 根据首尾索引计算渐变透明度。宽度随历史深度线性衰减。
    // ========================================================================
    Properties {}
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off ZWrite Off ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 5.0
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"
            #include "PhysicsCore.cginc"

            // 轨迹历史维度（与 PhysicsCore.cginc 中 TRAIL_WIDTH/HEIGHT 一致）
            #define TRAIL_LINE_WIDTH  256
            #define TRAIL_LINE_HEIGHT 64

            // 渲染参数
            #define TRAIL_BASE_WIDTH 1 // 轨迹线最大宽度
            #define TRAIL_BASE_ALPHA 1   // 轨迹线最大不透明度

            uniform float _Udon_FadeStartDistance;
            uniform sampler2D _Udon_TrailHistory;
            uniform sampler2D _Udon_Top64IDs;


            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;  // X: 归一化历史深度 [0,1], Y: 侧向偏移 (±1)
                float2 uv2 : TEXCOORD1; // X: 轨迹索引 (0-63), Y: 点索引 (0-255)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                // 从 TrailHistory CRT 采样历史坐标
                float u = (v.uv2.y + 0.5) / (float)TRAIL_LINE_WIDTH;
                float v_crt = (v.uv2.x + 0.5) / (float)TRAIL_LINE_HEIGHT;

                float4 histData = tex2Dlod(_Udon_TrailHistory, float4(u, v_crt, 0, 0));
                float3 worldPos = histData.xyz;

                float bodyIdFloat = tex2Dlod(_Udon_Top64IDs, float4(v_crt, 0.5, 0, 0)).r;
                if (bodyIdFloat < -0.5 || bodyIdFloat >= _Udon_MaxBodies - 0.5)
                {
                    o.pos = float4(0, 0, 0, 0);
                    o.color = float4(0, 0, 0, 0);
                    return o;
                }

                uint bodyId = (uint)(bodyIdFloat + 0.5);
                float expectedIdentity = (float)bodyId + 1.0;
                float2 bodyUV = GetUVFromID(bodyId);
                float mass = tex2Dlod(_Udon_PosMass, float4(bodyUV, 0, 0)).w;

                // 计算轨迹方向（当前点到下一个点）
                float u_next = (min(255.0, v.uv2.y + 1.0) + 0.5) / (float)TRAIL_LINE_WIDTH;
                float4 nextHistData = tex2Dlod(_Udon_TrailHistory, float4(u_next, v_crt, 0, 0));
                float3 nextWorldPos = nextHistData.xyz;

                float scale = _Udon_SimScale > 0.00001 ? _Udon_SimScale : 1.0;
                worldPos *= scale;
                nextWorldPos *= scale;
                worldPos.y += 1;
                nextWorldPos.y += 1;

                // 无效数据（当前/下一个像素为黑色）退化为零点
                if (mass <= 0.0 || abs(histData.w - expectedIdentity) > 0.25 ||
                    abs(nextHistData.w - expectedIdentity) > 0.25) {
                    o.pos = float4(0,0,0,0);
                    o.color = float4(0,0,0,0);
                    return o;
                }

                float3 dir = nextWorldPos - worldPos;
                if (length(dir) < 0.001) {
                    dir = float3(0,1,0);
                } else {
                    dir = normalize(dir);
                }

                // Billboard: 用轨迹方向和视线方向叉积得到侧向
                float3 viewVector = _WorldSpaceCameraPos - worldPos;
                float viewLength = length(viewVector);
                float3 viewDir = viewLength > 0.0001
                    ? viewVector / viewLength
                    : float3(0, 0, 1);
                float3 right = cross(dir, viewDir);
                float rightLength = length(right);
                if (rightLength < 0.0001)
                {
                    float3 fallbackAxis = abs(dir.y) < 0.999
                        ? float3(0, 1, 0)
                        : float3(1, 0, 0);
                    right = cross(dir, fallbackAxis);
                    rightLength = length(right);
                }
                right /= max(0.0001, rightLength);

                // 宽度改为恒定
                // 保证至少在屏幕上有 1 像素宽度，但不能超过天体本身的粗细
                float4 clipCenter = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                float halfPixelWidth = clipCenter.w / (_ScreenParams.y * abs(UNITY_MATRIX_P._m11));
                
                float bodyRadius = GetRadius(mass, _Udon_InnerDensity, _Udon_OuterDensity, _Udon_InnerRatio) * scale;
                
                float halfWidth = max((float)TRAIL_BASE_WIDTH * scale, halfPixelWidth);
                halfWidth = min(halfWidth, bodyRadius);
                
                worldPos += right * v.uv.y * halfWidth;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));

                // 颜色：采样对应天体的颜色，透明度随深度衰减
                float alpha = 1.0 - v.uv.x;
                float3 bodyColor = tex2Dlod(_Udon_Color, float4(bodyUV, 0, 0)).rgb;

                o.color = float4(bodyColor, alpha * TRAIL_BASE_ALPHA);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return i.color;
            }
            ENDCG
        }
    }
}
