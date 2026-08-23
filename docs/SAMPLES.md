# Where the samples come from

The sampled blocks — piano, guitar, bass, strings, brass, woodwind — need audio
that is not in the repository. The synthesised ones — mallets, drums, cymbals —
need nothing and work from a fresh clone.

## Why they are not loaded at runtime

The mod loader blacklists `System.IO` entirely: no `File`, `Stream`, `Path` or
`BinaryReader`. A mod cannot open a SoundFont, or anything else, while the game
runs. Audio reaches a block only as an `AudioClip` declared in `Mod.xml` and
fetched through `ModResource`, which is why the samples are cut ahead of time and
shipped as Ogg.

`SampleBank` then reads each clip once, on the game thread, with
`AudioClip.GetData` — the audio thread cannot call into Unity at all.

## Choosing a font

Any of these can be the source; none is redistributed, only the cut samples.

| SoundFont | Size | Licence |
| --- | --- | --- |
| FluidR3Mono_GM | 13.8 MB | MIT |
| GeneralUser GS | 29.8 MB | Permissive, unrestricted use |
| MuseScore_General | 35.9 MB | MIT |

## Cutting them

```sh
./tools/extract-samples.py path/to/FluidR3Mono_GM.sf2
```

Three notes per instrument, 22.05 kHz mono Ogg, two seconds each — roughly 20 KB
a sample, so the whole set lands near 2.5 MB and pitch-shifting never stretches
more than about three semitones.

The naming convention carries the tuning: `piano_grand_60` is that instrument
sampled at MIDI note 60. `SampleBank` reads the trailing number to build its key
map, so a sample can be added or replaced without touching code.

The tool resolves a General MIDI preset to its zones properly: `phdr` to find the
preset, `pbag`/`pgen` for its zones and the instrument each points at, then
`inst`/`ibag`/`igen` for the zone whose key range covers the wanted note, and
`shdr` for the sample itself. Root key comes from the zone's `overridingRootKey`
where it has one, and from the sample header otherwise — which is why the files
are named by the note they actually sound, not the note that was asked for.

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
