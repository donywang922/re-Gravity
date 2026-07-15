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
            #pragma fragmentoption ARB_precision_hint_nicest

            #include "UnityCustomRenderTexture.cginc"
            #include "PhysicsCore.cginc"

            uniform float _Udon_ApplyOffset;
            uniform float3 _Udon_VelOffset;

            /*
             * 贴图更新顺序 
             * velmisc -> eventdata -> posmass
             */
            float4 frag(v2f_customrendertexture IN) : SV_Target
            {
                int id = GetIDFromUV(IN.localTexcoord.xy);
                float4 old_vel_misc = tex2D(_Udon_VelMisc, IN.localTexcoord.xy);

                if (_Udon_ApplyOffset > 0.5)
                {
                    return float4(old_vel_misc.xyz - _Udon_VelOffset, old_vel_misc.w);
                }

                if (id >= _Udon_MaxBodies)
                    return old_vel_misc;

                // 全局变量
                float g_const = _Udon_GravitationalConstant;
                float dt = GetTimeStep();

                // 自身数据
                float4 my_pos_mass = tex2D(_Udon_PosMass, IN.localTexcoord.xy);
                float mass = my_pos_mass.w;
                float3 pos = my_pos_mass.xyz;
                float3 vel = old_vel_misc.xyz;
                float my_radius = GetRadius(mass, _Udon_InnerDensity, _Udon_OuterDensity, _Udon_InnerRatio);
                float my_inner_radius = GetInnerRadius(my_radius, _Udon_InnerRatio);

                // 自身事件
                int current_event;
                float current_data;
                DecodeEvent(old_vel_misc.w, current_event, current_data);

                // 结算数据
                float max_mass_swallowed = -1.0;
                float max_mass_absorbed = -1.0;
                float mass_settle_mass = mass;

                // 死亡统计数据
                float total_dead = 0.0;
                float total_mass_loss = 0.0;
                float min_score = 99999999.0;
                int selected_target = -1;
                uint seed = (uint)(_Udon_Cycle * 13.0 + id * 17.0);
                float3 random_pos = ComputeSpawnPosition(seed, _Udon_SpawnRadius);

                // 物理数据
                float3 total_force = float3(0, 0, 0);
                float3 vel_drag = float3(0, 0, 0);


                // 第1批
                if (_Udon_StartID < 0.5)
                {
                    if (current_event == EVENT_SHATTER || current_event == EVENT_TEAR)
                    {
                        current_event = EVENT_NONE;
                        current_data = 0.0;
                    }
                    // 重生在奇数帧生成，在奇数帧清除，历时2帧
                    if (fmod(_Udon_Cycle, 2) > 0.5 && current_event == EVENT_RESPAWN)
                    {
                        current_event = EVENT_NONE;
                        current_data = 300.0;
                    }
                    // 闪光视觉效果衰减 (每帧 -1.0)
                    if (current_event == EVENT_NONE && current_data > 0.5)
                    {
                        current_data = max(0.0, current_data - 1.0);
                    }
                    // 重生逻辑 奇数物理帧
                    // 此时死亡统计完成，事件产生但未结算
                    if (fmod(_Udon_Cycle, 2) > 0.5 && current_event == EVENT_DEAD)
                    {
                        // 直接从 vel 里读取上一帧最终累加的统计数据
                        float final_total_mass_loss = vel.y;
                        float final_total_dead = vel.z;
                        int final_selected_target = (int)current_data;

                        float avg_frag_mass = max(0.1, min(_Udon_FragmentSizeRange.x, _Udon_FragmentSizeRange.y));
                        float prob = final_total_mass_loss / (avg_frag_mass * max(1.0, final_total_dead));
                        float rand_val = hash(seed + 123u);
                        // 没选到
                        if (final_selected_target == -1 || rand_val >= prob)
                        {
                            return float4(0, 0, 0, EncodeEvent(EVENT_DEAD, 0.0));
                        }

                        float2 target_uv = GetUVFromID(final_selected_target);
                        float4 target_event_data = tex2Dlod(_Udon_EventData, float4(target_uv, 0, 0));
                        float3 target_dir = target_event_data.xyz;

                        float4 target_vel_misc = tex2Dlod(_Udon_VelMisc, float4(target_uv, 0, 0));
                        float4 target_pos_mass = tex2Dlod(_Udon_PosMass, float4(target_uv, 0, 0));
                        int target_event;
                        float target_data;
                        DecodeEvent(target_vel_misc.w, target_event, target_data);

                        float3 new_vel = float3(0, 0, 0);
                        float target_radius = GetRadius(target_pos_mass.w, _Udon_InnerDensity,
                                                        _Udon_OuterDensity, _Udon_InnerRatio);

                        float mass_loss = target_event_data.w;
                        float ring_radius = length(target_dir);
                        float3 n_dir = ring_radius > 0.0001 ? target_dir / ring_radius : float3(0, 1, 0);

                        // 撕裂
                        if (target_event == EVENT_TEAR)
                        {
                            float esc_speed = sqrt(
                                2.0 * _Udon_GravitationalConstant * target_pos_mass.w / max(0.1, target_radius));
                            new_vel = target_vel_misc.xyz + n_dir * esc_speed;
                        }
                        // 碰撞和吞噬
                        else if (target_event == EVENT_SHATTER || (target_event == EVENT_SWALLOWED && target_data > -
                            0.5))
                        {
                            uint seed_frame = (uint)(_Udon_Frame * 13.0 + id * 17.0);
                            float actual_ring_radius = max(0.1, ring_radius) * (0.8 + 0.4 * hash(seed_frame + 5u));

                            float3 up = abs(n_dir.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
                            float3 tangent = normalize(cross(n_dir, up));
                            float3 bitangent = cross(n_dir, tangent);

                            float angle = hash(seed_frame + 4u) * TWO_PI;
                            float3 ring_offset = (tangent * cos(angle) + bitangent * sin(angle)) * actual_ring_radius;

                            if (target_event == EVENT_SWALLOWED)
                            {
                                float3 spread_dir = length(ring_offset) > 0.0001 ? normalize(ring_offset) : float3(0, 1, 0);
                                float3 spawn_relative_dir = normalize(n_dir + spread_dir * 1.5);
                                float esc_speed = sqrt(2.0 * _Udon_GravitationalConstant * target_pos_mass.w / max(0.1, target_radius)) * 2.0;
                                new_vel = target_vel_misc.xyz + spawn_relative_dir * esc_speed * 1.2;
                            }
                            else
                            {
                                float3 outward_dir = length(ring_offset) > 0.0001 ? normalize(ring_offset) : n_dir;
                                float esc_speed = sqrt(2.0 * _Udon_GravitationalConstant * target_pos_mass.w / max(0.1, target_radius)) * 1.5;
                                new_vel = outward_dir * esc_speed + target_vel_misc.xyz;
                            }
                        }
                        return float4(new_vel, EncodeEvent(EVENT_RESPAWN, (float)final_selected_target));
                    }
                }

                // 下一事件
                int next_event = current_event;
                float next_data = current_data;

                if (current_event == EVENT_MASS_SETTLE)
                {
                    mass_settle_mass = current_data;
                }

                // 第2-n批
                if (_Udon_StartID > 0.5)
                {
                    // 事件与死亡统计 偶数帧计算
                    if (fmod(_Udon_Cycle, 2) < 0.5)
                    {
                        min_score = vel.x;
                        total_mass_loss = vel.y;
                        total_dead = vel.z;
                        selected_target = (int)current_data;

                        // 恢复优先级比较器
                        if (current_event == EVENT_SWALLOWED)
                        {
                            float2 prev_uv = GetUVFromID((uint)current_data);
                            max_mass_swallowed = tex2Dlod(_Udon_PosMass, float4(prev_uv, 0, 0)).w;
                        }
                        else if (current_event == EVENT_ABSORBED)
                        {
                            float2 prev_uv = GetUVFromID((uint)current_data);
                            max_mass_absorbed = tex2Dlod(_Udon_PosMass, float4(prev_uv, 0, 0)).w;
                        }
                    }
                    else
                    {
                        
                    }
                }

                // 第1-n批
                // 死体特殊结算与跳过后续物理
                if (fmod(_Udon_Cycle, 2) > 0.5 && current_event == EVENT_DEAD)
                {
                    // 奇数帧 死体直接跳过
                    return old_vel_misc;
                }
                int active_loop_start = (int)_Udon_StartID;
                int active_loop_end = min((int)_Udon_EndID, (int)_Udon_MaxBodies - 1);
                for (int i = active_loop_start; i <= active_loop_end; i++)
                {
                    if (i == id) continue;

                    // 对方天体
                    float2 other_uv = GetUVFromID(i);
                    float4 other_vel_misc = tex2Dlod(_Udon_VelMisc, float4(other_uv, 0, 0));
                    int other_event;
                    float other_data;
                    DecodeEvent(other_vel_misc.w, other_event, other_data);

                    float4 other_pos_mass = tex2Dlod(_Udon_PosMass, float4(other_uv, 0, 0));
                    float other_mass = other_pos_mass.w;


                    // 死亡统计 偶数帧进行
                    if (fmod(_Udon_Cycle, 2) < 0.5 && current_event == EVENT_DEAD)
                    {
                        if (other_event == EVENT_DEAD)
                        {
                            total_dead += 1.0;
                        }

                        float4 other_event_data = tex2Dlod(_Udon_EventData, float4(other_uv, 0, 0));
                        float loss = other_event_data.w;

                        if (abs(loss) > 0.0)
                        {
                            float abs_loss = abs(loss);
                            float3 diff = other_pos_mass.xyz - random_pos;
                            float dist_sq = dot(diff, diff);
                            total_mass_loss += abs_loss;
                            float score = dist_sq / abs_loss;
                            if (score < min_score)
                            {
                                min_score = score;
                                selected_target = i;
                            }
                        }
                    }

                    // 死体不参与物理和事件生成，跳过后续所有活跃天体逻辑
                    if (current_event == EVENT_DEAD) continue;

                    // 被吞噬/被吸收目标已死亡 → 本体也判死，不应该无限等待
                    if ((current_event == EVENT_SWALLOWED || current_event == EVENT_ABSORBED)
                        && other_event == EVENT_DEAD
                        && abs(current_data - (float)i) < 0.5)
                    {
                        next_event = EVENT_DEAD;
                        next_data = 0.0;
                    }

                    if (other_event == EVENT_DEAD) continue;

                    float3 diff = other_pos_mass.xyz - pos;
                    float dist_sq = dot(diff, diff);
                    if (dist_sq < 0.0001) continue;

                    // 物理计算
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

                    // 产生事件 偶数物理帧
                    if (fmod(_Udon_Cycle, 2) < 0.5)
                    {
                        int iter_event = EVENT_NONE;
                        float iter_data = 0.0;
                        bool is_smaller = (mass < other_mass) || (mass == other_mass && id > i);

                        // 事件计算
                        if (dist < sum_outer_radii)
                        {
                            if (mass < _Udon_MinInteractMass || other_mass < _Udon_MinInteractMass)
                            {
                                // 3. 吞并
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
                                    // 相撞，内层吞并
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
                                    float overlap_mass = overlap_vol * _Udon_OuterDensity;
                                    float limit_rate = FRICTION_RATE_LIMIT; // 与吸收速率限制一致
                                    float max_coeff = other_mass / max(0.0001, mass + other_mass);
                                    float friction_coeff = min(max_coeff, (overlap_mass / max(0.0001, mass)) * limit_rate * dt);
                                    vel_drag += friction_coeff * (other_vel_misc.xyz - vel);

                                    if (next_dist >= sum_outer_radii)
                                    {
                                        // 破碎，擦过边缘
                                        if (is_smaller)
                                        {
                                            iter_event = EVENT_SHATTER;
                                            iter_data = (float)i;
                                        }
                                    }
                                    else
                                    {
                                        // 吸收
                                        if (is_smaller)
                                        {
                                            iter_event = EVENT_ABSORBED;
                                            iter_data = (float)i;
                                        }
                                    }
                                }
                            }
                        }
                        else if (dist < sum_outer_radii * ROCHE_LIMIT_FACTOR && mass >= _Udon_MinInteractMass &&
                            other_mass >= _Udon_MinInteractMass)
                        {
                            // 撕裂 如果超过洛希极限
                            iter_event = EVENT_TEAR;
                            iter_data = (float)i;
                        }

                        // 优先级判定
                        if (current_event == EVENT_RESPAWN)
                        {
                            // 刚复活的这一帧赋予无敌，禁止任何事件覆盖，确保状态能留存到下一帧转换为 300.0 并触发渲染特效
                        }
                        // 被吞噬 > 被吸收 > 碰撞 > 撕裂
                        else if (iter_event == EVENT_SWALLOWED)
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
                    // 结算事件 奇数物理帧
                    else
                    {
                        // 结算被合并
                        if (current_event == EVENT_SWALLOWED || current_event == EVENT_ABSORBED)
                        {
                            // 对方必须是我的目标才处理
                            if (abs(current_data - (float)i) < 0.5)
                            {
                                // 对方被合并了，那么我不再需要结算
                                if (other_event == EVENT_SWALLOWED || other_event == EVENT_ABSORBED)
                                {
                                    next_event = EVENT_NONE;
                                    next_data = 0.0;
                                }
                                // 我被结算
                                else
                                {
                                    // 被吞噬 -> 死亡
                                    if (current_event == EVENT_SWALLOWED)
                                    {
                                        next_event = EVENT_DEAD;
                                    }
                                    // 被吸收 -> 无
                                    else
                                    {
                                        next_event = EVENT_NONE;
                                    }
                                    next_data = 0.0;
                                }
                            }
                        }
                        // 结算合并
                        else if (current_event != EVENT_DEAD)
                        {
                            if (other_event == EVENT_SWALLOWED && abs(other_data - (float)id) < 0.5)
                            {
                                next_event = EVENT_MASS_SETTLE;
                                float shatter_loss = 0.0;
                                // 如果被吞噬天体满足触发碎片的条件，则需要扣除产生碎片的质量
                                if (other_mass >= _Udon_MinInteractMass)
                                {
                                    shatter_loss = other_mass * SHATTER_MASS_RATIO;
                                }
                                float actual_absorbed_mass = max(0.0, other_mass - shatter_loss);
                                vel = (mass_settle_mass * vel + actual_absorbed_mass * other_vel_misc.xyz) / (
                                    mass_settle_mass +
                                    actual_absorbed_mass);
                                mass_settle_mass += actual_absorbed_mass;
                                next_data = mass_settle_mass;
                            }
                            // 结算吸收
                            else if (other_event == EVENT_ABSORBED && abs(other_data - (float)id) < 0.5)
                            {
                                next_event = EVENT_MASS_SETTLE;
                                float overlap_vol = CalculateOverlapVolume(other_radius, my_radius, dist);
                                float lost_mass = overlap_vol * _Udon_OuterDensity;
                                float limit_rate = other_mass * ABSORB_RATE_LIMIT; // 限制每秒最多吸收量
                                lost_mass = min(lost_mass, limit_rate * dt);
                                vel = (mass_settle_mass * vel + lost_mass * other_vel_misc.xyz) / (mass_settle_mass +
                                    lost_mass);
                                mass_settle_mass += lost_mass;
                                next_data = mass_settle_mass;
                            }
                        }
                    }
                }
                
                // 死亡统计结算 偶数帧进行
                if (fmod(_Udon_Cycle, 2) < 0.5 && current_event == EVENT_DEAD)
                {
                    return float4(min_score, total_mass_loss, total_dead,
                                  EncodeEvent(EVENT_DEAD, (float)selected_target));
                }

                // 结算物理
                vel += total_force * dt + vel_drag;

                // 第n批
                if (_Udon_EndID >= _Udon_MaxBodies - 1.5)
                {
                    // 销毁逻辑
                    float destroy_radius = _Udon_DestroyRadius;
                    if (length(pos) > destroy_radius && dot(pos, vel) > 0.0)
                    {
                        next_event = EVENT_DEAD;
                        next_data = 0;
                    }

                    // 质量结算状态转移：如果在整个奇数帧结算结束时，质量没有任何增加，则平滑过渡到发光衰减状态
                    if (fmod(_Udon_Cycle, 2) > 0.5 && next_event == EVENT_MASS_SETTLE)
                    {
                        if (abs(next_data - mass) < 0.0001)
                        {
                            next_event = EVENT_NONE;
                            next_data = 100.0;
                        }
                    }
                }

                return float4(vel, EncodeEvent(next_event, next_data));
            }
            ENDCG
        }
    }
}