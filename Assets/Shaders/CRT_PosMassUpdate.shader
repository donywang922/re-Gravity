Shader "re-Gravity/CRT_PosMassUpdate"
{
    // ========================================================================
    // 位置与质量更新 (Pipeline Phase 2)
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

            uniform float _Udon_ApplyOffset;
            uniform float3 _Udon_PosOffset;

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                
                int id = GetIDFromUV(IN.localTexcoord.xy);
                float4 old_pos_mass = tex2D(_Udon_PosMass, IN.localTexcoord.xy);
                if (id >= _Udon_MaxBodies) return old_pos_mass;

                if (_Udon_ApplyOffset > 0.5)
                {
                    return float4(old_pos_mass.xyz - _Udon_PosOffset, old_pos_mass.w);
                }

                float mass = old_pos_mass.w;
                float3 pos = old_pos_mass.xyz;

                float4 vel_misc = tex2D(_Udon_VelMisc, IN.localTexcoord.xy);
                float3 vel = vel_misc.xyz;
                float dt = GetTimeStep();

                int event;
                float data;
                DecodeEvent(vel_misc.w, event, data);
                
   

                // 死亡天体不更新位置
                if (event == EVENT_DEAD)
                {
                    return float4(0, 0, 0, 0);
                }
                
                // 重生逻辑 奇数帧
                if (_Udon_Cycle % 2 > 0.5 && event == EVENT_RESPAWN)
                {
                    int target_id = (int)data;
                    float2 target_uv = GetUVFromID(target_id);
                    float4 target_pos_mass = tex2Dlod(_Udon_PosMass, float4(target_uv, 0, 0));

                    float4 target_vel_misc = tex2Dlod(_Udon_VelMisc, float4(target_uv, 0, 0));
                    int target_event;
                    float target_data;
                    DecodeEvent(target_vel_misc.w, target_event, target_data);

                    float4 target_event_data = tex2Dlod(_Udon_EventData, float4(target_uv, 0, 0));
                    float3 target_dir = target_event_data.xyz;
                    float mass_loss = target_event_data.w;

                    bool is_tear = (mass_loss > 0.0001);
                    bool is_swallowed = (target_event == EVENT_DEAD);

                    uint seed = (uint)(_Udon_Frame * 13.0 + id * 17.0);
                    float target_radius = GetRadius(target_pos_mass.w, _Udon_InnerDensity, _Udon_OuterDensity,
                        _Udon_InnerRatio);

                    float ring_radius = length(target_dir);
                    float3 n_dir = ring_radius > 0.0001 ? target_dir / ring_radius : float3(0, 1, 0);

                    if (is_tear)
                    {
                        float3 rand_dir = normalize(float3(hash(seed + 1u) - 0.5, hash(seed + 2u) - 0.5,
                                 hash(seed + 3u) - 0.5));
                        if (dot(rand_dir, n_dir) < 0.0) rand_dir = -rand_dir;
                        pos = target_pos_mass.xyz + rand_dir * target_radius;
                    }
                    else
                    {
                        // 破碎/被吞噬，生成范围是围绕接触面的一圈（环形）
                        float actual_ring_radius = max(0.1, ring_radius) * (0.8 + 0.4 * hash(seed + 5u));

                        float3 up = abs(n_dir.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
                        float3 tangent = normalize(cross(n_dir, up));
                        float3 bitangent = cross(n_dir, tangent);

                        float angle = hash(seed + 4u) * TWO_PI;
                        float3 ring_offset = (tangent * cos(angle) + bitangent * sin(angle)) * actual_ring_radius;
                        
                        if (is_swallowed)
                        {
                            pos = target_pos_mass.xyz + n_dir * target_radius * 1.5 + ring_offset;
                        }
                        else
                        {
                            pos = target_pos_mass.xyz + n_dir * target_radius + ring_offset;
                        }
                    }

                    mass = lerp(_Udon_FragmentSizeRange.x, _Udon_FragmentSizeRange.y, hash(seed + 6u));
                    
                    // NaN 防护：如果位置或质量计算出现 NaN，回退到目标位置，防止 NaN 传染
                    if (any(isnan(pos)) || isnan(mass))
                    {
                        pos = target_pos_mass.xyz;
                        mass = lerp(_Udon_FragmentSizeRange.x, _Udon_FragmentSizeRange.y, hash(seed + 6u));
                    }
                }
                // 质量变动
                else
                {
                    // 质量结算
                    if (event == EVENT_MASS_SETTLE)
                    {
                        mass = data;
                    }
                    // 质量损失
                    else
                    {
                        bool is_shatter = (event == EVENT_SHATTER);
                        bool is_tear = (event == EVENT_TEAR);
                        bool is_absorbed = (event == EVENT_ABSORBED);

                        // 如果对方没了，不结算被吸收
                        if (event == EVENT_ABSORBED)
                        {
                            int target_id = (int)data;
                            float2 target_uv = GetUVFromID(target_id);
                            float4 target_vel_misc = tex2Dlod(_Udon_VelMisc, float4(target_uv, 0, 0));
                            int target_event;
                            float target_data;
                            DecodeEvent(target_vel_misc.w, target_event, target_data);
                            if (target_event == EVENT_SWALLOWED || target_event == EVENT_ABSORBED)
                            {
                                is_absorbed = false;
                            }
                        }

                        if (is_shatter || is_tear || is_absorbed)
                        {
                            int target_id = (int)data;
                            float2 target_uv = GetUVFromID(target_id);
                            float4 target_pos_mass = tex2Dlod(_Udon_PosMass, float4(target_uv, 0, 0));
                            float dist = length(target_pos_mass.xyz - pos);
                            float my_radius = GetRadius(mass, _Udon_InnerDensity, _Udon_OuterDensity, _Udon_InnerRatio);
                            float other_radius = GetRadius(target_pos_mass.w, _Udon_InnerDensity, _Udon_OuterDensity,
                                                         _Udon_InnerRatio);

                            if (is_shatter || is_absorbed)
                            {
                                float overlap_vol = CalculateOverlapVolume(my_radius, other_radius, dist);
                                float lost_mass = overlap_vol * _Udon_OuterDensity;
                                if (is_absorbed)
                                {
                                    float limit_rate = mass * ABSORB_RATE_LIMIT; // 限制每秒最多吸收量
                                    lost_mass = min(lost_mass, limit_rate * dt);
                                }
                                mass -= lost_mass;
                            }
                            else if (is_tear)
                            {
                                float roche_limit = (my_radius + other_radius) * ROCHE_LIMIT_FACTOR;
                                float ratio = clamp(1.0 - dist / max(0.001, roche_limit), 0.0, 1.0);
                                float loss_rate = min(ratio * 0.05 * mass, 400.0);
                                mass -= loss_rate * dt;
                            }
                        }
                    }

                    mass = max(0.0001, mass);
                    pos += vel * dt;
                }

                return float4(pos, mass);
            }
            ENDCG
        }
    }
}