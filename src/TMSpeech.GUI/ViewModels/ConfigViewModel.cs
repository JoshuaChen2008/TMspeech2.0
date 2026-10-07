using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Media;
using ReactiveUI;
using TMSpeech.Core;
using TMSpeech.Core.Plugins;

namespace TMSpeech.GUI.ViewModels
{
    class ConfigJsonValueAttribute : Attribute
    {
        public string Key { get; }

        public ConfigJsonValueAttribute(string key)
        {
            Key = key;
        }

        public ConfigJsonValueAttribute()
        {
        }
    }


    public abstract class SectionConfigViewModelBase : ViewModelBase
    {
        protected virtual string SectionName => "";

        private string PropertyToKey(PropertyInfo prop)
        {
            var key = prop.GetCustomAttributes(typeof(ConfigJsonValueAttribute), false)
                .Select(u => u as ConfigJsonValueAttribute)
                .FirstOrDefault()?.Key;

            if (key != null) return key;
            return $"{SectionName}.{prop.Name}";
        }

        public virtual Dictionary<string, object> Serialize()
        {
            var ret = new Dictionary<string, object>();
            this.GetType().GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(ConfigJsonValueAttribute), false).Length > 0)
                .ToList()
                .ForEach(p =>
                {
                    var value = p.GetValue(this);
                    ret[PropertyToKey(p)] = value;
                });
            return ret;
        }

        public static object? ChangeType(object? value, Type type)
        {
            // 特殊处理List<int>类型
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = type.GetGenericArguments()[0];
                if (elementType == typeof(int))
                {
                    // 处理 List<int>
                    if (value is System.Text.Json.JsonElement jsonElement)
                    {
                        var intList = new List<int>();
                        foreach (var item in jsonElement.EnumerateArray())
                        {
                            intList.Add(item.GetInt32());
                        }
                        return intList;
                    }
                    else
                    {
                        throw new InvalidCastException($"Expected JsonElement for List<int> property!");
                    }
                }
            }
            return Convert.ChangeType(value, type);
        }

        public virtual void Deserialize(IReadOnlyDictionary<string, object> dict)
        {
            this.GetType().GetProperties()
                .Where(p => p.GetCustomAttributes(typeof(ConfigJsonValueAttribute), false).Length > 0)
                .ToList()
                .ForEach(p =>
                {
                    if (!dict.ContainsKey(PropertyToKey(p))) return;
                    var value = dict[PropertyToKey(p)];
                    var type = p.PropertyType;

                    try
                    {
                        p.SetValue(this, ChangeType(value, type));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"属性 {p.Name} 转换失败: {ex.Message}");
                    }
                });
        }

        public void Load()
        {
            var dict = ConfigManagerFactory.Instance.GetAll();
            Deserialize(
                dict.Where(x => ConfigManager.IsInSection(x.Key, SectionName))
                    .ToDictionary(x => x.Key, x => x.Value)
            );
        }

        public void Apply()
        {
            var dict = Serialize();
            ConfigManagerFactory.Instance.BatchApply(dict.Where(u => u.Value != null)
                .ToDictionary(x => x.Key, x => x.Value));
        }

        public SectionConfigViewModelBase()
        {
            Load();
            this.PropertyChanged += (sender, args) =>
            {
                var propName = args.PropertyName;
                var type = sender.GetType();

                if (sender.GetType().GetProperty(propName)
                    .GetCustomAttributes(false)
                    .Any(u => u.GetType() == typeof(ConfigJsonValueAttribute)))
                {
                    Apply();
                }
            };
        }
    }

    public class ConfigViewModel : ViewModelBase
    {
        public GeneralSectionConfigViewModel GeneralSectionConfig { get; } = new GeneralSectionConfigViewModel();

        public AppearanceSectionConfigViewModel AppearanceSectionConfig { get; } =
            new AppearanceSectionConfigViewModel();

        public AudioSectionConfigViewModel AudioSectionConfig { get; } = new AudioSectionConfigViewModel();
        public RecognizeSectionConfigViewModel RecognizeSectionConfig { get; } = new RecognizeSectionConfigViewModel();
        public NotificationConfigViewModel NotificationConfig { get; } = new NotificationConfigViewModel();
        public LockSectionConfigViewModel LockSectionConfig { get; } = new LockSectionConfigViewModel();

        [ObservableAsProperty]
        public bool IsNotRunning { get; }

        // 页签索引：0 通用 / 1 通知 / 2 显示与字幕 / 3 音频源 / 4 语音识别 / 5 资源管理 / 6 关于
        [Reactive]
        public int CurrentTab { get; set; } = 2;

        public ConfigViewModel()
        {
            Observable.Return(JobManagerFactory.Instance.Status != JobStatus.Running).Merge(
                Observable.FromEventPattern<JobStatus>(
                    x => JobManagerFactory.Instance.StatusChanged += x,
                    x => JobManagerFactory.Instance.StatusChanged -= x
                ).Select(x => x.EventArgs != JobStatus.Running)
            ).ToPropertyEx(this, x => x.IsNotRunning);
        }
    }

    public class GeneralSectionConfigViewModel : SectionConfigViewModelBase
    {
        protected override string SectionName => GeneralConfigTypes.SectionName;

        [Reactive]
        [ConfigJsonValue]
        public string Language { get; set; }

        public ObservableCollection<KeyValuePair<string, string>> LanguagesAvailable { get; } =
        [
            new KeyValuePair<string, string>("zh-cn", "简体中文"),
            new KeyValuePair<string, string>("en-us", "English"),
        ];

        //[Reactive]
        //[ConfigJsonValue]
        //public string UserDir { get; set; } = "D:\\TMSpeech";

        [Reactive]
        [ConfigJsonValue]
        public string ResultLogPath { get; set; }

        /// <summary>界面主题：system / light / dark，见 GeneralConfigTypes.ThemeEnum。</summary>
        [Reactive]
        [ConfigJsonValue]
        public string Theme { get; set; } = GeneralConfigTypes.ThemeEnum.System;

        [Reactive]
        [ConfigJsonValue]
        public bool LaunchOnStartup { get; set; }

        [Reactive]
        [ConfigJsonValue]
        public bool StartOnLaunch { get; set; }

        [Reactive]
        [ConfigJsonValue]
        public bool AutoUpdate { get; set; }

        // Left, Top, Width, Height
        [Reactive]
        [ConfigJsonValue]
        public List<int> MainWindowLocation { get; set; } = [];
    }

    /// <summary>字幕自动锁定与锁定后悬浮控制条的设置。</summary>
    public class LockSectionConfigViewModel : SectionConfigViewModelBase
    {
        protected override string SectionName => LockConfigTypes.SectionName;

        [Reactive]
        [ConfigJsonValue]
        public bool AutoLockOnStart { get; set; }

        [Reactive]
        [ConfigJsonValue]
        public bool ShowControlBar { get; set; } = true;

        [Reactive]
        [ConfigJsonValue]
        public bool ShowUnlock { get; set; } = true;

        [Reactive]
        [ConfigJsonValue]
        public bool ShowPlayStop { get; set; } = true;

        [Reactive]
        [ConfigJsonValue]
        public bool ShowRestart { get; set; } = true;

        [Reactive]
        [ConfigJsonValue]
        public bool ShowExit { get; set; }
    }

    public class AppearanceSectionConfigViewModel : SectionConfigViewModelBase
    {
        protected override string SectionName => AppearanceConfigTypes.SectionName;

        public List<FontFamily> FontsAvailable { get; private set; }

        [Reactive]
        [ConfigJsonValue]
        public uint ShadowColor { get; set; }


        [Reactive]
        [ConfigJsonValue]
        public int ShadowSize { get; set; }


        [Reactive]
        [ConfigJsonValue]
        public string FontFamily { get; set; }

        [Reactive]
        [ConfigJsonValue]
        public int FontSize { get; set; }

        [Reactive]
        [ConfigJsonValue]
        public uint FontColor { get; set; }

        [Reactive]
        [ConfigJsonValue]
        public uint MouseHover { get; set; }

        [Reactive]
        [ConfigJsonValue]
        public int TextAlign { get; set; }

        [Reactive]
        [ConfigJsonValue(AppearanceConfigTypes.BackgroundColor)]
        public uint BackgroundColor { get; set; }

        public List<KeyValuePair<int, string>> TextAligns { get; } =
        [
            new KeyValuePair<int, string>(AppearanceConfigTypes.TextAlignEnum.Left, "左对齐"),
            new KeyValuePair<int, string>(AppearanceConfigTypes.TextAlignEnum.Center, "居中对齐"),
            new KeyValuePair<int, string>(AppearanceConfigTypes.TextAlignEnum.Right, "右对齐"),
            new KeyValuePair<int, string>(AppearanceConfigTypes.TextAlignEnum.Justify, "两端对齐"),
        ];

        /// <summary>恢复本节全部设置为默认值。</summary>
        public ReactiveCommand<Unit, Unit> ResetCommand { get; }

        public AppearanceSectionConfigViewModel()
        {
            FontsAvailable = FontManager.Current.SystemFonts.ToList();
            ResetCommand = ReactiveCommand.Create(() =>
            {
                var defaults = DefaultConfig.GenerateConfig()
                    .Where(x => ConfigManager.IsInSection(x.Key, SectionName))
                    .ToDictionary(x => x.Key, x => x.Value);
                ConfigManagerFactory.Instance.BatchApply(defaults);
                Load();
            });
        }
    }

    public class NotificationConfigViewModel : SectionConfigViewModelBase
    {
        protected override string SectionName => NotificationConfigTypes.SectionName;


        public List<KeyValuePair<int, string>> NotificaitonTypes { get; } =
        [
            new KeyValuePair<int, string>(NotificationConfigTypes.NotificationTypeEnum.None, "关闭通知"),
            new KeyValuePair<int, string>(NotificationConfigTypes.NotificationTypeEnum.System, "系统通知 (暂不支持 macOS)"),
            // new KeyValuePair<int, string>(NotificationTypeEnum.Custom, "TMSpeech 通知"),
        ];

        [Reactive]
        [ConfigJsonValue]
        public int NotificationType { get; set; } = NotificationConfigTypes.NotificationTypeEnum.System;

        [Reactive]
        [ConfigJsonValue]
        public string SensitiveWords { get; set; } = "";
    }

    public class AudioSectionConfigViewModel : SectionConfigViewModelBase
    {
        [Reactive]
        [ConfigJsonValue]
        public string AudioSource { get; set; }

        [ObservableAsProperty]
        public IReadOnlyDictionary<string, Core.Plugins.IAudioSource> AudioSourcesAvailable { get; }

        [ObservableAsProperty]
        public IPluginConfigEditor? ConfigEditor { get; }

        [Reactive]
        public string PluginConfig { get; set; } = "";

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        public IReadOnlyDictionary<string, Core.Plugins.IAudioSource> Refresh()
        {
            try { App.PluginsLoadTask.Wait(TimeSpan.FromSeconds(15)); } catch { }
            var plugins = Core.Plugins.PluginManagerFactory.GetInstance().AudioSources;
            if (AudioSource == "" && plugins.Count >= 1)
                AudioSource = plugins.First().Key;
            return plugins;
        }

        public override Dictionary<string, object> Serialize()
        {
            var ret = new Dictionary<string, object>
            {
                { "audio.source", AudioSource },
            };
            // 不在此处保存插件配置，由 PluginConfig 属性变化时单独触发保存
            // 避免 AudioSource 切换时，将旧配置保存到新音频源的配置键

            return ret;
        }

        public override void Deserialize(IReadOnlyDictionary<string, object> dict)
        {
            if (dict.ContainsKey(AudioSourceConfigTypes.AudioSource))
            {
                AudioSource = dict[AudioSourceConfigTypes.AudioSource]?.ToString() ?? "";
            }

            if (dict.ContainsKey(AudioSourceConfigTypes.GetPluginConfigKey(AudioSource)))
            {
                PluginConfig = dict[AudioSourceConfigTypes.GetPluginConfigKey(AudioSource)]?.ToString() ?? "";
            }
        }

        public AudioSectionConfigViewModel()
        {
            this.RefreshCommand = ReactiveCommand.Create(() => { });
            // 插件后台加载完成后自动补一次刷新，避免窗口开得太早时列表为空
            var pluginsReady = Observable.FromAsync(async () =>
            {
                try { await App.PluginsLoadTask; } catch { }
            });
            this.RefreshCommand.Merge(Observable.Return(Unit.Default)).Merge(pluginsReady)
                .SelectMany(u => Observable.FromAsync(() => Task.Run(() => Refresh())))
                .ToPropertyEx(this, x => x.AudioSourcesAvailable);

            this.WhenAnyValue(u => u.AudioSource, u => u.AudioSourcesAvailable)
                .Where((u) => u.Item1 != null && u.Item2 != null)
                .Select(u => u.Item1)
                .Where(x => !string.IsNullOrEmpty(x))
                .DistinctUntilChanged()
                .Select(x => AudioSourcesAvailable.FirstOrDefault(u => u.Key == x))
                .Select(x =>
                {
                    var plugin = x.Value;
                    var editor = plugin?.CreateConfigEditor();
                    var config = ConfigManagerFactory.Instance.Get<string>(
                        AudioSourceConfigTypes.GetPluginConfigKey(AudioSource));
                    editor?.LoadConfigString(config);
                    return editor;
                })
                .ToPropertyEx(this, x => x.ConfigEditor);


            this.WhenAnyValue(x => x.ConfigEditor)
                .Subscribe(x =>
                {
                    var config =
                        ConfigManagerFactory.Instance.Get<string>(
                            AudioSourceConfigTypes.GetPluginConfigKey(AudioSource));
                    PluginConfig = config;
                });

            // 监听 PluginConfig 变化，手动保存到正确的配置键
            this.WhenAnyValue(x => x.PluginConfig)
                .Skip(1) // 跳过初始值
                .Subscribe(config =>
                {
                    if (!string.IsNullOrEmpty(AudioSource))
                    {
                        ConfigManagerFactory.Instance.Apply(
                            AudioSourceConfigTypes.GetPluginConfigKey(AudioSource),
                            config
                        );
                    }
                });
        }
    }


    public class RecognizeSectionConfigViewModel : SectionConfigViewModelBase
    {
        protected override string SectionName => "";

        [Reactive]
        [ConfigJsonValue]
        public string Recognizer { get; set; } = "";

        [ObservableAsProperty]
        public IReadOnlyDictionary<string, Core.Plugins.IRecognizer> RecognizersAvailable { get; }

        [ObservableAsProperty]
        public IPluginConfigEditor? ConfigEditor { get; }

        [Reactive]
        public string PluginConfig { get; set; } = "";

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        public IReadOnlyDictionary<string, Core.Plugins.IRecognizer> Refresh()
        {
            try { App.PluginsLoadTask.Wait(TimeSpan.FromSeconds(15)); } catch { }
            var plugins = Core.Plugins.PluginManagerFactory.GetInstance().Recognizers;
            if (Recognizer == "" && plugins.Count >= 1)
                Recognizer = plugins.First().Key;
            return plugins;
        }

        public override Dictionary<string, object> Serialize()
        {
            var ret = new Dictionary<string, object>
            {
                { RecognizerConfigTypes.Recognizer, Recognizer },
            };
            // 不在此处保存插件配置，由 PluginConfig 属性变化时单独触发保存
            // 避免 Recognizer 切换时，将旧配置保存到新识别器的配置键

            return ret;
        }

        public override void Deserialize(IReadOnlyDictionary<string, object> dict)
        {
            if (dict.ContainsKey(RecognizerConfigTypes.Recognizer))
            {
                Recognizer = dict[RecognizerConfigTypes.Recognizer]?.ToString() ?? "";
            }

            if (dict.ContainsKey(RecognizerConfigTypes.GetPluginConfigKey(Recognizer)))
            {
                PluginConfig = dict[RecognizerConfigTypes.GetPluginConfigKey(Recognizer)]?.ToString() ?? "";
            }
        }

        public RecognizeSectionConfigViewModel()
        {
            this.RefreshCommand = ReactiveCommand.Create(() => { });
            // 插件后台加载完成后自动补一次刷新，避免窗口开得太早时列表为空
            var pluginsReady = Observable.FromAsync(async () =>
            {
                try { await App.PluginsLoadTask; } catch { }
            });
            this.RefreshCommand.Merge(Observable.Return(Unit.Default)).Merge(pluginsReady)
                .SelectMany(u => Observable.FromAsync(() => Task.Run(() => Refresh())))
                .ToPropertyEx(this, x => x.RecognizersAvailable);

            this.WhenAnyValue(u => u.Recognizer, u => u.RecognizersAvailable)
                .Where((u) => u.Item1 != null && u.Item2 != null)
                .Select(u => u.Item1)
                .Where(x => !string.IsNullOrEmpty(x))
                .DistinctUntilChanged()
                .Select(x => RecognizersAvailable.FirstOrDefault(u => u.Key == x))
                .Select(x =>
                {
                    var plugin = x.Value;
                    var editor = plugin?.CreateConfigEditor();
                    var config = ConfigManagerFactory.Instance.Get<string>(
                        RecognizerConfigTypes.GetPluginConfigKey(Recognizer));
                    editor?.LoadConfigString(config);
                    return editor;
                })
                .ToPropertyEx(this, x => x.ConfigEditor);

            this.WhenAnyValue(x => x.ConfigEditor)
                .Subscribe(x =>
                {
                    var config = ConfigManagerFactory.Instance.Get<string>(
                        RecognizerConfigTypes.GetPluginConfigKey(Recognizer));
                    PluginConfig = config;
                });

            // 监听 PluginConfig 变化，手动保存到正确的配置键
            this.WhenAnyValue(x => x.PluginConfig)
                .Skip(1) // 跳过初始值
                .Subscribe(config =>
                {
                    if (!string.IsNullOrEmpty(Recognizer))
                    {
                        ConfigManagerFactory.Instance.Apply(
                            RecognizerConfigTypes.GetPluginConfigKey(Recognizer),
                            config
                        );
                    }
                });
        }
    }
}
