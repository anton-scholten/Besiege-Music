# Changelog

## Unreleased

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

- A block model of its own. All nine share Sound Blocks' speaker mesh, tinted.

**Build**

- A checker that fails to compile now fails the build. Both `XmlCheck` and
  `BlacklistCheck` were built with their output discarded, so a broken checker
  quietly fell back to the previous build's binary and still printed "OK".
- The build's scratch directory is the mod's own; it was named after another
  mod, so two repos shared one set of compiled checkers.
