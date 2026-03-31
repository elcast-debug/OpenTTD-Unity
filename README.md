# OpenTTD Unity Clone

A transport tycoon game inspired by OpenTTD, built in Unity 6 with C#.

## Features (Core Prototype)

- **3D Isometric View** — Orthographic camera with 45°/30° isometric projection, WASD pan, scroll zoom, Q/E rotation
- **Procedural Terrain** — Perlin noise heightmap with stepped terrain (classic TTD style), vertex-colored chunks
- **Rail System** — Click-and-drag rail placement with auto-routing (straight + L-bends), junctions, curves
- **Trains** — A* pathfinding on rail graph, smooth movement with curve speed reduction, cargo loading/unloading
- **Stations** — Place on straight rail, acceptance radius for nearby industries, cargo waiting queues, rating system
- **Industries** — Coal Mine (producer) → Power Station (consumer) chain with production cycles
- **Economy** — Starting balance, income from cargo delivery (distance-based), building costs, running costs
- **UI** — Top bar (money/date/speed), bottom toolbar (build tools), info panel (click selection details)

## Tech Stack

- **Engine:** Unity 6 (6000.x) with URP
- **Language:** C# with `OpenTTDUnity` namespace
- **Architecture:** Singleton managers, event-driven, chunk-based terrain, A* rail pathfinding

## Project Structure

```
Assets/Scripts/
├── Core/          — GameManager, GridManager, Tile, Constants
├── Camera/        — Isometric camera controller
├── Terrain/       — Procedural terrain generation and rendering
├── Rail/          — Rail placement, mesh generation, network management
├── Vehicles/      — Train entity, movement, pathfinding, orders
├── Stations/      — Station logic and placement
├── Economy/       — Money tracking, cargo definitions, payment calculation
├── Industry/      — Industry base class, Coal Mine, Power Station
└── UI/            — UIManager, TopBar, Toolbar, InfoPanel, BuildPreview
```

## Getting Started

1. Open the project in Unity 6 (6000.x)
2. Install URP if not already configured
3. Install TextMeshPro (via Package Manager)
4. Open `Assets/Scenes/MainScene.unity`
5. Create GameObjects and attach the manager scripts (see setup guide below)

### Scene Setup

Create these GameObjects in the scene:

| GameObject | Scripts to Attach |
|---|---|
| `GameManager` | `GameManager` |
| `GridManager` | `GridManager` |
| `TerrainGenerator` | `TerrainGenerator` |
| `TerrainModifier` | `TerrainModifier` |
| `RailManager` | `RailManager`, `RailPlacer` |
| `IndustryManager` | `IndustryManager` |
| `EconomyManager` | `EconomyManager` |
| `UIManager` | `UIManager` |
| `Main Camera` | `IsoCameraController` (set to Orthographic) |
| `Canvas` | `TopBar`, `Toolbar`, `InfoPanel`, `BuildPreview` |

## Controls

| Input | Action |
|---|---|
| WASD / Arrows | Pan camera |
| Scroll wheel | Zoom in/out |
| Q / E | Rotate camera 90° |
| Middle mouse drag | Pan camera |
| R | Rail tool |
| S | Station tool |
| T | Terraform tool |
| B | Bulldoze tool |
| Space | Pause/unpause |
| 1 / 2 / 3 | Game speed 1× / 2× / 4× |
| Escape | Cancel current tool |
| Left click + drag | Place rail / Use current tool |
| Right click | Cancel placement |

## License

MIT
