# Zeit, Steuerung und Recording

Dieses Dokument beschreibt die Systeme fuer zeitliche Organisation, Zustandssteuerung, externe Eingaben und Aufzeichnung im VL.SceneGraph-Compositor.

---

## Constraint-System

### Constraint-Typen

| Constraint | Beschreibung |
|---|---|
| **ParameterLink** | `Clip A.Parameter X -> Clip B.Parameter Y` mit Modi: Direct, Inverse, Add, Multiply, Remap, Expression |
| **ActivityLink** | Follow, Inverse, Trigger, ChainAfter, Sync |
| **TempoSync** | Multiplier + PhaseOffset relativ zu einer Tempo-Quelle |
| **ExpressionConstraint** | Mathematische Ausdruecke, z.B. `sin(time * 2) * source.opacity` |
| **LookAtConstraint** | Rotation auf Ziel ausrichten (fuer 3D-Content) |
| **ParentConstraint** | Position/Rotation eines anderen Nodes folgen |
| **PositionConstraint** | Position mit Offset |

### Flache Constraint-Arrays (SoA)

Pro Constraint-Typ werden separate Arrays im `FlatConstraints`-Objekt gehalten. Diese werden beim Compile aus den Components extrahiert:

```csharp
public sealed class FlatConstraints
{
    public int LookAtCount;
    public int[] LookAt_OwnerIndex;
    public int[] LookAt_TargetIndex;
    public Vector3[] LookAt_UpVector;
    // ... analog fuer jeden Constraint-Typ
}
```

### Topologische Sortierung

Constraints koennen voneinander abhaengen: Constraint A schreibt Node X, Constraint B liest Node X, also muss A vor B ausgefuehrt werden. **Kahns Algorithmus** berechnet die Ausfuehrungsreihenfolge. Diese Berechnung findet nur bei strukturellen Aenderungen statt, nicht pro Frame.

Zyklen werden erkannt und als Fehler gemeldet.

### ConstraintSolver

Der Solver fuehrt Constraints in topologisch sortierter Reihenfolge aus. Nach jedem Constraint, der eine Transform aendert, wird `PropagateTransformToDescendants()` aufgerufen, um alle Nachkommen neu zu berechnen.

---

## Zeit-System und verschachtelte Timelines

### TimeContext-Component

Jeder Node kann seine eigene Zeitbasis haben oder die des Parents erben:

```csharp
[SceneComponent(FlatStorage = true)]
public partial record TimeContext(
    float LocalTime,
    float LocalDuration,
    float PlaybackRate,
    TimeMode Mode,
    bool IsTimeSource);

public enum TimeMode : byte
{
    Once,       // einmalige Wiedergabe, stoppt am Ende
    Loop,       // springt zurueck auf 0
    PingPong,   // Vorwaerts-Rueckwaerts-Schleife
    Hold,       // haelt den letzten Frame
    Free        // laeuft unbegrenzt (z.B. generative Clips)
}
```

### Zeit-Propagation (Top-Down)

Zeitwerte werden hierarchisch von oben nach unten weitergegeben. Jede Ebene kann einen eigenen Zeitraum, Geschwindigkeit und Modus definieren:

```
[Show]            <-- Master-Timeline (0:00 -> Unendlich)
  [Akt 1]         <-- Sub-Timeline (0:00 -> 5:00 relativ zum Parent)
    [Szene A]      <-- Sub-Sub-Timeline (0:00 -> 2:30)
      [Clip "Stars"] <-- Keyframes relativ zu Szene A
```

Wenn `Akt 1` auf halbe Geschwindigkeit gesetzt wird, laufen alle darin enthaltenen Clips automatisch halb so schnell. Looping, PingPong und andere Modi sind pro Ebene konfigurierbar.

### Keyframes

Animation basiert auf typisierten Tracks mit sortierten Keyframes:

```csharp
public record AnimationTrack(
    string ParameterName,
    Type ValueType,
    ImmutableArray<Keyframe> Keyframes,
    InterpolationMode DefaultInterpolation);

public record Keyframe(
    float Time,
    object Value,
    InterpolationMode Interpolation,
    float? TangentIn,
    float? TangentOut);

public enum InterpolationMode : byte
{
    Step,     // kein Uebergang, harter Schnitt
    Linear,   // lineare Interpolation
    Smooth,   // Hermite-Spline
    Bezier,   // kubische Bezier-Kurve
    Spring,   // federbasierte Dynamik
    Custom    // benutzerdefinierte Kurve
}
```

**Evaluation:** Binary Search findet das passende Keyframe-Paar, danach wird entsprechend dem `InterpolationMode` interpoliert.

### Cue-System

Cues sind benannte Zeitpunkte mit zugeordneten Aktionen und optionalen Parameter-Overrides:

```csharp
public record Cue(
    string Name,
    float Time,
    CueAction Action,
    ImmutableDictionary<string, object?>? ParameterOverrides);

public enum CueAction : byte
{
    Play, Stop, Pause, Resume,
    FadeIn, FadeOut,
    GotoAndPlay, GotoAndStop,
    Trigger
}
```

---

## StateMachine-System

### Definition

State Machines steuern, welche Clips unter welchen Bedingungen aktiv sind. Jeder State referenziert eine Menge aktiver Clips mit optionalen Parameter-Overrides:

```csharp
public record StateDefinition(
    string Name,
    ImmutableArray<string> ActiveClipIds,
    ImmutableDictionary<string, ImmutableDictionary<string, object?>> ClipParameters,
    float MinDuration,
    ImmutableArray<CueAction> OnEnter,
    ImmutableArray<CueAction> OnExit);

public record TransitionDefinition(
    string FromState,
    string ToState,
    TransitionCondition Condition,
    float CrossfadeDuration,
    InterpolationMode CrossfadeMode);

public record TransitionCondition(
    TransitionTrigger Trigger,
    string? Expression,
    float? AfterDuration,
    string? TriggerName);

public enum TransitionTrigger : byte
{
    Manual,         // User-Aktion
    AfterDuration,  // nach festgelegter Zeit
    OnComplete,     // wenn der aktuelle Clip fertig ist
    Expression,     // mathematischer Ausdruck wird wahr
    Random,         // zufaellig gewichtete Auswahl
    OnBeat,         // auf den naechsten Beat synchronisiert
    External        // externer Trigger (OSC, MIDI, etc.)
}
```

### Laufzeit-Zustand

Der aktuelle Zustand der State Machine wird als Component gespeichert:

```csharp
[SceneComponent(FlatStorage = true)]
public partial record StateMachineState(
    string CurrentState,
    float TimeInState,
    string? PendingTransition,
    float TransitionProgress);
```

### Generative vs. Authored Timeline

Zwei grundlegend verschiedene Arbeitsweisen:

- **Authored Timeline**: Klassische Keyframes mit festen Start-/End-Zeiten. Der Benutzer hat den zeitlichen Ablauf vorab angelegt.
- **Generative/Live Timeline**: Die FSM trifft Entscheidungen zur Laufzeit. Ein `ActivityLogger` zeichnet auf, was wann passiert. Die Timeline-UI zeigt eine scrollbare History der vergangenen Zustandswechsel.

Beide Modi koennen kombiniert werden: Eine authored Basis-Timeline mit generativen Verzweigungen via FSM.

---

## Externe Inputs

### Quellen

Alle unterstuetzten Input-Typen werden ueber ein einheitliches Enum beschrieben:

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

### ExternalInput-Component

```csharp
[SceneComponent]
public partial record ExternalInput(
    InputSourceKind Source,
    string? Address,
    Type OutputType,
    float SmoothingFactor,
    float InputMin, float InputMax,
    float OutputMin, float OutputMax);
```

### InputContext

Von VL befuellt, enthaelt Referenzen auf Audio-Analyzer, MIDI-Input, OSC-Receiver, BPM-Source etc. Wird pro Frame aktualisiert.

### Integration ueber den ConstraintSolver

Externe Inputs sind regulaere Nodes im Baum mit Output-Werten. Der bestehende **ConstraintSolver** routet die Werte an Clip-Parameter. Es ist kein Sondersystem noetig: ParameterLink-Constraints verbinden Input-Nodes mit Clip-Parametern, und der Solver wertet sie in der richtigen Reihenfolge aus.

---

## Recording-System

### Vier Aufnahme-Ebenen

| Ebene | Inhalt | Groesse |
|---|---|---|
| **1. Input Recording** | Rohe Daten: Stift-Positionen, MIDI-CCs, OSC-Messages | Kompakt, verlustfrei |
| **2. Parameter Recording** | Alle Parameter-Werte pro Frame | Medium |
| **3. State Recording** | Kompletter Szenegraph-Snapshot (dank Immutability billig) | Medium-Gross |
| **4. Output Recording** | Gerenderte Texturen, Audio-Streams | Gross, braucht Kompression |

### TrackRecorder\<T\>

Generischer zeitindizierter Recorder mit Binary-Search-Zugriff und Douglas-Peucker-Vereinfachung:

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

- **Record**: Fuegt einen Wert mit Zeitstempel hinzu
- **Evaluate**: Binary Search findet die passenden Nachbar-Werte, interpoliert dazwischen
- **Simplify**: Douglas-Peucker-Algorithmus entfernt redundante Samples innerhalb der angegebenen Toleranz

### Stroke Recording fuer Live-Drawing

Spezialisierter Recorder fuer Zeichnungs-Performances (z.B. Julias Live-Drawing bei Kopffarben):

```csharp
public readonly record struct StrokePoint(
    float Time,
    Vector2 Position,
    float Pressure,
    float Tilt,
    float Rotation,
    Vector2 Velocity);

public record StrokeData(
    int StrokeId,
    float StartTime,
    float EndTime,
    ImmutableArray<StrokePoint> Points,
    StrokeBrush Brush);
```

Features:
- **Progressive Wiedergabe**: Strich waechst visuell nach, wie beim Original-Zeichnen
- **Undo/Redo-Tracking**: Jeder Strich ist einzeln rueckgaengig machbar
- **Delta-Encoding**: Effiziente Kompression fuer Speicherung und Streaming

### Recording als Clip-Ersetzung

Eine Aufnahme kann einen Live-Clip ersetzen. Die `RecordingPlayback`-Component steuert das Verhaeltnis:

- `BlendWithLive = 0.0` — nur Recording
- `BlendWithLive = 1.0` — nur Live
- Werte dazwischen — Crossfade zwischen Live und Aufnahme

### Workflows

Drei typische Einsatz-Szenarien:

1. **Probe aufzeichnen, Show abspielen**: Julia malt in der Probe, die Aufnahme wird in der Show abgespielt. Timing kann angepasst werden.
2. **Live mit Fallback**: Wenn Julia aufhoert zu zeichnen (PenUp > 5s), Crossfade zur Aufnahme im Loop. Sobald sie weiter zeichnet, Crossfade zurueck zu Live.
3. **Geschichtete Aufnahmen**: Base Drawing (Probe Tag 1) + Detail Layer (Probe Tag 2) + Live Accents. Jede Schicht ist separat steuerbar.

### Speicher-Abschaetzung

| Szenario | Geschaetzte Groesse |
|---|---|
| 30 Minuten Zeichnung bei 60fps | ~3.5 MB |
| Ganze Show (20 Clips, 60 Min) | ~30 MB |

Die kompakten Groessen ergeben sich aus Delta-Encoding und der Tatsache, dass nur Aenderungen gespeichert werden.

### Binaeres Format `.vlrc`

Recordings werden in einem binaeren Format mit der Endung `.vlrc` gespeichert. Kernmerkmale:
- Delta-Encoding fuer Timestamps (float-Differenzen statt Absolutwerte)
- Delta-Encoding fuer Stroke-Punkte (Positionsdifferenzen)
- Kompakte Speicherung ermoeglicht schnelles Laden und Streaming
