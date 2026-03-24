# VL.SceneGraph — Architektur-Dokumentation

## Compositor / Show-Control-System für vvvv/VL

**Stand: März 2026**
**Autor: Johannes Schmidt (Kopffarben)**

---

## Was ist das?

Ein **Compositor / Show-Control-System** für vvvv/VL. Kein klassischer 3D-Szenegraph, sondern ein System das VL-Patches als **stateful Plugins (Clips)** behandelt und sie zeitlich, hierarchisch und über Constraints organisiert.

Vergleichbar mit TouchDesigner, Resolume oder Notch — aber auf VL-Patches als Bausteine aufgebaut und über eine ImGui-UI steuerbar.

### Kernkonzepte

- **Clip** — Eine Instanz eines VL-Patches mit konkreter Konfiguration, animierbaren Parametern und Zustand
- **Layer/Group** — Hierarchische Gruppierung von Clips. Bestimmt Kompositions-Reihenfolge, Sichtbarkeit und Scope
- **Timeline** — Zeitliche Organisation: Wann ist welcher Clip aktiv, welche Parameter-Werte hat er wann
- **StateMachine** — Generative/reaktive Steuerung: Unter welchen Bedingungen werden Clips gestartet/gestoppt/übergeblendet
- **Constraints** — Beziehungen zwischen Clips: Parameter-Links, Activity-Sync, Tempo-Sync, Expressions
- **Recording** — Aufzeichnung und Wiedergabe von Inputs, Parametern und Zeichnungen

### Anwendungsfall: Kopffarben

Für Projection-Painting-Performances (z.B. Augustinerkloster Erfurt):

```
[Show "Kirchenprojektion"]
  [Layer "Architektur"]
    [Clip "FassadenMapping" — ProjectionWarp]
    [Clip "Fenster-Highlight" — MaskReveal]
  [Layer "Julia's Malerei"]
    [Clip "LiveDrawing" — TabletInput → TextureStream]
    [Clip "Partikel" — DrawingParticles]
  [Layer "Generativ"]
    [Clip "Sternenhimmel" — ProceduralStars]
  [Layer "PostFX"]
    [Clip "Bloom" — BloomFX]
    [Clip "ColorGrade" — LUTGrade]
```

---

## Architektur-Schichten

```
╔══════════════════════════════════════════════════════════════╗
║  DESIGN-ZEIT (C# + Source Generators)                        ║
║                                                              ║
║  [SceneComponent]  →  Generator erzeugt:                     ║
║  record Transform3D     • Typisierte SceneNode-Accessors    ║
║  record AudioEmitter    • SoA-Array-Definitionen             ║
║  record LookAt          • Constraint-Compiler-Code           ║
║  record MyCustom        • ComponentRegistry                  ║
║                                                              ║
║  VL hot-reloads bei jedem Save des csproj                    ║
╚══════════════════════════════════════════════════════════════╝
                              │
                              ▼
╔══════════════════════════════════════════════════════════════╗
║  VL PATCH-ZEIT (NodeFactories + Records)                     ║
║                                                              ║
║  NodeFactory erzeugt dynamisch:                              ║
║    • Get/Set-Nodes für jede Component                        ║
║    • ForEachWith{Component}-Nodes                            ║
║    • SceneNode-Inspector mit dynamischen Pins                ║
║                                                              ║
║  VL-Nutzer können eigene Components als Records patchen      ║
╚══════════════════════════════════════════════════════════════╝
                              │
                              ▼
╔══════════════════════════════════════════════════════════════╗
║  RUNTIME                                                     ║
║                                                              ║
║  ┌─────────────────┐   ┌────────────────────────┐           ║
║  │ Logischer Baum   │   │ Flaches BFS-Array      │           ║
║  │ (Immutable)      │──▶│ (SoA, Cache-freundlich)│           ║
║  │ SceneNode + Edges│   │ + Multi-Pass Pipeline  │           ║
║  └─────────────────┘   └────────────────────────┘           ║
║           │                        │                         ║
║           ▼                        ▼                         ║
║  ┌─────────────────┐   ┌────────────────────────┐           ║
║  │ ImGui UI         │   │ Clip-Evaluation         │           ║
║  │ (Show Control)   │   │ (VL-Patches ausführen) │           ║
║  └─────────────────┘   └────────────────────────┘           ║
╚══════════════════════════════════════════════════════════════╝
```

---

## Design-Entscheidungen

- **Immutable-First**: Passt zu VLs Dataflow-Modell. Ermöglicht Undo/Redo, Snapshots, Thread-Sicherheit
- **Keine Vererbung auf VL-Seite**: VL-Klassen erben von `VLObject`, daher Component-basierte Architektur
- **Zwei-Schichten-Modell**: Immutabler logischer Baum (Korrektheit) + flache BFS-Arrays (Performance)
- **ISceneClip Marker-Interface**: VL-User patchen normale Processes, implementieren ein Interface — kein C# nötig
- **Patchbares Model**: SceneGraph lebt im VL-Patch des Users (Accumulator-Pattern), kein Hidden State
- **SceneEdits als Records**: Alle Änderungen (UI, Clips, OSC, Timeline) fließen als immutable Edit-Records
- **Source Generators + NodeFactories**: Eliminieren Boilerplate, nutzen VLs csproj-Hot-Reload
- **Hotswap via VL.Core**: `IHotSwappableEntryPoint.OnSwap` für nahtlosen Patch-Reload bei laufender Show
- **PersistentId für stabile Referenzen**: VLs PersistentId überlebt Umbenennungen — Schema-Migration ohne Datenverlust
- **Drei State-Typen**: Persistent (Components), Transient (Components mit Transient=true, nicht serialisiert), Clip-intern (VL Process Felder)
- **VL Channels für Events**: IChannelHub als System-Pin — Low-Frequency Events und Inter-Clip-Kommunikation über VLs bestehendes Channel-System
- **Layout via Components (Option C)**: LayoutConfig ist eine optionale Component, kein Interface-Gate — Compositor-Nodes ohne Layout haben zero Overhead
- **Flexbox Layout-Engine**: Pure C# Port von Yoga (~6000 Zeilen), direkt migriert, hinter ILayoutEngine Interface austauschbar
- **Modulare Pakete**: Kern (VL.SceneGraph) bleibt schlank, Layout/Input/Compositor als optionale Erweiterungspakete

---

## Dokumentation — Dateistruktur

| Datei | Inhalt |
|-------|--------|
| **[01-KERN.md](architecture/01-KERN.md)** | Immutabler Baum (SceneNode), Component-System, Gerichteter Graph, Slot-Typsystem, Templates |
| **[02-PIPELINE.md](architecture/02-PIPELINE.md)** | Flache BFS-Repräsentation, Multi-Pass Frame-Pipeline, Stream-Routing, Double-Buffering, Memory-Management |
| **[03-CLIPS.md](architecture/03-CLIPS.md)** | ISceneClip Drei-Schichten-Modell, ISceneContext, SceneEdit, Patchbares Model, Clip-Lifecycle, Pin-Binding |
| **[04-ZEIT-UND-STEUERUNG.md](architecture/04-ZEIT-UND-STEUERUNG.md)** | Constraint-System, Zeit-System, StateMachines, Externe Inputs, Recording-System |
| **[05-SERIALISIERUNG.md](architecture/05-SERIALISIERUNG.md)** | Show-Datei, PatchRegistry, Schema-Migration, Hotswap, Preset-System |
| **[06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md)** | NodeFactories, VL-Integration, Undo-System, ImGui-Compositor-UI |
| **[07-PROJEKTSTRUKTUR.md](architecture/07-PROJEKTSTRUKTUR.md)** | Source Generators, Verzeichnis-Layout, NuGet, Offene Punkte, Performance-Ziele |
| **[08-LAYOUT-UND-INPUT.md](architecture/08-LAYOUT-UND-INPUT.md)** | Layout-Engine (Flexbox), LayoutConfig, ComputedLayout, InputRouting, Hit-Testing, IMeasurable |
| **[09-STRIDE-INTEGRATION.md](architecture/09-STRIDE-INTEGRATION.md)** | ClipOutput, TextureCompositor, Scene3DCompositor, Compute, PostFX, Mapping, Output Sinks, GPU Lifecycle |
| **[OFFENE-FRAGEN.md](OFFENE-FRAGEN.md)** | Offene Design-Fragen, Brainstorming-Backlog, nächste Schritte |

---

## Performance-Ziele

- 60fps bei 1000+ Nodes
- Zero Allocations im Hot-Path (pro Frame)
- < 2ms für die gesamte Pipeline (ohne Clip-Evaluation)
- Inkrementeller Compile < 0.5ms bei Property-Only-Änderungen
- Full Rebuild < 5ms bei 1000 Nodes

---

## Dependencies

- `System.Collections.Immutable` (8.0.0)
- `System.Numerics.Vectors` (4.5.0)
- VL.StandardLibs (Submodule): VL.Core, VL.ImGui, VL.Stride, VL.Skia, VL.Serialization
