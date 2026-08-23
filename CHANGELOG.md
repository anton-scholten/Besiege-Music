# Changelog

## Unreleased

**Fixed**

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
- Every instrument now faces the side a machine is looked at from: the piano shows
  its keyboard, the guitars their faces rather than their backs, the violin its
  front rather than its edge, and the trumpet and saxophone their length rather
  than their bell.
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
