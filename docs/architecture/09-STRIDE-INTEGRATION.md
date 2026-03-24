# Stride-Integration und Rendering

Dieses Dokument beschreibt die Integration mit der Stride-Rendering-Engine: Wie Clips GPU-Ressourcen erzeugen, wie Texturen und 3D-Szenen composited werden, und wie die gesamte Render-Pipeline in den Frame-Zyklus eingebettet ist.

---

## ClipOutput — Was Clips liefern

### Grundprinzip

`ClipOutput` ist ein **Read-Only Value Type**, der GPU-Ressourcen **referenziert, aber nicht besitzt**. Der Clip selbst besitzt seine Ressourcen (erzeugt in `OnActivation()`, freigegeben in `OnDeactivation()`). `ClipOutput` lebt nur einen Frame lang im `StreamPool` und wird beim nächsten Frame überschrieben.

```csharp
/// <summary>
/// Lightweight reference to a clip's rendering output for one frame.
/// Does NOT own any GPU resources — the clip that produced it does.
/// Lives in the StreamPool for exactly one frame, then is overwritten.
/// </summary>
public readonly record struct ClipOutput
{
    public ClipOutputKind Kind { get; }
    public Texture? Texture { get; }
    public ISceneRenderer? Renderer { get; }
    public ImageEffect? ImageEffect { get; }
    public Entity? EntityRoot { get; }
    public Stride.Graphics.Buffer? ComputeBuffer { get; }
    public int Width { get; }
    public int Height { get; }

    private ClipOutput(
        ClipOutputKind kind,
        Texture? texture = null,
        ISceneRenderer? renderer = null,
        ImageEffect? imageEffect = null,
        Entity? entityRoot = null,
        Stride.Graphics.Buffer? computeBuffer = null,
        int width = 0, int height = 0)
    {
        Kind = kind;
        Texture = texture;
        Renderer = renderer;
        ImageEffect = imageEffect;
        EntityRoot = entityRoot;
        ComputeBuffer = computeBuffer;
        Width = width;
        Height = height;
    }

    // --- Factory Methods ---

    /// <summary>No output (inactive clip, logic-only clip).</summary>
    public static readonly ClipOutput None = new(ClipOutputKind.None);

    /// <summary>Clip rendered to a texture (most common case).</summary>
    public static ClipOutput FromTexture(Texture texture)
        => new(ClipOutputKind.Texture, texture: texture,
               width: texture.Width, height: texture.Height);

    /// <summary>
    /// Clip provides a scene renderer that will be drawn to a texture
    /// by the compositor when needed (deferred rendering).
    /// </summary>
    public static ClipOutput FromRenderer(ISceneRenderer renderer, int width, int height)
        => new(ClipOutputKind.Renderer, renderer: renderer,
               width: width, height: height);

    /// <summary>
    /// Clip provides a post-processing image effect (screen-space).
    /// Applied in-place on the current texture in a sequential chain.
    /// </summary>
    public static ClipOutput FromImageEffect(ImageEffect effect)
        => new(ClipOutputKind.ImageEffect, imageEffect: effect);

    /// <summary>
    /// Clip provides an entity tree for 3D scene composition.
    /// The entity root and all children are merged into the shared SceneInstance.
    /// </summary>
    public static ClipOutput FromEntities(Entity entityRoot)
        => new(ClipOutputKind.Entities, entityRoot: entityRoot);

    /// <summary>
    /// Clip provides a GPU compute buffer (particle data, simulation state).
    /// The next clip in the chain reads this buffer.
    /// </summary>
    public static ClipOutput FromComputeBuffer(Stride.Graphics.Buffer buffer)
        => new(ClipOutputKind.ComputeBuffer, computeBuffer: buffer);
}
```

### ClipOutputKind

```csharp
public enum ClipOutputKind : byte
{
    None,           // Kein Output (Logic-Clip, UI-Clip)
    Texture,        // Fertige Textur — direkt verwendbar
    Renderer,       // Scene-Renderer — muss in Textur gerendert werden
    ImageEffect,    // Post-Processing-Effekt — in-place auf aktuelle Textur
    Entities,       // Entity-Baum — wird in SceneInstance gemerged
    ComputeBuffer   // GPU-Buffer — für Compute-zu-Render Brücken
}
```

### Warum kein RenderFeature?

`RenderFeature` wurde bewusst **entfernt**. Stride registriert RenderFeatures statisch beim Start des `RenderSystem` — sie können nicht pro Frame dynamisch hinzugefügt oder entfernt werden. Das passt nicht zum Clip-Lifecycle, wo Clips jederzeit aktiviert und deaktiviert werden.

Stattdessen:
- **Custom Rendering** → `ClipOutput.FromRenderer()` — der Compositor rendert den `ISceneRenderer` in eine Pool-Textur
- **Custom Entities** → `ClipOutput.FromEntities()` — Entities werden in eine gemeinsame `SceneInstance` gemerged

### Ownership-Regeln

| Ressource | Besitzer | Lebensdauer | Freigabe |
|-----------|----------|-------------|----------|
| Clip-eigene Texturen, Shader, Buffers | Der Clip selbst | `OnActivation()` bis `OnDeactivation()` | Clip disposed in `OnDeactivation()` |
| Pool-Texturen (Composition-Zwischenergebnisse) | `TexturePool` | Ein Frame | `ReleaseAll()` am Frame-Ende |
| `ClipOutput` Struct | Niemand (Value Type) | Ein Frame im `StreamPool` | Wird überschrieben |
| Deaktivierte Clip-Ressourcen | Clip (verzögert) | Bis **nach** dem Buffer-Swap | Deferred Disposal Queue |

**Deferred Disposal**: Wenn ein Clip deaktiviert wird, darf er seine GPU-Ressourcen **nicht sofort** freigeben — der Render-Thread liest möglicherweise noch aus dem Front-Buffer. Die Freigabe erfolgt erst **nach** dem Buffer-Swap, wenn sichergestellt ist, dass kein Thread mehr auf die Ressourcen zugreift:

```csharp
// In CompositorRuntime.Frame():
// Pass 14: Swap
(_front, _back) = (_back, _front);
_streamPool.SwapBuffers();

// NACH dem Swap: Jetzt ist es sicher
_deferredDisposalQueue.FlushAll(); // disposed deaktivierte Clip-Ressourcen
```

---

## Clip-Familie — Alle Typen

### Übersichtstabelle

| Kategorie | Interface | SlotType (In → Out) | Beispiele |
|-----------|-----------|---------------------|-----------|
| **Texture Generator** | `ITextureGenerator` | `None → Texture` | VideoPlayer, LiveDrawing (Skia), ProceduralTexture, ImageLoader |
| **Audio Generator** | `IAudioGenerator` | `None → AudioBuffer` | AudioPlayer, Synthesizer, Microphone |
| **Scene3D Generator** | `IScene3DGenerator` | `None → Entities` | EnvironmentScene, CharacterRig, ParticleSystem |
| **Compute Generator** | `IComputeGenerator` | `None → ComputeBuffer` | FluidSim, ParticleSim, PhysicsWorld |
| **Texture Effect** | `ITextureEffect` | `Texture → Texture` | ColorCorrection, Blur, Distortion, Sharpen |
| **PostFX Effect** | `IPostFXEffect` | `Texture → Texture` | Bloom, DOF, Tonemap, Vignette, ChromaticAberration |
| **Mapping Effect** | `IMappingEffect` | `Texture → Texture` | GridWarp, SoftEdgeBlend, AlphaMask |
| **Texture Compositor** | `ITextureCompositor` | `Spread<Texture> → Texture` | TextureLayer, TextureMixer |
| **Scene3D Compositor** | `IScene3DCompositor` | `Spread<Entities> → Entities` | Scene3DLayer, SceneMerger |
| **Output Sink** | `IOutputSink` | `Any → None` | ProjectionOutput, NDIOutput, SpoutOutput, VideoRecorder |
| **Logic Clip** | `ILogicClip` | `None → None` | OSCController, MIDIMapper, SceneAutomation |
| **UI Element** | `IUIElement` | `None → None` | ControlPanel, ParameterWidget, PreviewMonitor |

### Interface-Hierarchie und PatchDescriptor

Die Interfaces dienen primär der **Kategorisierung im NodeBrowser** — sie bestimmen, unter welcher Rubrik ein Clip-Typ angezeigt wird. Das tatsächliche Routing (welche Slot-Typen fließen, welche Kinder erlaubt sind) wird durch den `PatchDescriptor` bestimmt:

```csharp
// Interface = NodeBrowser-Kategorie
public interface ITextureEffect : ISceneClip { }

// PatchDescriptor = tatsächliche Routing-Signatur
// Wird automatisch aus dem Interface abgeleitet, kann aber überschrieben werden
var blurDescriptor = new PatchDescriptor(
    PrimaryInput: SlotType.Texture,
    PrimaryOutput: SlotType.Texture,
    AdditionalInputs: ImmutableArray<SlotRequirement>.Empty,
    AdditionalOutputs: ImmutableArray<SlotOutput>.Empty,
    ChildPolicy: ChildPolicy.NoChildren,
    AcceptsChildrenOf: ImmutableArray<SlotType>.Empty);
```

Ein Clip kann ein Interface implementieren, aber durch seinen `PatchDescriptor` ein abweichendes Routing definieren. Das Interface bestimmt die UX (wo der Clip im Browser erscheint), der Descriptor bestimmt die Logik (was er akzeptiert und liefert).

---

## TextureCompositor

Der `TextureCompositor` ist das Herz der visuellen Pipeline. Er nimmt die `ClipOutput`s aller aktiven Kinder eines Layers und kombiniert sie zu einer einzelnen Textur.

### Vollständiger Code-Sketch

```csharp
/// <summary>
/// Composites child clip outputs into a single texture.
/// Supports parallel (layer blending) and sequential (effect chain) modes.
/// Zero allocations in steady state — all textures from pool.
/// </summary>
public sealed class TextureCompositor : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly TexturePool _texturePool;
    private readonly BlendEffect _blendEffect;
    private readonly SpriteBatch _spriteBatch;
    private CommandList _commandList;

    public TextureCompositor(GraphicsDevice device, TexturePool texturePool)
    {
        _device = device;
        _texturePool = texturePool;
        _blendEffect = new BlendEffect(device);
        _spriteBatch = new SpriteBatch(device);
        _commandList = device.CreateCommandList();
    }

    /// <summary>
    /// Composite all child outputs according to the parent's CompositionMode.
    /// Returns a pool texture with the composited result.
    /// </summary>
    public Texture? Composite(
        FlatSceneGraph flat,
        int parentIndex,
        StreamPool streamPool,
        CompositionMode mode,
        int outputWidth,
        int outputHeight,
        PixelFormat format = PixelFormat.R8G8B8A8_UNorm_SRgb)
    {
        // Gather active child outputs
        int start = flat.CompositionInputStart[parentIndex];
        int count = flat.CompositionInputCount[parentIndex];

        if (count == 0)
            return null;

        return mode switch
        {
            CompositionMode.Parallel => CompositeParallel(
                flat, streamPool, start, count, outputWidth, outputHeight, format),
            CompositionMode.Sequential => CompositeSequential(
                flat, streamPool, start, count, outputWidth, outputHeight, format),
            _ => null
        };
    }

    // ─── Parallel Composition (Layer Blending) ──────────────────────

    /// <summary>
    /// Blends all child outputs as layers with individual BlendMode and Opacity.
    /// Bottom-to-top: first child = bottom layer, last child = top layer.
    /// </summary>
    private Texture CompositeParallel(
        FlatSceneGraph flat,
        StreamPool streamPool,
        int inputStart,
        int inputCount,
        int width, int height,
        PixelFormat format)
    {
        var result = _texturePool.Acquire(width, height, format);
        _commandList.Clear(result, Color4.Transparent);

        for (int i = 0; i < inputCount; i++)
        {
            var slot = flat.CompositionInputSlots[inputStart + i];
            var clipOutput = streamPool.Read<ClipOutput>(slot);
            if (clipOutput is null) continue;

            float weight = flat.CompositionInputWeights[inputStart + i];
            var blendMode = flat.CompositionInputBlendModes[inputStart + i];

            // Activity moduliert die Opacity — Activity=0 heißt unsichtbar
            if (weight <= 0f) continue;

            // Resolve to texture if needed
            var texture = ResolveToTexture(clipOutput.Value, width, height, format);
            if (texture is null) continue;

            // Blend onto result
            _blendEffect.SetInput(0, result);   // destination
            _blendEffect.SetInput(1, texture);  // source
            _blendEffect.BlendMode = blendMode;
            _blendEffect.Opacity = weight;
            _blendEffect.Draw(_commandList, result);
        }

        return result;
    }

    // ─── Sequential Composition (Effect Chain / Ping-Pong) ──────────

    /// <summary>
    /// Chains outputs sequentially: output of clip N becomes input of clip N+1.
    /// Uses ping-pong rendering with 2 pool textures to avoid copies.
    /// </summary>
    private Texture CompositeSequential(
        FlatSceneGraph flat,
        StreamPool streamPool,
        int inputStart,
        int inputCount,
        int width, int height,
        PixelFormat format)
    {
        // Ping-pong: two textures, alternate read/write
        var pingTexture = _texturePool.Acquire(width, height, format);
        var pongTexture = _texturePool.Acquire(width, height, format);
        bool pingIsCurrent = true;

        // First input initializes the chain
        var firstSlot = flat.CompositionInputSlots[inputStart];
        var firstOutput = streamPool.Read<ClipOutput>(firstSlot);

        if (firstOutput is not null)
        {
            var firstTexture = ResolveToTexture(firstOutput.Value, width, height, format);
            if (firstTexture is not null)
                _commandList.Copy(firstTexture, pingTexture);
        }

        // Subsequent clips process the chain
        for (int i = 1; i < inputCount; i++)
        {
            var slot = flat.CompositionInputSlots[inputStart + i];
            var clipOutput = streamPool.Read<ClipOutput>(slot);
            if (clipOutput is null) continue;

            float weight = flat.CompositionInputWeights[inputStart + i];
            if (weight <= 0f) continue;

            var current = pingIsCurrent ? pingTexture : pongTexture;
            var target = pingIsCurrent ? pongTexture : pingTexture;

            switch (clipOutput.Value.Kind)
            {
                case ClipOutputKind.ImageEffect:
                    // Apply image effect in-place (reads current, writes target)
                    var effect = clipOutput.Value.ImageEffect!;
                    effect.SetInput(0, current);
                    // Scale effect intensity by activity
                    if (effect is IIntensityEffect intensityEffect)
                        intensityEffect.Intensity = weight;
                    effect.Draw(_commandList, target);
                    pingIsCurrent = !pingIsCurrent;
                    break;

                case ClipOutputKind.Texture:
                    // Blend texture onto current
                    var blendMode = flat.CompositionInputBlendModes[inputStart + i];
                    _blendEffect.SetInput(0, current);
                    _blendEffect.SetInput(1, clipOutput.Value.Texture!);
                    _blendEffect.BlendMode = blendMode;
                    _blendEffect.Opacity = weight;
                    _blendEffect.Draw(_commandList, target);
                    pingIsCurrent = !pingIsCurrent;
                    break;

                case ClipOutputKind.Renderer:
                    // Render to temp texture, then blend
                    var rendered = RenderToTexture(
                        clipOutput.Value.Renderer!, width, height, format);
                    _blendEffect.SetInput(0, current);
                    _blendEffect.SetInput(1, rendered);
                    _blendEffect.BlendMode = BlendMode.Normal;
                    _blendEffect.Opacity = weight;
                    _blendEffect.Draw(_commandList, target);
                    pingIsCurrent = !pingIsCurrent;
                    break;
            }
        }

        // Release the unused ping/pong texture
        var unused = pingIsCurrent ? pongTexture : pingTexture;
        _texturePool.Release(unused);

        return pingIsCurrent ? pingTexture : pongTexture;
    }

    // ─── Resolution Helpers ─────────────────────────────────────────

    /// <summary>
    /// Converts any ClipOutputKind to a texture for blending.
    /// </summary>
    private Texture? ResolveToTexture(
        ClipOutput output, int width, int height, PixelFormat format)
    {
        return output.Kind switch
        {
            ClipOutputKind.Texture => output.Texture,
            ClipOutputKind.Renderer => RenderToTexture(
                output.Renderer!, width, height, format),
            ClipOutputKind.ImageEffect => ApplyEffectToBlank(
                output.ImageEffect!, width, height, format),
            _ => null
        };
    }

    /// <summary>
    /// Renders an ISceneRenderer into a pool texture.
    /// </summary>
    private Texture RenderToTexture(
        ISceneRenderer renderer, int width, int height, PixelFormat format)
    {
        var target = _texturePool.Acquire(width, height, format);
        _commandList.Clear(target, Color4.Transparent);
        _commandList.SetRenderTarget(null, target);
        renderer.Draw(_commandList);
        return target;
    }

    /// <summary>
    /// Applies an ImageEffect to a blank texture (for standalone use).
    /// </summary>
    private Texture ApplyEffectToBlank(
        ImageEffect effect, int width, int height, PixelFormat format)
    {
        var blank = _texturePool.Acquire(width, height, format);
        _commandList.Clear(blank, Color4.Transparent);
        var target = _texturePool.Acquire(width, height, format);
        effect.SetInput(0, blank);
        effect.Draw(_commandList, target);
        _texturePool.Release(blank);
        return target;
    }

    public void Dispose()
    {
        _blendEffect.Dispose();
        _spriteBatch.Dispose();
        _commandList.Dispose();
    }
}
```

### BlendEffect Shader (SDSL)

```hlsl
// File: BlendEffect.sdsl
shader BlendEffect : ImageEffectShader
{
    // Inputs
    Texture2D Source;      // Layer-Textur (oben)
    Texture2D Destination; // Hintergrund (unten)

    float Opacity;         // 0..1, moduliert durch Activity
    int Mode;              // BlendMode enum as int

    // Blend-Funktionen
    float3 BlendNormal(float3 src, float3 dst)
    {
        return src;
    }

    float3 BlendAdd(float3 src, float3 dst)
    {
        return dst + src;
    }

    float3 BlendMultiply(float3 src, float3 dst)
    {
        return dst * src;
    }

    float3 BlendScreen(float3 src, float3 dst)
    {
        return 1.0 - (1.0 - dst) * (1.0 - src);
    }

    float3 BlendOverlay(float3 src, float3 dst)
    {
        float3 result;
        [unroll]
        for (int i = 0; i < 3; i++)
        {
            result[i] = dst[i] < 0.5
                ? 2.0 * dst[i] * src[i]
                : 1.0 - 2.0 * (1.0 - dst[i]) * (1.0 - src[i]);
        }
        return result;
    }

    float3 BlendSoftLight(float3 src, float3 dst)
    {
        float3 result;
        [unroll]
        for (int i = 0; i < 3; i++)
        {
            result[i] = src[i] < 0.5
                ? dst[i] - (1.0 - 2.0 * src[i]) * dst[i] * (1.0 - dst[i])
                : dst[i] + (2.0 * src[i] - 1.0) * (sqrt(dst[i]) - dst[i]);
        }
        return result;
    }

    float3 BlendDifference(float3 src, float3 dst)
    {
        return abs(dst - src);
    }

    // Dispatch
    float3 ApplyBlend(float3 src, float3 dst)
    {
        switch (Mode)
        {
            case 0: return BlendNormal(src, dst);
            case 1: return BlendAdd(src, dst);
            case 2: return BlendMultiply(src, dst);
            case 3: return BlendScreen(src, dst);
            case 4: return BlendOverlay(src, dst);
            case 5: return BlendSoftLight(src, dst);
            case 6: return BlendDifference(src, dst);
            default: return BlendNormal(src, dst);
        }
    }

    override stage float4 Shading()
    {
        float2 uv = streams.TexCoord;

        float4 srcColor = Source.Sample(LinearSampler, uv);
        float4 dstColor = Destination.Sample(LinearSampler, uv);

        // Pre-multiplied alpha workflow
        float3 blended = ApplyBlend(srcColor.rgb, dstColor.rgb);

        // Mix with opacity (Activity * LayerOpacity)
        float alpha = srcColor.a * Opacity;
        float3 result = lerp(dstColor.rgb, blended, alpha);
        float resultAlpha = dstColor.a + alpha * (1.0 - dstColor.a);

        return float4(result, resultAlpha);
    }
};
```

### BlendMode Enum

```csharp
public enum BlendMode : byte
{
    Normal = 0,
    Add = 1,
    Multiply = 2,
    Screen = 3,
    Overlay = 4,
    SoftLight = 5,
    Difference = 6
}
```

### Activity als Intensity-Multiplikator

Die `Activity` eines Clips (0..1) wirkt als **Intensity-Multiplikator** für die Komposition:

- **Parallel Mode**: `Activity` wird mit der `Opacity` des Layers multipliziert → `weight = activity * opacity`. Bei `Activity = 0` wird der Layer komplett übersprungen (kein Blend-Call, kein GPU-Overhead).
- **Sequential Mode**: `Activity` skaliert die Effekt-Intensität. Ein Bloom mit `Activity = 0.5` hat halbe Stärke. Bei `Activity = 0` wird der Effekt **bypassed** — die Textur wird unverändert durchgereicht.

```csharp
// In StreamRoutingPass:
float weight = flat.Activity[childIndex] * flat.LayerOpacity[childIndex];
flat.CompositionInputWeights[inputSlotIndex] = weight;

// Activity = 0 → Skip entirely (zero GPU cost)
// Activity = 0.5 → Half blend / half effect intensity
// Activity = 1.0 → Full blend / full effect
```

---

## Scene3DCompositor

Der `Scene3DCompositor` kombiniert Entity-Bäume aus mehreren Clips in einer gemeinsamen `SceneInstance` für 3D-Rendering.

### Grundprinzip

Jeder 3D-Clip besitzt einen eigenen Entity-Baum mit einem einzelnen Root-Entity. Der Compositor merged diese Bäume in eine gemeinsame Szene. Anders als beim Texture-Compositing ist hier **kein Layer-Blending** möglich — stattdessen werden Entities einfach zusammengeführt.

```csharp
/// <summary>
/// Merges entity trees from multiple clips into a shared SceneInstance.
/// Uses diffing instead of clear+re-add to minimize Entity lifecycle overhead.
/// </summary>
public sealed class Scene3DCompositor : IDisposable
{
    private readonly SceneInstance _sceneInstance;
    private readonly Entity _compositionRoot;

    // Track which entities belong to which clip (for diffing)
    private readonly Dictionary<string, Entity> _clipRootEntities = new();
    private readonly HashSet<string> _activeClipIds = new();
    private readonly HashSet<string> _previousActiveClipIds = new();

    public Scene3DCompositor(IServiceRegistry services)
    {
        var scene = new Scene();
        _compositionRoot = new Entity("CompositionRoot");
        scene.Entities.Add(_compositionRoot);
        _sceneInstance = new SceneInstance(services, scene);
    }

    /// <summary>
    /// Updates the shared scene with current clip outputs.
    /// Diffs against previous frame to avoid remove+re-add overhead.
    /// </summary>
    public SceneInstance Compose(
        FlatSceneGraph flat,
        int parentIndex,
        StreamPool streamPool,
        CameraComponent? overrideCamera = null)
    {
        _activeClipIds.Clear();

        int start = flat.CompositionInputStart[parentIndex];
        int count = flat.CompositionInputCount[parentIndex];

        // Phase 1: Collect active clip entities
        for (int i = 0; i < count; i++)
        {
            var slot = flat.CompositionInputSlots[start + i];
            var clipOutput = streamPool.Read<ClipOutput>(slot);
            if (clipOutput is null || clipOutput.Value.Kind != ClipOutputKind.Entities)
                continue;

            float weight = flat.CompositionInputWeights[start + i];
            if (weight <= 0f) continue;

            int childIndex = flat.FirstChildIndex[parentIndex] + i;
            string clipId = flat.Handles[childIndex].Id;
            var entityRoot = clipOutput.Value.EntityRoot!;
            _activeClipIds.Add(clipId);

            if (!_clipRootEntities.TryGetValue(clipId, out var existing))
            {
                // NEW: Clip was just activated — add entity tree
                _compositionRoot.AddChild(entityRoot);
                _clipRootEntities[clipId] = entityRoot;
            }
            else if (!ReferenceEquals(existing, entityRoot))
            {
                // CHANGED: Clip swapped its entity tree — replace
                _compositionRoot.RemoveChild(existing);
                _compositionRoot.AddChild(entityRoot);
                _clipRootEntities[clipId] = entityRoot;
            }
            // UNCHANGED: same entity reference — no work needed
        }

        // Phase 2: Remove entities from deactivated clips
        foreach (var clipId in _previousActiveClipIds)
        {
            if (!_activeClipIds.Contains(clipId)
                && _clipRootEntities.TryGetValue(clipId, out var staleEntity))
            {
                _compositionRoot.RemoveChild(staleEntity);
                _clipRootEntities.Remove(clipId);
            }
        }

        // Swap active sets for next frame's diff
        (_previousActiveClipIds, var temp) = (_activeClipIds, _previousActiveClipIds);
        _activeClipIds.Clear();

        // Phase 3: Camera selection
        if (overrideCamera is not null)
        {
            _sceneInstance.GetProcessor<CameraProcessor>()?.ActiveCamera = overrideCamera;
        }
        else
        {
            // Default: use the camera from the last active clip that provides one
            SelectCamera(flat, parentIndex, streamPool);
        }

        return _sceneInstance;
    }

    /// <summary>
    /// Camera from the last active clip wins (top of stack).
    /// </summary>
    private void SelectCamera(
        FlatSceneGraph flat, int parentIndex, StreamPool streamPool)
    {
        int start = flat.CompositionInputStart[parentIndex];
        int count = flat.CompositionInputCount[parentIndex];

        // Iterate backwards — last active clip's camera wins
        for (int i = count - 1; i >= 0; i--)
        {
            var slot = flat.CompositionInputSlots[start + i];
            var clipOutput = streamPool.Read<ClipOutput>(slot);
            if (clipOutput is null || clipOutput.Value.Kind != ClipOutputKind.Entities)
                continue;

            var entityRoot = clipOutput.Value.EntityRoot!;
            var camera = FindCamera(entityRoot);
            if (camera is not null)
            {
                _sceneInstance.GetProcessor<CameraProcessor>()?.ActiveCamera = camera;
                return;
            }
        }
    }

    private static CameraComponent? FindCamera(Entity root)
    {
        var cam = root.Get<CameraComponent>();
        if (cam is not null) return cam;

        foreach (var child in root.GetChildren())
        {
            cam = FindCamera(child);
            if (cam is not null) return cam;
        }
        return null;
    }

    public void Dispose()
    {
        _sceneInstance.Dispose();
    }
}
```

### Diffing statt Clear+Re-Add

Jeder Frame könnte einfach alle Entities entfernen und neu hinzufügen. Das ist aber teuer, weil Stride bei jedem `AddChild`/`RemoveChild` Entity-Prozessoren benachrichtigt und interne Listen aktualisiert. Stattdessen vergleicht der Compositor per `ReferenceEquals`:

| Situation | Aktion | Kosten |
|-----------|--------|--------|
| Clip war und bleibt aktiv, gleiche Entity-Referenz | Nichts tun | Zero |
| Clip war und bleibt aktiv, neue Entity-Referenz | Remove + Add | Selten |
| Clip wurde gerade aktiviert | Add | Einmalig |
| Clip wurde gerade deaktiviert | Remove | Einmalig |

Im Normalfall (laufende Show, keine Clip-Wechsel) kostet das Diffing **nichts**.

### Single Parent Rule

Stride-Entities haben einen internen `Parent`-Pointer. Ein Entity kann nur in **einer** Szene gleichzeitig leben. Deshalb besitzt jeder Clip seinen eigenen Entity-Baum — das Teilen von Entities zwischen Clips ist nicht erlaubt und führt zu einem Laufzeitfehler.

---

## TexturePool

Der `TexturePool` verwaltet wiederverwendbare GPU-Texturen für Composition-Zwischenergebnisse. Ziel: **Zero Allocations im Steady State** — nach wenigen Frames hat der Pool genug Texturen, um nie wieder neue erstellen zu müssen.

```csharp
/// <summary>
/// Pools GPU textures by (width, height, format) key.
/// After warm-up, runs with zero GPU allocations per frame.
/// Thread-safe for single-writer (compositor) usage.
/// </summary>
public sealed class TexturePool : IDisposable
{
    private readonly GraphicsDevice _device;

    // Key: (width, height, format) → Stack of available textures
    private readonly Dictionary<TextureKey, Stack<Texture>> _available = new();

    // All textures acquired this frame (for ReleaseAll)
    private readonly List<(TextureKey Key, Texture Texture)> _inUse = new();

    private readonly record struct TextureKey(int Width, int Height, PixelFormat Format);

    public TexturePool(GraphicsDevice device)
    {
        _device = device;
    }

    /// <summary>
    /// Acquire a texture from the pool, or create a new one if none available.
    /// </summary>
    public Texture Acquire(int width, int height, PixelFormat format)
    {
        var key = new TextureKey(width, height, format);

        if (_available.TryGetValue(key, out var stack) && stack.Count > 0)
        {
            var texture = stack.Pop();
            _inUse.Add((key, texture));
            return texture;
        }

        // Pool miss — create new texture (only during warm-up)
        var newTexture = Texture.New2D(
            _device, width, height, format,
            TextureFlags.ShaderResource | TextureFlags.RenderTarget);

        _inUse.Add((key, newTexture));
        return newTexture;
    }

    /// <summary>
    /// Return a single texture to the pool before frame end.
    /// Used when a ping-pong pass releases one of its two textures early.
    /// </summary>
    public void Release(Texture texture)
    {
        var key = new TextureKey(texture.Width, texture.Height, texture.Description.Format);

        if (!_available.TryGetValue(key, out var stack))
        {
            stack = new Stack<Texture>();
            _available[key] = stack;
        }

        stack.Push(texture);

        // Remove from in-use tracking
        for (int i = _inUse.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_inUse[i].Texture, texture))
            {
                _inUse.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>
    /// Return ALL textures acquired this frame back to the pool.
    /// Called once at frame end, after buffer swap.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var (key, texture) in _inUse)
        {
            if (!_available.TryGetValue(key, out var stack))
            {
                stack = new Stack<Texture>();
                _available[key] = stack;
            }
            stack.Push(texture);
        }
        _inUse.Clear();
    }

    /// <summary>
    /// Diagnostics: how many textures exist in the pool total.
    /// </summary>
    public int TotalPooled => _available.Values.Sum(s => s.Count);
    public int InUseCount => _inUse.Count;

    public void Dispose()
    {
        foreach (var stack in _available.Values)
        {
            foreach (var texture in stack)
                texture.Dispose();
        }
        _available.Clear();

        foreach (var (_, texture) in _inUse)
            texture.Dispose();
        _inUse.Clear();
    }
}
```

### Warm-Up-Verhalten

| Frame | Pool-Misses | Neue Texturen | Danach im Pool |
|-------|-------------|---------------|----------------|
| 1 | Alle | 5 (z.B.) | 5 |
| 2 | 0 | 0 | 5 |
| 3+ | 0 | 0 | 5 |

Nach dem ersten Frame (oder bei Resolution-Wechsel) stabilisiert sich der Pool. Kein `new Texture()` mehr, keine GPU-Allocations. Falls Clips hinzukommen, wächst der Pool einmalig und bleibt dann wieder stabil.

---

## Compute Clips

### Datenfluss

Compute-Clips führen GPU-Compute-Shader aus und liefern `Buffer[]`-Objekte (Structured Buffers, Append Buffers etc.) als Output. Sie produzieren keine Texturen — ihre Ergebnisse müssen von einem nachfolgenden Clip in sichtbare Pixel gewandelt werden.

```
[Clip "ParticleSim"]       → ComputeBuffer (positions, velocities)
    ↓ Sequential chain
[Clip "ParticleRender"]    → Texture (reads buffer, renders points/quads)
```

### IComputeClip Interface

```csharp
public interface IComputeClip : ISceneClip { }

// Example: Fluid simulation
// Process "FluidSim" : IComputeClip
//   OnActivation():
//     - Allocate structured buffers (velocity field, pressure field)
//     - Compile compute shaders
//   Update(out ClipOutput Output, float DeltaTime, ...):
//     - Dispatch compute shader
//     - Output = ClipOutput.FromComputeBuffer(velocityBuffer)
```

### Compute → Render Brücke

Der Übergang von Compute zu Rendering passiert innerhalb eines normalen Clips. Ein Render-Clip kann im Sequential-Modus den `ComputeBuffer` des Vorgängers lesen und daraus eine Textur rendern:

```csharp
// In a VL Process "ParticleRender" : ITextureGenerator
// The clip reads the upstream ComputeBuffer via ISceneContext:

public void Update(
    out ClipOutput Output,
    ISceneContext context,
    float activity)
{
    // Read the compute buffer from the previous clip in the chain
    var upstream = context.GetChildOutput<ClipOutput>("ParticleSim");
    if (upstream?.Kind == ClipOutputKind.ComputeBuffer)
    {
        var buffer = upstream.Value.ComputeBuffer;

        // Bind buffer as shader resource
        _particleEffect.Parameters.Set(
            ParticleRenderKeys.ParticleBuffer, buffer);

        // Render particles to our own texture
        _commandList.SetRenderTarget(null, _renderTarget);
        _commandList.Clear(_renderTarget, Color4.Transparent);
        _particleEffect.Draw(_commandList);

        Output = ClipOutput.FromTexture(_renderTarget);
    }
    else
    {
        Output = ClipOutput.None;
    }
}
```

### Synchronisation

`CommandList` ist innerhalb eines Frames **synchron** — alle Dispatches und Draw-Calls werden sequentiell auf derselben Command-List ausgeführt. Es gibt keine Timing-Probleme zwischen Compute-Dispatches und nachfolgenden Render-Calls, solange sie auf derselben Command-List liegen.

Stride garantiert, dass ein `Dispatch()` abgeschlossen ist, bevor der nächste Draw/Dispatch auf derselben `CommandList` beginnt (implicit UAV barrier). Kein manuelles Synchronisieren nötig.

---

## Mapping Clips

Mapping-Clips transformieren Texturen geometrisch für Multi-Projektor-Setups und Projection Mapping. Sie sitzen als **Sequential-Chain** zwischen dem Content und dem Output-Sink.

### MappingMode

```csharp
public enum MappingMode : byte
{
    None,
    GridWarp,       // Bezier/Bilinear grid deformation
    SoftEdgeBlend,  // Feathered overlap for multi-projector
    AlphaMask,      // Polygon or texture based masking
    UVRemap         // Arbitrary UV remapping via lookup texture
}
```

### GridWarp

Interaktive Kontrollpunkte auf einem Gitter, die eine Bezier- oder Bilinear-Verzerrung definieren. Jeder Projektor hat sein eigenes Warp-Grid. Die Kontrollpunkte sind **animierbare Parameter** — sie können in der Timeline gespeichert und zwischen Presets übergeblendet werden.

```csharp
// Process "GridWarp" : IMappingEffect
// Parameters:
//   GridResolution : Int2 (e.g. 5x5)
//   ControlPoints : Spread<Vector2> (normalized 0..1)
//   InterpolationMode : GridInterpolation (Bilinear, Bezier)
//
// Update(out Texture Output, Texture Input, ...):
//   - Build mesh from control points
//   - Render Input texture through deformed mesh
//   - Output = result texture
```

### SoftEdgeBlend

Für Multi-Projektor-Overlap: Erzeugt weiche Kanten (Feathering) an den Rändern der Projektion, damit sich überlappende Bereiche nicht doppelt hell erscheinen.

```csharp
// Process "SoftEdgeBlend" : IMappingEffect
// Parameters:
//   EdgeLeft, EdgeRight, EdgeTop, EdgeBottom : float (0..1, width of blend region)
//   Gamma : float (edge curve correction)
//   BlendCurve : BlendCurveType (Linear, Cosine, Cubic)
//
// Shader multiplies input texture with a gradient mask.
```

### AlphaMask

Masking per Polygon (interaktiv gezeichnet) oder per Textur. Nützlich für unregelmäßige Projektionsflächen (Gebäudefassaden, Skulpturen).

```csharp
// Process "AlphaMask" : IMappingEffect
// Parameters:
//   MaskTexture : Texture (optional, external mask)
//   Polygons : Spread<Polygon2D> (interaktiv gezeichnete Masken)
//   Invert : bool
//   Feather : float (edge softness)
```

### Mapping in der Kette

```
[Layer "Mapping Proj1" — Sequential]
    Input: Composited content texture
    [Clip "GridWarp"]        → Texture (warped)
    [Clip "SoftEdgeBlend"]   → Texture (feathered edges)
    [Output "Projektor 1"]   → Sink
```

Jeder Mapping-Clip ist ein normaler `ITextureEffect` mit `Texture → Texture` Signatur. Durch die Sequential-Chain werden sie automatisch hintereinander geschaltet. Das macht die Mapping-Pipeline beliebig erweiterbar — ein User kann eigene Mapping-Clips in VL patchen.

---

## PostFX Clips

Screen-Space-Effekte als Sequential-Chains. Jeder PostFX-Clip nimmt eine Textur, wendet einen Effekt an, und gibt die veränderte Textur zurück. Die Ping-Pong-Technik des `TextureCompositor` stellt sicher, dass beliebig viele Effekte hintereinander geschaltet werden können, ohne Textur-Kopien.

### Verfügbare Effekte

| Effekt | Beschreibung | Activity-Verhalten |
|--------|-------------|-------------------|
| Bloom | Glühende Highlights | `Activity` skaliert Bloom-Intensität |
| DepthOfField | Tiefenunschärfe | `Activity` blendet zwischen scharf und unscharf |
| Tonemap | HDR → LDR Konvertierung | `Activity` blendet zwischen unmapped und mapped |
| Vignette | Rand-Abdunklung | `Activity` skaliert Vignette-Stärke |
| ChromaticAberration | Farbsaum-Verschiebung | `Activity` skaliert Aberration-Offset |
| FilmGrain | Film-Rauschen | `Activity` skaliert Grain-Intensität |

### Activity als Effekt-Intensität

Bei PostFX-Clips hat `Activity` eine doppelte Bedeutung:

1. **Lifecycle**: `Activity = 0` → Clip ist inaktiv, Effekt wird **komplett übersprungen** (bypassed)
2. **Intensität**: `Activity = 0.5` → Effekt mit halber Stärke (z.B. halber Bloom)

Das ermöglicht Fade-Outs von Effekten über die FSM oder Timeline: Ein Bloom, dessen `Activity` von 1.0 auf 0.0 animiert wird, wird sanft ausgeblendet und dann komplett bypassed.

```csharp
// IIntensityEffect — optionales Interface für PostFX-Clips
public interface IIntensityEffect
{
    float Intensity { get; set; }
}

// In der Sequential-Composition:
if (effect is IIntensityEffect intensityEffect)
    intensityEffect.Intensity = weight; // weight = activity * opacity
```

### Ping-Pong Rendering

Die Sequential-Chain nutzt zwei Pool-Texturen abwechselnd:

```
Frame N:
  Ping ← Input-Textur
  Pong ← Bloom(Ping)       → read Ping, write Pong
  Ping ← Tonemap(Pong)     → read Pong, write Ping
  Pong ← Vignette(Ping)    → read Ping, write Pong
  Result = Pong
  Release Ping
```

Kein Kopieren, keine Extra-Texturen. Genau 2 Pool-Texturen pro Kette, unabhängig von der Anzahl der Effekte.

---

## Output Sinks

Output Sinks sind **Clips, kein separates System**. Sie sitzen im selben Baum wie alle anderen Clips und unterliegen denselben Regeln: Activity, FSM-Steuerung, Parameter, Presets. Das vereinfacht die Architektur enorm — ein Output ist einfach ein Blatt-Node, der seine Input-Textur irgendwohin schickt.

### Sink-Typen

| Typ | Beschreibung | Community/Built-in |
|-----|-------------|-------------------|
| `ProjectionOutput` | OS-Fenster auf Display/Projektor | Built-in |
| `NDIOutput` | NDI-Netzwerk-Stream | Community-Clip |
| `SpoutOutput` | Spout Shared Texture | Community-Clip |
| `VideoRecorder` | H.264/H.265 Datei-Recording | Community-Clip |

### Universelle Input-Konvertierung

Alle Sinks akzeptieren **jeden Input-Typ** (`SlotType.Any`). Falls der Input kein Texture ist, wird er automatisch konvertiert:

```csharp
// In IOutputSink base implementation:
var input = context.GetPrimaryInput<ClipOutput>();

Texture? outputTexture = input.Kind switch
{
    ClipOutputKind.Texture => input.Texture,
    ClipOutputKind.Renderer => RenderToTexture(input.Renderer!, width, height),
    ClipOutputKind.ImageEffect => ApplyToBlank(input.ImageEffect!, width, height),
    ClipOutputKind.Entities => RenderScene(input.EntityRoot!, width, height),
    ClipOutputKind.ComputeBuffer => null, // No visual representation
    _ => null
};
```

### FSM-Steuerung der Outputs

Weil Outputs Clips sind, kann die FSM sie steuern:

- **Activity = 0** → Output ist dunkel (Blackout)
- **Activity = 0.5** → Output ist gedimmt (50% Helligkeit)
- **Activity = 1.0** → Output mit voller Helligkeit

Das macht `Activity` zum **Dimmer** — ein Output kann per FSM-Transition sanft ein- und ausgeblendet werden, ohne extra Dimmer-Logic.

### Display-Konfiguration als Parameter

Alle display-relevanten Einstellungen sind normale Clip-Parameter:

```csharp
// Process "ProjectionOutput" : IOutputSink
// Parameters:
//   DisplayIndex : int              // Which monitor/projector (0-based)
//   DisplayNameHint : string        // e.g. "DELL U2723QE" — for cross-machine portability
//   Fullscreen : bool               // Exclusive fullscreen vs borderless window
//   Resolution : Int2               // Override resolution (0 = native)
//   Rotation : DisplayRotation      // None, CW90, CW180, CW270
//   Position : Int2                 // Window position (for windowed mode)
//   VSync : bool                    // Vertical sync
```

**DisplayNameHint**: Wenn ein Rechner andere Monitor-Indizes hat als der Entwicklungsrechner, versucht das System zuerst den `DisplayNameHint` zu matchen. Nur wenn kein passender Monitor gefunden wird, fällt es auf `DisplayIndex` zurück. Das macht Shows portabel zwischen verschiedenen Hardware-Setups.

### Multiple Sinks per SubGraph

Mehrere Sinks können aus **verschiedenen Punkten** im Graphen lesen — wie Audio-Aux-Sends, bei denen das Signal an beliebiger Stelle abgegriffen wird.

Das wird über explizite `DataFlow`-Edges realisiert:

```csharp
// NDI-Output greift das Signal nach dem PostFX-Layer ab
graph = graph.WithEdge(new SceneEdge(
    source: "postfx_layer",
    target: "ndi_output",
    kind: EdgeKind.DataFlow));

// Recorder greift das Signal vor dem Mapping ab
graph = graph.WithEdge(new SceneEdge(
    source: "2d_content_layer",
    target: "recorder",
    kind: EdgeKind.DataFlow));
```

Ohne explizite Edge folgt ein Sink dem normalen hierarchischen Routing (Input vom Parent oder vorherigen Sibling). Mit Edge kann er sich an jeden beliebigen Punkt im Graphen hängen.

```
[Layer "PostFX"]
    [Clip "Bloom"]
    [Clip "Tonemap"]  ──DataFlow Edge──→  [Output "NDI"]
                                          (eigener SubGraph-Branch)

[Layer "2D Content"]
    [Clip "LiveDrawing"]
    [Clip "VideoPlayer"]  ──DataFlow Edge──→  [Output "Recorder"]
```

---

## Pipeline-Integration

### Wo Composition im Frame-Zyklus stattfindet

Die Composition passiert in den Phasen 10–14 der Frame-Pipeline (siehe [02-PIPELINE.md](02-PIPELINE.md)):

```
Pass  1:   SyncStructure          (Tree → Flat Arrays)
Pass  2:   Time Propagation       (Top-Down)
Pass  3:   External Inputs        (OSC, MIDI, Audio)
Pass  4:   Recording Playback
Pass  5:   State Machines
Pass  6:   Activity Update        (Fade-In/Out, Inheritance)
Pass  7:   Timeline Keyframes
Pass  8:   Constraint Solving
Pass  9:   Stream Routing         (StreamPool.Cleanup, Slot-Zuweisung)
Pass 10:   Clip Evaluation        ← Clips produzieren ClipOutput, ab in StreamPool
Pass 10.5: Texture Composition    ← Bottom-Up: Blatt-Outputs → Layer-Composites
Pass 10.6: Scene3D Composition    ← Entity-Merge in shared SceneInstance
Pass 11:   Recording Capture
Pass 12:   Optional 3D Passes    (Transform, Bounds, Visibility)
Pass 13:   Activity Logging
Pass 14:   Swap + Output Present  ← Buffer-Swap, Sinks präsentieren an Fenster
           Deferred Disposal      ← GPU-Ressourcen deaktivierter Clips freigeben
```

### Pass 10: Clip Evaluation

Alle aktiven Clips werden in **Bottom-Up-Reihenfolge** evaluiert (Blätter zuerst). Jeder Clip produziert ein `ClipOutput`, das im `StreamPool` abgelegt wird:

```csharp
// ClipEvaluator.Evaluate()
for (int i = 0; i < evaluationOrder.Length; i++)
{
    int nodeIndex = evaluationOrder[i];
    if (flat.Activity[nodeIndex] <= 0f) continue;

    var clip = GetClipInstance(nodeIndex);
    clip.Update(/* system pins, parameters */);

    var output = clip.GetOutput(); // ClipOutput
    streamPool.Write(flat.PrimaryOutputSlot[nodeIndex], output);
}
```

### Pass 10.5: Texture Composition (Bottom-Up)

Nach der Clip-Evaluation liegen alle Blatt-Outputs im StreamPool. Jetzt composited jeder Parent-Node die Outputs seiner Kinder:

```csharp
// Bottom-Up: from deepest level to root
for (int level = flat.LevelCount - 2; level >= 0; level--)
{
    int levelStart = flat.LevelOffsets[level];
    int levelEnd = (level + 1 < flat.LevelCount)
        ? flat.LevelOffsets[level + 1]
        : flat.NodeCount;

    for (int i = levelStart; i < levelEnd; i++)
    {
        if (flat.CompositionInputCount[i] == 0) continue;
        if (flat.Activity[i] <= 0f) continue;

        var mode = flat.CompositionMode[i]; // Parallel or Sequential

        if (mode == CompositionMode.Scene3D)
            continue; // handled in Pass 10.6

        var composited = _textureCompositor.Composite(
            flat, i, _streamPool, mode,
            outputWidth, outputHeight);

        if (composited is not null)
        {
            streamPool.Write(
                flat.PrimaryOutputSlot[i],
                new ClipOutput(ClipOutputKind.Texture, texture: composited));
        }
    }
}
```

### Pass 10.6: Scene3D Composition

Separate Phase für 3D-Szenen, weil Entity-Merging andere Logik erfordert als Textur-Blending:

```csharp
for (int i = 0; i < flat.NodeCount; i++)
{
    if (flat.CompositionMode[i] != CompositionMode.Scene3D) continue;
    if (flat.Activity[i] <= 0f) continue;

    var sceneInstance = _scene3DCompositor.Compose(flat, i, _streamPool);

    // Render the merged 3D scene to a texture for further composition
    var renderedTexture = RenderSceneToTexture(sceneInstance, outputWidth, outputHeight);
    streamPool.Write(
        flat.PrimaryOutputSlot[i],
        ClipOutput.FromTexture(renderedTexture));
}
```

### Pass 14: Swap + Output Sinks

Nach dem Buffer-Swap lesen die Output-Sinks ihre Input-Texturen und präsentieren sie:

```csharp
// Buffer swap
(_front, _back) = (_back, _front);
_streamPool.SwapBuffers();

// Output Sinks present to their targets
foreach (var sinkIndex in _outputSinkIndices)
{
    var slot = _front.PrimaryInputSlot[sinkIndex];
    var input = _streamPool.ReadFront<ClipOutput>(slot);
    if (input is null) continue;

    // Sink presents (window, NDI, Spout, file, ...)
    var sink = GetClipInstance(sinkIndex) as IOutputSink;
    sink?.Present(input.Value, _commandList);
}

// Deferred disposal — safe now, no thread reads old resources
_deferredDisposalQueue.FlushAll();

// Return all pool textures for reuse next frame
_texturePool.ReleaseAll();
```

---

## GPU Resource Lifecycle

### Clip-Lifecycle für GPU-Ressourcen

```
Create()            → Nichts allozieren. Der Clip existiert als VL-Objekt,
                      hat aber keine GPU-Ressourcen.

OnActivation()      → GPU-Ressourcen allozieren:
                      - Shader kompilieren / laden
                      - Render-Targets erstellen
                      - Structured Buffers allozieren
                      - Entity-Bäume aufbauen

Update()            → GPU-Ressourcen nutzen:
                      - In Render-Targets rendern
                      - Compute Shader dispatchen
                      - ClipOutput zurückgeben (Referenz, kein Transfer)

OnDeactivation()    → GPU-Ressourcen in Deferred-Queue einreihen:
                      - NICHT sofort disposen!
                      - _deferredDisposalQueue.Enqueue(renderTarget)
                      - _deferredDisposalQueue.Enqueue(computeBuffer)
                      - Tatsächliche Disposal passiert NACH Buffer-Swap

Dispose()           → Finale Aufräumarbeiten:
                      - Falls Ressourcen noch nicht disposed: jetzt disposen
                      - Clip wird aus dem Graphen entfernt
                      - Sollte im Normalfall nichts mehr zu tun haben
```

### Sequenzdiagramm eines Frame

```
Frame N:
  ┌─────────────────────────────────────────────────────────────────┐
  │ Update Thread (Back Buffer)                                     │
  │                                                                 │
  │  1. SyncStructure                                               │
  │  2. Time / Input / FSM / Activity / Constraints                 │
  │  3. Stream Routing (StreamPool slots allocated)                 │
  │  4. Clip Evaluation:                                            │
  │     ├── Clip A: OnActivation() → alloc RenderTarget             │
  │     ├── Clip A: Update() → render to RT, output ClipOutput      │
  │     ├── Clip B: Update() → compute dispatch, output ClipOutput  │
  │     └── Clip C: OnDeactivation() → enqueue RT to deferred       │
  │  5. Texture Composition (pool textures acquired)                │
  │  6. Scene3D Composition                                         │
  │  7. SWAP (front ↔ back)                                         │
  │  8. Output Sinks present from front buffer                      │
  │  9. Deferred disposal (Clip C's RT freed)                       │
  │ 10. TexturePool.ReleaseAll()                                    │
  └─────────────────────────────────────────────────────────────────┘
```

### Warum Deferred Disposal?

Ohne Deferred Disposal könnte folgendes passieren:

```
Thread 1 (Update):  Clip deaktiviert → Texture.Dispose()
Thread 2 (Render):  Liest noch aus derselben Texture → CRASH / Korruption
```

Mit Deferred Disposal:

```
Thread 1 (Update):  Clip deaktiviert → Enqueue(texture)
                    ... Swap ...
Thread 2 (Render):  Liest von Front Buffer (neue Daten, alte Texture nicht mehr referenziert)
Thread 1 (Update):  FlushAll() → Texture.Dispose() → sicher
```

---

## Beispiel: Vollständiger Show-Graph

```
[Show]
  [Layer "3D Scene" — Scene3D]
    [Clip "Environment"]         → Entities (Skybox, Terrain, Lighting)
    [Clip "Characters"]          → Entities (Rigged characters)
    [Clip "ParticleSim"]         → ComputeBuffer (GPU particle positions)
    [Clip "ParticleRender"]      → Renderer → Texture (reads ParticleSim buffer)

  [Layer "2D Content" — Parallel]
    [Clip "LiveDrawing"]         → Texture (Skia canvas)
    [Clip "VideoPlayer"]         → Texture (decoded video frame)

  [Layer "Simulation" — Sequential]
    [Clip "FluidSim"]            → ComputeBuffer (velocity + density field)
    [Clip "FluidRender"]         → Texture (visualizes fluid as color field)

  [Layer "PostFX" — Sequential]
    [Clip "Bloom"]               → ImageEffect (glow on highlights)
    [Clip "Tonemap"]             → ImageEffect (HDR → SDR conversion)

  [Layer "Mapping Proj1" — Sequential]
    [Clip "GridWarp"]            → Texture (geometric correction)
    [Clip "SoftEdge"]            → Texture (feathered edges)
    [Output "Projektor 1"]       → Sink (OS window on DisplayIndex 1)

  [Layer "Mapping Proj2" — Sequential]
    [Clip "WarpSide"]            → Texture (side projector warp)
    [Output "Projektor 2"]       → Sink (OS window on DisplayIndex 2)

  [Output "NDI"]                 → Sink (Edge from "PostFX" — taps after effects)
  [Output "Recorder"]            → Sink (Edge from "2D Content" — records raw content)
```

### Datenfluss dieses Graphen

```
"3D Scene" Layer (Scene3D mode):
  Environment.Entities ─┐
  Characters.Entities  ─┤→ Scene3DCompositor → merged SceneInstance → render → Texture
  ParticleSim.Buffer  ─→ ParticleRender.Renderer ─┘

"2D Content" Layer (Parallel mode):
  LiveDrawing.Texture  ─┐→ TextureCompositor (BlendMode, Opacity) → Texture
  VideoPlayer.Texture  ─┘

"Simulation" Layer (Sequential mode):
  FluidSim.ComputeBuffer → FluidRender reads buffer → Texture

Root Parallel Composition:
  "3D Scene".Texture   ─┐
  "2D Content".Texture ─┤→ TextureCompositor → Composited Texture
  "Simulation".Texture ─┘

"PostFX" Sequential Chain:
  Composited Texture → Bloom (ImageEffect) → Tonemap (ImageEffect) → Post-Processed Texture

"Mapping Proj1" Sequential Chain:
  Post-Processed Texture → GridWarp → SoftEdge → "Projektor 1" Sink → Display 1

"Mapping Proj2" Sequential Chain:
  Post-Processed Texture → WarpSide → "Projektor 2" Sink → Display 2

Edge-basierter Tap:
  "PostFX" Output ──DataFlow Edge──→ "NDI" Sink → Network
  "2D Content" Output ──DataFlow Edge──→ "Recorder" Sink → File
```

Dieses Setup zeigt eine typische Projection-Mapping-Show mit:
- 3D-Szene + Partikel-Simulation auf der GPU
- 2D-Content (Live-Zeichnung + Video) als parallele Layer
- Fluid-Simulation als Compute → Render Pipeline
- PostFX-Kette (Bloom + Tonemap)
- Zwei Projektoren mit individuellem Mapping (Warp + SoftEdge)
- NDI-Stream für Monitoring und Datei-Recording als Aux-Taps
