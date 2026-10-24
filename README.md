# Genshin Companion

## IMPORTANT: USE THE RELEASE INSTALLER TO GET THE CURRENT VERSION

**DOWNLOAD `GenshinCompanionSetup.exe` FROM THE [LATEST GITHUB RELEASE](https://github.com/greyfoxdevelopment/GFDev-Genshin-Companion/releases/latest). DO NOT DOWNLOAD OR RUN AN EXE FROM THE REPOSITORY'S MAIN FILES — THOSE FILES MAY BE OUTDATED (FOR EXAMPLE, VERSION 1.0.1) AND ARE NOT THE PUBLISHED RELEASE.**

**The release installer is the supported way to install or refresh the app. Once installed from a release, the app checks for updates on launch.**

![GitHub Downloads (all assets, latest release)](https://img.shields.io/github/downloads/greyfoxdevelopment/GFDev-Genshin-Companion/latest/total)


[Discord](https://discord.gg/WQknYbRKBt)    [Ko-fi](https://ko-fi.com/greyfoxdevelopment)

## Your wishes, pity, and daily progress at a glance

Keep your Genshin Impact wish history close while you play. Genshin Companion is a free, community-made Windows app with a live wish dashboard, pity counters, an interactive map, embedded character showcases, a changelog of recent updates, and a resin and daily-commission view.

**Want to try it?** Download **`GenshinCompanionSetup.exe`** from the [latest GitHub Release](https://github.com/greyfoxdevelopment/GFDev-Genshin-Companion/releases/latest). Run the installer, accept the license, choose an install location, and it downloads and verifies the latest app for you. An internet connection is required during installation.

> The installer is published with tagged releases. If there is no installer asset on the latest release yet, check back after the next project release.

> ⚠️ **Note on Windows Warnings:** Windows SmartScreen may show a warning for the unsigned installer. Click **"More info"** and **"Run anyway"**, or verify safety via the VirusTotal report below:

> 🛡️ **[View the latest VirusTotal Scan Report - V1.1.2 ](https://www.virustotal.com/gui/file/08f7aa3260f5634d36859e7bba5e675273fe39bae3560273a7f31a20ae99fc6d?nocache=1)**
        **Virus total Link will be updated with each new Release**

## Screenshots

<p align="center">
  <img src="screenshots/wish.png" alt="Wish Tracker Dashboard" width="400"> &nbsp;&nbsp;&nbsp;&nbsp;
  <img src="screenshots/primos.png" alt="Pity and Primogem Calculator" width="400">
</p>
<p align="center">
  <img src="screenshots/resin-daily.png" alt="Resin and Daily Progress" width="400"> &nbsp;&nbsp;&nbsp;&nbsp;
  <img src="screenshots/map.png" alt="Interactive Map Dashboard" width="400">
</p>

## Made for your play session

* **Track your wishes and pity.** View Character Event, Weapon Event, and Standard banner counts, including 4-star and 5-star pity. Sync your history by reading the game's local web cache to retrieve available history from HoYoverse safely.


* **Plan your next character.** Explore an estimated featured-character probability based on primogems, fates, pity, and guarantee state. View a public character showcase by UID in the embedded Enka.Network page, including its character and artifact details.


* **Keep useful tools together.** View real-time player data in your companion dashboard, tracking your current Resin, commissions, Realm Currency, and expedition progress.

* **Stay up to date.** The dashboard shows a changelog built from the project's recent commits, so you can see what changed in each update.

* **Join the community and support the project.** Use the Discord and Ko-fi buttons in the title bar, and see what your support goes toward in the Support tab.

* **Integrated layout features.** Open HoYoLAB's interactive map, and public Enka.Network showcases inside the app. Resize or move the window to fit your play setup, or keep it pinned on top.

## A privacy-conscious companion

The app reads the game's local cache files; it does not interact with the game process. Wish history and settings are stored locally under `%LOCALAPPDATA%\GenshinWishTracker`. The wish link is held in memory during syncing and is not written to the history, settings, or logs. Network requests are made to the services needed for the feature you use, including HoYoverse, HoYoLAB, Enka.Network for public showcase pages, GitHub for releases, updates, and the changelog, and Ko-fi for the support widget (loaded only when you open the Support tab).

This is an unofficial community project, not affiliated with or endorsed by HoYoverse. Genshin Impact is a trademark of HoYoverse (COGNOSPHERE).

## Need help?

See [`instructions.txt`](instructions.txt) for installation, first-time setup, feature guides, troubleshooting, and developer/release instructions.

## For developers

The app is a Windows x64 WPF application built with .NET 8. Open `src\GenshinWishTracker.csproj` in Visual Studio or build it with the .NET 8 SDK. See `instructions.txt` for build and release steps.

Released under the [MIT License](LICENSE).
