Shader "re-Gravity/CRT_VelMiscUpdate"
{
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

            uniform float _Udon_ApplyOffset;
            uniform float3 _Udon_VelOffset;
            uniform float _Udon_Cycle;

            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                int id = GetIDFromUV(IN.localTexcoord.xy);
                float4 old_vel_misc = tex2D(_Udon_VelMisc, IN.localTexcoord.xy);

                if (_Udon_ApplyOffset > 0.5)
                {
                    return float4(old_vel_misc.xyz - _Udon_VelOffset, old_vel_misc.w);
                }
                
                // 已实现：全天体并行更新，按引力源分批。状态刷新和计算仅在对应帧结算。
                if (id >= _Udon_MaxBodies)
                    return old_vel_misc;

                float4 my_pos_mass = tex2D(_Udon_PosMass, IN.localTexcoord.xy);
                float mass = my_pos_mass.w;
                float3 pos = my_pos_mass.xyz;
                float3 vel = old_vel_misc.xyz;

                int current_event;
                float current_data;
                DecodeEvent(old_vel_misc.w, current_event, current_data);

                int base_next_event = EVENT_NONE;
                float base_next_data = 0.0;

                float max_mass_swallowed = -1.0;
                float max_mass_absorbed = -1.0;
                float swallowed_mass_sum = 0.0;
                float3 swallowed_momentum = float3(0, 0, 0);
                float mass_settle_mass = mass;

                // --- 状态转换 (仅在周期初执行) ---
                if (_Udon_StartID < 0.5)
                {
                    if (current_event == EVENT_SWALLOWED)
                    {
                        uint target_id = (uint)current_data;
                        float2 target_uv = GetUVFromID(target_id);
                        float3 target_vel = tex2Dlod(_Udon_VelMisc, float4(target_uv, 0, 0)).xyz;
                        float3 new_vel = (2.0 * target_vel - vel) * 2.0;
                        return float4(new_vel, EncodeEvent(EVENT_DEAD, 0.0));
                    }

                    if (current_event == EVENT_RESPAWN)
                    {
                        return float4(vel, EncodeEvent(EVENT_NONE, 300.0));
                    }

                    if (current_event == EVENT_MASS_SETTLE)
                    {
                        base_next_event = EVENT_NONE;
                        base_next_data = 100.0;
                    }
                    else if (current_event == EVENT_NONE && current_data > 0.5)
                    {
                        base_next_event = EVENT_NONE;
                        base_next_data = max(0.0, current_data - 1.0);
                    }
                }
                else
                {
                    // 非首批：已结算的一次性状态（SWALLOWED→DEAD, RESPAWN→NONE）在首批已 return，
                    // 到达此处的只可能是需要跨 Batch 累加的活跃态或死亡态。
                    // 活跃天体的事件恢复在下方活跃天体段落处理。
                }

                // --- 死亡天体逻辑 ---
                if (current_event == EVENT_DEAD)
                {
                    float total_dead = 0.0;
                    float total_mass_loss = 0.0;
                    float min_score = 99999999.0;
                    int selected_target = -1;

                    // 恢复分批累加器
                    if (_Udon_StartID > 0.5)
                    {
                        min_score = vel.x;
                        total_mass_loss = vel.y;
                        total_dead = vel.z;
                        selected_target = (int)current_data;
                    }

                    uint seed = (uint)(_Udon_Cycle * 13.0 + id * 17.0);
                    float3 random_pos = ComputeSpawnPosition(seed, _Udon_SpawnRadius);

                    int loop_start = (int)_Udon_StartID;
                    int loop_end = min((int)_Udon_EndID, (int)_Udon_MaxBodies - 1);

                    for (int i = loop_start; i <= loop_end; i++)
                    {
                        if (i == id) continue;

                        float2 other_uv = GetUVFromID(i);
                        float4 other_vel_misc = tex2Dlod(_Udon_VelMisc, float4(other_uv, 0, 0));
                        int other_event;
                        float other_data;
                        DecodeEvent(other_vel_misc.w, other_event, other_data);

                        if (other_event == EVENT_DEAD)
                        {
                            total_dead += 1.0;
                        }

                        float4 other_event_data = tex2Dlod(_Udon_EventData, float4(other_uv, 0, 0));
                        float loss = other_event_data.w;

                        if (loss > 0.0)
                        {
                            total_mass_loss += loss;
                            float4 other_pos_mass = tex2Dlod(_Udon_PosMass, float4(other_uv, 0, 0));
                            float3 other_pos = other_pos_mass.xyz;
                            float3 diff = other_pos - random_pos;
                            float dist_sq = dot(diff, diff);
                            float score = dist_sq / loss;
                            if (score < min_score)
                            {
                                min_score = score;
                                selected_target = i;
                            }
                        }
                    }

                    // 最后一次批处理时判定重生概率
                    if (_Udon_EndID >= _Udon_MaxBodies - 1.5)
                    {
                        float avg_frag_mass = max(0.1, min(_Udon_FragmentSizeRange.x, _Udon_FragmentSizeRange.y));
                        float prob = total_mass_loss / (avg_frag_mass * max(1.0, total_dead));
                        float rand_val = hash(seed + 123u);

                        if (selected_target != -1 && rand_val < prob)
                        {
                            float2 target_uv = GetUVFromID(selected_target);
                            float4 target_event_data = tex2Dlod(_Udon_EventData, float4(target_uv, 0, 0));
                            float3 target_dir = target_event_data.xyz;

                            float4 target_vel_misc = tex2Dlod(_Udon_VelMisc, float4(target_uv, 0, 0));
                            int target_event;
                            float target_data;
                            DecodeEvent(target_vel_misc.w, target_event, target_data);

                            float3 new_vel = float3(0, 0, 0);

                            if (target_event == EVENT_TEAR)
                            {
                                float4 target_pos_mass = tex2Dlod(_Udon_PosMass, float4(target_uv, 0, 0));
                                float target_radius = GetRadius(target_pos_mass.w, _Udon_InnerDensity, _Udon_OuterDensity,
                                                                _Udon_InnerRatio);
                                float esc_speed = sqrt(
                                    2.0 * _Udon_GravitationalConstant * target_pos_mass.w / max(0.1, target_radius));
                                float3 tear_dir = dot(target_dir, target_dir) > 0.000001
                   ? normalize(target_dir)
                   : float3(0, 1, 0);
                                new_vel = target_vel_misc.xyz + tear_dir * esc_speed;
                            }
                            else if (target_event == EVENT_SHATTER || target_event == EVENT_SWALLOWED)
                            {
                                float2 other_uv = GetUVFromID((uint)target_data);
                                float3 other_vel = tex2Dlod(_Udon_VelMisc, float4(other_uv, 0, 0)).xyz;
                                new_vel = (target_vel_misc.xyz + other_vel) * 0.5;
                            }
                            else
                            {
                                float speed = log(target_event_data.w + 1.0) * 0.5;
                                float3 rand_vec = float3(hash(seed + 1u) - 0.5, hash(seed + 2u) - 0.5,
                                                        hash(seed + 3u) - 0.5);
                                new_vel = target_vel_misc.xyz + normalize(rand_vec) * speed;
                            }

                            return float4(new_vel, EncodeEvent(EVENT_RESPAWN, (float)selected_target));
                        }

                        return float4(0, 0, 0, EncodeEvent(EVENT_DEAD, 0.0));
                    }
                    else
                    {
                        // 还没结算完，将中间状态塞进通道传递给下一次Batch
                        return float4(min_score, total_mass_loss, total_dead, EncodeEvent(EVENT_DEAD, (float)selected_target));
                    }
                }

                // --- 活跃天体逻辑 ---
                float my_radius = GetRadius(mass, _Udon_InnerDensity, _Udon_OuterDensity, _Udon_InnerRatio);
                float my_inner_radius = GetInnerRadius(my_radius, _Udon_InnerRatio);

                float g_const = _Udon_GravitationalConstant;
                float dt = GetTimeStep();
                float3 total_force = float3(0, 0, 0);

                int next_event = EVENT_NONE;
                float next_data = 0.0;
                float3 vel_drag = float3(0, 0, 0);

                // 从上一个 Batch 的输出中恢复事件累加状态
                if (_Udon_StartID > 0.5)
                {
                    next_event = current_event;
                    next_data = current_data;
                    // 恢复优先级比较器
                    if (current_event == EVENT_SWALLOWED) {
                        float2 prev_uv = GetUVFromID((uint)current_data);
                        max_mass_swallowed = tex2Dlod(_Udon_PosMass, float4(prev_uv, 0, 0)).w;
                    } else if (current_event == EVENT_ABSORBED) {
                        float2 prev_uv = GetUVFromID((uint)current_data);
                        max_mass_absorbed = tex2Dlod(_Udon_PosMass, float4(prev_uv, 0, 0)).w;
                    } else if (current_event == EVENT_MASS_SETTLE) {
                        mass_settle_mass = current_data;
                    }
                }

                int active_loop_start = (int)_Udon_StartID;
                int active_loop_end = min((int)_Udon_EndID, (int)_Udon_MaxBodies - 1);

                for (int i = active_loop_start; i <= active_loop_end; i++)
                {
                    if (i == id) continue;

                    float2 other_uv = GetUVFromID(i);
                    float4 other_vel_misc = tex2Dlod(_Udon_VelMisc, float4(other_uv, 0, 0));
                    int other_event;
                    float other_data;
                    DecodeEvent(other_vel_misc.w, other_event, other_data);

                    if (other_event == EVENT_DEAD || other_event == EVENT_RESPAWN) continue;

                    float4 other_pos_mass = tex2Dlod(_Udon_PosMass, float4(other_uv, 0, 0));
                    float other_mass = other_pos_mass.w;
                    float3 diff = other_pos_mass.xyz - pos;
                    float dist_sq = dot(diff, diff);

                    if (dist_sq < 0.0001) continue;

                    float inv_dist = rsqrt(dist_sq);
                    float dist = dist_sq * inv_dist;
                    float3 dir = diff * inv_dist;

                    float other_radius = GetRadius(other_mass, _Udon_InnerDensity, _Udon_OuterDensity,
        _Udon_InnerRatio);
                    float other_inner_radius = GetInnerRadius(other_radius, _Udon_InnerRatio);
                    float sum_outer_radii = my_radius + other_radius;
                    float sum_inner_radii = my_inner_radius + other_inner_radius;

                    float min_dist = max(0.1, sum_outer_radii * 0.5);
                    float eff_dist_sq = max(dist_sq, min_dist * min_dist);
                    float gravity_acc = g_const * other_mass / eff_dist_sq;
                    total_force += dir * gravity_acc;

                    int iter_event = EVENT_NONE;
                    float iter_data = 0.0;

                    // 1.（结算吞并）
                    if (other_event == EVENT_SWALLOWED && abs(other_data - (float)id) < 0.5)
                    {
                        iter_event = EVENT_MASS_SETTLE;
                        mass_settle_mass += other_mass;
                        swallowed_mass_sum += other_mass;
                        swallowed_momentum += other_mass * other_vel_misc.xyz;
                    }

                    // 2.（结算吸收)
                    if (other_event == EVENT_ABSORBED && abs(other_data - (float)id) < 0.5)
                    {
                        iter_event = EVENT_MASS_SETTLE;
                        float overlap_vol = CalculateOverlapVolume(other_radius, my_radius, dist);
                        float lost_mass = overlap_vol * _Udon_OuterDensity;
                        mass_settle_mass += lost_mass;
                        swallowed_mass_sum += lost_mass;
                        swallowed_momentum += lost_mass * other_vel_misc.xyz;
                    }

                    bool is_smaller = (mass < other_mass) || (mass == other_mass && id > i);

                    if (dist < sum_outer_radii)
                    {
                        if (mass < _Udon_MinInteractMass || other_mass < _Udon_MinInteractMass)
                        {
                            // 3. （吞并）
                            if (is_smaller)
                            {
                                iter_event = EVENT_SWALLOWED;
                                iter_data = (float)i;
                            }
                        }
                        else
                        {
                            if (dist < sum_inner_radii)
                            {
                                // 5. （相撞，内层吞并）
                                if (is_smaller)
                                {
                                    iter_event = EVENT_SWALLOWED;
                                    iter_data = (float)i;
                                }
                            }
                            else
                            {
                                // 预测下一帧是否重叠
                                float3 rel_vel = vel - other_vel_misc.xyz;
                                float3 next_diff = diff - rel_vel * dt;
                                float next_dist = length(next_diff);

                                // 双方都计算速度损失
                                float overlap_vol = CalculateOverlapVolume(my_radius, other_radius, dist);
                                float overlap_ratio = min(
                                    1.0, overlap_vol / max(0.001, FOUR_THIRDS_PI * my_radius * my_radius * my_radius));
                                vel_drag += (other_vel_misc.xyz - vel) * min(0.5, overlap_ratio);

                                if (next_dist >= sum_outer_radii)
                                {
                                    // 4.1 （破碎，擦过边缘）
                                    if (is_smaller)
                                    {
                                        iter_event = EVENT_SHATTER;
                                        iter_data = (float)i;
                                    }
                                }
                                else
                                {
                                    // 4.2 （吸收）
                                    if (is_smaller)
                                    {
                                        iter_event = EVENT_ABSORBED;
                                        iter_data = (float)i;
                                    }
                                }
                            }
                        }
                    }
                    else if (dist < sum_outer_radii * ROCHE_LIMIT_FACTOR && mass >= _Udon_MinInteractMass && other_mass
                        >= _Udon_MinInteractMass)
                    {
                        // 6. （撕裂）如果超过洛希极限
                        iter_event = EVENT_TEAR;
                        iter_data = (float)i;
                    }

                    // 优先级判定
                    if (iter_event == EVENT_SWALLOWED)
                    {
                        if (other_mass > max_mass_swallowed)
                        {
                            max_mass_swallowed = other_mass;
                            next_event = EVENT_SWALLOWED;
                            next_data = iter_data;
                        }
                    }
                    else if (next_event != EVENT_SWALLOWED)
                    {
                        if (iter_event == EVENT_ABSORBED)
                        {
                            if (other_mass > max_mass_absorbed)
                            {
                                max_mass_absorbed = other_mass;
                                next_event = EVENT_ABSORBED;
                                next_data = iter_data;
                            }
                        }
                        else if (next_event != EVENT_ABSORBED)
                        {
                            if (iter_event == EVENT_MASS_SETTLE)
                            {
                                next_event = EVENT_MASS_SETTLE;
                                next_data = mass_settle_mass;
                            }
                            else if (next_event != EVENT_MASS_SETTLE)
                            {
                                if (iter_event == EVENT_SHATTER)
                                {
                                    next_event = EVENT_SHATTER;
                                    next_data = iter_data;
                                }
                                else if (next_event != EVENT_SHATTER && iter_event == EVENT_TEAR)
                                {
                                    next_event = EVENT_TEAR;
                                    next_data = iter_data;
                                }
                            }
                        }
                    }
                }

                vel += total_force * dt + vel_drag;

                // 仅在最后一个 Batch 做最终事件结算
                if (_Udon_EndID >= _Udon_MaxBodies - 1.5)
                {
                    if (swallowed_mass_sum > 0.0)
                    {
                        vel = (mass * vel + swallowed_momentum) / (mass + swallowed_mass_sum);
                    }

                    if (next_event == EVENT_NONE)
                    {
                        next_event = base_next_event;
                        next_data = base_next_data;
                    }

                    // 越界销毁逻辑
                    float destroy_radius = _Udon_DestroyRadius > 0.0 ? _Udon_DestroyRadius : 50000.0;
                    if (length(pos) > destroy_radius && dot(pos, vel) > 0.0)
                    {
                        next_event = EVENT_DEAD;
                        next_data = 0.0;
                    }
                }

                return float4(vel, EncodeEvent(next_event, next_data));
            }
            ENDCG
        }
    }
}