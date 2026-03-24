# Projektstruktur und Tooling

## Source Generators

### Zweck

Eliminieren Boilerplate. Werden bei jedem Save vom Roslyn-Compiler ausgefuehrt. VL hot-reloaded die generierten Nodes.

### Attribut: [SceneComponent]

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class SceneComponentAttribute : Attribute
{
    public bool FlatStorage { get; set; } = false;
}
```

### Was wird generiert?

Aus `[SceneComponent] public partial record Transform3D(Matrix4x4 Matrix)` erzeugt der Generator:

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

5. **Pool-Allokation**: Automatisches Rent/Return der generierten Arrays via `ArrayPool<T>`

6. **ComponentRegistry**: Alle bekannten Typen, Name-to-Type Mapping

### Attribut: [SceneConstraint]

```csharp
[SceneConstraint(WritesTo = "Transform3D", DependsOn = new[] { "Transform3D" })]
public partial record LookAtConstraint([property: NodeReference] NodeHandle Target, Vector3 UpVector);
```

Generiert:
- Constraint-SoA-Arrays
- Handle-Resolution-Code
- Abhaengigkeits-Metadaten fuer topologische Sortierung

### Projekt-Setup

```xml
<!-- VL.SceneGraph.csproj -->
<ItemGroup>
  <ProjectReference Include="../VL.SceneGraph.Generators/VL.SceneGraph.Generators.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

---

## Projekt-Struktur

```
VL.SceneGraph/
├── src/
│   ├── VL.SceneGraph.Generators/        # Source Generator (Roslyn Analyzer)
│   │   ├── VL.SceneGraph.Generators.csproj
│   │   ├── SceneComponentGenerator.cs   # Generiert Accessors, SoA-Felder, Registry
│   │   ├── FlatStorageGenerator.cs      # Generiert Compile/Sync-Code fuer SoA
│   │   ├── ConstraintGenerator.cs       # Generiert Constraint-Arrays + Abhaengigkeiten
│   │   ├── RegistryGenerator.cs         # Generiert ComponentRegistry
│   │   └── NodeFactoryGenerator.cs      # Generiert Get/Set/Has/ForEach VL-Nodes
│   │
│   ├── VL.SceneGraph/                   # Hauptprojekt
│   │   ├── VL.SceneGraph.csproj
│   │   │
│   │   ├── Attributes/
│   │   │   ├── SceneComponentAttribute.cs   # [SceneComponent] Marker + FlatStorage Flag
│   │   │   ├── SceneConstraintAttribute.cs  # [SceneConstraint] mit WritesTo/DependsOn
│   │   │   └── NodeReferenceAttribute.cs    # Markiert Handle-Properties fuer Resolution
│   │   │
│   │   ├── Core/                            # Partial Klassen (Generator ergaenzt)
│   │   │   ├── SceneNode.cs                 # Immutabler Knoten mit Component-Dictionary
│   │   │   ├── SceneGraph.cs                # Wurzel mit globalem Knotenindex
│   │   │   ├── SceneEdit.cs                 # Edit-Command Records (Clip-generiert)
│   │   │   ├── SceneGraphEditing.cs         # ApplyEdits Extension Methods
│   │   │   ├── ISceneClip.cs                # Marker-Interface fuer VL-Clip-Patches
│   │   │   ├── ISceneContext.cs             # Read-only Model-Zugriff fuer Clips
│   │   │   ├── NodeHandle.cs                # Stabiler Verweis auf Nodes (ueberlebt Rebuilds)
│   │   │   ├── Edge.cs                      # Gerichtete Kante mit EdgeType
│   │   │   ├── IComponent.cs                # Basis-Interface fuer alle Components
│   │   │   ├── SlotType.cs                  # Typ-Enum (Group, Layer, Clip, Effect, etc.)
│   │   │   ├── SlotCompatibility.cs         # Regeln welcher SlotType wohin darf
│   │   │   └── HierarchyValidator.cs        # Validiert Eltern-Kind-Beziehungen
│   │   │
│   │   ├── Components/
│   │   │   ├── ClipComponents.cs            # ClipReference, ClipLifetime, ClipActivity, etc.
│   │   │   ├── TimeComponents.cs            # TimeContext, ParameterTimeline, CueList
│   │   │   ├── ControlComponents.cs         # ControlSource, LayerBlending, BypassFlag
│   │   │   ├── ConstraintComponents.cs      # LookAt, ParentConstraint, ParameterLink, etc.
│   │   │   ├── StateMachineComponents.cs    # FSM-Zustandsdefinitionen
│   │   │   ├── InputComponents.cs           # ExternalInput (MIDI, OSC, Tablet)
│   │   │   ├── RecordingComponents.cs       # Recordable, RecordingPlayback
│   │   │   ├── HierarchyComponents.cs       # PatchDescriptor, ChildComposition
│   │   │   └── StreamComponents.cs          # FeedbackLoop
│   │   │
│   │   ├── Flat/                            # Performance-Schicht (partial, Generator ergaenzt)
│   │   │   ├── FlatSceneGraph.cs            # SoA-Arrays, BFS-Order
│   │   │   ├── FlatConstraints.cs           # Topologisch sortierte Constraint-Arrays
│   │   │   ├── SceneCompiler.cs             # Baum -> flache Arrays
│   │   │   ├── ConstraintCompiler.cs        # Constraints -> sortierte Ausfuehrung
│   │   │   ├── HandleIndex.cs               # NodeHandle -> BFS-Index Mapping
│   │   │   ├── EdgeIndex.cs                 # Schneller Edge-Lookup
│   │   │   ├── StreamPool.cs                # ArrayPool-basierter Stream-Manager
│   │   │   └── Passes/                      # 14-Pass Frame-Pipeline
│   │   │       ├── TimePass.cs              # Pass 1: Zeitpropagation
│   │   │       ├── ExternalInputPass.cs     # Pass 2: MIDI/OSC/Tablet Inputs
│   │   │       ├── StateMachinePass.cs      # Pass 4: FSM-Transitionen
│   │   │       ├── ActivityPass.cs          # Pass 5: Clip-Aktivierung/Deaktivierung
│   │   │       ├── TimelinePass.cs          # Pass 6: Keyframe-Interpolation
│   │   │       ├── ConstraintSolver.cs      # Pass 7: Constraint-Aufloesung
│   │   │       ├── StreamRoutingPass.cs     # Pass 8: Datenfluss-Routing
│   │   │       ├── TransformPass.cs         # Pass 9: Hierarchische Transforms
│   │   │       ├── BoundsPass.cs            # Pass 10: Bounding-Box-Berechnung
│   │   │       ├── VisibilityPass.cs        # Pass 11: Frustum-Culling
│   │   │       └── RecordingPass.cs         # Pass 3: Recording-Capture/Playback
│   │   │
│   │   ├── Evaluation/
│   │   │   ├── ClipEvaluator.cs             # Clip-Lifecycle + Hotswap-Management
│   │   │   ├── SceneContext.cs              # ISceneContext Implementation
│   │   │   ├── PinBinder.cs                 # System/Parameter/Primary-Pin-Discovery
│   │   │   ├── TextureCompositor.cs         # Layer-Compositing fuer Texturen
│   │   │   └── PrimaryInputResolver.cs      # Primaer-Input-Erkennung
│   │   │
│   │   ├── Recording/
│   │   │   ├── TrackRecorder.cs             # Parameter-Track-Aufnahme
│   │   │   ├── NodeRecorder.cs              # Node-Level-Aufnahme
│   │   │   ├── DrawingRecorder.cs           # Tablet-Zeichnungs-Aufnahme
│   │   │   ├── RecordingSerializer.cs       # Binaer-Serialisierung
│   │   │   └── ActivityLogger.cs            # Activity-History fuer Timeline-Darstellung
│   │   │
│   │   ├── Runtime/
│   │   │   ├── CompositorRuntime.cs         # Frame-Pipeline + Double-Buffer
│   │   │   ├── DoubleBufferedScene.cs       # Atomarer Front/Back-Swap
│   │   │   └── MemoryBudget.cs              # Speicher-Budgetierung
│   │   │
│   │   ├── Presets/
│   │   │   ├── Preset.cs                    # Preset-Definition
│   │   │   ├── PresetManager.cs             # CRUD + Morph-Interpolation
│   │   │   └── PresetSerializer.cs          # JSON-Persistenz
│   │   │
│   │   ├── Undo/
│   │   │   ├── Edit.cs                      # Edit Record + EditCategory
│   │   │   ├── EditScope.cs                 # Panel-spezifischer Undo-Stack
│   │   │   ├── UndoManager.cs               # Scope-Routing + Global-Undo
│   │   │   └── EditHelper.cs                # BeginEdit/EndEdit + Merge-Window
│   │   │
│   │   ├── Serialization/
│   │   │   ├── ShowFile.cs                  # Show-Manifest + Settings
│   │   │   ├── ShowSerializer.cs            # Verzeichnis-basierte Persistenz
│   │   │   ├── GraphSerializer.cs           # JSON-Serialisierung des Szenegraphen
│   │   │   ├── PatchRegistry.cs             # Schema-Snapshots + PersistentId-Mapping
│   │   │   └── SchemaMigration.cs           # SchemaDiff + Auto-Migration
│   │   │
│   │   └── VL/                              # VL-spezifische API
│   │       ├── ComponentExtensions.cs       # Get/Set Extension Methods
│   │       ├── TraversalExtensions.cs       # ForEach, Rewrite, FindAll, Fold
│   │       ├── QueryExtensions.cs           # Query-Helpers
│   │       ├── SceneBuilderExtensions.cs    # Fluent Builder API
│   │       ├── RuntimeExtensions.cs         # Runtime-Convenience
│   │       ├── ComponentNodeFactory.cs      # Dynamische Get/Set/Has VL-Nodes
│   │       └── TraversalNodeFactory.cs      # Dynamische ForEach VL-Nodes
│   │
│   ├── VL.SceneGraph.Tests/
│   │
│   └── References/                          # NUR REFERENZ — NICHT AENDERN
│       └── VL.StandardLibs/                 # Git-Submodule (vvvv/VL.StandardLibs)
│
├── vl/
│   └── VL.SceneGraph.vl                     # VL-Dokument (Forwarding + Process Nodes)
│
├── help/
│   ├── Overview.vl
│   ├── HowTo Build a Scene.vl
│   ├── HowTo Use Components.vl
│   ├── HowTo Constraints.vl
│   └── HowTo Custom Components.vl
│
├── docs/
│   └── ARCHITEKTUR.md                       # Vollstaendige Architektur-Referenz
│
└── README.md
```

---

## NuGet-Paket

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

Das NuGet-Paket enthaelt sowohl die kompilierte C#-Assembly als auch die VL-Dokumente und Help-Patches, sodass `nuget install VL.SceneGraph` alles Noetige liefert.

---

## Offene Punkte

### Noch zu designen / implementieren

1. **StateMachine-Crossfading**: Wie genau ueberblendet man zwischen States wenn verschiedene Clips aktiv/inaktiv werden?
2. **Expression-Engine**: Wie werden mathematische Ausdruecke evaluiert? (Parsing, Kompilation, Performance)
3. **Stride-Integration**: Wie fliessen Render-Commands aus Clips in Strides Render-Pipeline?
4. **Netzwerk/Multi-Machine**: Synchronisierung fuer grosse Installationen mit mehreren Rechnern
5. **Keyframe-Kurven-Editor**: Bezier-Tangenten-Editing in ImGui
6. **Performance-Profiling**: Welche Passes wie lange dauern, wo sind Bottlenecks
7. **Error Recovery**: Was passiert wenn ein Clip-Patch crasht? Isolation?
8. **Spread\<T\> vs ImmutableArray\<T\>**: Wo ist die Grenze zwischen VL-nativen Typen und C#-Typen?

### Performance-Ziele

| Metrik | Ziel |
|--------|------|
| Framerate bei 1000+ Nodes | 60 fps |
| Allocations im Hot-Path (pro Frame) | Zero |
| Pipeline-Dauer (ohne Clip-Evaluation) | < 2 ms |
| Inkrementeller Compile (Property-Only) | < 0.5 ms |
| Full Rebuild bei 1000 Nodes | < 5 ms |

### Architektur-Risiken

- **ImmutableDictionary\<Type, IComponent\>** hat O(log n) Lookup — bei vielen Components pro Node relevant. Alternative: `FrozenDictionary` (einmalig erstellt, schneller Lookup) oder direkte Felder fuer haeufige Components
- **Object Boxing** bei `ClipParameters` (Dictionary\<string, object?\>) — fuer den Hot-Path die SoA-Arrays nutzen, nicht die Components
- **Source Generator Komplexitaet** — kann schwierig zu debuggen sein. Generierter Code muss klar und nachvollziehbar sein
- **ImGui-Performance** — bei vielen Clips in der Timeline kann das Zeichnen selbst zum Bottleneck werden. Culling/Virtualisierung noetig
