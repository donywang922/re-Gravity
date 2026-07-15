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

                if (my_event == EVENT_SHATTER || my_event == EVENT_TEAR || my_event == EVENT_SWALLOWED)
                {
                    uint target_id = (uint)my_data;
                    float2 target_uv = GetUVFromID(target_id);
                    float4 target_vel_misc = tex2Dlod(_Udon_VelMisc, float4(target_uv, 0, 0));
                    float4 target_pos_mass = tex2Dlod(_Udon_PosMass, float4(target_uv, 0, 0));

                    int target_event;
                    float target_data;
                    DecodeEvent(target_vel_misc.w, target_event, target_data);

                    float dist = length(target_pos_mass.xyz - my_pos_mass.xyz);
                    float3 raw_dir = target_pos_mass.xyz - my_pos_mass.xyz;
                    float3 norm_dir = length(raw_dir) > 0.001 ? normalize(raw_dir) : float3(0, 1, 0);

                    float my_mass = my_pos_mass.w;
                    float target_mass = target_pos_mass.w;

                    float my_radius = GetRadius(my_mass, _Udon_InnerDensity, _Udon_OuterDensity, _Udon_InnerRatio);
                    float target_radius = GetRadius(target_mass, _Udon_InnerDensity, _Udon_OuterDensity,
                                                _Udon_InnerRatio);

                    // 如果，我破碎了，或者，我被吞噬了且这个吞噬会被结算且这个吞噬会产生碎片
                    bool is_shatter = (my_event == EVENT_SHATTER);
                    if (my_event == EVENT_SWALLOWED && target_event != EVENT_SWALLOWED && target_event != EVENT_ABSORBED
                        && my_mass >= _Udon_MinInteractMass)
                    {
                        is_shatter = true;
                    }

                    // 破碎
                    if (is_shatter)
                    {
                        if (my_event == EVENT_SWALLOWED)
                        {
                            // 吞噬破碎：直接按小天体百分比质量计算，负数标记
                            mass_loss = -(my_mass * SHATTER_MASS_RATIO);
                            // 吞噬时，方向取自身速度的反方向
                            float3 my_vel = my_vel_misc.xyz;
                            float3 swallow_dir = length(my_vel) > 0.001 ? normalize(-my_vel) : float3(0, 1, 0);
                            // 环半径至多为目标大天体半径的一半
                            float ring_radius = min(my_radius, target_radius * 0.5);
                            dir = swallow_dir * ring_radius;
                        }
                        else
                        {
                            // 擦过破碎：按重叠体积计算，负数标记
                            float overlap_vol = CalculateOverlapVolume(my_radius, target_radius, dist);
                            mass_loss = -(overlap_vol * _Udon_OuterDensity);
                            
                            // 算出精确的圆环半径
                            float x = (dist * dist - target_radius * target_radius + my_radius * my_radius) / max(0.0001, 2.0 * dist);
                            float ring_radius = 0.1;
                            if (x < my_radius)
                            {
                                ring_radius = sqrt(max(0.0001, my_radius * my_radius - x * x));
                            }
                            dir = norm_dir * ring_radius;
                        }
                    }
                    // 撕裂
                    else if (my_event == EVENT_TEAR)
                    {
                        float dt = GetTimeStep();
                        float roche_limit = (my_radius + target_radius) * ROCHE_LIMIT_FACTOR;
                        float ratio = clamp(1.0 - dist / max(0.001, roche_limit), 0.0, 1.0);

                        float my_loss_rate = min(ratio * 0.05 * my_mass, 400.0);

                        // 撕裂损失，正数标记
                        mass_loss = my_loss_rate * dt;
                        
                        // 方向为对方天体方向，长度为自身半径作为圆环半径
                        float ring_radius = my_radius;
                        dir = norm_dir * ring_radius;
                    }
                }

                // 限制质量损失防止碎片数过多
                if (abs(mass_loss) > MAX_BODIES * _Udon_MinInteractMass)
                {
                    mass_loss = sign(mass_loss) * MAX_BODIES * _Udon_MinInteractMass;
                }

                return float4(dir, mass_loss);
            }
            ENDCG
        }
    }
}