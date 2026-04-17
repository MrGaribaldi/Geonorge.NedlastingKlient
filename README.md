# Geonorge.NedlastingKlient

This project provides a client software for downloading dataset's published through Geonorge's Atom Feed. It also includes feeds from NGU, NIBIO, Norwegian Environment Agency and elevation data. The general idea is to provided a tool to synchronize dataset on a regular basis. 

It includes a desktop application for browsing and selecting files you want to download. A console application is provided to perform downloads. This console application can be scheduled to run through Scheduled tasks on windows, or cron on *nix platforms.

It also includes a cross-platform terminal UI (`Geonorge.MassivNedlasting.TerminalUi`) for Linux/macOS/Windows that can browse datasets and edit download selection without the Windows-only GUI.

# Introduction

Use the graphical client to select which files you want to download. The selected files are saved to download.json. The file is saved at the following locations:

Windows:
C:\Users\{USERNAME}\AppData\Local\Geonorge\Nedlasting

Linux/Mac:
/home/{USERNAME}/.local/share/Geonorge/Nedlasting

When you start the console application the download.json file is parsed together with the latest version of the Atom Feed. The application inspects the last updated date and compares it with the local copy of the file. If a new file has been published it will start the download. 

The graphical client is only available on Windows. The terminal UI and console downloader can run on all platforms. This means you can configure and maintain your selection directly on Linux/macOS without a Windows machine.

## How to change download location

The default download location is **My Documents\Geonorge-Nedlasting** (windows) or **/home/{username}/Geonorge-Nedlasting** (linux/mac). 

To change this location go into the application settings directory, se previous paragraph, and edit the settings.json file. Here you can change the DownloadDirectory setting. Save the file and it will be used next time you run the download application. 
When you move the settings to a new machine/user account, the encrypted password will no longer be readable and you will have to set it again. If you have an old version of the client, you must first set Password blank in settings.json before you can set it in the user interface.

## How to setup development environment

Project depends on:
* .net core 10.0 SDK
* .net framework 4.7.1 Developer pack

Packages can be downloaded from here:
https://www.microsoft.com/net/download/windows

Linux SDK setup example (for local user install):

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --version 10.0.100 --install-dir "$HOME/.dotnet"
```

Make it persistent for future shells:

```bash
cat >> ~/.bash_profile <<'EOF'
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
EOF
```

Solution builds with Visual Studio 2026. 

Signing of application binaries can be done with signtool.exe. This tool is a part of Windows SDK.

Remember to sign both the exe file and the final setup exe/msi file. 

## Project structure

### NedlastingKlient
Common class library for parsing atom feeds and downloading files.

Compilation targets both netstandard 2.1 (.net core) and .net framework 4.7.1

### NedlastingKlient.Gui

Graphical user interface for browsing and selecting files for download

Compilation target: .net framework 4.7.1

### Geonorge.MassivNedlasting.TerminalUi

Cross-platform terminal UI for browsing datasets and editing download selection/configuration.

Compilation target: net10.0

### NedlastingKlient.Konsoll
Console application for downloading selected files

Compilation target: netstandard 2.1 (.net core)


## How to build

Build and publish for windows (64-bit)

    dotnet publish -r win-x64

Build and publish for windows (64 bit) self contained

    dotnet publish -r win-x64 --self-contained

Build terminal UI (Linux/macOS/Windows):

    dotnet build TerminalUi/Geonorge.MassivNedlasting.TerminalUi.csproj

Run terminal UI:

    dotnet run --project TerminalUi/Geonorge.MassivNedlasting.TerminalUi.csproj

Run downloader with active config:

    dotnet run --project Console/Geonorge.Nedlaster.csproj

## Migration note (Linux-compatible UI)

### What was added
* New terminal UI project: `TerminalUi/Geonorge.MassivNedlasting.TerminalUi.csproj`
* Interactive text UI flow for:
  * browsing/searching datasets
  * searching/selecting/removing dataset files
  * enabling subscribe mode and toggling projection/format filters
  * editing download/log directories and credentials
  * saving selection and settings

### Config compatibility
* Uses the same `settings.json` and config JSON files (`default.json` etc.) through `ApplicationService` and `DatasetService`.
* Existing downloader flow and console downloader are unchanged.

### Known limitations
* Terminal UI is menu-driven (not ncurses/full-screen), optimized for low complexity and maintainability.
* Feed/file listings are intentionally capped per view (`top 50` datasets, `top 200` files) to keep terminal interaction responsive; users can refine search and repeat.
