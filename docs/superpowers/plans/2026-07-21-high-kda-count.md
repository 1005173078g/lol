# High KDA Count Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace unavailable MVP output with the count of ranked matches whose per-game KDA is at least 5.0.

**Architecture:** `PlayerAnalyzer` owns the threshold and derives the count from the same capped 20-match sample used by every other metric. `PlayerAnalysis` carries the derived integer to the existing card and broadcast formatters; neither presentation layer recalculates raw match data.

**Tech Stack:** .NET 8, C# records, WPF/MVVM, xUnit, FluentAssertions.

## Global Constraints

- Per-game KDA is `(Kills + Assists) / max(1, Deaths)`.
- A match is high KDA when its value is greater than or equal to `5.0`.
- Use only the actual ranked sample, capped at 20 matches.
- Display this as “高KDA”, never as Tencent MVP/SVP.
- Preserve the existing 180-character broadcast limit.

---

### Task 1: Derive High KDA Count in the Analysis Layer

**Files:**
- Modify: `src/LolScout.Core/Domain/Models.cs`
- Modify: `src/LolScout.Core/Analysis/PlayerAnalyzer.cs`
- Test: `tests/LolScout.Core.Tests/PlayerAnalyzerTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyList<RecentMatch>` and selected `championId` through `PlayerAnalyzer.Analyze`.
- Produces: `PlayerAnalysis.HighKdaMatchCount : int`.

- [ ] **Step 1: Write the failing boundary test**

```csharp
[Fact]
public void Analyze_counts_kda_at_least_five_and_treats_zero_deaths_as_one()
{
    RecentMatch[] matches =
    [
        new(true, null, "MID", 1, 6, 2, 4), // 5.0
        new(true, null, "MID", 1, 4, 1, 0), // 4.0
        new(true, null, "MID", 1, 2, 0, 3)  // 5.0
    ];

    var result = PlayerAnalyzer.Analyze(matches, 1);

    result.HighKdaMatchCount.Should().Be(2);
}
```

- [ ] **Step 2: Run the test and verify RED**

Run: `dotnet test tests/LolScout.Core.Tests/LolScout.Core.Tests.csproj --no-restore --filter Analyze_counts_kda_at_least_five`

Expected: compile failure because `HighKdaMatchCount` does not exist.

- [ ] **Step 3: Add the model field and minimal calculation**

Append an optional positional property to preserve existing test helpers:

```csharp
public sealed record PlayerAnalysis(
    int MatchCount,
    double WinRate,
    double? MvpRate,
    string? PrimaryPosition,
    double? PrimaryPositionRate,
    int ChampionMatchCount,
    double? ChampionWinRate,
    double? ChampionKda,
    int HighKdaMatchCount = 0);
```

In `PlayerAnalyzer`, define `private const double HighKdaThreshold = 5.0;`, calculate:

```csharp
var highKdaMatchCount = sample.Count(match =>
    (double)(match.Kills + match.Assists) / Math.Max(1, match.Deaths) >= HighKdaThreshold);
```

Pass `highKdaMatchCount` as the final `PlayerAnalysis` constructor argument.

- [ ] **Step 4: Run Core tests and verify GREEN**

Run: `dotnet test tests/LolScout.Core.Tests/LolScout.Core.Tests.csproj --no-restore`

Expected: all Core tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/LolScout.Core/Domain/Models.cs src/LolScout.Core/Analysis/PlayerAnalyzer.cs tests/LolScout.Core.Tests/PlayerAnalyzerTests.cs
git commit -m "feat: calculate high kda match count"
```

### Task 2: Replace MVP Presentation in Cards and Broadcasts

**Files:**
- Modify: `src/LolScout.App/ViewModels/PlayerCardViewModel.cs`
- Modify: `src/LolScout.Core/Analysis/BroadcastFormatter.cs`
- Test: `tests/LolScout.App.Tests/MainWindowViewModelTests.cs`
- Test: `tests/LolScout.Core.Tests/BroadcastFormatterTests.cs`

**Interfaces:**
- Consumes: `PlayerAnalysis.HighKdaMatchCount` and `PlayerAnalysis.MatchCount` from Task 1.
- Produces: card text `高KDA X/N（≥5.0）` and broadcast field `高KDA X场`.

- [ ] **Step 1: Write failing card and broadcast tests**

Add a card assertion after applying an analysis whose `HighKdaMatchCount` is 6:

```csharp
viewModel.Players[0].Mvp.Should().Be("高KDA 6/20（≥5.0）");
```

Replace the obsolete MVP formatter test with:

```csharp
[Fact]
public void Format_includes_high_kda_match_count()
{
    var item = AnalysisFor("玩家");
    var analysis = item.Analysis with { HighKdaMatchCount = 6 };

    var text = BroadcastFormatter.Format([(item.Participant, analysis)], 180);

    text.Should().Contain("高KDA6场");
    text.Should().NotContain("MVP");
}
```

- [ ] **Step 2: Run the two focused test classes and verify RED**

Run: `dotnet test tests/LolScout.Core.Tests/LolScout.Core.Tests.csproj --no-restore --filter BroadcastFormatterTests`

Run: `dotnet test tests/LolScout.App.Tests/LolScout.App.Tests.csproj --no-restore --filter MainWindowViewModelTests`

Expected: assertions fail because output still uses MVP.

- [ ] **Step 3: Implement card and formatter output**

In `PlayerCardViewModel.Update`, replace the MVP assignment with:

```csharp
Mvp = $"高KDA {analysis.HighKdaMatchCount}/{analysis.MatchCount}（≥5.0）";
```

In `BroadcastFormatter.PlayerCard`, replace the MVP visibility flag with `ShowHighKda`, initialize it to `true`, remove it in the same constrained-field priority position, and render:

```csharp
if (ShowHighKda)
    fields.Add($"高KDA{Analysis.HighKdaMatchCount}场");
```

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the two commands from Step 2.

Expected: both test classes pass and no output contains `MVP`.

- [ ] **Step 5: Commit**

```powershell
git add src/LolScout.App/ViewModels/PlayerCardViewModel.cs src/LolScout.Core/Analysis/BroadcastFormatter.cs tests/LolScout.App.Tests/MainWindowViewModelTests.cs tests/LolScout.Core.Tests/BroadcastFormatterTests.cs
git commit -m "feat: show high kda games instead of mvp"
```

### Task 3: Full Verification, Publish, and Launch

**Files:**
- Verify: `LolScout.sln`
- Produce: `artifacts/publish/win-x64/LolScout.App.exe`

**Interfaces:**
- Consumes: completed analysis and presentation changes from Tasks 1–2.
- Produces: tested single-file Windows executable and pushed branch commits.

- [ ] **Step 1: Run the full suite serially**

Run: `dotnet test LolScout.sln --no-restore -m:1 -p:UseSharedCompilation=false -nodeReuse:false`

Expected: all Core, Infrastructure, and App tests pass with zero failures.

- [ ] **Step 2: Run release publishing and security verification**

Run: `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1`

Expected: `Published, manifest-validated, and security-scanned` followed by the EXE path.

- [ ] **Step 3: Launch the new executable**

Stop only the existing `LolScout.App` process, then launch `artifacts/publish/win-x64/LolScout.App.exe`. Confirm the new process exists and the card displays high-KDA text after a successful query.

- [ ] **Step 4: Push the branch**

Run: `git push`

Expected: `feat/opponent-scout` updates successfully and the existing PR includes both feature commits.
