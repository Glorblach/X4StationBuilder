# X4 Station Builder

A Windows desktop tool for planning and building stations in
[**X4: Foundations**](https://www.egosoft.com/games/x4/info_en.php) — scan your own
game install, work out production chains, lay out modules, and export a blueprint you
can import back into the game.

> ⚠️ **Work in progress.** This is a hobby project that gets worked on *when inspiration
> and spare time align*. Expect rough edges, missing features, and breaking changes.
> It is not feature-complete and may not be at any particular point in time.

## About

X4 Station Builder reads the data from your existing X4: Foundations installation
(including detected DLCs/extensions) and helps you design a station around it. Instead of
guessing how many production modules, storage, and workforce habitats you need, the tool
parses the game's wares and modules and does the math for you.

Because it works directly from your installed game files, it stays in sync with the wares
and modules that are actually available to you — including DLC content — rather than a
hardcoded snapshot.

## Features

- **Game-folder scanning** — mounts the game's CAT/DAT archives and parses wares and
  station modules straight from your install.
- **DLC / extension aware** — detects installed expansions and merges their data into the
  catalog.
- **Production planning** — calculates production chains and the modules needed to meet a
  target output, including intermediate wares.
- **Storage & workforce planning** — works out storage requirements and habitat/workforce
  needs (with per-species housing).
- **Station layout** — arranges chosen modules into a buildable station layout.
- **3D preview** — visualize the planned station in 3D (powered by HelixToolkit).
  > 📝 **Note:** The 3D viewer is currently very bare bones and may not be developed further.
- **Blueprint export** — export the design as station blueprint XML.

## Requirements

- **Windows** (this is a WPF application).
- **[.NET 10 SDK](https://dotnet.microsoft.com/download)** (target framework
  `net10.0-windows`).
- A legitimate installation of **X4: Foundations**. The app ships **no** game data — it
  reads from your own install.

## Building & Running

Clone the repository and build/run with the .NET CLI:

```bash
git clone https://github.com/Glorblach/X4StationBuilder.git
cd X4StationBuilder

# Build the whole solution
dotnet build

# Run the desktop app
dotnet run --project X4StationBuilder.App
```

You can also open `X4StationBuilder.slnx` in Visual Studio (2022/2026 with .NET 10 support)
and run the `X4StationBuilder.App` project.

### Running tests

```bash
dotnet test
```

## Usage

1. Launch the app.
2. Open the **Game data & scan** section and point it at your X4: Foundations install
   folder, then **Scan Game Folder**. (The tool can attempt to locate the install
   automatically.)
3. Pick the wares/modules you want to produce and set your targets and workforce species.
4. Review the calculated production, storage, and workforce plan.
5. Open the **3D preview** to inspect the station layout.
6. **Export** the blueprint XML and import it into the game.

## Project structure

| Project | Description |
| --- | --- |
| `X4StationBuilder.Core` | Domain logic: archive reading, DLC detection, ware/module parsing, production calculation, storage/workforce/layout planning, blueprint export. |
| `X4StationBuilder.App` | WPF UI (MVVM via CommunityToolkit.Mvvm) with a 3D station preview (HelixToolkit.Wpf). |
| `X4StationBuilder.Tests` | Unit tests for the core services. |

## Disclaimer

This is an unofficial, fan-made tool and is **not affiliated with, endorsed by, or
associated with Egosoft**. *X4: Foundations* and all related names, marks, and assets are
the property of their respective owners. You must own and have X4: Foundations installed
to use this tool.
