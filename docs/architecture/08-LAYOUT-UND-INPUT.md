# 08 — Layout und Input

## Design-Entscheidung: Layout via Components (Option C)

Layout ist **nicht** an ein Interface gebunden. Jeder Node der eine `LayoutConfig` Component hat, bekommt Layout. Ein `ITextureGenerator` mit `LayoutConfig` bekommt genauso Layout wie ein `ISceneElement`. Nodes ohne `LayoutConfig` werden vom `LayoutPass` uebersprungen -- zero Overhead fuer Compositor-Szenarien.

**Konsequenz:** Das System ist vollstaendig opt-in. Compositor-Clips ohne UI-Bezug zahlen nichts fuer Layout. Touch-Apps mit komplexen Layouts bekommen volle Flexbox-Power.

---

## Interface-Hierarchie

```csharp
// Interfaces = "Was bin ich" (Identitaet)
public interface ISceneClip { }                     // Marker
public interface ISceneLogic : ISceneClip { }       // Kein visueller Output
public interface ISceneElement : ISceneClip { }     // UI-Element (Hint, keine Pipeline-Konsequenz)
public interface ITextureGenerator : ISceneClip { } // () -> Texture
public interface ITextureEffect : ISceneClip { }    // Texture -> Texture
public interface ITextureCompositor : ISceneClip { }// Spread<Texture> -> Texture
public interface IAudioGenerator : ISceneClip { }   // () -> Audio
public interface IAudioEffect : ISceneClip { }      // Audio -> Audio

// Components = "Was kann ich" (Faehigkeiten, optional)
// LayoutConfig   -> Node bekommt Layout
// InputConfig    -> Node ist hit-testable
// ComputedBounds -> Node hat berechnete Bounds
```

`ISceneElement` ist ein **Hint** fuer den Node-Browser und Slot-Validierung, aber **kein Gate** fuer die Layout-Pipeline.

---

## LayoutConfig Component

```csharp
[SceneComponent]
public partial record LayoutConfig(
    LayoutMode Mode,
    // Size
    SizeValue Width,
    SizeValue Height,
    SizeValue MinWidth,
    SizeValue MaxWidth,
    SizeValue MinHeight,
    SizeValue MaxHeight,
    float AspectRatio,            // 0 = none, 16f/9f, 1f, etc.
    // Spacing
    Thickness Margin,
    Thickness Padding,
    // Alignment
    Alignment HAlign,             // Start, Center, End, Stretch
    Alignment VAlign,
    // Mode-specific
    int GridColumns,
    float Gap,
    StackDirection Direction,     // Vertical, Horizontal
    DockPosition Dock,            // Top, Bottom, Left, Right, Center, Fill
    // Scroll
    bool Scrollable,
    ScrollDirection ScrollDir);
```

### LayoutMode

```csharp
public enum LayoutMode : byte
{
    Absolute,     // Manuell: X, Y, Width, Height
    Stack,        // Kinder stapeln (Vertical/Horizontal) -> flex-direction
    Grid,         // Kinder in Raster -> flex-wrap + feste Breiten
    Dock,         // Kind dockt an einer Seite an -> absolute positioning
    Fill          // Fuellt den verfuegbaren Platz -> flex-grow: 1
}
```

### SizeValue

```csharp
public readonly record struct SizeValue(SizeUnit Unit, float Value)
{
    public static SizeValue Auto => new(SizeUnit.Auto, 0);
    public static SizeValue Px(float px) => new(SizeUnit.Pixels, px);
    public static SizeValue Pct(float pct) => new(SizeUnit.Percent, pct);
    public static SizeValue Fr(float fr) => new(SizeUnit.Fraction, fr);
}

public enum SizeUnit : byte { Auto, Pixels, Percent, Fraction }
```

---

## ComputedLayout (Transient)

Wird vom `LayoutPass` geschrieben und lebt nur einen Frame lang. Clips lesen diese Component via `ISceneContext`.

```csharp
[SceneComponent(Transient = true, FlatStorage = true)]
public partial record ComputedLayout(
    RectangleF Bounds,           // Position + Groesse im Parent-Koordinatensystem
    RectangleF GlobalBounds,     // Absolute Position auf dem Screen
    Vector2 ContentOffset,       // Scroll-Offset
    Vector2 ContentSize);        // Tatsaechliche Content-Groesse (kann groesser als Bounds sein)
```

---

## Layout-Engine: Flexbox (Pure C#)

### Warum Flexbox?

| Kriterium | Begruendung |
|-----------|-------------|
| **Measure-Callback** | Kritisch fuer Text-Layout -- "Wenn du 300px breit bist, wie hoch wirst du?" |
| **Pure C#** | ~6000 Zeilen, kein P/Invoke, kein Native-DLL |
| **Geloestes Problem** | Basiert auf Facebook Yoga, tausendfach getestet (React Native, Litho) |
| **Direkt migriert** | Code lebt in unserem Projekt (kein Submodule, keine aktiven Updates upstream) |

### IMeasurable Interface

Clips die Content-Sizing brauchen (Text, dynamische Inhalte) implementieren `IMeasurable`:

```csharp
public interface IMeasurable
{
    SizeF Measure(float availableWidth, float availableHeight,
                  MeasureMode widthMode, MeasureMode heightMode);
}

public enum MeasureMode : byte
{
    Exactly,      // Exakt diese Groesse
    AtMost,       // Maximal diese Groesse (content-sized)
    Undefined     // Keine Einschraenkung
}
```

**Beispiel -- Text-Clip:**

```
Process "TextLabel" : ISceneElement, IMeasurable

  SizeF Measure(float availableWidth, float availableHeight,
                MeasureMode widthMode, MeasureMode heightMode)
  {
    return font.MeasureText(Text, availableWidth);
  }

  Update(
    out Texture Output,
    ISceneContext Context,
    float Activity,
    string Text = "Hello",
    Font Font = DefaultFont,
    Color TextColor = White)
  {
    var layout = Context.Self.GetComponent<ComputedLayout>();
    Output = RenderText(Text, Font, TextColor, layout.Bounds);
  }
```

### ILayoutEngine Interface (austauschbar)

```csharp
public interface ILayoutEngine
{
    void ComputeLayout(FlatSceneGraph flat, int rootIndex, RectangleF viewport);
}

public sealed class FlexLayoutEngine : ILayoutEngine { ... }  // Default: Flexbox
// Optional: YogaLayoutEngine, CustomLayoutEngine, ...
```

---

## LayoutPass -- Integration in die Pipeline

Der `LayoutPass` laeuft nach dem `ConstraintSolver`, vor `StreamRouting`. Er arbeitet Top-Down: der Parent bestimmt das Layout seiner Kinder.

```csharp
public static class LayoutPass
{
    public static void Execute(FlatSceneGraph flat, ILayoutEngine engine, RectangleF viewport)
    {
        // Finde Layout-Roots (Nodes mit LayoutConfig deren Parent KEIN LayoutConfig hat)
        for (int i = 0; i < flat.NodeCount; i++)
        {
            if (!flat.HasComponent<LayoutConfig>(i)) continue;

            int parent = flat.ParentIndex[i];
            if (parent >= 0 && flat.HasComponent<LayoutConfig>(parent)) continue;

            // Layout-Root gefunden -> Engine berechnet den gesamten Teilbaum
            engine.ComputeLayout(flat, i, viewport);
        }
    }
}
```

**Layout-Root-Erkennung:** Ein Node ist Layout-Root wenn er `LayoutConfig` hat, sein Parent aber nicht. Das erlaubt mehrere unabhaengige Layout-Teilbaeume in einer Szene.

---

## FlexLayoutEngine -- Synchronisierung

```csharp
public sealed class FlexLayoutEngine : ILayoutEngine
{
    private readonly Dictionary<string, FlexNode> _nodePool = new();

    public void ComputeLayout(FlatSceneGraph flat, int rootIndex, RectangleF viewport)
    {
        // 1. Flex-Tree synchronisieren (nur bei Strukturaenderung)
        SyncFlexTree(flat, rootIndex);

        // 2. LayoutConfig -> Flex-Properties mappen
        MapLayoutConfigs(flat, rootIndex);

        // 3. Measure-Callbacks setzen (fuer Text etc.)
        SetMeasureCallbacks(flat, rootIndex);

        // 4. Layout berechnen (ein Aufruf!)
        var root = _nodePool[flat.Handles[rootIndex].NodeId];
        Flex.CalculateLayout(root, viewport.Width, viewport.Height, Direction.LTR);

        // 5. Ergebnisse -> ComputedLayout (Transient Components)
        WriteResults(flat, rootIndex);
    }
}
```

### Mapping: LayoutConfig zu Flexbox

| LayoutConfig | Flexbox-Property |
|-------------|------------------|
| `Mode: Stack(Vertical)` | `flex-direction: column` |
| `Mode: Stack(Horizontal)` | `flex-direction: row` |
| `Mode: Fill` | `flex-grow: 1` |
| `Mode: Grid(3 Columns)` | `flex-wrap: wrap` + `width: 33%` pro Kind |
| `Mode: Absolute` | `position: absolute` + `left/top` |
| `Mode: Dock(Top)` | `position: absolute` + `top: 0` + `width: 100%` |
| `Width: Px(200)` | `width: 200` |
| `Width: Pct(50)` | `width: 50%` |
| `Width: Auto` | `width: auto` (content-sized) |
| `Gap: 8` | `margin` auf Children (Flexbox hat kein natives gap) |
| `HAlign: Center` | `align-items: center` / `justify-content: center` |

---

## Input-Routing und Hit-Testing

### InputHit (Transient Component)

Wird vom `InputRoutingPass` geschrieben. Jeder Node mit `ComputedLayout` bekommt potenziell einen `InputHit`.

```csharp
[SceneComponent(Transient = true, FlatStorage = true)]
public partial record InputHit(
    bool IsHit,              // Irgendein Touch/Maus in meinem Bereich
    bool IsDirectHit,        // Ich bin das tiefste getroffene Element
    bool IsChildHit,         // Ein Kind wurde getroffen
    string? HitChildId,      // Welches Kind
    Vector2 LocalPosition);  // Position relativ zu meinen Bounds
```

### InputRoutingPass

Laeuft **Bottom-Up**: Das tiefste Element wird zuerst getroffen. Parents erfahren ueber `IsChildHit` und `HitChildId`, welches Kind den Hit bekommen hat.

```csharp
public static class InputRoutingPass
{
    public static void Execute(FlatSceneGraph flat, Vector2 touchPosition)
    {
        string? directHitId = null;

        // Bottom-up: Blaetter zuerst
        for (int i = flat.NodeCount - 1; i >= 0; i--)
        {
            var layout = flat.GetTransientComponent<ComputedLayout>(i);
            if (layout == null) continue;

            bool containsPoint = layout.GlobalBounds.Contains(touchPosition);

            if (containsPoint && directHitId == null)
            {
                // Tiefstes Element gefunden
                directHitId = flat.Handles[i].NodeId;
                flat.SetTransientComponent(i, new InputHit(
                    IsHit: true,
                    IsDirectHit: true,
                    IsChildHit: false,
                    HitChildId: null,
                    LocalPosition: touchPosition - layout.GlobalBounds.Location));
            }
            else if (containsPoint)
            {
                // Parent eines getroffenen Elements
                flat.SetTransientComponent(i, new InputHit(
                    IsHit: true,
                    IsDirectHit: false,
                    IsChildHit: true,
                    HitChildId: directHitId,
                    LocalPosition: touchPosition - layout.GlobalBounds.Location));
            }
        }
    }
}
```

---

## LayoutConfig API-Design

Die `LayoutConfig` Component wird nicht direkt mit Flexbox-Properties konstruiert, sondern ueber **Factory-Methoden**, die gaengige Layout-Patterns auf Flexbox abbilden. Der User muss Flexbox nicht verstehen.

### Factory-Methoden

```csharp
public static LayoutConfig Stack(StackDirection direction = Vertical, float gap = 0, Alignment align = Stretch)
public static LayoutConfig Fill(Thickness margin = default)
public static LayoutConfig DockTop(SizeValue height, Thickness margin = default)
public static LayoutConfig DockBottom(SizeValue height, Thickness margin = default)
public static LayoutConfig DockLeft(SizeValue width, Thickness margin = default)
public static LayoutConfig DockRight(SizeValue width, Thickness margin = default)
public static LayoutConfig Grid(int columns, float gap = 0, float rowGap = 0)
public static LayoutConfig GridChild(int column = 0, int row = 0, int colSpan = 1, int rowSpan = 1)
public static LayoutConfig Centered(SizeValue width = default, SizeValue height = default)
public static LayoutConfig Absolute(float x, float y, SizeValue width = default, SizeValue height = default)
public static LayoutConfig Hidden()
public static LayoutConfig Custom(...)  // Direct Flex access
```

### SizeValue (aktualisiert)

```csharp
public readonly record struct SizeValue(float Value, SizeUnit Unit)
{
    public static readonly SizeValue Auto = new(0, SizeUnit.Auto);
    public static SizeValue Px(float pixels) => new(pixels, SizeUnit.Pixels);
    public static SizeValue Pct(float percent) => new(percent, SizeUnit.Percent);
    public static SizeValue Fr(float fraction) => new(fraction, SizeUnit.Fraction);
    public static implicit operator SizeValue(float pixels) => Px(pixels);
}

public enum SizeUnit : byte { Auto, Pixels, Percent, Fraction }
```

Der `implicit operator` erlaubt `DockTop(60)` statt `DockTop(SizeValue.Px(60))`.

### Mapping-Tabelle: Factory → Flexbox

| Factory | FlexDirection | FlexGrow | FlexShrink | FlexWrap | Width | Height | PositionType |
|---------|--------------|----------|------------|----------|-------|--------|-------------|
| Stack(V) | Column | 0 | 0 | NoWrap | Auto | Auto | Relative |
| Stack(H) | Row | 0 | 0 | NoWrap | Auto | Auto | Relative |
| Fill | - | 1 | 1 | - | 100% | 100% | Relative |
| DockTop | - | 0 | 0 | - | 100% | fixed | Relative |
| Grid | Row | 0 | 0 | Wrap | Auto | Auto | Relative |
| Centered | - | 0 | 0 | - | param | param | Relative |
| Absolute | - | 0 | 0 | - | param | param | **Absolute** |
| Hidden | - | - | - | - | - | - | Display.None |

### Dock-Pattern

Docking funktioniert ueber die **Reihenfolge der Kinder** in einem Parent-Stack, **nicht** ueber `position: absolute`. Ein `DockTop`-Kind bekommt `FlexGrow: 0` und eine feste Hoehe, ein `Fill`-Kind bekommt `FlexGrow: 1` und fuellt den Rest. Die Anordnung ergibt sich aus der Kind-Reihenfolge im Baum:

```
Stack(Vertical)
  DockTop(60)      -> FlexGrow: 0, Height: 60px
  Fill()           -> FlexGrow: 1 (fuellt den Rest)
  DockBottom(80)   -> FlexGrow: 0, Height: 80px
```

### Grid via FlexWrap

Grid wird ueber `FlexWrap.Wrap` realisiert. Der `FlexLayoutEngine` berechnet die Kind-Breite automatisch: `100% / columns - gap`. Beispiel: `Grid(3, gap: 8)` ergibt Kinder mit `width: calc(33.33% - 8px)`. Der User gibt nur Spaltenanzahl und Gap an.

### Gap-Simulation

Flexbox hat kein natives `gap`-Property. Der `FlexLayoutEngine` simuliert Gap durch **halben Gap als Margin** auf jedes Kind (`margin-left: gap/2, margin-right: gap/2`). Am Rand wird negativer Margin auf dem Container verwendet, damit das erste und letzte Kind buendig abschliessen.

### SizeUnit.Fraction

Flexbox kennt kein `fr`-Unit. Der `FlexLayoutEngine` rechnet Fraktionen in Prozent um basierend auf der Summe aller Geschwister-Fraktionen. Beispiel: Drei Kinder mit `Fr(1)`, `Fr(2)`, `Fr(1)` ergeben 25%, 50%, 25%.

### ScrollConfig Component

```csharp
[SceneComponent]
public partial record ScrollConfig(
    ScrollDirection Direction,
    bool ShowScrollbar,
    float ScrollPosition,
    float ScrollVelocity,
    bool SnapToChildren);

[SceneComponent(Transient = true)]
public partial record ComputedScroll(
    Vector2 ContentSize,
    Vector2 ViewportSize,
    Vector2 ScrollOffset,
    float ScrollProgress);
```

`ScrollConfig` ist eine eigene Component (kein Feld in `LayoutConfig`), weil nicht jeder Layout-Node scrollbar ist. `ComputedScroll` wird vom `LayoutPass` geschrieben wenn `Overflow.Scroll` aktiv ist — die eigentliche Offset-Berechnung erfolgt extern (Input-getrieben).

### Flexbox API-Verifizierung

Uebersicht welche Features nativ in der Flexbox-Engine verfuegbar sind und welche der `FlexLayoutEngine` zusaetzlich implementiert:

| Feature | Flexbox-API | Status |
|---------|------------|--------|
| FlexDirection | StyleSetFlexDirection() | ✅ Nativ |
| FlexWrap | StyleSetFlexWrap() | ✅ Nativ |
| FlexGrow/Shrink | StyleSetFlexGrow/Shrink() | ✅ Nativ |
| Width/Height (Px) | StyleSetWidth/Height(float) | ✅ Nativ (Unit.Point) |
| Width/Height (%) | StyleSetWidthPercent(float) | ✅ Nativ |
| Width/Height (Auto) | StyleSetWidthAuto() | ✅ Nativ |
| AspectRatio | StyleSetAspectRatio(float) | ✅ Nativ |
| Position Absolute | StyleSetPositionType() | ✅ Nativ |
| Margin/Padding | StyleSetMargin/Padding(Edge, float) | ✅ Nativ |
| Overflow | StyleSetOverflow(Overflow) | ✅ Nativ |
| MeasureFunc | node.SetMeasureFunction() | ✅ Nativ |
| Display.None | Display.None | ✅ Nativ |
| Gap/RowGap | - | ⚠️ Via Margin simuliert |
| SizeUnit.Fraction | - | ⚠️ Via Percent umgerechnet |
| Scroll-Verhalten | Overflow.Scroll (nur Layout) | ⚠️ Offset-Berechnung extern |
| MeasureMode | Flexbox: Undefined=0, Exactly=1, AtMost=2 | ⚠️ Flexbox-Enum direkt verwenden |

### Beispiel: Touch-App Layout (mit Factories)

```
[Page "TouchApp"]
  LayoutConfig: Stack(Vertical)

  [Header "TopBar"]
    LayoutConfig: DockTop(60)

  [Content "Main"]
    LayoutConfig: Fill()

    [Grid "CardGrid"]
      LayoutConfig: Grid(columns: 3, gap: 8)

      [Card "card_001"]
        LayoutConfig: GridChild(column: 0, row: 0)

      [Card "card_002"]
        LayoutConfig: GridChild(column: 1, row: 0)

  [Footer "NavBar"]
    LayoutConfig: DockBottom(80)
```

---

## Zusammenspiel Layout, Input und Clip-Evaluation

Die neuen Passes fuegen sich zwischen `ConstraintSolver` und `StreamRouting` in die bestehende Frame-Pipeline ein:

```
Frame-Pipeline:
  ...
  Phase 8:   ConstraintSolver
  Phase 8.5: LayoutPass        -> ComputedLayout (Transient)
  Phase 8.6: InputRoutingPass  -> InputHit (Transient)
  Phase 9:   StreamRouting
  Phase 10:  ClipEvaluation    -> Clips lesen ComputedLayout + InputHit via ISceneContext
  ...
```

Clips greifen auf die Layout- und Input-Daten ueber `ISceneContext` zu:

```csharp
var layout = Context.Self.GetComponent<ComputedLayout>();
var hit = Context.Self.GetComponent<InputHit>();
```

---

## Beispiel: Touch-App Layout

```
[Page "Home"]
  LayoutConfig: Stack(Vertical)

  [Header "Logo"]
    LayoutConfig: Height(Px(60))

  [Grid "Exhibits"]
    LayoutConfig: Fill, Grid(3 Columns, Gap: 8)

    [Card "exhibit_001"]
      LayoutConfig: AspectRatio(4/3)

    [Card "exhibit_002"]
      LayoutConfig: AspectRatio(4/3)

  [NavBar "Footer"]
    LayoutConfig: Height(Px(80))
```

**Ergebnis nach LayoutPass (bei 800x600 Viewport):**

```
Header:   (0, 0, 800, 60)
Grid:     (0, 60, 800, 460)
  Card 1: (0, 60, 261, 196)      <- 800/3 - gap, aspect 4:3
  Card 2: (269, 60, 261, 196)
NavBar:   (0, 520, 800, 80)
```

---

## Compositor-Szenario: Kein Layout

```
[Layer "PostFX"]
  [Clip "Bloom" -- BloomFX]         <- Kein LayoutConfig
  [Clip "Warp" -- ProjectionWarp]   <- Kein LayoutConfig
```

`LayoutPass` ueberspringt diese Nodes komplett. **Zero Overhead.** Clips rendern fullscreen wie gewohnt.
