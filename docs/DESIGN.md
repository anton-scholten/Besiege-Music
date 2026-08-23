# Orchestra — design draft

A set of instrument blocks for Besiege: piano, guitar, bass, strings, brass,
woodwind, tuned percussion, drums, cymbals. Each block plays one instrument, on a
key, at a note, as a point source you can hear move.

Status: **draft for discussion.** Nothing here is built. Four questions at the
end change large parts of it.

## What the environment allows

Three constraints were measured rather than assumed, and each one rules something
out.

**No file access at runtime.** The mod loader's blacklist refuses `System.IO`
entirely — `File`, `Stream`, `Path`, `BinaryReader`, `MemoryStream`. A SoundFont
player that opens an `.sf2` at startup is therefore impossible, and so is
downloading samples on first run. Audio can only enter through `ModResource`, as
AudioClips declared in `Mod.xml`. What we *can* do is `AudioClip.GetData` into a
`float[]` and run our own sampler over it.

**The in-game compiler is C# 4 and segfaults on `enum`.** Instrument and
articulation constants are `const int`, as in the Braids port.

**Besiege's variables attach to `MKey`, nothing else.** `KeySelector` carries the
variable selector; `MValue`, `MSlider` and `MMenu` have no equivalent. Automation
can therefore *trigger* a block but cannot drive its note number. This is the
single biggest shape constraint on the whole feature — see question 2.

## Sound: three options

### A. Multisamples, extracted offline from an open SoundFont

Pick an open General MIDI SoundFont, extract a handful of samples per instrument
at build time, ship them as `.ogg`, and play them through our own sampler.

The SoundFont itself is never shipped or parsed at runtime — it is source
material for a build tool, which sidesteps the `System.IO` ban completely.

Candidates, all permissively licensed:

| SoundFont | Size | Licence |
| --- | --- | --- |
| FluidR3Mono_GM | 13.8 MB | MIT |
| GeneralUser GS | 29.8 MB | Permissive custom, unrestricted use |
| MuseScore_General (SF3) | 35.9 MB | MIT |

FluidR3 is the usual Linux default and balances size against quality well;
GeneralUser GS is the most efficient per megabyte. Either is a fine source.

Shipped size is set by how many samples per instrument, not by the source font.
Three samples per type, ~2 s, mono, 22.05 kHz Ogg is roughly 20 KB each — about
60 KB per instrument type, so ten instruments at four types each lands near
2.5 MB. That is comfortable for a Workshop item.

Sampling three notes per octave-and-a-bit keeps pitch-shifting inside ±3
semitones, where it is inaudible. One sample stretched across a whole instrument
is what makes cheap samplers sound like chipmunks.

### B. Synthesis on the fly

No data at all. We already have the DSP for it in the Braids mod: Karplus–Strong
for plucked and struck strings, modal synthesis for bells, cymbals and mallets,
noise plus a shaped envelope for drums, and the blown and bowed models from
`digital_oscillator.cc`.

Tiny, endlessly tweakable, and every parameter becomes a real control rather than
a sample switch. But a convincing grand piano or trumpet is a research project,
not a weekend — this route gives an orchestra that sounds synthesised.

### C. Hybrid — the recommendation

Samples for anything whose character is its recording: piano, guitar, brass,
woodwind, strings, choir. Synthesis for anything whose character is its physics
and whose samples are long: cymbals especially, where a 5 s decay is the most
expensive thing in the pack and modal synthesis is both smaller and *better*,
because the decay responds to the block's own parameters.

Drums fall either way — short samples are cheap, but synthesised drums tune
continuously, which suits a machine that revs.

## Point-source audio

The blocks must be heard where they are, in stereo, as the machine moves. The
tempting way to get that is `spatialBlend = 1` and a streaming `AudioClip` fed by
a PCM reader callback:

```csharp
AudioClip.Create(name, length, 1, rate, stream: true, PCMReaderCallback)
```

Unity pulls mono samples from that callback *before* its 3D stage, so distance,
doppler and stereo position all come free from the engine. **This was built, and
then taken out again.** A stream is read well ahead of being heard: the callback
that drains the note queue runs long before the samples it fills reach the
speakers, so every block answered its key late, which is worse than any amount of
free spatialisation is worth.

So the blocks do what the Braids synth already does: `OnAudioFilterRead` on a 2D
source looping one sample of silence — a filter runs in the mixer, on the buffer
about to be played — and `wantLeft`/`wantRight`, a gain per ear worked out each
frame on the game thread from where the block stands relative to the listener.
Doppler is what that costs, and it is the only thing.

## Blocks

Nine, each a separate block so the toolbar reads like an instrument list.

| Block | Types | Beyond the common settings |
| --- | --- | --- |
| Piano | Grand, Upright, Electric, Honky-tonk | Sustain pedal, release length |
| Guitar | Nylon, Steel, Jazz, Clean electric, Overdriven | Pluck position, palm mute |
| Bass | Acoustic, Fingered, Picked, Fretless, Synth | Slap |
| Strings | Violin, Viola, Cello, Double bass, Ensemble | Bowed or pizzicato, vibrato depth and rate |
| Brass | Trumpet, Trombone, French horn, Tuba, Section | Mute, attack hardness, vibrato |
| Woodwind | Flute, Clarinet, Oboe, Bassoon, Sax | Breath noise, vibrato |
| Mallets | Glockenspiel, Vibraphone, Marimba, Xylophone, Tubular bells | Hardness, motor speed on vibes |
| Drums | Kick, Snare, Tom, Rim, Clap | Tuning, decay, damping |
| Cymbals | Crash, Ride, Hi-hat, Splash, Gong | Size, open or closed, choke key |

Common to every block: **Play** key, **Note**, **Volume**, **Range**, **Type**,
and **Toggle** for hold-versus-latch. Range is the existing Max Dist idea — how
far the sound carries.

## Architecture

One mod, `Besiege-Orchestra`, laid out like the siblings: `Orchestra/` as the
folder Besiege loads, `tools/` outside it.

**One behaviour, many blocks.** `InstrumentBehaviour` is shared; what differs per
block is declared in the block XML, exactly as Sound Blocks declares its clip
list:

```xml
<OrchestraMod>
  <Types>
    <Type name="Grand piano" samples="piano_grand" lowNote="21" highNote="108" />
    <Type name="Electric piano" samples="piano_rhodes" ... />
  </Types>
  <Extras>
    <Toggle key="SustainKey" name="Sustain" />
    <Slider key="ReleaseKey" name="Release" min="0" max="4" default="0.4" />
  </Extras>
</OrchestraMod>
```

Adding an instrument is then XML plus samples, with no new code — the same
property that lets people add their own sounds to Sound Blocks today.

**Shared pieces already written**, to be lifted rather than rewritten:

- Key handling that respects emulation, via `KeyEmulationUpdate` — the framework
  pass, so a variable-driven press fires exactly once. Both existing mods do this.
- The compact two-column mapper layout from Sound Blocks, which these blocks need
  more than it does: nine controls will not fit a single column.
- The UI Factory panel and its soft-dependency wrapper from the Braids synth,
  falling back to the stock mapper when UI Factory is absent.
- `DcBlocker` and `ClickShield` from the synth, for the generated voices.

**Sampler.** Per type, a set of `AudioClip`s read once into `float[]` via
`GetData`, a key map choosing the nearest sample, cubic interpolation for the
pitch shift, loop points for sustaining instruments, and an ADSR. Polyphony is
question 3.

**Build tool.** `tools/extract-samples.py` reads the chosen SoundFont, pulls the
named presets at the named notes, trims, normalises, encodes Ogg, and writes the
`Mod.xml` resource block. Sample choice becomes a data file under review rather
than a pile of binaries someone dropped in.

## Multiplayer

Every client simulates every block, so notes trigger locally as they do in Sound
Blocks. Nothing needs to go over the wire, and custom samples would not travel
anyway.

## Decided

1. **Hybrid sound.** Samples for piano, guitar, bass, strings, brass, woodwind and
   mallets; synthesis for cymbals, and for drums where tuning them continuously is
   worth more than the recording. Two engines behind one voice interface.
2. **One block per note.** Each block is tuned to a note and triggered by its own
   variable, which is the plain Besiege idiom and needs no new machinery. Chords
   are several blocks. No sequencer block for now — the door stays open, since a
   sequencer would drive the same keys through the emulation API.
3. **Four to eight voices per block.** Not for chords, which come from separate
   blocks, but so a retrigger does not cut the sound already ringing: a piano note
   struck twice quickly, or a cymbal hit while the last one decays. A voice
   allocator with oldest-first stealing.
4. **Around 5 MB.** Three samples per instrument type, so pitch-shifting stays
   inside about +/-3 semitones and nothing sounds stretched. No velocity layers.

## First slice

The order that gets something audible early and proves the two riskiest parts:

1. The mod skeleton, shared `InstrumentBehaviour`, and the XML type/extras schema.
2. The sampler: `AudioClip.GetData` into `float[]`, nearest-sample key map, cubic
   interpolation, ADSR, the voice allocator.
3. `AudioClip.Create` with a PCM reader callback, on a 3D source, to prove
   point-source stereo works for generated *and* sampled voices before anything
   is built on it.
4. One sampled block end to end -- Piano -- with the sample extraction tool.
5. One synthesised block end to end -- Cymbals -- to prove the second engine.
6. The remaining seven, which by then should be XML and samples.
