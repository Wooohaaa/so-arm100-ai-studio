# 机械臂 AI 工作台

这是一个独立实现的 Unity 3D 机械臂控制工程，参考了 [full-self-surgery 的整体架构](https://github.com/lidangzzz/full-self-surgery) 和 [Agent API 设计](https://github.com/lidangzzz/full-self-surgery/blob/main/docs/AGENT_API.md)。本工程没有复制上游脚本或素材，也不包含 SOFA、软组织模拟、手术场景或手术示范。

## 已实现

- Unity 场景中程序化生成桌面工作台、SO-100 外形简化仿真臂（5 个旋转关节 + 夹爪）和可抓取方块。
- 中文控制面板：自然语言任务输入、机械臂状态、回零、夹爪、暂停、恢复和急停。
- 三色分拣场景：红、蓝、黄方块及对应的彩色目标框，任务指引显示在控制面板。
- 可配置的 OpenAI 兼容 Chat Completions 接口。AI 输出受限动作计划，经本地工作空间、关节限位和动作格式检查后自动执行。
- 本机 HTTP API，可查询状态、标定参数并从外部程序控制仿真机械臂。
- `IRobotArmDriver` 驱动接口。当前提供 Unity 运动学仿真；SO-100 实体串口/Feetech 驱动尚未实现。
- Windows 独立程序构建脚本。

相机视角可用 **V** 在斜视与俯视间切换；也可右键拖动和滚轮缩放。测试记录见 [`docs/test-records/`](docs/test-records/README.md)，包括 [三色视觉分拣测试](docs/test-records/vision-color-sorting.md)。

后续机械臂操作测试以渲染画面作为唯一感知与结果核验来源；控制 API 只发送动作，不用 `/api/state` 或物体坐标反馈替代视觉。

没有自动演示任务。场景中的方块仅用于自然语言抓取与移动操作。

## 打开工程

工程使用 Unity **6000.6.7f1**。在 Unity Hub 中添加本目录，或双击 `Start-RobotArmStudio.bat`。首次打开时，编辑器脚本会创建 `Assets/Scenes/RobotArmStudio.unity`；打开该场景后按 Play 即可使用。

SO-100 模型使用标准关节顺序 `shoulder_pan`、`shoulder_lift`、`elbow_flex`、`wrist_flex`、`wrist_roll`，夹爪单独控制。当前模型按 SO-100 结构与尺寸做了轻量化的程序化外观和运动学近似，不包含官方 STL 网格，也不模拟舵机动力学、回差或负载；它适合验证 AI 指令、关节限位、末端位置和控制接口。位置逆解会保持当前腕部旋转角。

如果首次启动要求登录或激活 Unity，请在 Unity Hub 中使用你自己的 Unity 账号完成。工程不依赖 SOFA 或第三方模型包。

也可以构建 Windows 独立程序：双击 `Build-RobotArmStudio.bat`。当前 Unity 6000.6.7f1 安装已包含 Windows Mono 播放器支持，输出位于 `Build/Windows/RobotArmStudio.exe`；之后双击 `Run-RobotArmStudio.bat` 启动。若在其他电脑构建，请确保 Unity Hub 中已安装 Windows Standalone Build Support。

## 接入大模型

在运行界面展开 **AI 设置**，填写：

1. Base URL，例如 `https://api.openai.com/v1`，或你使用的兼容服务地址。
2. 服务商支持的模型 ID。
3. API Key（服务商要求时填写；本地无认证服务可留空）。

点击“保存地址与模型”会把地址和模型 ID 保存到本机 Unity Preferences。API Key 仅保留在当前运行进程内，不写入工程文件。也可以在启动 Unity 前设置 `ROBOT_LLM_API_KEY` 环境变量。

发送的用户指令和当前机械臂/物体状态会传给你填写的模型服务。AI 只可生成 `move`、`joints`、`gripper`、`home`、`wait`、`stop` 六种动作；不可执行任意代码。动作会在执行前检查工作空间、可达性和关节限位。有效计划会自动执行，界面中的红色“急停”可立即停止仿真臂。

## 本机 HTTP API

场景运行期间，API 仅监听 `127.0.0.1:8765`。

```powershell
Invoke-RestMethod http://127.0.0.1:8765/api/state

Invoke-RestMethod -Method Post `
  -Uri http://127.0.0.1:8765/api/command `
  -ContentType 'application/json' `
  -Body '{"action":"MoveTcp","arm":0,"target":[0.16,0.45,0.31]}'

Invoke-RestMethod -Method Post `
  -Uri http://127.0.0.1:8765/api/command `
  -ContentType 'application/json' `
  -Body '{"action":"Gripper","arm":0,"open":0.0}'

Invoke-RestMethod -Method Post `
  -Uri http://127.0.0.1:8765/api/command `
  -ContentType 'application/json' `
  -Body '{"action":"SetJoints","arm":0,"target":[0,-25,50,-25,0]}'

Invoke-RestMethod -Method Post `
  -Uri http://127.0.0.1:8765/api/control `
  -ContentType 'application/json' `
  -Body '{"action":"stop"}'
```

| 方法 | 路径 | 作用 |
| --- | --- | --- |
| GET | `/health` | API 运行状态 |
| GET | `/api/state` | 仿真、各机械臂状态，以及可抓取物体的位置和抓取状态 |
| GET | `/api/calibration` | 关节名称、限位和工作空间 |
| POST | `/api/command` | `MoveTcp`、`SetJoints`、`Gripper`、`Home`、`Stop`、`Resume` |
| POST | `/api/control` | 全局 `pause`、`resume`、`stop` |

坐标使用 Unity 世界坐标，单位为米，Y 轴向上。当前工作空间限制为 X[-0.48,0.48]、Y[0.43,0.78]、Z[-0.35,0.55]。末端移动目标还会经过 SO-100 近似逆运动学与关节限位检查；关节目标会逐个检查限位。命令由 Unity 主线程处理。

API 每 0.2 秒更新状态快照。若 Unity 主循环停滞超过 3 秒，状态和控制请求会返回 HTTP 503；超时请求不会在恢复后延迟执行。

## 调整机械臂数量

默认生成一台机械臂。打开场景，在 `RobotArmStudio` 组件中把 `Simulated Arm Count` 改为 1 到 4，即可调整仿真机械臂数量。HTTP API 和 AI 规划器按 arm index 控制机械臂。

`SetJoints` 的 `target` 数组必须按 `[shoulder_pan, shoulder_lift, elbow_flex, wrist_flex, wrist_roll]` 顺序提供 5 个角度（度）；夹爪使用独立的 `Gripper` 命令。以后连接实体 SO-100 时，驱动端还需把这些逻辑角度与各舵机的 LeRobot/Feetech 校准值对应起来，不能直接把本仿真的零位当成实机校准零位。

## 工程结构

```text
Assets/
  Editor/       首次场景生成与 Windows 构建
  Scripts/
    AI/         OpenAI 兼容规划器与动作校验
    API/        本机 HTTP API
    App/        场景、中文控制台和运行编排
    Robotics/   驱动接口、机械臂运动学、夹爪与机械臂组
    Shared/     API 数据结构与安全边界
```
