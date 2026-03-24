# Clip-Plugin-System

Das Clip-System ist das zentrale Plugin-Modell des SceneGraph. VL-User schreiben Clips als normale VL-Processes, die ein Marker-Interface implementieren. Kein C#-Code noetig.

---

## ISceneClip — Drei-Schichten-Modell

### Schicht 1: C# Marker Interface (Library)

```csharp
public interface ISceneClip { }
```

### Schicht 2: VL Interfaces (vordefiniert, erweiterbar durch User)

```
ITextureGenerator : ISceneClip           // () -> Texture
ITextureEffect : ISceneClip              // Texture -> Texture
ITextureCompositor : ISceneClip          // Spread<Texture> -> Texture
IAudioGenerator : ISceneClip             // () -> AudioBuffer
IAudioEffect : ISceneClip               // AudioBuffer -> AudioBuffer
...
```

### Schicht 3: VL Process (User-Code)

```
Process "MyBloom" : ITextureEffect
  ├── Create()
  ├── OnActivation()
  ├── Update(out Texture Output, Texture Input, float Intensity, ...)
  ├── OnDeactivation()
  └── Dispose()
```

### Generierter Code (vom VL-Compiler)

Wenn der User in VL einen Process erstellt der ein Clip-Interface implementiert, generiert der VL-Compiler eine C#-Klasse:

```csharp
[Element(DocumentId = "EKYvICxF27COKXc5f2ctJU", PersistentId = "SEeVW6E6jOkLZwCgUlRJjT")]
public interface ITextureEffect_I : ISceneClip, IVLObject { }

public sealed class MyBloom_C : PatchedObject<MyBloom_C>, ITextureEffect_I, IDisposable
{
    // VL-Compiler erzeugt Update() mit allen Pins als Parameter
    public MyBloom_C Update(
        out Texture Output,
        Texture Input,
        float Intensity,
        // ... weitere Parameter-Pins
    ) { ... }

    public MyBloom_C OnActivation() { ... }
    public MyBloom_C OnDeactivation() { ... }
}
```

### Discovery via TypeRegistry

Zur Laufzeit findet das System alle verfuegbaren Clip-Typen ueber VLs TypeRegistry:

```csharp
var clipTypes = appHost.TypeRegistry.RegisteredTypes
    .Where(t => typeof(ISceneClip).IsAssignableFrom(t.ClrType));

// Weitere Filterung nach konkretem Interface
var textureEffects = clipTypes
    .Where(t => typeof(ITextureEffect).IsAssignableFrom(t.ClrType));
```

---

## Pin-Binding

Das System erkennt Pins automatisch anhand ihres Typs und Namens und ordnet sie in vier Kategorien ein:

| Pin-Kategorie | Erkennungsregel | Wer setzt den Wert |
|---------------|-----------------|---------------------|
| **System-Pins** | Activity (`float`), LocalTime (`float`), DeltaTime (`float`), ISceneContext | CompositorRuntime |
| **Primary I/O** | Input/Output Pins deren Typ zum Slot-Typ passt (Texture, AudioBuffer) | Stream-Routing |
| **Edit-Output** | `Spread<SceneEdit>` Output-Pin | Clip selbst |
| **Parameter** | Alle uebrigen Input-Pins | ClipParameters aus dem Baum |

Parameter = alle Input-Pins die nicht System/Primary sind. Automatische Discovery ueber `IVLNodeDescription.Inputs`.

### ParameterDescriptor

Wird automatisch aus den VL-Patch-Pins extrahiert:

```csharp
public record ParameterDescriptor(
    string Name,
    string PersistentId,           // VL-PersistentId, ueberlebt Renames
    Type ValueType,
    object? DefaultValue,
    object? MinValue, object? MaxValue,
    string? Group,
    ParameterFlags Flags);

[Flags]
public enum ParameterFlags
{
    None = 0, Animatable = 1, Automatable = 2,
    Preset = 4, Hidden = 8, ReadOnly = 16
}
```

Die `PersistentId` ist entscheidend: Sie bleibt stabil wenn der User einen Pin umbenennt, sodass gespeicherte Parameter-Werte und Animationen erhalten bleiben.

---

## Clip-Lifecycle

### Zustandsmaschine

```
Stopped -> FadingIn -> Playing -> FadingOut -> Stopped
                 |          |
               Paused     Paused
```

Der Activity-Wert (0..1) inkludiert die Fade-In/Out-Rampe und wird mit der Parent-Activity multipliziert.

### Lifecycle-Callbacks

| Callback | Zeitpunkt | Typischer Einsatz |
|----------|-----------|-------------------|
| `Create()` | VL-Konstruktor, bei erster Aktivierung | Initiale Ressourcen |
| `OnActivation()` | Stopped/FadingOut -> FadingIn | GPU-State vorbereiten, Resources allozieren |
| `Update()` | Jeden Frame waehrend aktiv (Activity > 0) | Hauptverarbeitung, System setzt Pins vor dem Aufruf |
| `OnDeactivation()` | Uebergang zu Stopped | Resources freigeben |
| `Dispose()` | Clip wird aus dem Graphen entfernt | Finale Aufraeumarbeiten |

---

## ControlMode

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
    Constraint,     // Activity kommt ueber ActivityLink
    Recorded,       // Spielt eine Aufnahme ab
    RecordingLive   // Zeichnet gerade auf
}
```

Mischungen sind moeglich: Teile der Show Timeline-gesteuert, andere FSM-gesteuert, andere manuell.

---

## ISceneContext — Model-Zugriff

### Problem

Clips brauchen mehr als nur ihre eigenen Pins. Ein Layer muss wissen was seine Kinder outputten. Ein Effekt will den Parent-Zustand lesen. Alles ueber Pins zu routen waere zu starr.

### Loesung: Direkter Read-only-Zugriff auf den immutablen Graphen

Da der SceneGraph immutable ist, kann jeder Clip ihn gefahrlos lesen — kein Lock, kein Kopieren, kein Risiko.

```csharp
public interface ISceneContext
{
    // Das Model — voller Lesezugriff (immutable, sicher)
    SceneGraph Graph { get; }
    SceneNode Self { get; }            // Eigener Node
    SceneNode? Parent { get; }         // Parent-Node

    // Performance-Schicht
    FlatSceneGraph Flat { get; }       // SoA-Arrays (read-only)
    HandleIndex HandleIndex { get; }   // Handle -> Index Mapping

    // Eigene Position
    string NodeId { get; }
    int FlatIndex { get; }

    // Convenience: Child-Outputs aus StreamPool
    T? GetChildOutput<T>(string childId) where T : class;
    ReadOnlySpan<object?> GetChildOutputs();
}
```

### Verwendungsbeispiel: TextureLayer

```
Process "TextureLayer" : ITextureCompositor
  Update(
    out Texture Output,
    ISceneContext Context,
    float Activity)
  {
    // Kinder durchgehen — direkt am Model
    foreach (var child in Context.Self.Children)
    {
        var blending = child.GetComponent<LayerBlending>();
        var activity = child.GetComponent<ClipActivity>();

        if (activity?.IsActive == true && blending?.Mute != true)
        {
            var childOutput = Context.GetChildOutput<Texture>(child.Id);
            Composite(childOutput, blending.Opacity, blending.Mode);
        }
    }

    // Edges lesen
    var incoming = Context.Graph.Edges
        .Where(e => e.Target.NodeId == Context.NodeId);

    // Parent inspizieren
    var parentTime = Context.Parent?.GetComponent<TimeContext>();
  }
```

### Warum das sicher ist

```
Immutable Graph (Front-Buffer Snapshot)
        |
        |  read-only Referenz
        v
+-- ISceneContext ---------------------+
|  .Graph    -> SceneGraph             |  <- Kann nicht mutiert werden
|  .Self     -> SceneNode              |  <- Kann nicht mutiert werden
|  .Flat     -> FlatSceneGraph         |  <- Nur ReadOnlySpan
|  .Parent   -> SceneNode              |  <- Kann nicht mutiert werden
+--------------------------------------+
        |
        |  Clip liest frei
        v
    Clip Output-Pins (einziger Schreibweg)
```

Kein Clip mutiert einen anderen Clip direkt. Alles geht ueber den immutablen Baum und die Pipeline.

### Zugriffs-Matrix

| Richtung | Mechanismus | Beispiel |
|----------|-------------|---------|
| System -> Clip | System-Pins (Activity, LocalTime, ISceneContext) | Clip kennt seinen Zustand |
| Kind -> Parent | Stream-Routing via `ISceneContext.GetChildOutputs()` | Layer composited Kinder |
| Parent -> Kind | Constraints / ParameterLinks im Baum | Parent-Opacity vererbt sich |
| Clip -> Clip | Edges (DataFlow, Reference) im Baum | LookAt, ParameterLink |
| Clip -> Graph aendern | SceneEdit-Output (siehe naechster Abschnitt) | Clip erzeugt/entfernt Nodes |

---

## SceneEdit — Clip-generierte Graph-Aenderungen

### Problem

Clips sind reine Verarbeitungseinheiten mit read-only Model-Zugriff. Aber manchmal muss ein Clip den Graphen aendern — z.B. ein Partikel-Spawner der Kinder erzeugt, oder ein Clip der sich selbst deaktiviert.

### Loesung: Edit-Commands als Output-Pin

Clips geben Aenderungswuensche als immutable Records ueber einen `Spread<SceneEdit>` Output-Pin zurueck:

```csharp
// Basis — alles sind immutable Records, patchbar in VL
public abstract record SceneEdit;

// Struktur-Edits
public record AddChild(string ParentId, SceneNode Child) : SceneEdit;
public record RemoveChild(string ParentId, string ChildId) : SceneEdit;
public record MoveChild(string ChildId, string NewParentId, int Index) : SceneEdit;

// Component-Edits
public record SetComponent(string NodeId, IComponent Component) : SceneEdit;
public record RemoveComponent(string NodeId, Type ComponentType) : SceneEdit;

// Parameter-Edits (haeufigster Fall)
public record SetParameter(string NodeId, string Name, object? Value) : SceneEdit;

// Edge-Edits
public record AddEdge(string SourceId, string TargetId, EdgeKind Kind) : SceneEdit;
public record RemoveEdge(string SourceId, string TargetId, EdgeKind Kind) : SceneEdit;

// Batch
public record BatchEdit(ImmutableArray<SceneEdit> Edits) : SceneEdit;
```

### Beispiel: ParticleSpawner

```
Process "ParticleSpawner" : ISceneClip
  Update(
    out Texture Output,
    out Spread<SceneEdit> Edits,        // <- Edit-Output
    ISceneContext Context,
    float Activity,
    float SpawnRate = 5.0)
  {
    edits = SpreadBuilder<SceneEdit>();

    // Neue Partikel erzeugen
    if (ShouldSpawn(SpawnRate))
    {
        var child = SceneNode.Create("particle_" + nextId)
            .WithComponent(ClipReference.Create("Particle", 1))
            .WithComponent(ClipLifetime.Create(Context.LocalTime, 3.0));
        edits.Add(new AddChild(Context.NodeId, child));
    }

    // Abgelaufene Partikel entfernen
    foreach (var child in Context.Self.Children)
    {
        var lifetime = child.GetComponent<ClipLifetime>();
        if (IsExpired(lifetime, Context.LocalTime))
            edits.Add(new RemoveChild(Context.NodeId, child.Id));
    }

    Edits = edits.ToSpread();
  }
```

### Edit-Verarbeitung im CompositorRuntime

```csharp
public sealed class CompositorRuntime
{
    public SceneGraph Frame(
        SceneGraph graph,
        float time, float dt,
        out ReadOnlyMemory<object?> outputs,
        out ImmutableArray<SceneEdit> clipEdits)
    {
        // Passes 1-9: Time, Activity, Constraints, Routing, ...

        // Pass 10: Clip-Evaluation — sammelt Edits
        var allEdits = ImmutableArray.CreateBuilder<SceneEdit>();
        foreach (var clip in activeClips)
        {
            clip.Node.Update();

            if (clip.EditOutputIndex >= 0)
            {
                var edits = clip.Node.Outputs[clip.EditOutputIndex].Value
                    as Spread<SceneEdit>;
                if (edits?.Count > 0)
                    allEdits.AddRange(edits);
            }
        }

        clipEdits = allEdits.ToImmutable();
        return graph;  // Graph wird NICHT hier geaendert — der User-Patch tut das
    }
}
```

### ApplyEdits — Edits auf den Graphen anwenden

```csharp
public static class SceneGraphEditing
{
    public static SceneGraph ApplyEdits(this SceneGraph graph, IEnumerable<SceneEdit> edits)
    {
        var result = graph;
        foreach (var edit in edits)
        {
            result = edit switch
            {
                AddChild add => result.WithRoot(
                    AddChildToNode(result.Root, add.ParentId, add.Child)),
                RemoveChild rm => result.WithRoot(
                    RemoveChildFromNode(result.Root, rm.ParentId, rm.ChildId)),
                SetComponent sc => result.WithRoot(
                    SetComponentOnNode(result.Root, sc.NodeId, sc.Component)),
                SetParameter sp => result.WithRoot(
                    SetParameterOnNode(result.Root, sp.NodeId, sp.Name, sp.Value)),
                AddEdge ae => result.Connect(ae.SourceId, ae.TargetId, ae.Kind),
                RemoveEdge re => result.Disconnect(re.SourceId, re.TargetId, re.Kind),
                BatchEdit batch => result.ApplyEdits(batch.Edits),
                _ => result
            };
        }
        return result;
    }
}
```

---

## Patchbares Model — Accumulator-Pattern

### Prinzip

Der SceneGraph lebt **im VL-Patch des Users**, nicht in einer Black Box. Er ist ein immutables Record das pro Frame durch einen Accumulator laeuft.

### VL-Hauptpatch (FrameDelay-Accumulator)

```
+-----------------------------------------------------+
|  VL Hauptpatch                                       |
|                                                      |
|  previousGraph <------ FrameDelay <----+             |
|      |                                 |             |
|      +-- ApplyEdits(clipEdits)         |             |
|      +-- ApplyEdits(uiEdits)           |             |
|      +-- ApplyEdits(oscEdits)          |             |
|      |                                 |             |
|      v                                 |             |
|  currentGraph --> CompositorRuntime    |             |
|                       |         |      |             |
|                    outputs   clipEdits |             |
|                       |         |      |             |
|                       v         +------+             |
|                    Renderer                          |
|                                                      |
|  currentGraph ------------------> FrameDelay         |
+-----------------------------------------------------+
```

### Alles ist patchbar

| Element | VL-Typ | Was der User damit tut |
|---------|--------|------------------------|
| SceneGraph | Record | Konstruieren, inspizieren, modifizieren |
| SceneNode | Record | Erstellen, Components anhaengen, Kinder hinzufuegen |
| SceneEdit | Record | Erzeugen, filtern, kombinieren, eigene definieren |
| ISceneContext | Interface (read-only) | Im Clip-Patch lesen |
| CompositorRuntime | Process | In den Hauptpatch einstecken |

Kein verstecktes Magic. Der User sieht den Datenfluss und kann an jeder Stelle eingreifen.

### Edit-Quellen

Verschiedene Quellen produzieren `Spread<SceneEdit>`:

```
                    +--- UI (ImGui Layer-Stack, Inspector)
                    +--- Timeline (automatische Start/Stop)
                    +--- StateMachine (State-Wechsel)
Edits <-------------+--- OSC/MIDI (gemappte Parameter)
                    +--- Clips (Spread<SceneEdit> Output-Pin)
                    +--- Undo-Manager (Undo/Redo)
                    +--- Presets (Apply/Morph)
```

Alle Edits fliessen im selben Kreislauf. Egal ob ein Slider im UI bewegt wird, ein Clip ein Kind erzeugt, oder ein OSC-Controller einen Parameter aendert — der Mechanismus ist identisch.

---

## Drei Arten von State

| Typ | Wo | Serialisiert | Lesbar von anderen | Beispiel |
|-----|----|--------------|--------------------|---------|
| **Persistent** | SceneNode Components | Ja | Ja (ISceneContext) | ClipParameters, LayerBlending, ClipLifetime |
| **Transient** | SceneNode Components (Transient=true) | **Nein** | Ja (ISceneContext) | ComputedLayout, InputHit, ComputedBounds |
| **Clip-intern** | VL Process Felder | Nein | Nein | GPU-Texture, Accumulation-Buffer, interne Counter |

### Persistent State
Components auf SceneNode — die Wahrheit. Fließen ins ShowFile, unterstützen Undo/Redo, Timeline-Keyframes, Presets.

### Transient State
Components mit `[SceneComponent(Transient = true)]` — leben im Graphen, sind über `ISceneContext` lesbar, aber werden nicht serialisiert. Für frame-synchrone Werte die von der Pipeline berechnet werden (Layout, Bounds, Hit-Testing, Sensor-Daten bei hoher Frequenz).

### Clip-interner State
Felder im VL Process — privat, nur der Clip selbst sieht sie. GPU-Textures, Accumulation-Buffer, geladene Daten, interne Counter. Werden von VLs Hotswap automatisch migriert.

### Wann Transient vs. Channel?

| Kriterium | Transient Component | VL Channel |
|-----------|--------------------:|:-----------|
| **Frequenz** | Hoch (60fps, Sensoren) | Niedrig (Events, Klicks) |
| **Timing** | Frame-synchron | Reaktiv/asynchron |
| **Zugriff** | ISceneContext (Pull) | Subscribe (Push) |
| **Beispiele** | ComputedLayout, InputHit, SensorData | "App/Selection/PhotoId", Navigation-Bangs |
| **Performance** | FlatStorage → SoA-Array | Observable-Notification |

---

## VL Channels als System-Pin

`IChannelHub` wird als System-Pin erkannt und vom System gesetzt — wie `ISceneContext`:

```
Process "MyClip" : ITextureEffect
  Update(
    out Texture Output,
    Texture Input,
    IChannelHub Hub,           ← System-Pin, vom System gesetzt
    ISceneContext Context,     ← System-Pin
    float Activity,
    float Intensity = 1.0)
```

### Channels für Inter-Clip-Kommunikation

```
Process "GridController" : ISceneLogic
  Update(out Spread<SceneEdit> Edits, IChannelHub Hub, ...)
  {
    // Channel schreiben — alle Subscriber reagieren sofort
    Hub.TryGetChannel("App/Selection/PhotoId").Object = "foto_002";
    Hub.TryGetChannel("App/Navigation/GoDetail").Object = Unit.Default;  // Bang
  }

Process "PhotoViewer" : ITextureGenerator
  Create(IChannelHub Hub)
  {
    // Einmalig subscriben — reaktiv, nicht polling
    Hub.TryGetChannel("App/Selection/PhotoId")
        .Subscribe(id => LoadPhoto((string)id));
  }
```

### Channels für externe Bindings

OSC, MIDI, Sensoren und UI-Elemente können direkt über Channels kommunizieren — ohne SceneEdits, ohne Components:

```
ChannelHub
  ├── "App/Selection/PhotoId"      : string     ← Inter-Clip
  ├── "App/Navigation/GoDetail"    : Unit        ← Bang-Event
  ├── "OSC/Sensor/Zone_001"        : float       ← Externe Hardware
  └── "MIDI/CC/01"                 : float       ← MIDI-Controller
```

Channels ersetzen NICHT das Component-System. Sie ergänzen es für den Teil der **nicht frame-synchron und nicht persistent** sein muss.
