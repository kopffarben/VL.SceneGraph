# 10 — Input-System

> Vollstaendige Referenz des Input-Systems. Beschreibt wie externe Eingaben (Maus, Touch, Stift, MIDI, OSC, Keyboard, Gamepad, Audio, Kamera) ueber Channels in den SceneGraph gelangen, wie Spatial-Inputs hit-getestet werden und wie MIDI-Learn funktioniert.

---

## Kern-Idee: Channels als universeller Input-Bus

Alle Inputs werden in **Channels** konvertiert. Die Quelle ist irrelevant — Clips abonnieren Channel-Pfade. Der `IChannelHub` dient als zentraler, typisierter Message-Bus. Dadurch entsteht eine vollstaendige Entkopplung zwischen Input-Providern und Consumern:

```
[MouseInputClip]  ──→  Mouse/Position          ──→  [DrawingClip liest Channel]
[MIDIInputClip]   ──→  MIDI/nanoKONTROL/CC/1   ──→  [Clip liest Channel via InputMapping]
[OSCInputClip]    ──→  OSC/sensor/temperature   ──→  [Clip liest Channel via InputMapping]
[TouchInputClip]  ──→  Touch/0/Position         ──→  [InputRoutingPass → InputHit]
```

Spatial Inputs (Mouse, Touch, Pen) werden zusaetzlich vom `InputRoutingPass` gegen `ComputedLayout` hit-getestet. Das Ergebnis sind `InputHit` Transient-Components auf den getroffenen Nodes.

### Channel-Hierarchie

Alle Channel-Pfade folgen dem Muster `{DeviceType}/{Detail}`. Die vollstaendige Auflistung:

#### Mouse

```
Mouse/Position            : Vector2       — Absolute Position in Viewport-Koordinaten
Mouse/Delta               : Vector2       — Bewegungsdelta seit letztem Frame
Mouse/LeftButton          : bool          — Linke Maustaste gehalten
Mouse/RightButton         : bool          — Rechte Maustaste gehalten
Mouse/MiddleButton        : bool          — Mittlere Maustaste gehalten
Mouse/WheelDelta          : float         — Scrollrad-Delta
Mouse/LeftClicked         : bool          — Linke Maustaste Bang (ein Frame true)
Mouse/RightClicked        : bool          — Rechte Maustaste Bang (ein Frame true)
Mouse/DoubleClicked       : bool          — Doppelklick Bang
```

#### Touch (Multi-Touch)

```
Touch/Count               : int           — Anzahl aktiver Touch-Punkte
Touch/0/Position          : Vector2       — Position des ersten Fingers
Touch/0/Delta             : Vector2       — Delta des ersten Fingers
Touch/0/Id                : int           — System-ID des Touch-Punkts
Touch/0/Pressure          : float         — Druck (0..1), sofern Hardware unterstuetzt
Touch/1/Position          : Vector2       — Position des zweiten Fingers
Touch/1/Delta             : Vector2       — ...
...                                       — (bis Touch/9 fuer 10 Finger)
```

#### Pen (Stift / Stylus)

```
Pen/Position              : Vector2       — Stiftposition
Pen/Delta                 : Vector2       — Bewegungsdelta
Pen/Pressure              : float         — Anpressdruck (0..1)
Pen/Tilt                  : Vector2       — Neigung (X/Y in Grad)
Pen/Rotation              : float         — Rotation um die Stiftachse (0..360)
Pen/IsDown                : bool          — Stift beruehrt Surface
Pen/IsEraser              : bool          — Radierer-Ende aktiv
Pen/BarrelButton          : bool          — Seitentaste am Stift
```

#### Keyboard

```
Keyboard/Modifiers        : KeyModifiers  — Shift, Ctrl, Alt, Win (Flags)
Keyboard/LastKeyPressed   : Keys          — Letzte gedrueckte Taste
Keyboard/TextInput        : string        — Unicode-Texteingabe (IME-kompatibel)
Keyboard/Key/{KeyName}    : bool          — Einzelne Taste (Bang), z.B. Keyboard/Key/Space
```

#### MIDI

```
MIDI/{Device}/CC/{N}      : float         — Control Change 0..127 → 0..1
MIDI/{Device}/Note/{N}/On : bool          — Note On Bang
MIDI/{Device}/Note/{N}/Off: bool          — Note Off Bang
MIDI/{Device}/Note/{N}/Velocity : float   — Velocity 0..1
MIDI/{Device}/PitchBend   : float         — Pitch Bend -1..1
MIDI/{Device}/Aftertouch  : float         — Channel Aftertouch 0..1
MIDI/{Device}/Clock/Beat  : bool          — MIDI Clock Beat Bang
MIDI/{Device}/Clock/BPM   : float         — Errechnetes BPM
```

#### OSC

```
OSC/{beliebiger/pfad}     : object        — OSC-Adressen werden direkt zu Channel-Pfaden
```

Beispiele:
```
OSC/sensor/temperature    : float
OSC/tracker/1/position    : Vector3
OSC/dmx/universe/1        : byte[]
```

#### Gamepad

```
Gamepad/0/LeftStick       : Vector2       — Linker Analogstick (-1..1)
Gamepad/0/RightStick      : Vector2       — Rechter Analogstick (-1..1)
Gamepad/0/LeftTrigger     : float         — Linker Trigger (0..1)
Gamepad/0/RightTrigger    : float         — Rechter Trigger (0..1)
Gamepad/0/A               : bool          — A-Button (Bang)
Gamepad/0/B               : bool          — B-Button (Bang)
Gamepad/0/X               : bool          — X-Button (Bang)
Gamepad/0/Y               : bool          — Y-Button (Bang)
Gamepad/0/DPad            : Vector2       — D-Pad als Richtung
Gamepad/0/LeftShoulder    : bool          — Schultertaste links
Gamepad/0/RightShoulder   : bool          — Schultertaste rechts
Gamepad/1/...             : ...           — Zweiter Gamepad
```

#### Audio

```
Audio/Level               : float         — RMS-Pegel (0..1)
Audio/Peak                : float         — Spitzenpegel (0..1)
Audio/Beat                : bool          — Beat-Detection Bang
Audio/BPM                 : float         — Erkanntes BPM
Audio/FFT/Bands           : float[]       — Komplettes FFT-Spektrum (normalisiert)
Audio/Low                 : float         — Tieffrequenz-Energie (0..1)
Audio/Mid                 : float         — Mittelfrequenz-Energie (0..1)
Audio/High                : float         — Hochfrequenz-Energie (0..1)
Audio/Onset               : bool          — Onset-Detection Bang
```

#### Camera

```
Camera/{Device}/Frame     : Texture       — Aktuelles Kamerabild als Texture
Camera/{Device}/Resolution: Vector2       — Aktuelle Aufloesung
```

---

## InputProvider = Clips

Input-Provider sind **Clips** (`ISceneClip`), kein separates System. Sie nutzen die gleiche Infrastruktur wie alle anderen Clips: Lifecycle-Management, Inspector-Konfiguration, FSM-Steuerung, NodeBrowser-Discovery, Show-Datei-Serialisierung und Runtime-Konfigurierbarkeit in kompilierten Apps.

### Warum Clips statt separatem System?

| Kriterium | Als separates System | Als Clip (`ISceneClip`) |
|-----------|---------------------|------------------------|
| **Konfiguration** | Eigenes Config-UI noetig | Inspector zeigt alle Parameter |
| **Aktivierung** | Eigene Enable/Disable-Logik | FSM steuert `OnActivation`/`OnDeactivation` |
| **Discovery** | Eigene Registry | `TypeRegistry` findet alle `ISceneClip` |
| **Serialisierung** | Eigenes Format | Show-Datei speichert als normalen Node |
| **NodeBrowser** | Separate Kategorie | Erscheint unter "Input" mit allen anderen Clips |
| **Runtime-Aenderung** | Eigenes Settings-Panel noetig | Clip-Parameter sind SceneEdits → Runtime-aenderbar |
| **Lifecycle** | Manuelles Create/Dispose | Runtime managed `OnActivation`/`OnDeactivation`/`Dispose` |
| **Undo** | Eigener Undo-Stack noetig | Standard-UndoStack via `SceneEdit` |
| **Hotswap** | Eigener Reload-Mechanismus | VL-Hotswap wie bei allen Clips |

**Fazit:** Null zusaetzliche Infrastruktur. Alle bestehenden Systeme (Inspector, FSM, Serialisierung, Undo, Hotswap) funktionieren automatisch.

### InputClipBase — Channel-Management

Alle Input-Clips erben von einer gemeinsamen Basisklasse, die das Channel-Lifecycle-Management kapselt:

```csharp
public abstract class InputClipBase
{
    // Cached channel references — zero-lookup in Update()
    private readonly Dictionary<string, IChannel<object>> _channels = new();

    /// <summary>
    /// Creates or retrieves a channel. Safe for input clips since they own their channels.
    /// TryAddChannel is OK here — input clips are the authoritative source.
    /// </summary>
    protected IChannel<object> EnsureChannel(IChannelHub hub, string path, Type type)
    {
        if (!_channels.TryGetValue(path, out var channel))
        {
            channel = hub.TryAddChannel(path, type);
            _channels[path] = channel;
        }
        return channel;
    }

    /// <summary>
    /// Writes a value to a cached channel reference.
    /// IMPORTANT: There is no hub.Write() method. Writing is done via the
    /// channel's Object property on the cached reference.
    /// </summary>
    protected void WriteChannel(string path, object value)
    {
        if (_channels.TryGetValue(path, out var channel))
        {
            channel.Object = value;
        }
    }

    /// <summary>
    /// Removes all channels created by this clip.
    /// Called in OnDeactivation to clean up the channel bus.
    /// </summary>
    protected void RemoveChannels(IChannelHub hub)
    {
        foreach (var kvp in _channels)
        {
            hub.TryRemoveChannel(kvp.Key);
        }
        _channels.Clear();
    }
}
```

**Lifecycle-Vertrag:**

1. **`OnActivation`**: Channels werden ueber `EnsureChannel` erstellt und im internen Dictionary gecacht.
2. **`Update` (pro Frame)**: Werte werden ueber `WriteChannel` auf die gecachten Referenzen geschrieben. Kein Dictionary-Lookup auf dem IChannelHub — die Referenz ist bereits aufgeloest.
3. **`OnDeactivation`**: `RemoveChannels` entfernt alle Channels vom Hub und leert den Cache.

**Kritischer Punkt:** Es gibt **keine** `hub.Write()`-Methode. Der einzige Weg, einen Channel zu beschreiben, ist ueber `channel.Object = value` auf der gecachten `IChannel<object>`-Referenz. Das ist bewusst so designed, damit der Lookup einmalig bei Activation stattfindet und der Hot-Path (Update) allocation-frei bleibt.

---

## Konkrete Input-Clips

### MouseInputClip

Liest Maus-Input ueber den Stride `InputManager`. Keine externen Dependencies.

```csharp
public sealed class MouseInputClip : InputClipBase, ISceneLogic
{
    private IChannel<object> _position, _delta, _leftButton, _rightButton;
    private IChannel<object> _wheelDelta, _leftClicked, _rightClicked;
    private bool _wasLeftDown, _wasRightDown;

    public void OnActivation(IChannelHub hub)
    {
        _position     = EnsureChannel(hub, "Mouse/Position", typeof(Vector2));
        _delta        = EnsureChannel(hub, "Mouse/Delta", typeof(Vector2));
        _leftButton   = EnsureChannel(hub, "Mouse/LeftButton", typeof(bool));
        _rightButton  = EnsureChannel(hub, "Mouse/RightButton", typeof(bool));
        _wheelDelta   = EnsureChannel(hub, "Mouse/WheelDelta", typeof(float));
        _leftClicked  = EnsureChannel(hub, "Mouse/LeftClicked", typeof(bool));
        _rightClicked = EnsureChannel(hub, "Mouse/RightClicked", typeof(bool));
    }

    public void Update(InputManager input)
    {
        var mouse = input.Mouse;

        _position.Object   = mouse.Position;
        _delta.Object      = mouse.Delta;
        _leftButton.Object = mouse.IsButtonDown(MouseButton.Left);
        _rightButton.Object = mouse.IsButtonDown(MouseButton.Right);
        _wheelDelta.Object = mouse.WheelDelta;

        // Bang-Channels: ein Frame true, dann false
        bool leftDown = mouse.IsButtonDown(MouseButton.Left);
        _leftClicked.Object = leftDown && !_wasLeftDown;
        _wasLeftDown = leftDown;

        bool rightDown = mouse.IsButtonDown(MouseButton.Right);
        _rightClicked.Object = rightDown && !_wasRightDown;
        _wasRightDown = rightDown;
    }

    public void OnDeactivation(IChannelHub hub) => RemoveChannels(hub);
}
```

**Verfuegbarkeit:** Stride `InputManager` ist Teil von VL.StandardLibs.

---

### TouchInputClip

Multi-Touch-Unterstuetzung. Jeder Finger bekommt einen eigenen Channel-Slot.

```csharp
public sealed class TouchInputClip : InputClipBase, ISceneLogic
{
    private const int MaxTouches = 10;
    private IChannel<object> _count;
    private IChannel<object>[] _positions, _deltas, _ids, _pressures;

    public void OnActivation(IChannelHub hub)
    {
        _count = EnsureChannel(hub, "Touch/Count", typeof(int));
        _positions = new IChannel<object>[MaxTouches];
        _deltas    = new IChannel<object>[MaxTouches];
        _ids       = new IChannel<object>[MaxTouches];
        _pressures = new IChannel<object>[MaxTouches];

        for (int i = 0; i < MaxTouches; i++)
        {
            _positions[i] = EnsureChannel(hub, $"Touch/{i}/Position", typeof(Vector2));
            _deltas[i]    = EnsureChannel(hub, $"Touch/{i}/Delta", typeof(Vector2));
            _ids[i]       = EnsureChannel(hub, $"Touch/{i}/Id", typeof(int));
            _pressures[i] = EnsureChannel(hub, $"Touch/{i}/Pressure", typeof(float));
        }
    }

    public void Update(InputManager input)
    {
        var touches = input.PointerEvents;
        int count = 0;

        foreach (var pointer in touches)
        {
            if (count >= MaxTouches) break;
            _positions[count].Object = pointer.Position;
            _deltas[count].Object    = pointer.DeltaPosition;
            _ids[count].Object       = pointer.PointerId;
            _pressures[count].Object = pointer.Pressure;
            count++;
        }

        _count.Object = count;

        // Nicht-aktive Slots zuruecksetzen
        for (int i = count; i < MaxTouches; i++)
        {
            _pressures[i].Object = 0f;
        }
    }

    public void OnDeactivation(IChannelHub hub) => RemoveChannels(hub);
}
```

**Verfuegbarkeit:** Stride `InputManager` liefert Touch-Events ueber `PointerEvents`.

---

### PenInputClip

**NICHT in VL.StandardLibs enthalten.** Muss custom ueber die Windows `WM_POINTER` API gebaut werden.

Der Stift-Input ist **kritisch fuer Kopffarben** (Julias Zeichen-Performance). Stride's InputManager kennt keinen Pen als eigenstaendiges Device — er mapped Pen-Events auf Mouse oder Touch. Dadurch gehen Druck, Neigung und Rotation verloren.

```csharp
public sealed class PenInputClip : InputClipBase, ISceneLogic
{
    private IChannel<object> _position, _delta, _pressure, _tilt, _rotation;
    private IChannel<object> _isDown, _isEraser, _barrelButton;
    private WmPointerHandler _pointer; // Custom WM_POINTER wrapper

    public void OnActivation(IChannelHub hub)
    {
        _position     = EnsureChannel(hub, "Pen/Position", typeof(Vector2));
        _delta        = EnsureChannel(hub, "Pen/Delta", typeof(Vector2));
        _pressure     = EnsureChannel(hub, "Pen/Pressure", typeof(float));
        _tilt         = EnsureChannel(hub, "Pen/Tilt", typeof(Vector2));
        _rotation     = EnsureChannel(hub, "Pen/Rotation", typeof(float));
        _isDown       = EnsureChannel(hub, "Pen/IsDown", typeof(bool));
        _isEraser     = EnsureChannel(hub, "Pen/IsEraser", typeof(bool));
        _barrelButton = EnsureChannel(hub, "Pen/BarrelButton", typeof(bool));

        // WM_POINTER provides unified Mouse+Touch+Pen on Windows
        _pointer = new WmPointerHandler();
        _pointer.Initialize();
    }

    public void Update()
    {
        var state = _pointer.GetPenState();
        if (state == null) return;

        _position.Object     = state.Position;
        _delta.Object        = state.Delta;
        _pressure.Object     = state.Pressure;     // 0..1, echte Druckstufen
        _tilt.Object         = state.Tilt;          // X/Y in Grad
        _rotation.Object     = state.Rotation;      // 0..360
        _isDown.Object       = state.IsDown;
        _isEraser.Object     = state.IsEraser;      // Hardware-Eraser-Ende
        _barrelButton.Object = state.BarrelButton;  // Seitentaste
    }

    public void OnDeactivation(IChannelHub hub)
    {
        _pointer?.Dispose();
        RemoveChannels(hub);
    }
}
```

**Warum WM_POINTER statt RawInput?**

`WM_POINTER` ist die modernere Windows-API (ab Windows 8) und liefert unified Input fuer Mouse, Touch und Pen. Sie bietet:
- Echte Druckstufen (1024+ Levels bei Wacom)
- Neigung in X/Y (Grad)
- Rotation (Barrel Rotation)
- Eraser-Erkennung (Hardware-Flag)
- Korrekte HiDPI-Koordinaten

`RawInput` ist noetig fuer noch tiefere Hardware-Zugriffe, aber `WM_POINTER` reicht fuer Pen-Input vollkommen aus.

---

### KeyboardInputClip

Liest Keyboard-Input ueber den Stride `InputManager`.

```csharp
public sealed class KeyboardInputClip : InputClipBase, ISceneLogic
{
    private IChannel<object> _modifiers, _lastKey, _textInput;
    private readonly Dictionary<Keys, IChannel<object>> _keyChannels = new();

    public void OnActivation(IChannelHub hub)
    {
        _modifiers = EnsureChannel(hub, "Keyboard/Modifiers", typeof(KeyModifiers));
        _lastKey   = EnsureChannel(hub, "Keyboard/LastKeyPressed", typeof(Keys));
        _textInput = EnsureChannel(hub, "Keyboard/TextInput", typeof(string));
    }

    public void Update(InputManager input, IChannelHub hub)
    {
        var kb = input.Keyboard;

        // Modifier-Flags
        var mods = KeyModifiers.None;
        if (kb.IsKeyDown(Keys.LeftShift) || kb.IsKeyDown(Keys.RightShift))
            mods |= KeyModifiers.Shift;
        if (kb.IsKeyDown(Keys.LeftCtrl) || kb.IsKeyDown(Keys.RightCtrl))
            mods |= KeyModifiers.Ctrl;
        if (kb.IsKeyDown(Keys.LeftAlt) || kb.IsKeyDown(Keys.RightAlt))
            mods |= KeyModifiers.Alt;
        _modifiers.Object = mods;

        // Per-Key Bang Channels (lazy creation)
        foreach (var key in kb.PressedKeys)
        {
            _lastKey.Object = key;

            string keyPath = $"Keyboard/Key/{key}";
            if (!_keyChannels.TryGetValue(key, out var ch))
            {
                ch = EnsureChannel(hub, keyPath, typeof(bool));
                _keyChannels[key] = ch;
            }
            ch.Object = true;
        }

        // Vorherige Bangs zuruecksetzen
        foreach (var kvp in _keyChannels)
        {
            if (!kb.IsKeyDown(kvp.Key))
                kvp.Value.Object = false;
        }

        // Text-Input (IME-kompatibel)
        // Stride liefert Text-Events separat
    }

    public void OnDeactivation(IChannelHub hub) => RemoveChannels(hub);
}
```

**Verfuegbarkeit:** Stride `InputManager`.

---

### MIDIInputClip

**NICHT in VL.StandardLibs enthalten.** Benoetigt `NAudio` als NuGet-Dependency.

MIDI-Callbacks kommen auf einem **Background-Thread**. Deshalb ist eine `ConcurrentQueue` fuer die Thread-sichere Uebergabe an den Main-Thread noetig.

```csharp
public sealed class MIDIInputClip : InputClipBase, ISceneLogic, IDisposable
{
    // Inspector-Parameter
    public DynamicEnum Device { get; set; }  // DynamicEnum fuer Device-Listing
    public int MidiChannel { get; set; } = 0; // 0 = Omni (alle Kanaele)

    private MidiIn _midiIn;
    private readonly ConcurrentQueue<MidiEvent> _eventQueue = new();

    // Cached channel references
    private readonly Dictionary<string, IChannel<object>> _ccChannels = new();
    private readonly Dictionary<string, IChannel<object>> _noteOnChannels = new();
    private readonly Dictionary<string, IChannel<object>> _noteVelChannels = new();
    private IChannel<object> _pitchBend, _aftertouch, _clockBeat, _clockBpm;

    // Clock tracking
    private int _clockCount;
    private double _lastClockTime;
    private float _currentBpm;

    public void OnActivation(IChannelHub hub)
    {
        string deviceName = Device?.Value ?? "Unknown";

        _pitchBend  = EnsureChannel(hub, $"MIDI/{deviceName}/PitchBend", typeof(float));
        _aftertouch = EnsureChannel(hub, $"MIDI/{deviceName}/Aftertouch", typeof(float));
        _clockBeat  = EnsureChannel(hub, $"MIDI/{deviceName}/Clock/Beat", typeof(bool));
        _clockBpm   = EnsureChannel(hub, $"MIDI/{deviceName}/Clock/BPM", typeof(float));

        // NAudio MIDI-In oeffnen
        int deviceIndex = FindDeviceIndex(deviceName);
        if (deviceIndex >= 0)
        {
            _midiIn = new MidiIn(deviceIndex);
            _midiIn.MessageReceived += OnMidiMessage;  // Background thread!
            _midiIn.Start();
        }
    }

    /// <summary>
    /// MIDI callback — runs on background thread!
    /// Only enqueue, never touch channels here.
    /// </summary>
    private void OnMidiMessage(object sender, MidiInMessageEventArgs e)
    {
        _eventQueue.Enqueue(e.MidiEvent);
    }

    public void Update(IChannelHub hub)
    {
        string deviceName = Device?.Value ?? "Unknown";

        // Reset all Bang channels from previous frame
        ResetBangs();

        // Drain queue on main thread — process all accumulated MIDI events
        while (_eventQueue.TryDequeue(out var evt))
        {
            switch (evt)
            {
                case ControlChangeEvent cc:
                {
                    string path = $"MIDI/{deviceName}/CC/{(int)cc.Controller}";
                    if (!_ccChannels.TryGetValue(path, out var ch))
                    {
                        ch = EnsureChannel(hub, path, typeof(float));
                        _ccChannels[path] = ch;
                    }
                    ch.Object = cc.ControllerValue / 127f;
                    break;
                }

                case NoteOnEvent noteOn:
                {
                    int noteNum = noteOn.NoteNumber;
                    string onPath  = $"MIDI/{deviceName}/Note/{noteNum}/On";
                    string velPath = $"MIDI/{deviceName}/Note/{noteNum}/Velocity";

                    if (!_noteOnChannels.TryGetValue(onPath, out var onCh))
                    {
                        onCh = EnsureChannel(hub, onPath, typeof(bool));
                        _noteOnChannels[onPath] = onCh;
                    }
                    if (!_noteVelChannels.TryGetValue(velPath, out var velCh))
                    {
                        velCh = EnsureChannel(hub, velPath, typeof(float));
                        _noteVelChannels[velPath] = velCh;
                    }

                    onCh.Object  = true;  // Bang
                    velCh.Object = noteOn.Velocity / 127f;
                    break;
                }

                case PitchWheelChangeEvent pw:
                    _pitchBend.Object = (pw.Pitch - 8192) / 8192f; // -1..1
                    break;

                case ChannelAfterTouchEvent at:
                    _aftertouch.Object = at.AfterTouchPressure / 127f;
                    break;

                case MidiEvent when evt.CommandCode == MidiCommandCode.TimingClock:
                    ProcessMidiClock();
                    break;
            }
        }
    }

    private void ProcessMidiClock()
    {
        _clockCount++;
        if (_clockCount >= 24) // 24 PPQN = 1 Beat
        {
            _clockBeat.Object = true;  // Bang
            _clockCount = 0;

            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            if (_lastClockTime > 0)
            {
                double beatDuration = now - _lastClockTime;
                _currentBpm = (float)(60.0 / beatDuration);
                _clockBpm.Object = _currentBpm;
            }
            _lastClockTime = now;
        }
    }

    private void ResetBangs()
    {
        _clockBeat.Object = false;
        foreach (var ch in _noteOnChannels.Values)
            ch.Object = false;
    }

    public void OnDeactivation(IChannelHub hub)
    {
        _midiIn?.Stop();
        _midiIn?.Dispose();
        _midiIn = null;
        RemoveChannels(hub);
    }

    public void Dispose() => _midiIn?.Dispose();

    private static int FindDeviceIndex(string name)
    {
        for (int i = 0; i < MidiIn.NumberOfDevices; i++)
        {
            if (MidiIn.DeviceInfo(i).ProductName == name)
                return i;
        }
        return -1;
    }
}
```

**Thread-Safety-Modell:**

```
[MIDI-Thread]                    [Main-Thread (Update)]
     │                                  │
     ├── OnMidiMessage()                │
     │   └── _eventQueue.Enqueue()      │
     │                                  ├── _eventQueue.TryDequeue() (loop)
     │                                  ├── Process event
     │                                  └── Write to cached channels
```

Die `ConcurrentQueue<MidiEvent>` ist der einzige Synchronisationspunkt. Kein Locking im Hot-Path.

**DynamicEnum fuer Device-Listing:**

```csharp
public class MidiInputDeviceEnum : DynamicEnumBase<MidiInputDeviceEnum, MidiInputDeviceEnumDefinition>
{
    // VL sieht das als Dropdown im Inspector
    // Aktualisiert sich automatisch bei Device-Aenderungen
}
```

---

### OSCInputClip

**NICHT in VL.StandardLibs enthalten.** Benoetigt `OscCore` oder `Rug.Osc` als NuGet-Dependency.

OSC-Adressen werden **direkt** zu Channel-Pfaden. Das ist moeglich, weil OSC-Adressen bereits hierarchisch strukturiert sind (`/sensor/temperature` → `OSC/sensor/temperature`).

```csharp
public sealed class OSCInputClip : InputClipBase, ISceneLogic, IDisposable
{
    // Inspector-Parameter
    public int Port { get; set; } = 9000;

    private OscServer _server;
    private readonly ConcurrentQueue<(string Address, object Value)> _messageQueue = new();

    public void OnActivation(IChannelHub hub)
    {
        _server = new OscServer(Port);
        _server.MessageReceived += (address, args) =>
        {
            // Background thread! Nur enqueuen.
            object value = args.Length == 1 ? args[0] : args;
            _messageQueue.Enqueue((address, value));
        };
        _server.Start();
    }

    public void Update(IChannelHub hub)
    {
        while (_messageQueue.TryDequeue(out var msg))
        {
            // OSC-Adresse → Channel-Pfad: "/sensor/temp" → "OSC/sensor/temp"
            string channelPath = "OSC" + msg.Address.Replace('/', '/');
            // In practice: "OSC/sensor/temp"

            var channel = EnsureChannel(hub, channelPath, msg.Value.GetType());
            channel.Object = msg.Value;
        }
    }

    public void OnDeactivation(IChannelHub hub)
    {
        _server?.Stop();
        _server?.Dispose();
        RemoveChannels(hub);
    }

    public void Dispose() => _server?.Dispose();
}
```

**Hinweis:** OSC-Channels werden **dynamisch** erstellt, weil OSC-Adressen nicht im Voraus bekannt sind. `EnsureChannel` ist idempotent — bei wiederholten Nachrichten an dieselbe Adresse wird der gecachte Channel verwendet.

---

### AudioInputClip

Audio-Analyse: Level, Peak, Beat-Detection, FFT-Baender.

```csharp
public sealed class AudioInputClip : InputClipBase, ISceneLogic
{
    // Inspector-Parameter
    public float BeatThreshold { get; set; } = 0.6f;
    public int FFTSize { get; set; } = 1024;
    public float Smoothing { get; set; } = 0.8f;

    private IChannel<object> _level, _peak, _beat, _bpm;
    private IChannel<object> _fftBands, _low, _mid, _high, _onset;

    // Analysis state
    private float[] _fftBuffer;
    private float _smoothedLevel;
    private BeatDetector _beatDetector;

    public void OnActivation(IChannelHub hub)
    {
        _level    = EnsureChannel(hub, "Audio/Level", typeof(float));
        _peak     = EnsureChannel(hub, "Audio/Peak", typeof(float));
        _beat     = EnsureChannel(hub, "Audio/Beat", typeof(bool));
        _bpm      = EnsureChannel(hub, "Audio/BPM", typeof(float));
        _fftBands = EnsureChannel(hub, "Audio/FFT/Bands", typeof(float[]));
        _low      = EnsureChannel(hub, "Audio/Low", typeof(float));
        _mid      = EnsureChannel(hub, "Audio/Mid", typeof(float));
        _high     = EnsureChannel(hub, "Audio/High", typeof(float));
        _onset    = EnsureChannel(hub, "Audio/Onset", typeof(bool));

        _fftBuffer = new float[FFTSize];
        _beatDetector = new BeatDetector(BeatThreshold);
    }

    public void Update(AudioAnalyzer analyzer)
    {
        analyzer.GetFFT(_fftBuffer);

        float rms = analyzer.RMSLevel;
        _smoothedLevel = MathF.Lerp(_smoothedLevel, rms, 1f - Smoothing);

        _level.Object    = _smoothedLevel;
        _peak.Object     = analyzer.PeakLevel;
        _fftBands.Object = _fftBuffer;

        // Frequenzband-Aufteilung (typisch: 3-Band Split)
        _low.Object  = AverageBand(_fftBuffer, 0, FFTSize / 8);        // ~0-2kHz
        _mid.Object  = AverageBand(_fftBuffer, FFTSize / 8, FFTSize / 2); // ~2-8kHz
        _high.Object = AverageBand(_fftBuffer, FFTSize / 2, FFTSize);  // ~8-20kHz

        // Beat-Detection
        bool isBeat = _beatDetector.Process(_smoothedLevel);
        _beat.Object  = isBeat;
        _bpm.Object   = _beatDetector.CurrentBPM;
        _onset.Object = _beatDetector.IsOnset;
    }

    public void OnDeactivation(IChannelHub hub) => RemoveChannels(hub);

    private static float AverageBand(float[] fft, int from, int to)
    {
        float sum = 0;
        for (int i = from; i < to && i < fft.Length; i++)
            sum += fft[i];
        return sum / (to - from);
    }
}
```

---

### GamepadInputClip

Liest Gamepad-Input. Unterstuetzt mehrere Gamepads ueber Index-Parameter.

```csharp
public sealed class GamepadInputClip : InputClipBase, ISceneLogic
{
    // Inspector-Parameter
    public int GamepadIndex { get; set; } = 0;
    public float DeadZone { get; set; } = 0.15f;

    private IChannel<object> _leftStick, _rightStick;
    private IChannel<object> _leftTrigger, _rightTrigger;
    private IChannel<object> _a, _b, _x, _y, _dpad;
    private IChannel<object> _leftShoulder, _rightShoulder;

    public void OnActivation(IChannelHub hub)
    {
        string prefix = $"Gamepad/{GamepadIndex}";

        _leftStick     = EnsureChannel(hub, $"{prefix}/LeftStick", typeof(Vector2));
        _rightStick    = EnsureChannel(hub, $"{prefix}/RightStick", typeof(Vector2));
        _leftTrigger   = EnsureChannel(hub, $"{prefix}/LeftTrigger", typeof(float));
        _rightTrigger  = EnsureChannel(hub, $"{prefix}/RightTrigger", typeof(float));
        _a             = EnsureChannel(hub, $"{prefix}/A", typeof(bool));
        _b             = EnsureChannel(hub, $"{prefix}/B", typeof(bool));
        _x             = EnsureChannel(hub, $"{prefix}/X", typeof(bool));
        _y             = EnsureChannel(hub, $"{prefix}/Y", typeof(bool));
        _dpad          = EnsureChannel(hub, $"{prefix}/DPad", typeof(Vector2));
        _leftShoulder  = EnsureChannel(hub, $"{prefix}/LeftShoulder", typeof(bool));
        _rightShoulder = EnsureChannel(hub, $"{prefix}/RightShoulder", typeof(bool));
    }

    public void Update(InputManager input)
    {
        var gamepad = input.GamePadByIndex(GamepadIndex);
        if (gamepad == null) return;

        _leftStick.Object     = ApplyDeadZone(gamepad.LeftThumb, DeadZone);
        _rightStick.Object    = ApplyDeadZone(gamepad.RightThumb, DeadZone);
        _leftTrigger.Object   = gamepad.LeftTrigger;
        _rightTrigger.Object  = gamepad.RightTrigger;
        _a.Object             = gamepad.IsButtonDown(GamePadButton.A);
        _b.Object             = gamepad.IsButtonDown(GamePadButton.B);
        _x.Object             = gamepad.IsButtonDown(GamePadButton.X);
        _y.Object             = gamepad.IsButtonDown(GamePadButton.Y);
        _leftShoulder.Object  = gamepad.IsButtonDown(GamePadButton.LeftShoulder);
        _rightShoulder.Object = gamepad.IsButtonDown(GamePadButton.RightShoulder);
    }

    public void OnDeactivation(IChannelHub hub) => RemoveChannels(hub);

    private static Vector2 ApplyDeadZone(Vector2 stick, float deadZone)
    {
        float mag = stick.Length();
        if (mag < deadZone) return Vector2.Zero;
        return stick * ((mag - deadZone) / (1f - deadZone) / mag);
    }
}
```

**Verfuegbarkeit:** Stride `InputManager` oder direkt XInput. Stride abstrahiert bereits.

---

### CameraInputClip

Nutzt `VL.Video` (existiert in VL.StandardLibs). Besonderheit: Kamera ist gleichzeitig **Input-Provider** (Channels) und **Texture-Generator** (Output).

```csharp
public sealed class CameraInputClip : InputClipBase, ITextureGenerator
{
    // Inspector-Parameter
    public DynamicEnum Device { get; set; }  // VideoCaptureDeviceEnum
    public Vector2 Resolution { get; set; } = new(1920, 1080);

    private IChannel<object> _frame, _resolution;
    private VideoCapture _capture;

    public void OnActivation(IChannelHub hub)
    {
        string deviceName = Device?.Value ?? "Default";
        _frame      = EnsureChannel(hub, $"Camera/{deviceName}/Frame", typeof(Texture));
        _resolution = EnsureChannel(hub, $"Camera/{deviceName}/Resolution", typeof(Vector2));

        _capture = new VideoCapture(deviceName, (int)Resolution.X, (int)Resolution.Y);
    }

    public void Update(out Texture Output)
    {
        var texture = _capture.GrabFrame();

        // Channel schreiben (fuer andere Clips die das Bild lesen wollen)
        _frame.Object      = texture;
        _resolution.Object = new Vector2(texture.Width, texture.Height);

        // Texture Output (normaler ITextureGenerator-Pfad)
        Output = texture;
    }

    public void OnDeactivation(IChannelHub hub)
    {
        _capture?.Dispose();
        RemoveChannels(hub);
    }
}
```

**Doppelrolle:** Der `CameraInputClip` ist sowohl `InputClipBase` (schreibt Channels) als auch `ITextureGenerator` (liefert Texture-Output in die Stream-Routing-Pipeline). Andere Clips koennen entweder den Channel `Camera/{Device}/Frame` lesen oder die Texture direkt ueber Stream-Routing empfangen.

**Device-Enumeration:** `VideoCaptureDeviceEnum` aus VL.Video liefert die verfuegbaren Kameras als DynamicEnum fuer den Inspector.

---

## Spatial Input → Hit-Testing

### Erweiterter InputRoutingPass

Der `InputRoutingPass` aus `08-LAYOUT-UND-INPUT.md` wird erweitert, um mehrere Input-Quellen (Maus, Touch, Pen) und Multi-Touch zu unterstuetzen.

#### InputSource Enum

```csharp
public enum InputSource : byte
{
    Mouse,
    Touch,
    Pen
}
```

#### Erweiterte InputHit Component

```csharp
[SceneComponent(Transient = true, FlatStorage = true)]
public partial record InputHit(
    bool IsHit,              // Irgendein Pointer in meinem Bereich
    bool IsDirectHit,        // Ich bin das tiefste getroffene Element
    bool IsChildHit,         // Ein Kind wurde getroffen
    string? HitChildId,      // Welches Kind
    Vector2 LocalPosition,   // Position relativ zu meinen Bounds
    InputSource Source,      // Welche Input-Quelle (Mouse, Touch, Pen)
    int TouchId,             // Touch-ID bei Multi-Touch (-1 fuer Mouse/Pen)
    float Pressure);         // Druck (0..1, nur bei Touch/Pen mit Hardware-Support)
```

#### InputRoutingPass — Multi-Source Hit-Testing

```csharp
public static class InputRoutingPass
{
    public static void Execute(
        FlatSceneGraph flat,
        IChannelHub hub,
        ReadOnlySpan<InputPointer> pointers)
    {
        // Fuer jeden aktiven Pointer (Mouse, Touch-Finger, Pen)
        foreach (var pointer in pointers)
        {
            string? directHitId = null;

            // Bottom-up: Blaetter zuerst
            for (int i = flat.NodeCount - 1; i >= 0; i--)
            {
                // Opt-out check: InputConfig.IsHitTestable
                var inputConfig = flat.GetComponent<InputConfig>(i);
                if (inputConfig != null && !inputConfig.IsHitTestable)
                    continue;

                var layout = flat.GetTransientComponent<ComputedLayout>(i);
                if (layout == null) continue;

                bool containsPoint = layout.GlobalBounds.Contains(pointer.Position);

                if (containsPoint && directHitId == null)
                {
                    directHitId = flat.Handles[i].NodeId;
                    flat.SetTransientComponent(i, new InputHit(
                        IsHit: true,
                        IsDirectHit: true,
                        IsChildHit: false,
                        HitChildId: null,
                        LocalPosition: pointer.Position - layout.GlobalBounds.Location,
                        Source: pointer.Source,
                        TouchId: pointer.TouchId,
                        Pressure: pointer.Pressure));
                }
                else if (containsPoint)
                {
                    flat.SetTransientComponent(i, new InputHit(
                        IsHit: true,
                        IsDirectHit: false,
                        IsChildHit: true,
                        HitChildId: directHitId,
                        LocalPosition: pointer.Position - layout.GlobalBounds.Location,
                        Source: pointer.Source,
                        TouchId: pointer.TouchId,
                        Pressure: pointer.Pressure));
                }
            }
        }
    }
}
```

#### InputPointer — Vereinheitlichte Pointer-Daten

```csharp
public readonly record struct InputPointer(
    Vector2 Position,
    InputSource Source,
    int TouchId,      // -1 fuer Mouse/Pen
    float Pressure);  // 0..1
```

Der Pass liest die Pointer-Positionen aus den Channels:

```csharp
// Vor dem Pass: Pointer aus Channels sammeln
var pointers = new List<InputPointer>();

// Mouse
if (hub.TryGetChannel("Mouse/Position", out var mousePos))
{
    pointers.Add(new InputPointer(
        (Vector2)mousePos.Object,
        InputSource.Mouse,
        TouchId: -1,
        Pressure: 1f));
}

// Touch (alle aktiven Finger)
if (hub.TryGetChannel("Touch/Count", out var touchCount))
{
    int count = (int)touchCount.Object;
    for (int i = 0; i < count; i++)
    {
        if (hub.TryGetChannel($"Touch/{i}/Position", out var touchPos))
        {
            float pressure = 1f;
            if (hub.TryGetChannel($"Touch/{i}/Pressure", out var touchPressure))
                pressure = (float)touchPressure.Object;

            pointers.Add(new InputPointer(
                (Vector2)touchPos.Object,
                InputSource.Touch,
                TouchId: i,
                Pressure: pressure));
        }
    }
}

// Pen
if (hub.TryGetChannel("Pen/Position", out var penPos) &&
    hub.TryGetChannel("Pen/IsDown", out var penDown) &&
    (bool)penDown.Object)
{
    float pressure = 0.5f;
    if (hub.TryGetChannel("Pen/Pressure", out var penPressure))
        pressure = (float)penPressure.Object;

    pointers.Add(new InputPointer(
        (Vector2)penPos.Object,
        InputSource.Pen,
        TouchId: -1,
        Pressure: pressure));
}
```

### InputConfig Component — Opt-Out

```csharp
[SceneComponent]
public partial record InputConfig(
    bool IsHitTestable = true);  // false = Node wird vom Hit-Testing uebersprungen
```

Nodes ohne `InputConfig` sind standardmaessig hit-testable (sofern sie `ComputedLayout` haben). `InputConfig(IsHitTestable: false)` schaltet Hit-Testing explizit ab, z.B. fuer dekorative Overlay-Elemente die Klicks durchreichen sollen.

---

## Focus-System

Das Focus-System bestimmt, welcher Node Keyboard-Eingaben empfaengt. Nur ein Node hat gleichzeitig Focus.

### Focus-Channel

```
Focus/CurrentNodeId       : string        — ID des aktuell fokussierten Nodes
```

### FocusState (Transient Component)

```csharp
[SceneComponent(Transient = true, FlatStorage = true)]
public partial record FocusState(
    bool IsFocused,         // Dieser Node hat aktuell Focus
    bool HasFocusWithin);   // Ein Kind dieses Nodes hat Focus
```

### FocusPass

```csharp
public static class FocusPass
{
    public static void Execute(FlatSceneGraph flat, IChannelHub hub)
    {
        string focusedId = null;

        // 1. Klick setzt Focus
        for (int i = 0; i < flat.NodeCount; i++)
        {
            var hit = flat.GetTransientComponent<InputHit>(i);
            if (hit is { IsDirectHit: true, Source: InputSource.Mouse or InputSource.Touch })
            {
                focusedId = flat.Handles[i].NodeId;
                break;
            }
        }

        // 2. Tab navigiert zum naechsten fokussierbaren Node
        if (hub.TryGetChannel("Keyboard/Key/Tab", out var tabCh) && (bool)tabCh.Object)
        {
            focusedId = GetNextFocusableNode(flat, focusedId);
        }

        // 3. Focus-Channel aktualisieren
        if (focusedId != null)
        {
            var ch = hub.TryGetChannel("Focus/CurrentNodeId") ??
                     hub.TryAddChannel("Focus/CurrentNodeId", typeof(string));
            ch.Object = focusedId;
        }

        // 4. FocusState auf alle Nodes schreiben
        string currentFocus = null;
        if (hub.TryGetChannel("Focus/CurrentNodeId", out var focusCh))
            currentFocus = focusCh.Object as string;

        for (int i = 0; i < flat.NodeCount; i++)
        {
            string nodeId = flat.Handles[i].NodeId;
            bool isFocused = nodeId == currentFocus;
            bool hasFocusWithin = false;

            // Pruefe ob ein Kind fokussiert ist
            if (!isFocused && currentFocus != null)
            {
                int childStart = flat.FirstChildIndex[i];
                int childEnd = childStart + flat.ChildCount[i];
                for (int c = childStart; c < childEnd; c++)
                {
                    if (flat.Handles[c].NodeId == currentFocus)
                    {
                        hasFocusWithin = true;
                        break;
                    }
                }
            }

            flat.SetTransientComponent(i, new FocusState(isFocused, hasFocusWithin));
        }
    }

    private static string GetNextFocusableNode(FlatSceneGraph flat, string currentId)
    {
        // Tab-Order: Tree-Order (BFS) als Default
        // Nodes mit InputConfig(IsHitTestable: true) und ComputedLayout sind fokussierbar
        bool foundCurrent = currentId == null;

        for (int i = 0; i < flat.NodeCount; i++)
        {
            if (!foundCurrent)
            {
                if (flat.Handles[i].NodeId == currentId)
                    foundCurrent = true;
                continue;
            }

            if (flat.HasComponent<ComputedLayout>(i))
            {
                var config = flat.GetComponent<InputConfig>(i);
                if (config == null || config.IsHitTestable)
                    return flat.Handles[i].NodeId;
            }
        }

        // Wrap-around: zurueck zum ersten fokussierbaren Node
        for (int i = 0; i < flat.NodeCount; i++)
        {
            if (flat.HasComponent<ComputedLayout>(i))
            {
                var config = flat.GetComponent<InputConfig>(i);
                if (config == null || config.IsHitTestable)
                    return flat.Handles[i].NodeId;
            }
        }

        return null;
    }
}
```

**Focus-Regeln:**

1. **Klick setzt Focus:** Ein `IsDirectHit` von Mouse oder Touch setzt den Focus auf den getroffenen Node.
2. **Tab navigiert:** Tab wechselt zum naechsten fokussierbaren Node in BFS-Reihenfolge (Tree-Order).
3. **Keyboard geht an fokussierten Node:** Clips lesen `FocusState.IsFocused` aus `ISceneContext` und verarbeiten nur dann Keyboard-Input.
4. **HasFocusWithin:** Container-Nodes wissen, ob ein Kind fokussiert ist (fuer visuelle Indikatoren).

---

## InputMapping — MIDI/OSC → Parameter

Das InputMapping-System verbindet beliebige Channel-Werte mit Clip-Parametern. Es ermoeglicht MIDI-Controller-Mapping, OSC-Steuerung und jede andere Channel-basierte Parameter-Kontrolle.

### InputMapping Component

```csharp
[SceneComponent]
public partial record InputMapping(
    ImmutableArray<InputMapEntry> Entries);
```

### InputMapEntry

```csharp
public record InputMapEntry(
    string ChannelPath,         // z.B. "MIDI/nanoKONTROL/CC/1"
    string TargetParameter,     // z.B. "Intensity" (Pin-Name des Clips)
    InputMapMode Mode,          // Wie wird der Wert angewendet
    float RangeMin,             // Input-Range remapping: Min (Default 0)
    float RangeMax,             // Input-Range remapping: Max (Default 1)
    float TargetMin,            // Output-Range: Min
    float TargetMax,            // Output-Range: Max
    float Smoothing);           // Glaettung (0 = keine, 0.99 = stark)
```

### InputMapMode

```csharp
public enum InputMapMode : byte
{
    Direct,     // Wert direkt setzen (nach Remap)
    Add,        // Zum aktuellen Wert addieren
    Multiply,   // Mit aktuellem Wert multiplizieren
    Toggle,     // Bei Schwellenwert > 0.5: Toggle bool
    Trigger,    // Bei Schwellenwert > 0.5: einmaliger Bang
    Remap       // Voller Remap mit Min/Max auf beiden Seiten
}
```

### InputMappingPass

Laeuft nach dem InputRoutingPass. Liest Channel-Werte und schreibt sie als Parameter-Overrides auf die Clip-Instanzen.

```csharp
public static class InputMappingPass
{
    public static void Execute(FlatSceneGraph flat, IChannelHub hub)
    {
        for (int i = 0; i < flat.NodeCount; i++)
        {
            var mapping = flat.GetComponent<InputMapping>(i);
            if (mapping == null) continue;

            foreach (var entry in mapping.Entries)
            {
                if (!hub.TryGetChannel(entry.ChannelPath, out var channel))
                    continue;

                float rawValue = Convert.ToSingle(channel.Object);

                float mapped = entry.Mode switch
                {
                    InputMapMode.Direct   => Remap(rawValue, entry),
                    InputMapMode.Add      => GetCurrentValue(flat, i, entry.TargetParameter) +
                                             Remap(rawValue, entry),
                    InputMapMode.Multiply => GetCurrentValue(flat, i, entry.TargetParameter) *
                                             Remap(rawValue, entry),
                    InputMapMode.Toggle   => rawValue > 0.5f
                                             ? 1f - GetCurrentValue(flat, i, entry.TargetParameter)
                                             : GetCurrentValue(flat, i, entry.TargetParameter),
                    InputMapMode.Trigger  => rawValue > 0.5f ? 1f : 0f,
                    InputMapMode.Remap    => RemapFull(rawValue, entry),
                    _ => rawValue
                };

                // Smoothing
                if (entry.Smoothing > 0f)
                {
                    float current = GetCurrentValue(flat, i, entry.TargetParameter);
                    mapped = current + (mapped - current) * (1f - entry.Smoothing);
                }

                SetParameterOverride(flat, i, entry.TargetParameter, mapped);
            }
        }
    }

    private static float Remap(float value, InputMapEntry entry)
    {
        float normalized = (value - entry.RangeMin) / (entry.RangeMax - entry.RangeMin);
        normalized = Math.Clamp(normalized, 0f, 1f);
        return entry.TargetMin + normalized * (entry.TargetMax - entry.TargetMin);
    }

    private static float RemapFull(float value, InputMapEntry entry)
    {
        return Remap(value, entry); // Same as Direct, but semantically distinct
    }
}
```

### MIDI-Learn

MIDI-Learn ermoeglicht das interaktive Zuweisen von MIDI-Controllern zu Clip-Parametern direkt im Inspector.

**Ablauf:**

```
1. User klickt [M]-Button neben einem Parameter im Inspector
2. System wechselt in "MIDI-Learn-Modus" fuer diesen Parameter
3. Inspector zeigt "Waiting for MIDI..." Indikator
4. User bewegt einen Fader/Knob am MIDI-Controller
5. System erkennt: MIDI/nanoKONTROL/CC/7 hat sich geaendert
6. System erstellt InputMapEntry:
   - ChannelPath: "MIDI/nanoKONTROL/CC/7"
   - TargetParameter: "Intensity"
   - Mode: Direct
   - Range: 0..1 → 0..1
7. InputMapEntry wird ueber SceneEdit + UndoStack auf den Node geschrieben
8. Mapping ist sofort aktiv UND undo-bar!
```

```csharp
public sealed class MidiLearnService
{
    private string _targetNodeId;
    private string _targetParameter;
    private bool _isListening;

    // Vorherige CC-Werte speichern um Aenderung zu erkennen
    private readonly Dictionary<string, float> _lastCCValues = new();

    public void StartLearning(string nodeId, string parameterName)
    {
        _targetNodeId = nodeId;
        _targetParameter = parameterName;
        _isListening = true;

        // Snapshot aller aktuellen CC-Werte
        SnapshotCurrentCCValues();
    }

    /// <summary>
    /// Called per frame while learning. Checks all MIDI CC channels for changes.
    /// </summary>
    public SceneEdit? Update(IChannelHub hub)
    {
        if (!_isListening) return null;

        // Alle MIDI CC-Channels durchsuchen
        foreach (var channel in hub.EnumerateChannels("MIDI/*/CC/*"))
        {
            float current = Convert.ToSingle(channel.Object);

            if (_lastCCValues.TryGetValue(channel.Path, out float last) &&
                MathF.Abs(current - last) > 0.05f)  // Signifikante Aenderung
            {
                _isListening = false;

                // SceneEdit erzeugen → geht ueber UndoStack → undo-bar!
                var entry = new InputMapEntry(
                    ChannelPath: channel.Path,
                    TargetParameter: _targetParameter,
                    Mode: InputMapMode.Direct,
                    RangeMin: 0f, RangeMax: 1f,
                    TargetMin: 0f, TargetMax: 1f,
                    Smoothing: 0.1f);

                return SceneEdit.AddInputMapping(_targetNodeId, entry);
            }

            _lastCCValues[channel.Path] = current;
        }

        return null;
    }

    public void Cancel() => _isListening = false;
}
```

**Wichtig:** Das Mapping wird als `SceneEdit` erzeugt und laeuft ueber den normalen `UndoStack`. Dadurch ist MIDI-Learn vollstaendig undo-bar — der User kann Ctrl+Z druecken und das Mapping verschwindet.

---

## Device-Konfiguration in kompilierten Apps

Da alle Input-Provider Clips sind, ist ihre Konfiguration (Device-Auswahl, Port, Channel, etc.) in normalen Clip-Parametern gespeichert. Diese Parameter sind `SceneEdits` und funktionieren identisch in Editor und Runtime.

### Runtime-Device-Auswahl

```
[Show]
  [Settings]                        ← ISceneLogic, Runtime-Panel
    [MIDIInput "Controller"]        ← MIDIInputClip
      Device: "nanoKONTROL2"        ← DynamicEnum, aenderbar zur Runtime
      MidiChannel: 0
    [OSCInput "Sensors"]            ← OSCInputClip
      Port: 9000                    ← aenderbar zur Runtime
    [CameraInput "Webcam"]          ← CameraInputClip
      Device: "Logitech C920"      ← DynamicEnum, aenderbar zur Runtime
      Resolution: (1920, 1080)
```

### DeviceSettingsPanel

Fuer kompilierte Apps (ohne vvvv-Editor) kann ein `DeviceSettingsPanel` als `ISceneElement` in die Szene eingebaut werden:

```csharp
public sealed class DeviceSettingsPanel : ISceneElement
{
    public void Update(
        out Texture Output,
        ISceneContext context,
        ImGuiContext imgui)
    {
        // ImGui-Panel zeigt alle Input-Clip-Parameter
        // Aenderungen erzeugen SceneEdits → Runtime-konfigurierbar
        imgui.Begin("Device Settings");

        foreach (var inputNode in context.FindNodesByInterface<InputClipBase>())
        {
            var parameters = context.GetParameters(inputNode);
            foreach (var param in parameters)
            {
                imgui.DrawParameter(param); // DynamicEnum → Dropdown
            }
        }

        imgui.End();
    }
}
```

### Settings aus JSON laden

```json
{
  "devices": {
    "Controller": { "Device": "nanoKONTROL2", "MidiChannel": 1 },
    "Sensors":    { "Port": 8000 },
    "Webcam":     { "Device": "HD Pro Webcam C920", "Resolution": [1280, 720] }
  }
}
```

Diese JSON-Werte werden beim App-Start als `SceneEdits` auf die entsprechenden Clip-Nodes angewendet. Kein separates Config-System noetig.

---

## Dependencies

Die Input-Clips haben unterschiedliche Dependencies. Um die Paketgroesse minimal zu halten, werden sie in separate Packages aufgeteilt:

### Option A: Einzelnes IO-Package

```
VL.SceneGraph.IO               → NAudio (MIDI), OscCore (OSC), WM_POINTER (Pen)
```

### Option B: Granulare Packages (bevorzugt)

```
VL.SceneGraph                   → Keine I/O-Dependencies (Kern)
VL.SceneGraph.Input.MIDI        → NAudio
VL.SceneGraph.Input.OSC         → OscCore
VL.SceneGraph.Input.Pen         → Custom WM_POINTER wrapper (kein NuGet, eigener Code)
VL.SceneGraph.Input.Audio       → NAudio (oder Stride.Audio)
```

### Bereits in VL.StandardLibs enthalten (keine zusaetzliche Dependency)

| Input-Clip | Source |
|-----------|--------|
| MouseInputClip | Stride InputManager |
| TouchInputClip | Stride InputManager |
| KeyboardInputClip | Stride InputManager |
| GamepadInputClip | Stride InputManager |
| CameraInputClip | VL.Video |

### Custom / externe Dependency noetig

| Input-Clip | Dependency | Grund |
|-----------|-----------|-------|
| MIDIInputClip | NAudio | MIDI-In nicht in Stride |
| OSCInputClip | OscCore oder Rug.Osc | OSC nicht in Stride |
| PenInputClip | Custom WM_POINTER | Pen-spezifische Daten (Pressure, Tilt) nicht in Stride InputManager |
| AudioInputClip | NAudio oder Stride.Audio | FFT-Analyse, Beat-Detection |

---

## Pipeline-Integration

Die Input-Passes fuegen sich in die bestehende 14-Pass-Pipeline ein. Die Phasen 3 bis 3.7 bilden den Input-Block:

```
Frame-Pipeline:
  Pass  1:   SyncStructure         → Compile tree → flat arrays
  Pass  2:   TimePass              → Time propagation (Top-Down)
  Pass  3:   InputProviders        → Input-Clips schreiben Channels
  Pass  3.5: InputRoutingPass      → Hit-Testing → InputHit (Transient)
  Pass  3.6: InputMappingPass      → MIDI/OSC → Parameter-Overrides
  Pass  3.7: FocusPass             → Focus-Updates → FocusState (Transient)
  Pass  4:   RecordingPlayback     → Recording-Daten abspielen
  Pass  5:   StateMachinePass      → FSM evaluieren
  Pass  6:   ActivityPass          → Fade-in/out, ControlMode
  Pass  7:   TimelinePass          → Keyframes → Parameter
  Pass  8:   ConstraintSolver      → Constraints aufloesen
  Pass  8.5: LayoutPass            → ComputedLayout (Transient)
  Pass  8.6: InputRoutingPass      → Hit-Testing (nach Layout!)
  Pass  9:   StreamRouting         → Texture/Audio-Routing
  Pass 10:   ClipEvaluation        → Clips auswerten
  Pass 11:   RecordingCapture      → Recording aufnehmen
  Pass 12:   3D Passes (optional)  → Transform, Bounds, Visibility
  Pass 13:   ActivityLogger        → Timeline-UI Daten
  Pass 14:   DirtyClear + Swap     → Buffer-Swap
```

**Hinweis zur Reihenfolge:** Hit-Testing passiert zweimal:

1. **Pass 3.5 (frueh):** Mit der Position des letzten Frames (fuer sofortige Button-Reaktionen)
2. **Pass 8.6 (nach Layout):** Mit den aktuellen `ComputedLayout`-Bounds (fuer praezises Hit-Testing)

Input-Clips (Pass 3) laufen **vor** StateMachine und Activity, damit FSM-Transitions auf Input reagieren koennen (z.B. "bei MIDI Note On → naechster State").

---

## Vollstaendiger Show-Graph mit I/O

Ein komplettes Beispiel das zeigt, wie Inputs, Content, Mapping und Outputs als Clips im selben Baum koexistieren:

```
[Show "Kopffarben Performance"]
  │
  ├── [Settings]                           ← ISceneLogic (Runtime-Panel)
  │     ├── [MIDIInput "Controller"]       ← MIDIInputClip
  │     │     Device: "nanoKONTROL2"
  │     │     MidiChannel: 0
  │     ├── [OSCInput "Tracking"]          ← OSCInputClip
  │     │     Port: 7000
  │     ├── [AudioInput "Music"]           ← AudioInputClip
  │     │     BeatThreshold: 0.6
  │     ├── [MouseInput]                   ← MouseInputClip
  │     ├── [PenInput "Wacom"]             ← PenInputClip
  │     └── [CameraInput "Webcam"]         ← CameraInputClip
  │           Device: "Logitech C920"
  │
  ├── [Layer "Background"]                 ← ITextureCompositor
  │     ├── [Clip "Gradient" : GradientGen]     ← ITextureGenerator
  │     │     Color1: #001122
  │     │     Color2: #334455
  │     │     InputMapping:
  │     │       CC/1 → Color1.Hue (Direct, 0..1 → 0..360)
  │     │       CC/2 → Color2.Hue (Direct, 0..1 → 0..360)
  │     │
  │     └── [Clip "Particles" : ParticleGen]    ← ITextureGenerator
  │           Count: 5000
  │           InputMapping:
  │             Audio/Beat  → Emit (Trigger)
  │             Audio/Low   → Size (Multiply, 0..1 → 0.5..2.0)
  │             Audio/BPM   → Speed (Direct, Smoothing: 0.9)
  │
  ├── [Layer "Drawing"]                    ← ITextureCompositor
  │     └── [Clip "PenDraw" : DrawingClip]      ← ITextureGenerator
  │           InputMapping:
  │             Pen/Position → Position (Direct)
  │             Pen/Pressure → BrushSize (Remap, 0..1 → 2..50)
  │             Pen/Tilt     → BrushAngle (Direct)
  │
  ├── [Layer "Camera"]                     ← ITextureCompositor
  │     ├── [Clip "WebcamFeed" : CameraInputClip]  ← ITextureGenerator + InputProvider
  │     └── [Clip "ChromaKey" : ChromaKeyFX]        ← ITextureEffect
  │           KeyColor: #00FF00
  │           InputMapping:
  │             CC/3 → Threshold (Direct, 0..1 → 0..0.5)
  │
  ├── [Layer "PostFX"]                     ← ITextureCompositor
  │     ├── [Clip "Bloom" : BloomFX]       ← ITextureEffect
  │     │     InputMapping:
  │     │       CC/4 → Intensity (Direct, Smoothing: 0.5)
  │     │
  │     └── [Clip "Warp" : ProjectionWarp] ← ITextureEffect
  │           InputMapping:
  │             OSC/tracker/1/position → WarpCenter (Direct)
  │
  └── [Output "Projector1"]               ← OutputSink (siehe 09)
        Resolution: (1920, 1080)
        Fullscreen: true
        Monitor: 2
```

**Datenfluss in einem Frame:**

```
1. Input-Clips schreiben Channels:
   MIDIInput   → MIDI/nanoKONTROL2/CC/1..4
   AudioInput  → Audio/Beat, Audio/Low, Audio/BPM
   PenInput    → Pen/Position, Pen/Pressure, Pen/Tilt
   OSCInput    → OSC/tracker/1/position
   CameraInput → Camera/Logitech C920/Frame

2. InputMappingPass mapped Channels → Parameter:
   CC/1          → Gradient.Color1.Hue
   CC/2          → Gradient.Color2.Hue
   Audio/Beat    → Particles.Emit (Trigger)
   Pen/Pressure  → PenDraw.BrushSize (Remap)
   CC/4          → Bloom.Intensity
   OSC/tracker/1 → Warp.WarpCenter

3. ClipEvaluation rendert Clips mit gemappten Parametern:
   Gradient  → Texture (mit MIDI-gesteuerten Farben)
   Particles → Texture (mit Audio-reaktiven Parametern)
   PenDraw   → Texture (mit Stift-gesteuerten Strichen)
   Webcam    → Texture (direkt von Kamera)
   ChromaKey → Texture (MIDI-gesteuerte Schwelle)

4. StreamRouting composited die Layer:
   Background + Drawing + Camera + PostFX → Final Texture

5. Output sendet an Projektor:
   Final Texture → Projector1 (Monitor 2, 1920x1080)
```

Dieses Modell zeigt die zentrale Staerke des Ansatzes: **Inputs, Content und Outputs leben im selben Baum.** Alles ist ein Clip. Alles laeuft durch dieselbe Pipeline. Alles wird in derselben Show-Datei gespeichert. Alles ist ueber denselben Inspector konfigurierbar. Alles ist undo-bar.
