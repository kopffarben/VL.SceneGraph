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
