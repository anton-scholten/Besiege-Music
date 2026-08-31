# Changelog

## Unreleased

**Added**

- **The Braids Synth mod is now a block in this one.** Mutable Instruments'
  macro-oscillator, twenty-three models and its own panel, moved in whole: the
  sources sit under `Music/MusicScripts/Braids/` unchanged from the mod
  they came from, licence file included, and the only edit is that what was their
  `Mod.OnLoad` is two lines of this mod's. Its block XML is the same file with a
  new local id and this mod's guid on the module element.
  So the mod no longer *asks* whether Braids is installed: it is here. A machine
  holding synth blocks names one mod where it used to name two, `make-song.py`
  writes them by default (`--no-braids` is what asks for the other block now), and
  `Braids.RequiredMods` is gone. The family a score asks for is called **Braids**
  rather than "Synth", because the FM block took that name and two families
  sharing one would have filled an FM block with Braids' mapper keys.

- **A Synth block**, and the one engine in the mod that is not a recording:
  two-operator FM, a sine bending the phase of another sine, from a 4096-entry
  table. It exists because **22.5% of the notes in the bundled songs -- 10,484 of
  46,638 -- are General MIDI's synth leads, pads and effects**, and nothing
  acoustic stands in for those: without it they went to an overdriven guitar
  (5442 notes) or a string ensemble (5042). Seven types -- Lead, Square lead, Pad,
  Choir pad, Bell, Electric piano, Bass -- and the thing they can do that a sample
  cannot is move while they sound: the modulation index falls from its start
  towards `brightness` of it, so the Bell's partials are
  `1.0 2.5 4.5 6.0 8.0 9.5 11.5` at 0.05 s and have lost their top two by 1.5 s.
  `ratio` is what each sound *is* -- whole numbers harmonic, 3.5 the bell everyone
  knows, 14 the tine of an electric piano -- and each type carries a **measured**
  `level` so seven very different spectra arrive at the 0.20 rms the recorded
  blocks come to. The Braids Synth mod is still preferred where it is installed.
  Model is a MIDI controller by Gabriel Ibias, from Poly Pizza, CC-BY 3.0.

- A **MIDI loader block** -- a download arrow, built out of boxes by
  `tools/make-arrow-mesh.py` rather than modelled -- which does inside the game
  what `tools/make-song.py` does outside it. Click it and Besiege's own menu holds
  the settings (instrument, volume, range, transpose, lead-in, and whether the
  song starts with the simulation or on a key) while a panel docked underneath
  holds a box for the file, a button that opens the system's file dialog, a
  summary of what the score comes to in blocks, and two buttons: **ADD TO
  MACHINE** and **SAVE AS MACHINE**. The panel leads with the folder MIDI files go
  in -- the path itself is a button that opens it in the file manager, with a
  reload arrow beside it so a file dropped in while the game is running can be
  listed without a restart -- and everything in that folder is listed underneath,
  a click to the file -- as a dropdown, which is at the top of the panel because a
  uGUI `Dropdown` parents its open list to itself and one lower down would open
  into the window's own scroll mask. Buttons keep UI Factory's own hover swell, as
  the rest of Besiege's interface has it; only full-width toggles have it taken
  off, where growing the row carries its lettering out of the window.
  Besiege's own mapper keeps the key and nothing else, as it does for the
  instrument blocks: the settings are `< choice >` selectors for the block and the
  instrument within it -- the second refilled whenever the first moves, `MMenu.Items`
  having a public setter -- and slider rows for volume, range, transpose and delay.
  **Delay is the old Lead-in renamed**, and it replaces the "on start" toggle:
  every timer waits its own time from the key, so a key that is bound starts the
  song and one that is not leaves the timers starting with the simulation. Two
  sliders that both meant "quiet before the first note" would have been a bug
  rather than a setting. It starts at nought: a key pressed is a song started,
  and the seconds are for a machine still falling into a level when its key goes.
  The panel is one fixed height with no conditional rows, in the order
  instrument, type, the four sliders, the folder, the file, the summary and the two
  buttons. The folder is a box rather than a button -- it can be pointed at a
  subfolder of the mod's data directory, and a button would have carried UI
  Factory's hover swell, which grows a whole path out of its own row.
  A file chosen from the list is remembered by **name**, not by path: `ModIO`
  takes absolute paths but is meant for paths relative to the mod's own data
  folder, and the relative form cannot be wrong about where that folder is. Four things had to be established for any of
  it to be possible, and each is written up in
  [AGENTS.md](AGENTS.md#the-midi-loader-block):
  `Modding.ModIO` reads and writes **absolute** paths, despite `System.IO.File`
  being blacklisted, because `ModPaths.GetFilePath` combines what it is given with
  the mod folder and `Path.Combine` returns a rooted path whole; Besiege ships
  `SFB.StandaloneFileBrowser` and never calls it, so the system file dialog is
  available but unproven, and the mod falls back to a `Songs` folder in its own
  data directory; adding the blocks is Besiege's own additive load, step for step
  from `MachineFileBrowserController.LoadAdditive`, so they arrive selected and
  one undo removes them; and saving has to write the `.bsg` itself, `XmlSaver.Save`
  being one of the four methods the mod loader forbids outright.
- `tools/tests/SongCheck.cs`, run by the build: it converts a made-up scale with
  the mod's own converter, writes the machine, reads it back, and checks the
  things that decide whether a machine plays or sits there -- a timer per note at
  the right second, a block per voice, a keycode on every variable key, every
  block flat and standing up. It also takes a real file and prints what the
  converter makes of it, which is how the two converters are held against each
  other: they now agree exactly on five real scores. That comparison found a
  tempo bug -- `List.Sort` is not stable, so the assumed 120 bpm could outrank a
  file's own tempo at tick 0 and play the whole score at the wrong speed.
- `tools/make-song.py` builds a machine that plays a MIDI file: an instrument
  block per pitch, a timer block per note, joined by Besiege's variables and laid
  out in a grid. No dependencies -- the MIDI parser is in the tool, and
  `--self-test` builds a scale and checks the result without the game. MIDI
  rather than audio because a recording has to be transcribed first, which is a
  research problem and a neural network away, and a score already is the notes.
  Two things are not obvious, and both were found the hard way: a key written with
  a variable and no keycode is never registered -- `Machine.InitSimBlock` files
  keys from inside a loop over `KeysCount` -- so the first machines were silent
  and looked like blocks that do not support emulation; and an emulated key is
  reference counted, so a repeat starting while the same name is still held
  raises no press at all, which is why every note is cut short of the next on its
  own block. The blocks are turned to stand up, as a block placed on a flat
  surface is in Besiege's own saves, and `--key` starts the song on a keypress
  rather than with the simulation. See [docs/SONGS.md](docs/SONGS.md).
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
- A **TEMPO slider** on the loader block, in beats per minute. It shows whatever
  the file says as soon as one is read and goes back to following the file every
  time another is picked; move it or type into it and that number is what ADD and
  SAVE use instead. Following the file is not the same as asking for the tempo the
  file starts at -- a score that changes speed part way through keeps every one of
  its changes, where a number here flattens the whole of it to one speed -- so the
  two are kept apart by a control of their own, saved with the machine like every
  other setting. The summary says which it is using.
  The panel shows its sliders again whenever the block reads a file, not only when
  it is filled: TEMPO is the first setting here the *block* moves rather than the
  player, and without that the handle kept the last file's number under a summary
  written for the new one until the reload button was pressed.
- The loader block's Start mapping can be a **variable** rather than a key, and the
  song now waits for it. A Besiege key set to a variable does not answer to its own
  keycode at all, so timers written with the keycode were a song nothing could
  start; they are now written with the same `Message=`/`Use=True` the block itself
  carries. The keycode goes in beside it and is never answered to -- a key with no
  keycodes is registered under no name at all, `Machine.InitSimBlock` filing keys
  once per keycode they hold -- which is the trap this mod's emulate keys were
  already written around. `--variable NAME` does the same from the command line,
  and both converters are checked on it.
- `tools/make-song.py` now matches the loader block. Three things differed, and
  each would have made the same file come out as two different machines:
  **`--offset`** defaulted to 1 second where DELAY is now nought; **`--key`**
  defaulted to no key, starting the song with the simulation, where the block's
  mapper starts bound to `M` (`--key none` is the new way to ask for the old
  behaviour); and the two **ordered simultaneous notes differently** --
  `Midi.ByStart` broke ties on pitch alone where the tool sorts the whole
  `(start, length, pitch, velocity, channel, track)` tuple, so the note limit kept
  a different 1200th note and one file came out half a second longer in game than
  from the tool. `ByStart` now compares all six, which also makes it a total order:
  `List.Sort` is unstable, so what it left tied could come out differently between
  two runs of the same file. `SongOptions.Offset` follows the block to nought.
  All four bundled songs now convert to the same note count, block count and length
  in both.
- A list of **where to get MIDI files** in the README -- Online Sequencer, MIDI
  Toolbox, BitMidi, MidiWorld, VGMusic, Mutopia and mfiles. Each was checked by
  fetching a file from it: all seven return a `.mid` with no account and no
  payment. What to look at before converting one is there too, the tempo and the
  note count being the two that decide whether a file is worth a thousand blocks.
- A **NOTE LIMIT slider**, which was a hardcoded 1200 in `SongOptions`. **700**
  by default -- it does not follow the file, the number that matters being how
  many blocks the machine should have rather than how many notes somebody wrote --
  with the handle over the first 5000 and up to 10000 typed into the box. The
  summary redraws when the drag settles, so what a number costs in dropped notes is
  visible before anything is placed. Slider rows can now be whole numbers: a count
  snaps and is shown without decimals, where before only note rows snapped.
- **Songs can ship with the mod.** Anything in `Music/Songs/` is listed in the
  loader's file selector after the player's own, with `(built-in)` in front of it,
  and read out of the mod's own folder rather than its data one -- `ModIO`'s other
  root, and the directory a Workshop subscription downloads into. Nothing needs
  declaring: a MIDI file is read as bytes, and a mod's folder is uploaded whole
  (`UploadData.Path` is `ModInfo.Directory`, `IsFolder` true). The mark is what
  makes it work as well as what it says -- a chosen song is kept by name alone, so
  the name has to say which folder to read it back from, and a bundled `waltz.mid`
  and a player's own of the same name can both be in the list.

**Removed**

- **The `drum` and `plucked` engines**, with the blocks that used them now playing
  recordings. Karplus-Strong was three weeks old and measured well -- it is a real
  string where modal is a bar -- and it still lost to a recording of a harp. An
  engine nothing plays is a page of arithmetic to keep working and a claim in the
  documentation nobody can hear. `pitchDrop`, which only the drum engine read, goes
  with them.
- **Braids' copies of six files this mod already had**: `Log`, `UIF`, `Chooser`,
  `Swell`, `ZoomGuard` and `ClickShield` came across with the block and were the
  same code in another namespace. Four were identical but for the namespace line;
  `UIF` differed by five members, which moved into this mod's; `Chooser` differed by
  a row height and a font size, which are the caller's argument now, so the Braids
  panel draws exactly what it drew before. 46 source files to 40, and the assembly
  from 177 KB to 167 KB.
- Four accessors nothing read: `Catalogue.ModAuthor`, `Master.Reduction`,
  `Master.Playing`, `Midi.TrackCount`, and `UIF.UntranslateAll`, which was dead in
  the mod it arrived from too.
**Changed**

- **The mod is called Music, not Orchestra.** It grew past the name: an FM synth
  and Braids' macro-oscillator are not orchestral instruments, and the loader
  block is not an instrument at all. The mod folder is `Music/`, its sources
  `Music/MusicScripts/`, the assembly `Music.dll`, the namespace `MusicMod`, and
  the block module element in every block XML is `<MusicMod>`. The mod's guid is
  unchanged, so machines built with it still load; its data folder moves with the
  name, from `Mods/Data/Orchestra_<guid>/` to `Mods/Data/Music_<guid>/`, so
  **songs and machines kept in the old folder have to be moved across by hand**.
- The two synth blocks are called **FM Synth** and **Braids** now, rather than
  "Synth" and "Synth Block", which named neither of them and read as a pair of
  the same thing in the toolbar. The FM block's family in a score is `FM Synth`,
  its mesh and texture `FMSynth`, and the converters' General MIDI tables moved
  with it.
- **The FM Synth block faced backwards.** Its model is authored the other way
  round from the instruments, so the quarter turn they take put the keyboard's
  keys on the far side of the block -- a keyboard seen from behind. It takes a
  half turn, and the toolbar tile follows it.

**Fixed**

- **The Braids block turned its own toolbar tile into the loading texture.** It
  cleared `SkinCanBeChanged` to hide the skin picker, and `BlockPrefab.SetIcons`
  calls `VisualController.SetPrefabIcons()` only while that flag is true -- the
  same fault every block in this mod had and the same fix: `Skins.Hide`, which
  builds the MVisual the mapper reads and marks it not for display. It shipped
  that way for as long as Braids was a mod of its own.

- **Every block control was driven through the sampler and measured**, after the
  switch to recordings changed what each one has to act on. Thirteen of them do
  what they say -- Brass MUTE, Guitar PLUCK and palm mute, Strings PIZZICATO,
  Piano SUSTAIN and RELEASE, Brass ATTACK, VIBRATO, Woodwind BREATH, Bass SLAP,
  Drums TUNE and DECAY and DAMPING, Cymbals SIZE -- and three did not:
  * **Hi-hat OPEN could only close.** The recording was a closed hat, so the
    control had nothing to open. The block carries **both hats** now and picks by
    note -- 60 closed, 72 open, each played as recorded -- because a closed hi-hat
    is not an open one with the ring taken off. Both converters write 72 where a
    score names General MIDI's open hat, and OPEN still chokes either of them.
  * **HARDNESS did nothing above half.** It mapped to a lowpass, which clamps at
    nothing once the recording is unfiltered. `Damping` is signed for the sampler
    now: a lowpass one way, and the other way what is left after that pole added
    back, which leans on the top of the recording. A hard beater cannot put
    partials into a sample that has none, but it can lean on the ones there.
  * **Plucked had a Motor slider**, inherited from the mallets' engine, and a
    vibraphone's motor means nothing on a harp. It is **Pluck** now -- where along
    the string it was plucked, the Guitar block's own control -- which is the one
    thing a single recording cannot already be.

- **Every instrument that was synthesised now plays recordings, bar one.** The
  mod had four engines' worth of synthesis in it and each was put beside a cut of
  the real instrument: the cymbals (twenty-four partials against a disc with
  hundreds of modes), the kit, the mallets, and the plucked strings the last
  release added. The recordings win every time, and the two written by hand --
  the organ's sine drawbars and the choir's formant-filtered saws -- are now a
  church organ and a choir. `tools/make-voices.py` is gone with them.
  The one that stayed synthesised is the **tubular bells**: every General MIDI
  font has a single recording of them for the whole range, and a bell pitched
  three octaves down is a slow, dull imitation of a bell, where modal rings real
  partials at whatever pitch it is asked for.
- **The samples are cut from MuseScore_General now**, 215 MB against
  GeneralUser GS's 30, and the whole set was re-cut: 131 clips, 1.6 MB. The
  blocks' own controls still reach the recordings -- Size and Decay set how long a
  cymbal rings on past the end of the cut, Damping is a hand on the head,
  Hardness is the beater, and the vibraphone's Motor is put back by a tremolo in
  the sampler voice, a font having no way to record one.
- **The extractor picks the zone whose root is nearest the note**, where it used
  to take the first zone that covered it. That is how a trumpet at note 54 came
  out of a recording of note 64 -- right instrument, stretched down most of an
  octave. Mean stretch across the set is now 2.8 semitones.
- The kit is cut from bank 128 and published at note 60, which is where a block's
  NOTE slider starts, so a drum placed by hand plays what was recorded. Both
  converters write 60 for every unpitched piece and offset the toms around it by
  their General MIDI note, so a kit's six toms stay six toms. They were written
  at 36 to 78 -- pitches chosen for the synthesised engines, and two octaves down
  and an octave and a half up against a recording.
- Cymbals, the snare, the glockenspiel and the xylophone are cut at 44.1 kHz
  where the rest of the mod is at 22.05: a cymbal is mostly above 8 kHz, and at
  the lower rate half of it is not there at all.
- The build now checks that every recording a block names is declared in `Mod.xml`
  and present on disk, and that a type's loop list is the same length as its
  sample list. A name nothing declares is not an error anywhere in the game: the
  block is simply silent.

- **The harp faced the wrong way and was the wrong colour**, both from the same
  assumption: that the tenth model would be like the other nine. It faces a half
  turn round from theirs, so at the quarter turn the rest take it stood on the
  block edge-on, showing the thin side of its string plane -- the one view of a
  harp that reads as a stick. And it is the only one of the ten with a real baked
  texture rather than a flat colour per material: its `baseColorFactor` is white,
  which is all the converter read, so the game got a white harp where the model is
  mahogany with pale strings. `tools/make-block-meshes.py` now paints a triangle
  with the texel under its own middle wherever a material has an image, quantised
  to sixteen levels a channel; the harp comes to fifty colours in a 64-pixel PNG,
  and the other nine convert byte for byte as before.
- **A synth block was three to four times as loud as the orchestra around it.**
  Both converters wrote Braids' volume slider at the same number as this mod's
  own, and the two do not mean the same thing: a raw saw at full scale against a
  struck bar that decays. Measured, by compiling Braids' oscillator out of its own
  source and rendering it beside this mod's voices -- one second of middle C, RMS:
  raw saw 0.574, raw square 0.863, sine 0.704, triple saw 0.524, saw swarm 0.162,
  against 0.04 to 0.29 for this mod's blocks and about 0.2 for the modal ones. Each
  model is now written with its own trim onto that 0.2, so a synth part sits in the
  band; the swarm, already the quieter, is left alone. `Braids.Trim` and
  `make-song.py`'s table are held together by `SongCheck`, which reads the Python.

- A newly placed loader block starts on **As the file says**, so a song plays on
  its own instruments unless you ask for one block for all of it. Declared in
  `Loader.xml` rather than in code, and only a *newly placed* block reads it -- a
  menu is saved as an index, so a loader already in a machine keeps what it was set
  to. The build now checks that name against the menu the block actually builds: a
  name it does not hold falls back to the first family, which is Bass
  alphabetically, so a typo would have been a silent change of instrument rather
  than an error.
- A **Plucked** block -- harp, koto, pizzicato, banjo, sitar, and it starts on the
  harp -- with **an engine of its own**. It takes the parts that were the worst
  served by the stand-ins: 3661 notes across the bundled songs were going to a
  nylon guitar or, for pizzicato, to a *bowed* violin sample. It was built on the
  modal engine first and sounded like the mallets, because it *was* the mallets:
  modal rings a bank of partials on one smooth tilt, so everything on it is the
  same sound with the treble moved, and no setting there can make a harmonic
  series. The new `plucked` engine is Karplus-Strong -- a burst going round a
  delay line one period long, damped a little each trip -- which is a string
  rather than a struck bar, and the partials say so: banjo comes out
  `0.99, 1.98, 2.96, 3.95` against the modal vibraphone's
  `1.00, 2.26, 3.66, 5.13`. It is also **19 times cheaper than the modal engine**
  it replaced: 0.58% of a core for eight voices. `noise` is now the plectrum
  against the fingertip, `brightness` how dull the loop damping is, and
  `inharmonicity` drives an allpass that stiffens the string, which is what makes
  a sitar buzz.
- **Organ** on Woodwind (a flue pipe is a whistle), **Choir** on Strings and
  **Steel drum** on Mallets. The steel drum is modal like the rest of that block;
  the other two hold a note rather than decaying, which the modal engine cannot do,
  so they are sampled -- and the samples are **written by
  `tools/make-voices.py`** rather than cut from a SoundFont. An organ is a sum of
  sine drawbars, which is what an organ literally is; a choir is detuned saws
  through three formants. No permissively licensed font was on hand to cut from,
  and the one that was is GPL, which is not a licence to ship samples under.
  They loop exactly: the loop length is chosen first and every partial snapped onto
  its grid, because a whole number of *fundamental* periods still clicks -- an
  organ's 16' drawbar is an octave below the fundamental.
- **Synth parts go to the Braids Synth block when that mod is installed.** The
  loader looks for its prefab by the same guid-and-id name this mod finds its own
  by, and writes Braids' own mapper keys -- model, pitch, timbre, colour, attack,
  release -- with the model chosen from the General MIDI program: a raw saw for a
  saw lead, a raw square for a square lead, the saw swarm for a pad, a triple saw
  for the effects. A note outside the five octaves Braids accepts is folded by
  octaves rather than clamped flat, which would turn a bass line into a drone. The
  machine then names both mods in `requiredMods`, or the game would swap those
  blocks for a ballast without saying so. Nothing is referenced at compile time and
  nothing breaks when the mod is absent: the synth parts go to the nearest
  Music block, as before. `tools/make-song.py --braids` does the same, and asks
  rather than detects, a command-line tool having no way to see what is installed.
  On Shelter, with Braids: 70 synth blocks where there were none, and the guitar
  drops from 50 blocks to 22.
- **The modal engine was most of the lag.** It called `Mathf.Sin` once per partial
  per sample -- twenty-four transcendentals a sample -- which measured at **44% of
  a core for one block's eight voices**: four blocks ringing at once was a core
  gone, and a machine full of mallets and cymbals is many more than four. Each
  partial is now a coupled-form resonator, two multiplies and two adds, which is
  the same sine from bounded arithmetic: **11.2%**, near four times faster. The
  bank also shortens as a note dies rather than testing every partial to skip it.
- The master volume read added last time was **a call into Unity per block per
  frame** -- `AudioSource.outputAudioMixerGroup` is native, and
  `SimulateUpdateAlways` runs for every block. The routing cannot change under a
  running block, so it is asked once, and the slider is asked ten times a second
  rather than sixty times a second per block.
- **Songs can play on the instruments they name.** A MIDI file declares what each
  part is with a program change, and the converter threw all of it away: every
  melodic part went to whichever single block was chosen, so a file naming eight
  instruments came out as eight tracks of piano. (Percussion was never in that --
  channel 10 is a kit by convention, and its note numbers have always mapped onto
  Drums and Cymbals, which is why a song with drums and piano did get both.) `Gm`
  is a 128-entry table from General MIDI to `Family:Type`; INSTRUMENT gains an **As
  the file says** entry, `--instrument file` on the command line. Sax.mid goes from
  18 pianos to violins, a sax, a jazz guitar and a bass. The entry is appended to
  the instrument list rather than put first, because the menu is saved as an index
  and every loader block already in a machine would otherwise change what it plays.
  `SongCheck` holds all 128 entries to a block and an instrument this mod has,
  which caught one that named a drum piece the block does not have.
- `tools/strip-drums.py`, which takes everything on channel 10 out of a MIDI file
  and leaves every other channel alone. A timer per note makes percussion the most
  expensive thing in a pop arrangement: the bundled Rick Astley had 3097 notes of
  kit against 4246 for the whole of the rest of the song, so the machine was mostly
  hi-hat and ran out of blocks 49 seconds in. Stripped, the same 1200 notes reach
  88 seconds and no Drums or Cymbals blocks are placed at all. Deltas are
  recomputed rather than patched -- a MIDI time is relative to the event before it,
  so deleting one moves everything after it earlier -- and emptied tracks are kept,
  because `--track N=Piano` addresses them by number.
- A **VARIABLE** box on the loader block, between the summary and the two buttons:
  what the song's variables are named after, `orch_` by default. The blocks listen
  by name, so two songs on one machine need two names or the second song's timers
  press the first song's blocks. It is an `MText`, so it belongs to the block and
  is saved with the machine; a name that could not be a variable name -- `MKey`
  joins names with `;`, so one of those would be read back as two names -- is
  corrected in the box as it is typed rather than quietly ignored when the machine
  is written. `tools/make-song.py --prefix` does the same, with the same check.
- The piano starts on the **upright** rather than the grand -- a hand-placed
  block, the loader's TYPE selector, and a song that asks for "Piano" without
  naming an instrument. Done with a `default` attribute on the module element
  rather than by moving the upright to the front of `<Types>`: the type is saved
  as an index, so reordering the list would have changed what every machine
  already built plays, turning a saved grand into an upright without touching it.
  Any block can name its default; the other eight name none and start on their
  first, as before. `tools/make-song.py` reads the same attribute.
- **Besiege's master volume slider did not reach the instrument blocks.** The
  per-category sliders do -- BLOCKS and SFX are exposed parameters on an
  `AudioMixer` and a block's `AudioSource` is routed through a mixer group -- but
  the master slider sets `AudioListener.volume`, and Unity does not apply that to
  audio coming out of a mixer. The blocks apply it themselves now, read from
  `OptionsMaster.BesiegeConfig.MasterVolume` on the game thread, and only where the
  game does not: a source with no mixer group is still scaled by the listener, and
  applying it there too would work the slider twice. Any mod that gives a block an
  `AudioSource` has the same hole, Braids Synth included; it is the same three
  lines there.
- **A loud song still clipped up close**, with one instrument playing chords --
  the anthem on the overdriven guitar, standing next to the blocks. `Master`
  estimates the band's total as a power sum, which is right for notes that are not
  in phase and wrong for a chord of one sample: measured, six notes of the
  overdriven guitar reach 2.75 where the power sum says 2.40, so the estimate was
  held to 0.85 and the signal reached 0.98. `BandLimiter` sits on the object
  carrying the `AudioListener`, where Unity hands over the finished mix, and limits
  what it can actually see -- no estimate, and no sample can leave above the
  ceiling. It touches the game's audio only above that ceiling; below it the buffer
  is passed through rather than multiplied by one. Checked outside Unity against
  chords, steps, spikes, square waves and noise: nothing gets past, and a quiet
  buffer comes back bit for bit identical. Noise is what caught the one bug --
  taking the headroom only when the current gain would clip let the release climb
  past what the buffer allowed, one buffer at a time.
- **A loud song clipped**, and not in any one block: each keeps itself inside a
  soft knee, but Unity's mix adds sixty of them together and a sixty-block machine
  peaks near sixty. `Master` is one limiter they share -- every block reports the
  loudest sample it is about to play and is handed the gain they are all using, so
  the band gets quieter together rather than the loudest instrument being singled
  out. The total is the power sum, `sqrt(sum of squares)`, because separate notes
  are not in phase; adding peaks would have cut eight notes at 0.7 to a sixth
  rather than to the 0.43 they need. The release is divided between the blocks --
  a first version released sixty times faster on a sixty-block machine than on a
  one-block one -- and a block whose `AudioSource` is stopped gives its place up,
  or the last peak it reported would have held the rest down indefinitely.
- Each loader block keeps **its own file**, saved with the machine. The chosen song
  was a plain field, so the one setting that matters most was the one a saved
  machine forgot, and two loader blocks were handed the same remembered file rather
  than each keeping its own. It is an `MText` now, like every other setting on the
  block: saved, loaded, undone and sent over multiplayer. The last song converted is
  what a *newly placed* block starts with, which is a default rather than an
  override.
- The loader's default **volume is 0.7 and range 300**, matched by
  `SongOptions` and `tools/make-song.py --volume/--range`. Range is wider than an
  instrument block's own default: a song is a field of sixty blocks and is usually
  looked at from further away than it was built at.
- **Three of the four pianos were the same recording.** GeneralUser GS gives GM
  presets 0, 1 and 3 one sample set and separates them with generators --
  tuning, filter, envelope, and a detuned second zone for honky-tonk -- and
  `extract-samples.py` keeps the sample and drops the generators. Grand, upright
  and honky-tonk decoded byte for byte identical; only the Rhodes was its own
  instrument. `tools/derive-pianos.py` now builds the other two from the grand:
  honky-tonk against a copy of itself 14 cents sharp, faded out before the sustain
  loop so the loop does not click, and upright two poles down from 1800 Hz with a
  shorter decay. Both are made from the grand every time rather than from the file
  they replace, so the tool is the same run twice as run once. Upright moves from
  note 34 to 38 to share the grand's cut, which is a line each in Mod.xml and
  Piano.xml and no change to the loops.
  **The other 26 sampled instruments were checked and are all distinct** -- 84
  samples, decoded and compared; the closest pair anywhere else is clean and steel
  guitar at note 40 at r=0.51, which are two recordings of similar guitars.
- The loader's summary ran off the end of the window. Its last line carries both
  note counts and the tempo, which is more than a mapper-width row holds, and every
  label in these panels was set to overflow rather than wrap. That line now wraps,
  with three lines of room -- the rows are laid out to the mapper, which is
  narrower on a smaller screen. It reads "N notes inside another on the same block.
  M notes past the limit were dropped." rather than running the two together with
  an "and".
- The bundled **Chopin Nocturne declared 999 bpm** and played in 13 seconds. The
  fault was in the file, not the converter: Online Sequencer had written a tempo
  meta of 60060 microseconds per quarter where the sequence is 50 bpm (210 quarters
  against a 251-second recording of the same arrangement). Rewritten in place to
  1200000, which is 50 bpm, and it now comes to 257 seconds. Every other download
  from that site in this repository carries a sensible tempo.
- `Midi.StartBpm`, added for the slider above, reported **120 for every file**: it
  took the first mark in the tempo map, which is the assumed 120 the map is seeded
  with, where the one in force at tick 0 is the *last* mark there -- what the
  binary search that times the notes lands on. Caught by
  `tools/tests/SongCheck.cs`, which now prints the file's own tempo and takes a
  tempo to play at, so both converters can be compared on the same real file.
- `tools/make-song.py` played **every file faster than 120 bpm at 120 bpm**. Its
  tempo map starts with the MIDI default, `(0, 500000)`, and `changes.sort()`
  sorted the *tuples*: a file whose own tick-0 tempo is faster is a smaller
  microsecond count, so it sorted ahead of the default and the walk that follows
  took the default as the later of the two. Sorting by tick alone fixes it --
  Python's sort is stable, so the default stays first and the file's own tempo
  wins. `Sax.mid` (160 bpm) was coming out 33% slow, and the in-game converter,
  whose twin of this bug was fixed earlier, disagreed with the tool on any such
  file.
- A generated song came out played on **another mod's blocks** -- Sound Blocks'
  sound block, wherever that mod's ids happened to land. The loader worked out its
  instruments' ids by arithmetic from its own, `base + localId`, which assumes a
  mod's blocks are numbered contiguously and unshared; and it read the local id
  from `BlockPrefab.locID`, which the constructor sets to **-1** and which nothing
  in the game ever writes for a modded block, so the base was out by eleven as
  well. The ids are now looked up by the one thing on a registered prefab that
  says which mod it came from: its **name**, which for a modded block is
  `<mod guid>-<local id>`. A block that cannot be found keeps id 0 and the
  converter refuses to write anything -- a wrong id is silent, and looks like a bug
  in whichever mod owns it.
  Two answers in between were also wrong and are written up in
  `docs/MODDING-NOTES.md` beside the right one: a prefab does **not** carry its
  module's behaviour (`ModBlockBehaviourHandler.Awake` adds that to the instance),
  and `BlockPrefab.name` is **not** the block's `<Name>` by the time the prefab is
  in the table -- `BlockLoader.RegisterPrefab` calls `SetNameFromGameObject`, which
  copies the prefab object's name over it. Matching on the `<Name>` therefore found
  nothing at all: every instrument came back without an id, the loader refused to
  convert with "this game has no id for the Bass block", and the summary had no
  block count to show.
- **Blocks showed the loading texture in the block menu.** Taking the skin picker
  out of the mapper by clearing `BlockPrefab.SkinCanBeChanged` was the wrong flag:
  `BlockPrefab.SetIcons` reads it too, and calls
  `BlockVisualController.SetPrefabIcons()` -- what puts a block's own mesh and
  material on its button -- only while it is true. Without that the button keeps
  `BlockLoader.LoadingMaterial`, painted on by `BlockButtonCreator` while the mod's
  resources were still loading, and clicking it repaints from
  `BlockButtonControl.defaultMat`, captured from that same material. The blocks are
  now left skinnable and the mapper's control is hidden instead, by building the
  `MVisual` the mapper would have built and setting `DisplayInMapper` false --
  Special Effects' `Skins.Hide`, kept in step with it.
- The folder box could put anything on disk. Whatever it held was written to the
  mod's data folder as a directory name every time the panel opened, and something
  was filling it with fullwidth digits -- six empty folders named `４４４４…` in a
  real install. A folder name is now checked before it is stored (letters, digits,
  space, `- _ . /`), an empty box means "leave it alone" rather than "store
  nothing", and only the default `Songs` folder is ever created: one the player
  named and that is not there is a mistake to report.
- The folder box also lost its contents on a click. It holds the folder's *name*
  now, which is the part worth typing, with the whole path written out underneath
  where it can be read; a click that finds it empty puts back what it had, since
  nobody has typed yet at that point.
- The loader block's toolbar icon kept the wrong picture after the mesh was fixed:
  `BlockTypeIconCreator` renders a modded block's thumbnail once into
  `Mods/Thumbnails/Blocks/<mod guid>_<block id>.png` and **nothing invalidates
  it** -- not a new mesh, not a new `<Icon>` pose. Delete that file (or the folder)
  to have it drawn again; a mod cannot, `ModIO` reaching only its own folders.
- The loader could not read the mod's own block XMLs, so its instrument and type
  selectors were empty and every conversion said *the instrument blocks could not
  be read*. `ModIO.GetFiles("")` is the obvious way to list a mod's own folder and
  is the one thing that cannot work: `ModPaths.GetFilePath` treats a resolved path
  with no trailing separator as a *file*, so it listed `Mods/` -- and then threw,
  that being outside the mod's directory. The catalogue is read from the block list
  in `Mod.xml` now, every folder argument carries its slash, and the whole of it is
  logged once, with counts, so the next fault of this kind names itself.
- **A mod may not read or write outside its own folders at all.** The same method
  walks a resolved path's directory upwards looking for the mod's own and throws
  `Path is not in mod directory!` if it never arrives -- so an absolute path from
  the file dialog was never going to open, and neither was `SavedMachines`. Reading
  is now always by name inside the songs folder, and the file dialog says so when
  what it was handed is somewhere else. Saving goes through Besiege's own save
  screen instead (`FileBrowserView.Open`, public), which also names the file, asks
  before overwriting and draws the thumbnail.
- The loader block's arrowhead was wound inside out -- all six of its triangles --
  so Besiege culled them and the block was hollow where the head should be, which
  read as a texture fault. `make-block-meshes.wind` cannot catch that: it turns a
  triangle whose winding disagrees with its *shading* normal, and a generated mesh
  works its normals out from the winding, so the two always agree. The arrow tool
  now holds every solid to faces that point out of it, and says which one is wrong.

**Changed**

- Every selector in the mod -- the instrument blocks' instrument, the loader's
  block and instrument, and its file list -- is now the same `Chooser` that Braids
  Synth and Special Effects use: a `< name >` row whose middle opens the whole list
  at once, parented to the canvas so nothing clips it. UI Factory's own `Options`
  steps one at a time, which is forty clicks through a folder, and its `Text
  Dropdown` hangs its list inside the window's scroll mask.
- The loader's folder box can be typed into properly: a single click leaves a
  caret where it was clicked rather than the whole path selected for the next key
  to wipe, and a double click takes the lot. This Unity is older than
  `InputField.onFocusSelectAll`, so it is done on the click.
- The folder path under the loader's FOLDER box wraps onto three lines rather than
  two, with a little air between them (`lineSpacing` 1.2): a Steam library on
  another drive runs past two, and a path cut off mid-way hides the half that says
  which install it is.
- Text that is read or typed is a size larger (15pt, up from 13): every input
  field in both panels, every toggle's lettering, and the folder path under the
  loader's folder box, which was 10pt and is now 13 across two lines.
- Every toggle's lettering grows under the pointer, as the selectors' does. The
  prefab's own swell grows the whole row, which on a full-width toggle carries the
  words out of the window, so it comes off and a `Swell` on the caption goes on --
  the pairing is written up in the shared notes as the house style, along with how
  a selector is built.
- The file dialog button is gone. `SFB.StandaloneFileBrowser` can show the whole
  disk and `ModIO` can open none of it outside the mod's own folders, so it could
  only ever hand back something unreadable. The folder button and the reload arrow
  are what is left.
- The loader block's icon is posed like the nine instruments -- their pose rolled
  15 degrees back in the picture plane, so the shaft stands upright while the light
  still falls where it does on the others -- and drawn a fifth smaller, the arrow
  being the tallest thing in that row.
- The two panels share one `DockedPanel` base: the canvas, the window, the
  measuring of Besiege's mapper and the placing against it, **and the slider rows**
  -- widget, formatting, typed values, and the commit that reconciles a mapper
  setting's live and loaded halves -- are written once, and each panel says only
  which control each row stands for.
- `tools/build.sh` builds into a scratch *directory* rather than under a scratch
  *name*. An assembly is identified by its name once loaded, and building to
  `Music.<pid>.dll` named the assembly after the process, so nothing could
  reference it.
- `docs/MODDING-NOTES.md`: what this mod had to work out about Besiege's modding
  API, written for whoever needs the same thing next -- how to dock a UI Factory
  window to the block mapper, with the mapper's measured geometry and the three
  rules that look right and are not; `DisplayInMapper`; moving a block's visual
  without moving the block; and the two offline techniques (Mono.Cecil against
  `Assembly-CSharp.dll`, `strings` against `resources.assets`) that answered most
  of it. The README follows the other mods' shape, and says what the panel does
  now rather than what it used to.
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

- The blocks' module elements were given a `modid`. This was first recorded here
  as a fix, and it was not one: `CustomModules.DeserializeBlockModules` resolves a
  module element *without* a `modid` against the mod that owns the block XML, so
  omitting it was never a fault. The attribute exists to let a block use a module
  some other mod registered, and when it is present it is the only thing
  consulted — so a wrong one is fatal where none at all is fine. The build checks
  it only where it appears.

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
