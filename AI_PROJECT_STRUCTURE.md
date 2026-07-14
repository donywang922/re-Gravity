# 📂 AI Project Structure Guide (re-Gravity)

This document provides a high-level overview of the `re-Gravity` VRChat project hierarchy. It is designed to help AI assistants quickly locate relevant files, code, and assets without having to list or search the entire workspace.

## 📁 Root Directory (`/`)
* **`Assets/`**: Core project files. Most tasks will involve files within this directory.
* **`Packages/`**, **`ProjectSettings/`**, **`Library/`**...: Standard Unity project directories.

## 📁 Assets Directory (`/Assets/`)

### 📜 Key Documentation Files
* `/Assets/re-gravity草案.md`: Unified Draft and Specification for the VRChat N-body gravity simulation, including UI and architecture logic.

### 🎮 Main Scene
* `/Assets/Scenes/main.unity`: The primary scene for the simulation.

### 💻 Scripts (`/Assets/Scripts/`)
Contains UdonSharp (U#) scripts for simulation control and UI interactions.
* `GravitySimulator.cs`: Main controller for the gravity simulation.
* `CtrlPanel.cs`, `PanelHandler.cs`: Control panel UI logic.
* `StepRingCtrl.cs`: Logic for the step ring controller.
* `TrailManager.cs`: Manages object trails.
* `TextSlider.cs`, `TextDuoSlider.cs`: Custom UI slider scripts.

#### 🌐 Network Scripts (`/Assets/Scripts/network/`)
* `SyncManager.cs`: Handles VRChat networking and state synchronization.
* `PlayerState.cs`, `PlayerRow.cs`: Player state representation.
* `TransferChannel.cs`: Used for data transfer logic between players.

### 🎨 Shaders & GPU Simulation (`/Assets/Shaders/`)
The physics simulation is heavily GPU-driven using Custom Render Textures (CRTs). The data is strictly packed into `float4` textures:
* **PosMass**: `float4(posX, posY, posZ, mass)`
* **VelMisc**: `float4(velX, velY, velZ, eventSignal)` (where `eventSignal` encodes `eventType` + `data/targetID`)
* **EventData**: `float4(dirX, dirY, dirZ, massLoss)` (along with timers)

**Shader Pipeline & Logic Breakdown**:
* `PhysicsCore.cginc`: Shared core library containing math primitives, UV/ID mapping functions, and the crucial `EncodeEvent`/`DecodeEvent` functions.
* `CRT_VelMiscUpdate.shader`: **Phase 1** of the simulation pipeline. Computes N-body gravity (forces/accelerations), evaluates collision predictions, calculates overlap ratios, and decides state transitions (e.g., `EVENT_SWALLOWED`, `EVENT_SHATTER`, `EVENT_TEAR`, `EVENT_DEAD`, `EVENT_RESPAWN`). Outputs the new velocity and `eventSignal`.
* `CRT_PosMassUpdate.shader`: **Phase 2** of the pipeline. Applies the state transitions determined in Phase 1 (e.g., physically executing respawn mechanics, accumulating swallowed mass, or applying tearing mass loss), and integrates the updated velocity to output the new position and mass.
* `CRT_EventDataUpdate.shader`: **Phase 3** (executed for the n+1 frame). Evaluates complex interactions (like Shattering or Tearing) to calculate and store the precise mass loss and interaction direction for use in the next logic step.
* `CRT_TrailHistoryUpdate.shader`: Manages a 256×64 CRT buffer that records the historical positions of the top 64 most massive bodies for rendering trails, handling coordinate offsets automatically.
* `CRT_Init*.shader`: Initialization shaders for wiping/populating the data textures.
* `Render_BodyImpostor.shader`, `Render_TrailLine.shader`: The final pass shaders used to visually render the celestial bodies (as impostors) and their trail lines.
* `Mat_*.mat`: Materials assigned to the CRTs to drive the simulation passes.

### 🖼️ Custom Render Textures (`/Assets/CRTs/`)
Contains `.asset` files used as double-buffered data storage for GPU computations.
* `PosMass_A.asset`, `PosMass_B.asset`
* `VelMisc_A.asset`, `VelMisc_B.asset`
* `EventData_A.asset`, `EventData_B.asset`
* `TrailHistory_A.asset`, `TrailHistory_B.asset`
* `Color.asset`

### 🖥️ UI & Prefabs (`/Assets/UI/`)
* `TextSlider.prefab`, `TextDuoSlider.prefab`
* `PageBtn.prefab`, `TabBtn.prefab`, `PlayerRow.prefab`

### 📦 Models (`/Assets/Models/`)
* `BodyImpostorMesh.asset`, `TrailLineMesh.asset`: Meshes used for instanced rendering of the simulation.

## 💡 Navigating and Debugging Tips for AI
1. **GPU Simulation Logic**: If the user reports issues with celestial body physics (gravity, collision, merging), check `/Assets/Shaders/PhysicsCore.cginc` and the `CRT_*Update.shader` files.
2. **C# Control & State**: If the issue is related to UI control, VRChat player interactions, or passing data to the shaders, look in `/Assets/Scripts/GravitySimulator.cs` and related UI scripts.
3. **Networking/Syncing**: Syncing bugs in VRChat instances are usually located in `/Assets/Scripts/network/SyncManager.cs`.
4. **Documentation**: Always check the `.md` files in `Assets/` when trying to understand the author's original design and recent ideas.
