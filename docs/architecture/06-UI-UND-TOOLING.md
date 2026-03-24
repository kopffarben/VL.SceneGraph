# UI und Tooling

## NodeFactories fuer VL

### Zweck

Dynamisch VL-Nodes erzeugen basierend auf registrierten Components. Nutzt VLs `IVLNodeDescriptionFactory`.

### Component Get/Set/Has Nodes

Fuer jede registrierte Component werden automatisch drei Nodes erzeugt:

- `GetTransform3D`: SceneNode -> Transform3D?
- `SetTransform3D`: (SceneNode, Transform3D) -> SceneNode
- `HasTransform3D`: SceneNode -> bool

### Traversal Nodes

Fuer jede Component gibt es spezialisierte Iteratoren:

- `ForEachWithTransform3D`: Iteriert alle Nodes mit Transform
- `ForEachWithAudioEmitter`: Iteriert alle Nodes mit Audio
- Etc. fuer jede Component

### Dynamischer Inspector

Ein Process Node der seine Pins basierend auf den angehangenen Components aendert. Zur Laufzeit werden die Pins dynamisch erzeugt, sodass der Inspector immer die aktuell vorhandenen Components eines Nodes anzeigt.

---

## VL-Integration und API-Design

### Kein `abstract`/Vererbung auf VL-Seite

VL-Klassen erben von `VLObject` — keine eigene Vererbungshierarchie moeglich. Stattdessen:

- Composition ueber Components (`IComponent` Interface)
- Extension Methods als VL-Nodes
- SceneBuilder (fluent API) fuer ergonomischen Szenenaufbau

### Extension Methods

Extension Methods bilden die primaere API fuer VL-Nutzer:

```csharp
public static class ComponentExtensions
{
    public static SceneNode SetTransform(this SceneNode node, Matrix4x4 matrix) => ...;
    public static Matrix4x4 GetTransform(this SceneNode node) => ...;
    public static SceneNode LookAt(this SceneNode node, string targetId) => ...;
    public static SceneNode FollowNode(this SceneNode node, string sourceId, float weight) => ...;
}
```

### SceneBuilder (Fluent API)

```csharp
SceneBuilder.Begin("root")
    .Transform(Matrix4x4.Identity)
    .BeginGroup("lights")
        .AddLeaf("spot1", new SpotLight(...), new Transform3D(...))
    .EndGroup()
    .Build();
```

### CompositorRuntime als VL Process Node

Die `CompositorRuntime` ist der zentrale Einstiegspunkt fuer VL. Sie kapselt die gesamte Frame-Pipeline und das Double-Buffering.

```csharp
public sealed class CompositorRuntime : IDisposable
{
    public void Update(
        SceneGraph scene, float time, bool enabled,
        Frustum? frustum, InputContext inputContext,
        out ReadOnlyMemory<object?> outputs,
        out ImmutableArray<SceneEdit> clipEdits,
        out int nodeCount, out string frameStats);
}
```

**Parameter:**

| Parameter | Beschreibung |
|-----------|-------------|
| `scene` | Der aktuelle immutable Szenegraph |
| `time` | Globale Zeit in Sekunden |
| `enabled` | Master-Enable fuer die gesamte Pipeline |
| `frustum` | Optionales Frustum fuer Visibility-Culling |
| `inputContext` | Externe Inputs (MIDI, OSC, Tablet, etc.) |
| `outputs` | Clip-Outputs (Texturen, Audio-Streams, etc.) |
| `clipEdits` | Von Clips angeforderte Graph-Aenderungen (SceneEdit Records) |
| `nodeCount` | Anzahl der aktiven Nodes im aktuellen Frame |
| `frameStats` | Debug-String mit Pipeline-Timing |

### Convenience Outputs

```csharp
public Matrix4x4 GetWorldTransform(string nodeId);
public bool IsVisible(string nodeId);
public BoundingBox GetWorldBounds(string nodeId);
```

### VL-Dokument Kategorien

Forwarding-Referenzen auf die C#-Assembly mit Kategorie-Zuweisungen:

- `"SceneGraph"` — Core Types + Builder
- `"SceneGraph.Traverse"` — ForEach, Rewrite, FindAll, Fold
- `"SceneGraph.Runtime"` — CompositorRuntime

---

## Undo-System

### Scope-basiert mit globalem Fallback

Jedes Editor-Panel hat seinen eigenen Edit-Scope. **Ctrl+Z** wirkt im fokussierten Panel. **Ctrl+Shift+Z** ist global.

### Edit Record

```csharp
public record Edit(
    string Id, string ScopeId, string Description,
    float Timestamp, EditCategory Category,
    SceneGraph Before, SceneGraph After);

public enum EditCategory : byte
{
    Structure, Parameter, Timeline, Constraint,
    StateMachine, Recording, Preset, Blending, Playback
}
```

### Standard-Scopes

| Scope | Bereich |
|-------|---------|
| `layers` | Layer-Stack (add/remove/reorder/visibility) |
| `timeline` | Timeline-Editing (Keyframes, Cues) |
| `parameters` | Inspector (Slider, Werte) |
| `constraints` | Constraint-Panel (Verbindungen) |
| `statemachine` | FSM-Editor |
| `browser` | Node-Browser (neuen Clip hinzufuegen) |
| `transport` | Playback-Controls |

### Merge-Window fuer Slider-Drags

Slider-Drags werden zu einem einzigen Edit zusammengefasst (konfigurierbar, Default 500ms). `BeginEdit` bei MouseDown, `EndEdit` bei MouseUp.

### Scope-Undo vs. Global-Undo

- **Scope-Undo** setzt nur die Nodes zurueck die der Scope geaendert hat. Nutzt `ReferenceEquals`-basiertes Diffing dank Structural Sharing.
- **Global-Undo** setzt den gesamten Graphen zurueck.

---

## ImGui-Compositor-UI

### Layout

```
+----------------------------------------------------------+
| Transport Bar (Play/Pause/Stop, Zeit, BPM, REC, CPU)     |
+------------------+---------------------------------------+
| Layer Stack      | Viewport / Preview                     |
| (Hierarchie,     |                                        |
|  Solo/Mute,      |                                        |
|  Opacity)        |                                        |
|                  |                                        |
+------------------+---------------------------------------+
| Timeline (Clips / Keyframes / Activity)                   |
+--------------------------+-------------------------------+
| Inspector                | States / Presets               |
| (Parameter, Constraints, | (StateMachine-Vis,             |
|  Presets, ControlSource) |  Preset-Buttons, Morph)        |
+--------------------------+-------------------------------+
```

### UI-State (getrennt vom Scene-State)

Der UI-State ist vollstaendig getrennt vom logischen Scene-State. Der Szenegraph bleibt rein — UI-Belange wie Selektion, Scroll-Position und Drag-Operationen leben ausserhalb.

```csharp
public sealed class CompositorUIState
{
    public string? SelectedNodeId;
    public HashSet<string> MultiSelection;
    public float TimelineZoom, TimelineScroll;
    public bool TimelineFollowPlayhead;
    public TimelineViewMode TimelineMode;
    public DragOperation? CurrentDrag;
    // etc.
}
```

### Interaktions-Prinzipien

| Eingabe | Aktion |
|---------|--------|
| Linker Mausklick | Selektion |
| Doppelklick | Detail-Editor (Timeline -> Keyframe-View) |
| Rechtsklick | Kontextmenues |
| Alt+Drag | Timeline-Scrubbing |
| Scroll-Wheel | Zoom (zum Mauszeiger hin) |
| Drag & Drop | Clips zwischen Layern, Presets auf Clips, Patches aus Browser |
| Ctrl+Z | Scope-Undo |
| Ctrl+Shift+Z | Global-Redo |
| Ctrl+S | Save |
| Space | Play/Pause |

### Farbsprache

**Zustand:**
- Gruen = aktiv
- Gelb = fading
- Grau = inaktiv

**Stream-Typ:**
- Blau = Textur
- Gruen = Audio
- Orange = Render
- Grau = Daten

**Markierungen:**
- Rot = Keyframe-Markierung
- Cyan = Constraint-Verbindung

### Timeline-Modi

| Modus | Beschreibung |
|-------|-------------|
| **Clips** | Clip-Bloecke auf der Timeline (authored: feste Bloecke; generative: aufgezeichnete History) |
| **Keyframes** | Kurven-Editor des selektierten Clips |
| **Activity** | Activity-Verlaeufe aller Clips |

### Layer-Stack Features

- Drag-Reorder
- Solo/Mute per Layer und per Clip
- Opacity-Slider
- Clip Drop-Zone pro Layer (mit Typ-Validierung via SlotType)
- Rechtsklick-Menue: Duplicate, Delete, Save as Preset, Bypass

### Inspector Features

- Parameter-Widgets basierend auf Typ (float -> DragFloat, Color -> ColorEdit, etc.)
- **[K]-Button** pro Parameter fuer Keyframe-Toggle
- **Constraint-Indikator** bei verlinkten Parametern
- Preset-Buttons mit Morph-Slider
- ControlSource-Dropdown (Manual, MIDI, OSC, Expression)
