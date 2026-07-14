# re-Gravity

`re-Gravity` 是一个运行在 **VRChat** 环境下的高仿真、高性能多体引力（N-Body）物理模拟世界。该项目完全基于 **UdonSharp** (VRChat SDK3 Udon) 与 Unity **Custom Render Textures (CRTs)** 技术构建。通过将复杂的物理交互与重力结算卸载至 GPU 执行，并在渲染、排序和网络同步上进行极限优化，项目能够在保障 VRChat 顺畅帧率（60+ FPS）的同时，支持多达 **65,536 个** 天体的实时万有引力与碰撞演化模拟。

---

## 核心特性与技术实现

### 1. 基于 GPU CRTs 的双缓冲物理计算 (GPU Physics Simulation)
为了在运行时更新数万天体的物理状态，项目设计了基于着色器（Shader）的粒子物理结算管线：
* **数据编码架构**：使用 256×256 的 `RGBAFloat` 格式 Custom Render Texture 存储天体状态，将物理原语完全编码进纹理通道中：
  - `PosMass` 纹理：存储天体 3D 位置与质量 `float4(posX, posY, posZ, mass)`。
  - `VelMisc` 纹理：存储 3D 速度与编码事件信号 `float4(velX, velY, velZ, eventSignal)`。
  - `EventData` 纹理：存储相撞/撕裂的方向矢量与质量损失量 `float4(dirX, dirY, dirZ, timer * 100000 + massLoss)`。
  - `Color` 纹理：基于 HSL 色相、饱和度、明度范围，在初始化时为每个天体生成其专属 HSL 颜色，避免 CPU 重生天体时的额外计算。
* **双缓冲 Ping-Pong 机制**：将 CRT 分为 Group A 和 Group B。每一物理步更新时，以一组为只读输入，更新并写入到另一组中，杜绝读写冲突与渲染撕裂。
* **双层密度物理球体模型**：天体半径非线性映射。采用“固态内层芯 (`innerDensity`)”与“气/液态外层包层 (`outerDensity`)”的双密度模型，支持通过界面调整两层比例（`innerRatio`），使引力结算和碰撞计算更契合宇宙天体学。
* **洛希极限撕裂与碰撞算法**：
  - **洛希极限撕裂（Tidal Tearing）**：当两个天体靠近至其洛希极限（外径之和的 1.5 倍）且质量足够时，较小天体会在重力梯度下持续撕裂损失质量（`EVENT_TEAR`）。
  - **边缘擦过破碎（Shatter）**：两球体外包层重叠，但预测下一帧将脱离接触时判定为擦肩碰撞，天体将损失重叠部分的质量并产生轨道飞散碎片（`EVENT_SHATTER`）。
  - **包层吸收（Absorbed）**：重叠且持续接近时，质量大者按重叠球面体积不断剥夺小者的质量（`EVENT_ABSORBED`）。
  - **核心吞并（Swallowed）**：若距离小于内径之和，或其中之一质量低于最小交互质量（`minInteractMass`），则触发核心合并，小天体能量与质量融入大天体，动量守恒结算（`EVENT_SWALLOWED`）。
  - **碰撞摩擦力（Drag）**：在重叠状态下，双向施加基于重叠比例的阻尼，模拟天体相撞时的能量耗散。

### 2. 分批物理更新与视觉帧间插值 (Multi-Batch & Interpolation)
$O(N^2)$ 的万有引力结算在 $N=65536$ 时计算极其高昂。为避免单帧 GPU 耗时过长导致 VRChat 掉帧或触发显卡 TDR 复位，项目引入了分批与插值技术：
* **分批（Multi-Batching）**：引力源的累加计算被拆分到多帧（Batches）中进行。主物理循环分为两个阶段：
  - **Phase 1**：每一帧仅让所有天体与 $1 / \text{BatchCount}$ 数量的引力源进行相互吸引、碰撞和事件检测，累加计算速度，并更新对应的 `VelMisc` 区域。
  - **Phase 2**：在所有批次跑完后，触发一次全量位置与质量更新（更新 `PosMass` 和 `EventData`），完成一个物理步。
* **自适应批次缩放 (Auto-Batch Scaling)**：根据平均帧率（`averageDeltaTime`）动态增减批次数（最高至 256 批次）。当运行帧率低于 50 FPS 时自动增加批次数以降低单帧渲染时间，保障流畅性。
* **视觉帧间插值 (Visual Interpolation)**：由于位置物理步更新频率低于渲染频率，渲染着色器接收 `_Udon_InterpolationRatio` 参数，在上一帧 `PosMass_Prev` 与当前帧 `PosMass` 之间进行光滑的线性插值，使天体即便在低频物理循环下也能展现出滑顺的高帧率视觉位移。

### 3. Sphere Impostor 渲染系统 (Sphere Impostor Rendering)
* **单 Draw Call 渲染**：利用一个包含 65,536 个 Quad（面片）的合并 Mesh，在单个 Draw Call 内完成全体天体的渲染。
* **球面伪装数学 (Sphere Impostor Math)**：片元着色器通过 Quad 局部的 UV 坐标计算出虚拟球体的 3D 凸出高度，并重写像素深度（`SV_Depth`）。这使得数十万个简易面片在视觉上呈现完美的 3D 球体遮挡，并且在相撞时具有真实的弧面穿插边界。
* **VR 视空间 billboard**：在顶点着色器中，根据视空间方向对每个 Quad 执行 Billboard 对齐。这消除了 VR 头显边缘视场角（FOV）拉伸导致的球体扁平、变形问题，提供了极佳的虚拟现实沉浸感。
* **视觉特效**：集成了 Toon Shading 卡通渲染以适配 VRChat 艺术风格；根据质量进行星体自发光（Glow）；发生相撞事件时，受击天体在短时间内触发自发光高亮（Flash Timer）。

### 4. 异步轨迹（Trail）记录与渲染
* **最重天体追踪**：动态挑选系统中质量最大的 64 个天体并绘制其演化轨迹，突出主恒星或黑洞的引力扰动。
* **零卡顿异步 CPU 排序**：为了找出质量最大的 64 个天体，UdonSharp 需回读 GPU 端的 `PosMass` 纹理。为了防止 CPU 在单帧中排序 65,536 个数据导致 VRChat 卡顿，项目设计了**分批回读处理**（每帧仅排序 256 个天体），排序过程无感知。
* **迟滞匹配算法 (Hysteresis ID Matching)**：排序后，若天体质量产生微小波动，会导致追踪列表发生 ID 抖动。项目引入迟滞匹配，新进入前 64 的天体只填补空缺或剔除已死亡天体，保证已追踪天体的轨迹插槽（Index）不改变，从而避免轨迹线闪烁或 teleport。
* **面片连线渲染 (Trail Line Renderer)**：使用单次 Draw Call 绘制 64 条长度为 256 的轨迹面片线。轨迹线宽度根据距离与视角自适应（保证至少在屏幕上显示 1 像素，且不超过天体本身视觉半径），透明度随轨迹历史深度（时间）线性衰减。

### 5. 动态天体系统重生与自转生成
* **天体越界销毁与重生**：当碎片因引力弹射飞出边界（`_Udon_DestroyRadius`）并远离系统时，将其判定为 `EVENT_DEAD`。
* **debris 智能偏置生成**：死亡天体不会简单地随机放置，而是会读取 `EventData`。根据系统中发生大质量撕裂和相撞的区域，在空间中以更高概率在其周边重生（`EVENT_RESPAWN`），模拟碰撞产生的抛射物与碎屑。
* **倾斜轨道星盘自转**：重生天体被投射到 `_Udon_SpawnRadius` 的球面，并赋予其围绕一个偏离 Y 轴的自转轴旋转的初速度。初速度结合了开普勒轨道速度估算与扰动噪声，促使重生碎屑快速形成稳定的倾斜原行星盘结构，极大丰富了模拟的多样性。

### 6. 天体系统动量中心重定位 (Recenter)
由于浮点数精度限制，天体系统在长时间演化后可能会发生整体漂移，导致远距离渲染精度丢失。
* 用户触发重定位后，系统通过 `VRCAsyncGPUReadback` 获取全部天体位置和速度。
* 在 CPU 中计算出系统总质量、质心（Center of Mass）和动量中心。
* 将位移和速度修正量传入 GPU，在一次特殊的 CRT Offset Pass 中整体平移天体位置和速度。
* 同时，轨迹历史 CRT 也会接收重定位信号，对其储存的 256 帧历史点同步平移 `_Udon_PosOffset`，确保历史轨迹线不发生断裂或错位。

### 7. 自定义网络快照同步协议 (Network Snapshot Sync Protocol)
由于 VRChat 没有开放大型自定义数据包的网络同步 API，项目在 Udon 限制下设计了一套高性能的**块状网络传输协议**：
* **对象池认领**：场景内放置预备的 `PlayerState`（轻量元数据广播）与 `TransferChannel`（大型数据通道）Udon 行为池。玩家加入时认领专属槽位，并将其网络同步模式设为 `Manual`。
* **分块切片传输（Chunking）**：快照包含全体活跃天体的位置与速度（RGBAFloat）。快照数据在网络发送端被切片为大小为 128 的块（Chunk，每次传输约 4KB 纹理数据），通过 `TransferChannel` 的同步数组分批发送。
* **流控制与 ACK 握手**：
  - 发送方将分块数据写入通道并请求网络同步（`RequestSerialization`）。
  - 接收方通过反序列化事件被动接收数据。接收成功后，通过轻量级 `PlayerState`（约 30 字节）更新 `ackChunk` 并序列化以发送确认信。
  - 发送方收到 ACK 后，延迟 0.15s 发送下一分块，避开 VRChat 高频网络发送限制。
* **并发排队管理**：当多个玩家同时向同一个人请求快照时，发送方 Udon 内部使用排队队列（`_pendingReceivers`）进行串行发送，避免网络通道饱和。
* **超时重试机制**：针对丢包情况，设计了 15 秒发送/接收超时检测。超时后支持最多 3 次重发重试；如果接收来源离线，系统能够优雅释放通道，复位 UI。

### 8. VR 交互与控制面板 UI
* **手势/热键面板唤醒**：
  - PC 端按下 `Tab` 键切换面板开关。
  - VR 端支持双击手柄 `Trigger` 键快速开关面板。
  - VR 端支持手势唤醒：双手持握 `Grip`（握拳姿势）并向右快速挥手（Swipe Right）呼出面板，向左快速挥手（Swipe Left）关闭面板。
* **面板防丢失与自动关闭**：面板会出现在视线前方并微微向上倾斜 19° 方便操作。若玩家移动距离面板超过 3 米（`maxDistance`），面板将自动关闭，防止遗忘在远处占用视线。

---

## 技术栈与依赖

* **引擎环境**：Unity 2022.3.x (VRChat 推荐版本)
* **SDK 与脚本**：VRChat SDK3 + UdonSharp
* **图形技术**：HLSL / CG, Unity Custom Render Textures, Custom Shaders
* **硬件接口**：`VRCAsyncGPUReadback` (用于异步回读), `VRCGraphics.Blit` (用于数据注入)
* **UI 交互**：Unity UI, TextMesh Pro

---

## 主要文件结构

```text
Assets/
├── CRTs/                           # 双缓冲 Custom Render Texture 资源
│   ├── PosMass_A.asset             # 位置质量缓存 A
│   ├── PosMass_B.asset             # 位置质量缓存 B
│   ├── VelMisc_A.asset             # 速度事件缓存 A
│   ├── VelMisc_B.asset             # 速度事件缓存 B
│   ├── TrailHistory_A.asset        # 轨迹历史缓存 A
│   └── ...                         
├── Shaders/                        # 物理计算与渲染着色器
│   ├── PhysicsCore.cginc           # 共享的物理计算与数据编码函数库
│   ├── CRT_PosMassUpdate.shader    # 位置与质量物理计算 Shader
│   ├── CRT_VelMiscUpdate.shader    # 引力场与碰撞力结算 Shader (分批支持)
│   ├── CRT_TrailHistoryUpdate.shader # 轨迹点累加与偏移平移 Shader
│   ├── Render_BodyImpostor.shader  # 65536 伪装者单 DrawCall 渲染着色器
│   ├── Render_TrailLine.shader     # 64 轨迹线单 DrawCall 渲染着色器
│   └── ...                         
└── Scripts/                        # UdonSharp 逻辑控制脚本
    ├── GravitySimulator.cs         # 主物理循环与 CRT 管理器
    ├── TrailManager.cs             # 轨迹追踪器 (异步 CPU 排序)
    ├── CtrlPanel.cs                # 参数面板控制与滑条映射器
    ├── PanelHandler.cs             # 面板跟随与 VR 挥拳手势控制
    └── network/                    # 快照传输网络逻辑
        ├── SyncManager.cs          # 快照传输的核心协议状态机
        ├── PlayerState.cs          # 玩家状态与 ACK 广播槽位
        └── TransferChannel.cs      # 大块快照数据切片传输通道
```
