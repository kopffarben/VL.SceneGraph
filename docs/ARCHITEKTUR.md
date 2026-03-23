# VL.SceneGraph — Architektur-Dokumentation

## Compositor / Show-Control-System für vvvv/VL

**Stand: März 2026**
**Autor: Johannes Schmidt (Kopffarben)**

---

## Inhaltsverzeichnis

1. [Vision und Überblick](#1-vision-und-überblick)
2. [Architektur-Schichten](#2-architektur-schichten)
3. [Immutabler Baum mit Structural Sharing](#3-immutabler-baum-mit-structural-sharing)
4. [Component-System](#4-component-system)
5. [Gerichteter Graph als Overlay](#5-gerichteter-graph-als-overlay)
6. [Slot-Typsystem und Hierarchie-Validierung](#6-slot-typsystem-und-hierarchie-validierung)
7. [Flache BFS-Repräsentation (Performance-Kern)](#7-flache-bfs-repräsentation-performance-kern)
8. [Multi-Pass Frame-Pipeline](#8-multi-pass-frame-pipeline)
9. [Stream-Routing und Datenfluss](#9-stream-routing-und-datenfluss)
10. [Constraint-System](#10-constraint-system)
11. [Clip-System und Lifecycle](#11-clip-system-und-lifecycle)
12. [Zeit-System und verschachtelte Timelines](#12-zeit-system-und-verschachtelte-timelines)
13. [StateMachine-System](#13-statemachine-system)
14. [Externe Inputs](#14-externe-inputs)
15. [Recording-System](#15-recording-system)
16. [Double-Buffering und Concurrency](#16-double-buffering-und-concurrency)
17. [Memory-Management](#17-memory-management)
18. [Source Generators](#18-source-generators)
19. [NodeFactories für VL](#19-nodefactories-für-vl)
20. [VL-Integration und API-Design](#20-vl-integration-und-api-design)
21. [Serialisierung und Persistenz](#21-serialisierung-und-persistenz)
22. [Preset-System](#22-preset-system)
23. [Undo-System](#23-undo-system)
24. [ImGui-Compositor-UI](#24-imgui-compositor-ui)
25. [Projekt-Struktur](#25-projekt-struktur)
26. [Offene Punkte](#26-offene-punkte)

---

## 1. Vision und Überblick

### Was ist das?

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

### Design-Entscheidungen

- **Immutable-First**: Passt zu VLs Dataflow-Modell. Ermöglicht Undo/Redo, Snapshots, Thread-Sicherheit
- **Keine Vererbung auf VL-Seite**: VL-Klassen erben von `VLObject`, daher Component-basierte Architektur
- **Zwei-Schichten-Modell**: Immutabler logischer Baum (Korrektheit) + flache BFS-Arrays (Performance)
- **Source Generators + NodeFactories**: Eliminieren Boilerplate, nutzen VLs csproj-Hot-Reload
- **Offenes Component-System**: VL-Nutzer können eigene Components als Records definieren

---

## 2. Architektur-Schichten

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

## 3. Immutabler Baum mit Structural Sharing

### SceneNode

Zentrale Datenstruktur. Bewusst als `class` statt `record` wegen VL-Kompatibilität (Equals-Performance, `with`-Einschränkungen). Alternativ als C#-Record wenn VLs Record-Typ `with` von C# unterstützt.

```csharp
public sealed class SceneNode
{
    public string Id { get; }
    public ImmutableArray<SceneNode> Children { get; }
    private readonly ImmutableDictionary<Type, IComponent> _components;

    private SceneNode(
        string id,
        ImmutableArray<SceneNode> children,
        ImmutableDictionary<Type, IComponent> components)
    {
        Id = id;
        Children = children;
        _components = components;
    }

    public static SceneNode Create(string id) => new(id,
        ImmutableArray<SceneNode>.Empty,
        ImmutableDictionary<Type, IComponent>.Empty);

    // Immutable Update Methods
    public SceneNode AddChild(SceneNode child)
        => new(Id, Children.Add(child), _components);
    public SceneNode SetChild(int index, SceneNode child)
        => new(Id, Children.SetItem(index, child), _components);
    public SceneNode RemoveChildAt(int index)
        => new(Id, Children.RemoveAt(index), _components);
    public SceneNode WithChildren(ImmutableArray<SceneNode> children)
        => new(Id, children, _components);
    public SceneNode WithComponent(IComponent component)
        => new(Id, Children, _components.SetItem(component.GetType(), component));
    public SceneNode WithoutComponent<T>() where T : IComponent
        => new(Id, Children, _components.Remove(typeof(T)));

    // Queries
    public T? GetComponent<T>() where T : class, IComponent
        => _components.TryGetValue(typeof(T), out var c) ? (T)c : null;
    public bool HasComponent<T>() where T : IComponent
        => _components.ContainsKey(typeof(T));
    public IEnumerable<IComponent> AllComponents => _components.Values;
    public int ChildCount => Children.Length;
    public bool IsLeaf => Children.IsEmpty;
}
```

### Structural Sharing

Wenn ein Node tief im Baum geändert wird, werden nur Nodes entlang des Pfades zur Wurzel neu erzeugt — alle unveränderten Teilbäume werden per Referenz geteilt (Path Copying).

In einem Baum mit 1 Million Nodes kopiert eine Blatt-Änderung nur ~20 Nodes (O(log n)).

`ReferenceEquals(oldNode, newNode)` dient als **kostenloser Dirty-Flag** — ist true, hat sich nichts geändert.

### Performance-Hinweis: ImmutableArray vs ImmutableList

`ImmutableArray<T>` ist **30x schneller** beim Iterieren als `ImmutableList<T>` (438ns vs 16.915ns für 1000 Elemente). `ImmutableList<T>` nur verwenden wenn häufige Einfügungen/Entfernungen mit Structural Sharing der Flaschenhals sind.

---

## 4. Component-System

### Interface

```csharp
public interface IComponent { }
```

Jeder Node hat ein `ImmutableDictionary<Type, IComponent>` — maximal eine Component pro Typ. Offene Erweiterung: VL-Nutzer können eigene Records die `IComponent` implementieren erstellen.

### Standard-Components (Domain-spezifisch)

**Clip-System:**
- `ClipReference(string PatchId, int PatchVersion)` — Welcher VL-Patch
- `ClipLifetime(float StartTime, float Duration, float FadeIn, float FadeOut, ClipPlayState PlayState)`
- `ClipActivity(float Activity, float LocalTime, bool IsActive)` — Berechneter Wert pro Frame [FlatStorage]
- `ClipParameters(ImmutableDictionary<string, object?> Values)` — Animierbare Werte
- `ControlSource(ControlMode Mode, ...)` — Timeline / FSM / Manual / Expression / Recorded / AlwaysOn
- `LayerBlending(BlendMode Mode, float Opacity, bool Solo, bool Mute)`

**Zeit:**
- `TimeContext(float LocalTime, float LocalDuration, float PlaybackRate, TimeMode Mode, bool IsTimeSource)` [FlatStorage]

**Hierarchie/Typsystem:**
- `PatchDescriptor(SlotType PrimaryInput, SlotType PrimaryOutput, ChildPolicy, AcceptsChildrenOf)`
- `ChildComposition(CompositionMode Mode, BlendMode DefaultBlendMode)`

**Constraints:**
- `LookAtConstraint(NodeHandle Target, Vector3 UpVector)`
- `ParentConstraint(NodeHandle Source, float Weight)`
- `PositionConstraint(NodeHandle Target, Vector3 Offset)`
- `ParameterLink(NodeHandle SourceClip, string SourceParameter, string TargetParameter, LinkMode, float Weight)`
- `ActivityLink(NodeHandle SourceClip, ActivityLinkMode Mode, float Weight)`
- `TempoSync(NodeHandle TempoSource, float Multiplier, float PhaseOffset)`
- `ExpressionConstraint(string TargetParameter, string Expression, ImmutableArray<ExpressionBinding> Bindings)`

**Timeline:**
- `ParameterTimeline(ImmutableDictionary<string, AnimationTrack> Tracks)`
- `CueList(ImmutableArray<Cue> Cues)`

**StateMachine:**
- `StateMachineDefinition(ImmutableArray<StateDefinition> States, ImmutableArray<TransitionDefinition> Transitions, string InitialState)`
- `StateMachineState(string CurrentState, float TimeInState, string? PendingTransition, float TransitionProgress)` [FlatStorage]

**Externe Inputs:**
- `ExternalInput(InputSourceKind Source, string? Address, Type OutputType, float SmoothingFactor, float InputMin, float InputMax, float OutputMin, float OutputMax)`

**Recording:**
- `Recordable(RecordMode Mode, RecordTarget Target, bool IsRecording, bool IsPlaying, int MaxFrames, RecordSampling Sampling)`
- `RecordingPlayback(string RecordingId, float PlaybackSpeed, float StartOffset, TimeMode LoopMode, float BlendWithLive)`
- `DrawingRecordable(bool RecordStrokes, bool RecordUndos, int MaxStrokes)`

**Sonstige:**
- `BypassFlag(bool Bypassed)` — Effekt überspringen
- `FeedbackLoop(bool UseFeedback, float FeedbackAmount, int DelayFrames)`
- `InstanceOf(string TemplateId)` — Referenz auf ein Template
- `VisibilityFlag(bool Visible)` [FlatStorage]

### Source-Generator-Attribut

Components werden über Attribute markiert. Der Source Generator erzeugt automatisch typisierte Accessors, SoA-Arrays und Compile-Code:

```csharp
[SceneComponent(FlatStorage = true)]  // → SoA-Array im FlatSceneGraph
public partial record Transform3D(Matrix4x4 Matrix);

[SceneConstraint(WritesTo = "Transform3D", DependsOn = new[] { "Transform3D" })]
public partial record LookAtConstraint(
    [property: NodeReference] NodeHandle Target,
    Vector3 UpVector);
```

---

## 5. Gerichteter Graph als Overlay

### Struktur

Die Hierarchie (Baum) definiert die **räumliche/logische Struktur**. Zusätzliche gerichtete Kanten definieren **Verarbeitungsreihenfolge und Querverweise**:

```csharp
public readonly record struct NodeHandle(string NodeId);

public record Edge(NodeHandle Source, NodeHandle Target, EdgeKind Kind);

public enum EdgeKind
{
    Hierarchy,      // Parent-Child (implizit im Baum)
    Reference,      // LookAt, Constraint, etc.
    DataFlow,       // Audio-Routing, Signal-Flow, Primary Stream Routing
    Dependency,     // "Muss vor X berechnet werden"
    Trigger         // StateMachine-Trigger
}

public sealed record SceneGraph(
    SceneNode Root,
    ImmutableDictionary<string, SceneNode> Templates,
    ImmutableList<Edge> Edges)
{
    public static SceneGraph Create(SceneNode root) => new(root,
        ImmutableDictionary<string, SceneNode>.Empty,
        ImmutableList<Edge>.Empty);

    public SceneGraph WithRoot(SceneNode root) => new(root, Templates, Edges);
    public SceneGraph DefineTemplate(string name, SceneNode template)
        => new(Root, Templates.SetItem(name, template), Edges);
    public SceneGraph Connect(string sourceId, string targetId, EdgeKind kind)
        => new(Root, Templates, Edges.Add(new Edge(new NodeHandle(sourceId), new NodeHandle(targetId), kind)));
    public SceneGraph Disconnect(string sourceId, string targetId, EdgeKind kind)
        => new(Root, Templates, Edges.Remove(new Edge(new NodeHandle(sourceId), new NodeHandle(targetId), kind)));
}
```

### Handle-Index

Handles (symbolische IDs) statt direkter Referenzen. Ein `HandleIndex` mapped `NodeHandle → int` (flacher Array-Index):

```csharp
public sealed class HandleIndex
{
    private readonly Dictionary<NodeHandle, int> _handleToFlat;

    public HandleIndex(FlatSceneGraph flat) { /* Build from flat.Handles[] */ }
    public int Resolve(NodeHandle handle) => _handleToFlat.TryGetValue(handle, out int idx) ? idx : -1;
}
```

### Edge-Index

Für performante Abfragen: Adjacency-Lists als `ImmutableDictionary<NodeHandle, ImmutableArray<Edge>>` für Incoming und Outgoing Edges. Unterstützt topologische Sortierung.

### Templates / Instancing

Shared Subtrees über Indirektion: Der Baum bleibt ein Baum, Nodes referenzieren Templates per `InstanceOf`-Component. Templates leben im `SceneGraph.Templates`-Dictionary. Auflösung beim Traversieren.

---

## 6. Slot-Typsystem und Hierarchie-Validierung

### Problem

Nicht jeder Patch kann Kind jedes anderen Patches sein. Ein PostFX braucht Textur-Input, ein Audio-Effekt braucht Audio-Stream.

### SlotType

```csharp
public readonly record struct SlotType(string Name)
{
    public static readonly SlotType Texture = new("Texture");
    public static readonly SlotType AudioBuffer = new("AudioBuffer");
    public static readonly SlotType RenderCommand = new("RenderCommand");
    public static readonly SlotType Mesh = new("Mesh");
    public static readonly SlotType Spread = new("Spread");
    public static readonly SlotType Value = new("Value");
    public static readonly SlotType Trigger = new("Trigger");
    public static readonly SlotType Any = new("Any");
    public static readonly SlotType None = new("None");
}
```

Kompatibilität: `SlotCompatibility.IsCompatible(provider, consumer)` — prüft ob ein Output-Typ an einem bestimmten Input-Slot andocken kann. Erweiterbar für eigene Typen.

### PatchDescriptor

Jeder Patch deklariert sein Profil:

```csharp
[SceneComponent]
public partial record PatchDescriptor(
    SlotType PrimaryInput,
    SlotType PrimaryOutput,
    ImmutableArray<SlotRequirement> AdditionalInputs,
    ImmutableArray<SlotOutput> AdditionalOutputs,
    ChildPolicy ChildPolicy,
    ImmutableArray<SlotType> AcceptsChildrenOf);

public enum ChildPolicy : byte
{
    NoChildren,         // Leaf-Node (Generator)
    OrderedChildren,    // Kinder werden in Reihenfolge kombiniert (Layer)
    SingleChild,        // Genau ein Kind (Effekt)
    NamedSlots,         // Kinder docken an benannte Slots an
    AnyChildren         // Beliebig (Group)
}
```

### ChildComposition

Bestimmt wie ein Parent die Outputs seiner Kinder kombiniert:

```csharp
public enum CompositionMode : byte
{
    Parallel,       // Kinder rendern unabhängig, Parent mischt
    Sequential,     // Output von Kind N → Input von Kind N+1
    Additive,       // Alle Outputs werden addiert
    Custom          // VL-Patch entscheidet
}
```

### Validierung

`HierarchyValidator.CanBeChildOf(child, parent)` prüft: Akzeptiert der Parent Kinder? Sind die Slot-Typen kompatibel? Ist die ChildPolicy eingehalten? Gibt `ValidationResult` mit Severity (Ok/Info/Warning/Error) und Nachricht zurück.

`HierarchyValidator.ValidateTree(root)` validiert den gesamten Baum. Wird für ImGui-UI-Feedback genutzt (rote Markierung, Drag&Drop-Filterung).

---

## 7. Flache BFS-Repräsentation (Performance-Kern)

### SoA-Layout (Struct of Arrays)

Separate Arrays pro Eigenschaft statt einem Array of Structs. Vorteile: Cache-Lokalität (nur benötigte Daten werden geladen), Prefetcher-Freundlichkeit, keine Verschwendung durch ungenutztes Padding.

```csharp
public sealed class FlatSceneGraph : IDisposable
{
    public int NodeCount;
    public int Capacity;

    // Strukturelle Arrays (ändern sich bei Tree-Rebuild)
    public int[] ParentIndex;         // parent[i] < i (BFS-Invariante)
    public int[] FirstChildIndex;
    public int[] ChildCount;
    public int[] Depth;               // Tiefe im Baum (Root = 0)
    public NodeHandle[] Handles;      // Mapping zum logischen Node

    // Status-Arrays
    public byte[] DirtyFlags;         // Bitfield pro Node
    public byte[] Visibility;

    // Level-basierte Metadaten
    public int[] LevelOffsets;        // LevelOffsets[d] = erster Index auf Tiefe d
    public int LevelCount;

    // Stream-Routing
    public StreamSlot[] PrimaryInputSlot;
    public StreamSlot[] PrimaryOutputSlot;
    public int[] CompositionInputStart;
    public int[] CompositionInputCount;
    public StreamSlot[] CompositionInputSlots;
    public float[] CompositionInputWeights;
    public BlendMode[] CompositionInputBlendModes;

    // Weitere SoA-Arrays werden vom Source Generator erzeugt
    // basierend auf [SceneComponent(FlatStorage = true)]
}
```

### BFS-Ordnung

Breitensuche (Level für Level): Root → Kinder → Enkel → ...

Zentrales Invariant: **`ParentIndex[i] < i`** — der Parent steht immer vor dem Kind im Array.

Konsequenz: Eine einzige `for`-Schleife von vorne nach hinten (Top-Down) garantiert dass der Parent-Zustand schon berechnet ist. Rückwärts (Bottom-Up) garantiert dass alle Kinder schon verarbeitet sind.

### BFS-Compile

```csharp
public void Compile(SceneNode root, ImmutableDictionary<string, SceneNode> templates, FlatSceneGraph target)
{
    // Queue-basierte Breitensuche
    // Pro Node: ParentIndex, Depth, Handle, FirstChildIndex, ChildCount berechnen
    // LevelOffsets: Wo beginnt jedes Level im Array?
    // Template-Auflösung: InstanceOf → Template-Children einmischen
    // Arrays aus Pool (nicht neu allozieren)
}
```

### Level-basierte Parallelisierung

`LevelOffsets` ermöglicht Parallelisierung: Alle Nodes auf demselben Level können parallel verarbeitet werden (nur Abhängigkeiten zu Parents, die auf dem vorherigen Level liegen). Threshold: Ab 256 Nodes pro Level lohnt sich `Parallel.For`.

### Inkrementeller Update

Drei Strategien:

1. **Property-Diff**: `ReferenceEquals` erkennt ob sich die Struktur geändert hat. Wenn nur Properties geändert: In-Place Update in flachen Arrays
2. **Subtree-Swap**: Wenn nur ein Teilbaum getauscht wird und die Größe gleich bleibt: In-Place kopieren
3. **Full Rebuild**: Bei strukturellen Änderungen (Node add/remove): Komplett neu kompilieren

---

## 8. Multi-Pass Frame-Pipeline

### Reihenfolge (kritisch!)

```csharp
public void Frame(SceneGraph scene, float globalTime, float deltaTime,
    InputContext inputContext, Frustum? frustum)
{
    // Phase 1: Struktur synchronisieren
    SyncStructure(scene);

    // Phase 2: Zeit-Propagation (Top-Down)
    TimePass.PropagateTime(_back, globalTime, deltaTime);

    // Phase 3: Externe Inputs (OSC, MIDI, Audio, Sensoren)
    ExternalInputPass.Evaluate(_back, inputContext);

    // Phase 4: Recording Playback
    RecordingPass.Playback(_back, _recordingLibrary, _handleIndex);

    // Phase 5: StateMachines
    StateMachinePass.Evaluate(_back, deltaTime, inputContext);

    // Phase 6: Activity (Fade-In/Out, ControlMode-Dispatch)
    ActivityPass.UpdateActivity(_back, globalTime);

    // Phase 7: Timeline-Keyframes → ClipParameters
    TimelinePass.EvaluateKeyframes(_back);

    // Phase 8: Constraints (topologisch sortiert)
    ConstraintSolver.Resolve(_back);

    // Phase 9: Stream-Routing
    _streamPool.Cleanup();
    StreamRoutingPass.ComputeRouting(_back, _streamPool, _edgeIndex, _handleIndex);

    // Phase 10: Evaluation-Order + Clip-Evaluation
    _clipEvaluator.ComputeEvaluationOrder(_back);
    SyncClipInstances(scene);
    _clipEvaluator.Evaluate(_back, deltaTime);

    // Phase 11: Recording Capture
    RecordingPass.Record(_back, _activeRecorders, globalTime);

    // Phase 12: Optional 3D-Passes
    if (frustum.HasValue)
    {
        TransformPass.Execute(_back);
        BoundsPass.Execute(_back);
        VisibilityPass.Execute(_back, frustum.Value);
    }

    // Phase 13: Activity Logging (für Timeline-UI)
    ActivityLogger.Record(_back, globalTime);

    // Phase 14: Dirty Clear + Buffer Swap
    FlatPasses.ClearDirtyFlags(_back);
    (_front, _back) = (_back, _front);
    _streamPool.SwapBuffers();
}
```

### Top-Down Pass (Vorwärts-Iteration)

```csharp
// Transform-Propagation, Zeit-Propagation, Visibility-Culling
for (int i = 1; i < nodeCount; i++)
{
    int p = parentIndex[i];
    // parentIndex[p] < i garantiert: Parent schon berechnet
    worldTransform[i] = localTransform[i] * worldTransform[p];
}
```

### Bottom-Up Pass (Rückwärts-Iteration)

```csharp
// Bounding-Box-Aggregation, Buffer-Füllung
for (int i = nodeCount - 1; i >= 1; i--)
{
    int p = parentIndex[i];
    bounds[p] = BoundingBox.Merge(bounds[p], bounds[i]);
}
```

### Dirty-Propagation

Dirty-Flags propagieren Top-Down: Wenn Parent dirty, werden alle Kinder auch dirty. In einem typischen Frame ändern sich ~5% der Nodes → 95% der Arbeit wird übersprungen.

---

## 9. Stream-Routing und Datenfluss

### StreamPool

Verwaltet typisierte Daten-Slots die zwischen Nodes fließen:

```csharp
public readonly record struct StreamSlot(int Index);

public sealed class StreamPool
{
    private SlotType[] _types;
    private object?[] _data;
    private int[] _refCount;

    public StreamSlot Allocate(SlotType type);
    public void Write(StreamSlot slot, object? data);
    public T? Read<T>(StreamSlot slot) where T : class;
    public void AddRef(StreamSlot slot);
    public void Release(StreamSlot slot);
    public void Cleanup(); // Nicht mehr referenzierte Slots freigeben
}
```

### Routing-Logik

1. **Output-Slots allozieren** für alle aktiven Nodes
2. **Input-Routing** bestimmen pro Node:
   - Zuerst: Explizite Edge vorhanden? → Hat Vorrang
   - Sonst: Hierarchie-basiertes Routing
     - **Sequential**: Input vom vorherigen aktiven Sibling (oder Parent)
     - **Parallel**: Kein Input von Siblings, ggf. Parent-Input
3. **Composition-Inputs** für Parents sammeln (Output-Slots aller aktiven Kinder)

### Composition-Modi

**Parallel**: Alle Kinder rendern unabhängig. Parent mischt die Ergebnisse (Layer-Compositing mit BlendMode und Opacity pro Kind).

**Sequential**: Output von Kind N ist Input von Kind N+1 (Effekt-Kette). Das letzte Kind in der Kette liefert den Output des Parents.

### Evaluation-Order

Bottom-Up: Blätter zuerst, dann aufwärts. Bei Sequential: Kinder in Reihenfolge. Inaktive Nodes werden übersprungen.

### Solo / Mute / Bypass

- **Mute**: Kind-Output wird ignoriert (nicht in Composition)
- **Solo**: Nur dieses Kind wird in die Composition einbezogen
- **Bypass**: In Sequential-Chain: Input wird direkt als Output durchgereicht

### Feedback-Loop

Output eines Frames wird zum Input des nächsten Frames. Funktioniert mit Double-Buffering: Front-Buffer hat Outputs vom letzten Frame.

---

## 10. Constraint-System

### Constraint-Typen

- **ParameterLink**: `Clip A.Parameter X → Clip B.Parameter Y` (Direct, Inverse, Add, Multiply, Remap, Expression)
- **ActivityLink**: Follow, Inverse, Trigger, ChainAfter, Sync
- **TempoSync**: Multiplier + PhaseOffset relativ zu einer Tempo-Quelle
- **ExpressionConstraint**: Mathematische Ausdrücke (`sin(time * 2) * source.opacity`)
- **LookAtConstraint**: Rotation auf Ziel ausrichten (für 3D-Content)
- **ParentConstraint**: Position/Rotation eines anderen Nodes folgen
- **PositionConstraint**: Position mit Offset

### Flache Constraint-Arrays (SoA)

Pro Constraint-Typ separate Arrays. Werden beim Compile aus den Components extrahiert:

```csharp
public sealed class FlatConstraints
{
    public int LookAtCount;
    public int[] LookAt_OwnerIndex;
    public int[] LookAt_TargetIndex;
    public Vector3[] LookAt_UpVector;
    // ... etc. für jeden Constraint-Typ
}
```

### Topologische Sortierung

Constraints können voneinander abhängen (Constraint A schreibt Node X, Constraint B liest Node X → A muss vor B laufen). Kahn's Algorithmus berechnet die Ausführungsreihenfolge. Wird nur bei strukturellen Änderungen neu berechnet, nicht pro Frame.

Zyklen werden erkannt und als Fehler gemeldet.

### Constraint-Solver

Führt Constraints in topologisch sortierter Reihenfolge aus. Nach jedem Constraint der eine Transform ändert: `PropagateTransformToDescendants()` — alle Nachkommen neu berechnen.

---

## 11. Clip-System und Lifecycle

### IClipInstance Interface

Das Interface das VL-Patches implementieren müssen um als Clip nutzbar zu sein:

```csharp
public interface IClipInstance : IDisposable
{
    PatchDescriptor Descriptor { get; }
    IReadOnlyList<ParameterDescriptor> Parameters { get; }

    void SetPrimaryInput(object? data);
    object? GetPrimaryOutput();
    void SetInput(string name, object? data);
    object? GetOutput(string name);

    void SetParameters(IReadOnlyDictionary<string, object?> values);
    void SetActivity(float activity);
    void SetLocalTime(float localTime);
    void Evaluate(float deltaTime);

    object? CaptureState();
    void RestoreState(object? state);
}
```

### ParameterDescriptor

```csharp
public record ParameterDescriptor(
    string Name, Type ValueType, object? DefaultValue,
    object? MinValue, object? MaxValue,
    string? Group, ParameterFlags Flags);

[Flags]
public enum ParameterFlags
{
    None = 0, Animatable = 1, Automatable = 2,
    Preset = 4, Hidden = 8, ReadOnly = 16
}
```

### Clip-Lifecycle

```
Stopped → FadingIn → Playing → FadingOut → Stopped
                ↕         ↕
              Paused     Paused
```

Activity-Wert (0..1) inkludiert Fade-In/Out-Rampe und wird mit Parent-Activity multipliziert.

### ControlMode

Bestimmt woher die Steuerung eines Clips kommt:

```csharp
public enum ControlMode : byte
{
    Timeline,       // Feste Start/End-Zeit
    StateMachine,   // FSM entscheidet
    Manual,         // User-Trigger (UI, OSC, MIDI)
    Expression,     // Bedingung ("audioLevel > 0.5")
    ParentDriven,   // Aktiv wenn Parent aktiv
    AlwaysOn,       // Immer aktiv
    Constraint,     // Activity kommt über ActivityLink
    Recorded,       // Spielt eine Aufnahme ab
    RecordingLive   // Zeichnet gerade auf
}
```

Mischungen sind möglich: Teile der Show Timeline-gesteuert, andere FSM-gesteuert, andere manuell.

---

## 12. Zeit-System und verschachtelte Timelines

### TimeContext

Jeder Node kann seine eigene Zeitbasis haben oder die des Parents erben:

```csharp
[SceneComponent(FlatStorage = true)]
public partial record TimeContext(
    float LocalTime, float LocalDuration, float PlaybackRate,
    TimeMode Mode, bool IsTimeSource);

public enum TimeMode : byte { Once, Loop, PingPong, Hold, Free }
```

### Zeit-Propagation (Top-Down)

```
[Show] ← Master-Timeline (0:00 → ∞)
  [Akt 1] ← Sub-Timeline (0:00 → 5:00 relativ zum Parent)
    [Szene A] ← Sub-Sub-Timeline (0:00 → 2:30)
      [Clip "Stars"] ← Keyframes relativ zu Szene A
```

Wenn `Akt 1` auf halbe Geschwindigkeit gesetzt wird, laufen alle Clips darin automatisch halb so schnell. Looping, PingPong etc. pro Ebene konfigurierbar.

### Keyframes

```csharp
public record AnimationTrack(
    string ParameterName, Type ValueType,
    ImmutableArray<Keyframe> Keyframes,
    InterpolationMode DefaultInterpolation);

public record Keyframe(
    float Time, object Value, InterpolationMode Interpolation,
    float? TangentIn, float? TangentOut);

public enum InterpolationMode : byte
{
    Step, Linear, Smooth, Bezier, Spring, Custom
}
```

Evaluation: Binary Search für das richtige Keyframe-Paar, dann Interpolation.

### Cue-System

```csharp
public record Cue(string Name, float Time, CueAction Action,
    ImmutableDictionary<string, object?>? ParameterOverrides);

public enum CueAction : byte
{
    Play, Stop, Pause, Resume, FadeIn, FadeOut,
    GotoAndPlay, GotoAndStop, Trigger
}
```

---

## 13. StateMachine-System

### Definition

```csharp
public record StateDefinition(
    string Name,
    ImmutableArray<string> ActiveClipIds,
    ImmutableDictionary<string, ImmutableDictionary<string, object?>> ClipParameters,
    float MinDuration,
    ImmutableArray<CueAction> OnEnter,
    ImmutableArray<CueAction> OnExit);

public record TransitionDefinition(
    string FromState, string ToState,
    TransitionCondition Condition,
    float CrossfadeDuration, InterpolationMode CrossfadeMode);

public record TransitionCondition(
    TransitionTrigger Trigger,
    string? Expression, float? AfterDuration,
    string? TriggerName);

public enum TransitionTrigger : byte
{
    Manual, AfterDuration, OnComplete, Expression,
    Random, OnBeat, External
}
```

### Laufzeit-Zustand

```csharp
[SceneComponent(FlatStorage = true)]
public partial record StateMachineState(
    string CurrentState, float TimeInState,
    string? PendingTransition, float TransitionProgress);
```

### Generative vs. Authored Timeline

- **Authored**: Klassische Keyframes, feste Start/End-Zeiten, User hat das so angelegt
- **Generative/Live**: FSM trifft Entscheidungen, ActivityLogger zeichnet auf was wann passiert, Timeline-UI zeigt scrollbare History

---

## 14. Externe Inputs

### Quellen

```csharp
public enum InputSourceKind : byte
{
    OSC, MIDI_CC, MIDI_Note, MIDI_Velocity, DMX,
    AudioLevel, AudioBeat, AudioFFT,
    GamepadAxis, GamepadButton,
    MousePosition, TouchInput,
    CustomPatch, Clock, Random, LFO
}
```

### Integration

Externe Inputs sind Nodes im Baum mit Output-Werten. Der bestehende Constraint-Solver routet die Werte an Clip-Parameter — kein Sondersystem nötig.

```csharp
[SceneComponent]
public partial record ExternalInput(
    InputSourceKind Source, string? Address, Type OutputType,
    float SmoothingFactor,
    float InputMin, float InputMax,
    float OutputMin, float OutputMax);
```

### InputContext

Von VL befüllt, enthält Referenzen auf Audio-Analyzer, MIDI-Input, OSC-Receiver, BPM etc.

---

## 15. Recording-System

### Ebenen

1. **Input Recording**: Rohe Daten (Stift-Positionen, MIDI-CCs, OSC). Kompakt, verlustfrei.
2. **Parameter Recording**: Pro Frame alle Parameter-Werte. Medium.
3. **State Recording**: Kompletter Szenegraph-Snapshot (dank Immutability billig).
4. **Output Recording**: Gerenderte Texturen, Audio-Streams. Groß, braucht Kompression.

### TrackRecorder<T>

Zeitindizierte Werte mit Binary-Search-Zugriff:

```csharp
public sealed class TrackRecorder<T> where T : struct
{
    private float[] _timestamps;
    private T[] _values;

    public void Record(float time, T value, RecordSampling sampling);
    public T Evaluate(float time, InterpolationMode interpolation);
    public void TrimBefore(float time);
    public void TrimAfter(float time);
    public void Simplify(float tolerance); // Douglas-Peucker
}
```

### Stroke Recording (Zeichnungen)

Spezialisierter Recorder für Julia's Live-Drawing:

```csharp
public readonly record struct StrokePoint(
    float Time, Vector2 Position, float Pressure,
    float Tilt, float Rotation, Vector2 Velocity);

public record StrokeData(
    int StrokeId, float StartTime, float EndTime,
    ImmutableArray<StrokePoint> Points, StrokeBrush Brush);
```

Features: Progressive Wiedergabe (Strich wächst), Undo/Redo-Tracking, Delta-Encoding für Kompression.

### Recording als Clip-Ersetzung

Eine Aufnahme kann einen Live-Clip ersetzen. `RecordingPlayback`-Component mit `BlendWithLive` (0 = nur Recording, 1 = nur Live). Crossfade zwischen Live und Aufnahme möglich.

### Workflows

1. **Probe aufzeichnen → Show abspielen**: Julia malt in der Probe, Aufnahme wird in der Show abgespielt
2. **Live mit Fallback**: Wenn Julia aufhört (PenUp > 5s), Crossfade zur Aufnahme im Loop
3. **Geschichtete Aufnahmen**: Base Drawing (Probe Tag 1) + Detail Layer (Probe Tag 2) + Live Accents

### Speicher-Abschätzung

30 Minuten Julia-Zeichnung bei 60fps: ~3.5 MB. Ganze Show (20 Clips, 60 Min): ~30 MB.

### Serialisierung

Binäres Format `.vlrc` mit Delta-Encoding für Timestamps und Stroke-Punkte.

---

## 16. Double-Buffering und Concurrency

### Prinzip

Zwei `FlatSceneGraph`-Instanzen: Front-Buffer (Render-Thread liest) und Back-Buffer (Update-Thread schreibt). Atomarer Swap am Frame-Ende.

```csharp
// Update-Thread arbeitet auf Back-Buffer
// Render-Thread liest vom Front-Buffer
// Am Frame-Ende:
(_front, _back) = (_back, _front);
```

### Immutable Graph als natürliches Double-Buffering

Weil der logische Graph immutable ist, hält der Renderer eine Referenz die unbegrenzt gültig bleibt — keine Locks, keine zerrissenen Reads.

### ReadOnlySpan / ReadOnlyMemory

```csharp
public ReadOnlySpan<Matrix4x4> GetWorldTransforms()
    => _front.WorldTransform.AsSpan(0, _front.NodeCount);
```

`ReadOnlySpan<T>` kommuniziert Immutability-Intent. `Memory<T>` nur wenn Async-Grenzen überschritten werden.

---

## 17. Memory-Management

### Ziel: Zero Allocations im Hot-Path

### ArrayPool

Alle SoA-Arrays kommen aus `ArrayPool<T>.Shared`. `RentedArray<T>` als RAII-Wrapper:

```csharp
public ref struct RentedArray<T>
{
    private readonly T[] _array;
    private readonly ArrayPool<T> _pool;
    public readonly int Length;
    public Span<T> Span => _array.AsSpan(0, Length);
    public void Dispose() => _pool.Return(_array);
}
```

### Drei Stufen für temporäre Buffer

1. **stackalloc** für kleine Buffer (< 1KB)
2. **ArrayPool** für mittlere Buffer (1KB - 1MB)
3. **Persistente Buffer** die über Frames leben

### Closure-Vermeidung

Lambdas die lokale Variablen capturen erzeugen Heap-Objekte. Lösung: Struct-basierte Callbacks mit generischem Interface:

```csharp
public interface INodeProcessor<TState>
{
    void Process(int nodeIndex, FlatSceneGraph graph, ref TState state);
}

public struct AudioProcessor : INodeProcessor<float> { /* kein Heap */ }
```

### Span-basierte Passes

Passes nehmen `Span<T>` statt rohe Arrays — erzwingt Bounds-Checking im Debug, ermöglicht JIT-Optimierungen.

### Memory Budget Tracking

Debug-Modus: Zählt Pool-Rentals/Returns pro Frame. Ziel: Delta = 0 (alles was gemietet wird, wird zurückgegeben).

---

## 18. Source Generators

### Zweck

Eliminieren Boilerplate. Werden bei jedem Save vom Roslyn-Compiler ausgeführt. VL hot-reloaded die generierten Nodes.

### Attribut: [SceneComponent]

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class SceneComponentAttribute : Attribute
{
    public bool FlatStorage { get; set; } = false;
}
```

### Was wird generiert?

Aus `[SceneComponent] public partial record Transform3D(Matrix4x4 Matrix);`:

1. **Typisierte Accessors** auf `SceneNode`:
   ```csharp
   public partial class SceneNode
   {
       public Transform3D? Transform3D => GetComponentDirect(Transform3D.TypeId) as Transform3D;
       public SceneNode WithTransform3D(Matrix4x4 matrix) => WithComponentDirect(Transform3D.TypeId, new Transform3D(matrix));
   }
   ```

2. **SoA-Array-Felder** auf `FlatSceneGraph` (wenn `FlatStorage = true`):
   ```csharp
   public partial class FlatSceneGraph { public Matrix4x4[] Transform3D_Matrix; }
   ```

3. **Compile-Mapping**: Automatisches Extrahieren von Component-Daten in flache Arrays
4. **Sync-Code**: Property-Diff zwischen logischem Node und flachem Array
5. **Pool-Allokation**: Automatisches Rent/Return der generierten Arrays
6. **ComponentRegistry**: Alle bekannten Typen, Name→Type Mapping

### Attribut: [SceneConstraint]

```csharp
[SceneConstraint(WritesTo = "Transform3D", DependsOn = new[] { "Transform3D" })]
public partial record LookAtConstraint([property: NodeReference] NodeHandle Target, Vector3 UpVector);
```

Generiert: Constraint-SoA-Arrays, Handle-Resolution-Code, Abhängigkeits-Metadaten für topologische Sortierung.

### Projekt-Setup

```xml
<!-- VL.SceneGraph.csproj -->
<ItemGroup>
  <ProjectReference Include="../VL.SceneGraph.Generators/VL.SceneGraph.Generators.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

---

## 19. NodeFactories für VL

### Zweck

Dynamisch VL-Nodes erzeugen basierend auf registrierten Components. Nutzt VLs `IVLNodeDescriptionFactory`.

### Component Get/Set/Has Nodes

Für jede registrierte Component automatisch erzeugt:
- `GetTransform3D`: SceneNode → Transform3D?
- `SetTransform3D`: (SceneNode, Transform3D) → SceneNode
- `HasTransform3D`: SceneNode → bool

### Traversal Nodes

- `ForEachWithTransform3D`: Iteriert alle Nodes mit Transform
- `ForEachWithAudioEmitter`: Iteriert alle Nodes mit Audio
- Etc. für jede Component

### Dynamischer Inspector

Ein Process Node der seine Pins basierend auf den angehängten Components ändert.

---

## 20. VL-Integration und API-Design

### Kein `abstract`/Vererbung auf VL-Seite

VL-Klassen erben von `VLObject` — keine eigene Vererbungshierarchie möglich. Stattdessen:
- Composition über Components (IComponent Interface)
- Extension Methods als VL-Nodes
- SceneBuilder (fluent API) für ergonomischen Szenenaufbau

### Extension Methods

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

```csharp
public sealed class CompositorRuntime : IDisposable
{
    public void Update(
        SceneGraph scene, Frustum frustum, float time, bool enabled,
        out ReadOnlyMemory<Matrix4x4> worldTransforms,
        out ReadOnlyMemory<byte> visibility,
        out ReadOnlyMemory<NodeHandle> handles,
        out int nodeCount, out string frameStats);
}
```

### Convenience Outputs

```csharp
public Matrix4x4 GetWorldTransform(string nodeId);
public bool IsVisible(string nodeId);
public BoundingBox GetWorldBounds(string nodeId);
```

### VL-Dokument (.vl)

Forwarding-Referenzen auf die C#-Assembly mit Kategorie-Zuweisungen:
- `"SceneGraph"` → Core Types + Builder
- `"SceneGraph.Traverse"` → ForEach, Rewrite, FindAll, Fold
- `"SceneGraph.Runtime"` → CompositorRuntime

---

## 21. Serialisierung und Persistenz

### Show-Datei (Verzeichnis-basiert)

```
show/
├── show.json           ← Manifest + Settings
├── graph.json          ← Szenegraph (JSON für Git-Diffbarkeit)
├── recordings/         ← Aufnahmen (binär)
│   ├── rec_001.vlrc
│   └── rec_002.vlrc
└── presets/            ← Presets (JSON)
    ├── clip_bloom_warm.json
    └── layer_bg_night.json
```

### Show-Manifest

```csharp
public record ShowFile(
    string Name, string Version,
    DateTime CreatedAt, DateTime ModifiedAt,
    SceneGraph Graph,
    ImmutableDictionary<string, RecordingAsset> Recordings,
    ShowSettings Settings,
    ImmutableArray<PatchRegistryEntry> RequiredPatches);

public record ShowSettings(
    float DefaultBPM, int TargetFPS, Vector2 OutputResolution,
    ImmutableArray<OutputMapping> Outputs,
    ImmutableDictionary<string, string> OscMappings,
    ImmutableDictionary<string, string> MidiMappings);
```

### Graph-JSON-Struktur

```json
{
  "Root": {
    "Id": "root",
    "Components": [
      { "Type": "PatchDescriptor", "Data": { "ChildPolicy": "Parallel" } }
    ],
    "Children": [
      {
        "Id": "layer_bg",
        "Components": [ ... ],
        "Children": [ ... ]
      }
    ]
  },
  "Templates": { },
  "Edges": [
    { "Source": "layer_main", "Target": "layer_fx", "Kind": "DataFlow" }
  ]
}
```

---

## 22. Preset-System

### Preset-Scopes

```csharp
public enum PresetScope : byte
{
    ClipParameters,    // Nur Parameter eines Clips
    ClipFull,          // Clip komplett (Patch + Parameter + Constraints)
    Subtree,           // Ganzer Teilbaum (Layer mit allen Clips)
    Scene,             // Gesamte Szene
    StateMachine,      // Nur FSM-Definition
    Timeline,          // Nur Timeline-Daten
    ConstraintSet,     // Nur Constraints
    InputMapping       // OSC/MIDI-Mappings
}
```

### Parameter-Presets

Speichern und Laden von Clip-Parametern. Pro Patch-Typ gefiltert.

### Subtree-Presets

Ein ganzer Teilbaum als wiederverwendbares Template. Enthält interne Edges und markiert offene Verbindungen die beim Einfügen gemapped werden müssen.

### Preset-Morphing

Überblendung zwischen zwei Parameter-Presets über einen Morph-Slider (0..1). Interpolation pro Parameter basierend auf Typ (float → Lerp, Vector → Lerp, bool → Step).

### Capture und Apply

```csharp
presetManager.CaptureClipParameters(graph, nodeId, "Warm");
presetManager.ApplyParameterPreset(graph, nodeId, preset);
presetManager.MorphPresets(graph, nodeId, presetA, presetB, blend: 0.5f);
```

---

## 23. Undo-System

### Scope-basiert mit globalem Fallback

Jedes Editor-Panel hat seinen eigenen Edit-Scope. Ctrl+Z wirkt im fokussierten Panel. Ctrl+Shift+Z ist global.

### Edit

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

```
layers      — Layer-Stack (add/remove/reorder/visibility)
timeline    — Timeline-Editing (Keyframes, Cues)
parameters  — Inspector (Slider, Werte)
constraints — Constraint-Panel (Verbindungen)
statemachine — FSM-Editor
browser     — Node-Browser (neuen Clip hinzufügen)
transport   — Playback-Controls
```

### Merge-Window

Slider-Drags werden zu einem einzigen Edit zusammengefasst (konfigurierbar, Default 500ms). `BeginEdit` bei MouseDown, `EndEdit` bei MouseUp.

### Scope-Undo vs. Global-Undo

Scope-Undo setzt nur die Nodes zurück die der Scope geändert hat (nutzt `ReferenceEquals`-basiertes Diffing). Global-Undo setzt den gesamten Graphen zurück.

---

## 24. ImGui-Compositor-UI

### Layout

```
┌──────────────────────────────────────────────────────────┐
│ Transport Bar (Play/Pause/Stop, Zeit, BPM, REC, CPU)     │
├──────────────────┬───────────────────────────────────────┤
│ Layer Stack      │ Viewport / Preview                     │
│ (Hierarchie,     │                                        │
│  Solo/Mute,      │                                        │
│  Opacity)        │                                        │
│                  │                                        │
├──────────────────┴───────────────────────────────────────┤
│ Timeline (Clips / Keyframes / Activity)                   │
├──────────────────────────┬───────────────────────────────┤
│ Inspector                │ States / Presets               │
│ (Parameter, Constraints, │ (StateMachine-Vis,             │
│  Presets, ControlSource) │  Preset-Buttons, Morph)        │
└──────────────────────────┴───────────────────────────────┘
```

### UI-State (getrennt vom Scene-State)

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

- Linker Mausklick: Selektion
- Doppelklick: Detail-Editor (Timeline → Keyframe-View)
- Rechtsklick: Kontextmenüs
- Alt+Drag: Timeline-Scrubbing
- Scroll-Wheel: Zoom (zum Mauszeiger hin)
- Drag & Drop: Clips zwischen Layern, Presets auf Clips, Patches aus Browser
- Ctrl+Z: Scope-Undo, Ctrl+Shift+Z: Global-Redo, Ctrl+S: Save, Space: Play/Pause

### Farbsprache

- Grün = aktiv, Gelb = fading, Grau = inaktiv
- Blau = Textur, Grün = Audio, Orange = Render, Grau = Daten
- Rot = Keyframe-Markierung, Cyan = Constraint-Verbindung

### Timeline-Modi

- **Clips**: Clip-Blöcke auf der Timeline (authored: feste Blöcke; generative: aufgezeichnete History)
- **Keyframes**: Kurven-Editor des selektierten Clips
- **Activity**: Activity-Verläufe aller Clips

### Layer-Stack Features

- Drag-Reorder
- Solo/Mute per Layer und per Clip
- Opacity-Slider
- Clip Drop-Zone pro Layer (mit Typ-Validierung)
- Rechtsklick-Menü: Duplicate, Delete, Save as Preset, Bypass

### Inspector Features

- Parameter-Widgets basierend auf Typ (float → DragFloat, Color → ColorEdit, etc.)
- [K]-Button pro Parameter für Keyframe-Toggle
- Constraint-Indikator (⊙) bei verlinkten Parametern
- Preset-Buttons mit Morph-Slider
- ControlSource-Dropdown

---

## 25. Projekt-Struktur

```
VL.SceneGraph/
├── src/
│   ├── VL.SceneGraph.Generators/        # Source Generator (Roslyn Analyzer)
│   │   ├── VL.SceneGraph.Generators.csproj
│   │   ├── SceneComponentGenerator.cs
│   │   ├── FlatStorageGenerator.cs
│   │   ├── ConstraintGenerator.cs
│   │   ├── RegistryGenerator.cs
│   │   └── NodeFactoryGenerator.cs
│   │
│   ├── VL.SceneGraph/                   # Hauptprojekt
│   │   ├── VL.SceneGraph.csproj
│   │   │
│   │   ├── Attributes/
│   │   │   ├── SceneComponentAttribute.cs
│   │   │   ├── SceneConstraintAttribute.cs
│   │   │   └── NodeReferenceAttribute.cs
│   │   │
│   │   ├── Core/                        # Partial Klassen (Generator ergänzt)
│   │   │   ├── SceneNode.cs
│   │   │   ├── SceneGraph.cs
│   │   │   ├── NodeHandle.cs
│   │   │   ├── Edge.cs
│   │   │   ├── IComponent.cs
│   │   │   ├── SlotType.cs
│   │   │   ├── SlotCompatibility.cs
│   │   │   └── HierarchyValidator.cs
│   │   │
│   │   ├── Components/
│   │   │   ├── ClipComponents.cs        # ClipReference, ClipLifetime, ClipActivity, etc.
│   │   │   ├── TimeComponents.cs        # TimeContext, ParameterTimeline, CueList
│   │   │   ├── ControlComponents.cs     # ControlSource, LayerBlending, BypassFlag
│   │   │   ├── ConstraintComponents.cs  # LookAt, ParentConstraint, ParameterLink, etc.
│   │   │   ├── StateMachineComponents.cs
│   │   │   ├── InputComponents.cs       # ExternalInput
│   │   │   ├── RecordingComponents.cs   # Recordable, RecordingPlayback
│   │   │   ├── HierarchyComponents.cs   # PatchDescriptor, ChildComposition
│   │   │   └── StreamComponents.cs      # FeedbackLoop
│   │   │
│   │   ├── Flat/                        # Performance-Schicht (partial)
│   │   │   ├── FlatSceneGraph.cs
│   │   │   ├── FlatConstraints.cs
│   │   │   ├── SceneCompiler.cs
│   │   │   ├── ConstraintCompiler.cs
│   │   │   ├── HandleIndex.cs
│   │   │   ├── EdgeIndex.cs
│   │   │   ├── StreamPool.cs
│   │   │   └── Passes/
│   │   │       ├── TimePass.cs
│   │   │       ├── ExternalInputPass.cs
│   │   │       ├── StateMachinePass.cs
│   │   │       ├── ActivityPass.cs
│   │   │       ├── TimelinePass.cs
│   │   │       ├── ConstraintSolver.cs
│   │   │       ├── StreamRoutingPass.cs
│   │   │       ├── TransformPass.cs
│   │   │       ├── BoundsPass.cs
│   │   │       ├── VisibilityPass.cs
│   │   │       └── RecordingPass.cs
│   │   │
│   │   ├── Evaluation/
│   │   │   ├── ClipEvaluator.cs
│   │   │   ├── TextureCompositor.cs
│   │   │   └── PrimaryInputResolver.cs
│   │   │
│   │   ├── Recording/
│   │   │   ├── TrackRecorder.cs
│   │   │   ├── NodeRecorder.cs
│   │   │   ├── DrawingRecorder.cs
│   │   │   ├── RecordingSerializer.cs
│   │   │   └── ActivityLogger.cs
│   │   │
│   │   ├── Runtime/
│   │   │   ├── CompositorRuntime.cs     # Frame-Pipeline + Double-Buffer
│   │   │   ├── DoubleBufferedScene.cs
│   │   │   └── MemoryBudget.cs
│   │   │
│   │   ├── Presets/
│   │   │   ├── Preset.cs
│   │   │   ├── PresetManager.cs
│   │   │   └── PresetSerializer.cs
│   │   │
│   │   ├── Undo/
│   │   │   ├── Edit.cs
│   │   │   ├── EditScope.cs
│   │   │   ├── UndoManager.cs
│   │   │   └── EditHelper.cs
│   │   │
│   │   ├── Serialization/
│   │   │   ├── ShowFile.cs
│   │   │   ├── ShowSerializer.cs
│   │   │   └── GraphSerializer.cs
│   │   │
│   │   └── VL/                          # VL-spezifische API
│   │       ├── ComponentExtensions.cs
│   │       ├── TraversalExtensions.cs
│   │       ├── QueryExtensions.cs
│   │       ├── SceneBuilderExtensions.cs
│   │       ├── RuntimeExtensions.cs
│   │       ├── ComponentNodeFactory.cs
│   │       └── TraversalNodeFactory.cs
│   │
│   └── VL.SceneGraph.Tests/
│
├── vl/
│   └── VL.SceneGraph.vl                # VL-Dokument (Forwarding + Process Nodes)
│
├── help/
│   ├── Overview.vl
│   ├── HowTo Build a Scene.vl
│   ├── HowTo Use Components.vl
│   ├── HowTo Constraints.vl
│   └── HowTo Custom Components.vl
│
└── README.md
```

### NuGet-Paket

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>VL.SceneGraph</PackageId>
    <Version>0.1.0-alpha</Version>
    <Authors>kopffarben</Authors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Collections.Immutable" Version="8.0.0" />
    <PackageReference Include="System.Numerics.Vectors" Version="4.5.0" />
    <ProjectReference Include="../VL.SceneGraph.Generators/VL.SceneGraph.Generators.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
  <ItemGroup>
    <None Include="../../vl/**" Pack="true" PackagePath="vl/" />
    <None Include="../../help/**" Pack="true" PackagePath="help/" />
  </ItemGroup>
</Project>
```

---

## 26. Offene Punkte

### Noch zu designen / implementieren

1. **StateMachine-Crossfading**: Wie genau überblendet man zwischen States wenn verschiedene Clips aktiv/inaktiv werden?
2. **Expression-Engine**: Wie werden mathematische Ausdrücke evaluiert? (Parsing, Kompilation, Performance)
3. **Stride-Integration**: Wie fließen Render-Commands aus Clips in Strides Render-Pipeline?
4. **IClipInstance Adapter**: Wie werden bestehende VL-Patches zu IClipInstance adaptiert? (Reflection? Code-Gen? Manuell?)
5. **Netzwerk/Multi-Machine**: Synchronisierung für große Installationen mit mehreren Rechnern
6. **Keyframe-Kurven-Editor**: Bezier-Tangenten-Editing in ImGui
7. **Performance-Profiling**: Welche Passes wie lange dauern, wo sind Bottlenecks
8. **Error Recovery**: Was passiert wenn ein Clip-Patch crasht? Isolation?
9. **Hot-Reload von Patches**: Was passiert wenn ein VL-Patch sich ändert während die Show läuft?
10. **VL-Record mit `with`**: Genau klären was VLs Record-Typ von C# aus unterstützt
11. **Spread<T> vs ImmutableArray<T>**: Wo ist die Grenze zwischen VL-nativen Typen und C#-Typen?

### Performance-Ziele

- 60fps bei 1000+ Nodes
- Zero Allocations im Hot-Path (pro Frame)
- < 2ms für die gesamte Pipeline (ohne Clip-Evaluation)
- Inkrementeller Compile < 0.5ms bei Property-Only-Änderungen
- Full Rebuild < 5ms bei 1000 Nodes

### Architektur-Risiken

- **ImmutableDictionary<Type, IComponent>** hat O(log n) Lookup — bei vielen Components pro Node relevant. Alternative: FrozenDictionary (einmalig erstellt, schneller Lookup) oder direkte Felder für häufige Components
- **Object Boxing** bei `ClipParameters` (Dictionary<string, object?>) — für den Hot-Path die SoA-Arrays nutzen, nicht die Components
- **Source Generator Komplexität** — kann schwierig zu debuggen sein. Generierter Code muss klar und nachvollziehbar sein
- **ImGui-Performance** — bei vielen Clips in der Timeline kann das Zeichnen selbst zum Bottleneck werden. Culling/Virtualisierung nötig
