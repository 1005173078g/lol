# 联盟一区对手战绩助手

这是一个 Windows 桌面辅助工具：在英雄联盟进入加载/游戏阶段后读取 Riot 开发者文档记录的本地 Live Client Data API 所提供的敌方身份，再查询近期战绩并逐张更新对手卡片。程序只展示信息和复制精简播报，**不会自动发送游戏聊天**。

> 本工具不是 Riot Games 官方产品，未获得 Riot Games 背书，也不代表 Riot Games 或其相关方的观点。Riot Games、League of Legends 及相关商标归其各自权利人所有。

## 使用条件

- Windows x64；程序清单要求以管理员身份运行，启动时会出现 UAC 提示。
- WeGame 已登录，并从 WeGame 启动 **联盟一区** 的英雄联盟客户端。
- 先启动并登录 WeGame 与游戏客户端，再运行 `LolScout.App.exe`。

敌方身份通常要到加载或游戏阶段，才会由本地 Live Client Data API（端口 `2999`）出现；选人阶段看不到敌方并不一定是故障。LCU 战绩查询在当前客户端版本上做过实机验证，但 LCU 属于 unsupported/private 接口，没有稳定性承诺，客户端更新后可能变化。近期胜率、场次和本局英雄相关统计会在可用时显示；**MVP 与常用位置目前因 LCU 没有可信字段而显示“不可用”**，本工具不承诺这两项完整。

## 使用方式

1. 依上述顺序启动 WeGame、英雄联盟和本程序。
2. 窗口状态会在“等待客户端/等待对局/查询中/完成/部分失败”之间变化；查询结果逐张更新，单个玩家失败不会阻塞其他玩家。
3. 第一次查询冷 history 数据时可能超时，可点击“重新查询”或托盘菜单中的“重新查询”。
4. 点击“复制精简播报”只写入系统剪贴板，由用户决定是否以及在哪里粘贴。

## 隐私与限制

程序使用本机客户端接口，不记录 Authorization、Cookie、token、会话文件、真实玩家 ID、完整响应或本机路径。诊断日志对字段名和值同时做白名单、类型、格式和范围校验，仅允许时间、阶段、错误码、HTTP 状态、缺失字段名和耗时。发布扫描能检查明确列出的凭据赋值、路径、测试/探测标记和 ASCII/UTF-16 可打印字符串，但不能证明任意编码或加密数据绝对不存在；当构建用户名为 Windows 保留术语 `Administrator` 时，只在用户路径等机器特定上下文中匹配，以免将 .NET 运行库的通用权限术语误判为泄漏。客户端更新可能改变 unsupported/private LCU 接口形状；遇到问题请参考 [排障文档](docs/troubleshooting.md)。

## Riot 政策与公开分发

本仓库不声称已经完成 Riot 产品登记。LCU 是 unsupported/private 接口，客户端更新可随时使功能失效。维护者在公开分发或发布任何二进制前，必须先在 Riot Developer Portal 登记产品，并重新核对、遵守当时有效的 Riot 开发者政策。详见 [Riot 政策与发布门禁](docs/riot-policy.md)。

普通 `publish.ps1` 只生成本地产物。公开发布流程必须使用 `-PublicRelease`，并且只有在维护者实际确认登记与当前政策合规后，才可把仓库变量或环境变量 `RIOT_PRODUCT_REGISTRATION_CONFIRMED` 设为精确的小写 `true`。当前 GitHub Actions 仅验证构建，不上传 artifact，也不创建 GitHub Release。

## 构建与发布

需要 .NET 8 SDK。在仓库根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

脚本会测试、发布并执行安全检查，生成自包含的 Windows x64 单文件 `artifacts/publish/win-x64/LolScout.App.exe`。发布包不需要目标机器预装 .NET。
