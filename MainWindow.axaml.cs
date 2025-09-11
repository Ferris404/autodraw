using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Newtonsoft.Json;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;

namespace Autodraw;

public class ActionDisp
{
    public required string Text { get; set; }
    public required InputAction boundAction { get; set; }
    public int Speed { get; set; }
    public int Delay { get; set; }
}

public partial class MainWindow : Window
{
    public static MainWindow? CurrentMainWindow { get; private set; }
    private OpenAIPrompt? _aiPrompt;
    private DevTest? _devwindow;
    private Bitmap? _displayedBitmap;
    private bool _inChange;

    private readonly Regex _numberRegex = MyRegex();

    private static Regex MyRegex() => new Regex(@"[^0-9]", RegexOptions.Compiled);

    // Automation
    public ObservableCollection<ActionDisp> ActionsContext { get; set; } = new();
    private readonly List<InputAction> _actionStack = new();
    List<SKBitmap> _layersStack = new();

    private long _lastMem;
    private long _lastTime = DateTime.Now.ToFileTime();
    private int _maxBlackThreshold = 127;
    private int _alphaThresh = 200;

    private int _minBlackThreshold;
    private SKBitmap? _preFxBitmap = new(318, 318, true);
    private SKBitmap? _processedBitmap;

    private SKBitmap? _rawBitmap = new(318, 318, true);
    private bool _isAnimatedImage;

    private Settings? _settings;

    public int WidthLockValue { get; set; }
    public int HeightLockValue { get; set; }
    public int WidthNumber { get; set; } = 1;
    public int HeightNumber { get; set; } = 1;

    public MainWindow()
    {
        DataContext = this; // This stupid piece of shit is required, unlike normal Xaml, fuck it.
        
        InitializeComponent();

        if (Design.IsDesignMode) return;

        this.AttachDevTools();

        // Set language to user-specified language 
        var installedLanguage = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        Thread.CurrentThread.CurrentCulture = new CultureInfo(Config.GetEntry("userlang") ?? installedLanguage);
        Thread.CurrentThread.CurrentUICulture = new CultureInfo(Config.GetEntry("userlang") ?? installedLanguage);
        Utils.Log(installedLanguage);

        CurrentMainWindow = this;
        // Onboarding
        //if (!File.Exists(Config.ConfigPath)) 
        Config.init();

        UpdateActionsContext();

        // Taskbar
        CloseAppButton.Click += (_, _) => Close();
        MinimizeAppButton.Click += MinimizeAppOnClick;
        SettingsButton.Click += OpenSettingsOnClick;
        DevButton.Click += (_, _) => OpenDevWindow();

        // Base
        Closing += (_, _) => Cleanup();
        OpenButton.Click += OpenButtonOnClick;
        ProcessButton.Click += ProcessButtonOnClick;
        RunButton.Click += RunButtonOnClick;

        ImageAIGeneration.Click += ImageAIGenerationOnClick;
        ImageSaveImage.Click += ImageSaveImageOnClick;
        ImageClearImage.Click += ImageClearImageOnClick;

        // Inputs
        SizeSlider.ValueChanged += SizeSliderOnValueChanged;
        WidthInput.TextChanging += WidthInputOnTextChanged;
        HeightInput.TextChanging += HeightInputOnTextChanged;
        
        WidthLock.Click += WidthLockOnClick;
        HeightLock.Click += HeightLockOnClick;
        
        PercentageNumber.TextChanging += PercentageNumberOnTextChanged;

        DrawIntervalElement.TextChanging += DrawIntervalOnTextChanging;
        ClickDelayElement.TextChanging += ClickDelayOnTextChanging;
        minBlackThresholdElement.TextChanging += minBlackThresholdElementOnTextChanging;
        maxBlackThresholdElement.TextChanging += maxBlackThresholdElementOnTextChanging;
        AlphaThresholdElement.TextChanging += AlphaThresholdOnTextChanging;

        FreeDrawCheckbox.Click += FreeDrawCheckboxOnClick;

        EventHandler<TextChangingEventArgs> textChangeEvent = (sender, e) => HandleTextChange(e);
        HorizontalFilterText.TextChanging += textChangeEvent;
        VerticalFilterText.TextChanging += textChangeEvent;
        BorderAdvancedText.TextChanging += textChangeEvent;
        OutlineAdvancedText.TextChanging += textChangeEvent;
        InlineAdvancedText.TextChanging += textChangeEvent;
        InlineBorderAdvancedText.TextChanging += textChangeEvent;
        ErosionAdvancedText.TextChanging += textChangeEvent;

        // Config
        RefreshConfigsButton.Click += RefreshConfigList;
        SelectFolderElement.Click += SetConfigFolderViaDialog;
        SaveConfigButton.Click += SaveConfigViaDialog;
        OpenConfigElement.Click += LoadConfigViaDialog;
        LoadSelectButton.Click += LoadSelectedConfig;
        RefreshConfigList(this, null);

        Input.Start();
    }

    // User Configuration Handles

    //*
    public static FilePickerFileType ConfigsFileFilter { get; } = new("AutoDraw Config Files")
    {
        Patterns = new[] { "*.drawcfg" }
    };

    public static FilePickerFileType PngFileFilter { get; } = new("Portable Network Graphics")
    {
        Patterns = new[] { "*.png" }
    };

    private void ImageAIGenerationOnClick(object? sender, RoutedEventArgs e)
    {
        if (_aiPrompt is not null) return;
        _aiPrompt = new OpenAIPrompt();
        _aiPrompt.Show();
        _aiPrompt.Closed += AiPromptOnClosed;
    }

    private void AiPromptOnClosed(object? sender, EventArgs e)
    {
        _aiPrompt = null;
    }


    // Core Functions

    public void Cleanup()
    {
        _devwindow?.Close();
        _settings?.Close();
        _aiPrompt?.Close();
        if (Utils.LogObject != null) Utils.LogObject.Close();
        Input.Stop();
        Drawing.Halt();
    }
    
    public ImageProcessing.Filters GetSelectFilters() // This has practically become an Update _CurrentFilters if anything, but aight.
    {
        // Generic Filters
        ImageProcessing._currentFilters.MinThreshold = (byte)_minBlackThreshold;
        ImageProcessing._currentFilters.MaxThreshold = (byte)_maxBlackThreshold;
        ImageProcessing._currentFilters.AlphaThreshold = (byte)_alphaThresh;

        // Primary Filters

        //// Generic Filters
        ImageProcessing._currentFilters.Invert = InvertFilterCheck.IsChecked ?? false;
        ImageProcessing._currentFilters.Outline = OutlineFilterCheck.IsChecked ?? false;

        //// Pattern Filters
        ImageProcessing._currentFilters.Crosshatch = CrosshatchFilterCheck.IsChecked ?? false;
        ImageProcessing._currentFilters.DiagCrosshatch = DiagCrossFilterCheck.IsChecked ?? false;
        ImageProcessing._currentFilters.HorizontalLines = int.Parse(HorizontalFilterText.Text ?? "0");
        ImageProcessing._currentFilters.VerticalLines = int.Parse(VerticalFilterText.Text ?? "0");

        //// Experimental Filters
        ImageProcessing._currentFilters.BorderAdvanced = int.Parse(BorderAdvancedText.Text ?? "0");
        ImageProcessing._currentFilters.OutlineAdvanced = int.Parse(OutlineAdvancedText.Text ?? "0");
        ImageProcessing._currentFilters.InlineAdvanced = int.Parse(InlineAdvancedText.Text ?? "0");
        ImageProcessing._currentFilters.InlineBorderAdvanced = int.Parse(InlineBorderAdvancedText.Text ?? "0");
        ImageProcessing._currentFilters.ErosionAdvanced = int.Parse(ErosionAdvancedText.Text ?? "0");

        // Dither Filters
        // **Yet to be implemented**

        return ImageProcessing._currentFilters;
    }

    // External Window Opening/Closing Handles

    private void OpenSettingsOnClick(object? sender, RoutedEventArgs e)
    {
        if (_settings is not null) return;
        _settings = new Settings();
        _settings.Show();
        _settings.Closed += Settings_Closed;
    }

    private void Settings_Closed(object? sender, EventArgs e)
    {
        _settings = null;
    }

    private void OpenDevWindow()
    {
        _devwindow ??= new DevTest();
        _devwindow.Show();
        _devwindow.Closed += DevWindow_Closed;
    }

    private void DevWindow_Closed(object? sender, EventArgs e)
    {
        _devwindow = null;
    }
    
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.GetPosition(this).Y <= 20)
            BeginMoveDrag(e);
    }


    // Base UI Handles

    private void ProcessButtonOnClick(object? sender, RoutedEventArgs e)
    {
        if (_preFxBitmap.IsNull) return;
        _processedBitmap?.Dispose();
        _displayedBitmap?.Dispose();

        _processedBitmap = ImageProcessing.Process(_preFxBitmap, GetSelectFilters());
        _displayedBitmap = _processedBitmap.ConvertToAvaloniaBitmap();
        ImagePreview.Image = _displayedBitmap;
    }

    public void ImportImage(string? path, byte[]? img = null)
    {
        ClearLayersStack();

        if (IsGif(path, img))
        {
            ImportGifImage(path);
        }
        else
        {
            ImportBitmapImage(path, img);
        }

        _processedBitmap?.Dispose();
        _processedBitmap = null;
        ImagePreview.Image = _displayedBitmap;

        UpdateImageInputs();

        if (WidthLockValue > 0 || HeightLockValue > 0)
            ResizeImage(_displayedBitmap.Size.Width, _displayedBitmap.Size.Height);
    }

    private void ClearLayersStack()
    {
        if (_layersStack.Count > 0)
        {
            foreach (var f in _layersStack) f.Dispose();
            _layersStack.Clear();
        }
        _isAnimatedImage = false;
    }

    private static bool IsGif(string? path, byte[]? img)
    {
        return img is null && !string.IsNullOrWhiteSpace(path) && path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private void ImportGifImage(string? path)
    {
        try
        {
            var frames = DecodeGifFrames(path);
            if (frames.Count > 0)
            {
                _layersStack = frames;
                _rawBitmap = frames[0].Copy();
                _preFxBitmap = _rawBitmap.Copy();
                _displayedBitmap = _rawBitmap.ConvertToAvaloniaBitmap();
                _isAnimatedImage = true;
            }
            else
            {
                InitBitmapFromPath(path);
            }
        }
        catch
        {
            InitBitmapFromPath(path);
        }
    }

    private void ImportBitmapImage(string? path, byte[]? img)
    {
        _rawBitmap = img is null ? SKBitmap.Decode(path).NormalizeColor() : SKBitmap.Decode(img).NormalizeColor();
        _preFxBitmap = _rawBitmap.Copy();
        _displayedBitmap = _rawBitmap.ConvertToAvaloniaBitmap();
    }

    private void InitBitmapFromPath(string? path)
    {
        _rawBitmap = SKBitmap.Decode(path).NormalizeColor();
        _preFxBitmap = _rawBitmap.Copy();
        _displayedBitmap = _rawBitmap.ConvertToAvaloniaBitmap();
    }

    private void UpdateImageInputs()
    {
        _inChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = WidthLockValue > 0 ? WidthLockValue.ToString() : _displayedBitmap.Size.Width.ToString();
        HeightInput.Text = HeightLockValue > 0 ? HeightLockValue.ToString() : _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }

    private static List<SKBitmap> DecodeGifFrames(string gifPath)
    {
        var frames = new List<SKBitmap>();
        try
        {
            using var stream = File.OpenRead(gifPath);
            using var codec = SKCodec.Create(stream);
            if (codec == null || codec.FrameCount <= 1)
            {
                return frames;
            }
            var srcInfo = codec.Info;
            var outInfo = new SKImageInfo(srcInfo.Width, srcInfo.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var frameInfos = codec.FrameInfo;
            for (int i = 0; i < codec.FrameCount; i++)
            {
                var bmp = new SKBitmap(outInfo);
                int prior = frameInfos != null && i < frameInfos.Length ? frameInfos[i].RequiredFrame : -1;
                var opts = prior >= 0 ? new SKCodecOptions(i, prior) : new SKCodecOptions(i);
                var result = codec.GetPixels(outInfo, bmp.GetPixels(), opts);
                if (result == SKCodecResult.Success || result == SKCodecResult.IncompleteInput)
                {
                    frames.Add(bmp);
                }
                else
                {
                    bmp.Dispose();
                }
            }
        }
        catch
        {
            // ignore
        }
        return frames;
    }

    private async void OpenButtonOnClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Image",
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll },
            AllowMultiple = false
        });

        if (file.Count == 1) ImportImage(file[0].TryGetLocalPath());
    }

    private void RunButtonOnClick(object? sender, RoutedEventArgs e)
    {
    // Capture current filter settings
    GetSelectFilters();
        if (_processedBitmap == null && !_isAnimatedImage && _layersStack.Count == 0)
        {
            new MessageBox().ShowMessageBox("Error!", "Please select and process an image beforehand.", "error");
            return;
        }

        // Windows doesn't ask for permissions before mouse movement, Linux (wayland) and macOS require it.
        // We just create an empty Mouse Movement to trigger the popup
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var hook = new TaskPoolGlobalHook();
            hook.MouseMoved += (o, args) => { }; // Start listening for input
            hook.RunAsync();
        }
        if (Drawing.IsDrawing) return;


        if (_isAnimatedImage && _layersStack.Count > 0)
        {
            var pv = new Preview();
            pv.ReadyStackDraw(_preFxBitmap, _layersStack, _actionStack);
        }
        else
        {
            var pv = new Preview();
            pv.ReadyDraw(_processedBitmap);
        }
        WindowState = WindowState.Minimized;
    }

    private async void ImageSaveImageOnClick(object? sender, RoutedEventArgs e)
    {
        if (_processedBitmap is null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Processed Image",
            FileTypeChoices = new[] { PngFileFilter }
        });

        if (file is not null)
        {
            var encodedData = _processedBitmap.Encode(SKEncodedImageFormat.Png, 100);
            await using var stream = await file.OpenWriteAsync();

            encodedData.SaveTo(stream);
        }
    }

    private void ImageClearImageOnClick(object? sender, RoutedEventArgs e)
    {
        if (_layersStack.Count > 0)
        {
            foreach (var f in _layersStack) f.Dispose();
            _layersStack.Clear();
        }
        _isAnimatedImage = false;
        _rawBitmap = new SKBitmap(318, 318, true);
        _preFxBitmap = new SKBitmap(318, 318, true);
        _processedBitmap = null;
        _displayedBitmap = null;
        ImagePreview.Image = null;
    }

    // Inputs Handles

    private void HandleTextChange(TextChangingEventArgs e)
    {
        var source = (TextBox)e.Source;
        source.Text = _numberRegex.Replace(source.Text, "");
    }

    private void ResizeImage(double width, double height)
    {
        width = WidthLockValue > 0 ? WidthLockValue : Math.Max(1, width);
        height = HeightLockValue > 0 ? HeightLockValue : Math.Max(1, height);

        if (WidthLockValue == 0) WidthNumber = (int)width;
        if (HeightLockValue == 0) HeightNumber = (int)height;

        UpdateMemoryPressure();

        var newSize = new SKSizeI((int)width, (int)height);

        if (_processedBitmap == null)
        {
            ResizeRawBitmap(newSize);
        }
        else
        {
            ResizeProcessedBitmap(newSize);
        }
    }

    private void UpdateMemoryPressure()
    {
        if (GC.GetTotalMemory(false) < _lastMem)
            GC.RemoveMemoryPressure(_lastMem);
        _lastMem = GC.GetTotalMemory(false);
    }

    private void ResizeRawBitmap(SKSizeI newSize)
    {
        var resizedBitmap = _rawBitmap.Resize(newSize, SKFilterQuality.High);
        _preFxBitmap.Dispose();
        _preFxBitmap = resizedBitmap;
        _displayedBitmap?.Dispose();
        _displayedBitmap = resizedBitmap.ConvertToAvaloniaBitmap();
        ImagePreview.Image = _displayedBitmap;
        GC.AddMemoryPressure(resizedBitmap.ByteCount);

        ResizeAnimatedFramesIfNeeded(newSize);
    }

    private void ResizeProcessedBitmap(SKSizeI newSize)
    {
        var resizedBitmap = _rawBitmap.Resize(newSize, SKFilterQuality.High);
        _preFxBitmap.Dispose();
        _preFxBitmap = resizedBitmap;
        var postProcessBitmap = ImageProcessing.Process(resizedBitmap, GetSelectFilters());
        _processedBitmap.Dispose();
        _processedBitmap = postProcessBitmap;
        _displayedBitmap?.Dispose();
        _displayedBitmap = postProcessBitmap.ConvertToAvaloniaBitmap();
        ImagePreview.Image = _displayedBitmap;
        GC.AddMemoryPressure(resizedBitmap.ByteCount);

        ResizeAnimatedFramesIfNeeded(newSize);
    }

    private void ResizeAnimatedFramesIfNeeded(SKSizeI newSize)
    {
        if (_isAnimatedImage && _layersStack.Count > 0)
        {
            var resizedFrames = new List<SKBitmap>(_layersStack.Count);
            foreach (var frame in _layersStack)
            {
                var r = frame.Resize(newSize, SKFilterQuality.High);
                resizedFrames.Add(r);
            }
            foreach (var f in _layersStack) f.Dispose();
            _layersStack = resizedFrames;
        }
    }
    private void SizeSliderOnValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_inChange) return;
        if (DateTime.Now.ToFileTime() - _lastTime < 333_333) return;
        _lastTime = DateTime.Now.ToFileTime();

        ResizeImage(_rawBitmap.Width * SizeSlider.Value / 100, _rawBitmap.Height * SizeSlider.Value / 100);

        _inChange = true;
        PercentageNumber.Text = $"{Math.Round(SizeSlider.Value)}%";
        WidthInput.Text = _displayedBitmap.Size.Width.ToString();
        HeightInput.Text = _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }

    private void PercentageNumberOnTextChanged(object? sender, TextChangingEventArgs e)
    {
        if (_inChange) return;
        var numberText = _numberRegex.Replace(PercentageNumber.Text, "");
        PercentageNumber.Text = numberText + "%";
        e.Handled = true;
        var setNumber = int.Parse(numberText);
        if (setNumber < 1) return;
        if (setNumber > 500)
        {
            PercentageNumber.Text = "500%";
            return;
        }

    ResizeImage(_rawBitmap.Width * (double)setNumber / 100, _rawBitmap.Height * (double)setNumber / 100);

        _inChange = true;
        WidthInput.Text = _displayedBitmap.Size.Width.ToString();
        HeightInput.Text = _displayedBitmap.Size.Height.ToString();
        _inChange = false;
    }

    private void HeightInputOnTextChanged(object? sender, TextChangingEventArgs e)
    {
        if (HeightInput.Text == null) return;
        if (_inChange) return;
        var numberText = _numberRegex.Replace(HeightInput.Text, "");
        _inChange = true;
        HeightInput.Text = numberText;
        _inChange = false;
        e.Handled = true;

        if (numberText.Length < 1) return;
        _inChange = true;
        double ratio = (double)_rawBitmap.Width / _rawBitmap.Height; // STUPID STUPID STUPID!!!

    int heightVal =  int.Parse(_numberRegex.Replace(HeightInput.Text, ""));
    int widthVal = (bool)UnlockAspectRatioCheckBox.IsChecked! ? int.Parse(WidthInput.Text) : (int)(heightVal * ratio);

        if(widthVal > 4096)
        {
            heightVal = (int)(4096 / ratio);
            widthVal = 4096;
        }
        
        widthVal = Math.Max(Math.Min(widthVal, 4096), 1);
        heightVal = Math.Max(Math.Min(heightVal, 4096), 1);
        
        if (UnlockAspectRatioCheckBox.IsChecked ?? false) ResizeImage(int.Parse(WidthInput.Text), heightVal);
        else ResizeImage(widthVal, heightVal);

        PercentageNumber.Text = $"{Math.Round((decimal)heightVal / _rawBitmap.Height * 100)}%";
        WidthInput.Text = widthVal.ToString();
        HeightInput.Text = heightVal.ToString();
        _inChange = false;
        HeightInput.Text = heightVal.ToString();
        _inChange = false;
    }

    private void WidthInputOnTextChanged(object? sender, TextChangingEventArgs e)
    {
        if (WidthInput.Text == null) return;
        if (_inChange) return;
        var numberText = _numberRegex.Replace(WidthInput.Text, "");
        _inChange = true;
        WidthInput.Text = numberText;
        _inChange = false;
        e.Handled = true;

        if (numberText.Length < 1) return;
        _inChange = true;
        double ratio = (double)_rawBitmap.Height / _rawBitmap.Width; // STUPID STUPID STUPID!!!

        int widthVal2 = int.Parse(_numberRegex.Replace(WidthInput.Text, ""));
        int heightVal2 = (bool)UnlockAspectRatioCheckBox.IsChecked! ? int.Parse(HeightInput.Text) : (int)(widthVal2 * ratio);
        Utils.Log(heightVal2);
        Utils.Log(ratio);

        if(heightVal2 > 4096)
        {
            widthVal2 = (int)(4096 / ratio);
            heightVal2 = 4096;
        }
        
        widthVal2 = Math.Max(Math.Min(widthVal2, 4096), 1);
        heightVal2 = Math.Max(Math.Min(heightVal2, 4096), 1);

        if (UnlockAspectRatioCheckBox.IsChecked ?? false) ResizeImage(widthVal2, int.Parse(HeightInput.Text));
        else ResizeImage(widthVal2, heightVal2);

        PercentageNumber.Text = $"{Math.Round((decimal)widthVal2 / _rawBitmap.Width * 100)}%";
        WidthInput.Text = widthVal2.ToString();
        HeightInput.Text = heightVal2.ToString();
        _inChange = false;
    }
    private void WidthLockOnClick(object? sender, RoutedEventArgs e)
    {
        WidthLockValue = WidthLockValue > 0 ? 0 : WidthNumber;
        WidthLockImage.Classes.Clear();
        WidthLockImage.Classes.Add(WidthLockValue > 0 ? "LockedIcon" : "UnlockedIcon");
    }

    private void HeightLockOnClick(object? sender, RoutedEventArgs e)
    {
    HeightLockValue = HeightLockValue > 0 ? 0 : HeightNumber;
        HeightLockImage.Classes.Clear();
        HeightLockImage.Classes.Add(HeightLockValue > 0 ? "LockedIcon" : "UnlockedIcon");
    }
    
    private static void DrawIntervalOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        var source = sender as TextBox;
        if (source == null) return;

        CleanTextBoxInput(source);

        SetDrawingInterval(source.Text);
    }

    private static void CleanTextBoxInput(TextBox source)
    {
        source.Text = MyRegex().Replace(source.Text, "");
    }

    private static void SetDrawingInterval(string text)
    {
        if (int.TryParse(text, out var interval))
        {
            Drawing.Interval = interval;
        }
        else
        {
            Drawing.Interval = 10000;
        }
    }

    private static void ClickDelayOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        var source = sender as TextBox;
        if (source == null) return;
        source.Text = MyRegex().Replace(source.Text, "");
        Drawing.ClickDelay = int.TryParse(source.Text, out var clickDelay) ? clickDelay : 1000;
    }

    private void minBlackThresholdElementOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        _minBlackThreshold = int.TryParse(minBlackThresholdElement.Text, out var black) ? black : 127;
    }

    private void maxBlackThresholdElementOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        _maxBlackThreshold = int.TryParse(maxBlackThresholdElement.Text, out var black) ? black : 127;
    }

    private void AlphaThresholdOnTextChanging(object? sender, TextChangingEventArgs e)
    {
        HandleTextChange(e);
        _alphaThresh = int.TryParse(AlphaThresholdElement.Text, out var alpha) ? alpha : 127;
    }

    private static void FreeDrawCheckboxOnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox)
        {
            Drawing.FreeDraw2 = checkBox.IsChecked ?? false;
        }
    }

    // Toolbar Handles

    private void MinimizeAppOnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    public async Task PasteControl()
    {
        var clipboard = Clipboard;
        async void writeDump()
        {
            string dump = JsonConvert.SerializeObject(await clipboard.GetFormatsAsync(), Formatting.Indented);
            Utils.Log(dump);
            dump = JsonConvert.SerializeObject(await clipboard.GetTextAsync(), Formatting.Indented);
            Utils.Log(dump);
            dump = JsonConvert.SerializeObject(await clipboard.GetDataAsync(DataFormats.FileNames), Formatting.Indented);
            Utils.Log(dump);
            dump = JsonConvert.SerializeObject(await clipboard.GetDataAsync(DataFormats.Text), Formatting.Indented);
            Utils.Log(dump);
        }
        try
        {
            var file = await clipboard.GetDataAsync(DataFormats.Files) as IEnumerable<IStorageItem>;
            var img = await clipboard.GetDataAsync("PNG");
            string d = JsonConvert.SerializeObject(await clipboard.GetFormatsAsync(), Formatting.Indented);
            Utils.Log(d);
            if (file is not null) {ImportImage(file.First().Path.LocalPath);}
            else if (img is not null) {ImportImage("",(byte[]?)img);}
            else
            {
                new MessageBox().ShowMessageBox("Error!", "Invalid Image to Paste!", "error");
                Utils.Log("Error with PasteControl(): No image found in clipboard! Dumping clipboard.");
                writeDump();
            }
            
        }
        catch (Exception ex)
        {
            new MessageBox().ShowMessageBox("Error!", "Invalid Image to Paste!", "error");
            Utils.Log("Error with PasteControl(): " + ex);
            writeDump();
        }
    }

    public async void SetConfigFolderViaDialog(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folder.Count != 1) return;
        Config.SetEntry("ConfigFolder", folder[0].TryGetLocalPath());
        RefreshConfigList(this, null);
    }

    public static void LoadConfig(string? path)
    {
        if (!path.EndsWith(".drawcfg")) return;
        if (!File.Exists(path))
        {
            new MessageBox().ShowMessageBox("Warning!", "This config does not exist!", "warning");
            return;
        }
        var lines = File.ReadAllLines(path);

        // Access MainWindow.CurrentMainWindow to get instance members
        var mainWindow = MainWindow.CurrentMainWindow;
        if (mainWindow == null) return;

        mainWindow.SelectedConfigLabel.Content =
            $"{Properties.Resources.ConfigSelected} - {Path.GetFileNameWithoutExtension(path)}";

        mainWindow.DrawIntervalElement.Text = lines.Length > 0 ? lines[0] : "10000";

        mainWindow.ClickDelayElement.Text = lines.Length > 1 ? lines[1] : "1000";
        // Silly!!

        if (lines.Length <= 4) return;
        if (!bool.TryParse(lines[4], out var _fd2)) return;
        mainWindow.FreeDrawCheckbox.IsChecked = _fd2;
        Drawing.FreeDraw2 = _fd2;
    }

    public async void SaveConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Config",
            FileTypeChoices = new[] { ConfigsFileFilter }
        });

        if (file is not null)
        {
            await using var stream = await file.OpenWriteAsync();
            await using var streamWriter = new StreamWriter(stream);

            string?[] values =
            {
                DrawIntervalElement.Text,
                ClickDelayElement.Text,
                maxBlackThresholdElement.Text,
                AlphaThresholdElement.Text,
                FreeDrawCheckbox.IsChecked.ToString(),
                "", //We should've used Json for future compatibility and freedom to change and remove config variables @gz9.
                minBlackThresholdElement.Text
            };

            await streamWriter.WriteAsync(string.Join("\r\n", values));
        }
    }

    public async void LoadConfigViaDialog(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Load Config",
            FileTypeFilter = new[] { ConfigsFileFilter },
            AllowMultiple = false
        });

        if (file.Count == 1) LoadConfig(file[0].TryGetLocalPath());
    }

    public static void RefreshConfigList(object? sender, RoutedEventArgs? e)
    {
        var configFolder = Config.GetEntry("ConfigFolder");
        if (configFolder == null) return;
        if (!Directory.Exists(configFolder)) return;
        var files = Directory.GetFiles(configFolder, "*.drawcfg");
        var fileNames = files.Select(f => Path.GetFileNameWithoutExtension(f)).ToArray();

        var mainWindow = MainWindow.CurrentMainWindow;
        if (mainWindow == null) return;

        mainWindow.ConfigsListBox.ClearValue(ItemsControl.ItemsSourceProperty);
        mainWindow.ConfigsListBox.Items.Clear();
        mainWindow.ConfigsListBox.ItemsSource = fileNames;
    }

    public static void LoadSelectedConfig(object? sender, RoutedEventArgs e)
    {
        var mainWindow = MainWindow.CurrentMainWindow;
        if (mainWindow == null) return;
        if (mainWindow.ConfigsListBox.SelectedItem == null) return;
        var selectedItem = mainWindow.ConfigsListBox.SelectedItem.ToString();
        if (selectedItem == null) return;
        LoadConfig($"{Path.Combine(Config.GetEntry("ConfigFolder"), selectedItem)}.drawcfg");
    }
    
    // Actions
    ActionPrompt _actionPrompt = new();
    
    public static void ClickActionObject(InputAction Action)
    {
        // Show the action prompt window for the selected action
        var mainWindow = MainWindow.CurrentMainWindow;
        if (mainWindow == null) return;

        if (!mainWindow._actionPrompt.IsActive)
            mainWindow._actionPrompt = new ActionPrompt();

        mainWindow._actionPrompt.Action = Action;
        mainWindow._actionPrompt.Show();
        mainWindow._actionPrompt.Callback = mainWindow._addActionCallback;
    }

    public void AddAction()
    {
        if (!_actionPrompt.IsActive) _actionPrompt = new();
        _actionPrompt.Show();
        _actionPrompt.Callback = _addActionCallback;
        Console.WriteLine("Black");
    }

    public void _addActionCallback()
    {
        Console.WriteLine("Recv Message!");
        if (_actionPrompt.Action is not null) // Empty the compartments of your pantaloons.
        {
            Console.WriteLine("+1 Crime Level");
            Console.WriteLine(_actionPrompt.Action);
            _actionStack.Add(_actionPrompt.Action);
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(UpdateActionsContext);
        }
        // Mothership: "Fuck you." *obliterates your ass*
        _actionPrompt.Close();
    }

    private void UpdateActionsContext()
    {
        if (ActionsContext is null) return;

        // Clear the existing items in ActionsContext
        ActionsContext.Clear();

        // Populate ActionsContext with relevant data from _actionStack
        foreach (var action in _actionStack)
        {
            var _ActionType = "";
            if (action.Action == InputAction.ActionType.KeyDown) _ActionType = "Key Down";
            else if (action.Action == InputAction.ActionType.KeyUp) _ActionType = "Key Up";
            else if (action.Action == InputAction.ActionType.LeftClick) _ActionType = "Left Click";
            else if (action.Action == InputAction.ActionType.RightClick) _ActionType = "Right Click";
            else if (action.Action == InputAction.ActionType.WriteString) _ActionType = "Write String";
            else if (action.Action == InputAction.ActionType.MoveTo) _ActionType = "Move to";
            
            string positionText = action.Position.HasValue ? $" @ x={action.Position.Value.X}, y={action.Position.Value.Y}" : "";
            var _ActionData = action.Data is null
                ? positionText
                : $" - {action.Data}";
            
            var actionDisp = new ActionDisp
            { 
                Text = $"{_ActionType}{_ActionData}",
                boundAction = action,
            };

            ActionsContext.Add(actionDisp);
        }
    }
}