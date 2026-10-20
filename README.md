# Genshin Wish Tracker

## Your wishes, pity, and daily progress at a glance

Keep your Genshin Impact wish history close while you play. Genshin Wish Tracker is a free,
community-made Windows companion with a live wish dashboard, pity counters, an interactive map,
character showcase summaries, and a resin and daily-commission view.

**Want to try it?** Download **`GenshinWishTrackerSetup.exe`** from the
[latest GitHub Release](https://github.com/greyfoxdevelopment/GenshinWishTracker/releases/latest).
Run the installer, accept the license, choose an install location, and it downloads and verifies
the latest app for you. An internet connection is required during installation.

> The installer is published with tagged releases. If there is no installer asset on the latest
> release yet, check back after the next project release.

## Made for your play session

- **Track your wishes and pity.** View Character Event, Weapon Event, and Standard banner counts,
  including 4-star and 5-star pity.
- **Sync your wish history.** Read the game's local web cache and retrieve available history from
  HoYoverse. Your saved history stays on your PC.
- **Plan your next character.** Explore an estimated featured-character probability based on
  primogems, fates, pity, and guarantee state.
- **Check your showcase.** Look up a public character showcase by UID and review displayed stats
  and artifact Crit Value.
- **Keep useful tools together.** Open HoYoLAB's interactive map and view Resin, commissions,
  Realm Currency, and expeditions in the companion dashboard.
- **Fit it around your setup.** Resize the window, move it to another screen, and keep it on top.

## A privacy-conscious companion

The app reads the game's local cache files; it does not interact with the game process. Wish
history and settings are stored locally under `%LOCALAPPDATA%\GenshinWishTracker`. The wish link
is held in memory during syncing and is not written to the history, settings, or logs. Network
requests are made to the services needed for the feature you use, including HoYoverse, HoYoLAB,
Enka, and GitHub for releases and updates.

This is an unofficial community project, not affiliated with or endorsed by HoYoverse.
Genshin Impact is a trademark of HoYoverse (COGNOSPHERE).

## Need help?

See [`instructions.txt`](instructions.txt) for installation, first-time setup, feature guides,
troubleshooting, and developer/release instructions.

## For developers

The app is a Windows x64 WPF application built with .NET 8. Open `src\GenshinWishTracker.csproj`
in Visual Studio or build it with the .NET 8 SDK. See `instructions.txt` for build and release
steps.

Released under the [MIT License](LICENSE).
