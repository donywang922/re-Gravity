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
                
                // 重生逻辑：source_id 指向产生质量损失的事件源，EventMeta 保存稳定的事件类型和关联天体。
                if (fmod(_Udon_Cycle, 2.0) > 0.5 && event == EVENT_RESPAWN)
                {
                    int source_id = clamp((int)data, 0, (int)_Udon_MaxBodies - 1);
                    float2 source_uv = GetUVFromID(source_id);
                    float4 source_pos_mass = tex2Dlod(_Udon_PosMass, float4(source_uv, 0, 0));
                    float4 source_event_data = tex2Dlod(_Udon_EventData, float4(source_uv, 0, 0));
                    float4 source_event_meta = tex2Dlod(_Udon_EventMeta, float4(source_uv, 0, 0));

                    int source_event = (int)(source_event_meta.x + 0.5);
                    bool is_tear = source_event == EVENT_TEAR;
                    bool is_swallowed = source_event == EVENT_SWALLOWED;

                    int anchor_id = is_swallowed ? (int)(source_event_meta.y + 0.5) : source_id;
                    anchor_id = clamp(anchor_id, 0, (int)_Udon_MaxBodies - 1);
                    float2 anchor_uv = GetUVFromID(anchor_id);
                    float4 anchor_pos_mass = tex2Dlod(_Udon_PosMass, float4(anchor_uv, 0, 0));

                    uint seed = (uint)(_Udon_Frame * 13.0 + id * 17.0);
                    float anchor_radius = GetRadius(anchor_pos_mass.w, _Udon_InnerDensity, _Udon_OuterDensity,
                                                    _Udon_InnerRatio);

                    float ring_radius = length(source_event_data.xyz);
                    float3 n_dir = ring_radius > 0.0001 ? source_event_data.xyz / ring_radius : float3(0, 1, 0);

                    float min_fragment_mass = min(_Udon_FragmentSizeRange.x, _Udon_FragmentSizeRange.y);
                    float max_fragment_mass = max(_Udon_FragmentSizeRange.x, _Udon_FragmentSizeRange.y);
                    mass = lerp(min_fragment_mass, max_fragment_mass, hash(seed + 6u));
                    float fragment_radius = GetRadius(mass, _Udon_InnerDensity, _Udon_OuterDensity,
                                                      _Udon_InnerRatio);

                    if (is_tear)
                    {
                        float3 rand_dir = normalize(float3(hash(seed + 1u) - 0.5, hash(seed + 2u) - 0.5,
                                 hash(seed + 3u) - 0.5));
                        if (dot(rand_dir, n_dir) < 0.0) rand_dir = -rand_dir;
                        pos = source_pos_mass.xyz + rand_dir * (anchor_radius + fragment_radius * 1.05);
                    }
                    else
                    {
                        // 破碎/被吞噬，生成范围是围绕接触面的一圈（环形）
                        float actual_ring_radius = max(0.1, ring_radius) * (0.8 + 0.4 * hash(seed + 5u));

                        float3 up = abs(n_dir.y) < 0.999 ? float3(0, 1, 0) : float3(1, 0, 0);
                        float3 tangent = normalize(cross(n_dir, up));
                        float3 bitangent = cross(n_dir, tangent);

                        float angle = hash(seed + 4u) * TWO_PI;
                        float3 ring_dir = tangent * cos(angle) + bitangent * sin(angle);

                        // 把碰撞环投影到母体球面，再沿局部外法线留出碎片自身半径。
                        float surface_radius = max(0.0001, anchor_radius);
                        float surface_ring_radius = min(actual_ring_radius, surface_radius * 0.95);
                        float axial_offset = sqrt(max(0.0,
                            surface_radius * surface_radius - surface_ring_radius * surface_ring_radius));
                        float3 surface_offset = n_dir * axial_offset + ring_dir * surface_ring_radius;
                        float3 surface_normal = normalize(surface_offset);
                        pos = anchor_pos_mass.xyz + surface_offset + surface_normal * fragment_radius * 1.05;
                    }

                    // 比 isnan() 更适合 Unity 目标平台；NaN/Inf 与上限比较时会返回 false。
                    if (!all(abs(pos) < 1e30) || !(abs(mass) < 1e30))
                    {
                        pos = anchor_pos_mass.xyz + n_dir * (anchor_radius + fragment_radius * 1.05);
                        mass = lerp(min_fragment_mass, max_fragment_mass, hash(seed + 6u));
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
                                float interaction_dist = dist;
                                if (is_shatter)
                                {
                                    float4 target_vel_misc = tex2Dlod(
                                        _Udon_VelMisc, float4(target_uv, 0, 0));
                                    interaction_dist = CalculateSweptClosestDistance(
                                        target_pos_mass.xyz - pos,
                                        vel - target_vel_misc.xyz,
                                        dt);
                                }
                                float overlap_vol = CalculateOverlapVolume(
                                    my_radius, other_radius, interaction_dist);
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
                                float tidal_ratio = CalculateTidalStressRatio(
                                    mass, my_radius, target_pos_mass.w, dist);
                                float ratio = saturate(
                                    (tidal_ratio - ROCHE_TIDAL_THRESHOLD) / ROCHE_TIDAL_THRESHOLD);
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
