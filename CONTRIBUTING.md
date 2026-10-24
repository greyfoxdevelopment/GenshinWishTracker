# Contributing to Genshin Companion 🚀

First off, thank you for taking the time to contribute! This is an unofficial, community-driven project, and open-source contributions are what keep tools like this lightweight, secure, and useful for everyone.

Please take a moment to review these guidelines before getting started.

---

## 🛠️ Our Tech Stack

The application is a Windows x64 desktop companion built using:
- **Framework:** .NET 8 (WPF)
- **Language:** C#
- **UI WebView:** Microsoft WebView2
- **IDE:** Visual Studio 2022 or JetBrains Rider (with .NET 8 SDK installed)

---

## 🗺️ How Can I Contribute?

### 1. Reporting Bugs or Feature Ideas
- Check the **[Issues](https://github.com)** and **[Discussions](https://github.com)** tabs first to make sure it hasn't already been suggested or solved.
- Open a new discussion thread under **💡 Ideas** for feature proposals, or an **🙏 Q&A** thread if you are running into app problems.

### 2. Submitting Code Changes (Pull Requests)
We love pull requests! To ensure a smooth review process, please follow these steps:

1. **Fork the repository** to your own GitHub account.
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com
   ```
3. **Create a descriptive feature branch**:
   ```bash
   git checkout -b feature/amazing-new-ui
   ```
4. **Implement your changes** inside the `src/` directory. Ensure your solution builds cleanly with no compiler warnings.
5. **Commit and push** your branch to your fork.

---

## 🪵 Commit History & Merging Rules

To keep our main release history clean, professional, and easy to trace:
- **Squash Merging Only:** This repository uses **Squash Merging** exclusively. 
- You are free to make as many small micro-commits on your feature branch as you need (e.g., *"fixed typo"*, *"adjusted layout"*). 
- When your Pull Request is accepted, all your commits will be automatically condensed into **one single clean commit** on the `main` branch.

---

## 📜 Privacy & Safety First

Because this is a gaming companion companion app, security is our top priority:
- Do **not** write code that interacts directly with the active Genshin Impact game process (no memory manipulation or hooking).
- All web cache reading must remain **read-only** and local to the user's PC.
- Temporary authorization tokens or game log paths must **never** be written to logs, local configuration files, or settings.

---

Thank you for helping make the game experience smoother for the community! If you have any architectural questions, feel free to start a thread in our GitHub Discussions tab.
