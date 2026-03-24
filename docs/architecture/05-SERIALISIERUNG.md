# Serialisierung, Migration und Presets

Dieses Dokument beschreibt wie Shows persistent gespeichert werden, wie VL-Patch-Änderungen zur Laufzeit migriert werden und wie das Preset-System funktioniert.

---

## Show-Datei (Verzeichnis-basiert)

Eine Show wird als Verzeichnis gespeichert (nicht als einzelne Datei). Das ermöglicht Git-Diffbarkeit, inkrementelles Speichern und modulare Organisation:

```
show/
├── show.json           ← Manifest + Settings + PatchRegistry
├── graph.json          ← Szenegraph (JSON für Git-Diffbarkeit)
├── templates/          ← Sub-Graph Templates (JSON)
│   ├── particle.json
│   └── bg_layer.json
├── recordings/         ← Aufnahmen (binär)
│   ├── rec_001.vlrc
│   └── rec_002.vlrc
├── presets/            ← Presets (JSON)
│   ├── clip_bloom_warm.json
│   └── layer_bg_night.json
└── history/            ← Optional: Edit-History für Undo über Sessions
```

### Show-Manifest

Das Manifest (`show.json`) enthält Metadaten, globale Settings und die PatchRegistry:

```csharp
public record ShowFile(
    string Name, string Version,
    DateTime CreatedAt, DateTime ModifiedAt,
    SceneGraph Graph,
    ImmutableDictionary<string, RecordingAsset> Recordings,
    ShowSettings Settings,
    PatchRegistry PatchRegistry);

public record ShowSettings(
    float DefaultBPM, int TargetFPS, Vector2 OutputResolution,
    ImmutableArray<OutputMapping> Outputs,
    ImmutableDictionary<string, string> OscMappings,
    ImmutableDictionary<string, string> MidiMappings);
```

---

## PatchRegistry und stabile Referenzen

Die PatchRegistry speichert den Parameter-Schema-Snapshot jedes referenzierten VL-Patches zum Zeitpunkt des Saves. Sie ist der Schlüssel für Live-Patching und Schema-Migration.

### PatchRegistryEntry

```csharp
public record PatchRegistry(
    ImmutableDictionary<string, PatchRegistryEntry> Entries);

public record PatchRegistryEntry(
    string PatchId,
    string AssemblyQualifiedName,
    string DocumentId,             // VL-Dokument ID
    string PersistentId,           // VL-Node-Persistent-ID (survives renames!)
    int SchemaVersion,
    ImmutableArray<ParameterSchema> Parameters);

public record ParameterSchema(
    string Name,
    string PersistentId,           // Pin-PersistentId (survives pin renames!)
    string TypeName,               // "System.Single", "Stride.Core.Mathematics.Color4"
    object? DefaultValue);
```

### Stabile Referenzen über PersistentId

In VLs generiertem Code hat jedes Element eine `PersistentId`:

```csharp
[Element(DocumentId = "EKYvICxF27COKXc5f2ctJU", PersistentId = "SEeVW6E6jOkLZwCgUlRJjT")]
```

Die `PersistentId` überlebt Umbenennungen in VL. Wenn der User z.B. "BloomEffect" zu "GlowEffect" umbenennt, bleibt die PersistentId gleich. Das ist der stabile Anker für alle Referenzen:

```csharp
// Clip lookup: PersistentId instead of name
var clipType = appHost.TypeRegistry.RegisteredTypes
    .FirstOrDefault(t =>
        t.GetAttribute<ElementAttribute>()?.PersistentId == clipRef.PersistentId);
```

### ClipReference — 4-Tupel für stabilen Clip-Lookup

```csharp
[SceneComponent]
public partial record ClipReference(
    string PatchId,                // Display name
    int PatchVersion,              // Last known schema version
    string DocumentId,             // VL document ID
    string PersistentId);          // VL PersistentId (stable lookup key)
```

### ClipParameters — gespeicherte Werte mit State-Tracking

```csharp
[SceneComponent]
public partial record ClipParameters(
    ImmutableDictionary<string, StoredParameter> Values,
    int SchemaVersion);

public record StoredParameter(
    string Name,                   // For display
    string PersistentId,           // Stable key, survives renames
    object? Value,
    string TypeName,
    ParameterState State);

public enum ParameterState : byte
{
    Active,          // Parameter exists in patch, type matches
    Orphaned,        // Parameter no longer exists in patch
    TypeMismatch,    // Parameter exists but different type
    Default,         // No stored value, patch default is used
    Migrated         // Was automatically migrated (e.g. rename detected)
}
```

---

## Graph-JSON-Struktur

Der Szenegraph wird als JSON serialisiert. Die Struktur bildet den immutablen Baum mit Components und Edges ab:

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

## Live-Patching und Schema-Migration

### Problem

Der VL-User ändert seinen Clip-Patch während die Show läuft. Pins werden hinzugefügt, entfernt, umbenannt oder Typen geändert. Die gespeicherten `ClipParameters`, Timeline-Tracks, Presets und Constraints referenzieren das alte Schema.

### Die vier Fälle

| Fall | Betroffene Daten | Strategie |
|------|------------------|-----------|
| **Pin hinzugefügt** | ClipParameters, Presets | Patch-Default verwenden |
| **Pin entfernt** | ClipParameters, Timeline, Constraints, Presets | `State=Orphaned`, Daten bleiben erhalten |
| **Pin umbenannt** | Alles | PersistentId als stabiler Key; Rename-Heuristik als Fallback |
| **Pin-Typ geändert** | ClipParameters, Timeline, Presets | Auto-Konvertierung versuchen, sonst `TypeMismatch` + Default |

### SchemaDiff

Der `SchemaDiff` vergleicht altes und neues Schema. Primärer Matching-Mechanismus ist die `PersistentId`:

```csharp
public record SchemaDiff(
    ImmutableArray<ParameterAdded> Added,
    ImmutableArray<ParameterRemoved> Removed,
    ImmutableArray<ParameterRenamed> PossibleRenames,
    ImmutableArray<ParameterTypeChanged> TypeChanges);

public static SchemaDiff Compare(
    IReadOnlyList<ParameterSchema> oldSchema,
    IReadOnlyList<ParameterSchema> newSchema)
{
    // Primary: match by PersistentId (stable, survives renames)
    var matched = oldSchema.Join(newSchema,
        o => o.PersistentId, n => n.PersistentId, (o, n) => (o, n));

    // Detect renames: PersistentId matches but name changed
    var renames = matched
        .Where(pair => pair.o.Name != pair.n.Name)
        .Select(pair => new ParameterRenamed(
            pair.o.Name, pair.n.Name, pair.o.PersistentId));

    // Type changes: PersistentId matches but type changed
    var typeChanges = matched
        .Where(pair => pair.o.TypeName != pair.n.TypeName);

    // Removed: old PersistentId no longer in new schema
    var removed = oldSchema.Where(o =>
        !newSchema.Any(n => n.PersistentId == o.PersistentId));

    // Added: new PersistentId not in old schema
    var added = newSchema.Where(n =>
        !oldSchema.Any(o => o.PersistentId == n.PersistentId));

    return new SchemaDiff(added, removed, renames, typeChanges);
}
```

### Was passiert bei jedem Fall

**Pin hinzugefügt:**

- `ClipParameters`: Kein Eintrag vorhanden — Patch-Default wird verwendet
- `Timeline`: Kein Track — keine Animation (Parameter ist statisch)
- `Presets`: Kein Eintrag — Patch-Default
- `Constraints`: Keine Links — nicht betroffen
- Ergebnis: Alles funktioniert automatisch mit Defaults.

**Pin entfernt:**

- `ClipParameters`: Eintrag wird `State=Orphaned`, Wert bleibt erhalten
- `Timeline`: Track wird `State=Orphaned`, bleibt erhalten (wird nicht gelöscht!)
- `Presets`: Eintrag bleibt, wird bei Apply übersprungen
- `Constraints`: Links werden `State=Broken`, UI zeigt Warnung
- Ergebnis: **Kein Datenverlust.** Wenn der User den Pin zurückbringt, ist alles noch da.

**Pin umbenannt (über PersistentId erkannt):**

- PersistentId stimmt überein — Name-Feld wird automatisch aktualisiert
- Alle Referenzen bleiben intakt (sie nutzen PersistentId, nicht Name)
- Ergebnis: Transparent, kein User-Eingriff nötig.

**Pin umbenannt (PersistentId nicht verfügbar, z.B. beim Laden alter Shows):**

- Fallback-Heuristik: gleicher Typ + genau 1 removed + 1 added = wahrscheinlicher Rename
- System schlägt dem User eine Migration vor: "Intensity wurde zu Strength umbenannt. Migrieren?"
- Bei Ja: Alle Referenzen werden aktualisiert, `State=Migrated`
- Bei Nein: Wie remove + add behandeln

**Pin-Typ geändert:**

Automatische Konvertierung wird versucht:
- `float` → `int`: Runden
- `int` → `float`: Casten
- `Color4` → `Vector4`: Direkt (gleiche Struktur)
- `bool` → `float`: 0.0/1.0

Wenn Konvertierung möglich: Value konvertieren, `State=Migrated`. Wenn nicht möglich: `State=TypeMismatch`, Patch-Default verwenden, alter Wert bleibt erhalten.

### Kein-Datenverlust-Garantie

Orphaned- und TypeMismatch-Werte werden **nie gelöscht**, nur markiert. Der User kann in der UI sehen welche Parameter verwaist sind und sie manuell zuweisen oder löschen.

---

## Hotswap — VL-Patch-Reload zur Laufzeit

### VLs Hotswap-Mechanismus

VL bietet einen eingebauten Hotswap-Mechanismus über `IHotSwappableEntryPoint`:

```csharp
// VL.Core — internal interface, available via AppHost.Services
internal interface IHotSwappableEntryPoint
{
    IObservable<Unit> OnSwap { get; }             // Fires before next frame on recompile
    object Swap(object obj, Type compiletimeType); // Swap instance to new type
    Type SwapType(Type clrTypeOfValues);           // Type mapping old → new
}
```

Zentrale Methoden:

- **OnSwap**: Observable das feuert wenn der VL-Compiler einen Patch recompiled hat
- **Swap(obj, type)**: Tauscht eine Instanz von altem Typ auf den neuen Typ, migriert internen State
- **SwapType(type)**: Mappt alte CLR-Typen auf neue (auch rekursiv in Generics: `List<Foo_v1>` → `List<Foo_v2>`)

### ISwappableGenericType

Für Typen mit komplexem State die selbst steuern müssen wie ihr Zustand migriert wird:

```csharp
// VL.Core — for generic containers with state
public interface ISwappableGenericType
{
    object Swap(Type newType, Swapper swapObject);
}

public delegate object Swapper(object value, Type compiletimeType);
```

### Integration in den ClipEvaluator

Der `ClipEvaluator` subscribt auf `OnSwap` und führt bei jeder Recompilation die vollständige Migration durch:

```csharp
public sealed class ClipEvaluator : IDisposable
{
    private readonly IDisposable _swapSubscription;

    public ClipEvaluator(AppHost appHost)
    {
        _appHost = appHost;

        // Subscribe to VL's hotswap event
        var entryPoint = appHost.Services.GetService<IHotSwappableEntryPoint>();
        if (entryPoint != null)
        {
            _swapSubscription = entryPoint.OnSwap
                .Subscribe(_ => OnPatchRecompiled(entryPoint));
        }
    }

    private void OnPatchRecompiled(IHotSwappableEntryPoint entryPoint)
    {
        var migrationEdits = ImmutableArray.CreateBuilder<SceneEdit>();

        foreach (var (nodeId, clipInstance) in _activeClips)
        {
            // 1. Hotswap instance (VL migrates internal state)
            var oldInstance = clipInstance.Node;
            var oldType = oldInstance.GetType();
            var newType = entryPoint.SwapType(oldType);

            if (newType != oldType)
            {
                var newInstance = entryPoint.Swap(oldInstance, oldType);
                clipInstance.Node = (IVLNode)newInstance;

                // 2. Rebuild pin bindings (pins may have changed)
                clipInstance.RebuildPinBindings();

                // 3. Compute schema diff
                var oldSchema = clipInstance.CachedParameterSchema;
                var newSchema = ExtractParameterSchema(newInstance);
                var diff = SchemaDiff.Compare(oldSchema, newSchema);

                // 4. Generate migration edits
                if (!diff.IsEmpty)
                {
                    var edits = GenerateMigrationEdits(nodeId, diff);
                    migrationEdits.AddRange(edits);
                    clipInstance.CachedParameterSchema = newSchema;
                }
            }
        }

        // 5. Migration edits flow as normal edits into the next frame
        _pendingMigrationEdits = migrationEdits.ToImmutable();
    }
}
```

### Ablauf bei Patch-Änderung

```
VL-User ändert "BloomEffect"-Patch (z.B. Pin umbenennen)
    │
    ▼
VL-Compiler recompiled → generiert neue BloomEffect_C Klasse
    │
    ▼
IHotSwappableEntryPoint.OnSwap feuert
    │
    ▼
ClipEvaluator.OnPatchRecompiled():
    ├── 1. SwapType(BloomEffect_C_v1) → BloomEffect_C_v2
    ├── 2. Swap(instance, type) → neue Instanz mit migriertem State
    ├── 3. RebuildPinBindings() → neue Pins entdecken, alte verwerfen
    ├── 4. SchemaDiff.Compare() → Renames, TypeChanges erkennen
    └── 5. SceneEdits erzeugen → ClipParameters aktualisieren
    │
    ▼
Nächster Frame: Migration-Edits werden appliziert
    │
    ▼
Graph hat aktualisiertes Schema, Clip läuft mit neuer Version
→ Kein Frameabriss, kein Datenverlust
```

### State-Migration: Aufgabenteilung

**Was VL automatisch handhabt** (interner Clip-State via `Swap()`):

- Felder die in beiden Versionen existieren: Wert wird übernommen
- Neue Felder: Default-Wert
- Entfernte Felder: Werden verworfen
- Typ-geänderte Felder: VL versucht Konvertierung, sonst Default

**Was wir handhaben** (externe Ebene via SchemaDiff + SceneEdit):

- `ClipParameters` — gespeicherte Pin-Werte
- Timeline-Tracks — Animationsdaten pro Pin
- Constraints — Parameter-Links zwischen Clips
- Presets — gespeicherte Parametersätze

### Bekannte Einschränkungen

- **Zombie-Subscriptions**: Langlebige Subscriptions (Observables, Events) können nach Hotswap als Zombies überleben. Lösung: `OnDeactivation()` + `OnActivation()` bei Type-Change aufrufen.
- **Generische Container**: Generische Container mit alten Typ-Referenzen müssen rekursiv geswappt werden. `SwapType` handhabt das automatisch.
- **GPU-Resources**: Textures und Buffers werden nicht automatisch migriert. Der Clip muss in `OnActivation()` neu allozieren.

---

## Preset-System

### PresetScope

Presets können auf verschiedenen Ebenen erfasst und angewendet werden:

```csharp
public enum PresetScope : byte
{
    ClipParameters,    // Only parameters of a single clip
    ClipFull,          // Full clip (patch + parameters + constraints)
    Subtree,           // Entire subtree (layer with all clips)
    Scene,             // Complete scene
    StateMachine,      // FSM definition only
    Timeline,          // Timeline data only
    ConstraintSet,     // Constraints only
    InputMapping       // OSC/MIDI mappings
}
```

### Parameter-Presets

Speichern und Laden von Clip-Parametern. Pro Patch-Typ gefiltert — ein Preset für "BloomEffect" passt nicht auf "ParticleEmitter". Parameter werden über ihre `PersistentId` zugeordnet, sodass Renames kein Problem darstellen.

### Subtree-Presets

Ein ganzer Teilbaum als wiederverwendbares Template. Enthält:
- Alle Nodes und ihre Components
- Interne Edges (DataFlow, Constraints, etc.)
- Markierung offener Verbindungen die beim Einfügen gemapped werden müssen

### Preset-Morphing

Überblendung zwischen zwei Parameter-Presets über einen Morph-Slider (0..1). Die Interpolation erfolgt pro Parameter basierend auf Typ:

- `float` → Lerp
- `Vector2/3/4` → Lerp
- `Color4` → Lerp
- `bool` → Step (Schwelle bei 0.5)
- `int` → Gerundeter Lerp
- `string` → Step (kein sinnvolles Morphing möglich)

### Capture und Apply API

```csharp
// Capture current clip parameters as named preset
presetManager.CaptureClipParameters(graph, nodeId, "Warm");

// Apply a preset to a clip
presetManager.ApplyParameterPreset(graph, nodeId, preset);

// Morph between two presets (blend: 0.0 = presetA, 1.0 = presetB)
presetManager.MorphPresets(graph, nodeId, presetA, presetB, blend: 0.5f);
```
