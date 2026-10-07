using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using TMSpeech.Core;
using TMSpeech.Core.Plugins;
using TMSpeech.Core.Services.Notification;

namespace TMSpeech.GUI.ViewModels;

/// <summary>把某个配置键变成可观察序列：先给当前值，之后每次配置变化时推送新值。</summary>
internal static class ConfigObservables
{
    public static IObservable<T> Get<T>(string key)
    {
        return Observable.Return(ConfigManagerFactory.Instance.Get<T>(key))
            .Merge(
                Observable.FromEventPattern<ConfigChangedEventArgs>(
                        p => ConfigManagerFactory.Instance.ConfigChanged += p,
                        p => ConfigManagerFactory.Instance.ConfigChanged -= p)
                    .Where(x => x.EventArgs.Contains(key))
                    .Select(x =>
                        ConfigManagerFactory.Instance.Get<T>(key)
                    ));
    }
}

public class CaptionStyleViewModel : ViewModelBase
{
    [ObservableAsProperty]
    public int ShadowSize { get; }

    [ObservableAsProperty]
    public Color ShadowColor { get; }

    [ObservableAsProperty]
    public int FontSize { get; }

    [ObservableAsProperty]
    public Color FontColor { get; }

    [ObservableAsProperty]
    public TextAlignment TextAlign { get; }

    [ObservableAsProperty]
    public FontFamily FontFamily { get; }

    [ObservableAsProperty]
    public Color MouseHover { get; }

    [ObservableAsProperty]
    public Color BackgroundColor { get; }

    [ObservableAsProperty]
    public string Text { get; }

    private IObservable<T> GetPropObservable<T>(string key) => ConfigObservables.Get<T>(key);

    public CaptionStyleViewModel(MainViewModel mainViewModel)
    {
        GetPropObservable<int>(AppearanceConfigTypes.ShadowSize)
            .ToPropertyEx(this, x => x.ShadowSize);
        GetPropObservable<uint>(AppearanceConfigTypes.ShadowColor)
            .Select(Color.FromUInt32)
            .ToPropertyEx(this, x => x.ShadowColor);
        GetPropObservable<uint>(AppearanceConfigTypes.BackgroundColor)
            .Select(Color.FromUInt32)
            .ToPropertyEx(this, x => x.BackgroundColor);
        GetPropObservable<int>(AppearanceConfigTypes.FontSize)
            .Select(x => { return x; })
            .ToPropertyEx(this, x => x.FontSize);
        GetPropObservable<uint>(AppearanceConfigTypes.FontColor)
            .Select(Color.FromUInt32)
            .ToPropertyEx(this, x => x.FontColor);
        GetPropObservable<int>(AppearanceConfigTypes.TextAlign)
            .Select(x => x switch
            {
                AppearanceConfigTypes.TextAlignEnum.Left => TextAlignment.Left,
                AppearanceConfigTypes.TextAlignEnum.Center => TextAlignment.Center,
                AppearanceConfigTypes.TextAlignEnum.Right => TextAlignment.Right,
                AppearanceConfigTypes.TextAlignEnum.Justify => TextAlignment.Right,
                _ => TextAlignment.Left
            })
            .ToPropertyEx(this, x => x.TextAlign);

        GetPropObservable<string>(AppearanceConfigTypes.FontFamily)
            .Select(x => new FontFamily(x))
            .ToPropertyEx(this, x => x.FontFamily);

        GetPropObservable<uint>(AppearanceConfigTypes.MouseHover)
            .Select(Color.FromUInt32)
            .CombineLatest(mainViewModel.WhenAnyValue(x => x.IsLocked),
                (color, locked) => locked ? Colors.Transparent : color)
            .ToPropertyEx(this, x => x.MouseHover);
    }
}

public class MainViewModel : ViewModelBase
{
    [ObservableAsProperty]
    public JobStatus Status { get; }

    [ObservableAsProperty]
    public bool PlayButtonVisible { get; }

    [ObservableAsProperty]
    public bool PauseButtonVisible { get; }

    [ObservableAsProperty]
    public bool StopButtonVisible { get; }

    [ObservableAsProperty]
    public bool HistroyPanelVisible { get; }

    [ObservableAsProperty]
    public long RunningSeconds { get; }

    [ObservableAsProperty]
    public string RunningTimeDisplay { get; }

    public CaptionStyleViewModel CaptionStyle { get; }

    [ObservableAsProperty]
    public string Text { get; }

    [Reactive]
    public bool IsLocked { get; set; }

    // ---- 锁定后悬浮控制条 ----

    /// <summary>锁定且用户开启了悬浮控制条时为 true。</summary>
    [ObservableAsProperty]
    public bool LockBarVisible { get; }

    [ObservableAsProperty]
    public bool LockBarUnlockVisible { get; }

    [ObservableAsProperty]
    public bool LockBarPlayVisible { get; }

    [ObservableAsProperty]
    public bool LockBarStopVisible { get; }

    [ObservableAsProperty]
    public bool LockBarRestartVisible { get; }

    [ObservableAsProperty]
    public bool LockBarExitVisible { get; }

    public ObservableCollection<TextInfo> HistoryTexts { get; } = new();

    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit> PauseCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> RestartCommand { get; }
    public ReactiveCommand<Unit, Unit> LockCommand { get; }
    public ReactiveCommand<Unit, Unit> UnlockCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    private readonly JobManager _jobManager;

    public MainViewModel()
    {
        _jobManager = JobManagerFactory.Instance;
        CaptionStyle = new CaptionStyleViewModel(this);

        Observable.FromEventPattern<JobStatus>(
                p => { _jobManager.StatusChanged += p; },
                p => { _jobManager.StatusChanged -= p; }
            )
            .Select(x => x.EventArgs)
            .Merge(Observable.Return(_jobManager.Status))
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.Status);

        this.WhenAnyValue(x => x.Status) // IObservable<JobStatus>
            .Select(x => x == JobStatus.Stopped || x == JobStatus.Paused) // IObservable<bool>
            .ToPropertyEx(this, x => x.PlayButtonVisible);

        this.WhenAnyValue(x => x.Status)
            .Select(x => x == JobStatus.Running)
            .ToPropertyEx(this, x => x.PauseButtonVisible);

        this.WhenAnyValue(x => x.Status)
            .Select(x => x == JobStatus.Running || x == JobStatus.Paused)
            .ToPropertyEx(this, x => x.StopButtonVisible);

        this.LockCommand = ReactiveCommand.Create(LockCaption);

        this.UnlockCommand = ReactiveCommand.Create(() => { IsLocked = false; });

        this.ExitCommand = ReactiveCommand.Create(() => { (App.Current as App)?.ExitApplication(); });

        // 锁定后悬浮控制条：总开关 + 各按钮可见性（设置里可配置）
        this.WhenAnyValue(x => x.IsLocked)
            .CombineLatest(ConfigObservables.Get<bool>(LockConfigTypes.ShowControlBar),
                (locked, show) => locked && show)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.LockBarVisible);

        ConfigObservables.Get<bool>(LockConfigTypes.ShowUnlock)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.LockBarUnlockVisible);

        ConfigObservables.Get<bool>(LockConfigTypes.ShowPlayStop)
            .CombineLatest(this.WhenAnyValue(x => x.PlayButtonVisible), (show, play) => show && play)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.LockBarPlayVisible);

        ConfigObservables.Get<bool>(LockConfigTypes.ShowPlayStop)
            .CombineLatest(this.WhenAnyValue(x => x.StopButtonVisible), (show, stop) => show && stop)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.LockBarStopVisible);

        ConfigObservables.Get<bool>(LockConfigTypes.ShowRestart)
            .CombineLatest(this.WhenAnyValue(x => x.StopButtonVisible), (show, running) => show && running)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.LockBarRestartVisible);

        ConfigObservables.Get<bool>(LockConfigTypes.ShowExit)
            .ObserveOn(RxApp.MainThreadScheduler)
            .ToPropertyEx(this, x => x.LockBarExitVisible);

        this.PlayCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                // 插件在后台加载，若用户在加载完成前点击启动则先等待
                await App.PluginsLoadTask;
                await Task.Run(() => { _jobManager.Start(); });
            },
            this.WhenAnyValue(x => x.PlayButtonVisible));
        this.PauseCommand = ReactiveCommand.CreateFromTask(
            async () => { await Task.Run(() => { _jobManager.Pause(); }); },
            this.WhenAnyValue(x => x.PauseButtonVisible));
        this.StopCommand = ReactiveCommand.CreateFromTask(
            async () => { await Task.Run(() => { _jobManager.Stop(); }); },
            this.WhenAnyValue(x => x.StopButtonVisible));
        this.RestartCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await App.PluginsLoadTask;
                await Task.Run(() =>
                {
                    _jobManager.Stop();
                    _jobManager.Start();
                });
            },
            this.WhenAnyValue(x => x.StopButtonVisible));

        // Subscribe to exceptions with proper UI notifications
        this.PlayCommand.ThrownExceptions.Subscribe(async ex =>
        {
            await MessageBoxManager.GetMessageBoxStandard(
                "启动失败",
                $"无法启动语音识别。\n\n错误详情：{ex.Message}\n\n请检查音频设备和识别器配置。",
                ButtonEnum.Ok,
                Icon.Error
            ).ShowAsync();
        });

        this.PauseCommand.ThrownExceptions.Subscribe(async ex =>
        {
            await MessageBoxManager.GetMessageBoxStandard(
                "暂停失败",
                $"无法暂停语音识别。\n\n错误详情：{ex.Message}",
                ButtonEnum.Ok,
                Icon.Warning
            ).ShowAsync();
        });

        this.StopCommand.ThrownExceptions.Subscribe(async ex =>
        {
            await MessageBoxManager.GetMessageBoxStandard(
                "停止失败",
                $"无法停止语音识别。\n\n错误详情：{ex.Message}",
                ButtonEnum.Ok,
                Icon.Warning
            ).ShowAsync();
        });

        this.RestartCommand.ThrownExceptions.Subscribe(async ex =>
        {
            await MessageBoxManager.GetMessageBoxStandard(
                "重启失败",
                $"无法重启语音识别。\n\n错误详情：{ex.Message}",
                ButtonEnum.Ok,
                Icon.Warning
            ).ShowAsync();
        });

        Observable.FromEventPattern<long>(x => _jobManager.RunningSecondsChanged += x,
                x => _jobManager.RunningSecondsChanged -= x)
            .Select(x => x.EventArgs)
            .ToPropertyEx(this, x => x.RunningSeconds);

        this.WhenAnyValue(x => x.RunningSeconds)
            .Select(x => string.Format("{0:D2}:{1:D2}:{2:D2}", x / 60 / 60, (x / 60) % 60, x % 60))
            .ToPropertyEx(this, x => x.RunningTimeDisplay);

        // Keep only valid text in the caption stream (no error messages)
        Observable.FromEventPattern<SpeechEventArgs>(
                p => _jobManager.TextChanged += p,
                p => _jobManager.TextChanged -= p)
            .Select(x => x.EventArgs.Text.Text)
            .Merge(Observable.Return("欢迎使用TMSpeech"))
            .ToPropertyEx(this, x => x.Text);

        Observable.FromEventPattern<SpeechEventArgs>(
                p => _jobManager.SentenceDone += p,
                p => _jobManager.SentenceDone -= p)
            .Select(x => x.EventArgs.Text)
            .Subscribe(x => { this.HistoryTexts.Add(x); });

        // Status 已切换到 UI 线程；只在进入 Running 时锁定，尊重运行中的手动解锁。
        this.WhenAnyValue(x => x.Status)
            .DistinctUntilChanged()
            .Skip(1)
            .Where(status => status == JobStatus.Running)
            .Subscribe(_ =>
            {
                // 快速停止或启动回退后，不处理尚未送达 UI 的 Running 状态。
                if (_jobManager.Status == JobStatus.Running &&
                    ConfigManagerFactory.Instance.Get<bool>(LockConfigTypes.AutoLockOnStart))
                {
                    LockCaption();
                }
            });
    }

    private void LockCaption()
    {
        if (IsLocked) return;
        IsLocked = true;

        var config = ConfigManagerFactory.Instance;
        if (config.Get<bool>(NotificationConfigTypes.HasShownLockUsage)) return;

        config.Apply(NotificationConfigTypes.HasShownLockUsage, true);
        var hasUnlockButton = config.Get<bool>(LockConfigTypes.ShowControlBar) &&
                              config.Get<bool>(LockConfigTypes.ShowUnlock);
        var unlockHint = hasUnlockButton
            ? "点击悬浮工具栏的解锁按钮，或右键托盘图标以解锁"
            : "右键托盘图标以解锁";
        NotificationManager.Instance.Notify(unlockHint, "锁定成功", NotificationType.Info);
    }
}
