# Besiege modding notes

Working notes on the parts of Besiege's mod loader this mod leans on, written for
whoever — or whatever — needs the same thing next. Everything here was read out of
the game's own assemblies (`Besiege_Data/Managed/`) or measured in game; where a
fact was expensive to get, the way it was got is recorded with it.

Target: Besiege on Unity **5.4.0f3**, built-in mod loader.

General notes on the loader, the blacklist, resources and UI Factory are in the
sibling mods ([Clippy](https://github.com/ahscholt/Besiege-clippy),
[Git View](https://github.com/ahscholt/Besiege-Git-view)) and are not repeated
here. What follows is what this mod had to find out for itself.

## Docking a uGUI window to the block mapper

**The problem.** Besiege's block mapper is mesh UI drawn in world space, not uGUI.
A mod panel built from [UI Factory 3](https://gitlab.com/dagriefaa/ui-factory-3)
prefabs is uGUI on a `Canvas`. The two cannot share a hierarchy: you cannot parent
a `RectTransform` into the mapper and have it render, sort or lay out. So a panel
that wants to look like part of the mapper has to be a separate window *positioned
against it*, every frame, in screen space.

**The recipe**, as used by `OrchestraPanel`:

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
