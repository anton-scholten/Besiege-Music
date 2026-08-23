# Changelog

## Unreleased

**Added**

- A block swells when it plays: 12% larger over 50 ms, back to its own size over
  the next 220 ms, restarted by every note so a repeated key beats rather than
  sits still. It is the visual that moves and not the block -- the swell is
  written to the transforms the block's visual controller lists, while the
  colliders stay on the block's own transform, so a machine does not turn springy
  because it is playing. During a run only, and on game time, so a machine watched
  in slow motion swells in slow motion.
- A note still being held goes on breathing: half the depth of the strike, one
  breath every 1.3 seconds, swelling in from nothing as the strike dies away and
  easing back out over a third of a second when the note is let go, so a bowed
  note is one movement from the bow landing to the bow leaving. Only the blocks
  that can hold a note do it, which is the same set that has a Toggle.

**Changed**

- The panel is docked to the block mapper: the same width, its top edge against the
  mapper's bottom, following it as it is dragged. It has no title bar of its own
  any more -- no name, no cross -- because it is the lower half of a window that is
  already titled, and the mapper opens and closes it. LISTEN moved to the foot,
  bottom left, with the block's toggles sharing the rest of that row equally.
  Measuring the mapper is the whole of the trick: it is NGUI in world space, so the
  join is made by projecting the widest thing it draws -- its own frame -- through
  the camera that draws it. (Not `upperLeft`/`lowerRight`, which are public and
  look like exactly what that wants; they are found by tag and are the corners of
  the screen the mapper may be dragged within.) The window's remembered corner went
  with the drag, and `Prefs` with it.
- DECAY, RELEASE and ATTACK take any time up to a minute, typed, with a little more
  under the handle than before -- 3 seconds of decay, 6 of release, 1.5 of attack.
  `<Extra dragMax="...">` is what separates the two: what the handle covers, where
  the setting itself takes more.
- The toggles are drawn half again as tall, and the window is the height it was:
  the extra came out of the space that was under them.
- NOTE and RANGE accept more than their handles cover. RANGE will take any
  distance from half a metre to two kilometres, typed, while the handle still runs
  over the 5 to 500 that anybody drags through; NOTE takes the whole MIDI range,
  with the handle over a piano's 88 keys. A typed value past the end parks the
  handle there. The trumpet's ATTACK reaches a full second rather than 0.3.
- The panel is narrower -- 434 rather than 470 -- by way of the caption column,
  which was half again as wide as the longest caption in it. The sliders sit
  beside their names now instead of across a gap, and they are the same length as
  before: only the space between went.
- With UI Factory installed, a block leaves Besiege's own mapper holding nothing
  but its key: the panel draws every slider, menu and toggle already, and two
  panels for one block is one too many. Rebinding stays with the mapper, which is
  the only thing that can capture a key. Without UI Factory -- or if the panel
  gives up building itself -- the mapper keeps the lot, which is what makes it a
  fallback rather than a second copy.
- The instrument selector is centred on the slider column and the number beside it
  rather than started where the sliders start, and its arrows stand clear of the
  name again: scaling them up had grown them inwards over the gap the prefab
  leaves.
- The instrument selector is narrower than the row it sits in, which is what puts
  its arrows beside the name rather than out at the margins -- they are anchored
  to its own ends -- and they are drawn a fifth larger. Scaled rather than
  resized: they sit inside a control this panel did not lay out, and scale is the
  one change that cannot land them on top of the name between them.
- LISTEN moved to the left-hand corner of the title bar, opposite the close cross
  rather than beside it. The title is inset clear of both corners now, so a long
  one no longer runs underneath a button.
- The `PLAY <key> — rebind in the mapper behind` line at the foot of the panel is
  gone, and with it the block property that fed it.
- The Toggle is gone from the piano, both guitars, the xylophone, the drum and the
  cymbal. It latches the key down, which is worth having on something bowed or
  blown and means nothing on something struck: those notes die on their own
  whatever the key is doing, so the control was one that changed nothing anybody
  could hear. Which blocks get it is read from their own XML -- a block whose every
  type is struck has no Toggle -- rather than listed in code.

**Fixed**

- The panel did not dock and did not follow the mapper. When the mapper measured a
  different width than the rows had been laid out to, `Dock` set the rebuild flag
  and returned *without placing the window* -- and the same flag is what let it run
  at all, so it never placed the window again either. It rebuilds at the new width
  and places itself in the same frame now. Docking also moved to `LateUpdate`, so
  the panel is put against a mapper that has already moved this frame rather than
  one frame behind it.
- The panel docks to the mapper's `Background`, the tallest of the three that share
  its width. Two earlier rules were wrong and both shipped: the widest thing the
  mapper draws is `WideShadow`, an eleventh wider than the window and higher up,
  which put the panel across the mapper's lower half; and `Visual`, which sounds
  like a frame, is a 93-pixel button, which made the panel a narrow strip beside
  it. The geometry was settled by having the panel log every part it measured --
  it still says which one it docked to, once a session.
- Breath sounded like static rather than air. The noise it mixes in was the whole
  spectrum; it is a band now -- one pole takes the fizz off the top, a second the
  rumble off the bottom -- which is the part of it that sounds like a player.
- A drum with a long decay and little damping was left hissing. Its noise kept the
  same brightness all the way down, so what should have been the tail of a struck
  head was a bright fizz under a fading tone. The filter closes as the noise dies,
  the way high frequencies leave anything struck first, and the noise's own decay
  is capped at a second and a half however long the body rings -- without which the
  new range would have left seven seconds of it.
- Slap left a hiss on the tail of every bass. The noise it adds was mixed in at a
  fixed level, and these recordings are loudest at the strike and much quieter in
  the window they ring out through -- so the same hiss that was 3% of the attack
  was 8 to 24% of the ring, arriving as the note died. It is mixed in proportion to
  what the recording is doing now, through a peak follower quick to rise and slow
  to fall, which holds it at the attack's own 3 to 5% throughout. Breath on a flute
  is unchanged: that recording holds its level, so a proportion of it is what it
  always was.
- Every block answered its key late. The sound was fed to Unity through a
  streaming `AudioClip` with a PCM reader callback, which is read well ahead of
  being heard: a note queued by a keypress was rendered into audio that would not
  reach the speakers for some way yet. The blocks now write into
  `OnAudioFilterRead`, which the mixer calls on the buffer it is about to play, so
  a press is heard in the next block of samples. Reported against the piano and
  checked on all nine: nothing was the piano's own — its samples start on the
  transient, and its 2 ms attack was never what was heard.
- That callback runs *after* Unity's 3D stage, so the source is 2D now and the
  block places itself: a gain per ear worked out each frame from where it stands
  relative to the listener, on the same falloff the source used to be given — full
  volume within a metre, silent at RANGE — and slid across the buffer so a turning
  camera is not heard as a staircase. Doppler is the one thing lost; it was Unity
  resampling a clip, and there is no longer a clip.
- The panel showed a scrollbar down its right-hand side with nothing to scroll.
  UI Factory's Window prefab ships a scroll view, and the rows were being built
  *over* it rather than in it, leaving it holding the prefab's own 500-unit
  placeholder. They go in its content now, sized to what was built, and Besiege's
  scroll view hides both bars for contents that fit.
- The panel's title bar read ACA735EA-A614-4AEF-… . That is the id of the mod that
  owns the block, and `BlockPrefabInfo.Name` — the only name the modding API offers
  for a block — is what it hands back. The block XML says what the block is called
  a few lines further up, so the module declares it too, as `block="Piano"`, and
  `XmlCheck` holds the two to each other.

- Bass "Synth" was a click and the top note of the electric piano nearly so: 14 and
  59 milliseconds of audio. They are loop-based recordings, and the extractor only
  took loop points for the sustaining families, so what shipped was the attack and
  nothing else. Every sample is now cut through its loop, and a loop on an
  instrument that does not hold is a ring-out rather than a sustain — the note
  fades through it over `decay`. Synth bass goes from 14 ms to 1.2 s, the Rhodes'
  top note from 59 ms to 3.1 s. Re-cut from the same GeneralUser GS the pack came
  from, which was confirmed by checking that all 87 zone lookups still resolve to
  the same recordings.

- Nine of the shipped loops pointed past the end of their own clip, the tuba's
  middle note among them — so those notes did not sustain at all. Vorbis does not
  return the number of samples it was handed; the extractor now reads back what it
  writes and moves the loop down to fit, and the game trims a loop rather than
  discarding it.

- What `sustains` meant was two things at once: that the key damps the note, and
  that the loop holds it up. A piano wants the first and not the second. They are
  `damped` and `holds` now.

- Guitar and bass notes stopped rather than ended, and a held piano note stopped
  dead at two seconds. The recordings are cut where the font's sample ended or at
  two seconds, whichever came first, and the instrument is nowhere near silent by
  then -- the overdriven guitar is still at full level. A voice that reaches the
  end of one now turns round in a window at its end and fades, so the note rings
  out: a whole number of periods of the pitch it was recorded at, tuned to the
  seam and level-matched so neither the wrap nor the turn is heard. That is the
  fallback now that every sample carries the font's own loop points, and stands for
  a clip dropped in by hand. Guitar notes go from 0.5-1.4 s to 1.9-3.6 s, a struck
  grand piano from 2 s to 5.5 s, and how long the rest of the note takes is each
  type's `decay` in the block XML.
- The panel could be dragged off the screen, taking the title bar that drags it
  with it, and it remembered being out there. It is now held to the screen while
  it is dragged, not only when it opens.
- A piano's SUSTAIN toggle was labelled PALM MUTE — whatever the last block with
  the same number of controls had called it. The window is kept and rebound when
  the next block has the same shape, and the captions were written once, when the
  rows were made, rather than from the block each time it opens. Every row's
  caption now comes from the control it is bound to and is rewritten on every
  open, beside the values, the ranges, the type list and the title.
- Log lines were prefixed `[BraidsSynth]`.

**Added**

- Each block wears its own instrument. Nine low-poly models from Poly Pizza, all
  CC-BY 3.0 and seven of them by one author, converted by
  `tools/make-block-meshes.py`: glTF's Y-up frame swapped for the block's Z-up
  one, stood upright, scaled to the block, and their flat material colours
  gathered into a palette texture a few dozen bytes across. The models stay at
  their source; the script fetches what it needs. `--preview` renders each block
  so a pose can be judged without starting the game -- once its camera was
  building the up vector from the wrong cross product, which is how five blocks
  came to be turned over that did not need it, and went into the game standing on
  their heads.
- The toolbar icons show each block three-quarters on and the right way up,
  rather than from overhead. The `<Icon>` rotation was Sound Blocks', which suited
  a speaker that looks the same from every side; a trumpet does not.
- The icons were photographing the backs of the blocks, and from underneath: the
  piano showed its legs, the drum its bottom head, the xylophone its base. The
  toolbar's camera turned out to look along the opposite axis to the one the
  preview assumed, so undoing the icon rotation put the camera exactly where it is
  not. `icon_camera` now negates, having been calibrated against the game, and
  each block carries its own pose rather than all nine sharing one: front and from
  above for anything with a face, steeply from above for the three that are struck
  there, and profile for the trumpet and saxophone, which are the same shape from
  either side.
- Every instrument now faces the side a machine is looked at from: the piano shows
  its keyboard, the guitars their faces rather than their backs, the violin its
  front rather than its back, and the trumpet and saxophone their length rather
  than their bell.
- All nine models were mirrored, in the world and in the toolbar alike. glTF is
  right-handed and Unity is left-handed, and the swap that brought a model into
  the block's frame preserved the handedness instead of flipping it -- so every
  instrument arrived correct in every measurement and reflected: a piano with its
  long side on the wrong hand, a violin with its chin rest on the wrong cheek. The
  swap reflects now. The yaws in `POSE` changed sign with it, a reflection and a
  turn not commuting.
- The trumpet's bell lifts off the horizontal, the xylophone lies along its tile,
  and the drum head and cymbal face right rather than left. All four are rolls in
  the picture plane rather than turns of the block -- a drum is round, and looks
  the same however it is spun about its own axis, so only the picture can turn.
  The piano's icon is drawn at 0.45: a grand is the widest of the nine.
- Every toolbar icon is turned to face right, into the light: the toolbar lights a
  block from beside the camera and to that side, and the instruments had their
  faces in the shadow. They sit about 37 degrees off the camera now, far enough to
  be lit and not so far as to show only the edge of something flat; the trumpet and
  saxophone point their bells the same way. The drum's icon is drawn at 0.4 rather
  than 0.5 -- a drum is as wide as the block, and filled its tile edge to edge.
- The preview was drawing every block mirrored. Its projection took the screen's
  right as `cross(up, eye)`, which is a right-handed frame's answer, and these are
  Unity's left-handed coordinates. It had been wrong from the start and only showed
  once a pose was judged by it: "further right" in the preview was further left in
  the game, and one round of icons was turned the wrong way on the strength of it.
- `XmlCheck` reads the mesh tool's icon table out of the Python and holds each
  block's `<Icon>` rotation to it. The two have to say the same thing -- a preview
  drawn from a stale pose is a picture of a block that does not exist -- and they
  had already drifted once.
- `XmlCheck` now holds a block's `<Mesh>` and `<Texture>` names to what Mod.xml
  declares, and both to the files on disk. A name Besiege does not know is a block
  with no shape, and nothing else would have said so.

- Every slider's number is a box that can be typed into: UI Factory's Input
  Field, which brings the game's own look and the behaviour that stops Besiege
  acting on what is being typed. The note box takes a name or a number -- "C4",
  "F#3" and "61" are all the same note.
- The panel's title says which block and which instrument: "PIANO - GRAND",
  "BRASS - SECTION". A type that ends in the block's own name loses that half,
  since the title has already said it.

- A speaker in the panel's title bar, beside the close cross: it plays the block
  where it stands, with the settings as they are, so an instrument can be chosen
  by ear while the machine is being built rather than by starting a run. The note
  releases itself after a second or so, and the mark lights while it sounds. UI
  Factory's Icon Button, which is what the cross itself is; the speaker is drawn
  by `IconArt`, since UI Factory's sprite set cannot be listed.
- The panel opens where it was last closed. The top-left corner is what is kept,
  so a block with more rows than the last one still starts in the same place, and
  it is kept in Besiege's own per-mod configuration, so it survives a restart.

**Removed**

- `UIF.StopZoom` and `UIF.Draggable`, and the panel's call to the first. The
  Window prefab already carries `StopsZoomWhenHovered`, and its top bar already
  carries a `Drag` targeting the window; both were doing nothing. With them the
  unused prefab names, colours and font accessor.

First cut. Nine instrument blocks driven by one shared behaviour.

**Working**

- Mallets, Drums and Cymbals, entirely synthesised — modal synthesis for metal,
  a falling pitched body plus noise for drums. No audio data needed.
- Eight voices per block, oldest stolen first, so a retrigger does not cut the
  note still ringing.
- Point-source audio: a streaming clip fed by a PCM reader callback on a fully 3D
  source, so Unity applies distance, doppler and stereo position itself.
- Besiege's key emulation, through the framework's `KeyEmulationUpdate` pass, so
  automation variables drive a block exactly as a keypress does.
- Instruments declared in block XML — types, engines and extra controls — so
  adding one is data, not code.

- Piano, Guitar, Bass, Strings, Brass and Woodwind, sampled. 84 clips cut from
  GeneralUser GS by `tools/extract-samples.py`, three notes per instrument, 776 KB
  in total — so pitch-shifting never stretches more than about three semitones.
- The extras that shape a sound are read: Tune, Decay and Damping on drums, Size,
  Open and Hardness on cymbals and mallets, the vibraphone Motor, and Sustain and
  Release on the piano. Each means something different per engine — Size on a
  cymbal lengthens the decay *and* spreads the partials, because that is what a
  bigger plate does.

- Sustaining instruments really sustain. Strings, brass and woodwind are cut
  through their loop point and looped in the game, so a held note holds instead
  of running out after two seconds. Piano, guitar and bass are left unlooped
  deliberately — they decay on their own, and looping one would be wrong.

- Every control now does something. Vibrato is a pitch wobble of about fifty
  cents; Mute and Palm mute are the same low-pass, because a hand on the strings
  and a mute in a bell amount to the same thing; Pizzicato drops the loop and
  cuts the tail rather than pretending to be a second recording; Breath and Slap
  mix in noise; Pluck combs the sample where a string would be struck.
- Nine tinted textures, one per family, so the blocks are told apart in the
  toolbar — warm for struck and plucked, cool for bowed and blown.

- A UI Factory panel, shared by all nine blocks. It builds itself from whatever
  controls a block registered rather than from a list of its own, so declaring an
  Extra in a block's XML gives it a row. Instrument choices are buttons, the
  chosen one lit; notes read as note names and snap to semitones.
- UI Factory is a **soft** dependency: without it there is no panel and the stock
  mapper does the job, exactly as before.

**Fixed before release**

- The blocks still had nothing to customise, and this was the real cause: every
  `[XmlAttribute]` on a module class is *required* in the XML unless it also
  carries `[DefaultValue]`, and none of them did. Besiege rejected all nine block
  XMLs outright — `InstrumentType ... must have loops attribute!` — so the blocks
  never existed. Optional attributes are now marked the way Besiege's own modules
  mark theirs, and `XmlCheck` reads those markers off the module source and holds
  the block XMLs to them.

- The blocks' module elements carried no `modid`,
  so Besiege could not tell which mod owned `<OrchestraMod>` and never attached
  the behaviour. The build now checks every block's `modid` against `Mod.xml`.
  (This was real, but on its own it would not have shown the blocks either.)

- None of the blocks appeared in the toolbar: the block XMLs were written from
  scratch and lacked `BasePoint`, `Colliders` and `AddingPoints`, which Besiege
  requires. The build's XML check now asserts them, and the geometry is taken
  from a block known to work.

**Not yet**

- Nothing outstanding on the blocks themselves.

**Build**

- A checker that fails to compile now fails the build. Both `XmlCheck` and
  `BlacklistCheck` were built with their output discarded, so a broken checker
  quietly fell back to the previous build's binary and still printed "OK".
- The build's scratch directory is the mod's own; it was named after another
  mod, so two repos shared one set of compiled checkers.
