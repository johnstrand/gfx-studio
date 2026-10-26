using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GfxStudio.Core.Formatting;
using GfxStudio.Core.Parsing;
using GfxStudio.Core.Rendering;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
    private readonly LayerRendererRegistry _layerRegistry = LayerRendererRegistry.CreateDefault();
    private Bitmap? _currentPreviewBitmap;
    private Bitmap? _checkerboardBitmap;
    private bool _syncingScroll;
    private DispatcherTimer? _playTimer;

    private const string DefaultNewScript =
@"layers = {
}

variables = {
}

output = {
  dimensions = { width = 64, height = 64 }
  filename = ""image.png""
}";

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

        WireMenu();
        WireEditorQoL();
        PopulateSamples();
        WireScrollSync();
        WireFrameNavigation();
        WireDiagnostics();
        KeyDown += OnWindowKeyDown;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _checkerboardBitmap = CreateCheckerboardBitmap();
        CheckerboardImage.Source = _checkerboardBitmap;
        Closed += OnClosed;

        LoadSample();
        UpdateTitle();
    }

    private void WireMenu()
    {
        NewMenuItem.Click += OnNewClicked;
        OpenMenuItem.Click += OnOpenClicked;
        SaveMenuItem.Click += OnSaveClicked;
        SaveAsMenuItem.Click += OnSaveAsClicked;
        ExitMenuItem.Click += (s, e) => Close();
        RunMenuItem.Click += (s, e) => _viewModel.RunNow();
        RunOnChangeMenuItem.Click += (s, e) => _viewModel.RunOnChange = !_viewModel.RunOnChange;
        SetSeedMenuItem.Click += async (s, e) => await ShowSetSeedDialogAsync();
        QuickAddLayerMenuItem.Click += async (s, e) => await ShowQuickAddLayerDialogAsync();
        FormatDocumentMenuItem.Click += (s, e) => FormatDocument();
        QuickAddLayerButton.Click += async (s, e) => await ShowQuickAddLayerDialogAsync();
        FormatDocumentButton.Click += (s, e) => FormatDocument();
        UpdateRunOnChangeMenu();
    }

    private void WireEditorQoL()
    {
        EditorTextBox.KeyDown += OnEditorKeyDown;
        EditorTextBox.TextInput += OnEditorTextInput;
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        // Format Document: Shift+Alt+F or Ctrl+Shift+F
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
            (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            FormatDocument();
            e.Handled = true;
            return;
        }

        // Quick-Add Layer: Ctrl+Shift+L
        if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            _ = ShowQuickAddLayerDialogAsync();
            e.Handled = true;
            return;
        }

        // 1. Enter key: auto-indent & smart block expansion
        if (e.Key is Key.Enter or Key.Return)
        {
            var text = EditorTextBox.Text ?? string.Empty;
            var (newText, newCaret) = GfxEditorTextHelper.HandleEnter(
                text,
                EditorTextBox.CaretIndex,
                EditorTextBox.SelectionStart,
                EditorTextBox.SelectionEnd);

            EditorTextBox.Text = newText;
            EditorTextBox.CaretIndex = newCaret;
            e.Handled = true;
            return;
        }

        // 2. Backspace key: paired delimiter deletion
        if (e.Key == Key.Back)
        {
            var text = EditorTextBox.Text ?? string.Empty;
            var (handled, newText, newCaret) = GfxEditorTextHelper.HandleBackspace(
                text,
                EditorTextBox.CaretIndex,
                EditorTextBox.SelectionStart,
                EditorTextBox.SelectionEnd);

            if (handled)
            {
                EditorTextBox.Text = newText;
                EditorTextBox.CaretIndex = newCaret;
                e.Handled = true;
                return;
            }
        }

        // 3. Tab & Shift+Tab: indent / outdent
        if (e.Key == Key.Tab)
        {
            var text = EditorTextBox.Text ?? string.Empty;
            var isShift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            var (newText, newStart, newEnd) = GfxEditorTextHelper.HandleTab(
                text,
                EditorTextBox.SelectionStart,
                EditorTextBox.SelectionEnd,
                isShift);

            EditorTextBox.Text = newText;
            EditorTextBox.SelectionStart = newStart;
            EditorTextBox.SelectionEnd = newEnd;
            e.Handled = true;
            return;
        }

        // 4. Ctrl+/: toggle line comment
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && (e.Key is Key.Oem2 or Key.Divide or Key.OemQuestion))
        {
            var text = EditorTextBox.Text ?? string.Empty;
            var (newText, newStart, newEnd) = GfxEditorTextHelper.ToggleComment(
                text,
                EditorTextBox.SelectionStart,
                EditorTextBox.SelectionEnd);

            EditorTextBox.Text = newText;
            EditorTextBox.SelectionStart = newStart;
            EditorTextBox.SelectionEnd = newEnd;
            e.Handled = true;
            return;
        }
    }

    private void OnEditorTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;

        var ch = e.Text[0];
        var text = EditorTextBox.Text ?? string.Empty;
        var (handled, newText, newCaret, newSelStart, newSelEnd) = GfxEditorTextHelper.HandleTextInput(
            text,
            EditorTextBox.SelectionStart,
            EditorTextBox.SelectionEnd,
            ch);

        if (handled)
        {
            EditorTextBox.Text = newText;
            if (newSelStart != newSelEnd)
            {
                EditorTextBox.SelectionStart = newSelStart;
                EditorTextBox.SelectionEnd = newSelEnd;
            }
            else
            {
                EditorTextBox.CaretIndex = newCaret;
            }

            e.Handled = true;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            _viewModel.RunNow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Shift) &&
            (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            FormatDocument();
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.L && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _ = ShowQuickAddLayerDialogAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.N)
            {
                OnNewClicked(null, null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.O)
            {
                OnOpenClicked(null, null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    OnSaveAsClicked(null, null);
                }
                else
                {
                    OnSaveClicked(null, null);
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.R)
            {
                _viewModel.RunOnChange = !_viewModel.RunOnChange;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.E)
            {
                _ = ShowSetSeedDialogAsync();
                e.Handled = true;
                return;
            }
        }
    }

    private void FormatDocument()
    {
        var source = EditorTextBox.Text ?? string.Empty;
        var filePath = !string.IsNullOrWhiteSpace(_viewModel.SourceFilePath) ? _viewModel.SourceFilePath : "<editor>";
        var result = GfxFormatter.Format(source, filePath);

        if (result.HasErrors)
        {
            StatusBar.Text = "Cannot format document with syntax errors.";
            return;
        }

        if (result.Changed)
        {
            var (caretLine, caretCol) = CaretIndexToLineCol(source, EditorTextBox.CaretIndex);
            var scrollOffset = EditorScrollViewer.Offset;

            EditorTextBox.Text = result.FormattedText;
            EditorTextBox.CaretIndex = TextLineToCaretIndex(result.FormattedText, caretLine, caretCol);
            EditorScrollViewer.Offset = scrollOffset;
        }

        StatusBar.Text = "Document formatted.";
    }

    private static (int Line, int Column) CaretIndexToLineCol(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (1, 1);
        }

        var clamped = Math.Clamp(caretIndex, 0, text.Length);
        var line = 1;
        var col = 1;
        for (var i = 0; i < clamped; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                col = 1;
            }
            else if (text[i] != '\r')
            {
                col++;
            }
        }

        return (line, col);
    }

    private async Task ShowQuickAddLayerDialogAsync()
    {
        var allTypes = _layerRegistry.RegisteredLayerTypes
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        var defaultType = allTypes.Contains("fill") ? "fill" : allTypes.FirstOrDefault() ?? "fill";

        var nameBox = new TextBox
        {
            Text = SuggestUniqueLayerName(defaultType),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Watermark = "e.g. my_layer"
        };

        var validationText = new TextBlock
        {
            Foreground = Brush.Parse("#F48771"),
            FontSize = 11,
            IsVisible = false,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var typeCombo = new ComboBox
        {
            ItemsSource = allTypes,
            SelectedItem = defaultType,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13
        };

        var typeDescriptionText = new TextBlock
        {
            Foreground = Brush.Parse("#9A9A9A"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8)
        };

        var dynamicPropsStack = new StackPanel
        {
            Spacing = 8
        };

        var addButton = new Button
        {
            Content = "Add Layer",
            MinWidth = 96,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            IsDefault = true
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 80,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            IsCancel = true
        };

        var layerInputGetters = new List<(LayerPropertyDescriptor Descriptor, Func<string> GetValue)>();
        var commonInputGetters = new List<(string Key, Func<string> GetValue)>();
        var transformInputGetters = new List<(string Key, Func<string> GetValue)>();

        void ValidateForm()
        {
            var candidateName = nameBox.Text?.Trim() ?? string.Empty;
            if (!GfxEditorTextHelper.IsValidIdentifier(candidateName))
            {
                validationText.Text = "Layer name must be a valid identifier (letters, digits, '_', not starting with a digit).";
                validationText.IsVisible = true;
                addButton.IsEnabled = false;
                return;
            }

            foreach (var (desc, getter) in layerInputGetters)
            {
                if (desc.IsRequired && string.IsNullOrWhiteSpace(getter()))
                {
                    validationText.Text = $"Required property '{desc.Name}' must be specified.";
                    validationText.IsVisible = true;
                    addButton.IsEnabled = false;
                    return;
                }
            }

            validationText.IsVisible = false;
            addButton.IsEnabled = true;
        }

        Control CreatePropertyInput(LayerPropertyDescriptor desc, Action onChanged)
        {
            if (desc.AllowedValues is { Count: > 0 })
            {
                var options = new List<string> { string.Empty };
                options.AddRange(desc.AllowedValues);
                var combo = new ComboBox
                {
                    ItemsSource = options,
                    SelectedIndex = 0,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12,
                    PlaceholderText = desc.DefaultValue is not null ? $"default: {desc.DefaultValue}" : $"({desc.ValueType})"
                };
                ToolTip.SetTip(combo, desc.Description);
                combo.SelectionChanged += (_, _) => onChanged();
                layerInputGetters.Add((desc, () => combo.SelectedItem as string ?? string.Empty));
                return combo;
            }
            else
            {
                var tb = new TextBox
                {
                    Text = desc.IsRequired ? (desc.DefaultValue ?? GetSuggestedRequiredDefault(desc)) : string.Empty,
                    Watermark = desc.DefaultValue is not null
                        ? $"({desc.ValueType}) default: {desc.DefaultValue}"
                        : $"({desc.ValueType})",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12
                };
                ToolTip.SetTip(tb, desc.Description);
                tb.TextChanged += (_, _) => onChanged();
                layerInputGetters.Add((desc, () => tb.Text?.Trim() ?? string.Empty));
                return tb;
            }
        }

        void RebuildPropertiesPanel(string selectedType)
        {
            dynamicPropsStack.Children.Clear();
            layerInputGetters.Clear();
            commonInputGetters.Clear();
            transformInputGetters.Clear();

            if (!_layerRegistry.TryGet(selectedType, out var renderer) || renderer is null)
            {
                typeDescriptionText.Text = string.Empty;
                ValidateForm();
                return;
            }

            typeDescriptionText.Text = $"[{renderer.Category}] {renderer.Description}";

            var requiredProps = renderer.PropertyDescriptors.Where(p => p.IsRequired).ToList();
            var optionalProps = renderer.PropertyDescriptors.Where(p => !p.IsRequired).ToList();

            if (requiredProps.Count > 0)
            {
                dynamicPropsStack.Children.Add(CreateSectionHeader("Required Properties", "#FFD866"));
                foreach (var prop in requiredProps)
                {
                    dynamicPropsStack.Children.Add(CreateLabeledField(
                        $"{prop.Name} *",
                        prop.Description,
                        CreatePropertyInput(prop, ValidateForm)));
                }
            }

            if (optionalProps.Count > 0)
            {
                dynamicPropsStack.Children.Add(CreateSectionHeader("Optional Layer Properties", "#9CDCFE"));
                foreach (var prop in optionalProps)
                {
                    dynamicPropsStack.Children.Add(CreateLabeledField(
                        prop.Name,
                        prop.Description,
                        CreatePropertyInput(prop, ValidateForm)));
                }
            }

            // Common & Transform Properties section
            dynamicPropsStack.Children.Add(CreateSectionHeader("Common & Transform Properties (Optional)", "#C586C0"));

            var mixDescriptor = GfxCanvasCompositor.UniversalPropertyDescriptors.First(d => d.Name == "mix");
            var mixOptions = new List<string> { string.Empty };
            if (mixDescriptor.AllowedValues is not null)
            {
                mixOptions.AddRange(mixDescriptor.AllowedValues);
            }
            var mixCombo = new ComboBox
            {
                ItemsSource = mixOptions,
                SelectedIndex = 0,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                PlaceholderText = "default: normal"
            };
            commonInputGetters.Add(("mix", () => mixCombo.SelectedItem as string ?? string.Empty));
            dynamicPropsStack.Children.Add(CreateLabeledField("mix", mixDescriptor.Description, mixCombo));

            foreach (var commonName in new[] { "opacity", "size", "seed" })
            {
                var desc = GfxCanvasCompositor.UniversalPropertyDescriptors.First(d => d.Name == commonName);
                var tb = new TextBox
                {
                    Watermark = $"({desc.ValueType}) default: {desc.DefaultValue ?? "none"}",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12
                };
                var capturedName = commonName;
                commonInputGetters.Add((capturedName, () => tb.Text?.Trim() ?? string.Empty));
                dynamicPropsStack.Children.Add(CreateLabeledField(commonName, desc.Description, tb));
            }

            foreach (var transformName in new[] { "offset", "rotation", "scale" })
            {
                var desc = GfxCanvasCompositor.TransformPropertyDescriptors.First(d => d.Name == transformName);
                var tb = new TextBox
                {
                    Watermark = $"({desc.ValueType}) default: {desc.DefaultValue ?? "none"}",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 12
                };
                var capturedTransformName = transformName;
                transformInputGetters.Add((capturedTransformName, () => tb.Text?.Trim() ?? string.Empty));
                dynamicPropsStack.Children.Add(CreateLabeledField($"transform.{transformName}", desc.Description, tb));
            }

            ValidateForm();
        }

        var lastSuggestedName = nameBox.Text;
        nameBox.TextChanged += (_, _) => ValidateForm();
        typeCombo.SelectionChanged += (_, _) =>
        {
            if (typeCombo.SelectedItem is string newType)
            {
                if (string.Equals(nameBox.Text, lastSuggestedName, StringComparison.Ordinal))
                {
                    lastSuggestedName = SuggestUniqueLayerName(newType);
                    nameBox.Text = lastSuggestedName;
                }

                RebuildPropertiesPanel(newType);
            }
        };

        RebuildPropertiesPanel(defaultType);

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 380,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = dynamicPropsStack,
            Padding = new Thickness(0, 0, 8, 0)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancelButton, addButton }
        };

        var dialog = new Window
        {
            Title = "Quick-Add Layer",
            Width = 520,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#262626"),
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Layer Name",
                        FontWeight = FontWeight.Bold,
                        Foreground = Brush.Parse("#E8E8E8")
                    },
                    nameBox,
                    validationText,
                    new TextBlock
                    {
                        Text = "Layer Type",
                        FontWeight = FontWeight.Bold,
                        Foreground = Brush.Parse("#E8E8E8"),
                        Margin = new Thickness(0, 6, 0, 0)
                    },
                    typeCombo,
                    typeDescriptionText,
                    new Border
                    {
                        Background = Brush.Parse("#1E1E1E"),
                        BorderBrush = Brush.Parse("#3A3A3A"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(12),
                        Child = scrollViewer
                    },
                    buttonPanel
                }
            }
        };

        addButton.Click += (_, _) =>
        {
            ValidateForm();
            if (!addButton.IsEnabled)
            {
                return;
            }

            var layerName = nameBox.Text!.Trim();
            var selectedType = typeCombo.SelectedItem as string ?? defaultType;

            var props = new List<(string Key, string Value)>();
            foreach (var (desc, getter) in layerInputGetters)
            {
                var val = getter();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    props.Add((desc.Name, val));
                }
            }

            foreach (var (key, getter) in commonInputGetters)
            {
                var val = getter();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    props.Add((key, val));
                }
            }

            var transforms = new List<(string Key, string Value)>();
            foreach (var (key, getter) in transformInputGetters)
            {
                var val = getter();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    transforms.Add((key, val));
                }
            }

            var (newText, newCaret) = GfxEditorTextHelper.InsertLayerBlock(
                EditorTextBox.Text,
                layerName,
                selectedType,
                props,
                transforms);

            EditorTextBox.Text = newText;
            EditorTextBox.CaretIndex = newCaret;
            EditorTextBox.Focus();
            _viewModel.RunNow();
            dialog.Close();
        };

        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
    }

    private string SuggestUniqueLayerName(string layerType)
    {
        var baseName = GfxEditorTextHelper.IsValidIdentifier(layerType) ? layerType : "layer";
        var source = EditorTextBox.Text ?? string.Empty;
        for (var i = 1; i <= 999; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (!source.Contains(candidate, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return $"{baseName}_new";
    }

    private static string GetSuggestedRequiredDefault(LayerPropertyDescriptor desc) => desc.Name switch
    {
        "text" => "\"Hello\"",
        "source" => "\"sprite.png\"",
        "fn" => "\"(u, v) => [u, v, 0.5, 1.0]\"",
        "shapes" => "[{ shape = \"circle\", x = 0.5, y = 0.5, r = 0.3 }]",
        _ => string.Empty
    };

    private static Control CreateSectionHeader(string title, string hexColor) =>
        new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            Foreground = Brush.Parse(hexColor),
            Margin = new Thickness(0, 6, 0, 2)
        };

    private static Control CreateLabeledField(string label, string? description, Control inputControl)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,*")
        };

        var labelStack = new StackPanel
        {
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var nameText = new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = Brush.Parse("#D8D8D8")
        };
        ToolTip.SetTip(nameText, description);
        labelStack.Children.Add(nameText);

        Grid.SetColumn(labelStack, 0);
        Grid.SetColumn(inputControl, 1);
        grid.Children.Add(labelStack);
        grid.Children.Add(inputControl);
        return grid;
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

    private void SyncScroll(ScrollViewer source, ScrollViewer target)
    {
        if (_syncingScroll || source is null || target is null) return;
        _syncingScroll = true;
        target.Offset = new Point(0, source.Offset.Y);
        _syncingScroll = false;
    }

    private void OnNewClicked(object? sender, RoutedEventArgs? e)
    {
        StopPlay();
        EditorTextBox.Text = DefaultNewScript;
        _viewModel.SourceFilePath = string.Empty;
        UpdateTitle("Untitled");
        _viewModel.RunNow();
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs? e)
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
                    _viewModel.SourceFilePath = path;
                    UpdateTitle(Path.GetFileName(path));
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

    private async void OnSaveClicked(object? sender, RoutedEventArgs? e)
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
            UpdateTitle(Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            await ShowMessageAsync($"Could not save file:\n{ex.Message}", "Save Error");
        }
    }

    private async void OnSaveAsClicked(object? sender, RoutedEventArgs? e) => await SaveAsAsync();

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
                    UpdateTitle(Path.GetFileName(path));
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

    private async Task ShowSetSeedDialogAsync()
    {
        var seedBox = new TextBox
        {
            Text = _viewModel.SeedOverride,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Watermark = "e.g. 42 (leave blank for random/document seed)",
            Margin = new Thickness(0, 4, 0, 16)
        };

        var okButton = new Button
        {
            Content = "OK",
            MinWidth = 76,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            IsDefault = true
        };
        var clearButton = new Button
        {
            Content = "Clear",
            MinWidth = 76,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var cancelButton = new Button
        {
            Content = "Cancel",
            MinWidth = 76,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            IsCancel = true
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children = { clearButton, cancelButton, okButton }
        };

        var dialog = new Window
        {
            Title = "Set Master Seed Override",
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brush.Parse("#262626"),
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Children =
                {
                    new TextBlock
                    {
                        Text = "Enter a numeric master seed override:",
                        FontWeight = FontWeight.Bold,
                        Margin = new Thickness(0, 0, 0, 8),
                        Foreground = Brush.Parse("#E8E8E8")
                    },
                    new TextBlock
                    {
                        Text = "Overrides the deterministic master seed used for all procedural layers.",
                        Foreground = Brush.Parse("#9A9A9A"),
                        FontSize = 11,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    seedBox,
                    buttonPanel
                }
            }
        };

        okButton.Click += (_, _) =>
        {
            _viewModel.SeedOverride = seedBox.Text?.Trim() ?? string.Empty;
            dialog.Close();
        };

        clearButton.Click += (_, _) =>
        {
            _viewModel.SeedOverride = string.Empty;
            dialog.Close();
        };

        cancelButton.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(this);
    }

    private void UpdateTitle(string? fileName = null)
    {
        var name = !string.IsNullOrWhiteSpace(fileName)
            ? fileName
            : (!string.IsNullOrWhiteSpace(_viewModel.SourceFilePath) ? Path.GetFileName(_viewModel.SourceFilePath) : "Untitled");

        var seedSuffix = !string.IsNullOrWhiteSpace(_viewModel.SeedOverride)
            ? $" [Seed: {_viewModel.SeedOverride}]"
            : string.Empty;

        Title = $"GfxStudio — {name}{seedSuffix}";
    }

    private void UpdateRunOnChangeMenu()
    {
        if (RunOnChangeMenuItem is not null)
        {
            RunOnChangeMenuItem.Header = _viewModel.RunOnChange ? "Run on _Change  ✓" : "Run on _Change";
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

        if (e.PropertyName is nameof(MainViewModel.RunOnChange))
        {
            UpdateRunOnChangeMenu();
        }

        if (e.PropertyName is nameof(MainViewModel.SeedOverride))
        {
            UpdateTitle();
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

    private static bool IsKeyword(string identifier) => identifier is "layers" or "output" or "scripts" or "variables";

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
            return;
        }

        EditorTextBox.Text = FallbackGfx;
        _viewModel.SourceFilePath = string.Empty;
        UpdateTitle("Untitled");
    }

    private void PopulateSamples()
    {
        _samplePaths.Clear();
        LoadSampleMenuItem.Items.Clear();

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
            var fileName = Path.GetFileName(path);
            _samplePaths[fileName] = path;

            var item = new MenuItem { Header = fileName };
            var localPath = path;
            item.Click += (s, e) => LoadSampleScript(localPath);
            LoadSampleMenuItem.Items.Add(item);
        }
    }

    private void LoadSampleScript(string path)
    {
        EditorTextBox.Text = File.ReadAllText(path);
        _viewModel.SourceFilePath = path;
        UpdateTitle(Path.GetFileName(path));
    }
}
