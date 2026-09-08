# UnitySkills 安装与排障指南

本文档面向本地开发环境，说明如何安装 UnitySkills、如何把 AI Skill 模板放到目标工具、以及在编译或 Domain Reload 期间应该如何处理短暂不可达。

## 环境要求

- Unity：`2022.3+`
- 推荐重点验证版本：`2022.3 LTS` 与 `Unity 6`
- 网络环境：本地回环地址 `localhost` / `127.0.0.1`
- 典型 AI 客户端：Claude Code、Codex、Gemini CLI、Antigravity、Cursor

## 安装 Unity 包

### Package Manager 安装

在 Unity 中打开：

```text
Window > Package Manager > + > Add package from git URL
```

使用以下地址之一：

稳定版：

```text
https://github.com/Besty0728/Unity-Skills.git?path=/SkillsForUnity
```

Beta：

```text
https://github.com/Besty0728/Unity-Skills.git?path=/SkillsForUnity#beta
```

指定版本：

```text
https://github.com/Besty0728/Unity-Skills.git?path=/SkillsForUnity#v1.6.3
```

## 启动服务

在 Unity 编辑器中打开：

```text
Window > UnitySkills > Start Server
```

正常情况下，Console 会输出类似内容：

```text
[UnitySkills] REST Server started at http://localhost:8090/
```

## 安装 AI Skill 模板

### 推荐：使用 Unity 内置安装器

打开：

```text
Window > UnitySkills > Skill Installer
```

选择目标 AI 工具后执行安装。安装器会复制包内的 `unity-skills~/` 模板目录到目标位置。

目标目录应至少包含：

- `SKILL.md`
- `skills/`
- `scripts/unity_skills.py`
- `scripts/agent_config.json`

### 手动安装

如果不使用安装器，请把 UPM 包内的 `SkillsForUnity/unity-skills~/` 目录内容复制到你的 AI 工具技能目录中。

常见目录：

- Claude Code：`~/.claude/skills/`
- Codex：`~/.codex/skills/`
- Gemini CLI：`~/.gemini/skills/`
- Antigravity：`~/.agent/skills/`
- Cursor：`~/.cursor/skills/`

对于 Codex，推荐全局安装。项目级安装时，还需要在项目根目录的 `AGENTS.md` 中声明该技能。

## Python 客户端行为

`unity_skills.py` 当前具备以下行为：

- 默认请求超时为 `900` 秒，也就是 `15 分钟`
- 初始化时会从 `/health` 同步服务端超时设置
- 复用 `requests.Session`，减少频繁新建连接
- 遇到编译或 Domain Reload 导致的短暂断连时，会把错误标记为可重试
- `WorkflowContext` 在超时或连接异常后，会尝试读取服务端状态并恢复工作流一致性

## 编译、Domain Reload 与短暂不可达

以下操作都可能让服务短时间不可达：

- `script_create`
- `script_append`
- `script_replace`
- `debug_force_recompile`
- `debug_set_defines`
- 某些 `asset_import` / `asset_reimport` / `asset_move`
- 测试模板创建
- 部分包安装或移除

这是 Unity 编辑器行为，不是异常崩溃。建议做法：

1. 收到“暂时不可用”或连接超时后，先等待几秒。
2. 调用 `wait_for_unity()` 或使用 `call_skill_with_retry()`。
3. 脚本生成后，优先读取编译反馈，再继续后续步骤。

脚本示例：

```python
import unity_skills

result = unity_skills.create_script("PlayerController")
if result.get("success"):
    print(result.get("compilation"))
```

## macOS：编辑器在后台被饿死，服务不会自动回来

Domain Reload 之后服务本来会自己恢复：`SkillsHttpServer` 注册了
`EditorApplication.delayCall += CheckAndRestoreServer`，失败还会按 1s / 2s / 4s 重试三次，
连续失败 5 次才彻底放弃。

但在 macOS 上会遇到这样一种情况：**端口彻底没有监听，服务再也不回来，而 Unity 本身完全正常。**

先按下面的顺序区分，不要一上来就重启编辑器：

```bash
# 1. 端口是否真的在监听？
lsof -nP -iTCP:8090 -sTCP:LISTEN

# 2. 编辑器是卡死还是空闲？CPU 接近 0 且 Editor.log 长时间不再增长 = 空闲，不是卡死
ps -o pid,%cpu,stat,command -p <unity-pid>
stat -f "%Sm %N" ~/Library/Logs/Unity/Editor.log

# 3. 主线程到底在做什么（真正的判据）
sample <unity-pid> 3 -file /tmp/unity-sample.txt
```

如果主线程停在 `NSApplication run` → `__CFRunLoopRun` → `onEditorUpdatesTickTimer`，
那说明编辑器在正常跑事件循环，**没有卡死**，问题只在于服务没有重新绑定端口。

再看恢复计数器（把 `<InstanceId>` 换成你的实例名，可从 `/health` 的 `instanceId` 取得）：

```bash
defaults read com.unity3d.UnityEditor5.x | grep UnitySkills_<InstanceId>
```

如果看到的是：

```
UnitySkills_<InstanceId>_ConsecutiveRestartFailures = 0
UnitySkills_<InstanceId>_ServerShouldRun            = 1
```

失败计数为 0、而旗标为 1，说明恢复逻辑**根本没有被执行过**，不是执行了但失败。
`delayCall` 依赖编辑器 tick，而 macOS 的 **App Nap** 会把后台的 Unity 节流到几乎不 tick，
于是恢复回调永远排不到。此时你会观察到一个很有迷惑性的现象：把 Unity 切到前台也没用——
因为焦点只维持了几秒就还回去了，编辑器立刻又被饿死。

### 根治

```bash
# 关闭 App Nap（需重启 Unity 才完全生效）
defaults write com.unity3d.UnityEditor5.x NSAppSleepDisabled -bool YES

# 对当前进程立即解除后台 QoS 降级，无需重启
taskpolicy -B -p <unity-pid>
```

顺带检查这一项，它是另一个独立问题：

```bash
defaults read com.unity3d.UnityEditor5.x | grep kAutoRefresh
```

`kAutoRefresh = 0` 表示 **Auto Refresh 是关闭的**，改动的 `.cs` 永远不会被 import，
因此也不会重新编译——`editor_execute_menu` 会安静地跑在上一次编译的旧代码上。
这个坑的特征是：改了代码、重跑，结果**一模一样**。请在
`Preferences → Asset Pipeline → Auto Refresh` 打开；
或者在每次改完文件后显式调用一次 `asset_refresh`。

不要直接改 plist 里的 `kAutoRefresh`：Unity 运行期间用的是内存里的值，退出时会覆盖回去。

### 应急恢复

服务已经死掉、没有任何 REST 通道可用时，用界面把它叫回来。
注意没有单独的“Start Server”菜单项，**打开窗口这个动作本身**就会触发服务启动：

```applescript
-- 打开 Window > UnitySkills，然后把焦点还给原来的应用
set previousApp to ""
try
	tell application "System Events" to set previousApp to name of first application process whose frontmost is true
end try

tell application "Unity" to activate
delay 1.5
tell application "System Events" to tell process "Unity" to ¬
	click menu item "UnitySkills" of menu "Window" of menu bar 1
delay 2

if previousApp is not "" and previousApp is not "Unity" then
	tell application "System Events" to set frontmost of application process previousApp to true
end if
```

如果只是想让编辑器动一动（例如触发一次 import），把焦点给它、**一直等到它真的响应**再还回去。
睡固定秒数是没用的：焦点会在编译中途被交还，Unity 立刻重新进入饥饿状态，工作永远做不完。

```bash
#!/bin/bash
# 抓住焦点直到 /health 有响应为止，然后还给原来的应用
previous=$(osascript -e 'tell application "System Events" to get name of first application process whose frontmost is true')
trap '[ -n "$previous" ] && [ "$previous" != "Unity" ] && osascript -e "tell application \"System Events\" to set frontmost of application process \"$previous\" to true"' EXIT

osascript -e 'tell application "Unity" to activate'
waited=0
while [ "$waited" -lt "${1:-120}" ]; do
  curl -s -m 5 http://localhost:8090/health | grep -q '"status":"ok"' && exit 0
  sleep 3; waited=$((waited + 3))
done
exit 1
```

## 多实例路由

如果本机同时打开多个 Unity 项目，优先通过版本或目标名选择实例：

```python
import unity_skills

unity_skills.set_unity_version("2022.3")
unity_skills.call_skill("project_get_info")
```

也可以通过注册表枚举实例：

```python
import unity_skills

print(unity_skills.list_instances())
```

## 批量优先原则

当你要操作 2 个及以上对象时，优先使用 `*_batch` 技能，原因是：

- 请求数更少
- 编译窗口更短
- 工作流快照更集中
- AI 更不容易在循环里打爆请求队列

示例：

```python
unity_skills.call_skill(
    "gameobject_create_batch",
    items=[
        {"name": "Cube_A", "primitiveType": "Cube", "x": -1},
        {"name": "Cube_B", "primitiveType": "Cube", "x": 1},
    ],
)
```

## 测试模块说明

- `test_run` 和 `test_run_by_name` 对接的是 Unity Test Runner。
- 调用后立即返回 `jobId`。
- 使用 `test_get_result(jobId)` 轮询结果。
- 这不是启动独立的 Unity 可执行进程，而是在当前编辑器上下文里执行测试任务。

## 常见排障

| 问题 | 现象 | 建议 |
| --- | --- | --- |
| 连接失败 | `Cannot connect to http://localhost:8090` | 检查 Unity 是否已启动服务，或是否正处于编译 / Domain Reload |
| 请求超时 | 超过 15 分钟后返回超时 | 先确认是否是长任务；必要时在 Unity 面板中调高超时设置 |
| 技能列表为空 | `/skills` 返回异常 | 检查控制台是否有编译错误，确保插件成功导入 |
| 脚本创建后断连 | 创建脚本后接口暂时不可用 | 正常现象，等待编译完成后重试 |
| 多实例误连 | 请求打到了错误项目 | 先调用 `set_unity_version()` 或按目标名连接 |
| 工作流状态异常 | 本地认为开始了任务，但服务端状态不一致 | 重新读取 `workflow_session_status`，当前客户端已内置恢复逻辑 |
| 服务再也不回来（macOS） | 端口无监听，但 Unity 未卡死、CPU 接近 0、`ConsecutiveRestartFailures` 仍为 0 | App Nap 把后台编辑器节流到 `delayCall` 排不上；见[后台被饿死](#macos编辑器在后台被饿死服务不会自动回来) |
| 改了代码但行为完全不变 | 重跑得到**一模一样**的结果，控制台无编译错误 | `kAutoRefresh = 0`，文件从未被 import；打开 Auto Refresh，或每次改完显式 `asset_refresh` |

## 文档索引

- [中文 README](../README.md)
- [English README](../README_EN.md)
- [AI Skill 入口](../SkillsForUnity/unity-skills~/SKILL.md)
- [更新日志](../CHANGELOG.md)
