# Working notes

Nine instrument blocks for Besiege, one shared behaviour, two synthesis engines
and a sampler.

## Layout

```
Orchestra/            the folder Besiege loads, and what goes to the Workshop
  Mod.xml             manifest; <ID> is written by the game on first load, keep it
  Piano.xml ...       one per block, each declaring its own types and controls
  OrchestraScripts/   sources; the built Orchestra.dll sits beside them
  Resources/          mesh, texture, icon, and Samples/ once cut
tools/                build, install, and the sample extractor
```

`.git` must stay *outside* the folder Besiege copies when publishing — its
read-only objects jam the Workshop uploader.

## Build

`./tools/build.sh`, `./tools/verify-build.sh`, `./tools/install.sh`. No .NET
toolchain: the build drives Besiege's own `mcs.dll` through `libmono.so`, and
`build.sh` also runs the loader's blacklist check.

**That compiler is C# 4 and old.** No interpolated strings, no `?.`, no
`nameof`. **Any `enum` declaration segfaults it** — engine names are strings and
constants are `const int`.

`Modding.Serialization` may be imported in `OrchestraModule.cs` because that file
has no `UnityEngine.Vector3` to clash with the one that namespace declares. Do
not import it anywhere that does.

## Block XML

A block needs more than its module. `BasePoint`, `Colliders` and `AddingPoints`
are all required, and Besiege's only complaint is a line in `Player.log` —
`Block must contain BasePoint element!` — after which the block is simply absent
from the toolbar. Nine blocks written from scratch, without one, all failed to
load at once and looked exactly like a broken assembly.

**Copy the geometry from a block that works** rather than writing it out. These
nine take Sound Blocks' wholesale, because they share its mesh.

`tools/tests/XmlCheck.cs` now asserts the required elements as well as parsing,
so this fails the build rather than the game.

### Every module attribute is required unless it has `[DefaultValue]`

`InternalModding.Common.Serialization.Validate` picks the members it insists on
with

```csharp
members.Where(m => !m.IsDefined(typeof(DefaultValueAttribute)))
```

and then, for each of those carrying `[XmlAttribute]` that the element did not
supply, logs

```
[Mods] InstrumentType (at line 16, column 6 in Piano.xml) must have loops attribute!
[Mods] Error loading Piano.xml
```

and drops the whole file. A C# field initialiser counts for nothing here — the
initialiser is what the value *becomes*, `[DefaultValue]` is what makes the
attribute *optional*, and you need both. Besiege's own modules do exactly this;
`Modding.Modules.Official.ShootingModule` marks eight.

So the rule in `OrchestraModule.cs` is: **a field that declares a default in C#
carries the matching `[DefaultValue]`; a field without one is required.** Three
are required today — `Type name`, `Extra key`, `Extra name`. `XmlCheck` reads
those markers out of the module source and checks every block XML against them.

Validate stops at the first failure, so the log names one attribute in one file
even when several are wrong. Do not fix them one launch at a time.

### Reading the log is the first move, not the last

The nine blocks were "not customizable" for one launch longer than they needed
to be because `Player.log` was searched for `Orchestra` — which never appears —
instead of `[Mods]`, where all nine errors were sitting. Mods are logged under
that tag and under nothing else.

    grep -a "Mods\]" ~/.config/unity3d/Spiderling\ Games/Besiege/Player.log

## The three hard constraints

**`System.IO` is blacklisted.** No `File`, `Stream`, `Path`, `BinaryReader`. A
SoundFont cannot be opened at runtime, and nothing can be downloaded. Audio
arrives only as an `AudioClip` declared in `Mod.xml`; `SampleBank` then reads it
into a `float[]` with `GetData`, on the game thread, because the audio thread
cannot call Unity.

**Variables bind to `MKey` and nothing else.** `MSlider`, `MValue` and `MMenu`
have no variable selector. That is why a block plays one note: a note slider
could never be automated. A tune is a row of blocks.

**The audio thread is not the game thread.** `ReadPcm` may not touch Unity,
allocate, or lock. Settings cross as volatile primitives; note events cross
through a single-producer, single-consumer ring buffer. Voices are pooled and
pre-allocated — a collection during playback is an audible click.

## Point-source audio

`AudioClip.Create(name, length, 1, rate, stream: true, PCMReaderCallback)` on a
source with `spatialBlend = 1`. Unity pulls **mono** samples from the callback
and spatialises afterwards, so distance, doppler and stereo position are the
engine's job.

Do not use `OnAudioFilterRead` here. It sits inside the source's filter chain and
hands back a buffer that is already spatialised, so writing into it destroys the
panning — which is why the Braids synth pans by hand at `spatialBlend = 0`.

## Key emulation

Override `KeyEmulationUpdate`. `Machine.FixedUpdate` calls
`SendEmulationUpdateBlock` on every block first, so each emulator and variable
has raised its count, then `EmulationUpdateBlock` on everything registered — and
`BlockPrefabCreator.SetupBehaviour` registers modded blocks unconditionally.

Latch the edges there and consume them in `SimulateUpdateAlways`.
`MKey.CheckEmulation` keys its snapshot to `Time.fixedTime`, so an edge lives for
one fixed step: read from Update and you see the same press repeatedly at a high
frame rate, or miss it entirely at a low one.

## The panel

`OrchestraPanel` is one MonoBehaviour on a `DontDestroyOnLoad` object, made in
`Mod.OnLoad`, watching `BlockMapper.onMapperOpen`/`onMapperClose`. It builds from
the block's own registered controls — `ExtraSliders`, `ExtraToggles`, the type
menu — so an instrument declared in XML gets its rows without code.

**UI Factory is a soft dependency, and that rests on one thing:** every mention of
`Besiege.UI` lives in `UIF.cs`. A type that will not resolve fails as the method
mentioning it is compiled, so confining the mentions means one guarded call,
`UIF.Available`, decides whether the panel can exist at all.

**Use UI Factory's controls, not lookalikes.** It ships Besiege's own widgets as
prefabs, and two matter here: `Options` (with the `Besiege.UI.Bridge.Option`
component) is the `< Grand piano >` selector the block mapper itself uses, and
`Text Toggle` is the game's real toggle. Both replaced hand-built equivalents —
a grid of buttons painted red, and a button pretending to be a toggle. The full
list of prefab names is in `Besiege.UI.Mod.OnLoad`: Window, Panel, Text, Text
Button, Text Toggle, Text Dropdown, Icon, Icon Button, Icon Toggle, Input Field,
Slider, Options, Scroll View, Mask, Blur.

`Besiege.UI.Bridge.Behaviours.StopsZoomWhenHovered` is likewise theirs, and stops
the wheel zooming the level over the panel — the same problem Sound Blocks had to
solve by hand against `stopCamZoom`.

`Option.options` is a `List<string>`, not an array, and its `onValueChanged` is UI
Factory's own event type — so `UIF.OptionIndex` polls the index instead of binding
to a signature that may change.

**Besiege declares its own `Slider` in the global namespace**, so `using
UnityEngine.UI` does not win and `UnityEngine.UI.Slider` has to be spelled out.
`Text`, `Image` and `Button` are fine.

**Committing a setting is not the same as setting it.** A mapper value is stored
twice — the live one, and the one the block loads from. Assigning
`MapperType.Value` writes only the first, so it is heard now and lost on save.
`BlockMapper.OnEditField` reconciles them, reserialising the block and adding an
undo entry, which is why the panel writes live on every drag frame and commits
once when the mouse comes up.

## Engines

`modal` — a bank of exponentially decaying sine partials, spaced by
`inharmonicity`. 1 is a harmonic series; above it the partials stretch and go
metallic, which is what a cymbal or a bell actually does. `brightness` tilts the
top partials up and makes them die first.

`drum` — a sine body swept down by `pitchDrop` over the note, plus one-pole
filtered noise for the skin.

`sampler` — nearest recorded note from `SampleBank`, Catmull-Rom interpolation
(linear costs audible high end when shifting down), ADSR. Sustaining types hold
while the key is down; struck ones ignore the release.

Loop points travel in a `loops="start-end ..."` attribute parallel to `samples`,
because there is nowhere else to put them: a sidecar file would need `System.IO`.
They are offsets into the *cut*, at the *output* rate — the font's own indices are
absolute into its sample block and at its own rate, and `extract-samples.py`
converts. Only strings, brass and woodwind are looped; a piano decays by itself.

Adding an engine means a `Voice` subclass and a branch in `PoolFor` and
`NoteOn`. Adding an *instrument* means XML only.
