# VRDeviceTraining

**VR 设备拆装培训系统** —— 一套配置驱动的多任务 VR 工业培训框架，支持拆卸/组装双阶段流程、误操作分类统计、3D 引导系统与多套培训任务动态切换。

基于 **Unity 2022.3 + XR Interaction Toolkit** 开发，使用 **XR Device Simulator** 即可在无头显环境下完成全部交互开发与测试。

> 🎯 设计理念：**新增一套培训任务只改 CSV 表格，不改代码、不改场景绑定。**

---

## ✨ 核心亮点

### 1. CSV 配置驱动（培训内容与代码解耦）
培训步骤、零件绑定、零件类别全部由 `Assets/Configs/TrainingConfig.csv` 驱动。新增任务只需在表格中添加行：

```csv
TaskID,TaskName,StepName,PartObjectName,PartCategory
task_battery,电池拆装培训,拆卸 Battery,Battery,电源系统
task_battery,电池拆装培训,拆卸后盖,BackCover,外壳
task_motor,电机拆装培训,拆卸风扇,Fan,散热系统
task_motor,电机拆装培训,拆卸电机,Motor,动力系统
```

### 2. 多任务支持（一套框架跑多套任务）
- 按 `TaskID` 分组解析多套培训任务，运行时 `SelectTask(taskId)` 一键切换
- 切换任务时自动锁定非当前任务的零件，防止跨任务误抓
- 任务面板标题实时显示当前任务名

### 3. 误操作分类统计
- 误操作按零件类别（电源系统/外壳/散热系统/动力系统）分类累计
- 培训结果面板展示分类明细，支持 JSON 持久化留档
- 内置 0.3s 去重防抖，防止连续误操作重复计数

### 4. 3D 引导系统（零外部资源）
- 运行时自动识别当前目标零件并**高亮发光**（材质 Emission）
- 生成 **3D 浮动箭头**指向目标，目标移动时箭头实时跟随
- 培训结束/重置时自动恢复原始材质，不污染场景资源

### 5. 完整的培训闭环
拆卸 → 组装 → 评分 → 结果持久化 → 重置重训

| 模块 | 说明 |
|---|---|
| 拆卸/组装双阶段 | 步骤自动推进，拆卸完自动进入组装阶段 |
| 评分系统 | 基础分 60 + 时间分 25 + 操作规范分 15，纯函数可测试 |
| 结果持久化 | JSON 原子写入，记录耗时/错误/重置/分类统计 |
| 重置机制 | 零件复位 + 状态恢复 + 统计清零，支持随时重训 |

---

## 🛠 技术栈

| 组件 | 版本 |
|---|---|
| Unity | 2022.3.62f3c1 |
| XR Interaction Toolkit | 2.6.5 |
| Unity Input System | — |
| Unity UI（Legacy Text） | — |
| C# | — |

---

## 🚀 快速开始

### 环境要求
- Unity Hub + Unity 2022.3.62f3c1
- 无需真实 VR 头显（使用 XR Device Simulator）

### 运行步骤
1. `git clone` 本项目
2. Unity Hub 打开项目（选择 Unity 2022.3.62f3c1）
3. 打开场景 `Assets/Scenes/TrainingScene.unity`
4. 点击 **Play**
5. 使用 XR Device Simulator 交互操作

### 一键生成第二套任务零件
项目内置两套任务（电池拆装 / 电机拆装）。电机任务需要场景中的 `Fan`、`Motor` 零件，可通过菜单一键生成：

```
Tools → VRDeviceTraining → 一键生成电机任务零件
```

该工具自动创建零件并挂好 `PartInteractable` + `XRGrabInteractable` + `Rigidbody` + `Collider`，`taskId` 预设为 `task_motor`。

---

## 🎮 XR Device Simulator 常用操作

| 操作 | 按键 |
|---|---|
| 移动控制器 | W / A / S / D |
| 切换控制目标 | Tab（循环切换） |
| 切换至左手 / 右手 | T / Y |
| Grip / Select | G |
| 旋转控制目标 | 鼠标 / 触控板 |

---

## 📁 项目结构

```
Assets/
├── Configs/
│   └── TrainingConfig.csv        — 培训任务配置（核心）
├── Scenes/
│   └── TrainingScene.unity       — 主场景
├── Scripts/
│   ├── Core/
│   │   ├── TrainingManager.cs          — 流程管理（双阶段/步骤推进/多任务）
│   │   ├── TrainingConfigLoader.cs     — CSV 解析 + 多任务加载/切换
│   │   ├── TrainingScoring.cs          — 纯函数评分
│   │   ├── TrainingResult.cs           — 结果数据模型（含分类统计）
│   │   ├── TrainingResultPersistence.cs — JSON 持久化
│   │   └── TrainingStep.cs             — 步骤数据模型
│   ├── Interaction/
│   │   ├── PartInteractable.cs         — 零件交互（三态锁定/误操作检测）
│   │   ├── TrainingGuideSystem.cs      — 3D 引导（高亮+箭头）
│   │   └── XRTrainingRayOriginFix.cs   — 射线原点修正
│   ├── UI/
│   │   ├── TrainingTaskPanelUI.cs      — 任务面板（含任务名显示）
│   │   └── TrainingResultPanelUI.cs    — 结果面板（含分类统计）
│   └── Editor/
│       ├── TrainingSetupValidator.cs   — 场景配置校验器
│       ├── TaskPanelRebuilder.cs       — 面板重建工具
│       ├── XRTrainingSceneFixer.cs     — 场景修复工具
│       └── XRControllerActionFixer.cs  — 控制器配置修复工具
├── Editor/
│   └── CreateMotorTaskParts.cs         — 一键生成电机任务零件
├── Fonts/                              — Noto Sans SC（SIL OFL 1.1）
└── Samples/                            — XR Interaction Toolkit 2.6.5
```

---

## 🗂 场景结构

```
TrainingScene/
├── TrainingDevice          — 训练设备
├── Parts/
│   ├── Battery             — 电池（task_battery 步骤 0）
│   ├── BackCover           — 后盖（task_battery 步骤 1）
│   ├── Fan                 — 风扇（task_motor 步骤 0，一键生成）
│   └── Motor               — 电机（task_motor 步骤 1，一键生成）
├── TrainingManager         — 流程控制
├── TrainingCanvas/
│   ├── TaskPanel           — 任务面板
│   └── ResultPanel         — 结果面板
├── XR Interaction Manager
├── XR Origin (XR Rig)
├── XR Device Simulator
└── TrainingFloor
```

---

## 📝 如何新增一套培训任务（3 步）

1. **CSV 加行**：在 `TrainingConfig.csv` 中追加新 `TaskID` 的行（步骤从上到下 = 拆卸顺序）
2. **场景加零件**：在场景 `Parts` 节点下创建零件，挂 `PartInteractable` + `XRGrabInteractable`（可用 Editor 工具自动生成）
3. **切换任务**：运行时调用 `TrainingConfigLoader.SelectTask("新TaskID")` 即可

---

## ⚠️ 当前限制

- 主要使用 XR Device Simulator 开发测试，尚未在真实 VR 头显完整验证
- VR 手柄射线直接操作 Canvas UI 的配置尚未完成
- 零件模型使用 Unity 内置几何体，可替换为真实工业模型

---

## 📌 后续规划

- [ ] 接入大模型实现 AI 指导老师（实时解答操作问题）
- [ ] 真实 VR 头显适配验证
- [ ] 更多工业设备模型与拆装任务
- [ ] 培训记录云端存储与回放

---

## 📄 字体说明

项目 UI 使用 Noto Sans SC（`Assets/Fonts/NotoSansSC-Regular.otf`），授权协议 SIL Open Font License 1.1，已纳入版本管理，不依赖开发机系统字体。
