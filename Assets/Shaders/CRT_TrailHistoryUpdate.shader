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
            #pragma fragmentoption ARB_precision_hint_nicest

            #include "UnityCustomRenderTexture.cginc"
            #include "PhysicsCore.cginc"

            uniform sampler2D _Udon_Top64IDs;
            uniform sampler2D _Udon_TrailHistory_Prev;
            uniform float _Udon_TrailRecordDistance;
            uniform float _Udon_ApplyOffset;
            uniform float4 _Udon_PosOffset;

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                int x = (int)(IN.localTexcoord.x * TRAIL_WIDTH);
                int y = (int)(IN.localTexcoord.y * TRAIL_HEIGHT);

                // 根据udon传来的64个id，读取64个天体位置。
                float u_top = (y + 0.5) / (float)TRAIL_HEIGHT;
                float target_id_float = tex2D(_Udon_Top64IDs, float2(u_top, 0.5)).r;
                if (target_id_float < -0.5 || target_id_float >= _Udon_MaxBodies - 0.5)
                {
                    return float4(0, 0, 0, 0);
                }

                int target_id = (int)(target_id_float + 0.5);
                float target_identity = (float)target_id + 1.0;
                
                float2 target_uv = GetUVFromID(target_id);
                float4 curr_pos_mass = tex2D(_Udon_PosMass, target_uv);

                if (curr_pos_mass.w <= 0.0)
                {
                    return float4(0, 0, 0, 0);
                }

                float4 velMisc = tex2D(_Udon_VelMisc, target_uv);
                int eventType; float eventData;
                DecodeEvent(velMisc.w, eventType, eventData);

                if (eventType == EVENT_DEAD || eventType == EVENT_RESPAWN) {
                    return float4(0, 0, 0, 0);
                }

                // 读取上一次刷新位置作为锚点 (x=1)
                float2 uv_0 = float2(0.5 / (float)TRAIL_WIDTH, IN.localTexcoord.y);
                float4 newest_data = tex2D(_Udon_TrailHistory_Prev, uv_0);
                float2 uv_1 = float2(1.5 / (float)TRAIL_WIDTH, IN.localTexcoord.y);
                float4 anchor_data = tex2D(_Udon_TrailHistory_Prev, uv_1);

                // History rows are stable slots, but Top-64 membership changes.
                // Store body ID + 1 in w so a reassigned row cannot connect two
                // unrelated bodies. Only seed the newest point on reassignment.
                if (abs(newest_data.w - target_identity) > 0.25)
                {
                    return x == 0
                        ? float4(curr_pos_mass.xyz, target_identity)
                        : float4(0, 0, 0, 0);
                }

                // A newly assigned row only has x=0. Propagate that seed one
                // column per update instead of filling 256 coincident points.
                if (abs(anchor_data.w - target_identity) > 0.25)
                {
                    if (x == 0) return float4(curr_pos_mass.xyz, target_identity);

                    float2 seed_uv = float2(((float)x - 0.5) / (float)TRAIL_WIDTH,
                                            IN.localTexcoord.y);
                    float4 seed_data = tex2D(_Udon_TrailHistory_Prev, seed_uv);
                    if (_Udon_ApplyOffset > 0.5 && seed_data.w > 0.5)
                    {
                        seed_data.xyz -= _Udon_PosOffset.xyz;
                    }
                    return seed_data;
                }

                // Compare in one coordinate system during recenter. Otherwise
                // the global translation looks like a huge teleport and the
                // branch below collapses the entire trail to the new point.
                float3 comparable_anchor = anchor_data.xyz;
                if (_Udon_ApplyOffset > 0.5)
                {
                    comparable_anchor -= _Udon_PosOffset.xyz;
                }
                float dist = length(curr_pos_mass.xyz - comparable_anchor);

                float4 result = float4(0, 0, 0, 0);
                // needsOffset: 仅当数据来源于上一帧历史（旧坐标系）时才需要偏移校正。
                // curr_pos_mass 已经由 PosMassUpdate 施加了偏移，不可重复减去。
                bool needsOffset = false;

                if (dist > _Udon_TrailRecordDistance * 50.0) {
                    // 间隔超过10倍距离，这行全部设为当前位置（已在新坐标系）
                    result = float4(curr_pos_mass.xyz, target_identity);
                } else if (x == 0) {
                    // 第一列像素，更新为天体实际位置（已在新坐标系）
                    result = float4(curr_pos_mass.xyz, target_identity);
                } else if (dist > _Udon_TrailRecordDistance) {
                    // 间隔超过1倍距离，向右位移一像素（来自旧坐标系）
                    float2 prev_x_uv = float2(((float)x - 0.5) / (float)TRAIL_WIDTH, IN.localTexcoord.y);
                    result = tex2D(_Udon_TrailHistory_Prev, prev_x_uv);
                    needsOffset = true;
                } else {
                    // 未超过间隔，保持原样（来自旧坐标系）
                    result = tex2D(_Udon_TrailHistory_Prev, IN.localTexcoord.xy);
                    needsOffset = true;
                }

                // 如果像素值为0，则设为其左侧像素，用于自动填充空轨迹
                if (result.w < 0.5 && x > 0) {
                    float2 prev_x_uv = float2(((float)x - 0.5) / (float)TRAIL_WIDTH, IN.localTexcoord.y);
                    result = tex2D(_Udon_TrailHistory_Prev, prev_x_uv);
                    needsOffset = true;
                }

                // 仅对来自旧坐标系的历史数据施加偏移校正
                if (_Udon_ApplyOffset > 0.5 && needsOffset && result.w > 0.5) {
                    result.xyz -= _Udon_PosOffset.xyz;
                }

                return result;
            }
            ENDCG
        }
    }
}
