# Working notes

Twelve instrument blocks for Besiege, one shared behaviour, a sampler and two
synthesis engines behind it -- modal and FM -- plus Braids' macro-oscillator,
which came in whole from the mod it used to be -- plus an eleventh block, the MIDI loader, which reads a score off the
disk and writes the machine that plays it.

## Layout

```
Orchestra/            the folder Besiege loads, and what goes to the Workshop
  Mod.xml             manifest; <ID> is written by the game on first load, keep it
  Piano.xml ...       one per block, each declaring its own types and controls
  Loader.xml          the MIDI loader block: a tool, not an instrument
  OrchestraScripts/   sources; the built Orchestra.dll sits beside them
  Resources/          mesh, texture, icon, and Samples/ once cut
tools/                build, install, the sample extractor, the song makers
docs/                 sample cutting, design notes, and modding notes
```

The sources fall into three groups:

| Files | What they are |
| --- | --- |
| `InstrumentBehaviour`, `Voices`, `SampleBank`, `OrchestraModule` | the eleven instruments and their sound |
| `Midi`, `Song`, `Bsg`, `Drop`, `Catalogue`, `Files` | the converter: a score in, blocks out |
| `DockedPanel`, `OrchestraPanel`, `LoaderPanel`, `UIF`, `IconArt`, `ClickShield` | the two panels, and the UI Factory they need |

**The converter touches no Unity object**, which is why it can be -- and is --
checked at build time by `tools/tests/SongCheck.cs`, running on Besiege's own Mono
with no game in sight. Anything added to it should keep that property.

`.git` must stay *outside* the folder Besiege copies when publishing — its
read-only objects jam the Workshop uploader.

## Build

`./tools/build.sh`, `./tools/verify-build.sh`, `./tools/install.sh`. No .NET
toolchain: the build drives Besiege's own `mcs.dll` through `libmono.so`. It also
runs three checks, each of which has caught something real: the block XMLs against
the module's own attributes (`XmlCheck`), the built assembly against the loader's
blacklist (`BlacklistCheck`), and the MIDI converter against a made-up score
(`SongCheck`).

The assembly is built into a scratch *directory* rather than under a scratch
*name*: an assembly is identified by its name once loaded, so building to
`Orchestra.<pid>.dll` made it impossible for the song check to reference.

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

`tools/make-block-meshes.py` fetches eleven low-poly instruments from Poly Pizza
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
* **Nine of the models have no texture, only a flat colour per material.** A
  Besiege block wants a texture, so the colours go into a palette a few pixels
  across and every triangle points at the middle of its own patch. A block's
  texture is therefore about eighty bytes. **The harp is the exception**, and it
  cost a shipped block to find out: it carries a 2048-square baked image and a
  `baseColorFactor` of white, so reading the factor -- which is all there was to
  read for the other nine -- put a white harp in the game where the model is
  mahogany with pale strings. Where a material has a `baseColorTexture`, each
  triangle now takes the texel under its own middle: the models are low-poly
  enough that a triangle is one flat area of the image, and everything downstream
  goes on working in the one flat colour per triangle it always has. Quantised to
  sixteen levels a channel, which folds the wood grain away and keeps the palette
  to fifty patches in a 64-pixel PNG.
* **The toolbar photographs a block from the *opposite* side to the one the
  arithmetic says.** The camera is fixed and `<Icon><Rotation>` turns the block in
  front of it, so where it stands in the block's own frame is that rotation
  undone -- but it looks along **+z**, not the -z a Unity camera looks along by
  default. That is measured, not reasoned: at `-115,210,0` the undoing puts the
  camera in front of and above the block, and the game drew all nine from behind
  and below, the piano showing its legs and the drum its bottom head. So
  `icon_camera` undoes the rotation and then negates, and the poses below are read
  off that.

* **An instrument's authored front is not the same for every model, and the
  bounding box says which way it faces.** Seven of the ten take a quarter turn to
  face the block's +y; the harp took the same one and went into the game edge-on,
  presenting the thin side of its string plane, which is the one view of a harp
  that reads as a stick. The check is the printed size: a flat instrument should
  come out thin in **y**, and the harp was 0.28 x 0.71 x 1.60 -- thin in x -- until
  its yaw became a half turn.
* **The ten icon poses are a camera, not a magic number.** x is how far above the
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

(The general form of this, and of everything else here that is about Besiege
rather than about this mod, is in
[Besiege-Modding-AI-notes](https://github.com/anton-scholten/Besiege-Modding-AI-notes).
Start there for a different mod.)

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

**`System.IO` is blacklisted, with carve-outs.** No `File`, no `Directory`, no
`StringReader`/`StringWriter` -- but `Stream`, `MemoryStream`, `BinaryReader`,
`BinaryWriter`, `TextReader`, `TextWriter`, `Path` and `SeekOrigin` are each
exempted by name in `InternalModding.Assemblies.AssemblyScanner`, so a mod can
handle bytes it is given and cannot go and find any. A SoundFont cannot be opened
at runtime and nothing can be downloaded. Audio arrives only as an `AudioClip`
declared in `Mod.xml`; `SampleBank` then reads it into a `float[]` with `GetData`,
on the game thread, because the audio thread cannot call Unity. The full list, and
what it exempts, is in `tools/tests/BlacklistCheck.cs`, copied from the scanner
itself.

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

**Selectors and toggles follow the family's house style**, which is written up in
[04-ui-factory.md](https://github.com/anton-scholten/Besiege-Modding-AI-notes/blob/main/notes/04-ui-factory.md):
every selector is a `Chooser` (with arrows, or without for the file list), and
every toggle has the prefab's swell destroyed and a `Swell` put on its caption so
the lettering grows and the row does not. `Chooser.cs`, `Swell.cs` and
`ZoomGuard.cs` are copies -- keep them in step with Special
Effects rather than editing one of them here.

**Use UI Factory's controls, not lookalikes.** It ships Besiege's own widgets as
prefabs, and two matter here: `Options` (with the `Besiege.UI.Bridge.Option`
component) is the `< Grand piano >` selector the block mapper itself uses, and
`Text Toggle` is the game's real toggle. Both replaced hand-built equivalents —
a grid of buttons painted red, and a button pretending to be a toggle. The full
list is registered in `Besiege.UI.Mod.OnAllResourcesLoaded`: Empty, Icon, Text,
Text Button, Text Toggle, Text Dropdown, Icon Button, Icon Toggle, Button
Dropdown, Input Field, Slider, Options, Scroll View, Blur, Panel, Mask, Window.

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

**The panel is docked to the mapper, and measuring it is the awkward part.** The
window is a renderer named `Background` -- the tallest of the three that share its
width -- projected through the camera whose culling mask includes its layer. Two
other rules were shipped and both were wrong: the widest thing the mapper draws is
`WideShadow`, an eleventh too wide and higher up, and `Visual` is a 93-pixel
button. `upperLeft`/`lowerRight` are public and are *not* the window's corners.
Docking runs in `LateUpdate`, after the mapper's own drag; the width is taken
before the rows are laid out to it; and the placement path must never return
without placing, which is the bug that cost the most. The panel logs `docking to
'<name>' at <rect>` once a session, and the measured geometry is written up in
[docs/MODDING-NOTES.md](docs/MODDING-NOTES.md) for anyone doing this from another
mod.

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
`ReadFromBlock` and `Paint` beside the values, the ranges and the type list.

**Committing a setting is not the same as setting it.** A mapper value is stored
twice — the live one, and the one the block loads from. Assigning
`MapperType.Value` writes only the first, so it is heard now and lost on save.
`BlockMapper.OnEditField` reconciles them, reserialising the block and adding an
undo entry, which is why the panel writes live on every drag frame and commits
once when the mouse comes up.

## The MIDI loader block

`LoaderBehaviour` is a tool with no simulation behaviour at all. Its settings are
ordinary mapper controls; the file box, the summary and the two buttons are in
`LoaderPanel`, which -- unlike the instrument panel -- **needs UI Factory**,
Besiege's mapper having no text box to type a filename into. It says so once and
does nothing rather than sitting there looking broken.

**Find your own block ids, never compute them, and match on the prefab's name --
which is not the block's name.** A registered modded prefab is called
`<mod guid>-<local id>`: `BlockPrefabCreator.CreatePrefab` names the object that,
and `BlockLoader.RegisterPrefab` then calls `BlockPrefab.SetNameFromGameObject`,
which copies it over the `<Name>` that `SetupBehaviour` had put there. That string
names this mod's blocks exactly, and `Catalogue.LocalIdOf` reads the `<ID>` back
out of it. Three other answers are wrong and each shipped: `BlockPrefab.locID` is
-1 on every modded block (its constructor sets it, nothing writes it); a mod's
blocks are not numbered contiguously once other mods are installed; and the
module's behaviour is *not* on the prefab, `ModBlockBehaviourHandler.Awake` adding
it to the instance. Matching on `BlockPrefab.name` as the block's `<Name>` finds
nothing at all, which is how every family ended up without an id and the loader
refused to convert. Getting this wrong is silent and produces machines full of
another mod's blocks.

**Never clear `BlockPrefab.SkinCanBeChanged` to hide the skin picker.** It is read
by `BlockPrefab.SetIcons` as well as by the mapper, and `SetIcons` calls
`VisualController.SetPrefabIcons()` -- the thing that puts a block's own mesh and
material on its button -- only when the flag is true. Clear it and the block shows
`BlockLoader.LoadingMaterial` in the block menu, which is what "the icon is the
loading texture" means; clicking the button repaints it from `defaultMat`, captured
from the same loading material, so it stays wrong. `Skins.Hide` builds the `MVisual`
the mapper would have built and sets `DisplayInMapper = false` instead -- Special
Effects' answer, kept in step with it. Both wrong versions shipped here: once per
block in `SafeAwake`, and once for every prefab at load, which only spread it to
all ten.

**A mod may only reach its own folders, and a folder argument needs a trailing
slash.** Both come out of `ModPaths.GetFilePath`, and both cost something here
before it was read to the end. It combines the argument with the mod folder --
which does hand an absolute path straight through -- and then walks the result's
directory *upwards* looking for the mod's own, throwing
`Path is not in mod directory!` if it never arrives. And a resolved path that does
not end in a separator is treated as a file, so the folder acted on is its parent:
`GetFiles("")`, the obvious way to list the mod's own folder, tries to list `Mods/`
and throws. That is what emptied the block catalogue and made the loader say *the
instrument blocks could not be read*; the block list now comes from `Mod.xml`,
which names every one of them anyway. `Files.cs` is the only place that touches any
of this, and it says so at the top.

**A mod can open the system's file dialog.** `SFB.StandaloneFileBrowser` is in
`Assembly-CSharp` with `libStandaloneFileBrowser.so` beside it, and **nothing in
Besiege calls it** -- so it works or it does not, and `Files.Pick` treats a failure
as ordinary and falls back to a folder in the mod's data directory.

**Adding blocks to the machine is Besiege's own additive load.**
`MachineFileBrowserController.LoadAdditive` is what the load screen's "add to
machine" button runs, and every member it touches is public:
`Machine.AddBlocksFromInfo` (whose third argument is `ref`, not `out`),
`BlockSelectionTool.Duplicating`, `DeselectAll`, `Select`,
`AdvancedBlockEditor.SetActiveTool`, `UndoSystem.AddActions`,
`AddPiece.UpdateMiddleOfObject`. `Drop.cs` does the same steps in the same order,
so joints, clusters, undo and the selection tool behave as they do for a real
load.

**Saving goes through Besiege's own screen.** A mod can write neither with
`XmlSaver.Save` (forbidden by name, every caller private) nor with `ModIO`
(SavedMachines is outside its folders), so SAVE adds the blocks -- they arrive
selected -- and opens `FileBrowserView.Open(FileBrowserType.LocalMachines, true,
true)` over the top, where SELECTION ONLY saves exactly them. The view is inactive
while closed, so it is found with `Resources.FindObjectsOfTypeAll`, not
`FindObjectOfType`. `Bsg.cs` still writes the format -- into the mod's own data
folder when that screen cannot be found, and for `SongCheck` to hold against
`make-song.py`.

**Its panel is the whole menu.** Besiege's mapper keeps the key -- which is what
every timer the block writes waits for -- and `DisplayInMapper = false` takes the
rest away as soon as UI Factory answers, exactly as `InstrumentBehaviour` does.
The instrument and its type are two `MMenu`s, and the second's list is swapped
whenever the first moves: **`MMenu.Items` has a public setter**, so a menu whose
choices depend on another menu needs nothing else. The sliders are the same rows
the instrument panel draws, which is why they live on `DockedPanel`.

**A uGUI `Dropdown` opens inside whatever mask it is under.** `Dropdown` parents
its list to itself, so in a panel built into the Window prefab's scroll view the
list is clipped by that viewport -- a dropdown near the bottom opens into nothing.
Put it at the top of the panel, or do not use one.

**Prefer `ModIO`'s relative form.** It will take an absolute path (above), but a
file chosen from the mod's own Songs folder is remembered by name and read back
with `data: true`. The relative form is what the API is for and it cannot be wrong
about where the data folder is; the absolute path is shown to the player and used
for what they type.

**Two name clashes cost a build each.** `using Modding` makes `ModIO` mean the
*namespace* of that name rather than `Modding.ModIO`; and `Machine` under
`using Modding.Modules` resolves to `Modding.Blocks`', not the game's. Write both
out in full.

**`List.Sort` is not stable, and that silently changed the tempo.** The tempo map
starts with MIDI's assumed 120 bpm at tick 0, and most files set their own tempo
at tick 0 as well. Sorted by tick alone, the assumption could end up last and win,
and the whole score played at the wrong speed with nothing else out of place --
found by running both converters over the same five files and comparing, which is
what `SongCheck.cs <file.mid>` is for.

## Engines

`modal` — a bank of exponentially decaying sine partials, spaced by
`inharmonicity`. 1 is a harmonic series; above it the partials stretch and go
metallic, which is what a cymbal or a bell actually does. `brightness` tilts the
top partials up and makes them die first.

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
