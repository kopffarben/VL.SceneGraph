# VL.SceneGraph — Claude Code Projektkontext

## Sprache

- Wir kommunizieren auf **Deutsch**
- Kommentare und Identifier im Quellcode sind immer auf **Englisch**

## Projekt

**Compositor / Show-Control-System für vvvv/VL** — vergleichbar mit TouchDesigner/Resolume, aber auf VL-Patches als Bausteine aufgebaut. Für Projection-Painting-Performances (Kopffarben).

- **Sprache:** C# (.NET 8.0) + VL (vvvv gamma Visual Language)
- **Architektur-Doku:** `docs/ARCHITEKTUR.md` (vollständige Referenz, ~1500 Zeilen)

## Kern-Architektur

- **Immutabler Baum** (`SceneNode`) mit Structural Sharing (Path Copying)
- **Component-System** (`IComponent` Records, maximal eine pro Typ pro Node)
- **Gerichteter Graph** als Overlay (Edges für DataFlow, Reference, Dependency, Trigger)
- **Flache BFS-Repräsentation** (`FlatSceneGraph`) mit SoA-Layout für Performance
- **Multi-Pass Frame-Pipeline** (14 Passes: Time → Inputs → Recording → FSM → Activity → Timeline → Constraints → Routing → Evaluation → ...)
- **Source Generators** (`[SceneComponent]`, `[SceneConstraint]`) erzeugen typisierte Accessors, SoA-Arrays, Compile-Code
- **NodeFactories** für dynamische VL-Nodes (Get/Set/Has pro Component)
- **Double-Buffering** (Front/Back `FlatSceneGraph`, atomarer Swap)
- **Zero Allocations** im Hot-Path (ArrayPool, stackalloc, Span-basierte Passes)

## Projekt-Struktur (geplant)

```
VL.SceneGraph/
├── src/
│   ├── VL.SceneGraph.Generators/    # Source Generator (Roslyn Analyzer)
│   ├── VL.SceneGraph/               # Hauptprojekt
│   │   ├── Attributes/              # [SceneComponent], [SceneConstraint]
│   │   ├── Core/                    # SceneNode, SceneGraph, Edge, IComponent, SlotType
│   │   ├── Components/              # Clip, Time, Control, Constraint, FSM, Input, Recording
│   │   ├── Flat/                    # FlatSceneGraph, Compiler, Passes/
│   │   ├── Evaluation/              # ClipEvaluator, TextureCompositor
│   │   ├── Recording/               # TrackRecorder, DrawingRecorder
│   │   ├── Runtime/                 # CompositorRuntime, DoubleBufferedScene
│   │   ├── Presets/                 # PresetManager
│   │   ├── Undo/                    # UndoManager, EditScope
│   │   ├── Serialization/           # ShowFile, GraphSerializer
│   │   └── VL/                      # Extensions, NodeFactories
│   ├── VL.SceneGraph.Tests/
│   └── References/                  # ⚠️ NUR REFERENZ — NICHT ÄNDERN
│       └── VL.StandardLibs/         # Git-Submodule (vvvv/VL.StandardLibs)
├── vl/
│   └── VL.SceneGraph.vl
├── help/
└── docs/
    └── ARCHITEKTUR.md
```

## Wichtige Regeln

### References sind read-only
Alles unter `src/References/` ist ein Git-Submodule und dient nur als Referenz. **Niemals Dateien in `src/References/` ändern.**

### Code-Stil
- C# Records für Components: `public partial record MyComponent(...);`
- Immutable-First: `ImmutableArray`, `ImmutableDictionary`, kein mutables State im logischen Baum
- `ImmutableArray<T>` bevorzugen (30x schneller beim Iterieren als `ImmutableList<T>`)
- Span-basierte APIs im Hot-Path
- Extension Methods als VL-Nodes (statt Vererbung)
- Keine Vererbung auf VL-Seite (VL-Klassen erben von `VLObject`)

### Performance-Ziele
- 60fps bei 1000+ Nodes
- Zero Allocations im Hot-Path
- < 2ms für Pipeline (ohne Clip-Evaluation)

## Verfügbare Skills

Folgende Skills stehen für vvvv-spezifische Aufgaben bereit:

| Skill | Zweck |
|-------|-------|
| `vvvv-custom-nodes` | C# Node-Klassen ([ProcessNode], Update(), Pins) |
| `vvvv-node-libraries` | Library-Projekte, Initialization.cs, RegisterService |
| `vvvv-dotnet` | NuGet, .csproj, [ImportAsIs], Typ-Interop |
| `vvvv-spreads` | Spread<T>, SpreadBuilder<T>, Collection-Verarbeitung |
| `vvvv-channels` | IChannelHub, Public Channels, reaktiver Datenfluss |
| `vvvv-shaders` | SDSL/HLSL Shader, TextureFX, Compute, ShaderFX |
| `vvvv-patching` | Dataflow-Patterns, Regions, Event-Handling |
| `vvvv-fundamentals` | vvvv Grundkonzepte, Execution-Model, Datentypen |
| `vvvv-fileformat` | .vl XML-Format, Patch-Generierung/-Parsing |
| `vvvv-editor-extensions` | .HDE.vl, Command Nodes, Editor-Plugins |
| `vvvv-debugging` | VS Code launch.json, Debugger an vvvv attachen |
| `vvvv-startup` | Kommandozeile, Package-Repos, Dateisystem-Pfade |
| `vvvv-testing` | VL.TestFramework, NUnit, Test-Patches |
| `vvvv-troubleshooting` | Fehlerdiagnose, Red Nodes, Performance |

## Dependencies

- `System.Collections.Immutable` (8.0.0)
- `System.Numerics.Vectors` (4.5.0)
- VL.StandardLibs (Submodule): VL.Core, VL.ImGui, VL.Stride, VL.Skia, VL.Serialization, etc.
