using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// Classic/Enunu/DiffSinger/NEUTRINO 歌手安装向导的 ViewModel。
/// 四步流程：Step 0 压缩包编码 → Step 1 文本编码 → Step 2 歌手类型 → Step 3 安装摘要
/// </summary>
public class ClassicSingerSetupViewModel : NavigateViewModelBase, ICmdSubscriber, IDisposable
{
    private readonly CompositeDisposable _subscriptions = new();
    [Reactive] public int Step { get; set; }
    public string StepText => string.Format(L.S("SingerSetup.StepFormat"), Step + 1);

    [Reactive] public string ArchiveFilePath { get; set; } = string.Empty;

    public Encoding[] Encodings { get; set; } =
    [
        Encoding.GetEncoding("shift_jis"),
        Encoding.UTF8,
        Encoding.GetEncoding("gb2312"),
        Encoding.GetEncoding("big5"),
        Encoding.GetEncoding("ks_c_5601-1987"),
        Encoding.GetEncoding("Windows-1252"),
        Encoding.GetEncoding("macintosh")
    ];

    [Reactive] public Encoding ArchiveEncoding { get; set; }
    [Reactive] public Encoding TextEncoding { get; set; }
    [Reactive] public bool MissingInfo { get; set; }

    public string[] SingerTypes { get; set; } = ["utau", "enunu", "diffsinger", "neutrino", "vocaloid"];
    [Reactive] public string SingerType { get; set; }

    public ObservableCollection<string> TextItems => _textItems;
    private readonly ObservableCollectionExtended<string> _textItems;

    // 安装进度相关
    [Reactive] public bool IsInstalling { get; set; }
    [Reactive] public double InstallProgress { get; set; }
    [Reactive] public string InstallMessage { get; set; } = string.Empty;
    [Reactive] public bool InstallSuccess { get; set; }
    [Reactive] public string ErrorMessage { get; set; } = string.Empty;
    [Reactive] public bool ShowErrorState { get; set; }
    [Reactive] public bool ShowSuccessState { get; set; }
    [Reactive] public bool ShowProgressState { get; set; }

    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmInstallCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitCommand { get; }

    public ClassicSingerSetupViewModel(MainViewModel navigator) : base(navigator)
    {
        SingerType = SingerTypes[0];
        ArchiveEncoding = Encodings[0];
        TextEncoding = Encodings[0];

        _textItems = [];

        // 初始化命令
        // NextCommand is disabled while an error is shown on step 0 (encrypted, V6 AI, etc.)
        var canGoNext = this.WhenAnyValue(
            vm => vm.ShowErrorState,
            vm => vm.IsInstalling,
            (err, installing) => !err && !installing);
        NextCommand = ReactiveCommand.Create(OnNext, canGoNext);
        BackCommand = ReactiveCommand.Create(OnBack);
        ConfirmInstallCommand = ReactiveCommand.Create(OnConfirmInstall);
        ExitCommand = ReactiveCommand.Create(OnExit);

        // 订阅 ArchiveFilePath 变化
        this.WhenAnyValue(vm => vm.Step)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(StepText))).DisposeWith(_subscriptions);

        this.WhenAnyValue(vm => vm.ArchiveFilePath)
            .Subscribe(_ =>
            {
                if (!string.IsNullOrEmpty(ArchiveFilePath))
                {
                    ShowErrorState = false;
                    ErrorMessage = string.Empty;
                    try
                    {
                        if (IsEncrypted(ArchiveFilePath))
                        {
                            ShowErrorState = true;
                            ErrorMessage = L.S("SingerSetup.Error.Encrypted");
                            return;
                        }

                        if (IsVocaloidAIBank(ArchiveFilePath))
                        {
                            ShowErrorState = true;
                            ErrorMessage = L.S("SingerSetup.Error.VocaloidAI");
                            return;
                        }

                        VoicebankConfig? config = LoadCharacterYaml(ArchiveFilePath);
                        MissingInfo = config == null || string.IsNullOrEmpty(config.SingerType);

                        SingerType = DetermineSingerType(config, ArchiveFilePath);

                        if (!string.IsNullOrEmpty(config?.TextFileEncoding))
                        {
                            try
                            {
                                TextEncoding = Encoding.GetEncoding(config.TextFileEncoding);
                            }
                            catch
                            {
                                // 如果编码无效，保持默认
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowErrorState = true;
                        ErrorMessage = ex.Message;
                    }
                }
            }).DisposeWith(_subscriptions);

        // 订阅 Step、ArchiveEncoding、ArchiveFilePath 的变化以刷新存档列表
        this.WhenAnyValue(vm => vm.Step, vm => vm.ArchiveEncoding, vm => vm.ArchiveFilePath)
            .Subscribe(_ => RefreshArchiveItems()).DisposeWith(_subscriptions);

        // 订阅 Step、TextEncoding 的变化以刷新文本列表
        this.WhenAnyValue(vm => vm.Step, vm => vm.TextEncoding)
            .Subscribe(_ => RefreshTextItems()).DisposeWith(_subscriptions);

        // 注册为 DocManager 订阅者以接收进度通知
        DocManager.Inst.AddSubscriber(this);
    }

    private void OnNext()
    {
        if (Step < 3)
        {
            Step++;
        }
    }

    private void OnBack()
    {
        if (Step > 0)
        {
            Step--;
        }
        else
        {
            Navigator.NavigateBack(this);
        }
    }

    private async void OnConfirmInstall()
    {
        await InstallAsync();
    }

    private void OnExit()
    {
        Navigator.NavigateBack(this);
    }

    private void RefreshArchiveItems()
    {
        if (Step != 0)
        {
            return;
        }

        if (string.IsNullOrEmpty(ArchiveFilePath))
        {
            _textItems.Clear();
            return;
        }

        try
        {
            ReaderOptions readerOptions = new ReaderOptions
            {
                ArchiveEncoding = new ArchiveEncoding { Forced = ArchiveEncoding },
            };
            using (IArchive archive = ArchiveFactory.OpenArchive(ArchiveFilePath, readerOptions))
            {
                _textItems.Clear();
                _textItems.AddRange(archive.Entries
                    .Select(entry => entry.Key!)
                    .ToArray());
            }
        }
        catch (Exception ex)
        {
            _textItems.Clear();
            _textItems.Add($"[错误] {ex.Message}");
        }
    }

    private void RefreshTextItems()
    {
        if (Step != 1)
        {
            return;
        }

        if (string.IsNullOrEmpty(ArchiveFilePath))
        {
            _textItems.Clear();
            return;
        }

        try
        {
            ReaderOptions readerOptions = new ReaderOptions
            {
                ArchiveEncoding = new ArchiveEncoding { Forced = ArchiveEncoding },
            };
            using (IArchive archive = ArchiveFactory.OpenArchive(ArchiveFilePath, readerOptions))
            {
                _textItems.Clear();
                foreach (IArchiveEntry entry in archive.Entries.Where(entry =>
                             entry.Key!.EndsWith("character.txt", StringComparison.OrdinalIgnoreCase) ||
                             entry.Key.EndsWith("oto.ini", StringComparison.OrdinalIgnoreCase)))
                {
                    using (Stream stream = entry.OpenEntryStream())
                    {
                        using StreamReader reader = new StreamReader(stream, TextEncoding);
                        _textItems.Add($"------ {entry.Key} ------");
                        int count = 0;
                        while (count < 256 && !reader.EndOfStream)
                        {
                            string? line = reader.ReadLine();
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                _textItems.Add(line);
                                count++;
                            }
                        }

                        if (!reader.EndOfStream)
                        {
                            _textItems.Add("...");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _textItems.Clear();
            _textItems.Add($"[错误] {ex.Message}");
        }
    }

    private bool IsEncrypted(string archiveFilePath)
    {
        try
        {
            using (IArchive archive = ArchiveFactory.OpenArchive(archiveFilePath))
            {
                return archive.Entries.Any(e => e.IsEncrypted);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true when the archive looks like a VOCALOID6 AI bank: it carries a
    /// package-config.json that declares vocaloidVersion 6, but has no .ddb or .ddi
    /// (native V6 AI banks use a neural format with no classic binary assets).
    /// Classic V3/V4/V5 banks re-used by V6 have .ddb/.ddi and load fine; they are not flagged.
    /// </summary>
    private static bool IsVocaloidAIBank(string archiveFilePath)
    {
        try
        {
            using IArchive archive = ArchiveFactory.OpenArchive(archiveFilePath);
            var names = archive.Entries
                .Where(e => !e.IsDirectory && e.Key != null)
                .Select(e => Path.GetFileName(e.Key!.Replace('\\', '/')))
                .ToList();

            bool hasClassicAssets = names.Any(n =>
                n.EndsWith(".ddb", StringComparison.OrdinalIgnoreCase) ||
                n.EndsWith(".ddi", StringComparison.OrdinalIgnoreCase));
            if (hasClassicAssets) return false;

            // No .ddb/.ddi — only flag if the manifest or folder name screams V6 AI.
            // Signs: package-config.json with vocaloidVersion == 6, or a .vpr alongside
            // an "ai" marker file (some redistributors include one).
            var configEntry = archive.Entries.FirstOrDefault(e =>
                e.Key != null &&
                Path.GetFileName(e.Key.Replace('\\', '/')).Equals("package-config.json", StringComparison.OrdinalIgnoreCase));
            if (configEntry != null)
            {
                using var stream = configEntry.OpenEntryStream();
                using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
                string json = reader.ReadToEnd();
                // Quick text scan — no full JSON parse needed here.
                // Matches: "vocaloidVersion": 6  |  "vocaloidVersion": "6"  |  "VOCALOID6"
                if (System.Text.RegularExpressions.Regex.IsMatch(json,
                        @"""vocaloidVersion""\s*:\s*(6|""[Vv]?6"")",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            // A .vpr with no .ddb/.ddi and no classic config is a plain VOCALOID project,
            // not a voicebank archive — not our problem here, let the existing flow handle it.
            return false;
        }
        catch
        {
            return false;
        }
    }

    private VoicebankConfig? LoadCharacterYaml(string archiveFilePath)
    {
        try
        {
            using (IArchive archive = ArchiveFactory.OpenArchive(archiveFilePath))
            {
                IArchiveEntry? entry = archive.Entries.FirstOrDefault(e =>
                    Path.GetFileName(e.Key) == "character.yaml");
                if (entry == null)
                {
                    return null;
                }

                using (Stream stream = entry.OpenEntryStream())
                {
                    return VoicebankConfig.Load(stream);
                }
            }
        }
        catch
        {
            return null;
        }
    }

    private string DetermineSingerType(VoicebankConfig? config, string archiveFilePath)
    {
        if (!string.IsNullOrEmpty(config?.SingerType))
        {
            string? known = SingerTypes.FirstOrDefault(type =>
                string.Equals(type, config.SingerType, StringComparison.OrdinalIgnoreCase));
            if (known != null)
            {
                return known;
            }
            Log.Warning("Unknown SingerType {SingerType} in {ArchivePath}; using UTAU.",
                config.SingerType, archiveFilePath);
            return SingerTypes[0];
        }
        return DetectSingerTypeFromArchive(archiveFilePath);
    }

    private string DetectSingerTypeFromArchive(string archiveFilePath)
    {
        try
        {
            using IArchive archive = ArchiveFactory.OpenArchive(archiveFilePath);
            string?[] names = archive.Entries.Where(entry => !entry.IsDirectory)
                .Select(entry => Path.GetFileName(entry.Key?.Replace('\\', '/'))).ToArray();
            // 与 Core 保持一致：Enunu 优先于 DiffSinger。
            if (names.Contains(VoicebankLoader.kEnuconfigYaml))
            {
                return "enunu";
            }
            if (names.Contains(VoicebankLoader.kDsconfigYaml))
            {
                return "diffsinger";
            }
            if (names.Contains("info.toml"))
            {
                return "neutrino";
            }
            if (names.Any(name => name != null && (
                    name.EndsWith(".ddb", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".ddi", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".vcld", StringComparison.OrdinalIgnoreCase))))
            {
                return "vocaloid";
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to detect singer type from {ArchivePath}; using UTAU.", archiveFilePath);
        }
        return "utau";
    }

    private async Task InstallAsync()
    {
        string archiveFilePath = ArchiveFilePath;
        Encoding archiveEncoding = ArchiveEncoding;
        Encoding textEncoding = TextEncoding;
        string singerType = SingerType;

        // 进入安装状态
        IsInstalling = true;
        ShowProgressState = true;
        InstallProgress = 0;
        InstallMessage = L.S("SingerSetup.Installing");
        ShowErrorState = false;
        ShowSuccessState = false;

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    string basePath = PathManager.Inst.SingersInstallPath;
                    if (singerType == "vocaloid")
                    {
                        OpenUtau.Core.Vocaloid.VocaloidInstaller.Install(archiveFilePath, basePath,
                            (progress, info) => DocManager.Inst.ExecuteCmd(new ProgressBarNotification(progress, info)),
                            archiveEncoding);
                    }
                    else
                    {
                        VoicebankInstaller installer = new VoicebankInstaller(basePath, (progress, info) =>
                        {
                            // 这会发送 ProgressBarNotification，我们在 OnNext 中处理
                            DocManager.Inst.ExecuteCmd(new ProgressBarNotification(progress, info));
                        }, archiveEncoding, textEncoding);
                        installer.Install(archiveFilePath, singerType);
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception(string.Format(L.S("SingerSetup.InstallFailed"), ex.Message), ex);
                }
            });

            await ServiceHub.FlushFileSystemAsync();
            // 安装成功
            IsInstalling = false;
            ShowProgressState = false;
            ShowSuccessState = true;
            InstallSuccess = true;
            InstallMessage = L.S("SingerSetup.InstallComplete");

            // 刷新歌手列表
            DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
        }
        catch (Exception ex)
        {
            // 安装失败
            IsInstalling = false;
            ShowProgressState = false;
            ShowErrorState = true;
            ShowSuccessState = false;
            InstallSuccess = false;
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// ICmdSubscriber 实现 - 订阅 ProgressBarNotification
    /// </summary>
    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (cmd is ProgressBarNotification progressNotif)
        {
            // 更新进度条和消息
            InstallProgress = progressNotif.Progress;
            InstallMessage = progressNotif.Info;
        }
    }

    public void Dispose()
    {
        DocManager.Inst.RemoveSubscriber(this);
        _subscriptions.Dispose();
        NextCommand.Dispose(); BackCommand.Dispose(); ConfirmInstallCommand.Dispose(); ExitCommand.Dispose();
    }
    public override void OnNavigatedTo()
    {
        // 初始化时清空错误状态
        ShowErrorState = false;
        ErrorMessage = string.Empty;
    }
}