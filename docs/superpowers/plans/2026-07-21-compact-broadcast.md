# Compact Broadcast Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the overall match-count suffix and trailing disclaimer from copied broadcast text without changing cards or statistics.

**Architecture:** Keep all behavior inside `BroadcastFormatter`; change only its rendering rules and their focused tests. Preserve the existing field priority and 180-character caller limit, while making an empty result render as an empty string.

**Tech Stack:** C# 12, .NET 8, xUnit, FluentAssertions, WPF publishing for Windows x64

## Global Constraints

- Only copied broadcast text changes; player cards, queries, and statistic definitions remain unchanged.
- Overall win rate renders as `总胜率50%` without `(20场)`.
- Champion win-rate sample count remains, such as `英雄胜率67%(3场)`.
- High-KDA count and Chinese semicolon player separators remain.
- No `| 仅供参考` suffix or leftover delimiter is rendered.
- Existing maximum-length enforcement and field-removal priority remain.

---

### Task 1: Simplify broadcast rendering

**Files:**
- Modify: `tests/LolScout.Core.Tests/BroadcastFormatterTests.cs`
- Modify: `src/LolScout.Core/Analysis/BroadcastFormatter.cs`

**Interfaces:**
- Consumes: `BroadcastFormatter.Format(IReadOnlyList<(LiveParticipant Participant, PlayerAnalysis Analysis)> players, int maxLength)`
- Produces: the same public method signature with shorter plain-text output

- [ ] **Step 1: Write the failing tests**

Update the complete-output assertions:

```csharp
text.Should().Contain("英雄胜率67%(3场)");
text.Should().Contain("总胜率55%");
text.Should().NotContain("总胜率55%(20场)");
text.Should().Contain("高KDA6场");
text.Should().Contain("常用中路70%");
text.Should().NotContain("仅供参考");
text.Should().NotEndWith("|");
```

Update the chat-limit test so it rejects the removed disclaimer:

```csharp
text.Length.Should().BeLessThanOrEqualTo(180);
text.Should().NotContain("仅供参考");
```

Change the empty-input test to:

```csharp
[Fact]
public void Format_returns_empty_when_there_are_no_players()
{
    BroadcastFormatter.Format([], 3).Should().BeEmpty();
    BroadcastFormatter.Format([], 180).Should().BeEmpty();
}
```

Use a 38-character limit so the reclaimed disclaimer space does not prevent the removal path from being exercised:

```csharp
var text = BroadcastFormatter.Format([AnalysisFor("甲")], 38);

text.Should().Contain("英雄胜率67%(3场)");
text.Should().Contain("总胜率55%");
text.Should().Contain("高KDA6场");
text.Should().NotContain("常用");
text.Should().NotContain("仅供参考");
text.Length.Should().BeLessThanOrEqualTo(38);
```

- [ ] **Step 2: Run focused tests and verify RED**

Run:

```powershell
dotnet test tests/LolScout.Core.Tests/LolScout.Core.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --filter BroadcastFormatterTests
```

Expected: failures show the current output still contains `(20场)` and `仅供参考`, and empty input still returns the disclaimer.

- [ ] **Step 3: Implement the minimal renderer change**

Remove the `Disclaimer` constant and the disclaimer-length early return. Return empty for unusable input:

```csharp
if (maxLength <= 0 || players.Count == 0)
{
    return string.Empty;
}
```

Render only player cards:

```csharp
private static string Render(IReadOnlyList<PlayerCard> cards) =>
    string.Join("；", cards.Select(card => card.Render()));
```

Render overall win rate without its sample count:

```csharp
if (ShowOverallWinRate)
{
    fields.Add($"总胜率{Percentage(Analysis.WinRate)}");
}
```

- [ ] **Step 4: Run focused and full tests**

Run:

```powershell
dotnet test tests/LolScout.Core.Tests/LolScout.Core.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false --filter BroadcastFormatterTests
dotnet test LolScout.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false
```

Expected: all formatter tests pass; the full solution reports zero failures.

- [ ] **Step 5: Commit the tested change**

```powershell
git add tests/LolScout.Core.Tests/BroadcastFormatterTests.cs src/LolScout.Core/Analysis/BroadcastFormatter.cs
git commit -m "fix: shorten copied broadcast text"
```

### Task 2: Publish, launch, and deliver

**Files:**
- Regenerate: `artifacts/publish/win-x64/LolScout.App.exe`

**Interfaces:**
- Consumes: tested `LolScout.sln` and `scripts/publish.ps1`
- Produces: a running Windows x64 executable and an updated `feat/opponent-scout` branch

- [ ] **Step 1: Publish the verified application**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

Expected: release tests report zero failures and the script prints the manifest-validated, security-scanned EXE path.

- [ ] **Step 2: Replace the running application**

Stop only `LolScout.App`, then launch:

```text
C:\tmp\lol-design-repo\.worktrees\opponent-scout\artifacts\publish\win-x64\LolScout.App.exe
```

Expected: one new `LolScout.App` process is running from the published path.

- [ ] **Step 3: Push the feature branch**

```powershell
git push -u origin feat/opponent-scout
```

Expected: local `HEAD` equals `origin/feat/opponent-scout`, and the existing pull request contains the new commits.
