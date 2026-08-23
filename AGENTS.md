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
nine took Sound Blocks' collider and adding points wholesale; only the mesh is
their own.

* **What a block can do is read from its own XML, not listed in code.** The
  Toggle -- which latches the key down -- exists only where some type carries
  `holds="true"`, because latching a struck note changes nothing that can be
  heard; the same test decides whether the block breathes while a note is held.
  Both come out of `InstrumentBehaviour.Sustains`, so a tenth block sorts itself.
  The panel has to cope with the control being absent: `SameShape` counts it
  conditionally, or a window built for a violin would be handed to a piano.

* **A block's visual is not its transform.** `BlockBehaviour.VisualController`
  lists the renderers, and their transforms are children carrying the `<Mesh>`
  offset and scale; the block's own transform is the physics body the colliders
  are placed against. Anything that moves a block for show -- the swell it plays
  with -- writes to the former and must not touch the latter.

### The block meshes

`tools/make-block-meshes.py` fetches nine low-poly instruments from Poly Pizza
and converts them. The models are not in the repository — they are third-party
CC-BY work and the script re-downloads what is missing — and `tools/models/` is
ignored.

Three things it has to get right, and the reasons are worth keeping:

* **A block mesh is Z-up, centred on the origin, worn at half scale from
  `z = 0.5`.** The convention is the synth block's, whose own generator says it in
  as many words.

* **glTF is right-handed and Unity is left-handed, so the map between them has to
  flip the handedness.** `(x, y, z) -> (-x, -z, y)`: the up axis swaps in, and two
  are negated, which is a reflection. A swap that preserves the handedness -- what
  this did at first -- lands a model that measures correctly and is mirrored, and
  a mirrored piano is a piano whose keyboard runs the wrong way. `wind` puts the
  triangle winding back from the shading normals, so the reflection costs nothing
  there. Watch the yaws in `POSE` if this line ever changes again: a reflection
  does not commute with a turn, so mirroring the models reverses which way a given
  yaw carries an instrument's face, and all six had to change sign.
* **These models have no texture, only a flat colour per material.** A Besiege
  block wants a texture, so the colours go into a palette a few pixels across and
  every triangle points at the middle of its own patch. A block's texture is
  therefore about eighty bytes.
* **The toolbar photographs a block from the *opposite* side to the one the
  arithmetic says.** The camera is fixed and `<Icon><Rotation>` turns the block in
  front of it, so where it stands in the block's own frame is that rotation
  undone -- but it looks along **+z**, not the -z a Unity camera looks along by
  default. That is measured, not reasoned: at `-115,210,0` the undoing puts the
  camera in front of and above the block, and the game drew all nine from behind
  and below, the piano showing its legs and the drum its bottom head. So
  `icon_camera` undoes the rotation and then negates, and the poses below are read
  off that.

* **The nine icon poses are a camera, not a magic number.** x is how far above the
  block it sits, y and z which side it looks from -- z spins the block under the
  camera, being the turn Unity applies first. `-65,210,180` is Besiege's own
  three-quarter, a third of the way up; `-25,210,115` is half above, for the three
  that are struck from above and whose face is their top; and the horns take
  `z=180`, which is the same turn measured from a different start, they being laid
  across the block rather than facing out of it.

* **Some icon poses carry a roll, and are not readable as three angles.** The
  toolbar camera looks along world +z and an icon rotation turns the *block*, so
  nothing in the Euler triple tilts the picture on its own; `rolled()` applies the
  turn to the whole rotation and decomposes it back, which is where entries like
  `-31.3, 156.7, -151.6` come from. `--pose x y z roll` prints one. The comment
  beside each such entry keeps the base pose and the roll it was given, because
  the numbers themselves no longer say. It is the only way to face a drum head or
  a cymbal anywhere: a round thing looks the same however it is spun about its own
  axis, so only the picture can turn.

* **The toolbar lights a block from beside the camera and to the right**, which is
  measurable on the drum icon: of the shell facets, the right-hand one is the
  brightest. So `z` turns every instrument that way -- about 37 degrees right of
  the camera for the ones with a face, which is lit rather than shadowed and still
  open enough to read. Turning further only presents the edge of something flat: a
  guitar at ninety degrees is a stick. A drum and a cymbal are round, so the turn
  does not show on them at all -- for those two only the tilt or the scale can
  change what the tile looks like. Get the tilt's sign wrong and the icon is a view of the
  block's underside, which is easy to miss and obvious once seen.

* **The instruments face the block's +y**, which is the side a machine is looked
  at from while it is built. The three-quarter preview looks from there, so what
  it shows is what a placed block shows. The `ICON` table in the tool is the same
  rotation the block XMLs give their icons; it is duplicated so the preview can be
  honest about the toolbar, and `XmlCheck` reads the table out of the Python and
  holds the two together, a preview drawn from a stale pose being a picture of a
  block that does not exist.

* **The preview projects into a left-handed frame.** These coordinates are
  Unity's, so the screen's right is `cross(eye, up)`, not the `cross(up, eye)` a
  right-handed frame would want. With the arguments the other way round every
  render was a mirror of the game, which is invisible until it matters and then
  costs a round of poses turned the wrong way -- "further right" in the preview
  was further left in the toolbar. The trumpet settles it in one look: the game
  draws its bell towards the left of the tile.

* **Check a preview against the game before believing it.** `--preview` renders
  each mesh from a fixed angle with a z-buffer and flat shading, and its first
  version built the camera's up vector as `eye x right` rather than `right x eye`
  -- so it drew every block upside down, five of them were "corrected" on the
  strength of it, and they shipped standing on their heads. The models were right
  as they came. The `POSE` table is for real changes of pose, and it is empty.

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

**The audio thread is not the game thread.** `OnAudioFilterRead` may not touch
Unity, allocate, or lock. Settings cross as volatile primitives; note events cross
through a single-producer, single-consumer ring buffer. Voices are pooled and
pre-allocated — a collection during playback is an audible click.

## Where the sound is made, and where it is placed

`OnAudioFilterRead` on an AudioSource looping **one sample of silence**, at
`spatialBlend = 0`. A filter runs in the mixer, on the buffer that is about to be
played, so a note queued by a keypress this frame is heard in the next block of
samples.

**Do not go back to a streaming `AudioClip` with a `PCMReaderCallback.`** It is
fed *before* Unity's 3D stage, which is tempting — distance, doppler and panning
all become the engine's job — and it costs the stream's read-ahead: that callback
runs well before what it fills is heard, so every block answered its key late.
That was the whole of the delay the piano was reported for, and it was never the
piano's: the nine share one audio path, and the sampled attacks (2 ms on piano,
guitar and bass; 30–120 ms on brass, strings and woodwind, which is the swell of
a bowed or blown note) are as they were.

The price is that Unity's 3D stage no longer runs at all, so `Place` does it:
each frame, on the game thread, the block works out a gain for each ear from
where it stands relative to the `AudioListener` — full volume within a metre,
silent at RANGE, straight line between, which is exactly the linear rolloff the
source used to be given — and the filter slides onto that pair across the buffer,
or a turning camera is heard as a staircase. Keep the placement on the game
thread; a transform may not be read from the audio one. Keep re-finding the
listener, because Besiege swaps cameras between building and running and the held
one goes stale rather than null. Doppler is the one thing not reproduced: it was
Unity resampling the clip, and there is no clip here to resample.

The same reasoning, and the same shape of answer, is in the Braids synth.

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

**The Window prefab is more than a frame, and what it already carries does not
need adding.** It is `Window` (Image, `StopsZoomWhenHovered`) with three children:
`Blur`, `TopBar` (Image, `Drag` already targeting the window, holding `Text` and
`CloseButton`), and `ScrollView` — a full `ScrollRect` over `Viewport/Content`
with both scrollbars, set to hide them when what it holds fits.

So the panel adds no `Drag` and no `StopsZoomWhenHovered`, and it builds its rows
**into `ScrollRect.content`**. Building them onto the window instead left that
scroll view holding the prefab's own 500-unit placeholder — taller than any panel,
so the scrollbar was permanently up beside an empty scroll area. Size the content
to what was built and the bar goes away by itself.

(The prefab is an asset bundle, so this was read out of
`UIFactory/Resources/besiege-ui-prefabs` rather than guessed at: a `UnityFS`
container, one LZMA block, a version-15 serialized file with type trees.)

`Option.options` is a `List<string>`, not an array, and its `onValueChanged` is UI
Factory's own event type — so `UIF.OptionIndex` polls the index instead of binding
to a signature that may change.

**Besiege declares its own `Slider` in the global namespace**, so `using
UnityEngine.UI` does not win and `UnityEngine.UI.Slider` has to be spelled out.
`Text`, `Image` and `Button` are fine.

**The panel is docked to the mapper, and measuring it is the awkward part.**
The mapper's window is drawn in world space, so there is no rect to read: it is
found by projecting a renderer's bounds through the camera whose culling mask
includes its layer. Which renderer is the whole question, and the answer was got by
making the panel log every part it measured. With a piano open at 4K:

    Background   874.80 x  389.88   at y 1540.87   <- the window
    Background   874.80 x  281.88   at y 1540.87
    Background   874.80 x  174.96   at y 1658.59
    WideShadow   972.00 x  194.40   at y 1638.37
    Mask         874.80 x 1555.20   at y  267.55
    Visual        93.31 x   93.31

So: the window is a `Background`; they all share its width and the tallest is the
frame. Two other rules were shipped and both were wrong -- the widest thing drawn
is `WideShadow`, an eleventh wider and higher up, which put the panel over the
mapper's lower half; `Visual` by name is a 93-pixel button, which made the panel a
narrow strip. `BlockMapper.upperLeft` and `lowerRight` are public and look like
precisely what this wants; they are not, being found by tag and used by
`UpdateBackground` to clamp the window against the screen. `background` itself is
private, and the `ContainerDetails` components are one per row.

Docking runs in `LateUpdate`, after the mapper's own drag has moved it; the width
is taken before the rows are built, since they are laid out to it, and a mapper of
a different width rebuilds and re-places in the same frame. It said `docking to
'<name>' at <rect>` once a session in the log, which is how any of this is
checkable from outside the game.

**The mapper is a fallback, not a second panel.** Every mapper control except the
key sets `DisplayInMapper = false` once `UIF.Available` says yes, so with UI
Factory installed Besiege's own menu holds the key alone and the panel draws the
rest; without it, or if the panel throws while building (`OrchestraPanel.Failed`),
everything comes back. Besiege reads that flag as it builds its rows, so a change
lands on the next open rather than while the mapper is up. The question is put on
a half-second timer rather than every frame: UI Factory loads its bundle after the
mod does, so "not yet" is not "not installed" and one ask is not enough -- but when
it really is absent, each ask is a caught exception, and one per block per frame
buys nothing.

**LISTEN plays the block in the build scene**, which is a second owner for the
AudioSource. The rule is re-checked in `Update` rather than switched from the
callbacks that change it, because a simulation runs on a *clone* of the machine:
`OnSimulateStart` and `OnSimulateStop` land on that copy and never on the block
the panel edits. The clone takes the early return on `StatMaster.levelSimulating`
and keeps the source those two gave it. The audition releases itself after a
second or so, and the source is held up past that for as long as the audio thread
says a voice is still sounding — stopping it at the note-off is what would cut
the release. The speaker mark is drawn by `IconArt`, because UI Factory's sprite
set is Besiege's HUD sprites and cannot be listed: naming one would be a guess.

**A row's number is UI Factory's Input Field**, so a setting can be typed as well
as dragged. That prefab is the one that carries
`StopsHotkeysWhenInputFieldFocused`, which is what keeps Besiege from acting on
what is being typed; a hand-built box would have to solve that itself. A field is
not written to while it `isFocused`, or a drag elsewhere would take the caret out
from under whoever is typing.

**Everything a row shows must be written from the block on every open, not once
when the row was made.** A window is kept and rebound when the next block has the
same *shape* — the same number of sliders and toggles — and a block with the same
shape still calls its extras something else, which is how a piano came to have a
PALM MUTE where its SUSTAIN is. Captions therefore come from `MapperType.
DisplayName` for every row, the fixed three included, and are rewritten in
`ReadFromBlock` and `Paint` beside the values, the ranges, the type list and the
title.

**A window dragged off the screen takes the bar that drags it with it**, and the
position is remembered, so it would be out there again next time. `Fit` is
therefore applied every frame, not only when the panel opens: across, enough of
the window has to overlap the screen to aim at, from either side; the bar may go
neither above the top edge nor below the bottom one. Same policy as Git-view's
`KeepOnScreen`.

**Where the panel was left is remembered by its top-left corner**, not its middle:
one panel serves all nine blocks and is as tall as the block it opened on has
controls, so holding the middle would slide the window under the pointer whenever
an instrument with one more row was opened. `Prefs` keeps it across sessions in
`Modding.Configuration.GetData()`, which is Besiege's own store — PlayerPrefs
would work but would leave the setting in the game's options file after the mod
was uninstalled. UI Factory's drag reports nothing, so where the rect ended up is
the only account of it there is; it is read when the panel closes.

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

**A sample with no loop points ends where it was cut, not where the note
finished.** Nothing shipped is in that position any more -- the font loops
everything -- but a clip dropped in by hand can be, and the
recordings stop at two seconds or wherever the font's own sample did, and a
guitar is at two thirds of its body level by then — the overdriven one is at full
level — so the note stopped rather than ended. `SampleBank.FindTail` picks a
window at the end for the voice to turn round in while it fades: a whole number of
periods of the note the sample *is*, searched ±6% because a font's root key is
not exactly its recorded pitch, halved until the recording does not fall too far
across it, and kept clear of the fade the extractor leaves (about thirty
milliseconds by the time it comes back through Ogg, not the ten the script asks
for). `TailGain` puts back what the recording falls across that window, so the
level does not step up on every turn and the note goes on decaying at the rate it
already was. For a sampler type, `decay` is how long the rest of that note takes.

Two sample sets are past helping this way and want re-cutting from the font:
`bass_synth` is 14–37 ms and `piano_rhodes_80` is 59 ms — they are font samples
meant to be *looped*, and the extractor only takes loop points for strings, brass
and woodwind.

Loop points travel in a `loops="start-end ..."` attribute parallel to `samples`,
because there is nowhere else to put them: a sidecar file would need `System.IO`.
They are offsets into the *cut*, at the *output* rate — the font's own indices are
absolute into its sample block and at its own rate, and `extract-samples.py`
converts. **Every** preset in the font loops, and every sample carries its points
now: what differs is what the game does with them. `holds="true"` — bowed, blown —
means the loop is the sustain and the note goes round it for as long as the key is
down. Without it the loop is a ring-out: the note fades through it over `decay`,
which is how a font builds a guitar or a piano, and it is the only reason a
14-millisecond synth bass sample is a note at all rather than a click. `damped` is
the separate question of whether letting the key go stops the note, and is true of
a piano as well as of anything bowed.

**Vorbis does not hand back the number of samples it was given.** Nine of the
shipped loops pointed past the end of their decoded clip — the tuba's middle note
among them, which had therefore never sustained. The extractor now decodes each
Ogg it writes and moves the pair down to fit, and `ReadLoop` trims rather than
discards, three samples clear of the end so the interpolation's own guard cannot
stop the voice before the wrap.

Adding an engine means a `Voice` subclass and a branch in `PoolFor` and
`NoteOn`. Adding an *instrument* means XML only.
