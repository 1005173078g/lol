# 联盟一区对手战绩助手 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一个 Windows 独立桌面程序，在联盟一区新对局载入阶段自动识别敌方五人，查询其最近 20 场排位，并提供可复制的中文分析播报。

**Architecture:** 使用 .NET 8、WPF 和依赖注入。客户端检测、阵容读取、WeGame 查询、统计分析与桌面界面通过小型接口隔离；先交付只读接口探测器并用脱敏夹具锁定协议，再接入正式适配器。所有凭据只存在于进程内存中，日志经过字段级脱敏。

**Tech Stack:** C# 12、.NET 8、WPF、CommunityToolkit.Mvvm 8.x、Microsoft.Extensions.Hosting 8.x、xUnit 2.x、FluentAssertions 6.x

## Global Constraints

- 首版只支持合区后的“联盟一区”，只分析敌方五名玩家。
- 统计样本固定为最近 20 场排位；样本不足时显示实际场次。
- 不注入或修改游戏进程，不模拟按键，不自动发送聊天消息。
- 只使用本机当前 WeGame/英雄联盟登录会话；Cookie、令牌和原始响应不得写入磁盘、日志或仓库。
- MVP 率只使用 WeGame 明确返回的 MVP 标记；字段不存在时显示“不可用”。
- 正常网络下大部分结果目标约 10 秒内出现，但不作硬实时保证。
- 发布为 Windows x64 自包含单文件 `.exe`，用户无需安装 Python 或 .NET 运行时。

---

## Planned File Map

- `LolScout.sln`: 解决方案入口。
- `src/LolScout.Core/Domain/*.cs`: 对局、玩家、比赛和分析结果的不可变领域类型。
- `src/LolScout.Core/Abstractions/*.cs`: 客户端、阵容、战绩和时钟接口。
- `src/LolScout.Core/Analysis/PlayerAnalyzer.cs`: 最近 20 场统计。
- `src/LolScout.Core/Analysis/BroadcastFormatter.cs`: 游戏聊天精简文本。
- `src/LolScout.Infrastructure/League/*.cs`: 只读客户端发现、阶段和阵容适配。
- `src/LolScout.Infrastructure/WeGame/*.cs`: 会话发现、协议解析和战绩查询。
- `src/LolScout.Infrastructure/Diagnostics/RedactingLogger.cs`: 脱敏日志边界。
- `src/LolScout.App/*.xaml|*.cs`: WPF 托盘、窗口、视图模型和编排服务。
- `tools/LolScout.Probe/*`: 只读本地协议探测工具，只输出脱敏字段结构。
- `tests/*`: 单元、夹具解析、编排和 UI 视图模型测试。

### Task 1: 建立可构建的解决方案与领域契约

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `LolScout.sln`
- Create: `src/LolScout.Core/LolScout.Core.csproj`
- Create: `src/LolScout.Core/Domain/Models.cs`
- Create: `src/LolScout.Core/Abstractions/Ports.cs`
- Create: `tests/LolScout.Core.Tests/LolScout.Core.Tests.csproj`
- Test: `tests/LolScout.Core.Tests/DomainModelTests.cs`

**Interfaces:**
- Produces: `GamePhase`, `PlayerIdentity`, `LiveParticipant`, `RecentMatch`, `PlayerAnalysis`, `ILeagueSession`, `IRecentMatchSource`, `IClock`.

- [ ] **Step 1: 安装并固定 .NET 8 SDK**

Run: `winget install Microsoft.DotNet.SDK.8 --exact`

Expected: `dotnet --list-sdks` 包含 `8.0.x`。安装需要用户批准联网和系统写入。

- [ ] **Step 2: 写失败的领域约束测试**

```csharp
[Fact]
public void PlayerIdentity_rejects_blank_game_name()
{
    var act = () => new PlayerIdentity(" ", "CN1", "联盟一区");
    act.Should().Throw<ArgumentException>();
}
```

Run: `dotnet test tests/LolScout.Core.Tests/LolScout.Core.Tests.csproj`

Expected: FAIL，因为 `PlayerIdentity` 尚不存在。

- [ ] **Step 3: 创建解决方案与最小领域类型**

```csharp
public sealed record PlayerIdentity
{
    public PlayerIdentity(string gameName, string tagLine, string region)
    {
        if (string.IsNullOrWhiteSpace(gameName)) throw new ArgumentException("Game name is required.");
        GameName = gameName.Trim();
        TagLine = tagLine.Trim();
        Region = region.Trim();
    }
    public string GameName { get; }
    public string TagLine { get; }
    public string Region { get; }
}

public enum GamePhase { Waiting, ChampionSelect, Loading, InGame, Ended }
public sealed record LiveParticipant(PlayerIdentity Player, int TeamId, int ChampionId, string ChampionName);
public sealed record RecentMatch(bool Won, bool? IsMvp, string Position, int ChampionId, int Kills, int Deaths, int Assists);
public sealed record PlayerAnalysis(int MatchCount, double WinRate, double? MvpRate, string? PrimaryPosition, double? PrimaryPositionRate, int ChampionMatchCount, double? ChampionWinRate, double? ChampionKda);
```

在 `Ports.cs` 定义：

```csharp
public interface ILeagueSession
{
    Task<GamePhase> GetPhaseAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LiveParticipant>> GetParticipantsAsync(CancellationToken cancellationToken);
}

public interface IRecentMatchSource
{
    Task<IReadOnlyList<RecentMatch>> GetRankedMatchesAsync(PlayerIdentity player, int limit, CancellationToken cancellationToken);
}

public interface IClock { DateTimeOffset UtcNow { get; } }
```

- [ ] **Step 4: 运行测试与提交**

Run: `dotnet test LolScout.sln`

Expected: PASS。

Commit: `git commit -am "build: scaffold lol scout solution"`

### Task 2: 用 TDD 实现 20 场分析和精简播报

**Files:**
- Create: `src/LolScout.Core/Analysis/PlayerAnalyzer.cs`
- Create: `src/LolScout.Core/Analysis/BroadcastFormatter.cs`
- Test: `tests/LolScout.Core.Tests/PlayerAnalyzerTests.cs`
- Test: `tests/LolScout.Core.Tests/BroadcastFormatterTests.cs`

**Interfaces:**
- Consumes: `RecentMatch`, `PlayerAnalysis`, `LiveParticipant`.
- Produces: `PlayerAnalyzer.Analyze(IReadOnlyList<RecentMatch>, int championId)` and `BroadcastFormatter.Format(IReadOnlyList<(LiveParticipant Participant, PlayerAnalysis Analysis)>, int maxLength)`.

- [ ] **Step 1: 写统计失败测试**

```csharp
[Fact]
public void Analyze_uses_at_most_twenty_ranked_matches_and_explicit_mvp_flags()
{
    var matches = Enumerable.Range(0, 25)
        .Select(i => new RecentMatch(i < 12, i < 4, i < 14 ? "中路" : "辅助", 103, 6, 3, 9))
        .ToArray();
    var result = PlayerAnalyzer.Analyze(matches, 103);
    result.MatchCount.Should().Be(20);
    result.WinRate.Should().Be(0.60);
    result.MvpRate.Should().Be(0.20);
    result.PrimaryPosition.Should().Be("中路");
}

[Fact]
public void Analyze_returns_null_mvp_rate_when_flags_are_missing()
{
    var result = PlayerAnalyzer.Analyze([new RecentMatch(true, null, "上路", 122, 1, 1, 1)], 122);
    result.MvpRate.Should().BeNull();
}
```

- [ ] **Step 2: 确认测试失败并实现最小统计**

Run: `dotnet test --filter PlayerAnalyzerTests`

Expected: FAIL，因为 `PlayerAnalyzer` 不存在。

实现时先 `Take(20)`，胜率和位置分母使用实际样本数；KDA 使用 `(kills + assists) / max(1, deaths)` 的逐场平均值；只要任一场 MVP 标记缺失，MVP 率即为 `null`。

- [ ] **Step 3: 写播报长度失败测试并实现**

```csharp
[Fact]
public void Format_never_exceeds_chat_limit()
{
    var text = BroadcastFormatter.Format(FiveAnalyses(), 180);
    text.Length.Should().BeLessThanOrEqualTo(180);
    text.Should().Contain("仅供参考");
}
```

实现优先级固定为：本局英雄至少 3 场的胜率、20 场总体胜率、MVP 率、常用位置；超长时按相反顺序移除字段，再按玩家卡片末尾裁剪。

- [ ] **Step 4: 运行测试与提交**

Run: `dotnet test LolScout.sln`

Expected: PASS。

Commit: `git commit -am "feat: analyze recent ranked performance"`

### Task 3: 建立安全的本地接口探测器

**Files:**
- Create: `tools/LolScout.Probe/LolScout.Probe.csproj`
- Create: `tools/LolScout.Probe/Program.cs`
- Create: `src/LolScout.Infrastructure/LolScout.Infrastructure.csproj`
- Create: `src/LolScout.Infrastructure/Diagnostics/JsonShapeRedactor.cs`
- Test: `tests/LolScout.Infrastructure.Tests/JsonShapeRedactorTests.cs`
- Create locally only: `artifacts/probe/*.shape.json`

**Interfaces:**
- Produces: `JsonShapeRedactor.Describe(JsonElement)`，只返回字段名、JSON 类型和数组长度，不返回字段值。

- [ ] **Step 1: 写脱敏失败测试**

```csharp
[Fact]
public void Describe_never_emits_values_or_secret_headers()
{
    using var doc = JsonDocument.Parse("""{"token":"secret123","players":[{"name":"real-player"}]}""");
    var text = JsonSerializer.Serialize(JsonShapeRedactor.Describe(doc.RootElement));
    text.Should().Contain("players").And.NotContain("secret123").And.NotContain("real-player");
}
```

- [ ] **Step 2: 实现字段结构脱敏器并验证**

对象只输出属性名到子结构映射；数组只分析首个元素并记录数组长度；字符串、数字和布尔值仅输出类型名称；字段名匹配 `token|cookie|authorization|ticket|session` 时输出 `"redacted-field"`。

Run: `dotnet test --filter JsonShapeRedactorTests`

Expected: PASS。

- [ ] **Step 3: 实现只读探测命令**

命令接口固定为：

```text
LolScout.Probe.exe league-phase
LolScout.Probe.exe league-participants
LolScout.Probe.exe wegame-history --current-player --region "联盟一区"
```

探测器仅连接回环地址或由本机 WeGame 配置明确声明的 HTTPS 主机；禁止禁用 TLS 验证。控制台只输出响应状态、耗时和 `JsonShapeRedactor` 结果。原始响应仅在内存解析，`artifacts/probe` 只保存 `.shape.json`。

- [ ] **Step 4: 在 WeGame 与联盟客户端运行时执行实机探测**

Run: `dotnet run --project tools/LolScout.Probe -- league-phase`

Expected: 输出当前阶段和脱敏字段结构，不出现令牌或真实玩家名。

Run: `dotnet run --project tools/LolScout.Probe -- league-participants`

Expected: 载入阶段能确认敌我区分字段、玩家标识字段和英雄字段；若客户端不暴露敌方 ID，则记录明确的 capability failure 并停止正式适配器开发，不绕过限制。

Run: `dotnet run --project tools/LolScout.Probe -- wegame-history --current-player --region "联盟一区"`

Expected: 能确认最近排位列表、胜负、MVP、位置、英雄和 KDA 字段是否存在。任何缺失字段写入能力矩阵。

- [ ] **Step 5: 写能力矩阵并提交安全产物**

Create: `docs/protocol-capabilities.md`，逐项记录 `available/unavailable`、来源进程、阶段和脱敏字段路径。不得写入真实 URL 查询参数、玩家 ID、Cookie 或令牌。

Run: `rg -i "authorization|cookie|token|ticket|session|real-player" docs artifacts`

Expected: 除安全说明文字外无敏感值。

Commit: `git commit -am "feat: add redacted local protocol probe"`

### Task 4: 实现英雄联盟阶段与阵容只读适配器

**Files:**
- Create: `src/LolScout.Infrastructure/League/LeagueClientDiscovery.cs`
- Create: `src/LolScout.Infrastructure/League/LeagueSession.cs`
- Create: `src/LolScout.Infrastructure/League/LeagueDtos.cs`
- Test: `tests/LolScout.Infrastructure.Tests/LeagueSessionTests.cs`
- Test fixture: `tests/LolScout.Infrastructure.Tests/Fixtures/league-participants.sanitized.json`

**Interfaces:**
- Implements: `ILeagueSession`.
- Produces: enemy/team mapping as `IReadOnlyList<LiveParticipant>`.

- [ ] **Step 1: 从 Task 3 的脱敏结构手工创建固定夹具并写失败测试**

测试必须覆盖阶段映射、敌我阵营区分、五名敌方玩家、英雄 ID/名称，以及隐藏敌方信息时的 `ParticipantsUnavailableException`。夹具使用 `Enemy1#TEST` 等虚构 ID。

- [ ] **Step 2: 实现客户端发现与 HTTP 边界**

只读取客户端自身声明的本地连接信息；请求仅发送到 `127.0.0.1`/`localhost`。认证材料保存在局部变量中，HTTP 日志禁用请求头和值记录。进程退出后清空会话对象。

- [ ] **Step 3: 实现 DTO 解析和领域映射**

DTO 属性名必须以 `docs/protocol-capabilities.md` 中实机确认的字段为准；未知响应结构抛出 `ProtocolChangedException`，不得猜测阵营或玩家 ID。

- [ ] **Step 4: 测试与提交**

Run: `dotnet test --filter LeagueSessionTests`

Expected: PASS。

Commit: `git commit -am "feat: read league match participants"`

### Task 5: 实现 WeGame 最近排位适配器

**Files:**
- Create: `src/LolScout.Infrastructure/WeGame/WeGameSessionDiscovery.cs`
- Create: `src/LolScout.Infrastructure/WeGame/WeGameRecentMatchSource.cs`
- Create: `src/LolScout.Infrastructure/WeGame/WeGameDtos.cs`
- Create: `src/LolScout.Infrastructure/WeGame/WeGameErrors.cs`
- Test: `tests/LolScout.Infrastructure.Tests/WeGameRecentMatchSourceTests.cs`
- Test fixture: `tests/LolScout.Infrastructure.Tests/Fixtures/wegame-history.sanitized.json`

**Interfaces:**
- Implements: `IRecentMatchSource.GetRankedMatchesAsync(PlayerIdentity, int, CancellationToken)`.

- [ ] **Step 1: 用脱敏实机结构创建夹具和失败测试**

测试覆盖：最多返回 20 场排位；正确映射胜负、可空 MVP、位置、英雄和 KDA；未登录映射为 `WeGameNotSignedInException`；字段变化映射为 `ProtocolChangedException`；单次 4 秒超时后最多重试一次。

- [ ] **Step 2: 实现会话发现和查询**

只使用 Task 3 确认的本地会话入口。使用命名 `HttpClient`，总超时 4 秒；仅对连接失败、408、429、502、503、504 重试一次，延迟 250 毫秒；401/403 立即返回未登录/无权限错误。

- [ ] **Step 3: 实现 DTO 映射**

只接受队列类型被实机确认属于排位的记录；按时间降序取前 20 场。位置为空保留空字符串，MVP 字段缺失映射为 `null`，不从 KDA 推断。

- [ ] **Step 4: 安全与功能验证后提交**

Run: `dotnet test --filter WeGameRecentMatchSourceTests`

Run: `rg -i "authorization:|cookie:|token=" src tests`

Expected: 测试 PASS，扫描无凭据值。

Commit: `git commit -am "feat: query wegame ranked history"`

### Task 6: 实现新对局编排、去重和逐玩家更新

**Files:**
- Create: `src/LolScout.App/Services/MatchScoutCoordinator.cs`
- Create: `src/LolScout.App/Services/MatchFingerprint.cs`
- Create: `src/LolScout.App/ViewModels/ScoutState.cs`
- Test: `tests/LolScout.App.Tests/MatchScoutCoordinatorTests.cs`

**Interfaces:**
- Consumes: `ILeagueSession`, `IRecentMatchSource`, `PlayerAnalyzer`, `IClock`.
- Produces: `IAsyncEnumerable<ScoutState> WatchAsync(CancellationToken)`.

- [ ] **Step 1: 写状态机失败测试**

使用内存 fake 验证 `Waiting → ChampionSelect → Loading → Querying → Complete`；相同的 `teamId + sorted player IDs + champion IDs` 指纹只自动查询一次；五个请求并发执行；一个失败仍产生其他四个结果；进入 `Ended` 后清除当前指纹。

- [ ] **Step 2: 实现轮询和取消**

等待阶段每 1 秒检查一次；英雄选择/载入阶段每 500 毫秒检查一次；新指纹出现时创建一组关联取消令牌，对局结束时取消未完成请求。状态更新通过 `Channel<ScoutState>` 串行发布。

- [ ] **Step 3: 实现手动刷新语义**

`RefreshAsync` 复用当前窗口和阵容，取消旧查询并启动新查询；不清除自动去重指纹，不创建第二个窗口。

- [ ] **Step 4: 测试与提交**

Run: `dotnet test --filter MatchScoutCoordinatorTests`

Expected: PASS，全部测试使用 fake clock，不做真实等待。

Commit: `git commit -am "feat: orchestrate live opponent scouting"`

### Task 7: 构建 WPF 窗口、托盘和剪贴板交互

**Files:**
- Create: `src/LolScout.App/LolScout.App.csproj`
- Create: `src/LolScout.App/App.xaml`
- Create: `src/LolScout.App/App.xaml.cs`
- Create: `src/LolScout.App/MainWindow.xaml`
- Create: `src/LolScout.App/MainWindow.xaml.cs`
- Create: `src/LolScout.App/ViewModels/MainWindowViewModel.cs`
- Create: `src/LolScout.App/ViewModels/PlayerCardViewModel.cs`
- Create: `src/LolScout.App/Services/ClipboardService.cs`
- Create: `src/LolScout.App/Services/TrayIconService.cs`
- Test: `tests/LolScout.App.Tests/MainWindowViewModelTests.cs`

**Interfaces:**
- Consumes: `MatchScoutCoordinator`, `BroadcastFormatter`.
- Produces commands: `CopyBroadcastCommand`, `RefreshCommand`, `ToggleTopmostCommand`, `ShowStatusCommand`.

- [ ] **Step 1: 写视图模型失败测试**

验证五张卡片可独立从 Loading 更新为 Complete/Error；复制命令调用抽象剪贴板且文本不超过 180 字；置顶命令切换布尔值；新对局第一次结果使 `ShouldShowWindow` 为真。

- [ ] **Step 2: 实现视图模型与命令**

使用 `ObservableObject` 和 `AsyncRelayCommand`。所有来自协调器的更新切回 WPF Dispatcher；剪贴板失败时显示可重试状态，不使程序退出。

- [ ] **Step 3: 实现窗口布局和托盘**

窗口包含顶部运行状态、五张玩家卡片、底部四个按钮。关闭按钮默认隐藏到托盘，托盘菜单包含“显示”“重新查询”“退出”。自动弹窗只在新对局首次获得敌方阵容时发生。

- [ ] **Step 4: 测试与提交**

Run: `dotnet test LolScout.sln`

Expected: PASS。

Commit: `git commit -am "feat: add desktop opponent scout window"`

### Task 8: 加固日志、发布与中文文档

**Files:**
- Create: `src/LolScout.Infrastructure/Diagnostics/RedactingLogger.cs`
- Create: `.gitignore`
- Create: `README.md`
- Create: `docs/troubleshooting.md`
- Create: `scripts/publish.ps1`
- Create: `.github/workflows/build.yml`
- Test: `tests/LolScout.Infrastructure.Tests/RedactingLoggerTests.cs`

**Interfaces:**
- Produces: Windows x64 self-contained single-file release under `artifacts/publish/win-x64/`.

- [ ] **Step 1: 写日志脱敏失败测试**

输入包含 `Authorization`、`Cookie`、`token`、真实 ID 和完整响应的结构化属性；断言输出只保留错误代码、HTTP 状态、阶段和缺失字段名。

- [ ] **Step 2: 实现日志白名单和仓库忽略规则**

日志属性仅允许 `Timestamp`, `Phase`, `ErrorCode`, `StatusCode`, `MissingField`, `DurationMs`。`.gitignore` 必须排除 `artifacts/`, `*.log`, `*.dmp`, `**/bin/`, `**/obj/` 和本地会话文件。

- [ ] **Step 3: 写发布脚本与 CI**

`scripts/publish.ps1` 执行：

```powershell
dotnet test LolScout.sln -c Release
dotnet publish src/LolScout.App/LolScout.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish/win-x64
```

CI 在 `windows-latest` 上执行还原、测试和发布，但不运行实机探测，不上传任何本地夹具。

- [ ] **Step 4: 写 README 和排障文档**

README 明确联盟一区、WeGame 登录前置条件、启动顺序、状态含义、复制操作、隐私承诺、接口更新风险和构建命令。排障文档覆盖未登录、未识别客户端、敌方信息不可见、接口变化和网络超时。

- [ ] **Step 5: 全量验证、敏感信息扫描和发布**

Run: `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1`

Expected: 全部测试 PASS，`artifacts/publish/win-x64/LolScout.App.exe` 存在。

Run: `rg -i "authorization:|cookie:|token=|ticket=|session=" . --glob '!docs/superpowers/**' --glob '!**/bin/**' --glob '!**/obj/**'`

Expected: 无真实凭据。

Run: `git status --short`

Expected: 仅被 `.gitignore` 排除的发布产物，无未提交源码。

Commit: `git commit -am "docs: add secure release workflow"`

## Final Acceptance Run

- [ ] 启动 WeGame 并登录联盟一区，随后启动 `LolScout.App.exe`。
- [ ] 进入一场允许测试的对局，确认载入阶段自动出现敌方五人及英雄。
- [ ] 确认五张卡片逐个更新，一个查询失败不会阻塞其他人。
- [ ] 对照 WeGame 手工抽查至少两名玩家的 20 场胜率、MVP、位置和本局英雄统计。
- [ ] 点击“复制精简播报”，粘贴到记事本核对长度和内容；不要求也不执行自动游戏聊天。
- [ ] 结束对局后确认程序回到等待状态，同一局没有重复弹窗。
- [ ] 扫描日志、发布包和 Git 历史，确认无 Cookie、令牌、真实响应或本机路径。
