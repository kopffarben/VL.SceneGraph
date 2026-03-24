# Kern-Datenmodell

> Konsolidierte Referenz der Abschnitte 3--6 aus `ARCHITEKTUR.md`.
> Beschreibt den immutablen Baum, das Component-System, den gerichteten Graph-Overlay und das Slot-Typsystem.

---

## 1. Immutabler Baum mit Structural Sharing

### SceneNode

Zentrale Datenstruktur. Bewusst als `class` statt `record` implementiert -- VL-Kompatibilität (Equals-Performance, `with`-Einschränkungen bei VLs Record-Typ).

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

    // --- Immutable Update Methods ---
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

    // --- Queries ---
    public T? GetComponent<T>() where T : class, IComponent
        => _components.TryGetValue(typeof(T), out var c) ? (T)c : null;
    public bool HasComponent<T>() where T : IComponent
        => _components.ContainsKey(typeof(T));
    public IEnumerable<IComponent> AllComponents => _components.Values;
    public int ChildCount => Children.Length;
    public bool IsLeaf => Children.IsEmpty;
}
```

### Structural Sharing (Path Copying)

Wenn ein Node tief im Baum geaendert wird, werden nur Nodes entlang des Pfades zur Wurzel neu erzeugt -- alle unveraenderten Teilbaeume werden per Referenz geteilt.

Bei einem Baum mit 1 Million Nodes kopiert eine Blatt-Aenderung nur ~20 Nodes (O(log n)).

`ReferenceEquals(oldNode, newNode)` dient als **kostenloser Dirty-Flag**: gibt `true` zurueck, wenn sich nichts geaendert hat.

### Performance: ImmutableArray vs ImmutableList

`ImmutableArray<T>` ist **30x schneller** beim Iterieren als `ImmutableList<T>` (438 ns vs 16.915 ns fuer 1000 Elemente). `ImmutableList<T>` nur verwenden, wenn haeufige Einfuegungen/Entfernungen mit Structural Sharing der Flaschenhals sind.

---

## 2. Component-System

### Interface

```csharp
public interface IComponent { }
```

Jeder Node hat ein `ImmutableDictionary<Type, IComponent>` -- maximal eine Component pro Typ. Offene Erweiterung: VL-Nutzer koennen eigene Records, die `IComponent` implementieren, erstellen.

### Source-Generator-Attribut

Components werden ueber Attribute markiert. Der Source Generator erzeugt automatisch typisierte Accessors, SoA-Arrays und Compile-Code:

```csharp
[SceneComponent(FlatStorage = true)]  // -> SoA-Array im FlatSceneGraph
public partial record Transform3D(Matrix4x4 Matrix);

[SceneConstraint(WritesTo = "Transform3D", DependsOn = new[] { "Transform3D" })]
public partial record LookAtConstraint(
    [property: NodeReference] NodeHandle Target,
    Vector3 UpVector);
```

### Transient Components

Components mit `Transient = true` leben im Graphen und sind über `ISceneContext` lesbar, werden aber **nicht serialisiert**. Sie dienen als frame-synchroner Runtime-State.

```csharp
[SceneComponent(Transient = true)]
public partial record ComputedLayout(RectangleF Bounds, RectangleF GlobalBounds,
    Vector2 ContentOffset, Vector2 ContentSize);

[SceneComponent(Transient = true, FlatStorage = true)]
public partial record InputHit(bool IsHit, bool IsDirectHit, bool IsChildHit,
    string? HitChildId, Vector2 LocalPosition);

[SceneComponent(Transient = true)]
public partial record ComputedBounds(RectangleF LocalBounds, RectangleF GlobalBounds,
    bool IsHitTestable);
```

Der Serializer filtert Transient Components automatisch:
```csharp
var persistentComponents = node.AllComponents
    .Where(c => !ComponentRegistry.IsTransient(c.GetType()));
```

Transient Components eignen sich für:
- Layout-Ergebnisse (ComputedLayout, ComputedBounds)
- Input-State (InputHit)
- Sensor-Daten bei hoher Frequenz (60fps)
- Berechnete Pipeline-Werte die nicht persistiert werden müssen

→ Siehe [03-CLIPS.md](03-CLIPS.md) für die Abgrenzung zu VL Channels.

### Component-Referenztabelle

Kompakte Uebersicht aller Standard-Components. Detaillierte Beschreibungen in den jeweiligen Kapiteln.

| Component | Parameter | Kapitel |
|-----------|-----------|---------|
| **Clip-System** | | |
| `ClipReference` | `string PatchId, int PatchVersion, string DocumentId, string PersistentId` | -> 03-CLIPS.md |
| `ClipLifetime` | `float StartTime, float Duration, float FadeIn, float FadeOut, ClipPlayState PlayState` | -> 03-CLIPS.md |
| `ClipActivity` | `float Activity, float LocalTime, bool IsActive` | -> 03-CLIPS.md [FlatStorage] |
| `ClipParameters` | `ImmutableDictionary<string, StoredParameter> Values, int SchemaVersion` | -> 03-CLIPS.md |
| `ControlSource` | `ControlMode Mode, ...` | -> 04-ZEIT-UND-STEUERUNG.md |
| `LayerBlending` | `BlendMode Mode, float Opacity, bool Solo, bool Mute` | -> 03-CLIPS.md |
| **Zeit** | | |
| `TimeContext` | `float LocalTime, float LocalDuration, float PlaybackRate, TimeMode Mode, bool IsTimeSource` | -> 04-ZEIT-UND-STEUERUNG.md [FlatStorage] |
| **Hierarchie / Typsystem** | | |
| `PatchDescriptor` | `SlotType PrimaryInput, SlotType PrimaryOutput, ImmutableArray<SlotRequirement> AdditionalInputs, ImmutableArray<SlotOutput> AdditionalOutputs, ChildPolicy ChildPolicy, ImmutableArray<SlotType> AcceptsChildrenOf` | -> unten, Abschnitt 4 |
| `ChildComposition` | `CompositionMode Mode, BlendMode DefaultBlendMode` | -> unten, Abschnitt 4 |
| **Constraints** | | |
| `LookAtConstraint` | `NodeHandle Target, Vector3 UpVector` | -> 05-CONSTRAINTS.md |
| `ParentConstraint` | `NodeHandle Source, float Weight` | -> 05-CONSTRAINTS.md |
| `PositionConstraint` | `NodeHandle Target, Vector3 Offset` | -> 05-CONSTRAINTS.md |
| `ParameterLink` | `NodeHandle SourceClip, string SourceParameter, string TargetParameter, LinkMode, float Weight` | -> 05-CONSTRAINTS.md |
| `ActivityLink` | `NodeHandle SourceClip, ActivityLinkMode Mode, float Weight` | -> 05-CONSTRAINTS.md |
| `TempoSync` | `NodeHandle TempoSource, float Multiplier, float PhaseOffset` | -> 05-CONSTRAINTS.md |
| `ExpressionConstraint` | `string TargetParameter, string Expression, ImmutableArray<ExpressionBinding> Bindings` | -> 05-CONSTRAINTS.md |
| **Timeline** | | |
| `ParameterTimeline` | `ImmutableDictionary<string, AnimationTrack> Tracks` | -> 04-ZEIT-UND-STEUERUNG.md |
| `CueList` | `ImmutableArray<Cue> Cues` | -> 04-ZEIT-UND-STEUERUNG.md |
| **StateMachine** | | |
| `StateMachineDefinition` | `ImmutableArray<StateDefinition> States, ImmutableArray<TransitionDefinition> Transitions, string InitialState` | -> 04-ZEIT-UND-STEUERUNG.md |
| `StateMachineState` | `string CurrentState, float TimeInState, string? PendingTransition, float TransitionProgress` | -> 04-ZEIT-UND-STEUERUNG.md [FlatStorage] |
| **Externe Inputs** | | |
| `ExternalInput` | `InputSourceKind Source, string? Address, Type OutputType, float SmoothingFactor, float InputMin, float InputMax, float OutputMin, float OutputMax` | -> 06-INPUTS-RECORDING.md |
| **Recording** | | |
| `Recordable` | `RecordMode Mode, RecordTarget Target, bool IsRecording, bool IsPlaying, int MaxFrames, RecordSampling Sampling` | -> 06-INPUTS-RECORDING.md |
| `RecordingPlayback` | `string RecordingId, float PlaybackSpeed, float StartOffset, TimeMode LoopMode, float BlendWithLive` | -> 06-INPUTS-RECORDING.md |
| `DrawingRecordable` | `bool RecordStrokes, bool RecordUndos, int MaxStrokes` | -> 06-INPUTS-RECORDING.md |
| **Sonstige** | | |
| `BypassFlag` | `bool Bypassed` | -- |
| `FeedbackLoop` | `bool UseFeedback, float FeedbackAmount, int DelayFrames` | -- |
| `InstanceOf` | `string TemplateId` | -> unten, Abschnitt 3 (Templates) |
| `VisibilityFlag` | `bool Visible` | -- [FlatStorage] |
| **Transient (nicht serialisiert)** | | |
| `ComputedLayout` | `RectangleF Bounds, RectangleF GlobalBounds, Vector2 ContentOffset, Vector2 ContentSize` | -> Pipeline [Transient] |
| `InputHit` | `bool IsHit, bool IsDirectHit, bool IsChildHit, string? HitChildId, Vector2 LocalPosition` | -> Pipeline [Transient, FlatStorage] |
| `ComputedBounds` | `RectangleF LocalBounds, RectangleF GlobalBounds, bool IsHitTestable` | -> Pipeline [Transient] |

> **[FlatStorage]** = Component wird als SoA-Array im `FlatSceneGraph` materialisiert (Source-Generator).
> **[Transient]** = Component wird nicht serialisiert, dient als frame-synchroner Runtime-State.

---

## 3. Gerichteter Graph als Overlay

### Struktur

Die Hierarchie (Baum) definiert die **raeumliche/logische Struktur**. Zusaetzliche gerichtete Kanten definieren **Verarbeitungsreihenfolge und Querverweise**:

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
```

### SceneGraph (Top-Level Record)

```csharp
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
        => new(Root, Templates,
            Edges.Add(new Edge(new NodeHandle(sourceId), new NodeHandle(targetId), kind)));
    public SceneGraph Disconnect(string sourceId, string targetId, EdgeKind kind)
        => new(Root, Templates,
            Edges.Remove(new Edge(new NodeHandle(sourceId), new NodeHandle(targetId), kind)));
}
```

### HandleIndex

Handles (symbolische IDs) statt direkter Referenzen. Ein `HandleIndex` mapped `NodeHandle -> int` (flacher Array-Index):

```csharp
public sealed class HandleIndex
{
    private readonly Dictionary<NodeHandle, int> _handleToFlat;

    public HandleIndex(FlatSceneGraph flat) { /* Build from flat.Handles[] */ }
    public int Resolve(NodeHandle handle)
        => _handleToFlat.TryGetValue(handle, out int idx) ? idx : -1;
}
```

### EdgeIndex

Fuer performante Abfragen: Adjacency-Lists als `ImmutableDictionary<NodeHandle, ImmutableArray<Edge>>` fuer Incoming und Outgoing Edges. Unterstuetzt topologische Sortierung.

### Templates und Instancing

Shared Subtrees ueber Indirektion: Der Baum bleibt ein Baum, Nodes referenzieren Templates per `InstanceOf`-Component. Templates leben im `SceneGraph.Templates`-Dictionary.

#### Template-Definition (in VL patchbar)

```
VL Patch:
  // Template als Sub-Graph bauen
  particleTemplate = SceneNode.Create("particle_template")
      .WithComponent(ClipReference.Create("Particle", 1, docId, persistId))
      .WithComponent(ClipLifetime.Create(0, 3.0, 0.1, 0.5))
      .WithComponent(LayerBlending.Create(BlendMode.Additive, 1.0))

  // Im Graph registrieren
  graph = graph.DefineTemplate("particle", particleTemplate)
```

> **Hinweis:** `ClipReference.Create` nimmt vier Parameter: `PatchId`, `PatchVersion`, `DocumentId`, `PersistentId`.

#### Instanz-Erzeugung

```
  // Instanz referenziert Template, kann Werte ueberschreiben
  instance = SceneNode.Create("particle_042")
      .WithComponent(InstanceOf.Create("particle"))
      .WithComponent(ClipParameters.Create(
          values: new Dictionary<string, StoredParameter>
          {
              ["Tint"] = StoredParameter.From(Red),
              ["Size"] = StoredParameter.From(0.5f)
          }.ToImmutableDictionary(),
          schemaVersion: 1))
```

#### Template-Aufloesung beim BFS-Compile

Beim Compile in die flache Repraesentation werden Templates aufgeloest. Die Merge-Logik:

```csharp
if (node.HasComponent<InstanceOf>())
{
    var templateId = node.GetComponent<InstanceOf>().TemplateId;
    var template = graph.Templates[templateId];

    // Template-Components als Basis, Instanz-Components ueberschreiben
    var merged = MergeComponents(template, node);
    // Template-Children uebernehmen wenn Instanz keine eigenen hat
    var children = node.IsLeaf ? template.Children : node.Children;
}
```

**Merge-Regeln:**
1. Alle Components des Templates werden uebernommen.
2. Instanz-Components ueberschreiben gleichnamige Template-Components (Type-basiert).
3. `InstanceOf` selbst wird *nicht* in das Merge-Ergebnis uebernommen.
4. Children: Hat die Instanz eigene Children, werden die Template-Children *ignoriert*. Ist die Instanz ein Leaf, werden die Template-Children uebernommen.

#### Serialisierung

Templates werden als eigenstaendige JSON-Dateien im `templates/`-Ordner der Show gespeichert. Instanz-spezifische Overrides leben als Components auf dem Instanz-Node im Hauptgraph.

---

## 4. Slot-Typsystem und Hierarchie-Validierung

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

Kompatibilitaet: `SlotCompatibility.IsCompatible(provider, consumer)` prueft, ob ein Output-Typ an einem bestimmten Input-Slot andocken kann. Erweiterbar fuer eigene Typen.

### PatchDescriptor

Jeder Patch deklariert sein Profil. Vollstaendige Signatur mit allen Feldern:

```csharp
[SceneComponent]
public partial record PatchDescriptor(
    SlotType PrimaryInput,
    SlotType PrimaryOutput,
    ImmutableArray<SlotRequirement> AdditionalInputs,
    ImmutableArray<SlotOutput> AdditionalOutputs,
    ChildPolicy ChildPolicy,
    ImmutableArray<SlotType> AcceptsChildrenOf);
```

| Feld | Beschreibung |
|------|-------------|
| `PrimaryInput` | Haupt-Input-Slot des Patches (z.B. `Texture` fuer einen Effekt) |
| `PrimaryOutput` | Haupt-Output-Slot (z.B. `Texture` fuer einen Generator) |
| `AdditionalInputs` | Weitere benoetigte Inputs (z.B. Maske, Steuer-Signal) |
| `AdditionalOutputs` | Weitere Outputs (z.B. Depth-Buffer, Motion-Vectors) |
| `ChildPolicy` | Regelt ob und wie viele Kinder erlaubt sind |
| `AcceptsChildrenOf` | Welche Slot-Typen die Kinder liefern muessen |

### ChildPolicy

```csharp
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

Bestimmt, wie ein Parent die Outputs seiner Kinder kombiniert:

```csharp
public enum CompositionMode : byte
{
    Parallel,       // Kinder rendern unabhaengig, Parent mischt
    Sequential,     // Output von Kind N -> Input von Kind N+1
    Additive,       // Alle Outputs werden addiert
    Custom          // VL-Patch entscheidet
}
```

### HierarchyValidator

`HierarchyValidator.CanBeChildOf(child, parent)` prueft:
- Akzeptiert der Parent Kinder?
- Sind die Slot-Typen kompatibel?
- Ist die ChildPolicy eingehalten?

Gibt `ValidationResult` mit Severity (`Ok` / `Info` / `Warning` / `Error`) und Nachricht zurueck.

`HierarchyValidator.ValidateTree(root)` validiert den gesamten Baum. Wird fuer ImGui-UI-Feedback genutzt (rote Markierung, Drag&Drop-Filterung).
