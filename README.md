# VRDeviceTraining

VR 设备拆装培训系统原型。

基于 Unity + XR Interaction Toolkit 开发的交互式 VR 设备拆装培训原型，用于模拟设备零件拆装、操作步骤引导、错误操作反馈以及培训结果统计。

当前通过 **XR Device Simulator** 进行交互开发和测试，不要求真实 VR 头显即可运行基础交互流程。

---

## 核心功能

### 1. XR 零件交互

- 使用 XR Interaction Toolkit 实现交互
- Battery、BackCover 支持抓取
- 根据当前培训步骤控制零件是否可操作

### 2. 拆装步骤管理

当前配置为两个步骤：

1. 拆卸 Battery
2. 拆卸 BackCover

完成当前步骤后自动进入下一步骤。

### 3. 错误操作反馈

当用户尝试操作非当前步骤零件时，显示提示并统计错误操作。

- 提示内容：「请先完成：xxx」
- 错误操作计入当前轮次统计
- 内置 0.3 秒去重机制，防止连续重复计数

### 4. 培训重置

支持从任务面板或结果面板重新开始培训。重置时：

- 恢复零件初始位置
- 恢复 Rigidbody 状态
- 重置当前轮次统计

### 5. 培训结果统计

完成培训后显示结果面板：

- 培训状态
- 培训耗时（秒）
- 错误操作次数（当前轮次）
- 重置次数（当前会话累计）

---

## 技术栈

| 组件 | 版本 |
|---|---|
| Unity | 2022.3.62f3c1 |
| C# | — |
| XR Interaction Toolkit | 2.6.5 |
| Unity Input System | — |
| Unity UI（Legacy Text） | — |
| Git / GitHub | — |

---

## 运行环境

- Unity 2022.3.62f3c1
- macOS / Windows 均可作为 Unity 编辑环境
- 当前开发测试主要使用 MacBook + XR Device Simulator
- 不要求真实 VR 头显

---

## 快速开始

1. Clone 项目
2. 使用 Unity Hub 打开项目，选择 Unity 2022.3.62f3c1
3. 打开场景 `Assets/Scenes/TrainingScene.unity`
4. 点击 Play
5. 使用 XR Device Simulator 进行交互

---

## XR Device Simulator 常用操作

以下键位基于当前项目配置，具体行为可能受 Unity Input System / XR Device Simulator 配置影响：

| 操作 | 按键 |
|---|---|
| 移动控制器 | W / A / S / D |
| 切换控制目标 | Tab（循环切换） |
| 切换至左手 | T |
| 切换至右手 | Y |
| Grip / Select | G |
| 旋转控制目标 | 鼠标 / 触控板 |

---

## 项目结构

```
Assets/
├── Scenes/
│   └── TrainingScene.unity
├── Scripts/
│   ├── Core/
│   │   ├── TrainingManager.cs      — 培训流程管理
│   │   ├── TrainingResult.cs       — 培训结果数据
│   │   └── TrainingStep.cs         — 步骤数据模型
│   ├── Interaction/
│   │   ├── PartInteractable.cs     — 零件交互逻辑
│   │   └── XRTrainingRayOriginFix.cs — 射线原点修正
│   ├── UI/
│   │   ├── TrainingTaskPanelUI.cs  — 任务面板
│   │   └── TrainingResultPanelUI.cs — 结果面板
│   └── Editor/
│       ├── TaskPanelRebuilder.cs
│       ├── XRControllerActionFixer.cs
│       └── XRTrainingSceneFixer.cs
├── Fonts/
│   └── NotoSansSC-Regular.otf
├── Prefabs/
├── Materials/
├── Models/
└── Samples/
    └── XR Interaction Toolkit 2.6.5
        ├── Starter Assets
        └── XR Device Simulator
```

---

## 当前场景结构

```
TrainingScene/
├── TrainingDevice          — 可交互的训练设备
├── Parts/
│   ├── Battery             — 步骤 1 零件
│   └── BackCover           — 步骤 2 零件
├── TrainingManager         — 流程控制
├── TrainingCanvas/
│   ├── TaskPanel           — 当前任务与操作提示
│   └── ResultPanel         — 培训结果展示
├── XR Interaction Manager  — XRI 交互管理器
├── XR Origin (XR Rig)      — VR 相机与控制器
├── XR Device Simulator     — 模拟器的控制器
├── TrainingFloor           — 地面
└── EventSystem
```

---

## 当前限制

### 1. 真实 VR 设备适配尚未完成

当前主要使用 XR Device Simulator 进行开发和测试，尚未在真实 VR 头显上完整验证。

### 2. VR 控制器射线暂不能直接操作 Canvas UI

ResultPanel 和 TaskPanel 的 UI 操作主要依赖鼠标、触控板或 XR Device Simulator 的点击能力。VR 手柄射线点击 UI 的配置尚未完成。

### 3. 当前培训流程为原型流程

目前配置了 Battery 和 BackCover 两个拆装步骤，尚未覆盖完整设备全流程。

---

## 字体说明

项目 UI 使用 Noto Sans SC 作为中文字体。

- 字体文件：`Assets/Fonts/NotoSansSC-Regular.otf`
- 授权协议：SIL Open Font License 1.1
- 字体文件已纳入版本管理，不依赖开发机系统字体

---

## 后续规划

- 增加更多设备拆装步骤
- 完善真实 VR 控制器 UI 交互
- 增加更丰富的培训任务类型
- 完善培训过程记录与展示
- 优化 UI 与交互体验