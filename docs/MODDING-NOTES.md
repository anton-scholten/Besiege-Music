# Besiege modding notes

Working notes on the parts of Besiege's mod loader this mod leans on, written for
whoever — or whatever — needs the same thing next. Everything here was read out of
the game's own assemblies (`Besiege_Data/Managed/`) or measured in game; where a
fact was expensive to get, the way it was got is recorded with it.

Target: Besiege on Unity **5.4.0f3**, built-in mod loader.

The general notes -- the loader, the blacklist, blocks, keys and automation, UI
Factory, and how to read the game's own metadata -- have been moved out to
[Besiege-Modding-AI-notes](https://github.com/anton-scholten/Besiege-Modding-AI-notes),
which is where a mod that is not this one should start. What follows is the same
material with this mod's specifics attached.

## Docking a uGUI window to the block mapper

**The problem.** Besiege's block mapper is mesh UI drawn in world space, not uGUI.
A mod panel built from [UI Factory 3](https://gitlab.com/dagriefaa/ui-factory-3)
prefabs is uGUI on a `Canvas`. The two cannot share a hierarchy: you cannot parent
a `RectTransform` into the mapper and have it render, sort or lay out. So a panel
that wants to look like part of the mapper has to be a separate window *positioned
against it*, every frame, in screen space.

**The recipe**, as used by `MusicPanel`:

```csharp
// 1. The mapper's window is a renderer named "Background".
BlockMapper mapper = BlockMapper.CurrentInstance;
Renderer[] parts = mapper.GetComponentsInChildren<Renderer>(false);

// 2. Find the camera that draws it, by layer -- the mapper is in the world, and
//    only the camera whose culling mask includes its layer knows where on screen
//    it lands. Take the topmost such camera: Besiege draws its interface last.
Camera eye = null;
foreach (Camera c in Camera.allCameras)
{
    if ((c.cullingMask & (1 << part.gameObject.layer)) != 0
        && (eye == null || c.depth > eye.depth)) { eye = c; }
}

// 3. Project the renderer's world bounds to screen pixels.
Bounds box = part.bounds;
Vector3 a = eye.WorldToScreenPoint(new Vector3(box.min.x, box.min.y, box.center.z));
Vector3 b = eye.WorldToScreenPoint(new Vector3(box.max.x, box.max.y, box.center.z));

// 4. Screen pixels to canvas units. With a CanvasScaler matching on height
//    against a 1080-tall reference, one unit is one pixel at 1080p.
float scale = Reference.y / Screen.height;

// 5. Place the window: anchors and pivot at the centre, so anchoredPosition is
//    the window's centre relative to the screen's.
float left   = (frame.xMin - Screen.width  * 0.5f) * scale;
float bottom = (frame.yMin - Screen.height * 0.5f) * scale;
windowRect.sizeDelta = new Vector2(frame.width * scale, height);
windowRect.anchoredPosition =
    new Vector2(left + windowRect.sizeDelta.x * 0.5f, bottom - windowRect.sizeDelta.y * 0.5f);
```

**Which renderer is the window** is the whole difficulty, and guessing it wrong is
easy — two plausible rules shipped from this repo and both were wrong. The panel
was made to log every part it measured; with a piano open at 4K it saw:

| Name | Size (px) | Bottom (px) | What it is |
| --- | --- | --- | --- |
| `Background` | 874.80 × 389.88 | 1540.87 | **the window** |
| `Background` | 874.80 × 281.88 | 1540.87 | a section inside it |
| `Background` | 874.80 × 174.96 | 1658.59 | a section inside it |
| `WideShadow` | 972.00 × 194.40 | 1638.37 | the shadow behind it |
| `Mask` | 874.80 × 1555.20 | 267.55 | the scroll region |
| `Visual` | 93.31 × 93.31 | — | a button |
| `BG`, `TooltipText`, `KeyPrefab(Clone)`, … | small | — | rows and widgets |

So: **the window is the tallest renderer named `Background`.** They all share its
width, which makes the width robust; only the frame reaches the bottom edge.

Rules that look right and are not:

- *The widest thing the mapper draws* is `WideShadow` — an eleventh wider than the
  window, and its bottom sits ~98 px above the window's. A panel docked to it is
  visibly too wide and lies across the mapper's lower half.
- *`Visual`, by name* is a 93-pixel button. A panel docked to it becomes a narrow
  strip beside the mapper.
- **`BlockMapper.upperLeft` and `lowerRight`** are public `Transform`s and look
  exactly like the window's corners. They are not: `Awake` finds them with
  `GameObject.FindWithTag("upperLeft")`, and `UpdateBackground` clamps the window
  against them. They are the corners of the *screen area* the mapper may be
  dragged within.
- `BlockMapper.background` is the frame, and is private. `System.Reflection` is
  blacklisted, so it cannot be read.
- The `ContainerDetails` components under the mapper (public `Background`, `Top`,
  `Bottom`, `BackgroundPos`, `BackgroundScale`) are **one per row**, not one per
  window. `BlockMapper.Container` is typed `IWidgetContainer`, which exposes only
  `TopValue()` and `ZValue()`.

**Three things that are not obvious once the geometry is right:**

1. **Dock in `LateUpdate`.** The mapper is dragged by its own behaviour. A panel
   placed in `Update` is placed against where the mapper was, one frame behind,
   and the join visibly comes apart while dragging.
2. **Take the width before laying out the rows.** If the rows are sized to a width
   the mapper does not have, the panel is built wrong and has to be rebuilt.
3. **Never return from the placement path without placing.** The bug that cost the
   most here: on a width change the code set its rebuild flag and returned — and
   that same flag gated the placement call, so the panel never docked again and
   never followed a drag. Rebuild *and* place in the same frame.

**Verify it from outside the game.** The panel logs `docking to '<name>' at
<rect>` once a session. Log output lands in `Player.log`
(`~/.config/unity3d/Spiderling Games/Besiege/Player.log` on Linux) and in the
in-game console with `show_logs true`. When docking is wrong, that one line says
what it measured — which is how the table above was obtained.

## Hiding the mapper's own controls

`MapperType.DisplayInMapper` (on `MSlider`, `MToggle`, `MMenu`, `MKey`) decides
whether a control appears in Besiege's mapper. A mod drawing its own panel can set
it `false` on everything but the key, leaving the mapper as a key binder:

```csharp
NoteSlider.DisplayInMapper = false;      // …and the rest
```

Two caveats. Besiege reads the flag while *building* the mapper's rows, so a
change lands the next time the mapper opens, not while it is up. And if the panel
is a soft dependency — UI Factory absent, or the panel threw while building —
everything must be put back, or the block ends up with no way to be set at all.

Do not ask "is UI Factory here?" every frame: while it is absent the answer costs
a caught `TypeLoadException`, and one of those per block per frame is a bill for
nothing. Ask on a timer until the answer is yes, then stop.

## Driving blocks from a save: timers and variables

Besiege's **Timer** block is `BlockType.Timer` = **66**, and its mapper keys are
`activate`, `emulate`, `automatic`, `hold-to-activate`, `can-stop`, `loop`,
`wait` and `emulation-time` (so `bmt-wait` and the rest in a `.bsg`). With
`automatic` on it starts with the simulation, waits `wait` seconds, and holds
`emulate` down for `emulation-time`.

Both sliders are declared with `AddSliderUnclamped`, so a value past their 60 s
maximum survives a save and a load — a note four minutes into a song is one
timer, not a chain of them.

**Variables are how one block drives another.** An `MKey` serialises as a
`StringArray`: one entry per keycode, then optionally `Ignored=True`,
`Message=<names joined by ';'>` and `Use=True`. `KeyInputController` keeps
`usedMessages`, a table from name to the keys registered under it, so an
emulating key with `Message=foo` presses every key that names `foo`. Unlimited
names, and they cost no keyboard.

**A key with no keycodes is never registered.** `Machine.InitSimBlock` files a
block's keys with `KeyInputController` from inside
`for (i = 0; i < key.KeysCount; i++)`, and `AddMKey` is what puts a key into
`usedMessages` under its variable name. A key written into a `.bsg` with
`Message=` and `Use=True` but no keycode entry therefore registers nothing and
hears nothing — silently, and it looks for all the world like the block not
supporting emulation. Keep a keycode in the array; `AddMKey` files a key under
its name *or* its keys, never both, so the keycode stays inert. In game the case
never arises: `KeySelector.SetVariable` sets the name and leaves the keys alone.

**An emulated key is reference counted.** `MKey.UpdateEmulation` adds one on
press and takes one away on release; `Emulating` is "the count is above nought",
and a press is the nought-to-one edge. So a second emulator firing while the
first still holds the same name raises **no press at all**, and the key does not
come up until the last one lets go. Anything generating a stream of events onto
one key has to leave a gap between them — `tools/make-song.py` uses 60 ms.

A modded block is written with `modId` and `localId`, and the loader
(`XmlLoader.HandleMod`) recomputes the numeric `id` from those two through
`ModIds.GetEffectiveBlockId`, so the `id` attribute in the file is not what
resolves it. `fallback` is the vanilla block shown when the mod is absent.

## Taking the skin picker off a block

`BlockMapper.RefreshLists` shows the mapper's skin control when
`OptionsMaster.skinsEnabled`, the block is the only one selected,
`Prefab.hasBVC`, `Prefab.CanGetNewVisuals`, and the block has more than one skin
option. `CanGetNewVisuals` is
`SkinCanBeChanged && (CanChangeMesh || CanChangeTexture)`, and `SkinCanBeChanged`
is a public field on `BlockPrefab` -- so clearing it takes the row away.

**It also breaks the block menu, and this mod shipped that bug twice.**
`BlockPrefab.SetIcons` reads the same flag and calls
`VisualController.SetPrefabIcons()` only when it is *true*. That call is what puts
a block's own mesh and material on its button in the block menu. Without it the
button keeps `BlockLoader.LoadingMaterial`, which
`BlockButtonCreator.CreateBlockButton` painted on while the mod's resources were
still loading -- so the block shows the **loading texture** in the menu, and
clicking it repaints it from `BlockButtonControl.defaultMat`, which was captured
from the same loading material.

```csharp
BlockBehaviour.Prefab.SkinCanBeChanged = false;      // WRONG
Skins.Hide(BlockBehaviour);                          // in SafeAwake
```

`Skins.Hide` is Special Effects' answer, kept in step with it: build the `MVisual`
the mapper would have built and set `DisplayInMapper = false`, which
`GenericController.CreateContainers` honours. It has to exist before the mapper
first opens, or the game builds it there and shows it once; `RefreshLists` then
takes its reuse path and leaves the flag alone.

One wrinkle: when `StatMaster.collapseSkinMapper` is on, the mapper registers the
*collapsed* skin button before it reaches that gate, so the button can still
appear. Clicking it marks the mapper dirty, after which the full path runs and
finds nothing to show.

## Finding your own block prefabs, and the ids the game gave them

`PrefabMaster.BlockPrefabs` is every block in the game, keyed by the id a machine
file writes. To pick your own out of it, match the prefab's **name**, which for a
modded block is:

```
<mod guid>-<local id>          e.g. aca735ea-a614-4aef-9676-67ec1edd5059-3
```

`BlockPrefabCreator.CreatePrefab` names the prefab object that. `SetupBehaviour`
separately sets `BlockPrefab.name` to the block XML's `<Name>` -- and then
`BlockLoader.RegisterPrefab` calls `BlockPrefab.SetNameFromGameObject()`, which
copies the object's name over it. So by the time a prefab is in the table its
`name` is the guid-and-id string, **not** "Bass". The guid's own hyphens are no
trouble: the last hyphen is the separator, and what follows it is a number.

`prefab.Type` (and `prefab.ID`) is then the id to write into a `BlockInfo` or a
`.bsg`.

Three things that look like they would work and do not:

* **`BlockPrefab.locID`** is `-1` for every modded block. The constructor sets it;
  nothing ever writes it.
* **Arithmetic on your own block's id** (`base + localId`) assumes a mod's blocks
  are numbered contiguously from a known start. They are not, once other mods are
  installed -- it lands in a neighbour's range, and machines come out full of
  somebody else's blocks.
* **Looking for your module's behaviour on the prefab.**
  `ModBlockBehaviourHandler.Awake` adds module behaviours to the block *instance*.
  A prefab is inactive and its Awake has never run, so it carries the handler and
  nothing of yours.

What else does reach a prefab, and is worth having as a second route for a game
that has not renamed it: `nameKeywords`, which `SetupBehaviour` fills with the
block's `<SearchKeywords>` **and the owning mod's `<Author>`**.

The table is not filled when a mod's `OnLoad` runs, so ask again -- a
`MonoBehaviour` that retries once a second and stops when everything is accounted
for costs nothing and is the whole of it.

## Making a machine from inside the game

The MIDI loader block needs four things the modding API does not advertise, and
all four are written up in
[Besiege-Modding-AI-notes](https://github.com/anton-scholten/Besiege-Modding-AI-notes)
now -- 01 for the first two, 12 for the last two:

- **`Modding.ModIO` reaches the mod's own folders and nowhere else.**
  `ModPaths.GetFilePath` combines what it is given with the mod folder -- which
  does let an absolute path through -- and then walks the result's directory
  upwards looking for the mod's own, throwing `Path is not in mod directory!` when
  it never arrives. A second trap in the same method: a resolved path with no
  trailing separator is treated as a *file*, so `GetFiles("")` lists `Mods/` rather
  than the mod's folder, and throws. Both are why the catalogue is read from
  `Mod.xml` and why every folder argument here ends in a slash.
- **`SFB.StandaloneFileBrowser` ships with Besiege** and Besiege never calls it,
  so the system file dialog is available and unproven. Failure is treated as
  ordinary, and the fallback is a `Songs` folder under `Mods/Data/`.
- **Adding blocks is `MachineFileBrowserController.LoadAdditive`**, step for step;
  every member it uses is public, and the result is a selection with the move tool
  up and one undo behind it. `Machine.AddBlocksFromInfo`'s third argument is
  `ref`, not `out`.
- **`XmlSaver.Save` is forbidden by name** and every caller is private, and
  `ModIO` will not write to `SavedMachines` either -- so SAVE adds the blocks and
  opens Besiege's own save screen (`FileBrowserView.Open`, public; the view is
  inactive while closed, so `Resources.FindObjectsOfTypeAll` finds it). The `.bsg`
  writer stays for the fallback and for the build's check; `XData.Type` is already
  the element name the file uses, which makes it a short method rather than a table
  of kinds.

## Moving a block's visual without moving the block

`BlockBehaviour.VisualController.renderers` is the block's own list of
`MeshRenderer`s. Their transforms are children carrying the `<Mesh>` offset and
scale from the block XML; the block's *own* transform is the physics body the
colliders are placed against. Anything that moves a block for show — this mod
swells a block when it plays — writes to the former and must not touch the latter,
or the machine's collision changes with the animation.

A simulation runs on a **clone** of the machine, so read the renderer list from
the instance that is running rather than caching it at load.

## Reading the game's own metadata

Two techniques did most of the work above, both offline:

- **Mono.Cecil** ships in `Besiege_Data/Managed/`. A ~30-line tool can list a
  type's public members or dump a method's IL, which is how `DisplayInMapper`,
  `VisualController`, `ContainerDetails` and the meaning of `upperLeft` were
  found. Run it against `Assembly-CSharp.dll` with the game's own mono.
- **`strings -t d` on `resources.assets`** lists GameObject names in file order,
  and prefab children cluster near their parent. That is where `WideShadow`,
  `TopBar`, `CrossButton` and the rest of the mapper's furniture came from before
  the in-game log confirmed them.

Compiling a throwaway file against the game's assemblies is also a cheap way to
ask whether a member exists: if it compiles, it is there, with the signature you
guessed. The mod's own build script (`tools/build.sh`) shows the reference set.
