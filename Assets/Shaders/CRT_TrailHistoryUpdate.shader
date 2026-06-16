Shader "re-Gravity/CRT_TrailHistoryUpdate"
{
    // ========================================================================
    // 轨迹历史缓冲更新
    // 256×64 的 CRT。宽度代表历史帧，高度代表被追踪的 64 个最大天体。
    // ========================================================================
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

            #include "UnityCustomRenderTexture.cginc"
            #include "PhysicsCore.cginc"

            uniform sampler2D _Udon_Top64IDs;
            uniform sampler2D _Udon_TrailHistory_Prev;
            uniform float _Udon_TrailRecordDistance;

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                int x = (int)(IN.localTexcoord.x * TRAIL_WIDTH);
                int y = (int)(IN.localTexcoord.y * TRAIL_HEIGHT);

                // 根据udon传来的64个id，读取64个天体位置。
                float u_top = (y + 0.5) / (float)TRAIL_HEIGHT;
                float target_id_float = tex2D(_Udon_Top64IDs, float2(u_top, 0.5)).r;
                int target_id = (int)(target_id_float + 0.5);
                
                float2 target_uv = GetUVFromID(target_id);
                float4 curr_pos_mass = tex2D(_Udon_PosMass, target_uv);

                float4 velMisc = tex2D(_Udon_VelMisc, target_uv);
                int eventType; float eventData;
                DecodeEvent(velMisc.w, eventType, eventData);

                if (eventType == EVENT_DEAD || eventType == EVENT_RESPAWN) {
                    return float4(0, 0, 0, 0);
                }

                // 读取上一次刷新位置作为锚点 (x=1)
                float2 uv_1 = float2(1.5 / (float)TRAIL_WIDTH, IN.localTexcoord.y);
                float4 anchor_data = tex2D(_Udon_TrailHistory_Prev, uv_1);

                float dist = length(curr_pos_mass.xyz - anchor_data.xyz);

                float4 result = float4(0, 0, 0, 0);

                if (dist > _Udon_TrailRecordDistance * 10.0) {
                    // 间隔超过10倍距离，这行全部设为当前位置
                    result = curr_pos_mass;
                } else if (x == 0) {
                    // 第一列像素，更新为天体实际位置
                    result = curr_pos_mass;
                } else if (dist > _Udon_TrailRecordDistance) {
                    // 间隔超过1倍距离，向右位移一像素
                    float2 prev_x_uv = float2(((float)x - 0.5) / (float)TRAIL_WIDTH, IN.localTexcoord.y);
                    result = tex2D(_Udon_TrailHistory_Prev, prev_x_uv);
                } else {
                    // 未超过间隔，保持原样
                    result = tex2D(_Udon_TrailHistory_Prev, IN.localTexcoord.xy);
                }

                // 如果像素值为0，则设为其左侧像素，用于自动填充空轨迹
                if (length(result) < 0.001 && x > 0) {
                    float2 prev_x_uv = float2(((float)x - 0.5) / (float)TRAIL_WIDTH, IN.localTexcoord.y);
                    result = tex2D(_Udon_TrailHistory_Prev, prev_x_uv);
                }

                return result;
            }
            ENDCG
        }
    }
}
