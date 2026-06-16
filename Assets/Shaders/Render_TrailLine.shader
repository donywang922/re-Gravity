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

            #include "UnityCG.cginc"
            #include "PhysicsCore.cginc"

            // 轨迹历史维度（与 PhysicsCore.cginc 中 TRAIL_WIDTH/HEIGHT 一致）
            #define TRAIL_LINE_WIDTH  256
            #define TRAIL_LINE_HEIGHT 64

            // 渲染参数
            #define TRAIL_BASE_WIDTH 1 // 轨迹线最大宽度
            #define TRAIL_BASE_ALPHA 1   // 轨迹线最大不透明度

            uniform sampler2D _Udon_TrailHistory;
            uniform sampler2D _Udon_Top64IDs;


            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;  // X: 归一化历史深度 [0,1], Y: 侧向偏移 (±1)
                float2 uv2 : TEXCOORD1; // X: 轨迹索引 (0-63), Y: 点索引 (0-255)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;

                // 从 TrailHistory CRT 采样历史坐标
                float u = (v.uv2.y + 0.5) / (float)TRAIL_LINE_WIDTH;
                float v_crt = (v.uv2.x + 0.5) / (float)TRAIL_LINE_HEIGHT;

                float4 histData = tex2Dlod(_Udon_TrailHistory, float4(u, v_crt, 0, 0));
                float3 worldPos = histData.xyz;
                float mass = histData.w;

                // 计算轨迹方向（当前点到下一个点）
                float u_next = (min(255.0, v.uv2.y + 1.0) + 0.5) / (float)TRAIL_LINE_WIDTH;
                float4 nextHistData = tex2Dlod(_Udon_TrailHistory, float4(u_next, v_crt, 0, 0));
                float3 nextWorldPos = nextHistData.xyz;

                // 无效数据（当前/下一个像素为黑色）退化为零点
                if (dot(histData, histData) < 0.001 || dot(nextHistData, nextHistData) < 0.001) {
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
                float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                float3 right = cross(dir, viewDir);

                // 宽度改为恒定
                float width = TRAIL_BASE_WIDTH;
                worldPos += right * v.uv.y * width;

                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));

                // 颜色：采样对应天体的颜色，透明度随深度衰减
                float alpha = 1.0 - v.uv.x;
                float bodyId = tex2Dlod(_Udon_Top64IDs, float4(v_crt, 0.5, 0, 0)).r;
                float2 uvColor = GetUVFromID((uint)(bodyId + 0.5));
                float3 bodyColor = tex2Dlod(_Udon_Color, float4(uvColor, 0, 0)).rgb;

                o.color = float4(bodyColor, alpha * TRAIL_BASE_ALPHA);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.color;
            }
            ENDCG
        }
    }
}
