# Task 8 实现报告

## 完成内容

- 新增 `RedactingLogger`：JSON Lines 输出，调用方只能提供 `Phase`、`ErrorCode`、`StatusCode`、`MissingField`、`DurationMs`；`Timestamp` 由日志器生成。Authorization、Cookie、token、真实 ID、完整响应和本机路径等未授权字段直接丢弃。
- 加固 `.gitignore`，排除发布物、日志、dump、bin/obj 和本地会话/凭据文件。
- 新增中文 README 与排障文档，准确记录管理员权限、WeGame + 联盟一区、2999 敌方身份出现阶段、LCU 战绩实机验证、MVP/常用位置不可用、首次冷 history 可能超时且可刷新，以及不自动聊天。
- 新增 `scripts/publish.ps1`：运行 Release 全套测试，生成自包含 win-x64 单 EXE，校验源 manifest 与发布 EXE 均包含 `requireAdministrator`，拒绝额外发布文件、敏感命名文件、日志/dump/响应文件和本机路径标记。
- 新增 Windows GitHub Actions：还原、测试、发布，仅上传单 EXE；不运行实机探测。
- 修复 Task7 Minor：托盘构造失败时，即使 icon.Dispose 抛出仍释放 menu，且不覆盖原始构造异常；说明 `AppComposition.Dispose` 为空是因为组合根未持有可释放资源。
- 修复发布阻断测试波动：并发刷新测试此前在 channel 已发布 Complete、后台测试观察器尚未入队时立即断言。等待观察器收到第二个 Complete 后再断言，不改变产品代际逻辑。

## TDD 证据

- `RedactingLoggerTests` 初次编译失败：`RedactingLogger` 不存在；实现后 Infrastructure 53/53 通过。
- 托盘回归测试初次失败：期望保留 `configure failed`，实际被 `dispose failed` 覆盖且 menu 未释放；修复后通过。
- 并发测试在完整发布中复现为 Complete 期望 2、实际观察到 1；修正观察器同步后连续 50/50 次通过。

## 最终验证

- `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1`：Core 17/17、Infrastructure 53/53、App 29/29，共 99/99 通过；发布及安全检查成功。
- 发布目录仅含 `artifacts/publish/win-x64/LolScout.App.exe`，未提交 artifacts。
- EXE 基本启动检查：进程保持运行 3 秒后人工终止，未进入游戏、未执行自动操作。
- `git diff --check`：通过。
- 工作树与 Git 历史敏感模式扫描只命中明确标为 `fictional`/`secret` 的测试夹具和脱敏测试断言；未发现真实凭据、真实响应或真实本机用户路径。发布包安全扫描通过。

## 未执行

- 未在真实对局中自动操作或复跑人工验收；遵守本任务仅做 EXE 基本启动检查的边界。

## 独立审查修复

- 日志值边界改为 fail-closed：直接使用 `Utf8JsonWriter` 写安全标量，不序列化调用方对象；复杂对象、异常、集合、嵌套值和恶意 `ToString` 均不能进入输出。Phase、ErrorCode、StatusCode、MissingField、DurationMs 分别实施枚举、正则、类型和范围约束。
- 新增 6 个日志边界用例（定向共 8/8），覆盖嵌套 token、异常、数组、路径、玩家式 ID、恶意 `ToString`、安全枚举和数值。
- 发布通过 Microsoft 官方 `Microsoft.Windows.SDK.BuildTools` NuGet 构建依赖定位 x64 `mt.exe`，从最终 PE 的 `#1` manifest 资源提取 XML，并精确断言唯一 `requestedExecutionLevel/@level` 为 `requireAdministrator`；工具缺失即失败，临时文件始终删除。
- 发布前清空输出，发布后递归断言总共只有一个 `LolScout.App.exe` 且无子目录。源码/README/排障扫描 Git 跟踪文本中的凭据赋值和机器绝对路径；EXE 扫描 ASCII/UTF-16 可打印字符串中的工作树、构建用户名上下文、测试 secret 和探测标记。`PathMap` 将编译元数据中的真实工作树路径替换为 `/_/`。
- 对构建用户名 `Administrator` 采用机器路径上下文匹配，因为自包含 .NET runtime 和必须存在的 manifest 自身包含通用权限词 `Administrator`/`requireAdministrator`；README 明确了扫描能力和这一窄例外。
- README 将 2999 更正为 Riot 开发者文档记录的本地 Live Client Data API，并明确 LCU 为 unsupported/private、无稳定性承诺。CI 去除重复 restore，由同一发布脚本统一还原、测试、manifest 验证与扫描。
- 复审后最终发布验证：Core 17/17、Infrastructure 59/59、App 29/29，共 105/105 通过；PE manifest XML、单 EXE 结构、源码与 EXE 安全扫描全部通过。
