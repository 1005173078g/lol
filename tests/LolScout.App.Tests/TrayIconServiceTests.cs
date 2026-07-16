using FluentAssertions;
using LolScout.App.Services;
using Xunit;

namespace LolScout.App.Tests;

public sealed class TrayIconServiceTests
{
    [Fact]
    public void Constructor_failure_disposes_menu_created_before_icon()
    {
        var factory = new ThrowingFactory();

        var act = () => new TrayIconService(() => { }, () => { }, () => Task.CompletedTask, factory);

        act.Should().Throw<InvalidOperationException>();
        factory.Menu.DisposeCount.Should().Be(1);
    }

    private sealed class ThrowingFactory : ITrayPlatformFactory
    {
        public RecordingMenu Menu { get; } = new();
        public ITrayMenu CreateMenu() => Menu;
        public ITrayIcon CreateIcon() => throw new InvalidOperationException("icon failed");
    }

    private sealed class RecordingMenu : ITrayMenu
    {
        public int DisposeCount { get; private set; }
        public object NativeMenu { get; } = new();
        public void Add(string text, Action action) { }
        public void AddAsync(string text, Func<Task> action) { }
        public void AddSeparator() { }
        public void Dispose() => DisposeCount++;
    }
}
