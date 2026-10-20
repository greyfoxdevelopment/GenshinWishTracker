using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using System.Reflection;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Threading;

namespace GenshinWishTracker;

public partial class MainWindow : Window
{
    private const string InteractiveMapUrl = "https://act.hoyolab.com/ys/app/interactive-map/index.html";
    private const string HoyolabHomeUrl = "https://www.hoyolab.com/home";
    private const int PageSize = 20;
    private const int DelayBetweenPagesMs = 350;
    private bool _mapInitializationStarted;
    private bool _hoyolabInitializationStarted;
    private bool _hoyolabRequestRunning;
    private DateTimeOffset _lastHoyolabRequestUtc;
    private DateTimeOffset _lastHoyolabUpdateUtc;
    private HoyolabDailyNote? _dailyNote;
    private readonly DispatcherTimer _resinCountdownTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _buildCooldownTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<string, DateTimeOffset> _buildNextRequestUtcByUid =
        new(StringComparer.Ordinal);

    private static readonly HttpClient Http = CreateHttpClient();

    private static readonly string AppDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenshinWishTracker");
    private static readonly string HistoryPath = Path.Combine(AppDir, "wish_history.json");
    private static readonly string SettingsPath = Path.Combine(AppDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly BannerDef[] Banners =
    {
        new("character", "Character Event", "Limited 5★ · hard pity 90",  "301", 90, 74, Color.FromRgb(0xB7, 0x9C, 0xFF)),
        new("weapon",    "Weapon Event",    "Limited 5★ · hard pity 80",   "302", 80, 63, Color.FromRgb(0x6E, 0xC1, 0xFF)),
        new("standard",  "Standard Wanderlust", "Permanent · hard pity 90", "200", 90, 74, Color.FromRgb(0x8F, 0xE3, 0xA6)),
    };

    private static string BannerKeyOf(string gachaType) => gachaType switch
    {
        "301" or "400" => "character",
        "302" => "weapon",
        "200" => "standard",
        _ => "other"
    };

    private WishStore _store = new();
    private AppSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GenshinWishTracker/1.0");
        return client;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppDir);
        _settings = LoadJson<AppSettings>(SettingsPath) ?? new AppSettings();
        _store = LoadJson<WishStore>(HistoryPath) ?? new WishStore();
        RefreshUi();

        if (_store.Items.Count > 0)
        {
            string when = _store.LastSyncUtc?.ToLocalTime().ToString("g") ?? "never";
            SetStatus($"Loaded {_store.Items.Count} saved wishes · last sync: {when}");
        }
        else
        {
            SetStatus("No history yet. Open Paimon Menu → Wish → History in-game, then click Sync Wishes.");
        }

        UpdatePlanner();
        BuildUidInput.Text = _store.LastUid ?? "";
        _buildCooldownTimer.Tick += BuildCooldownTimer_Tick;
        _resinCountdownTimer.Tick += (_, _) => RenderDailyNote();
        await CheckForUpdatesAsync();
    }

    private async void HoyolabSignInButton_Click(object sender, RoutedEventArgs e)
    {
        HoyolabAuthView.Visibility = Visibility.Visible;
        HoyolabAuthPlaceholder.Visibility = Visibility.Collapsed;
        HoyolabStatusText.Text = "Opening HoYoLAB sign-in…";

        try
        {
            if (!_hoyolabInitializationStarted)
            {
                _hoyolabInitializationStarted = true;
                string profilePath = Path.Combine(AppDir, "WebView2");
                Directory.CreateDirectory(profilePath);
                HoyolabAuthView.CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = profilePath
                };
                await HoyolabAuthView.EnsureCoreWebView2Async();
            }

            HoyolabAuthView.CoreWebView2.Navigate(HoyolabHomeUrl);
            HoyolabStatusText.Text = "Sign in on the HoYoLAB page, then click “Load accounts”.";
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                   or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _hoyolabInitializationStarted = false;
            HoyolabStatusText.Text =
                $"Could not open the HoYoLAB sign-in page. Check that the Microsoft Edge WebView2 Runtime is installed.\n{ex.Message}";
        }
    }

    private async void HoyolabLoadAccountsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!await BeginHoyolabRequestAsync()) return;

        try
        {
            string cookieHeader = await GetHoyolabCookieHeaderAsync();
            IReadOnlyList<HoyolabRole> roles = await HoyolabService.GetRolesAsync(cookieHeader);
            if (roles.Count == 0)
            {
                HoyolabRoleSelector.ItemsSource = null;
                HoyolabStatusText.Text =
                    "No global Genshin accounts were found for this HoYoLAB login. Confirm the correct account is signed in.";
                return;
            }

            HoyolabRoleSelector.ItemsSource = roles;
            HoyolabRoleSelector.DisplayMemberPath = nameof(HoyolabRole.DisplayName);
            HoyolabRoleSelector.SelectedIndex = 0;
            HoyolabStatusText.Text = $"Found {roles.Count} Genshin account(s). Loading real-time notes…";
            await LoadSelectedDailyNoteAsync(cookieHeader);
        }
        catch (HoyolabRequestException ex)
        {
            HoyolabStatusText.Text = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            HoyolabStatusText.Text = $"Could not load HoYoLAB accounts: {ex.Message}";
        }
        finally
        {
            EndHoyolabRequest();
        }
    }

    private async void HoyolabRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (HoyolabRoleSelector.SelectedItem is not HoyolabRole)
        {
            HoyolabStatusText.Text = "Load your HoYoLAB accounts and select a Genshin account first.";
            return;
        }

        if (!await BeginHoyolabRequestAsync()) return;
        try
        {
            string cookieHeader = await GetHoyolabCookieHeaderAsync();
            await LoadSelectedDailyNoteAsync(cookieHeader);
        }
        catch (HoyolabRequestException ex)
        {
            HoyolabStatusText.Text = ex.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            HoyolabStatusText.Text = $"Could not refresh real-time notes: {ex.Message}";
        }
        finally
        {
            EndHoyolabRequest();
        }
    }

    private void HoyolabRoleSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HoyolabRoleSelector.SelectedItem is HoyolabRole role)
            HoyolabStatusText.Text = $"Selected {role.Nickname} · UID {role.GameUid}. Click Refresh notes to update.";
    }

    private Task<bool> BeginHoyolabRequestAsync()
    {
        if (_hoyolabRequestRunning) return Task.FromResult(false);
        TimeSpan sinceLastRequest = DateTimeOffset.UtcNow - _lastHoyolabRequestUtc;
        if (_lastHoyolabRequestUtc != default && sinceLastRequest < TimeSpan.FromSeconds(10))
        {
            int wait = (int)Math.Ceiling((TimeSpan.FromSeconds(10) - sinceLastRequest).TotalSeconds);
            HoyolabStatusText.Text = $"Please wait {wait} second(s) before requesting HoYoLAB again.";
            return Task.FromResult(false);
        }

        _hoyolabRequestRunning = true;
        _lastHoyolabRequestUtc = DateTimeOffset.UtcNow;
        HoyolabLoadAccountsButton.IsEnabled = false;
        HoyolabRefreshButton.IsEnabled = false;
        HoyolabStatusText.Text = "Contacting HoYoLAB…";
        return Task.FromResult(true);
    }

    private void EndHoyolabRequest()
    {
        _hoyolabRequestRunning = false;
        HoyolabLoadAccountsButton.IsEnabled = true;
        HoyolabRefreshButton.IsEnabled = true;
    }

    private async Task<string> GetHoyolabCookieHeaderAsync()
    {
        if (!_hoyolabInitializationStarted || HoyolabAuthView.CoreWebView2 == null)
            throw new HoyolabRequestException("Click Sign in to HoYoLAB and sign in before loading accounts.");

        string[] cookieScopes =
        [
            "https://www.hoyolab.com/",
            "https://api-account-os.hoyolab.com/",
            "https://bbs-api-os.hoyolab.com/"
        ];
        string[] permittedCookieNames =
        [
            "ltoken", "ltoken_v2", "ltuid", "ltuid_v2",
            "cookie_token", "cookie_token_v2", "account_id", "account_id_v2"
        ];
        var cookiesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string scope in cookieScopes)
        {
            IReadOnlyList<CoreWebView2Cookie> cookies =
                await HoyolabAuthView.CoreWebView2.CookieManager.GetCookiesAsync(scope);
            foreach (CoreWebView2Cookie cookie in cookies)
            {
                if (permittedCookieNames.Contains(cookie.Name, StringComparer.OrdinalIgnoreCase) &&
                    !cookiesByName.ContainsKey(cookie.Name))
                    cookiesByName.Add(cookie.Name, cookie.Value);
            }
        }

        if (!cookiesByName.ContainsKey("ltoken") && !cookiesByName.ContainsKey("ltoken_v2"))
            throw new HoyolabRequestException("No HoYoLAB login was found. Sign in in the embedded page and retry.");

        return string.Join("; ", cookiesByName.Select(cookie => $"{cookie.Key}={cookie.Value}"));
    }

    private async Task LoadSelectedDailyNoteAsync(string cookieHeader)
    {
        if (HoyolabRoleSelector.SelectedItem is not HoyolabRole role)
        {
            HoyolabStatusText.Text = "Select a Genshin account before refreshing notes.";
            return;
        }

        _dailyNote = await HoyolabService.GetDailyNoteAsync(cookieHeader, role);
        _lastHoyolabUpdateUtc = DateTimeOffset.UtcNow;
        RenderDailyNote();
        _resinCountdownTimer.Start();
        HoyolabStatusText.Text = $"Updated {DateTime.Now.ToString("t")} · {role.Nickname} · UID {role.GameUid}.";
    }

    private void RenderDailyNote()
    {
        if (_dailyNote is not HoyolabDailyNote note) return;

        ResinAmountText.Text = $"{note.CurrentResin} / {note.MaxResin}";
        ResinRecoveryText.Text = note.CurrentResin >= note.MaxResin
            ? "At maximum"
            : $"Full in {FormatRemaining(note.ResinRecoveryTime)}";

        CommissionsText.Text = $"{note.FinishedTaskNum} / {note.TotalTaskNum}";
        CommissionRewardText.Text = note.IsExtraTaskRewardReceived
            ? "Completion reward claimed"
            : note.FinishedTaskNum >= note.TotalTaskNum
                ? "Claim the completion reward"
                : $"{Math.Max(0, note.TotalTaskNum - note.FinishedTaskNum)} commission(s) remaining";

        RealmCurrencyText.Text = $"{note.CurrentHomeCoin:N0} / {note.MaxHomeCoin:N0}";
        RealmCurrencyRecoveryText.Text = note.CurrentHomeCoin >= note.MaxHomeCoin
            ? "At maximum"
            : $"Full in {FormatRemaining(note.HomeCoinRecoveryTime)}";

        ExpeditionsText.Text = $"{note.CurrentExpeditionNum} / {note.MaxExpeditionNum}";
        IReadOnlyList<HoyolabExpedition> expeditions = note.Expeditions ?? [];
        int? nextExpedition = expeditions.Count == 0
            ? null
            : expeditions.Min(expedition => expedition.RemainedTime);
        ExpeditionsRecoveryText.Text = nextExpedition is null
            ? "No expeditions running"
            : nextExpedition <= 0
                ? "Expedition(s) complete"
                : $"Next complete in {FormatRemaining(nextExpedition.Value)}";
    }

    private string FormatRemaining(int seconds)
    {
        int elapsed = (int)Math.Max(0, (DateTimeOffset.UtcNow - _lastHoyolabUpdateUtc).TotalSeconds);
        int remaining = Math.Max(0, seconds - elapsed);
        TimeSpan time = TimeSpan.FromSeconds(remaining);
        return time.TotalDays >= 1
            ? $"{(int)time.TotalDays}d {time.Hours}h {time.Minutes}m"
            : time.TotalHours >= 1
                ? $"{(int)time.TotalHours}h {time.Minutes}m"
                : $"{time.Minutes}m";
    }

    private async void BuildLookupButton_Click(object sender, RoutedEventArgs e) =>
        await LoadBuildShowcaseAsync(BuildUidInput.Text);

    private async void UseSavedUid_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_store.LastUid))
        {
            BuildStatusText.Text = "No saved wish UID is available. Enter your public in-game UID.";
            return;
        }

        BuildUidInput.Text = _store.LastUid;
        await LoadBuildShowcaseAsync(_store.LastUid);
    }

    private async void BuildUidInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await LoadBuildShowcaseAsync(BuildUidInput.Text);
    }

    private async Task LoadBuildShowcaseAsync(string input)
    {
        string uid = input.Trim();
        if (uid.Length != 9 || !uid.All(char.IsAsciiDigit))
        {
            BuildStatusText.Text = "Enter a valid 9-digit Genshin UID.";
            BuildCharactersHost.ItemsSource = null;
            return;
        }

        if (_buildNextRequestUtcByUid.TryGetValue(uid, out DateTimeOffset nextRequest) &&
            nextRequest > DateTimeOffset.UtcNow)
        {
            BuildStatusText.Text = $"Please wait before requesting this UID again ({Math.Ceiling((nextRequest - DateTimeOffset.UtcNow).TotalSeconds)}s).";
            return;
        }

        BuildLookupButton.IsEnabled = false;
        BuildStatusText.Text = $"Looking up public showcase for UID {uid}…";
        BuildCharactersHost.ItemsSource = null;
        try
        {
            EnkaShowcase showcase = await EnkaService.GetShowcaseAsync(uid, AppDir);
            _buildNextRequestUtcByUid[uid] = DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, showcase.CacheSeconds));
            BuildStatusText.Text = showcase.Characters.Count == 0
                ? $"{showcase.Nickname} (AR {showcase.AdventureRank}) · UID {uid} has no public showcase characters. Enable Character Showcase visibility in-game and add characters."
                : $"{showcase.Nickname} (AR {showcase.AdventureRank}) · UID {uid} · {showcase.Characters.Count} showcased character(s). Cached for {showcase.CacheSeconds}s.";
            BuildCharactersHost.ItemsSource = showcase.Characters;
            _buildCooldownTimer.Start();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                   or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            BuildStatusText.Text = ex.Message;
            int cooldown = ex is EnkaRequestException enkaError
                ? enkaError.RetryAfterSeconds
                : 15;
            _buildNextRequestUtcByUid[uid] = DateTimeOffset.UtcNow.AddSeconds(cooldown);
            _buildCooldownTimer.Start();
        }
        finally
        {
            BuildLookupButton.IsEnabled = true;
        }
    }

    private void BuildCooldownTimer_Tick(object? sender, EventArgs e)
    {
        foreach (string uid in _buildNextRequestUtcByUid
                     .Where(pair => pair.Value <= DateTimeOffset.UtcNow)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _buildNextRequestUtcByUid.Remove(uid);
        }
        if (_buildNextRequestUtcByUid.Count == 0) _buildCooldownTimer.Stop();
    }

    private void OpenWishTracker_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = WishTrackerTab;
    private void OpenResin_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = ResinTab;
    private void OpenBuilds_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = BuildEvaluatorTab;
    private void OpenPlanner_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedItem = PrimogemPlannerTab;

    private void PlannerInput_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded) UpdatePlanner();
    }

    private void PlannerInput_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) UpdatePlanner();
    }

    private void UpdatePlanner()
    {
        if (PlannerPrimogemsSlider == null) return;

        int primogems = (int)Math.Round(PlannerPrimogemsSlider.Value);
        int fates = (int)Math.Round(PlannerFatesSlider.Value);
        int pity = (int)Math.Round(PlannerPitySlider.Value);
        bool guaranteed = PlannerGuaranteedCheckBox.IsChecked == true;
        int wishes = primogems / 160 + fates;
        double chance = CalculateFeaturedChance(wishes, pity, guaranteed, targetCopies: 1);
        int worstCaseWishes = guaranteed ? 90 - pity : 180 - pity;

        PlannerPrimogemsValue.Text = $"{primogems:N0} Primogems";
        PlannerFatesValue.Text = $"{fates} Fates";
        PlannerPityValue.Text = $"{pity} / 90";
        PlannerAvailableWishes.Text = wishes.ToString();
        PlannerChance.Text = $"{chance:P1}";
        PlannerChanceBar.Value = chance * 100;
        PlannerGuaranteePulls.Text = worstCaseWishes.ToString();
        DashboardWishSummary.Text = _store.Items.Count == 0
            ? "No wish history loaded"
            : $"{_store.Items.Count:N0} wishes saved · UID {_store.LastUid ?? "—"}";
    }

    private static double CalculateFeaturedChance(int availableWishes, int currentPity, bool guaranteed, int targetCopies)
    {
        const int hardPity = 90;
        int remainingTargets = Math.Max(1, targetCopies);
        var states = new double[remainingTargets, hardPity, 2];
        states[0, currentPity, guaranteed ? 1 : 0] = 1;

        for (int wish = 0; wish < availableWishes; wish++)
        {
            var next = new double[remainingTargets, hardPity, 2];
            for (int obtained = 0; obtained < remainingTargets; obtained++)
            {
                for (int pity = 0; pity < hardPity; pity++)
                {
                    for (int isGuaranteed = 0; isGuaranteed <= 1; isGuaranteed++)
                    {
                        double stateChance = states[obtained, pity, isGuaranteed];
                        if (stateChance == 0) continue;

                        int nextPity = Math.Min(pity + 1, hardPity - 1);
                        double fiveStarChance = GetFiveStarChance(pity + 1);
                        next[obtained, nextPity, isGuaranteed] += stateChance * (1 - fiveStarChance);

                        if (isGuaranteed == 1)
                        {
                            if (obtained + 1 < remainingTargets)
                                next[obtained + 1, 0, 0] += stateChance * fiveStarChance;
                        }
                        else
                        {
                            if (obtained + 1 < remainingTargets)
                                next[obtained + 1, 0, 0] += stateChance * fiveStarChance * 0.5;
                            next[obtained, 0, 1] += stateChance * fiveStarChance * 0.5;
                        }
                    }
                }
            }
            states = next;
        }

        double noTargetChance = 0;
        for (int obtained = 0; obtained < remainingTargets; obtained++)
            for (int pity = 0; pity < hardPity; pity++)
                for (int guarantee = 0; guarantee <= 1; guarantee++)
                    noTargetChance += states[obtained, pity, guarantee];

        return Math.Clamp(1 - noTargetChance, 0, 1);
    }

    private static double GetFiveStarChance(int pullNumber)
    {
        if (pullNumber >= 90) return 1;
        if (pullNumber < 74) return 0.006;
        return Math.Min(1, 0.006 + (pullNumber - 73) * 0.06);
    }

    private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender) || MainTabs.SelectedItem != InteractiveMapTab) return;
        if (_mapInitializationStarted) return;
        _mapInitializationStarted = true;
        MapStatusPanel.Visibility = Visibility.Visible;
        MapStatusText.Text = "Initializing the embedded map…";

        try
        {
            string profilePath = Path.Combine(AppDir, "WebView2");
            Directory.CreateDirectory(profilePath);
            InteractiveMap.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = profilePath
            };

            await InteractiveMap.EnsureCoreWebView2Async();
            InteractiveMap.CoreWebView2.NavigationCompleted += InteractiveMap_NavigationCompleted;
            MapStatusText.Text = "Loading HoYoLAB Interactive Map…";
            InteractiveMap.CoreWebView2.Navigate(InteractiveMapUrl);
        }
        catch (Exception ex)
        {
            MapStatusText.Text =
                $"Could not initialize the embedded map. Install the Microsoft Edge WebView2 Runtime or open the map in your browser.\n\n{ex.Message}";
            MapOpenBrowserButton.Visibility = Visibility.Visible;
        }
    }

    private void InteractiveMap_NavigationCompleted(
        object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            MapStatusPanel.Visibility = Visibility.Collapsed;
            return;
        }

        MapStatusText.Text =
            $"The embedded map could not finish loading ({e.WebErrorStatus}). Check your connection or open it in your browser.";
        MapOpenBrowserButton.Visibility = Visibility.Visible;
    }

    private void MapOpenBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(InteractiveMapUrl) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            MessageBox.Show(this, $"Could not open the map in your browser: {ex.Message}",
                "Interactive map", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        string? executablePath = Environment.ProcessPath;
        if (executablePath == null ||
            !string.Equals(Path.GetFileNameWithoutExtension(executablePath), "GenshinWishTracker", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version
                ?? throw new InvalidDataException("The application version is unavailable.");
            AvailableUpdate? update = await UpdateService.GetAvailableUpdateAsync(currentVersion);
            if (update == null) return;

            MessageBoxResult result = MessageBox.Show(
                this,
                $"Version {update.Tag} is available (you have {currentVersion}). Download and install it now?",
                "Genshin Wish Tracker update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes) return;

            SetStatus($"Downloading and verifying version {update.Tag}…");
            string downloadedPath = await UpdateService.DownloadAsync(update);
            UpdateService.InstallWhenClosed(downloadedPath, Environment.ProcessId, executablePath);
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                   or InvalidDataException or IOException or UnauthorizedAccessException
                                   or Win32Exception)
        {
            SetStatus($"Update check failed: {ex.Message}", isError: true);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            e.Handled = true;
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            Point cursor = PointToScreen(e.GetPosition(this));
            WindowState = WindowState.Normal;
            Left = cursor.X - ActualWidth / 2;
            Top = cursor.Y - 14;
        }

        DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        WindowFrame.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(16);
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        PinButton.Foreground = Topmost ? (Brush)FindResource("GoldBrush") : new SolidColorBrush(Color.FromRgb(0xB8, 0xBC, 0xD0));
        PinButton.ToolTip = Topmost ? "Always on top: ON" : "Keep on top";
    }

    private void FolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select the 'Genshin Impact Game' folder (the one containing the *_Data folder)" };
        if (dlg.ShowDialog(this) == true)
        {
            _settings.GameFolder = dlg.FolderName;
            SaveJson(SettingsPath, _settings);
            SetStatus($"Game folder saved: {dlg.FolderName}");
        }
    }

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        SyncButton.IsEnabled = false;
        try
        {
            await SyncAsync();
        }
        catch (AuthKeyExpiredException)
        {
            SetStatus("Authkey expired. In-game: Paimon Menu → Wish → History, then click Sync again.", isError: true);
            await TryManualLinkSyncAsync();
        }
        catch (CacheNotFoundException ex)
        {
            SetStatus(ex.Message, isError: true);
            await TryManualLinkSyncAsync();
        }
        catch (ApiException ex)
        {
            SetStatus(ex.Message, isError: true);
            await TryManualLinkSyncAsync();
        }
        catch (TaskCanceledException)
        {
            SetStatus("The request timed out. Check your connection and try again.", isError: true);
            await TryManualLinkSyncAsync();
        }
        catch (HttpRequestException ex)
        {
            SetStatus($"Network error: {ex.Message}", isError: true);
            await TryManualLinkSyncAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Unexpected error: {ex.Message}", isError: true);
            await TryManualLinkSyncAsync();
        }
        finally
        {
            RefreshUi();
            SyncButton.IsEnabled = true;
        }
    }

    private async Task TryManualLinkSyncAsync()
    {
        var dialog = new WishLinkFallbackWindow { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.WishUrl)) return;

        try
        {
            string apiUrl = NormalizeManualWishUrl(dialog.WishUrl);
            SetStatus("Syncing from the wish link copied by PowerShell…");
            await SyncAsync(apiUrl);
        }
        catch (AuthKeyExpiredException)
        {
            SetStatus("The copied wish link has expired. Open Wish → History in-game, run the command again, and retry.", isError: true);
        }
        catch (ApiException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        catch (TaskCanceledException)
        {
            SetStatus("The request timed out. Check your connection and try again.", isError: true);
        }
        catch (HttpRequestException ex)
        {
            SetStatus($"Network error: {ex.Message}", isError: true);
        }
        catch (InvalidDataException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Unexpected error: {ex.Message}", isError: true);
        }
    }

    private async Task SyncAsync(string? suppliedApiUrl = null)
    {
        SetStatus("Step 1/3 · Scanning the game's web cache…");
        string apiUrl = suppliedApiUrl ?? await FindWishApiUrlAsync();

        var template = ParseQuery(apiUrl);
        string baseUrl = apiUrl[..apiUrl.IndexOf('?')];

        var known = new HashSet<string>(_store.Items.Select(i => i.Key));
        int totalAdded = 0;

        foreach (var banner in Banners)
        {
            SetStatus($"Step 2/3 · Fetching {banner.Title} history…");
            FetchResult result = await FetchBannerAsync(baseUrl, template, banner, known);

            foreach (var item in result.NewItems)
            {
                if (known.Add(item.Key))
                {
                    _store.Items.Add(item);
                    totalAdded++;
                }
            }

            if (result.Uid != null) _store.LastUid = result.Uid;
            _store.LastSyncUtc = DateTime.UtcNow;
            _store.Items.Sort(CompareItems);

            SaveJson(HistoryPath, _store);
        }

        SetStatus($"Step 3/3 · Done — {totalAdded} new wish(es) · {_store.Items.Count} stored in total.");
    }

    // =====================================================================
    // STEP 3 — Find a WORKING wish link (same strategy as MadeBaruna's paimon.moe script)
    //   1. Game folder  : read from output_log.txt (works for HoYoPlay / custom installs)
    //   2. Cache file   : newest webCaches\<ver>\Cache\Cache_Data\data_2 (then the other data_* files)
    //   3. Candidates   : split on "1/0/", keep chunks containing "webview_gacha",
    //                     take "https...game_biz=" from each, newest first
    //   4. Validate     : call the real API with every candidate until one returns retcode 0
    // The cache holds MANY old/expired links, so "take the last one" is not good enough.
    // =====================================================================

    private static readonly Regex HostRegex =
        new(@"^https://([A-Za-z0-9\-]+\.)*(hoyoverse|mihoyo)\.com/", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Drive-letter path ending in the game's data folder; the tempered dot stops it swallowing a second path.
    private static readonly Regex GameDirRegex = new(
        @"[A-Za-z]:[/\\](?:(?![A-Za-z]:[/\\]).)*?(?:GenshinImpact_Data|YuanShen_Data)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WishLinkRegex = new(@"https.+?game_biz=", RegexOptions.Compiled);

    private static readonly string[] DataFolderNames = { "GenshinImpact_Data", "YuanShen_Data" };
    private const int MaxLinkCandidates = 40;

    // Diagnostics (paths, counts, retcodes — never links or authkeys) saved to sync_log.txt
    private static readonly string SyncLogPath = Path.Combine(AppDir, "sync_log.txt");
    private readonly List<string> _syncLog = new();
    private void Diag(string line) => _syncLog.Add($"{DateTime.Now:HH:mm:ss}  {line}");

    private void WriteSyncLog()
    {
        try
        {
            Directory.CreateDirectory(AppDir);
            File.WriteAllLines(SyncLogPath, _syncLog);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task<string> FindWishApiUrlAsync()
    {
        _syncLog.Clear();
        try
        {
            // File scanning runs off the UI thread; API validation is async on the UI context.
            List<string> candidates = await Task.Run(CollectWishLinkCandidates);
            Diag($"{candidates.Count} wish link candidate(s) found in cache");

            if (candidates.Count == 0)
            {
                throw new CacheNotFoundException(
                    "No wish link in the game cache. In-game open Paimon Menu → Wish → History, wait for it to load, then Sync. " +
                    "(Details: %LocalAppData%\\GenshinWishTracker\\sync_log.txt)");
            }

            bool sawExpired = false;
            int tested = 0;
            foreach (string candidate in candidates)
            {
                tested++;
                SetStatus($"Step 1/3 · Checking wish link {tested}/{candidates.Count}…");

                int? retcode = await TestWishLinkAsync(candidate);
                Diag($"candidate {tested}: retcode {(retcode?.ToString() ?? "request failed")}");

                if (retcode == 0) return NormalizeWishApiUrl(candidate);
                if (retcode is -100 or -101) sawExpired = true;

                await Task.Delay(500);   // be gentle with the API while probing old links
            }

            if (sawExpired) throw new AuthKeyExpiredException();

            throw new CacheNotFoundException(
                $"Found {candidates.Count} link(s) in the cache but the API rejected all of them. " +
                "Reopen Wish History in-game and retry. (Details: %LocalAppData%\\GenshinWishTracker\\sync_log.txt)");
        }
        finally
        {
            WriteSyncLog();
        }
    }

    /// <summary>Returns the API retcode for a candidate link, or null if the request itself failed.</summary>
    private static async Task<int?> TestWishLinkAsync(string candidate)
    {
        try
        {
            string api = NormalizeWishApiUrl(candidate);
            var template = ParseQuery(api);
            string baseUrl = api[..api.IndexOf('?')];
            string url = BuildPageUrl(baseUrl, template, "301", 1, "0");
            GachaApiResponse resp = await GetPageAsync(url);
            return resp.Retcode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                      or JsonException or ApiException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>Reads the newest cache files and returns every plausible wish link, newest first.</summary>
    private List<string> CollectWishLinkCandidates()
    {
        List<string> cacheFiles = FindCacheFiles();
        if (cacheFiles.Count == 0)
        {
            throw new CacheNotFoundException(
                "Couldn't find the game's webCaches folder. Click the folder button and pick your game folder " +
                "(the one containing GenshinImpact_Data or YuanShen_Data). (Details: %LocalAppData%\\GenshinWishTracker\\sync_log.txt)");
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string file in cacheFiles)
        {
            string text;
            try
            {
                // FileShare.ReadWrite|Delete so this works while the game is running.
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                fs.CopyTo(ms);
                text = Encoding.Latin1.GetString(ms.ToArray());   // 1 byte = 1 char, binary-safe
            }
            catch (IOException ex) { Diag($"cannot read {file}: {ex.Message}"); continue; }
            catch (UnauthorizedAccessException ex) { Diag($"no access to {file}: {ex.Message}"); continue; }

            string[] chunks = text.Split("1/0/");
            int before = result.Count;

            for (int i = chunks.Length - 1; i >= 0 && result.Count < MaxLinkCandidates; i--)
            {
                string chunk = chunks[i];
                if (!chunk.Contains("webview_gacha", StringComparison.Ordinal)) continue;

                Match m = WishLinkRegex.Match(chunk);
                if (!m.Success || !m.Value.Contains('?')) continue;

                if (seen.Add(m.Value)) result.Add(m.Value);
            }

            Diag($"{file}: {result.Count - before} candidate(s)");
            if (result.Count >= MaxLinkCandidates) break;
        }

        return result;
    }

    /// <summary>webCaches\(two newest version folders)\Cache\Cache_Data\ — data_2 first, then other data_* files.</summary>
    private List<string> FindCacheFiles()
    {
        var files = new List<string>();

        foreach (string dataDir in FindGameDataDirs())
        {
            string webCaches = Path.Combine(dataDir, "webCaches");
            if (!Directory.Exists(webCaches))
            {
                Diag($"no webCaches folder in {dataDir}");
                continue;
            }

            try
            {
                var versions = new DirectoryInfo(webCaches).GetDirectories()
                    .OrderByDescending(d => d.LastWriteTimeUtc)
                    .Take(2);

                foreach (DirectoryInfo version in versions)
                {
                    string cacheData = Path.Combine(version.FullName, "Cache", "Cache_Data");
                    if (!Directory.Exists(cacheData)) continue;

                    var dataFiles = new DirectoryInfo(cacheData).GetFiles("data_*")
                        .OrderByDescending(f => f.Name.Equals("data_2", StringComparison.OrdinalIgnoreCase))
                        .ThenByDescending(f => f.LastWriteTimeUtc);

                    foreach (FileInfo f in dataFiles) files.Add(f.FullName);
                }
            }
            catch (IOException ex) { Diag($"cannot list {webCaches}: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { Diag($"no access to {webCaches}: {ex.Message}"); }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>All existing GenshinImpact_Data / YuanShen_Data folders: user choice → output_log.txt → default paths.</summary>
    private List<string> FindGameDataDirs()
    {
        var dirs = new List<string>();

        void AddRoot(string? root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            try
            {
                string trimmed = root.TrimEnd('\\', '/');
                if (DataFolderNames.Contains(Path.GetFileName(trimmed), StringComparer.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(trimmed)) dirs.Add(trimmed);   // the *_Data folder itself
                    return;
                }
                foreach (string name in DataFolderNames)
                {
                    string a = Path.Combine(trimmed, name);
                    string b = Path.Combine(trimmed, "Genshin Impact Game", name);
                    if (Directory.Exists(a)) dirs.Add(a);
                    if (Directory.Exists(b)) dirs.Add(b);
                }
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
        }

        // 1) Folder the user chose with the folder button
        AddRoot(_settings.GameFolder);

        // 2) The game writes its own install path into output_log.txt (this is what paimon.moe's script does)
        string lowLevel = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "miHoYo");
        string[] logs =
        {
            Path.Combine(lowLevel, "Genshin Impact", "output_log.txt"),
            Path.Combine(lowLevel, "\u539f\u795e", "output_log.txt")          // China client
        };

        foreach (string log in logs)
        {
            if (!File.Exists(log)) { Diag($"log not found: {log}"); continue; }
            try
            {
                using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.Contains("_Data", StringComparison.OrdinalIgnoreCase)) continue;
                    Match m = GameDirRegex.Match(line);
                    if (!m.Success) continue;

                    string dir = m.Value.Replace('/', '\\');
                    if (Directory.Exists(dir))
                    {
                        dirs.Add(dir);
                        Diag($"game folder from output_log.txt: {dir}");
                        break;
                    }
                }
            }
            catch (IOException ex) { Diag($"cannot read {log}: {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { Diag($"no access to {log}: {ex.Message}"); }
        }

        // 3) Common install locations, including the newer HoYoPlay launcher layout
        var roots = new List<string>();
        foreach (var special in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            string pf = Environment.GetFolderPath(special);
            if (string.IsNullOrEmpty(pf)) continue;
            roots.Add(Path.Combine(pf, "HoYoPlay", "games", "Genshin Impact game"));
            roots.Add(Path.Combine(pf, "Genshin Impact", "Genshin Impact Game"));
        }
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            string r = drive.RootDirectory.FullName;
            roots.Add(Path.Combine(r, "HoYoPlay", "games", "Genshin Impact game"));
            roots.Add(Path.Combine(r, "Program Files", "HoYoPlay", "games", "Genshin Impact game"));
            roots.Add(Path.Combine(r, "Games", "HoYoPlay", "games", "Genshin Impact game"));
            roots.Add(Path.Combine(r, "Genshin Impact", "Genshin Impact Game"));
            roots.Add(Path.Combine(r, "Program Files", "Genshin Impact", "Genshin Impact Game"));
            roots.Add(Path.Combine(r, "Games", "Genshin Impact", "Genshin Impact Game"));
        }
        foreach (string root in roots) AddRoot(root);

        var distinct = dirs.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 0) Diag("no game data folder found anywhere");
        return distinct;
    }

    private static string NormalizeWishApiUrl(string url)
    {
        var builder = new UriBuilder(url)
        {
            Host = url.Contains("mihoyo.com", StringComparison.OrdinalIgnoreCase)
                ? "public-operation-hk4e.mihoyo.com"
                : "public-operation-hk4e-sg.hoyoverse.com",
            Path = "/gacha_info/api/getGachaLog",
            Fragment = ""
        };

        return builder.Uri.AbsoluteUri;
    }

    private static string NormalizeManualWishUrl(string url)
    {
        string candidate = url.Trim();
        if (!HostRegex.IsMatch(candidate) ||
            !candidate.Contains("authkey=", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException(
                "The clipboard does not contain a valid Genshin wish-history link. Run the PowerShell command first.");
        }

        return NormalizeWishApiUrl(candidate);
    }
    // =====================================================================
    // STEP 4 — API fetching with end_id pagination
    // =====================================================================
    private sealed record FetchResult(List<WishItem> NewItems, string? Uid);

    private async Task<FetchResult> FetchBannerAsync(
        string baseUrl,
        List<KeyValuePair<string, string>> template,
        BannerDef banner,
        HashSet<string> known)
    {
        var newItems = new List<WishItem>();
        string? uid = null;
        string endId = "0";
        int page = 1;
        while (true)
        {
            string url = BuildPageUrl(baseUrl, template, banner.FetchType, page, endId);
            GachaApiResponse resp = await GetPageAsync(url);
            if (resp.Retcode == -100 || resp.Retcode == -101) throw new AuthKeyExpiredException();
            if (resp.Retcode != 0) throw new ApiException($"API error {resp.Retcode}: {resp.Message}");
            var list = resp.Data?.List ?? new List<WishItem>();
            if (list.Count == 0) break;
            uid ??= list[0].Uid;
            bool reachedKnown = false;
            foreach (var item in list)
            {
                if (known.Contains(item.Key)) reachedKnown = true;
                else newItems.Add(item);
            }
            SetStatus($"Step 2/3 · {banner.Title} · page {page} · {newItems.Count} new");
            if (reachedKnown || list.Count < PageSize) break;
            endId = list[^1].Id;
            page++;
            await Task.Delay(DelayBetweenPagesMs);
        }
        return new FetchResult(newItems, uid);
    }

    private static string BuildPageUrl(
        string baseUrl,
        List<KeyValuePair<string, string>> template,
        string gachaType,
        int page,
        string endId)
    {
        var parts = new List<string>();
        foreach (var kv in template)
        {
            if (kv.Key is "gacha_type" or "page" or "size" or "end_id") continue;
            parts.Add($"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
        }
        parts.Add($"gacha_type={gachaType}");
        parts.Add($"page={page}");
        parts.Add($"size={PageSize}");
        parts.Add($"end_id={endId}");
        return baseUrl + "?" + string.Join("&", parts);
    }
    private static async Task<GachaApiResponse> GetPageAsync(string url)
    {
        for (int attempt = 0; ; attempt++)
        {
            using HttpResponseMessage http = await Http.GetAsync(url);
            http.EnsureSuccessStatusCode();
            string json = await http.Content.ReadAsStringAsync();
            var resp = JsonSerializer.Deserialize<GachaApiResponse>(json, JsonOpts)
            ?? throw new ApiException("The server returned an empty response.");
            if (resp.Retcode == -110 && attempt < 3)
            {
                await Task.Delay(2000 * (attempt + 1));
                continue;
            }
            return resp;
        }
    }

    private static List<KeyValuePair<string, string>> ParseQuery(string url)
    {
        var list = new List<KeyValuePair<string, string>>();
        string query = url[(url.IndexOf('?') + 1)..];
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            string key = eq < 0 ? pair : pair[..eq];
            string value = eq < 0 ? "" : pair[(eq + 1)..];
            list.Add(new(Uri.UnescapeDataString(key), Uri.UnescapeDataString(value)));
        }
        return list;
    }

    // =====================================================================
    // STEP 5 — Local JSON storage
    // =====================================================================
    private static T? LoadJson<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts);
        }
        catch (JsonException)
        {
            try { File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}"); } catch { }
            return null;
        }
    }
    private static void SaveJson<T>(string path, T value)
    {
        Directory.CreateDirectory(AppDir);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private static ulong ParseId(string id) => ulong.TryParse(id, out var v) ? v : 0;

    private static int CompareItems(WishItem a, WishItem b)
    {
        int c = string.CompareOrdinal(a.Uid, b.Uid);
        return c != 0 ? c : ParseId(a.Id).CompareTo(ParseId(b.Id));
    }
    // =====================================================================
    // STEP 6 — Pity maths + dashboard refresh
    // =====================================================================
    private void RefreshUi()
    {
        string? uid = _store.LastUid;
        var mine = _store.Items.Where(i => i.Uid == uid).ToList();
        CardsHost.ItemsSource = Banners
        .Select(b => ComputeStats(b, mine.Where(i => BannerKeyOf(i.GachaType) == b.Key)))
        .ToList();
        UidText.Text = uid == null ? "UID —" : $"UID {uid}";
        UpdatePlanner();
    }
    private static BannerStats ComputeStats(BannerDef def, IEnumerable<WishItem> items)
    {
        var ordered = items.OrderBy(i => ParseId(i.Id)).ToList();
        int pity = 0, fourPity = 0, fiveCount = 0;
        string lastFive = "—";
        foreach (var wish in ordered)
        {
            pity++;
            fourPity++;
            if (wish.RankType == "5")
            {
                fiveCount++;
                lastFive = $"{wish.Name}  ({pity} pulls)";
                pity = 0;
            }
            else if (wish.RankType == "4")
            {
                fourPity = 0;
            }
        }
        var accent = new SolidColorBrush(def.Accent);
        accent.Freeze();
        return new BannerStats
        {
            Title = def.Title,
            Subtitle = def.Subtitle,
            Accent = accent,
            Pity = pity,
            HardPity = def.HardPity,
            SoftPity = def.SoftPity,
            FourStarPity = Math.Min(fourPity, 10),
            TotalPulls = ordered.Count,
            FiveStarCount = fiveCount,
            LastFiveStar = lastFive
        };
    }
    private void SetStatus(string text, bool isError = false)
    {
        StatusText.Text = text;
        StatusText.Foreground = (Brush)FindResource(isError ? "ErrorBrush" : "MutedBrush");
        StatusText.ToolTip = text;
    }
}