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

    [Fact]
    public void Constructor_failure_disposes_menu_even_when_icon_disposal_throws()
    {
        var factory = new ConfigureFailureFactory();

        var act = () => new TrayIconService(() => { }, () => { }, () => Task.CompletedTask, factory);

        act.Should().Throw<InvalidOperationException>().WithMessage("configure failed");
        factory.Icon.DisposeCalls.Should().Be(1);
        factory.Menu.DisposeCount.Should().Be(1);
    }

    [Fact]
    public void Dispose_attempts_hide_icon_and_menu_even_when_each_throws()
    {
        var factory = new DisposalFailureFactory();
        using var service = new TrayIconService(() => { }, () => { }, () => Task.CompletedTask, factory);

        var act = service.Dispose;

        act.Should().Throw<AggregateException>();
        factory.Icon.HideCalls.Should().Be(1);
        factory.Icon.DisposeCalls.Should().Be(1);
        factory.Menu.DisposeCount.Should().Be(1);
    }

    [Fact]
    public async Task Exit_event_catches_failure_and_reports_it()
    {
        var factory = new CapturingFactory();
        Exception? observed = null;
        using var service = new TrayIconService(() => { }, () => { },
            () => Task.FromException(new InvalidOperationException("exit")), factory,
            error => { observed = error; return Task.CompletedTask; });

        await factory.Menu.Exit!();

        observed.Should().BeOfType<InvalidOperationException>();
    }

    private sealed class ThrowingFactory : ITrayPlatformFactory
    {
        public RecordingMenu Menu { get; } = new();
        public ITrayMenu CreateMenu() => Menu;
        public ITrayIcon CreateIcon() => throw new InvalidOperationException("icon failed");
    }

    private sealed class RecordingMenu(bool throwOnDispose = false) : ITrayMenu
    {
        public int DisposeCount { get; private set; }
        public object NativeMenu { get; } = new();
        public void Add(string text, Action action) { }
        public void AddAsync(string text, Func<Task> action) { }
        public void AddSeparator() { }
        public void Dispose() { DisposeCount++; if (throwOnDispose) throw new InvalidOperationException("menu dispose"); }
    }

    private sealed class DisposalFailureFactory : ITrayPlatformFactory
    {
        public RecordingMenu Menu { get; } = new(throwOnDispose: true);
        public ThrowingIcon Icon { get; } = new();
        public ITrayMenu CreateMenu() => Menu;
        public ITrayIcon CreateIcon() => Icon;
    }

    private sealed class ConfigureFailureFactory : ITrayPlatformFactory
    {
        public RecordingMenu Menu { get; } = new();
        public ConfigureFailureIcon Icon { get; } = new();
        public ITrayMenu CreateMenu() => Menu;
        public ITrayIcon CreateIcon() => Icon;
    }

    private sealed class ConfigureFailureIcon : ITrayIcon
    {
        public int DisposeCalls { get; private set; }
        public void Configure(object menu, Action show) => throw new InvalidOperationException("configure failed");
        public void Hide() { }
        public void Dispose() { DisposeCalls++; throw new InvalidOperationException("dispose failed"); }
    }

    private sealed class CapturingFactory : ITrayPlatformFactory
    {
        public CapturingMenu Menu { get; } = new();
        public ITrayMenu CreateMenu() => Menu;
        public ITrayIcon CreateIcon() => new PassiveIcon();
    }

    private sealed class CapturingMenu : ITrayMenu
    {
        public Func<Task>? Exit { get; private set; }
        public object NativeMenu { get; } = new();
        public void Add(string text, Action action) { }
        public void AddAsync(string text, Func<Task> action) => Exit = action;
        public void AddSeparator() { }
        public void Dispose() { }
    }

    private sealed class PassiveIcon : ITrayIcon
    {
        public void Configure(object menu, Action show) { }
        public void Hide() { }
        public void Dispose() { }
    }

    private sealed class ThrowingIcon : ITrayIcon
    {
        public int HideCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public void Configure(object menu, Action show) { }
        public void Hide() { HideCalls++; throw new InvalidOperationException("hide"); }
        public void Dispose() { DisposeCalls++; throw new InvalidOperationException("icon dispose"); }
    }
}
