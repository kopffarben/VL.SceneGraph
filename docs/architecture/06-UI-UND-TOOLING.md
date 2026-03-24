# UI und Tooling

## Ueberblick: Editor-Architektur

Alle Editor-Panels sind **VL.ImGui Widgets**. Sie folgen einem einheitlichen Muster:

- Lesen den `SceneGraph` als Property (wird jeden Frame gesetzt)
- Erzeugen `ISceneCommand`-Objekte bei User-Interaktion
- Commands fliessen durch den `UndoRedoStack`
- Der `UndoRedoStack` erzeugt `SceneEdit`s die der `Accumulator` einsammelt
- UI-State (Selektion, Zoom, Playback) lebt in **VL Channels**
- Globale Shortcuts werden im Hauptpatch geprueft, Panel-Shortcuts in den Widgets selbst

### Datenfluss-Diagramm

```
┌─────────────────────────────────────────────────────────────────┐
│  VL Hauptpatch (pro Frame)                                       │
│                                                                  │
│  1. Globale Shortcuts pruefen (Undo/Redo/Save/Play)             │
│  2. UndoStack.FlushPendingEdits() → SceneEdits → Accumulator   │
│  3. Accumulator liefert neuen SceneGraph                         │
│  4. SceneGraph als Property an alle Widgets setzen               │
│  5. Widgets rendern, User interagiert                            │
│  6. Widget ruft UndoStack.Execute(command) auf                   │
│  7. → Naechster Frame: Goto 1                                    │
└─────────────────────────────────────────────────────────────────┘
```

**Warum diese Reihenfolge?** Der Graph wird erst aktualisiert, dann gelesen. Widgets sehen immer den neuesten Stand. Commands die im aktuellen Frame erzeugt werden, wirken im naechsten Frame — ein sauberer Ein-Frame-Delay der Race Conditions verhindert.

---

## UndoRedoStack — Zentraler Edit-Hub

### Design-Entscheidung: Globaler Stack

Angelehnt an T3/Tixl: **ein** globaler Undo-Stack fuer die gesamte Applikation, kein Scope-basierter Stack pro Panel. Gruende:

- Einfacher mentaler Modus fuer den User ("Ctrl+Z macht das Letzte rueckgaengig")
- Kein Mapping-Problem "welcher Scope hat gerade Focus"
- Batch-Operationen ueber mehrere Panels hinweg sind ein einzelner Undo-Schritt
- Weniger Infrastruktur-Code

### ISceneCommand Interface

```csharp
/// <summary>
/// A reversible command that modifies the SceneGraph.
/// Follows the Before/After pattern: Do() captures state before applying,
/// Undo() restores the captured state.
/// </summary>
public interface ISceneCommand
{
    /// <summary>Human-readable label for Undo history display.</summary>
    string Label { get; }

    /// <summary>
    /// Executes the command. Returns the resulting SceneEdits.
    /// Must capture "before" state on first call.
    /// </summary>
    ImmutableArray<SceneEdit> Do(SceneGraph graph);

    /// <summary>
    /// Reverses the command. Returns SceneEdits that restore previous state.
    /// </summary>
    ImmutableArray<SceneEdit> Undo(SceneGraph graph);

    /// <summary>
    /// Updates the command's target value without creating a new undo entry.
    /// Used for continuous gestures (slider drags, color pickers).
    /// Returns empty if this command does not support in-flight updates.
    /// </summary>
    ImmutableArray<SceneEdit> UpdateValue(SceneGraph graph, object newValue);

    /// <summary>
    /// Whether this command supports UpdateValue() for in-flight gestures.
    /// </summary>
    bool SupportsInFlightUpdate { get; }
}
```

### UndoRedoStack Klassen-Skizze

```csharp
public sealed class UndoRedoStack
{
    private readonly Stack<ISceneCommand> _undoStack = new();
    private readonly Stack<ISceneCommand> _redoStack = new();
    private readonly List<SceneEdit> _pendingEdits = new();
    private ISceneCommand? _inFlightCommand;

    /// <summary>Current SceneGraph reference, updated each frame.</summary>
    public SceneGraph Graph { get; set; }

    /// <summary>
    /// Executes a command and pushes it onto the undo stack.
    /// Clears the redo stack (new branch).
    /// </summary>
    public void Execute(ISceneCommand command)
    {
        var edits = command.Do(Graph);
        _pendingEdits.AddRange(edits);
        _undoStack.Push(command);
        _redoStack.Clear();
    }

    /// <summary>
    /// Undoes the last command. If an in-flight gesture is active,
    /// cancels it instead.
    /// </summary>
    public void Undo()
    {
        // Cancel in-flight gesture first
        if (_inFlightCommand != null)
        {
            var edits = _inFlightCommand.Undo(Graph);
            _pendingEdits.AddRange(edits);
            _inFlightCommand = null;
            return;
        }

        if (_undoStack.Count == 0) return;
        var command = _undoStack.Pop();
        var undoEdits = command.Undo(Graph);
        _pendingEdits.AddRange(undoEdits);
        _redoStack.Push(command);
    }

    /// <summary>Redoes the last undone command.</summary>
    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        var command = _redoStack.Pop();
        var edits = command.Do(Graph);
        _pendingEdits.AddRange(edits);
        _undoStack.Push(command);
    }

    /// <summary>
    /// Begins a continuous gesture (e.g. slider drag).
    /// All subsequent UpdateGesture() calls modify the same command.
    /// </summary>
    public void BeginGesture(ISceneCommand command)
    {
        var edits = command.Do(Graph);
        _pendingEdits.AddRange(edits);
        _inFlightCommand = command;
    }

    /// <summary>
    /// Updates the in-flight gesture with a new value.
    /// No undo entry is created — the gesture is one logical operation.
    /// </summary>
    public void UpdateGesture(object newValue)
    {
        if (_inFlightCommand == null) return;
        var edits = _inFlightCommand.UpdateValue(Graph, newValue);
        _pendingEdits.AddRange(edits);
    }

    /// <summary>
    /// Ends the in-flight gesture and commits it as a single undo entry.
    /// </summary>
    public void EndGesture()
    {
        if (_inFlightCommand == null) return;
        _undoStack.Push(_inFlightCommand);
        _redoStack.Clear();
        _inFlightCommand = null;
    }

    /// <summary>
    /// Called once per frame from the main patch.
    /// Returns all pending edits and clears the internal buffer.
    /// The Accumulator consumes these edits to produce the next SceneGraph.
    /// </summary>
    public ImmutableArray<SceneEdit> FlushPendingEdits()
    {
        if (_pendingEdits.Count == 0)
            return ImmutableArray<SceneEdit>.Empty;

        var result = _pendingEdits.ToImmutableArray();
        _pendingEdits.Clear();
        return result;
    }

    public bool CanUndo => _undoStack.Count > 0 || _inFlightCommand != null;
    public bool CanRedo => _redoStack.Count > 0;
    public bool IsGestureActive => _inFlightCommand != null;
    public string? LastUndoLabel => _undoStack.Count > 0 ? _undoStack.Peek().Label : null;
    public string? LastRedoLabel => _redoStack.Count > 0 ? _redoStack.Peek().Label : null;
}
```

### SetComponentCommand — Beispiel

```csharp
/// <summary>
/// Sets a component on a SceneNode. Captures the previous value for undo.
/// Supports in-flight updates for continuous parameter editing.
/// </summary>
public sealed class SetComponentCommand<T> : ISceneCommand where T : IComponent
{
    private readonly string _nodeId;
    private readonly T _newValue;
    private T? _previousValue;
    private T _currentValue;

    public string Label => $"Set {typeof(T).Name} on {_nodeId}";
    public bool SupportsInFlightUpdate => true;

    public SetComponentCommand(string nodeId, T newValue)
    {
        _nodeId = nodeId;
        _newValue = newValue;
        _currentValue = newValue;
    }

    public ImmutableArray<SceneEdit> Do(SceneGraph graph)
    {
        var node = graph.FindNode(_nodeId);
        _previousValue = node?.GetComponent<T>();
        return ImmutableArray.Create(
            SceneEdit.SetComponent(_nodeId, _currentValue));
    }

    public ImmutableArray<SceneEdit> Undo(SceneGraph graph)
    {
        if (_previousValue == null)
            return ImmutableArray.Create(
                SceneEdit.RemoveComponent<T>(_nodeId));
        return ImmutableArray.Create(
            SceneEdit.SetComponent(_nodeId, _previousValue));
    }

    public ImmutableArray<SceneEdit> UpdateValue(SceneGraph graph, object newValue)
    {
        _currentValue = (T)newValue;
        return ImmutableArray.Create(
            SceneEdit.SetComponent(_nodeId, _currentValue));
    }
}
```

### MacroCommand — Batch-Operationen

```csharp
/// <summary>
/// Groups multiple commands into a single undo step.
/// Undo reverses commands in reverse order.
/// </summary>
public sealed class MacroCommand : ISceneCommand
{
    private readonly ImmutableArray<ISceneCommand> _commands;

    public string Label { get; }
    public bool SupportsInFlightUpdate => false;

    public MacroCommand(string label, IEnumerable<ISceneCommand> commands)
    {
        Label = label;
        _commands = commands.ToImmutableArray();
    }

    public ImmutableArray<SceneEdit> Do(SceneGraph graph)
    {
        var builder = ImmutableArray.CreateBuilder<SceneEdit>();
        foreach (var cmd in _commands)
            builder.AddRange(cmd.Do(graph));
        return builder.ToImmutable();
    }

    public ImmutableArray<SceneEdit> Undo(SceneGraph graph)
    {
        var builder = ImmutableArray.CreateBuilder<SceneEdit>();
        // Reverse order for correct undo semantics
        for (int i = _commands.Length - 1; i >= 0; i--)
            builder.AddRange(_commands[i].Undo(graph));
        return builder.ToImmutable();
    }

    public ImmutableArray<SceneEdit> UpdateValue(SceneGraph graph, object newValue)
        => ImmutableArray<SceneEdit>.Empty;
}
```

### In-Flight Command Pattern — Slider-Drags

Das In-Flight Pattern loest ein zentrales UX-Problem: Wenn ein User einen Slider zieht, soll das Ergebnis live sichtbar sein, aber nur **ein** Undo-Eintrag entstehen.

```
MouseDown  → UndoStack.BeginGesture(new SetComponentCommand<Opacity>(...))
MouseDrag  → UndoStack.UpdateGesture(newOpacity)  // kein Undo-Eintrag
MouseDrag  → UndoStack.UpdateGesture(newOpacity)  // kein Undo-Eintrag
MouseUp    → UndoStack.EndGesture()                // jetzt ein Eintrag

Ctrl+Z     → Gesamter Drag wird rueckgaengig gemacht
```

**Undo waehrend In-Flight:** Wenn der User Ctrl+Z drueckt waehrend ein Gesture aktiv ist, wird die Gesture abgebrochen (Wert vor Gesture-Beginn wiederhergestellt). Der naechste Ctrl+Z macht dann den davor liegenden Stack-Eintrag rueckgaengig.

---

## Inspector — SceneComponentEditorFactory

### Grundprinzip

Der Inspector nutzt VLs bestehendes **ObjectEditor-System** (`IObjectEditorFactory` / `IObjectEditor`) anstatt eigene Widgets fuer jeden Typ zu bauen. Das gibt uns kostenlos alle VL-Standard-Widgets (Slider, ColorEdit, Checkbox, Enum-Dropdown etc.) und erlaubt VL-Nutzern eigene Editoren beizusteuern.

### SceneComponentEditorFactory

```csharp
/// <summary>
/// Creates editors for SceneGraph C# Record components.
/// Uses reflection to enumerate record properties and creates
/// Channel{T} per property so VL's DefaultFactory can provide leaf widgets.
/// </summary>
public sealed class SceneComponentEditorFactory : IObjectEditorFactory
{
    /// <summary>
    /// Returns true if the type is an IComponent record.
    /// </summary>
    public bool CanEdit(Type type)
        => typeof(IComponent).IsAssignableFrom(type)
           && type.IsValueType is false; // records only

    /// <summary>
    /// Creates a PropertyEditor per public property of the record.
    /// Each PropertyEditor wraps a Channel{T} that VL's default
    /// editor factory can bind to.
    /// </summary>
    public IObjectEditor CreateEditor(
        Type componentType,
        IObjectEditorFactory leafFactory)
    {
        var properties = componentType.GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        var propertyEditors = new List<PropertyEditor>();
        foreach (var prop in properties)
        {
            if (!prop.CanRead) continue;

            // Create a Channel<T> for this property
            var channelType = typeof(Channel<>).MakeGenericType(prop.PropertyType);
            var channel = Activator.CreateInstance(channelType);

            // Let VL's default factory create the leaf widget
            // (respects [WidgetType], [Min], [Max] attributes)
            var leafEditor = leafFactory.CreateEditor(
                prop.PropertyType, leafFactory);

            propertyEditors.Add(new PropertyEditor(
                prop, channel!, leafEditor));
        }

        return new ComponentEditor(componentType, propertyEditors);
    }
}
```

### PropertyEditor — Pro Property ein Channel

```csharp
/// <summary>
/// Wraps a single record property as a Channel{T} + leaf widget.
/// SyncValues() writes the current component value into the channel,
/// CollectEdits() reads back user-modified values.
/// </summary>
internal sealed class PropertyEditor
{
    public PropertyInfo Property { get; }
    public object Channel { get; }        // Channel<T>, boxed
    public IObjectEditor LeafEditor { get; }

    private object? _lastSyncedValue;

    public PropertyEditor(
        PropertyInfo property, object channel, IObjectEditor leafEditor)
    {
        Property = property;
        Channel = channel;
        LeafEditor = leafEditor;
    }

    /// <summary>
    /// Writes the current component value into the channel.
    /// Cheap operation — no widget rebuild needed.
    /// </summary>
    public void SyncValue(IComponent component)
    {
        var value = Property.GetValue(component);
        if (Equals(value, _lastSyncedValue)) return;
        _lastSyncedValue = value;
        SetChannelValue(Channel, value);
    }

    /// <summary>
    /// Reads the channel value back. If it differs from the component,
    /// the user has edited it and we need to create a command.
    /// </summary>
    public (bool changed, object? newValue) CollectEdit(IComponent component)
    {
        var channelValue = GetChannelValue(Channel);
        var currentValue = Property.GetValue(component);
        if (Equals(channelValue, currentValue))
            return (false, null);
        return (true, channelValue);
    }

    private static void SetChannelValue(object channel, object? value)
    {
        // Reflection call to IChannel<T>.Value setter
        var prop = channel.GetType().GetProperty("Value")!;
        prop.SetValue(channel, value);
    }

    private static object? GetChannelValue(object channel)
    {
        var prop = channel.GetType().GetProperty("Value")!;
        return prop.GetValue(channel);
    }
}
```

### ChainedEditorFactory

```csharp
/// <summary>
/// Chains multiple editor factories with priority:
/// 1. User custom factories (VL-patched)
/// 2. SceneComponentEditorFactory (our record handler)
/// 3. DefaultObjectEditorFactory (VL built-in: sliders, checkboxes, etc.)
///
/// First factory that returns CanEdit=true wins.
/// </summary>
public sealed class ChainedEditorFactory : IObjectEditorFactory
{
    private readonly ImmutableArray<IObjectEditorFactory> _factories;

    public ChainedEditorFactory(
        IEnumerable<IObjectEditorFactory> userFactories,
        SceneComponentEditorFactory sceneFactory,
        IObjectEditorFactory defaultFactory)
    {
        _factories = userFactories
            .Append(sceneFactory)
            .Append(defaultFactory)
            .ToImmutableArray();
    }

    public bool CanEdit(Type type)
        => _factories.Any(f => f.CanEdit(type));

    public IObjectEditor CreateEditor(
        Type type, IObjectEditorFactory leafFactory)
    {
        foreach (var factory in _factories)
        {
            if (factory.CanEdit(type))
                return factory.CreateEditor(type, leafFactory);
        }
        throw new InvalidOperationException(
            $"No editor factory found for {type.Name}");
    }
}
```

### Inspector Widget mit Caching

```csharp
/// <summary>
/// The Inspector widget. Caches editors as long as the selection
/// and component hash are unchanged. SyncValues() per frame is cheap.
/// </summary>
public sealed class InspectorWidget
{
    private string? _cachedNodeId;
    private int _cachedComponentHash;
    private IObjectEditor? _cachedEditor;
    private readonly ChainedEditorFactory _editorFactory;

    public SceneGraph Graph { get; set; }
    public UndoRedoStack UndoStack { get; set; }

    /// <summary>Selected node ID, read from Channel.</summary>
    public string? SelectedNodeId { get; set; }

    public void Draw(WidgetContext ctx)
    {
        if (SelectedNodeId == null) return;
        var node = Graph.FindNode(SelectedNodeId);
        if (node == null) return;

        foreach (var component in node.Components)
        {
            var componentHash = component.GetHashCode();
            var needsRebuild =
                _cachedNodeId != SelectedNodeId ||
                _cachedComponentHash != componentHash ||
                _cachedEditor == null;

            if (needsRebuild)
            {
                _cachedEditor = _editorFactory.CreateEditor(
                    component.GetType(), _editorFactory);
                _cachedNodeId = SelectedNodeId;
                _cachedComponentHash = componentHash;
            }

            // Cheap per-frame sync: push current values into channels
            if (_cachedEditor is ComponentEditor ce)
                ce.SyncValues(component);

            // Draw the editor widgets (VL.ImGui)
            _cachedEditor.Draw(ctx);

            // Collect edits: did user change any value?
            if (_cachedEditor is ComponentEditor ce2)
            {
                var edits = ce2.CollectEdits(component);
                foreach (var (prop, newValue) in edits)
                {
                    // Create with-expression to produce new record
                    var newComponent = RecordHelper.With(
                        component, prop.Name, newValue);
                    var cmd = new SetComponentCommand(
                        SelectedNodeId, newComponent);
                    UndoStack.Execute(cmd);
                }
            }
        }
    }
}
```

**VLs WidgetType/Min/Max Attribute** auf den C# Record Properties funktionieren direkt, weil VLs `DefaultObjectEditorFactory` sie per Reflection liest:

```csharp
[SceneComponent]
public partial record Opacity(
    [property: WidgetType("Slider")]
    [property: Min(0.0f)]
    [property: Max(1.0f)]
    float Value = 1.0f
);
```

---

## Panel-Widgets

Alle Panels bekommen drei Properties:

| Property | Typ | Beschreibung |
|----------|-----|-------------|
| `Graph` | `SceneGraph` | Aktueller immutabler Szenegraph, jeden Frame gesetzt |
| `UndoStack` | `UndoRedoStack` | Zentraler Edit-Hub fuer Commands |
| `Hub` | `IChannelHub` | Via `IChannelHub.HubForApp` — fuer UI-State Channels |

### TransportBar

Play/Pause/Stop/BPM/REC Buttons. Schreibt UI-State direkt in Channels:

- `UI/Transport/IsPlaying` → `Channel<bool>`
- `UI/Transport/PlayheadTime` → `Channel<float>`
- `UI/Transport/BPM` → `Channel<float>`

Kein Command noetig — Playback-State ist kein Graph-State und braucht kein Undo.

### LayerStack

Hierarchische Darstellung der SceneNodes als verschachtelte Liste.

- **Solo/Mute/Opacity** pro Layer und pro Clip
- **Drag-Reorder**: Erzeugt `ReorderChildrenCommand`
- **Doppelklick** auf Clip → `OpenPatch` (siehe Context-Menues)
- **Rechtsklick** → Context-Menue (Delete, Duplicate, Bypass, Save as Preset)
- Drop-Zone pro Layer mit Typ-Validierung via `SlotType`

### TimelineEditor

Drei Modi (umschaltbar via `UI/Timeline/Mode` Channel):

| Modus | Inhalt |
|-------|--------|
| **Clips** | Clip-Bloecke auf der Timeline (authored: feste Bloecke, generative: aufgezeichnete History) |
| **Keyframes** | Kurven-Editor des selektierten Clips |
| **Activity** | Activity-Verlaeufe aller Clips als ueberlagerte Graphen |

Features: Zoom/Scroll (Channel-basiert), Follow-Playhead-Modus, Snap-to-Beat bei aktiver BPM.

### Inspector

Auto-generierte Component-Editors (siehe Abschnitt oben). Plus:

- **[K]-Button** pro Parameter fuer Keyframe-Toggle
- **Constraint-Indikator** bei verlinkten Parametern
- **ControlSource-Dropdown** (Manual, MIDI, OSC, Expression)
- **Filter** via `UI/Inspector/Filter` Channel

### FSMEditor

State-Boxes und Transition-Pfeile als node-basierter Graph.

- Klick auf State-Box → manueller State-Wechsel (erzeugt `ForceStateCommand`)
- Transition-Pfeile zeigen Condition-Labels
- Aktiver State ist visuell hervorgehoben (gruen)

### PresetPanel

- Preset-Buttons (nummeriert, farbcodiert)
- **Capture**: Aktuellen Zustand als Preset speichern
- **Apply**: Preset auf selektierten Node anwenden (erzeugt `ApplyPresetCommand`)
- **Morph-Slider**: Stufenlose Ueberblendung zwischen zwei Presets (In-Flight Gesture)

### NodeBrowser

Typ-Discovery und Clip-Erstellung (siehe separater Abschnitt unten).

### Viewport

Render-Output Vorschau. Zeigt den Compositor-Output (Stride Texture). Kein eigener Edit-State — reine Darstellung.

---

## Context-Menues

### ImGui-Integration

Context-Menues nutzen ImGuis eingebaute `BeginPopupContextItem()` / `BeginPopupContextWindow()`. Kein eigenes Popup-System noetig.

### IContextMenuContributor — Erweiterbarkeit

```csharp
/// <summary>
/// Extension point for context menu items.
/// Plugins can register contributors to add custom entries.
/// </summary>
public interface IContextMenuContributor
{
    /// <summary>
    /// Called when a context menu opens for a given node.
    /// Return menu items to add.
    /// </summary>
    IEnumerable<ContextMenuItem> GetItems(
        SceneNode node, SceneGraph graph);
}

public record ContextMenuItem(
    string Label,           // "Delete  Del"
    Action Execute,         // () => undoStack.Execute(new DeleteCommand(...))
    bool Enabled = true,
    string? Icon = null);
```

### Standard-Menuepunkte

| Menuepunkt | Aktion | Shortcut |
|-----------|--------|----------|
| Delete | `DeleteNodeCommand` | Del |
| Duplicate | `DuplicateNodeCommand` | Ctrl+D |
| Bypass | `SetBypassCommand` | B |
| Save as Preset | `CapturePresetCommand` | — |

### VL-Integration: Open Patch

Der haeufigste Context-Menue-Eintrag und Doppelklick-Aktion: Den VL-Patch eines Clips im vvvv-Editor oeffnen.

```csharp
// "Open Patch" — oeffnet den VL-Patch im vvvv Editor
public void OpenPatch(SceneNode clipNode)
{
    var clipRef = clipNode.GetComponent<ClipReference>();
    if (clipRef == null) return;

    // UniqueId aus DocumentId + PersistentId konstruieren
    var uniqueId = new UniqueId(
        clipRef.DocumentId,
        clipRef.PersistentId);

    // VLs SessionNodes API: navigiert den vvvv-Editor zum Patch
    SessionNodes.ShowPatchOfNode(uniqueId, toDefinition: false);
}

// "Show in Parent Patch" — zeigt den Node im uebergeordneten Patch
public void ShowInParentPatch(SceneNode clipNode)
{
    var clipRef = clipNode.GetComponent<ClipReference>();
    if (clipRef == null) return;

    var uniqueId = new UniqueId(
        clipRef.DocumentId,
        clipRef.PersistentId);

    SessionNodes.ShowPatchOfNode(uniqueId, toDefinition: true);
}
```

**Doppelklick auf Clip** → `OpenPatch` ist der haeufigste Fall und die wichtigste Navigation zwischen Compositor-UI und vvvv-Editor.

---

## NodeBrowser

### Discovery ueber TypeRegistry

Der NodeBrowser findet verfuegbare Clip-Typen durch VLs TypeRegistry:

```csharp
/// <summary>
/// Discovers all types implementing ISceneClip in the current VL session.
/// </summary>
public static ImmutableArray<ClipTypeInfo> DiscoverClipTypes(
    IVLTypeRegistry typeRegistry)
{
    var builder = ImmutableArray.CreateBuilder<ClipTypeInfo>();

    foreach (var type in typeRegistry.RegisteredTypes)
    {
        if (!typeof(ISceneClip).IsAssignableFrom(type))
            continue;

        var category = CategorizeClipType(type);
        builder.Add(new ClipTypeInfo(
            Type: type,
            DisplayName: type.Name,
            Category: category,
            Icon: GetIconForCategory(category)));
    }

    return builder.ToImmutable();
}

/// <summary>
/// Categorizes clip types based on their interfaces.
/// </summary>
private static string CategorizeClipType(Type type)
{
    if (typeof(ITextureGenerator).IsAssignableFrom(type))
        return "Texture Generators";
    if (typeof(IAudioGenerator).IsAssignableFrom(type))
        return "Audio Generators";
    if (typeof(ITextureFX).IsAssignableFrom(type))
        return "Texture FX";
    if (typeof(IDataProcessor).IsAssignableFrom(type))
        return "Data Processors";
    return "Other";
}
```

### Slot-Validierung: Warning statt Filter

Inkompatible Nodes werden **nicht herausgefiltert**, sondern **ausgegraut mit Warning** angezeigt. Der User kann sie trotzdem einfuegen. Gruende:

- Slot-Kompatibilitaet kann sich zur Laufzeit aendern
- User moechte vielleicht erst den Slot-Typ aendern
- Harte Filter sind frustrierend ("Wo ist mein Node hin?")

```csharp
/// <summary>
/// Validates whether a clip type is compatible with a target slot.
/// Returns a validation result (not a bool) — incompatible shows warning.
/// </summary>
public static SlotValidation ValidateSlotCompatibility(
    ClipTypeInfo clipType, SlotType targetSlot)
{
    var outputType = clipType.Type
        .GetInterfaces()
        .FirstOrDefault(i => i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(ISceneClip<>))
        ?.GetGenericArguments()[0];

    if (outputType == null)
        return SlotValidation.Warning("No typed output detected");

    if (targetSlot.IsCompatibleWith(outputType))
        return SlotValidation.Ok;

    return SlotValidation.Warning(
        $"Output {outputType.Name} may not match slot {targetSlot.Name}");
}
```

### Zwei Modi: Popup und Panel

| Modus | Ausloeser | Verhalten |
|-------|-----------|-----------|
| **Popup** | Rechtsklick → "Add Clip" | Oeffnet als ImGui Popup, Auswahl fuegt direkt ein |
| **Panel** | Dockbares Panel | Bleibt offen, unterstuetzt Drag & Drop auf Layer |

### Hotswap-Kompatibilitaet

Bei VL Hot-Reload (`OnSwap`) wird der TypeRegistry-Cache invalidiert. Neue oder geaenderte Clip-Typen erscheinen sofort im NodeBrowser:

```csharp
// In CompositorRuntime.OnSwap():
_clipTypeCache = null; // Force re-discovery on next frame
```

### CreateNodeFromClipType

```csharp
/// <summary>
/// Creates a new SceneNode with all required components for a clip.
/// Called when user selects a type from the NodeBrowser.
/// </summary>
public static ISceneCommand CreateNodeFromClipType(
    ClipTypeInfo clipType, string parentId)
{
    var commands = new List<ISceneCommand>
    {
        new AddChildCommand(parentId, clipType.DisplayName),
        new SetComponentCommand<ClipReference>(
            newNodeId,
            new ClipReference(
                clipType.Type.Assembly.Location,
                clipType.Type.GetPersistentId())),
        new SetComponentCommand<ClipLifetime>(
            newNodeId,
            new ClipLifetime(LifetimeMode.Manual)),
        new SetComponentCommand<LayerBlending>(
            newNodeId,
            LayerBlending.Default),
    };

    return new MacroCommand(
        $"Add {clipType.DisplayName}", commands);
}
```

---

## Shortcut-System

### Design: T3-Pattern

Angelehnt an T3/Tixl: Shortcuts als typisierte Enum-Werte mit zugeordneten Key-Bindings. Keine String-basierte Lookup-Tabelle.

### CompositorActions Enum

```csharp
/// <summary>
/// All bindable actions in the compositor.
/// Each value can be mapped to a keyboard shortcut.
/// </summary>
public enum CompositorActions
{
    // Global (checked in main patch)
    Undo,
    Redo,
    Save,
    SaveAs,
    PlayPause,
    Stop,
    Record,

    // Selection
    SelectAll,
    DeselectAll,
    DeleteSelected,
    DuplicateSelected,

    // Navigation
    FocusSelected,
    OpenPatch,
    ShowInParent,

    // Timeline
    TimelineZoomIn,
    TimelineZoomOut,
    TimelineFollowToggle,
    TimelineModeClips,
    TimelineModeKeyframes,
    TimelineModeActivity,

    // Inspector
    KeyframeToggle,

    // Clip
    BypassToggle,
    SoloToggle,
    MuteToggle,

    // Node Browser
    OpenNodeBrowser,
}
```

### KeyBinding Klasse

```csharp
/// <summary>
/// A keyboard shortcut binding: modifier keys + trigger key + context flags.
/// </summary>
public readonly record struct KeyBinding(
    Keys Key,
    ModifierKeys Modifiers = ModifierKeys.None,
    ActionContext Context = ActionContext.NeedsWindowFocus)
{
    /// <summary>Human-readable label for menus: "Ctrl+Z", "Del", "Space".</summary>
    public string ToLabel()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        parts.Add(Key.ToString());
        return string.Join("+", parts);
    }
}

[Flags]
public enum ModifierKeys : byte
{
    None  = 0,
    Ctrl  = 1,
    Shift = 2,
    Alt   = 4,
}

[Flags]
public enum ActionContext : byte
{
    /// <summary>Action triggers only when its panel has OS window focus.</summary>
    NeedsWindowFocus = 1,

    /// <summary>Action triggers when mouse hovers over its panel.</summary>
    NeedsWindowHover = 2,

    /// <summary>Action triggers even while a text input is active.</summary>
    ActiveDuringInput = 4,

    /// <summary>Action is active only while the key is held down.</summary>
    KeyHoldOnly = 8,
}
```

### Shortcuts Static Class

```csharp
/// <summary>
/// Static helper for querying shortcuts in widgets.
/// Reads ImGui key state and matches against the active KeyMap.
/// </summary>
public static class Shortcuts
{
    private static KeyMap _activeKeyMap = DefaultKeyMap.Create();

    public static KeyMap ActiveKeyMap
    {
        get => _activeKeyMap;
        set => _activeKeyMap = value;
    }

    /// <summary>
    /// Checks if the given action was triggered this frame.
    /// Respects ActionContext flags (focus, hover, input state).
    /// </summary>
    public static bool Triggered(this CompositorActions action)
    {
        if (!_activeKeyMap.TryGetBinding(action, out var binding))
            return false;

        // Check context flags
        if (binding.Context.HasFlag(ActionContext.NeedsWindowFocus)
            && !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            return false;

        if (binding.Context.HasFlag(ActionContext.NeedsWindowHover)
            && !ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows))
            return false;

        if (!binding.Context.HasFlag(ActionContext.ActiveDuringInput)
            && ImGui.GetIO().WantTextInput)
            return false;

        // Check key state
        bool keyPressed = binding.Context.HasFlag(ActionContext.KeyHoldOnly)
            ? ImGui.IsKeyDown(binding.Key.ToImGuiKey())
            : ImGui.IsKeyPressed(binding.Key.ToImGuiKey(), repeat: false);

        if (!keyPressed) return false;

        // Check modifiers
        var io = ImGui.GetIO();
        if (binding.Modifiers.HasFlag(ModifierKeys.Ctrl) != io.KeyCtrl)
            return false;
        if (binding.Modifiers.HasFlag(ModifierKeys.Shift) != io.KeyShift)
            return false;
        if (binding.Modifiers.HasFlag(ModifierKeys.Alt) != io.KeyAlt)
            return false;

        return true;
    }

    /// <summary>
    /// Returns the display label for a context menu entry.
    /// Example: "Delete  Del", "Undo  Ctrl+Z"
    /// </summary>
    public static string Label(this CompositorActions action)
    {
        var name = action switch
        {
            CompositorActions.Undo => "Undo",
            CompositorActions.Redo => "Redo",
            CompositorActions.Save => "Save",
            CompositorActions.DeleteSelected => "Delete",
            CompositorActions.DuplicateSelected => "Duplicate",
            CompositorActions.BypassToggle => "Bypass",
            CompositorActions.OpenPatch => "Open Patch",
            _ => action.ToString()
        };

        if (_activeKeyMap.TryGetBinding(action, out var binding))
            return $"{name}  {binding.ToLabel()}";

        return name;
    }
}
```

### DefaultKeyMap — Sinnvolle Defaults

```csharp
/// <summary>
/// Factory key map with sensible defaults.
/// Can be overridden by user JSON config.
/// </summary>
public static class DefaultKeyMap
{
    public static KeyMap Create() => new KeyMap(
        new Dictionary<CompositorActions, KeyBinding>
        {
            // Global
            [CompositorActions.Undo]       = new(Keys.Z, ModifierKeys.Ctrl),
            [CompositorActions.Redo]        = new(Keys.Z, ModifierKeys.Ctrl | ModifierKeys.Shift),
            [CompositorActions.Save]        = new(Keys.S, ModifierKeys.Ctrl),
            [CompositorActions.SaveAs]      = new(Keys.S, ModifierKeys.Ctrl | ModifierKeys.Shift),
            [CompositorActions.PlayPause]   = new(Keys.Space, Context: ActionContext.NeedsWindowFocus),
            [CompositorActions.Stop]        = new(Keys.Escape),
            [CompositorActions.Record]      = new(Keys.R, ModifierKeys.Ctrl),

            // Selection
            [CompositorActions.SelectAll]       = new(Keys.A, ModifierKeys.Ctrl),
            [CompositorActions.DeselectAll]      = new(Keys.A, ModifierKeys.Ctrl | ModifierKeys.Shift),
            [CompositorActions.DeleteSelected]   = new(Keys.Delete),
            [CompositorActions.DuplicateSelected]= new(Keys.D, ModifierKeys.Ctrl),

            // Navigation
            [CompositorActions.FocusSelected] = new(Keys.F),
            [CompositorActions.OpenPatch]     = new(Keys.Enter),
            [CompositorActions.ShowInParent]   = new(Keys.Enter, ModifierKeys.Shift),

            // Timeline
            [CompositorActions.TimelineZoomIn]  = new(Keys.OemPlus, ModifierKeys.Ctrl),
            [CompositorActions.TimelineZoomOut] = new(Keys.OemMinus, ModifierKeys.Ctrl),
            [CompositorActions.TimelineFollowToggle] = new(Keys.L),
            [CompositorActions.TimelineModeClips]     = new(Keys.D1, Context: ActionContext.NeedsWindowHover),
            [CompositorActions.TimelineModeKeyframes] = new(Keys.D2, Context: ActionContext.NeedsWindowHover),
            [CompositorActions.TimelineModeActivity]  = new(Keys.D3, Context: ActionContext.NeedsWindowHover),

            // Inspector
            [CompositorActions.KeyframeToggle] = new(Keys.K),

            // Clip
            [CompositorActions.BypassToggle] = new(Keys.B),
            [CompositorActions.SoloToggle]   = new(Keys.S, Context: ActionContext.NeedsWindowHover),
            [CompositorActions.MuteToggle]   = new(Keys.M, Context: ActionContext.NeedsWindowHover),

            // Node Browser
            [CompositorActions.OpenNodeBrowser] = new(Keys.Tab),
        });
}
```

### Beispiel: Verwendung in einem Widget

```csharp
// In einem Timeline-Widget:
public void DrawTimelinePanel(WidgetContext ctx)
{
    // Panel-Shortcuts (nur aktiv wenn dieses Panel Hover/Focus hat)
    if (CompositorActions.TimelineModeClips.Triggered())
        _hub.GetChannel<TimelineViewMode>("UI/Timeline/Mode")
            .Value = TimelineViewMode.Clips;

    if (CompositorActions.TimelineModeKeyframes.Triggered())
        _hub.GetChannel<TimelineViewMode>("UI/Timeline/Mode")
            .Value = TimelineViewMode.Keyframes;

    if (CompositorActions.TimelineZoomIn.Triggered())
        _hub.GetChannel<float>("UI/Timeline/Zoom").Value *= 1.2f;

    // Context menu with shortcut labels
    if (ImGui.BeginPopupContextItem("timeline_ctx"))
    {
        if (ImGui.MenuItem(CompositorActions.DeleteSelected.Label()))
            UndoStack.Execute(new DeleteNodeCommand(_selectedId));

        if (ImGui.MenuItem(CompositorActions.DuplicateSelected.Label()))
            UndoStack.Execute(new DuplicateNodeCommand(_selectedId));

        ImGui.EndPopup();
    }

    // ... rest of timeline rendering
}
```

### KeyMap Serialisierung

Die `KeyMap` ist als JSON serialisierbar und austauschbar:

```json
{
  "bindings": {
    "Undo": { "key": "Z", "modifiers": "Ctrl" },
    "Redo": { "key": "Z", "modifiers": "Ctrl+Shift" },
    "PlayPause": { "key": "Space", "context": "NeedsWindowFocus" },
    "DeleteSelected": { "key": "Delete" },
    "BypassToggle": { "key": "B" }
  }
}
```

---

## Custom Windows und Docking

### VL.ImGui.Stride DockSpace

VL.ImGui.Stride bietet `DockSpaceOverViewport` mit `dockingEnabled`. Jedes Widget das in einem `Window()` Aufruf gerendert wird, kann vom User frei gedockt werden.

```csharp
// Im Hauptpatch (VL):
// DockSpaceOverViewport(dockingEnabled: true)
//   → Window("Transport", transportWidget)
//   → Window("Layers", layerStackWidget)
//   → Window("Timeline", timelineWidget)
//   → Window("Inspector", inspectorWidget)
//   → Window("Viewport", viewportWidget)
```

### Drei Varianten fuer eigene Panels

| Variante | Beschreibung | Anwendung |
|----------|-------------|-----------|
| **Reines VL-Patch** | Widget als VL Process Node gepatcht | Quick prototyping, Show-spezifische Tools |
| **C# Widget-Klasse** | Implementiert als C# Klasse mit ImGui Calls | Performance-kritische oder komplexe Panels |
| **Hybrid** | C# Kern + VL Konfiguration | Standard-Panels mit patchbaren Optionen |

Ein User-gepatchtes Widget wird einfach eingedockt:

```
// VL Patch:
MyCustomMonitor widget  // user's own VL Process Node
Window("My Monitor", widget)  // → docks into the compositor layout
```

### Layout-Persistierung

ImGui speichert das Docking-Layout intern. Wir persistieren es in die Show-Datei oder in einen Channel:

```csharp
// Save layout
var iniData = ImGui.SaveIniSettingsToMemory();
_hub.GetChannel<string>("UI/Layout/ImGuiIni").Value = iniData;

// Restore layout (on show load)
var iniData = _hub.GetChannel<string>("UI/Layout/ImGuiIni").Value;
if (!string.IsNullOrEmpty(iniData))
    ImGui.LoadIniSettingsFromMemory(iniData);
```

### Multi-Monitor

VL.ImGui.Stride unterstuetzt separate OS-Fenster ueber `ImGuiWindow`. Jedes Panel kann in ein eigenes Fenster gezogen werden — ideal fuer Multi-Monitor-Setups bei Shows:

- Monitor 1: Viewport (Fullscreen Output)
- Monitor 2: Timeline + Layers + Inspector
- Monitor 3: FSM + Presets

---

## NodeFactories fuer VL

### Zweck

Dynamisch VL-Nodes erzeugen basierend auf registrierten Components. Nutzt VLs `IVLNodeDescriptionFactory`.

### Component Get/Set/Has Nodes

Fuer jede registrierte Component werden automatisch drei Nodes erzeugt:

- `GetTransform3D`: SceneNode → Transform3D?
- `SetTransform3D`: (SceneNode, Transform3D) → SceneNode
- `HasTransform3D`: SceneNode → bool

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

## UI-State via Channels

### Warum Channels?

UI-State lebt in VL Channels (`IChannel<T>`) statt in einer monolithischen State-Klasse. Gruende:

1. **VL.ImGui Widgets unterstuetzen `IChannel<T>` nativ** — kein Adapter-Code noetig
2. **Mehrere Panels reagieren auf dieselben Werte** — z.B. Selektion aendert Inspector UND Timeline-Highlight
3. **VL-natives Pattern** — VL-Nutzer kennen Channels bereits und koennen eigene Panels anschliessen
4. **Entkopplung** — Panels muessen einander nicht kennen, nur den Channel-Namen
5. **Serialisierbar** — Channel-Werte koennen in die Show-Datei persistiert werden

### Channel-Verzeichnis

| Channel-Pfad | Typ | Beschreibung |
|-------------|-----|-------------|
| `UI/Selection/NodeId` | `Channel<string?>` | Aktuell selektierter Node (Primaer-Selektion) |
| `UI/Selection/Multi` | `Channel<ImmutableHashSet<string>>` | Multi-Selektion (Ctrl+Klick, Marquee) |
| `UI/Timeline/Zoom` | `Channel<float>` | Timeline-Zoom Level (1.0 = default) |
| `UI/Timeline/Scroll` | `Channel<float>` | Timeline horizontaler Scroll-Offset in Sekunden |
| `UI/Timeline/Mode` | `Channel<TimelineViewMode>` | Aktiver Modus: Clips, Keyframes, Activity |
| `UI/Timeline/FollowPlayhead` | `Channel<bool>` | Auto-Scroll der Timeline zum Playhead |
| `UI/Transport/IsPlaying` | `Channel<bool>` | Playback aktiv |
| `UI/Transport/PlayheadTime` | `Channel<float>` | Aktuelle Playhead-Position in Sekunden |
| `UI/Transport/BPM` | `Channel<float>` | Beats per Minute fuer Snap-to-Beat |
| `UI/Inspector/Filter` | `Channel<string>` | Text-Filter fuer Inspector-Properties |
| `UI/DragDrop/Active` | `Channel<bool>` | Ob gerade eine Drag-Operation laeuft |
| `UI/DragDrop/Payload` | `Channel<object?>` | Payload der aktuellen Drag-Operation |

### Beispiel: Selektion aendert mehrere Panels

```csharp
// LayerStack: User klickt auf einen Node
var selectionChannel = hub.GetChannel<string?>("UI/Selection/NodeId");
if (ImGui.Selectable(node.Name, isSelected))
    selectionChannel.Value = node.Id;

// Inspector: Reagiert auf Selektion
var selectedId = hub.GetChannel<string?>("UI/Selection/NodeId").Value;
if (selectedId != null)
    DrawComponentEditors(graph.FindNode(selectedId));

// Timeline: Hebt den selektierten Clip hervor
var selectedId = hub.GetChannel<string?>("UI/Selection/NodeId").Value;
DrawClipBlock(clip, isHighlighted: clip.NodeId == selectedId);
```

Alle drei Panels sind vollstaendig entkoppelt — sie kennen nur den Channel-Pfad `"UI/Selection/NodeId"`, nicht einander.
