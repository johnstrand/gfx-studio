using Avalonia.Threading;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using GfxStudio.Core.Diagnostics;

namespace GfxStudio.Editor;

/// <summary>
/// ViewModel for the editor main window. Owns the source text, seeds the render, and exposes
/// the resolved output config, rendered preview frames, and diagnostics.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly PreviewRenderer _renderer = new();
    private DispatcherTimer? _debounceTimer;
    private PreviewData? _previewData;
    private int _selectedFrameIndex;
    private int _renderRevision;
    private string _statusText = string.Empty;
    private bool _runOnChange = true;

    public MainViewModel() { }

    public string Source
    {
        get => _source;
        set
        {
            _source = value;
            OnPropertyChanged();
            RequestRenderForChange();
        }
    }
    private string _source = string.Empty;

    public string SourceFilePath { get => _sourceFilePath; set { _sourceFilePath = value; OnPropertyChanged(); } }
    private string _sourceFilePath = string.Empty;

    public string SeedOverride
    {
        get => _seedOverride;
        set
        {
            _seedOverride = value;
            OnPropertyChanged();
            RequestRenderForChange();
        }
    }
    private string _seedOverride = string.Empty;

    public bool RunOnChange
    {
        get => _runOnChange;
        set
        {
            if (_runOnChange == value) return;
            _runOnChange = value;
            OnPropertyChanged();

            if (value)
            {
                RaiseRenderRequested();
            }
            else
            {
                Interlocked.Increment(ref _renderRevision);
                _debounceTimer?.Stop();
                _debounceTimer = null;
            }
        }
    }

    public PreviewData? PreviewData
    {
        get => _previewData;
        private set
        {
            _previewData = value;
            OnPropertyChanged(nameof(PreviewData));
        }
    }

    public int SelectedFrameIndex
    {
        get => _selectedFrameIndex;
        private set
        {
            _selectedFrameIndex = value;
            OnPropertyChanged(nameof(SelectedFrameIndex));
            OnPropertyChanged(nameof(FrameCount));
        }
    }

    public int FrameCount => PreviewData?.Frames.Count ?? 0;

    public bool IsAnimation => PreviewData?.Config is { } config &&
        (config.Mode is GfxStudio.Core.Output.OutputMode.AnimationFrames or GfxStudio.Core.Output.OutputMode.AnimationGif ||
         config.Mode == GfxStudio.Core.Output.OutputMode.ImageMap && config.UseFrames);

    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public ObservableCollection<DiagnosticRow> Diagnostics { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    // -------------------------------------------------------------------
    // Rendering
    // -------------------------------------------------------------------

    private void RaiseRenderRequested()
    {
        Interlocked.Increment(ref _renderRevision);

        if (_debounceTimer is null)
        {
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _debounceTimer.Tick += OnDebounceTick;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void RequestRenderForChange()
    {
        if (RunOnChange)
        {
            RaiseRenderRequested();
        }
        else
        {
            Interlocked.Increment(ref _renderRevision);
            StatusText = "Changes pending — click Run to update the preview.";
        }
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        if (_debounceTimer is null) return;
        _debounceTimer.Stop();
        _debounceTimer = null;

        RunRender();
    }

    private void RunRender()
    {
        var source = Source;
        var filePath = SourceFilePath;
        var seedText = SeedOverride;
        var renderRevision = Volatile.Read(ref _renderRevision);

        ulong? seed = null;
        if (ulong.TryParse(seedText.Trim(), out var parsedSeed))
        {
            seed = parsedSeed;
        }
        else if (!string.IsNullOrWhiteSpace(seedText))
        {
            seed = 0UL; // force a zero seed when the override is non-empty but unparseable
        }

        StatusText = "Rendering...";

        Task.Run(() =>
        {
            PreviewResult result;
            try
            {
                result = _renderer.RenderPreview(source, filePath, seed);
            }
            catch (Exception ex)
            {
                result = new PreviewResult(
                    null,
                    Array.Empty<PreviewFrame>(),
                    Array.Empty<GfxDiagnostic>(),
                    $"Unhandled error during render: {ex.Message}");
            }

            var data = BuildPreviewData(result);
            Dispatcher.UIThread.Post(() =>
            {
                if (renderRevision == Volatile.Read(ref _renderRevision))
                {
                    OnPreviewDataUpdated(data);
                }
            });
        });
    }

    public void RunNow()
    {
        _debounceTimer?.Stop();
        _debounceTimer = null;
        Interlocked.Increment(ref _renderRevision);
        RunRender();
    }

    private PreviewData BuildPreviewData(PreviewResult result)
    {
        return new PreviewData(result.Config, result.Frames, result.Diagnostics, result.Error);
    }

    private void OnPreviewDataUpdated(PreviewData? data)
    {
        PreviewData = data;

        if (data?.Frames is { } frames)
        {
            SelectedFrameIndex = frames.Count > 0 ? 0 : -1;
        }
        else
        {
            SelectedFrameIndex = -1;
        }

        RebuildDiagnostics(data?.Diagnostics ?? Array.Empty<GfxDiagnostic>());
        UpdateStatus();
    }

    private void RebuildDiagnostics(IEnumerable<GfxDiagnostic> diagnostics)
    {
        Diagnostics.Clear();
        foreach (var d in diagnostics.Where(x => x.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error))
        {
            Diagnostics.Add(new DiagnosticRow(
                d.Severity,
                d.Code,
                d.Message,
                Math.Max(1, d.Span.Start.Line),
                Math.Max(1, d.Span.Start.Column),
                d.Span));
        }
    }

    private void UpdateStatus()
    {
        var cfg = PreviewData?.Config;
        var framesCount = PreviewData?.Frames.Count ?? 0;

        if (cfg is { })
        {
            var frameSuffix = IsAnimation ? $" | {framesCount} preview frame(s)" : string.Empty;
            StatusText = $"Mode: {cfg.Mode.ToString().ToLowerInvariant()} | {cfg.Width}x{cfg.Height} | count: {cfg.Count} | fps: {cfg.Fps:0.##} | seed: {cfg.Seed:0}{frameSuffix}";
        }
        else if (PreviewData?.Error is { } err)
        {
            StatusText = $">> {err}";
        }
        else
        {
            StatusText = "No valid .gfx output configuration.";
        }

        OnPropertyChanged(nameof(StatusText));
    }

    // -------------------------------------------------------------------
    // Frame navigation
    // -------------------------------------------------------------------

    private void SetSelectedFrame(int frameIndex)
    {
        if (PreviewData?.Frames is { } frames && frameIndex >= 0 && frameIndex < frames.Count)
        {
            _selectedFrameIndex = frameIndex;
            OnPropertyChanged(nameof(SelectedFrameIndex));
            OnPropertyChanged(nameof(FrameCount));
        }
    }

    public void PreviousFrame() => SetSelectedFrame(Math.Max(0, _selectedFrameIndex - 1));

    public void NextFrame() => SetSelectedFrame(_selectedFrameIndex + 1);

    public void FirstFrame() => SetSelectedFrame(0);

    public void LastFrame()
    {
        var count = PreviewData?.Frames.Count ?? 0;
        SetSelectedFrame(count > 0 ? count - 1 : -1);
    }

    public void JumpToFrame(int frameIndex) => SetSelectedFrame(frameIndex);

    // -------------------------------------------------------------------
    // File operations
    // -------------------------------------------------------------------

    public void LoadSource(string text, string filePath)
    {
        Source = text;
        SourceFilePath = filePath;
    }

    public string ToFileContent() => Source;

    // -------------------------------------------------------------------
    // INotifyPropertyChanged
    // -------------------------------------------------------------------

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
