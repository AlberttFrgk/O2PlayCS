# O2Play (O2Viewer)

A modern, high-performance C# / .NET 10 reimplementation of the classic **O2Play / O2Viewer** rhythm game chart player and preview tool. Built with **Raylib** for hardware-accelerated OpenGL rendering and **Un4seen BASS** for sample-accurate audio playback.

Acts as a standalone player and a drop-in replacement for legacy `o2play.exe` in chart editors like **iBMSC** and **μBMSC**.

---

## 📋 Prerequisites

- **OS**: Windows 10 / 11 (64-bit)
- **SDK**: [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (required to restore and build)

---

## ⚙️ Dependency Setup & Building

### 1. Restore Dependencies
Run this in PowerShell or Command Prompt at the project root:
```powershell
dotnet restore
```
> Feeds are configured in [`NuGet.Config`] (supports `nuget.org` and local package cache fallback). You can also run [`Setup-Dependencies.bat`].

### 2. Build the Application
Compile the Release build via CLI:
```powershell
dotnet build -c Release
```
Or open [`O2Play.sln`] in Visual Studio 2022/2026 or JetBrains Rider, select **Release (x64)**, and press **Ctrl+Shift+B**.

The compiled output will be located in:
```text
bin\release\net10.0-windows\
```
All required native libraries (`raylib.dll`, `bass.dll`, `bass_fx.dll`) and embedded assets are automatically copied alongside `O2Play.exe`.

---

## 🚀 How to Run

- **Direct Launch**: Double-click `O2Play.exe`.
- **Drag & Drop**: Drag and drop any `.ojn`, `.bms`, `.bme`, or `.bml` chart file directly onto `O2Play.exe` (or onto the window while running).
- **Open Dialog**: Press **`O`** while inside the app to browse for charts.

---

## 🎮 Controls & Hotkeys

### Keyboard Hotkeys
| Key | Action |
| :--- | :--- |
| **`Space`** | Toggle Play / Pause |
| **`O`** | Open File Dialog (Load `.ojn` / `.bms`) |
| **`F1` / `F2`** | Decrease / Increase Note Fall Speed (HiSpeed: `0.5x` - `8.0x`) |
| **`1` / `2`** | Decrease / Increase Music Speed (`0.5x` - `2.0x` pitch-scaled) |
| **`3`** | Reset Music Speed to `1.0x` |
| **`Esc`** | Exit O2Play (when window is focused) |

### Mouse Controls
| Action | Target Area | Description |
| :--- | :--- | :--- |
| **Left Click & Drag** | Bottom Progress Bar | Seek / scrub through song position in real-time |
| **Mouse Wheel** | Playfield or Progress Bar | Jump backward/forward by 1 measure |
| **Left Click** | EX / NX / HX Buttons | Switch OJN difficulty level |
| **Left Click** | Row 2 (Notes) | Cycle OJN difficulty |
| **Left Click / Wheel** | Row 7 (PlaySpeed) | Adjust HiSpeed |
| **Middle Click** | Row 7 or Row 8 | Reset PlaySpeed (`2.0x`) or MusicSpeed (`1.0x`) |
| **Left Click** | KEY / BGM / EFFECT | Toggle Key Sounds, Background Music, or Visual Effects |

---

## 🔌 iBMSC Integration

O2Play is a drop-in replacement matching standard legacy `o2play.exe` flags. In **iBMSC** / **μBMSC**:

- Go to **Options** -> **Settings** -> **Player** tab and point the player path directly to `O2Play.exe`.

---

## 💻 Command-Line Arguments Reference

```text
O2Play.exe [-P] [-N<measure>] [-S] [-EX|-NX|-HX] ["<FilePath>"]
```

| Flag | Description | Example |
| :--- | :--- | :--- |
| `"<FilePath>"` | Path to `.ojn`, `.bms`, `.bme`, or `.bml` chart file | `"C:\Songs\test.ojn"` |
| `-P` | Starts playback immediately upon loading | `-P "song.ojn"` |
| `-N<measure>` | Seeks to the specified 0-based measure index before playing | `-P -N12 "song.ojn"` |
| `-S` | Signals the running instance to stop playback and seek to 0 | `-S` |
| `-EX` / `-D0` | Loads Easy (EX) difficulty for `.ojn` files | `-EX "song.ojn"` |
| `-NX` / `-D1` | Loads Normal (NX) difficulty for `.ojn` files | `-NX "song.ojn"` |
| `-HX` / `-D2` | Loads Hard (HX) difficulty for `.ojn` files | `-HX "song.ojn"` |

