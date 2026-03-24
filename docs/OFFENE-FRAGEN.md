# VL.SceneGraph — Offene Fragen

**Lebendiges Dokument** — wird gemeinsam gepflegt und abgearbeitet.

---

## Priorität: Hoch

### Input-Routing im Detail
- Wie genau funktioniert Event-Bubbling? Brauchen wir Capture/Bubble-Phasen wie im DOM?
- Wie werden Multi-Touch-Events geroutet (mehrere simultane Touch-Punkte)?
- Gesture Recognition: Swipe, Pinch, Long-Press — wo lebt die Logik? (Pipeline-Pass oder Clip?)
- Focus-Management: Welcher Node hat den Tastatur-Fokus? Tab-Reihenfolge?

### Stride-Integration
- Wie fließen Render-Commands aus Clips in Strides Render-Pipeline?
- Wie integriert sich der TextureCompositor mit Strides RenderContext?
- Wo werden GPU-Resources (Textures, Buffers) alloziert/freigegeben im Clip-Lifecycle?
- Wie funktioniert die Texture-Composition in Stride (RenderTarget-Ping-Pong, Compute, etc.)?

### ImGui-UI: Kompatibilität mit VL.ImGui
- Die Compositor-UI (Layer-Stack, Timeline, Inspector, Transport) soll mit VL.ImGui gebaut werden
- VL.ImGui lebt im Immediate-Mode-Paradigma — wie integriert sich das mit unserem immutablen SceneGraph?
- Die UI soll **im Live-Patch leben können** — der User kann die UI neben seinem Content-Patch sehen und nutzen
- Wie fließen UI-Interaktionen (Slider-Drag, Clip-Reorder, Keyframe-Editing) als SceneEdits zurück?
- Brauchen wir eine Abstraktionsschicht zwischen ImGui-Widgets und SceneGraph-Operationen?
- Wie handhabt die UI gleichzeitig den immutablen Graph (Daten anzeigen) und SceneEdits (Daten ändern)?
- Performance: ImGui bei vielen Clips in der Timeline — Culling/Virtualisierung nötig?
- Können User eigene Inspector-Widgets pro Component-Typ registrieren?
- Wie integriert sich die UI mit VLs Editor (Fenster-Management, Docking)?

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

### StateMachine-Crossfading
- Wie genau überblendet man zwischen States wenn verschiedene Clips aktiv/inaktiv werden?
- Crossfade-Duration und -Kurve pro Transition?
- Was passiert wenn ein State gewechselt wird während ein Crossfade läuft?

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
