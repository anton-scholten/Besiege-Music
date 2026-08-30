# Where the samples come from

Every instrument in the mod but one plays recordings, and they are not in the
repository: they are cut from a General MIDI SoundFont at build time. The one
exception is the tubular bells, which are still synthesised — see below.

## Why they are not loaded at runtime

The mod loader blacklists `System.IO` entirely: no `File`, `Stream`, `Path` or
`BinaryReader`. A mod cannot open a SoundFont, or anything else, while the game
runs. Audio reaches a block only as an `AudioClip` declared in `Mod.xml` and
fetched through `ModResource`, which is why the samples are cut ahead of time and
shipped as Ogg.

`SampleBank` then reads each clip once, on the game thread, with
`AudioClip.GetData` — the audio thread cannot call into Unity at all.

## Choosing a font

**MuseScore_General** is what the shipped samples are cut from — 215 MB, 1246
recordings, most of them at 44.1 kHz, and the most thoroughly multisampled of the
freely available General MIDI fonts. That last part is what decides it: a font
with one recording per octave gives cuts within a few semitones of what was
played, and one with a recording per three octaves gives a chipmunk.

    https://ftp.osuosl.org/pub/musescore/soundfont/MuseScore_General/MuseScore_General.sf2

Any General MIDI font works; none is redistributed, only the cut samples.

| SoundFont | Size | Licence |
| --- | --- | --- |
| MuseScore_General | 215 MB | MIT |
| FluidR3Mono_GM | 13.8 MB | MIT |
| GeneralUser GS | 29.8 MB | Permissive, unrestricted use |

## What is recorded and what is not

The mod had four blocks' worth of synthesis in it, and every one of them has been
measured against a recording and lost:

| Block | Was | Why the recording wins |
| --- | --- | --- |
| Cymbals | modal, 24 partials | A cymbal is hundreds of modes of a thin bronze disc, dense enough to be heard as noise. Twenty-four of them is a bell. |
| Drums | the drum engine | A kick is a recording of a beater on a head in a room. A pitch-dropping sine with noise over it is a drum machine. |
| Mallets | modal | A bar is what modal synthesis is *for*, and it still lost: it cannot do the mallet against the bar or the resonator tube under it. |
| Plucked | Karplus-Strong | A real string with no body. A harp's soundboard, a banjo's head and a sitar's sympathetic strings are most of what those instruments are. |
| Organ, Choir | written by hand | Sine drawbars and formant-filtered saws are the textbook approximations, and a church organ recording is a church organ. |

**The one that stayed synthesised is the tubular bells.** Every General MIDI font
gives them a single recording — this one is a C7 — stretched across the whole
range by the preset's key zones. A bell pitched three octaves down is a slow, dull
imitation of a bell, where the modal engine rings real partials at whatever pitch
it is asked for. For a struck metal tube that is the honest way round.

Two of those engines went with the blocks they served: `drum` and `plucked` are
deleted, not disabled, because an engine nothing plays is arithmetic to maintain
and a claim nobody can hear. `engine` on a `<Type>` chooses between `sampler`,
`modal` and `fm`.

## The kit is cut differently

Percussion is addressed by note number rather than by program — bank 128, preset
0, "Standard" — so `PERCUSSION` in the extractor names a kit note per stem rather
than a preset and three pitches. Each is **one** recording: a drum has no range to
cover, and what a block's NOTE does to it is tune it, which is what a drummer does
to a kit.

They are published at note **60**, which is where a block's NOTE slider starts, so
a drum placed by hand plays the recording as it was recorded. `Song.PieceNotes`
and `make-song.py`'s `DRUM_NOTE` write the same 60, and offset the toms around it
by their General MIDI note so that a kit's six toms stay a kit's six toms.

**The hi-hat is two recordings**, closed at 60 and open at 72, and the block
picks by note. A closed hat is a tighter, drier sound than an open one cut short,
so the OPEN toggle cannot make one out of the other; what it does is choke either.
Both converters write 72 where a score names General MIDI's open hat.

They are also unlooped. The font loops some of them, and a looped cymbal is a
cymbal that never stops; a one-shot rings out on the recording's own tail instead,
which is what `SampleBank.FindTail` is for.

Cymbals, the snare, the glockenspiel and the xylophone are cut at **44.1 kHz**
where everything else is at 22.05. A cymbal is mostly above 8 kHz and a
glockenspiel's partials run past 12: at the lower rate half of both is simply not
there, and a crash cut that way is a hiss. They cost about twice the bytes and are
the samples most worth the room.

## The pianos are not all cut

Every font tried gives GM presets 0, 1 and 3 -- Grand, Bright and Honky-tonk --
**the same sample set**, MuseScore_General included. What separates them is the preset's *generators*: tuning, filter
cutoff, envelope, and for honky-tonk a second instrument zone detuned against the
first. `extract-samples.py` takes the sample a preset points at and drops the
generators, so all three came out of it as one recording, byte for byte once
decoded. Three of the four piano types sounded identical; only the Rhodes did not.

`tools/derive-pianos.py` puts the difference back, from the grand. It reads which
recordings those are, and where their loops start, out of the
`docs/generated-samples.md` the extractor writes, so a change of font moves it
rather than breaking it; the extractor's own upright and honky cuts are deleted,
being the grand's recordings under another root.

* **Honky-tonk** is a piano whose unison strings have drifted apart, so it is the
  grand mixed against a copy of itself 14 cents sharp. The copy is faded out
  before the sustain loop and is not inside it: `Voices` jumps from `loopEnd` back
  to `loopStart` with no crossfade, and a second layer whose phase does not match
  across that jump clicks once a bar. A struck piano is ringing out by then anyway.
* **Upright** is a smaller instrument in a smaller box: two poles rolling off above
  1800 Hz, which leaves it half the grand's 1--3 kHz and a twentieth of its top,
  and a shorter `decay` in Piano.xml for the sustain.

Both are built **from the grand every time**, not from the file they replace, so
running the tool twice is the same as running it once -- a filter applied to its
own output darkens further on every pass. All three types share the grand's
lengths and therefore its loop points.

The other 26 sampled instruments were checked the same way and are all distinct;
the closest pair anywhere else is clean and steel guitar at note 40, which
correlate at 0.51 and are plainly two recordings.

## Cutting them

```sh
./tools/extract-samples.py path/to/MuseScore_General.sf2
./tools/derive-pianos.py
```

Three notes per pitched instrument and one per kit piece: 131 clips, 1.6 MB, mean
stretch **2.8 semitones** from what was actually played.

The naming convention carries the tuning: `piano_grand_60` is that instrument
sampled at MIDI note 60. `SampleBank` reads the trailing number to build its key
map, so a sample can be added or replaced without touching code.

The tool resolves a General MIDI preset to its zones properly: `phdr` to find the
preset, `pbag`/`pgen` for its zones and the instrument each points at, then
`inst`/`ibag`/`igen` for the zone whose key range covers the wanted note, and
`shdr` for the sample itself. Root key comes from the zone's `overridingRootKey`
where it has one, and from the sample header otherwise — which is why the files
are named by the note they actually sound, not the note that was asked for.

**Where several zones cover a note, the one whose root is nearest it wins.**
Taking the first that covered it — which is what this did at first — is how a
trumpet at note 54 came out of a recording of note 64: right instrument,
stretched down most of an octave, audibly slower and duller than the real thing.

`--list` prints every preset in a font, and `--only <stem>` cuts a single
instrument.

**Loops.** Every sample is cut *through* its loop point rather than to a fixed two
seconds, and is not faded — the loop is what the note goes on sounding with. The
points are rewritten as offsets into the cut and scaled to the output rate, then
emitted as a `loops="start-end ..."` attribute running parallel to `samples`.

What the game does with them is the block XML's business, not the extractor's.
`holds="true"` on a type — strings, brass, woodwind — means the loop is a sustain,
held for as long as the key is down. Without it the loop is a ring-out: the note
fades through it over the type's `decay`, which is how the font itself builds a
guitar or a piano. It is also what makes the short recordings usable at all: this
font's synth bass is fourteen milliseconds of audio, and the loop is what turns
that into a note.

**The encoder is checked, not trusted.** Vorbis hands back a slightly different
number of samples than it was given, and a loop that ends past the end of the
decoded clip is one the game throws away. Each Ogg is read back after it is
written and the pair is moved down to fit, keeping its length.
