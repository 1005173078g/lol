# Task 4 实现报告

## 状态

已完成 League 阶段与参赛者只读适配器、可替换的进程枚举/HTTP 边界、严格 loopback 限制和 rclient 证书固定验证。未实现 Task 5 战绩功能。

提交 SHA：本报告所在提交（以 `git rev-parse HEAD` 为准）。

## 实现摘要

- 仅从 `LeagueClientUx.exe` 自声明命令行读取 `--app-port` 和 `--remoting-auth-token`；默认 Windows 枚举器不落盘输出。
- LCU 请求仅访问 `https://127.0.0.1:<declared-port>`；载入/游戏阶段访问官方 `https://127.0.0.1:2999/liveclientdata/playerlist`。
- 认证 token 仅作为局部连接值和请求头存在于内存，不写日志、不写 fixture。
- TLS 同时验证固定 SHA-256、精确 `CN=rclient`、Issuer 包含 `Riot Games` 和有效期；不使用 accept-any 验证器。
- 敌方信息隐藏时抛 `ParticipantsUnavailableException`，未知阶段/结构抛 `ProtocolChangedException`，不猜测玩家或阵营。
- 测试 fixture 只含 `Enemy1#TEST` 等虚构身份。

## 验证命令与结果

- `dotnet test --filter LeagueSessionTests`：通过 12，失败 0。
- `dotnet test`：Core 通过 11，Infrastructure 通过 20，合计通过 31，失败 0。
- `rg -n "DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback\\s*=\\s*\\(.*=>\\s*true|https?://(?!127\\.0\\.0\\.1)" ... -P`：无匹配。
- `git diff --check`：通过；仅 Git 提示现有行尾将在未来转换为 CRLF。

## 关注事项

- `docs/protocol-capabilities.md` 仍记录本机字段形状“未验证”。本实现依据本任务已批准的协议决定和虚构脱敏 fixture 完成，**不声称本次对真实对局 API 调用成功**。实机可用时仍应重新执行只读探测并核对 DTO 字段。
- 当前仓库没有最终 GUI/可执行应用项目，因此管理员权限 manifest 应在应用宿主任务中配置；本任务提供的 Windows 进程枚举按“宿主已以管理员运行”的前提执行。
- 证书轮换会触发 `CertificatePinMismatchException`，必须人工核验新证书身份后再更新固定值，不能降级为 accept-any。
