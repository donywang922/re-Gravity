Shader "re-Gravity/CRT_EventDataUpdate"
{
    // ========================================================================
    // EventData 更新
    // 在 n+1 帧更新。
    // 如果有撕裂，破碎，则设损失质量为双方总损失质量。
    // 如有吞噬且目标不为被吞噬，则按破碎处理。
    // 对于破碎，方向为对方天体方向，长度为自身原点到碰撞表面的距离。
    // 对于撕裂，方向为对方天体方向，长度为双方距离。
    // 如果没有事件，清空自身。
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

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                uint my_id = GetIDFromUV(IN.localTexcoord.xy);
                if (my_id >= (uint)_Udon_MaxBodies) return float4(0, 0, 0, 0);

                float4 my_vel_misc = tex2D(_Udon_VelMisc, IN.localTexcoord.xy);
                float4 my_pos_mass = tex2D(_Udon_PosMass, IN.localTexcoord.xy);

                int my_event;
                float my_data;
                DecodeEvent(my_vel_misc.w, my_event, my_data);

                float3 dir = float3(0, 0, 0);
                float mass_loss = 0.0;

                if (my_event == EVENT_SHATTER || my_event == EVENT_TEAR || my_event == EVENT_SWALLOWED) {
                    uint target_id = (uint)my_data;
                    float2 target_uv = GetUVFromID(target_id);
                    float4 target_vel_misc = tex2Dlod(_Udon_VelMisc, float4(target_uv, 0, 0));
                    float4 target_pos_mass = tex2Dlod(_Udon_PosMass, float4(target_uv, 0, 0));

                    int target_event;
                    float target_data;
                    DecodeEvent(target_vel_misc.w, target_event, target_data);

                    float dist = length(target_pos_mass.xyz - my_pos_mass.xyz);
                    float3 raw_dir = target_pos_mass.xyz - my_pos_mass.xyz;
                    uint fallback_seed = my_id * 7919u + (uint)(_Udon_Frame * 13.0);
                    float3 fallback_dir = normalize(float3(hash(fallback_seed) - 0.5, hash(fallback_seed + 1u) - 0.5, hash(fallback_seed + 2u) - 0.5));
                    float3 norm_dir = length(raw_dir) > 0.001 ? normalize(raw_dir) : fallback_dir;
                    
                    float my_mass = my_pos_mass.w;
                    float target_mass = target_pos_mass.w;

                    float my_radius = GetRadius(my_mass, _Udon_InnerDensity, _Udon_OuterDensity, _Udon_InnerRatio);
                    float target_radius = GetRadius(target_mass, _Udon_InnerDensity, _Udon_OuterDensity, _Udon_InnerRatio);

                    bool is_shatter = (my_event == EVENT_SHATTER);
                    if (my_event == EVENT_SWALLOWED && target_event != EVENT_SWALLOWED && target_event != EVENT_ABSORBED && my_mass >= _Udon_MinInteractMass) {
                        is_shatter = true;
                    }

                    if (is_shatter) {
                        float overlap_vol = CalculateOverlapVolume(my_radius, target_radius, dist);
                        // 设损失质量为双方总损失质量
                        mass_loss = overlap_vol * _Udon_OuterDensity * 2.0;
                        // 方向为对方天体方向，长度为自身原点到碰撞表面的距离
                        dir = norm_dir * my_radius;
                    } else if (my_event == EVENT_TEAR) {
                        float dt = GetTimeStep();
                        float roche_limit = (my_radius + target_radius) * ROCHE_LIMIT_FACTOR;
                        float ratio = clamp(1.0 - dist / max(0.001, roche_limit), 0.0, 1.0);
                        
                        float my_loss_rate = min(ratio * 0.05 * my_mass, 400.0);
                        
                        // 撕裂损失是双向的，只计算本身的损失
                        mass_loss = my_loss_rate * dt;
                        // 方向为对方天体方向，长度为双方距离
                        dir = norm_dir * dist;
                    }
                }

                // 限制质量损失防止碎片数过多
                // TODO 10.0应该是最小碎片质量
                if (abs(mass_loss) > MAX_BODIES * 10.0) {
                    mass_loss = sign(mass_loss) * MAX_BODIES * 10.0;
                }

                return float4(dir, mass_loss);
            }
            ENDCG
        }
    }
}
