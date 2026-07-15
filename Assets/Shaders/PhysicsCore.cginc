// ============================================================================
// PhysicsCore.cginc — re-Gravity 共享 Shader 库
//
// 全部 CRT 更新着色器和渲染着色器共用的常量、全局 uniform、工具函数
// 与物理计算原语。
//
// 数据编码规范:
//   PosMass:   float4(posX, posY, posZ, mass)
//   VelMisc:   float4(velX, velY, velZ, eventSignal)
//   EventData: float4(dirX, dirY, dirZ, timer*100000 + massLoss)

//
// 事件信号编码: eventType * 100000 + targetID
// ============================================================================

#ifndef RE_GRAVITY_PHYSICS_CORE_INCLUDED
#define RE_GRAVITY_PHYSICS_CORE_INCLUDED

// ---------------------------------------------------------------------------
// 数学常量
// ---------------------------------------------------------------------------
#define PI          3.14159265358979
#define TWO_PI      6.28318530717959
#define FOUR_THIRDS_PI 4.18879 // 4/3 * PI, 用于球体体积计算

// ---------------------------------------------------------------------------
// 纹理与天体数量
// ---------------------------------------------------------------------------
#define MAX_BODIES  65536
#define TEX_SIZE    256

// ---------------------------------------------------------------------------
// 物理阈值
// ---------------------------------------------------------------------------
#define ROCHE_LIMIT_FACTOR   1.5   // 洛希极限 = 外径之和 × 此系数
#define SHATTER_MASS_RATIO   0.9   // 吞噬破碎时，转化成碎片的质量比例
#define ABSORB_RATE_LIMIT    2.0   // 吸收速率限制，即每秒最多吸收目标自身质量的倍数
#define FRICTION_RATE_LIMIT  1.0   // 摩擦阻力限制，与吸收速率一致

// ---------------------------------------------------------------------------
// Stats CRT 布局 (16 × 8)
//   左半区 (x < 8): float4(动量X, 动量Y, 动量Z, 总质量)
//   右半区 (x >= 8): float4(质量矩X, 质量矩Y, 质量矩Z, 尘埃数)
//   64 个并发线程各遍历 1024 个天体
// ---------------------------------------------------------------------------
#define STATS_WIDTH      16
#define STATS_HEIGHT     8
#define STATS_CHUNK_SIZE 1024

// ---------------------------------------------------------------------------
// Trail History CRT 布局 (256 × 64)
//   宽度 = 历史帧数, 高度 = 被追踪天体数
// ---------------------------------------------------------------------------
#define TRAIL_WIDTH  256
#define TRAIL_HEIGHT 64

// ---------------------------------------------------------------------------
// 事件信号编码倍率
// ---------------------------------------------------------------------------
#define EVENT_SIGNAL_SCALE 100000.0

// ---------------------------------------------------------------------------
// 事件类型
// ---------------------------------------------------------------------------
#define EVENT_NONE          0  // 无事件 (+ 闪光计时器)
#define EVENT_MASS_SETTLE   1  // 质量结算 (+ 质量)
#define EVENT_SWALLOWED     2  // 被吞并 (+ id)
#define EVENT_ABSORBED      3  // 被吸收 (+ id)
#define EVENT_SHATTER       4  // 破碎 (+ id)
#define EVENT_TEAR          5  // 撕裂 (+ id)
#define EVENT_DEAD          6  // 死亡
#define EVENT_RESPAWN       7  // 重生 (+ id)

// ---------------------------------------------------------------------------
// 全局 Uniform — 由 Udon 通过 VRCShader.SetGlobal* 设置
// ---------------------------------------------------------------------------

// 时间与模拟控制
uniform float _Udon_DeltaTime;   // Unity Time.deltaTime（两次 PosMass 更新间隔）
uniform float _Udon_SimSpeed;    // 模拟速度倍率
uniform float _Udon_MaxStep;     // 最大物理步长
uniform float _Udon_Frame;       // 帧计数器，用于 hash 种子
uniform float _Udon_RandomSeed;  // 随机种子，用于初始化着色器
uniform float _Udon_SimScale;
uniform float _Udon_Cycle;

// 物理参数
uniform float _Udon_GravitationalConstant;
uniform float _Udon_InnerDensity;   // 内层密度（默认花岗岩 2.7）
uniform float _Udon_OuterDensity;   // 外层密度（默认水 1.0）
uniform float _Udon_InnerRatio;     // 内外层半径比（默认 0.5）
uniform float _Udon_MinInteractMass;// 低于此质量直接按外径合并

// 世界边界
uniform float _Udon_DestroyRadius;  // 销毁半径
uniform float _Udon_SpawnRadius;    // 生成半径

// 碎片与尘埃
uniform float2 _Udon_FragmentSizeRange; // (最小碎片质量, 最大碎片质量)
uniform float2 _Udon_InitialBodySizeRange; // 初始天体尺寸范围

// 分批处理范围
uniform float _Udon_StartID;
uniform float _Udon_EndID;
uniform float _Udon_MaxBodies;

// 主状态纹理（绑定到当前帧数据，用于渲染）
uniform sampler2D _Udon_PosMass;
uniform float4    _Udon_PosMass_TexelSize;
uniform sampler2D _Udon_VelMisc;

uniform sampler2D _Udon_EventData;
uniform sampler2D _Udon_Color;

// 双缓冲纹理（CRT 更新 Pass 使用）
uniform sampler2D _Udon_PosMass_Next;
uniform sampler2D _Udon_EventData_Next;


// ===========================================================================
// 工具函数
// ===========================================================================

// ---------------------------------------------------------------------------
// hash — 整数哈希，用于确定性伪随机数生成
// ---------------------------------------------------------------------------
inline float hash(uint state) {
    state ^= 2747636419u;
    state *= 2654435769u;
    state ^= state >> 16;
    state *= 2654435769u;
    state ^= state >> 16;
    state *= 2654435769u;
    return float(state) / 4294967295.0;
}

// ---------------------------------------------------------------------------
// ID ↔ UV 坐标映射
// ---------------------------------------------------------------------------
inline float2 GetUVFromID(uint id) {
    uint y = id / (uint)TEX_SIZE;
    uint x = id % (uint)TEX_SIZE;
    return float2((float)x + 0.5, (float)y + 0.5) / (float)TEX_SIZE;
}

inline uint GetIDFromUV(float2 uv) {
    uint x = (uint)(uv.x * TEX_SIZE);
    uint y = (uint)(uv.y * TEX_SIZE);
    return y * (uint)TEX_SIZE + x;
}

// ===========================================================================
// 物理计算
// ===========================================================================

// ---------------------------------------------------------------------------
// 双层密度模型半径计算
// ---------------------------------------------------------------------------
//   effectiveDensity = innerRatio³ × innerDensity + (1 - innerRatio³) × outerDensity
//   R = ³√(mass / effectiveDensity / (4/3π))
inline float GetRadius(float mass, float innerDensity, float outerDensity, float innerRatio) {
    float innerR3 = innerRatio * innerRatio * innerRatio;
    float effectiveDensity = innerR3 * innerDensity + (1.0 - innerR3) * outerDensity;
    float volume = mass / max(0.001, effectiveDensity);
    return pow(volume / FOUR_THIRDS_PI, 0.3333333);
}

inline float GetInnerRadius(float r, float innerRatio) {
    return r * innerRatio;
}

// ---------------------------------------------------------------------------
// 物理步长 — 取 dt × simSpeed 与 maxStep 的较小值
// ---------------------------------------------------------------------------
inline float GetTimeStep() {
    return min(_Udon_DeltaTime * _Udon_SimSpeed, _Udon_MaxStep);
}

// ===========================================================================
// 事件编码与解码 (移动端安全修复版)
inline float EncodeEvent(int type, float data) {
    if (data == 0.0 && type == 0) return 0.0;
    float safeData = (data == 0.0) ? asfloat(0x00800000u) : data;
    uint udata = asuint(safeData);
    udata = (udata & 0xFFFFFFF8u) | (uint)(type & 0x7);
    return asfloat(udata);
}

inline void DecodeEvent(float signal, out int type, out float data) {
    if (signal == 0.0) {
        type = 0;
        data = 0.0;
        return;
    }
    uint usig = asuint(signal);
    type = (int)(usig & 0x7u);
    uint udata = usig & 0xFFFFFFF8u;
    data = (udata == 0x00800000u) ? 0.0 : asfloat(udata);
}

// ---------------------------------------------------------------------------
// 两个球体相交重叠体积计算
// ---------------------------------------------------------------------------
inline float CalculateOverlapVolume(float r1, float r2, float d) {
    if (d >= r1 + r2) return 0.0;
    if (d <= abs(r1 - r2)) {
        float minR = min(r1, r2);
        return FOUR_THIRDS_PI * minR * minR * minR;
    }
    // 标准球冠相交体积公式
    float v = PI * ((r1 + r2 - d) * (r1 + r2 - d)) * (d * d + 2.0 * d * (r1 + r2) - 3.0 * (r1 - r2) * (r1 - r2)) / (12.0 * d);
    return max(0.0, v);
}



// ===========================================================================
// 重生辅助函数
// ===========================================================================

// ---------------------------------------------------------------------------
// 在生成半径球面上计算随机重生位置
// seed 偏移: +1u (theta), +2u (phi)
// ---------------------------------------------------------------------------
inline float3 ComputeSpawnPosition(uint seed, float spawnRadius) {
    float theta = hash(seed + 1u) * TWO_PI;
    float phi = acos(2.0 * hash(seed + 2u) - 1.0);
    return float3(
        spawnRadius * sin(phi) * cos(theta),
        spawnRadius * cos(phi),
        spawnRadius * sin(phi) * sin(theta)
    );
}

// ---------------------------------------------------------------------------
// 计算近似轨道速度
// 假设均匀密度球体（总质量约 24000）在给定半径内的轨道速度。
// 速度以绕 Y 轴顺时针切线方向为主，附加噪声和向心偏移以促进聚核。
// seed 偏移: +5u (速度噪声), +6u/+7u/+8u (方向噪声)
// ---------------------------------------------------------------------------
inline float3 ComputeOrbitalVelocity(uint seed, float3 pos, float spawnRadius, float G) {
    // 偏转后的全局自转轴，偏离纯 Y 轴以获得更好看的初始形态
    float3 axis = normalize(float3(0.25, 1.0, 0.3)); 
    
    // 计算天体位置到旋转轴的投影和垂直向量
    float dotPosAxis = dot(pos, axis);
    float3 posPerp = pos - dotPosAxis * axis;
    float dist = length(posPerp);
    
    float3 tangent = float3(0, 0, 0);
    if (dist > 0.001) {
        float3 radialDir = posPerp / dist;
        tangent = cross(radialDir, axis); // 绕全局倾斜轴顺时针旋转
    }

    float estimatedTotalMass = 24000.0;
    float baseSpeed = dist * sqrt(max(0.0001,
        (G * estimatedTotalMass) / max(0.1, spawnRadius * spawnRadius * spawnRadius)));

    // ±20% 速度噪声
    float speedMod = 0.2 + 10.8 * hash(seed + 5u);

    // 方向噪声（保持主旋转方向一致性）
    float3 noise = float3(
        hash(seed + 6u) - 0.5,
        hash(seed + 7u) - 0.5,
        hash(seed + 8u) - 0.5
    ) * 0.6;

    return normalize(tangent + noise) * baseSpeed * speedMod;
}

#endif // RE_GRAVITY_PHYSICS_CORE_INCLUDED
