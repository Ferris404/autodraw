using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Primitives;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SharpHook;
using SharpHook.Native;
using TextMateSharp.Grammars;

namespace Autodraw;

public partial class Settings : Window
{
    private readonly FilePickerFileType[] filetype =
    {
        new("All Theme Types") { Patterns = new[] { "*.axaml", "*.daxaml", "*.laxaml" }, MimeTypes = new[] { "*/*" } },
        new("Default Theme") { Patterns = new[] { "*.axaml" }, MimeTypes = new[] { "*/*" } },
        FilePickerFileTypes.All
    };
    
    private string _savedLocation = "";
    private bool _textmateLoaded;
    private bool _currentlyAwaitingKeypress;

    private const string WaitingText = "Waiting...";
    private const string ErrorTitle = "Error!";
    private const string AuthorKey = "author";
    private const string IdKey = "id";
    private const string NameKey = "name";
    private const string DescriptionKey = "description";
    private const string UsernameKey = "username";
    private const string TitleKey = "title";
    private const string DescKey = "desc";
    private const string ImageKey = "image";

    public Settings()
    {
        InitializeComponent();
        
        Settings.LoadLocalThemeItems(this);
        
        // Main Handle
        CloseAppButton.Click += CloseAppButton_Click;
        // Sidebar
        SettingsTabs.PropertyChanged += SettingsTabsOnPropertyChanged;

        // General
        AltMouseControl.IsCheckedChanged += AltMouseControl_IsCheckedChanged;
        ShowPopup.IsCheckedChanged += ShowPopup_IsCheckedChanged;
        NoRescan.IsCheckedChanged += NoRescanOnIsCheckedChanged;
        LogFile.IsCheckedChanged += LogFile_IsCheckedChanged;

        ShowPopup.IsChecked = Drawing.ShowPopup;
        AltMouseControl.IsChecked = Input.forceUio;
        NoRescan.IsChecked = Drawing.NoRescan;
        LogFile.IsChecked = Config.GetEntry("logsEnabled") == "True";
        
        //  Keybinds
        ChangeKeybind_StartDrawing.Content = Config.Keybind_StartDrawing;
        ChangeKeybind_StartDrawing.Click += async (sender, args) =>
        {
            if (_currentlyAwaitingKeypress) return;
            ChangeKeybind_StartDrawing.Content = WaitingText;
            var keybind = await ChangeKeybind_OnClick();
            Config.SetEntry("Keybind_StartDrawing", keybind.ToString());
            ChangeKeybind_StartDrawing.Content = Config.Keybind_StartDrawing;
        };
        
        ChangeKeybind_StopDrawing.Content = Config.Keybind_StopDrawing;
        ChangeKeybind_StopDrawing.Click += async (sender, args) =>
        {
            if (_currentlyAwaitingKeypress) return;
            ChangeKeybind_StopDrawing.Content = WaitingText;
            var keybind = await ChangeKeybind_OnClick();
            Config.SetEntry("Keybind_StopDrawing", keybind.ToString());
            ChangeKeybind_StopDrawing.Content = Config.Keybind_StopDrawing;
        };
        
        ChangeKeybind_PauseDrawing.Content = Config.Keybind_PauseDrawing;
        ChangeKeybind_PauseDrawing.Click += async (sender, args) =>
        {
            if (_currentlyAwaitingKeypress) return;
            ChangeKeybind_PauseDrawing.Content = WaitingText;
            var keybind = await ChangeKeybind_OnClick();
            Config.SetEntry("Keybind_PauseDrawing", keybind.ToString());
            ChangeKeybind_PauseDrawing.Content = Config.Keybind_PauseDrawing;
        };
        
        ChangeKeybind_LockPreview.Content = Config.Keybind_LockPreview;
        ChangeKeybind_LockPreview.Click += async (sender, args) =>
        {
            if (_currentlyAwaitingKeypress) return;
            ChangeKeybind_LockPreview.Content = WaitingText;
            var keybind = await ChangeKeybind_OnClick();
            Config.SetEntry("Keybind_LockPreview", keybind.ToString());
            ChangeKeybind_LockPreview.Content = Config.Keybind_LockPreview;
        };
        
    ChangeKeybind_SkipBacktrace.Content = Config.Keybind_SkipRescan;
        ChangeKeybind_SkipBacktrace.Click += async (sender, args) =>
        {
            if (_currentlyAwaitingKeypress) return;
            ChangeKeybind_SkipBacktrace.Content = WaitingText;
            var keybind = await ChangeKeybind_OnClick();
            Config.SetEntry("Keybind_SkipRescan", keybind.ToString());
            ChangeKeybind_SkipBacktrace.Content = Config.Keybind_SkipRescan;
        };
        
        // Marketplace
        MarketplaceTabs.PropertyChanged += MarketplaceTabsOnPropertyChanged;

        // DALL-E API Keys
        SaveOpenAiKey.Click += (sender, e) => Config.SetEntry("OpenAIKey", OpenAiKey.Text);
        RevealAiKey.Click += (sender, e) => OpenAiKey.RevealPassword = !OpenAiKey.RevealPassword;

        if (Config.GetEntry("showPopup") == null) Config.SetEntry("showPopup", Drawing.ShowPopup.ToString());
        if (Config.GetEntry("OpenAIKey") != null) OpenAiKey.Text = Config.GetEntry("OpenAIKey");

        //  Interactions

        DarkLightThemeToggle.Click += ToggleTheme_Click;
        NewTheme.Click += NewTheme_Click;
        SaveTheme.Click += SaveTheme_Click;
        OpenTheme.Click += OpenTheme_Click;
        LoadTheme.Click += LoadTheme_Click;
        
        //  Configuration
        ThemesLocationTextBox.Text = Config.ThemesPath;
        ThemesLocationFolderButton.Click += ThemesLocationFolderButtonOnClick;
        ThemesLocationSaveButton.Click += ThemesLocationSaveButtonOnClick;

        ImageCacheLocationTextBox.Text = Config.CachePath;
        ImageCacheLocationFolderButton.Click += ImageCacheLocationFolderButtonOnClick;
        ImageCacheLocationSaveButton.Click += ImageCacheLocationSaveButtonOnClick;
        ImageCacheLocationClearButton.Click += ImageCacheLocationClearButtonOnClick;
    }

    private async void MarketplaceTabsOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.ToString() != "SelectedIndex") return;
        if (MarketplaceTabs.SelectedIndex == 0)
        {
            LoadLocalThemeItems(this);
        }
        else if (MarketplaceTabs.SelectedIndex == 1)
        {
            await LoadOnlineThemeItems(this);
        }
    }
    
    private void SettingsTabsOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.ToString() != "SelectedItem") return;
        TreeViewItem select = (TreeViewItem)SettingsTabs.SelectedItem;
        string? selectionName = select.Name;
        if (string.IsNullOrWhiteSpace(selectionName)) return;
        string selectionTabName = MyRegex().Replace(selectionName, "") + "Tab";
        var selectionTab = this.FindControl<Grid>(selectionTabName);
        if (selectionTab is null) return;
        selectionTab.Opacity = 1;
        selectionTab.IsHitTestVisible = true;
        if (currentlyViewing is not null && currentlyViewing != selectionTab)
        {
            currentlyViewing.Opacity = 0;
            currentlyViewing.IsHitTestVisible = false;
        }
        
        // Marketplace Loading Stuff:
    if (selectionName == "MarketplaceSelector")
        {
            if (MarketplaceTabs.SelectedIndex == 0)
            {
                LoadLocalThemeItems(this);
            }
            else if (MarketplaceTabs.SelectedIndex == 1)
            {
                _ = LoadOnlineThemeItems(this);
            }
        }
        
        // Theme Editor Loading Stuff:
    if (selectionName == "ThemeEditorSelector" && !_textmateLoaded)
        {
            // Moved this here for performance reasons :P

            //  TextEditor Input
            var _textEditor1 = this.FindControl<TextEditor>("ThemeInput");
            var _registryOptions1 = new RegistryOptions(ThemeName.DarkPlus);
            var _textMateInstallation1 = _textEditor1.InstallTextMate(_registryOptions1);
            _textMateInstallation1.SetGrammar(
                _registryOptions1.GetScopeByLanguageId(_registryOptions1.GetLanguageByExtension(".xml").Id));

            //  TextEditor Output
            var _textEditor2 = this.FindControl<TextEditor>("ThemeOutput");
            var _registryOptions2 = new RegistryOptions(ThemeName.DarkPlus);
            var _textMateInstallation2 = _textEditor2.InstallTextMate(_registryOptions2);
            _textMateInstallation2.SetGrammar(
                _registryOptions2.GetScopeByLanguageId(_registryOptions2.GetLanguageByExtension(".md").Id));
        
            _textmateLoaded = true;
        }

    currentlyViewing = selectionTab;
    }

    private Task<KeyCode> ChangeKeybind_OnClick()
    {
        _currentlyAwaitingKeypress = true;

        var tcs = new TaskCompletionSource<KeyCode>();

    void handler(object? sender, KeyboardHookEventArgs e)
        {
            Input.taskHook.KeyPressed -= handler;
            _currentlyAwaitingKeypress = false;
            tcs.SetResult(e.Data.KeyCode);
    }
    
        Input.taskHook.KeyPressed += handler;

        return tcs.Task;
    }

    // Image Cache

    private static void ImageCacheLocationClearButtonOnClick(object? sender, RoutedEventArgs e)
    {
        string[] cachedImages = Directory.GetFiles(Config.CachePath, "*.jpeg", SearchOption.AllDirectories);
        foreach (string cachedImage in cachedImages)
        {
            try
            {
                File.Delete(cachedImage);
            }
            catch (UnauthorizedAccessException)
            {
                new MessageBox().ShowMessageBox(ErrorTitle,
                    "Appears the location provided may be a protected folder. Unable to clear cache automatically.");
                break;
            }
            catch (Exception ex)
            {
                Utils.Log(ex.ToString());
                return;
            }
        }
    }

    private static void ImageCacheLocationSaveButtonOnClick(object? sender, RoutedEventArgs e)
    {
        // Cast sender to TextBox if possible, otherwise use Config.CachePath
        var textBox = sender as TextBox;
        string cachePath = textBox != null ? textBox.Text : Config.CachePath;

        if (cachePath == Config.CachePath) return;
        if (!Directory.Exists(cachePath))
        {
            new MessageBox().ShowMessageBox(ErrorTitle, "Please provide a valid location!");
            return;
        }
        if (Directory.GetFiles(cachePath, "*", SearchOption.AllDirectories).Length != 0)
        {
            // Already KNOW someone's going to put the Cache location in somewhere unsafe like C:/ or System32.
            new MessageBox().ShowMessageBox(ErrorTitle, "Please ensure the folder is empty!");
            return;
        }

        UpdateCachePath(cachePath);

        if (textBox != null)
            textBox.Text = Config.CachePath;
    }

    private static void UpdateCachePath(string path)
    {
        Config.CachePath = Path.GetFullPath(path);
        Config.SetEntry("SavedCachePath", Config.CachePath);
    }

    private async void ImageCacheLocationFolderButtonOnClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folder.Count != 1) return;
        ImageCacheLocationTextBox.Text = folder[0].TryGetLocalPath();
    }
    
    // Themes Location

    private static void ThemesLocationSaveButtonOnClick(object? sender, RoutedEventArgs e)
    {
        // Cast sender to TextBox if possible, otherwise use Config.ThemesPath
        var textBox = sender as TextBox;
        string themesPath = textBox != null ? textBox.Text : Config.ThemesPath;

        if (!Directory.Exists(themesPath))
        {
            new MessageBox().ShowMessageBox(ErrorTitle, "Please provide a valid location!");
            return;
        }

        UpdateThemesPath(themesPath);

        if (textBox != null)
            textBox.Text = Config.ThemesPath;
    }

    private static void UpdateThemesPath(string path)
    {
        Config.ThemesPath = Path.GetFullPath(path);
        Config.SetEntry("SavedThemesPath", Config.ThemesPath);
    }

    private async void ThemesLocationFolderButtonOnClick(object? sender, RoutedEventArgs e)
    {
        var folder = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folder.Count != 1) return;
        ThemesLocationTextBox.Text = folder[0].TryGetLocalPath();
    }

    // Main Stuff
    
    private void CloseAppButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private Grid? currentlyViewing;
    
    public class ListedTheme
    {
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Image { get; set; } = string.Empty;
        
        public string ButtonParameter { get; set; } = string.Empty;
    }

    public static async void LoadOnlineTheme(object? data, RoutedEventArgs routedEventArgs)
    {
        // This is such a stupid way of doing this but I really am out of ideas :P
        string rawJsonData = (string)((Button)data).CommandParameter;
        JObject JsonData = JObject.Parse(rawJsonData);
        string FileLocation = await Marketplace.Download(Convert.ToInt32(JsonData.GetValue(IdKey).ToString()));
        string fileName = Path.GetFileNameWithoutExtension(FileLocation);
        await File.WriteAllTextAsync(Path.Combine(Config.ThemesPath, fileName + "-Data.json"), rawJsonData);
    }

    private static async Task LoadOnlineThemeItems(Settings instance)
    {
        instance.MarketplacePleaseWait.Opacity = 1;
        instance.OnlineThemes.Items.Clear();
        var MarketplaceList = await Marketplace.List("theme");
        instance.MarketplacePleaseWait.Opacity = 0;
        foreach (var token in MarketplaceList)
        {
            if (token is not JObject themeData) continue;
            string title = (string)themeData.GetValue(NameKey) ?? "Title";
            string description = (string)themeData.GetValue(DescriptionKey)  ?? string.Empty;
            string image = $"https://auto-draw.com/ugc/{themeData.GetValue(AuthorKey)}/{themeData.GetValue(IdKey)}.png";
            string author = (string)themeData.GetValue(UsernameKey) ?? "Unknown";
            
            var data = new Dictionary<string, string>()
            {
                { TitleKey, title },
                { DescKey, description },
                { ImageKey, image },
                { AuthorKey, author },
                { IdKey, themeData.GetValue(IdKey).ToString() }
            };

            string json = JsonConvert.SerializeObject(data);
            
            ListedTheme listData = new ListedTheme();
            listData.Title = title;
            listData.Description = description;
            listData.Image = image;
            listData.Author = "Theme by "+author;
            listData.ButtonParameter = json ?? "";
            instance.OnlineThemes.Items.Add(listData);
            
        }
    }

    public static void LoadLocalTheme(object? data, RoutedEventArgs routedEventArgs)
    {
        // This is such a stupid way of doing this but I really am out of ideas :P
        string location = (string)((Button)data).CommandParameter;
        App.LoadTheme(location);
        
    }
    
    private static void LoadLocalThemeItems(Settings instance)
    {
        instance.InstalledThemes.Items.Clear();
        string[] extensions = { ".axaml", ".laxaml", ".daxaml" };
        string[] dirFiles = Directory.GetFiles(Config.ThemesPath, "*", SearchOption.AllDirectories);
        var themes = dirFiles.Where(file =>
            extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
        foreach (string theme in themes)
        {
            ListedTheme listData = CreateListedTheme(theme);
            instance.InstalledThemes.Items.Add(listData);
        }
    }

    private static ListedTheme CreateListedTheme(string theme)
    {
        string fileName = Path.GetFileName(theme);
        string title = fileName;
        string description = "";
        string image = "";
        string author = "Theme is Locally Stored";

        string parent = Directory.GetParent(theme).FullName;

        image = GetThemeImage(parent, theme);
        (title, description, author, image) = GetThemeMetadata(parent, theme, title, description, author, image);

        return new ListedTheme
        {
            Title = title,
            Description = description,
            Image = image,
            Author = author,
            ButtonParameter = Path.GetFullPath(theme)
        };
    }

    private static string GetThemeImage(string parent, string theme)
    {
        string imagePath = Path.Combine(parent, Path.GetFileNameWithoutExtension(theme) + "-Image.jpeg");
        return File.Exists(imagePath) ? imagePath : "";
    }

    private static (string title, string description, string author, string image) GetThemeMetadata(
        string parent, string theme, string title, string description, string author, string image)
    {
        string dataPath = Path.Combine(parent, Path.GetFileNameWithoutExtension(theme) + "-Data.json");
        if (File.Exists(dataPath))
        {
            string rawJsonData = File.ReadAllText(dataPath);
            JObject JsonData = JObject.Parse(rawJsonData);
            if (JsonData.ContainsKey(TitleKey))
            {
                title = (string)JsonData.GetValue(TitleKey);
            }
            if (JsonData.ContainsKey(DescKey))
            {
                description = (string)JsonData.GetValue(DescKey);
            }
            if (JsonData.ContainsKey(AuthorKey))
            {
                author = (string)JsonData.GetValue(AuthorKey);
            }
            if (JsonData.ContainsKey(ImageKey))
            {
                image = (string)JsonData.GetValue(ImageKey);
            }
        }
        return (
            title ?? string.Empty,
            description ?? string.Empty,
            author ?? string.Empty,
            image ?? string.Empty
        );
    }

    private void LoadTheme_Click(object? sender, RoutedEventArgs e)
    {
        var Output = App.LoadThemeFromString(ThemeInput.Text, true, _savedLocation);
        ThemeOutput.Text = Output;
    }

    // Removed unused ListThemes method (S1144)

    private async void OpenTheme_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Theme",
            FileTypeFilter = filetype,
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Config.ThemesPath)
        });

        if (file.Count == 1)
        {
            await using var stream = await file[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            ThemeInput.Text = await reader.ReadToEndAsync();
            _savedLocation = file[0].TryGetLocalPath();
        }
    }

    private async void SaveTheme_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Theme",
            FileTypeChoices = filetype,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Config.ThemesPath)
        });

        if (file is not null)
        {
            await using var stream = await file.OpenWriteAsync();
            using var streamWriter = new StreamWriter(stream);
            await streamWriter.WriteAsync(ThemeInput.Text);
            _savedLocation = file.TryGetLocalPath(); // set to saved file location
        }
    }

    private async void NewTheme_Click(object? sender, RoutedEventArgs e)
    {
        var lightThemeTextUri = new UriBuilder("avares", typeof(App).Assembly.GetName().Name ?? string.Empty, -1, "Styles/DefaultTheme.txt").Uri;
        var themeText = await new StreamReader(AssetLoader.Open(lightThemeTextUri))
            .ReadToEndAsync();
        ThemeInput.Text = themeText;
        _savedLocation = "";
    }

    private static void ToggleTheme_Click(object? sender, RoutedEventArgs e)
    {
        var darkUri = new UriBuilder("avares", typeof(App).Assembly.GetName().Name ?? string.Empty, -1, "Styles/dark.axaml").Uri.ToString();
        var lightUri = new UriBuilder("avares", typeof(App).Assembly.GetName().Name ?? string.Empty, -1, "Styles/light.axaml").Uri.ToString();
        if (App.CurrentTheme == darkUri)
            App.LoadTheme(lightUri, false);
        else
            App.LoadTheme(darkUri);
    }
    
    private static void ShowPopup_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb || tb.IsChecked is null) return;
        var value = tb.IsChecked.Value;
        Drawing.ShowPopup = value;
        Config.SetEntry("showPopup", value.ToString());
    }

    private static void AltMouseControl_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb || tb.IsChecked is null) return;
        Input.forceUio = tb.IsChecked.Value;
    }

    private static void NoRescanOnIsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb || tb.IsChecked is null) return;
        Drawing.NoRescan = tb.IsChecked.Value;
    }

    private static void LogFile_IsCheckedChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tb || tb.IsChecked is null) return;
        var value = tb.IsChecked.Value;
        Config.SetEntry("logsEnabled", value.ToString());
        Utils.LoggingEnabled = value;
    }

    private static void LocalThemeOnClick(object? sender, RoutedEventArgs e)
    {
        throw new NotImplementedException();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.GetPosition(this).Y <= 20)
            BeginMoveDrag(e);
    }

    [GeneratedRegex("Selector$")]
    private static partial Regex MyRegex();
}