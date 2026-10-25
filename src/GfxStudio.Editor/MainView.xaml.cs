using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GfxStudio.Core.Parsing;
using SkiaSharp;
using System;
using System.ComponentModel;
using System.IO;

namespace GfxStudio.Editor;

public partial class MainView : Window
{
    private static readonly IBrush CommentBrush = Brush.Parse("#6A9955");
    private static readonly IBrush StringBrush = Brush.Parse("#CE9178");
    private static readonly IBrush NumberBrush = Brush.Parse("#FFD866");
    private static readonly IBrush BooleanBrush = Brush.Parse("#569CD6");
    private static readonly IBrush KeyBrush = Brush.Parse("#9CDCFE");
    private static readonly IBrush FunctionBrush = Brush.Parse("#DCDCAA");
    private static readonly IBrush KeywordBrush = Brush.Parse("#C586C0");
    private static readonly IBrush PunctuationBrush = Brush.Parse("#D4D4D4");
    private readonly MainViewModel _viewModel;
    private readonly Dictionary<string, string> _samplePaths = new(StringComparer.OrdinalIgnoreCase);
    private Bitmap? _currentPreviewBitmap;
    private Bitmap? _checkerboardBitmap;
    private bool _syncingScroll;
    private DispatcherTimer? _playTimer;
    private const string FallbackGfx =
        @"layers = {
  background = { type = fill color = rgb(20, 30, 40) }
  clouds = { type = fractal_clouds mix = screen color = color_ramp(NAMED_COLORS.RED, NAMED_COLORS.BLUE) }
}

output = {
  dimensions = { width = 64, height = 64 }
  filename = ""preview.png""
}";

    public MainViewModel ViewModel => _viewModel;

    public MainView()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        PopulateSamples();
        WireScrollSync();
        WireFrameNavigation();
        WireDiagnostics();
        WireSeedTextBox();
        SampleComboBox.SelectionChanged += OnSampleSelected;
        RunButton.Click += (s, e) => _viewModel.RunNow();

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _checkerboardBitmap = CreateCheckerboardBitmap();
        CheckerboardImage.Source = _checkerboardBitmap;
        Closed += OnClosed;

        LoadSample();
    }

    private void WireScrollSync()
    {
        EditorScrollViewer.ScrollChanged += (s, e) => SyncScroll(EditorScrollViewer, GutterScrollViewer);
        GutterScrollViewer.ScrollChanged += (s, e) => SyncScroll(GutterScrollViewer, EditorScrollViewer);
        EditorTextBox.TextChanged += (s, e) =>
        {
            UpdateGutter();
            UpdateSyntaxHighlighting();
        };
    }

    private void WireFrameNavigation()
    {
        NewButton.Click += OnNewClicked;
        OpenButton.Click += OnOpenClicked;
        SaveButton.Click += OnSaveClicked;
        SaveAsButton.Click += OnSaveAsClicked;
        FirstButton.Click += (s, e) => _viewModel.FirstFrame();
        PrevButton.Click += (s, e) => _viewModel.PreviousFrame();
        NextButton.Click += (s, e) => _viewModel.NextFrame();
        LastButton.Click += (s, e) => _viewModel.LastFrame();
        PlayButton.Click += (s, e) => TogglePlay();
    }

    private void WireDiagnostics()
    {
        DiagnosticsList.SelectionChanged += (s, e) => OnDiagnosticSelected();
    }

    private void WireSeedTextBox()
    {
        SeedTextBox.TextChanged += (s, e) => _viewModel.SeedOverride = SeedTextBox.Text ?? string.Empty;
    }

    private void SyncScroll(ScrollViewer source, ScrollViewer target)
    {
        if (_syncingScroll || source is null || target is null) return;
        _syncingScroll = true;
        target.Offset = new Point(0, source.Offset.Y);
        _syncingScroll = false;
    }

    private void OnNewClicked(object? sender, RoutedEventArgs e)
    {
        StopPlay();
        SampleComboBox.SelectedItem = null;
        EditorTextBox.Text = string.Empty;
        _viewModel.SourceFilePath = string.Empty;
        TitleText.Text = "";
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selected = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open .gfx",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Gfx files") { Patterns = new[] { "*.gfx" } },
                    FilePickerFileTypes.All
                }
            });

            if (selected.Count > 0)
            {
                var path = selected[0].TryGetLocalPath();
                if (path is null)
                {
                    await ShowMessageAsync("The selected file does not have a local path.", "Open Error");
                    return;
                }

                try
                {
                    EditorTextBox.Text = File.ReadAllText(path);
                    SampleComboBox.SelectedItem = null;
                    _viewModel.SourceFilePath = path;
                    TitleText.Text = Path.GetFileName(path);
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync($"Could not open file:\n{ex.Message}", "Open Error");
                }
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Open failed:\n{ex.Message}", "Open Error");
        }
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        var path = _viewModel.SourceFilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            await SaveAsAsync();
            return;
        }

        try
        {
            File.WriteAllText(path, EditorTextBox.Text ?? string.Empty);
            TitleText.Text = Path.GetFileName(path);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Could not save file:\n{ex.Message}", "Save Error");
        }
    }

    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e) => await SaveAsAsync();

    private async Task SaveAsAsync()
    {
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save .gfx",
                DefaultExtension = "gfx",
                SuggestedFileName = Path.GetFileName(_viewModel.SourceFilePath) is { Length: > 0 } name ? name : "untitled.gfx",
                FileTypeChoices = new[] { new FilePickerFileType("Gfx files") { Patterns = new[] { "*.gfx" } } }
            });

            if (file is not null)
            {
                var path = file.TryGetLocalPath();
                if (path is null)
                {
                    await ShowMessageAsync("The selected file does not have a local path.", "Save Error");
                    return;
                }

                try
                {
                    File.WriteAllText(path, EditorTextBox.Text ?? string.Empty);
                    _viewModel.SourceFilePath = path;
                    TitleText.Text = Path.GetFileName(path);
                }
                catch (Exception ex)
                {
                    await ShowMessageAsync($"Could not save file:\n{ex.Message}", "Save Error");
                }
            }
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Save As failed:\n{ex.Message}", "Save Error");
        }
    }

    private void OnDiagnosticSelected()
    {
        if (DiagnosticsList.SelectedItem is DiagnosticRow row)
        {
            JumpToEditorAt(row.Line, row.Column);
        }
    }

    private void JumpToEditorAt(int line, int column)
    {
        var offset = TextLineToCaretIndex(EditorTextBox.Text ?? string.Empty, line, column);
        EditorTextBox.CaretIndex = offset;
        EditorTextBox.Focus();
        EditorTextBox.CaretIndex = offset;
    }

    private static int TextLineToCaretIndex(string text, int line, int column)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var lines = text.Split('\n');
        int offset = 0;
        for (int i = 0; i < line - 1 && i < lines.Length; i++)
        {
            offset += lines[i].Length + 1;
        }
        if (line - 1 >= 0 && line - 1 < lines.Length)
        {
            offset += Math.Clamp(column - 1, 0, lines[line - 1].Length);
        }
        return Math.Clamp(offset, 0, text.Length);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.PreviewData) or nameof(MainViewModel.SelectedFrameIndex))
        {
            UpdatePreviewImage();
        }
    }

    private void UpdatePreviewImage()
    {
        var frames = _viewModel.PreviewData?.Frames;
        var index = _viewModel.SelectedFrameIndex;

        _currentPreviewBitmap?.Dispose();
        _currentPreviewBitmap = null;
        PreviewImage.Source = null;

        if (frames is { } fs && index >= 0 && index < fs.Count)
        {
            using var stream = new MemoryStream(fs[index].PngData);
            _currentPreviewBitmap = new Bitmap(stream);
            PreviewImage.Source = _currentPreviewBitmap;
        }

        FrameIndicator.Text = _viewModel.IsAnimation
            ? $"Frame {Math.Max(0, index + 1)} / {Math.Max(1, _viewModel.FrameCount)}"
            : string.Empty;

        var hasFrames = _viewModel.FrameCount > 0;
        PlayButton.IsVisible = _viewModel.IsAnimation;
        FirstButton.IsEnabled = _viewModel.IsAnimation && hasFrames;
        PrevButton.IsEnabled = _viewModel.IsAnimation && hasFrames;
        NextButton.IsEnabled = _viewModel.IsAnimation && hasFrames;
        LastButton.IsEnabled = _viewModel.IsAnimation && hasFrames;
    }

    private void TogglePlay()
    {
        if (_playTimer is not null)
        {
            _playTimer.Stop();
            _playTimer = null;
            PlayButton.Content = "Play";
            return;
        }

        if (!_viewModel.IsAnimation || _viewModel.FrameCount <= 0) return;

        var fps = _viewModel.PreviewData?.Config?.Fps ?? 15.0;
        var intervalMs = Math.Max(1, (int)Math.Round(1000.0 / Math.Max(1.0, fps)));
        _playTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };

        _playTimer.Tick += (s, e) =>
        {
            if (!_viewModel.IsAnimation || _viewModel.FrameCount <= 0)
            {
                StopPlay();
                return;
            }

            var next = (_viewModel.SelectedFrameIndex + 1) % _viewModel.FrameCount;
            _viewModel.JumpToFrame(next);
        };

        _playTimer.Start();
        PlayButton.Content = "Pause";
    }

    private void StopPlay()
    {
        if (_playTimer is { })
        {
            _playTimer.Stop();
            _playTimer = null;
        }
        PlayButton.Content = "Play";
    }

    private void UpdateGutter()
    {
        var lines = (EditorTextBox.Text ?? string.Empty).Split('\n');
        GutterPanel.Children.Clear();

        for (int i = 0; i < lines.Length; i++)
        {
            GutterPanel.Children.Add(new TextBlock
            {
                Text = (i + 1).ToString("0000"),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6A, 0x6A, 0x6A)),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness(0),
                Padding = new Thickness(0)
            });
        }

        if (GutterScrollViewer is not null && EditorScrollViewer is not null)
        {
            GutterScrollViewer.Offset = new Point(0, EditorScrollViewer.Offset.Y);
        }
    }

    private void UpdateSyntaxHighlighting()
    {
        var source = EditorTextBox.Text ?? string.Empty;
        var tokens = new GfxLexer(source).Tokenize();
        var inlines = new InlineCollection();

        var offset = 0;
        for (var tokenIndex = 0; tokenIndex < tokens.Count; tokenIndex++)
        {
            var token = tokens[tokenIndex];
            if (token.Kind == GfxTokenKind.EndOfFile) break;

            var start = Math.Clamp(token.Span.Start.Offset, offset, source.Length);
            if (start > offset)
            {
                AppendGap(source, offset, start, inlines);
            }

            var end = Math.Clamp(token.Span.End.Offset, start, source.Length);
            if (end > start)
            {
                var foreground = GetTokenBrush(tokens, tokenIndex);
                inlines.Add(new Run
                {
                    Text = source[start..end],
                    Foreground = foreground
                });
            }

            offset = end;
        }

        if (offset < source.Length)
        {
            AppendGap(source, offset, source.Length, inlines);
        }

        SyntaxHighlightText.Inlines = inlines;
    }

    private static void AppendGap(string source, int start, int end, InlineCollection inlines)
    {
        var plainStart = start;
        var index = start;

        while (index < end)
        {
            var lineComment = source[index] == '#' ||
                source[index] == '/' && index + 1 < end && source[index + 1] == '/';
            var blockComment = source[index] == '/' && index + 1 < end && source[index + 1] == '*';
            if (!lineComment && !blockComment)
            {
                index++;
                continue;
            }

            AddPlainRun(source, plainStart, index, inlines);
            var commentStart = index;

            if (lineComment)
            {
                while (index < end && source[index] is not '\r' and not '\n') index++;
            }
            else
            {
                index += 2;
                while (index < end && !(source[index - 1] == '*' && source[index] == '/')) index++;
                if (index < end) index++;
            }

            inlines.Add(new Run
            {
                Text = source[commentStart..index],
                Foreground = CommentBrush
            });
            plainStart = index;
        }

        AddPlainRun(source, plainStart, end, inlines);
    }

    private static void AddPlainRun(string source, int start, int end, InlineCollection inlines)
    {
        if (end > start)
        {
            inlines.Add(new Run { Text = source[start..end] });
        }
    }

    private static IBrush? GetTokenBrush(IReadOnlyList<GfxToken> tokens, int index)
    {
        var token = tokens[index];
        return token.Kind switch
        {
            GfxTokenKind.NumberLiteral => NumberBrush,
            GfxTokenKind.StringLiteral or GfxTokenKind.InterpolatedStringStart or
                GfxTokenKind.InterpolatedStringText or GfxTokenKind.InterpolatedStringEnd => StringBrush,
            GfxTokenKind.True or GfxTokenKind.False => BooleanBrush,
            GfxTokenKind.Identifier when IsKeyword(token.Lexeme) => KeywordBrush,
            GfxTokenKind.Identifier when NextTokenIs(tokens, index, GfxTokenKind.Equals, GfxTokenKind.Colon) => KeyBrush,
            GfxTokenKind.Identifier when NextTokenIs(tokens, index, GfxTokenKind.LeftParen) => FunctionBrush,
            GfxTokenKind.LeftBrace or GfxTokenKind.RightBrace or GfxTokenKind.LeftBracket or
                GfxTokenKind.RightBracket or GfxTokenKind.LeftParen or GfxTokenKind.RightParen or
                GfxTokenKind.Equals or GfxTokenKind.Colon or GfxTokenKind.Comma or GfxTokenKind.Dot or
                GfxTokenKind.Plus or GfxTokenKind.Minus or GfxTokenKind.Star or GfxTokenKind.Slash or
                GfxTokenKind.Percent or GfxTokenKind.InterpolationStart or GfxTokenKind.InterpolationEnd => PunctuationBrush,
            _ => null
        };
    }

    private static bool IsKeyword(string identifier) => identifier is "layers" or "output" or "scripts";

    private static bool NextTokenIs(IReadOnlyList<GfxToken> tokens, int index, params GfxTokenKind[] kinds) =>
        index + 1 < tokens.Count && kinds.Contains(tokens[index + 1].Kind);

    private Bitmap CreateCheckerboardBitmap()
    {
        const int size = 16;
        using var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                var light = ((x + y) & 1) == 0;
                bmp.SetPixel(x, y, light
                    ? new SKColor(222, 222, 222, 255)
                    : new SKColor(196, 196, 196, 255));
            }
        }

        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        StopPlay();
        PreviewImage.Source = null;
        CheckerboardImage.Source = null;
        _currentPreviewBitmap?.Dispose();
        _checkerboardBitmap?.Dispose();
        _currentPreviewBitmap = null;
        _checkerboardBitmap = null;
    }

    private async Task ShowMessageAsync(string message, string title)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(18),
                Spacing = 14,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    new Button
                    {
                        Content = "OK",
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        MinWidth = 72,
                        IsDefault = true
                    }
                }
            }
        };

        if (dialog.Content is StackPanel panel && panel.Children[1] is Button closeButton)
        {
            closeButton.Click += (_, _) => dialog.Close();
        }

        await dialog.ShowDialog(this);
    }

    private void LoadSample()
    {
        const string initialSample = "number.gfx";
        if (_samplePaths.TryGetValue(initialSample, out var samplePath))
        {
            LoadSampleScript(samplePath);
            SampleComboBox.SelectedItem = initialSample;
            return;
        }

        EditorTextBox.Text = FallbackGfx;
        _viewModel.SourceFilePath = string.Empty;
        TitleText.Text = "Untitled";
    }

    private void PopulateSamples()
    {
        var directories = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "samples"),
            Path.Combine(Directory.GetCurrentDirectory(), "samples")
        };

        var sampleDirectory = directories.FirstOrDefault(Directory.Exists);
        if (sampleDirectory is null) return;

        var files = Directory.GetFiles(sampleDirectory, "*.gfx")
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var path in files)
        {
            _samplePaths[Path.GetFileName(path)] = path;
        }

        SampleComboBox.ItemsSource = _samplePaths.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private void OnSampleSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (SampleComboBox.SelectedItem is string selectedName &&
            _samplePaths.TryGetValue(selectedName, out var path) &&
            !string.Equals(_viewModel.SourceFilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            LoadSampleScript(path);
        }
    }

    private void LoadSampleScript(string path)
    {
        EditorTextBox.Text = File.ReadAllText(path);
        _viewModel.SourceFilePath = path;
        TitleText.Text = Path.GetFileName(path);
    }
}
