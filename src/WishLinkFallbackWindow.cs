using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GenshinWishTracker;

internal sealed class WishLinkFallbackWindow : Window
{
    private const string PowerShellCommand = """
        # Wish link finder - based on MadeBaruna's paimon.moe script (credit: MadeBaruna).
        # Reads the game folder from output_log.txt, takes the newest cache file (data_2) and
        # tests every cached wish link against the API until one works.
        Add-Type -AssemblyName System.Web
        $ErrorActionPreference = 'Stop'

        $targets = @(
            @{ Log = (Join-Path $env:USERPROFILE 'AppData\LocalLow\miHoYo\Genshin Impact\output_log.txt'); ApiHost = 'public-operation-hk4e-sg.hoyoverse.com' },
            @{ Log = (Join-Path $env:USERPROFILE ('AppData\LocalLow\miHoYo\' + [string][char]0x539f + [string][char]0x795e + '\output_log.txt')); ApiHost = 'public-operation-hk4e.mihoyo.com' }
        )
        $target = $targets | Where-Object { Test-Path -LiteralPath $_.Log } | Select-Object -First 1
        if (-not $target) { throw 'Cannot find output_log.txt. Launch the game and open Wish History first.' }
        $apiHost = $target.ApiHost

        $hit = Select-String -LiteralPath $target.Log -Pattern '[A-Za-z]:/(?:(?![A-Za-z]:/).)*?(?:GenshinImpact_Data|YuanShen_Data)' | Select-Object -First 1
        if (-not $hit) { throw 'Cannot find the game folder in output_log.txt. Open Wish History in-game first.' }
        $dataDir = $hit.Matches[0].Value -replace '/', '\'

        $cacheVer = Get-ChildItem -LiteralPath (Join-Path $dataDir 'webCaches') -Directory | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $cacheVer) { throw 'No webCaches folder found. Open Wish History in-game first.' }
        $cacheFile = Join-Path $cacheVer.FullName 'Cache\Cache_Data\data_2'
        $tmpFile = Join-Path $env:TEMP 'gwt_cache_data_2'
        Copy-Item -LiteralPath $cacheFile -Destination $tmpFile -Force

        function Test-WishUrl($url) {
            $ProgressPreference = 'SilentlyContinue'
            $uri = [System.UriBuilder]::New($url)
            $uri.Path = 'gacha_info/api/getGachaLog'
            $uri.Host = $apiHost
            $uri.Fragment = ''
            $params = [System.Web.HttpUtility]::ParseQueryString($uri.Query)
            $params.Set('lang', 'en')
            $params.Set('gacha_type', 301)
            $params.Set('size', '5')
            $uri.Query = $params.ToString()
            $response = Invoke-WebRequest -Uri $uri.Uri.AbsoluteUri -ContentType 'application/json' -UseBasicParsing -TimeoutSec 10 | ConvertFrom-Json
            return ($response.retcode -eq 0)
        }

        $content = Get-Content -Encoding UTF8 -Raw -LiteralPath $tmpFile
        Remove-Item -LiteralPath $tmpFile -ErrorAction SilentlyContinue
        $found = @(($content -split '1/0/') -match 'webview_gacha')

        $wishUrl = $null
        for ($i = $found.Length - 1; $i -ge 0; $i--) {
            $m = [regex]::Match($found[$i], 'https.+?game_biz=')
            if (-not $m.Success) { continue }
            Write-Host "`rChecking link $($found.Length - $i)/$($found.Length)" -NoNewline
            try { if (Test-WishUrl $m.Value) { $wishUrl = $m.Value; break } } catch { }
            Start-Sleep -Seconds 1
        }
        Write-Host ''

        if (-not $wishUrl) { throw 'No working wish link found. Open Wish History in-game, wait for it to load, then run this again.' }

        Set-Clipboard -Value $wishUrl
        Write-Host 'Wish-history link copied to clipboard. Return to Genshin Wish Tracker and click "Use clipboard link".' -ForegroundColor Green
        """;

    private readonly TextBox _commandBox;

    internal string? WishUrl { get; private set; }

    internal WishLinkFallbackWindow()
    {
        Title = "Manual wish link sync";
        Width = 620;
        Height = 450;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x13, 0x1C));
        Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xF2));
        FontFamily = new FontFamily("Segoe UI");

        var layout = new Grid { Margin = new Thickness(18) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var instructions = new TextBlock
        {
            Text = "Open Wish History in-game, then click “Run in PowerShell”. Wait for PowerShell to confirm the link was copied, return here, and choose “Use clipboard link”. You can also copy and run the script yourself. The temporary link is sensitive; do not share it.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(instructions, 0);
        layout.Children.Add(instructions);

        _commandBox = new TextBox
        {
            Text = PowerShellCommand,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x2A)),
            Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE8, 0xF2)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x2D, 0x44)),
            Padding = new Thickness(8)
        };
        Grid.SetRow(_commandBox, 1);
        layout.Children.Add(_commandBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var copyButton = new Button
        {
            Content = "Copy command",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        copyButton.Click += CopyCommand_Click;
        buttons.Children.Add(copyButton);

        var runButton = new Button
        {
            Content = "Run in PowerShell",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        runButton.Click += RunPowerShell_Click;
        buttons.Children.Add(runButton);

        var useLinkButton = new Button
        {
            Content = "Use clipboard link",
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        useLinkButton.Click += UseClipboardLink_Click;
        buttons.Children.Add(useLinkButton);

        var cancelButton = new Button
        {
            Content = "Cancel",
            Padding = new Thickness(12, 6, 12, 6),
            IsCancel = true
        };
        buttons.Children.Add(cancelButton);

        Grid.SetRow(buttons, 2);
        layout.Children.Add(buttons);
        Content = layout;
    }

    private void CopyCommand_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_commandBox.Text);
        }
        catch (ExternalException ex)
        {
            MessageBox.Show(this, $"Could not copy the command: {ex.Message}", Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RunPowerShell_Click(object sender, RoutedEventArgs e)
    {
        string scriptPath = Path.Combine(Path.GetTempPath(), $"GenshinWishTracker-get-link-{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(scriptPath, PowerShellCommand);
            string powershellPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe");
            var startInfo = new ProcessStartInfo(powershellPath)
            {
                UseShellExecute = true
            };
            startInfo.ArgumentList.Add("-NoExit");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);

            if (Process.Start(startInfo) == null)
                throw new Win32Exception("Could not start Windows PowerShell.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            MessageBox.Show(this, $"Could not launch PowerShell: {ex.Message}", Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UseClipboardLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WishUrl = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(WishUrl))
            {
                MessageBox.Show(this, "The clipboard is empty. Run the PowerShell command first.", Title,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
        }
        catch (ExternalException ex)
        {
            MessageBox.Show(this, $"Could not read the clipboard: {ex.Message}", Title,
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
