# VL.SceneGraph — Offene Fragen

**Lebendiges Dokument** — wird gemeinsam gepflegt und abgearbeitet.

---

## Priorität: Hoch

### Input-Routing im Detail
- Wie genau funktioniert Event-Bubbling? Brauchen wir Capture/Bubble-Phasen wie im DOM?
- Wie werden Multi-Touch-Events geroutet (mehrere simultane Touch-Punkte)?
- Gesture Recognition: Swipe, Pinch, Long-Press — wo lebt die Logik? (Pipeline-Pass oder Clip?)
- Focus-Management: Welcher Node hat den Tastatur-Fokus? Tab-Reihenfolge?

### LayoutConfig API-Design
- Wie mappt LayoutConfig auf Flexbox-Properties für den VL-User? (Node-Browser-Darstellung)
- Brauchen wir Convenience-Factories? z.B. `LayoutConfig.Stack(Vertical, Gap: 8)`, `LayoutConfig.Fill()`
- Wie geht Dock-Layout genau? (Ist das echtes Docking oder absolute Positionierung?)
- Scroll-Verhalten: Wie interagiert Scrolling mit dem InputRoutingPass?

---

## Priorität: Mittel

### Expression-Engine
- Wie werden mathematische Ausdrücke in Constraints evaluiert? (z.B. `sin(time * 2) * source.opacity`)
- Parsing → AST → Kompilation?
- Performance: Pro Frame evaluiert oder gecacht?
- Welche Variablen/Funktionen sind verfügbar?

### Netzwerk / Multi-Machine
- Synchronisierung für große Installationen mit mehreren Rechnern
- Graph-State synchronisieren oder nur Edits streamen?
- Latenz-Kompensation für synchrones Playback?

### Text-Rendering
- Welches Text-Rendering-Backend? (Stride SpriteFont, SkiaSharp, benutzerdefiniert?)
- Wie integriert sich IMeasurable.Measure() mit dem gewählten Font-System?
- Rich-Text-Support? (Verschiedene Fonts/Farben in einem TextBlock?)
- Text-Rendering-Performance bei vielen Text-Nodes?

---

## Priorität: Niedrig / Zukunft

### Weitere Interface-Typen
- Brauchen wir `ISceneDataSource` für async Datenladung (API, DB)?
- Mesh/3D-Interfaces: `IMeshGenerator`, `IMeshEffect`?
- Video-spezifische Interfaces?

### Spread<T> vs ImmutableArray<T>
- Wo ist die Grenze zwischen VL-nativen Typen (Spread) und C#-Typen (ImmutableArray)?
- Sollen Components Spread<T> oder ImmutableArray<T> nutzen?

### Performance-Profiling
- Welche Passes wie lange dauern, wo sind Bottlenecks?
- Debug-UI für Pipeline-Timings?
- GPU-Profiling-Integration?

### Keyframe-Kurven-Editor
- Bezier-Tangenten-Editing in ImGui
- Kurven-Presets (Ease-In, Ease-Out, Bounce, etc.)

### Error Recovery
- Was passiert wenn ein Clip-Patch crasht? Isolation?
- Kann ein fehlerhafter Clip den Rest der Pipeline blockieren?
- Automatisches Deaktivieren fehlerhafter Clips?

---

## Entschieden (Archiv)

| Frage | Entscheidung | Doku |
|-------|-------------|------|
| Clip-System: Wie registrieren sich VL-Patches? | ISceneClip Marker-Interface + TypeRegistry Discovery | [03-CLIPS.md](architecture/03-CLIPS.md) |
| Model-Zugriff für Clips | ISceneContext mit read-only Zugriff auf immutablen Graph | [03-CLIPS.md](architecture/03-CLIPS.md) |
| Wie ändern Clips den Graph? | SceneEdit Records als Output-Pin | [03-CLIPS.md](architecture/03-CLIPS.md) |
| Wo lebt der Graph? | Im VL-Patch des Users (Accumulator-Pattern) | [03-CLIPS.md](architecture/03-CLIPS.md) |
| Stabile Referenzen bei Patch-Änderungen | PersistentId aus VL-Compiler | [05-SERIALISIERUNG.md](architecture/05-SERIALISIERUNG.md) |
| Hotswap-Mechanismus | IHotSwappableEntryPoint.OnSwap | [05-SERIALISIERUNG.md](architecture/05-SERIALISIERUNG.md) |
| Runtime-State der nicht persistent ist | Transient Components + VL Channels | [03-CLIPS.md](architecture/03-CLIPS.md) |
| Layout: Interface vs. Component | Option C: Layout via Components, kein Interface-Gate | [08-LAYOUT-UND-INPUT.md](architecture/08-LAYOUT-UND-INPUT.md) |
| Layout-Engine | Flexbox Pure C# Port, direkt migriert | [08-LAYOUT-UND-INPUT.md](architecture/08-LAYOUT-UND-INPUT.md) |
| Paketstruktur | Modulare Pakete: Kern + Layout + Input + Compositor | [07-PROJEKTSTRUKTUR.md](architecture/07-PROJEKTSTRUKTUR.md) |
| ImGui-UI Architektur | Widgets erben von VL.ImGui Widget, lesen Graph als Property, schreiben Commands in UndoRedoStack | [06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md) |
| Undo/Redo System | Globaler UndoRedoStack, ISceneCommand mit Do/Undo, In-Flight Pattern für Drags, MacroCommand für Batch | [06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md) |
| Inspector / ObjectEditor | SceneComponentEditorFactory für C# Records, ChainedEditorFactory, VLs WidgetType-Attribute | [06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md) |
| Custom Component-Editors | IObjectEditorFactory Chain: User Custom → SceneComponentEditorFactory → VL Default | [06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md) |
| Edit-Flow | UndoRedoStack ist der zentrale Edit-Hub, FlushPendingEdits() für Accumulator | [06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md) |
| Shortcuts | T3-Pattern: CompositorActions Enum + KeyMap + action.Triggered(), JSON-konfigurierbar | [06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md) |
| VL-Editor Integration | SessionNodes.ShowPatchOfNode(UniqueId) für "Open Patch" / "Show in Parent Patch" | [06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md) |
| Context-Menüs | ImGuis eingebaute Popups, IContextMenuContributor für Erweiterbarkeit | [06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md) |
| NodeBrowser | TypeRegistry Discovery, Slot-Validierung als Warning (nicht Filter), Drag&Drop | [06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md) |
| Custom Windows | VL.ImGui Window() + DockSpace, User patcht Widget → dockt sich automatisch ein | [06-UI-UND-TOOLING.md](architecture/06-UI-UND-TOOLING.md) |
| HFSM-Design | Selbst gebaut, hierarchisch, Crossfading mit TransitionProgress, Parallel-States | [04-ZEIT-UND-STEUERUNG.md](architecture/04-ZEIT-UND-STEUERUNG.md) |
| FSM-TimeControl | StateDefinition.OnEnterTimeControl steuert Timeline-Playhead (GotoTime, Rate, Mode) | [04-ZEIT-UND-STEUERUNG.md](architecture/04-ZEIT-UND-STEUERUNG.md) |
| Timeline↔FSM bidirektional | Cues triggern FSM, FSM steuert Playhead — alles über SceneEdits | [04-ZEIT-UND-STEUERUNG.md](architecture/04-ZEIT-UND-STEUERUNG.md) |
| Cue Threshold-Crossing | PreviousLocalTime in TimeContext, Crossing-Check statt Wertvergleich | [04-ZEIT-UND-STEUERUNG.md](architecture/04-ZEIT-UND-STEUERUNG.md) |
| Ghost-Preview | Transparente Ghost-Blöcke für mögliche FSM-Transitions am Playhead | [04-ZEIT-UND-STEUERUNG.md](architecture/04-ZEIT-UND-STEUERUNG.md) |
| Generative Timeline | ActivityLogger + authored/generated Clips in derselben Timeline | [04-ZEIT-UND-STEUERUNG.md](architecture/04-ZEIT-UND-STEUERUNG.md) |
| Nested FSM Crossfade | Current + Incoming evaluieren, Outgoing einfrieren (History) | [04-ZEIT-UND-STEUERUNG.md](architecture/04-ZEIT-UND-STEUERUNG.md) |
| ClipOutput Design | readonly record struct mit 6 Typen (Texture, Renderer, ImageEffect, Entities, ComputeBuffer, None), Borrow-Semantik | [09-STRIDE-INTEGRATION.md](architecture/09-STRIDE-INTEGRATION.md) |
| TextureCompositor | Parallel (Blending), Sequential (Ping-Pong), Scene3D (Entity-Merge), TexturePool für zero alloc | [09-STRIDE-INTEGRATION.md](architecture/09-STRIDE-INTEGRATION.md) |
| Mapping als Clips | GridWarp, SoftEdge, AlphaMask als eigene IMappingEffect Clips in Sequential-Ketten | [09-STRIDE-INTEGRATION.md](architecture/09-STRIDE-INTEGRATION.md) |
| PostFX als Clips | Bloom, DOF, Tonemap etc. als IPostFXEffect Clips, Activity = Intensity-Multiplier | [09-STRIDE-INTEGRATION.md](architecture/09-STRIDE-INTEGRATION.md) |
| Output Sinks als Clips | ProjectionOutput, NDI, Spout, Recorder — alles ISceneClip, FSM-steuerbar | [09-STRIDE-INTEGRATION.md](architecture/09-STRIDE-INTEGRATION.md) |
| Multi-Sink Routing | Mehrere Sinks lesen via DataFlow-Edges von beliebigen Stellen im Graph | [09-STRIDE-INTEGRATION.md](architecture/09-STRIDE-INTEGRATION.md) |
| GPU Resource Lifecycle | Deferred Disposal: Clips disposen nach Buffer-Swap, TexturePool für Intermediates | [09-STRIDE-INTEGRATION.md](architecture/09-STRIDE-INTEGRATION.md) |
| Compute im Graph | IComputeClip → ComputeBuffer → Render-Clip liest Buffer, CommandList synchron | [09-STRIDE-INTEGRATION.md](architecture/09-STRIDE-INTEGRATION.md) |
| RenderFeature entfernt | Stride registriert Features statisch → Clips nutzen Renderer oder Entities statt | [09-STRIDE-INTEGRATION.md](architecture/09-STRIDE-INTEGRATION.md) |
