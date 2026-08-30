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

## One limiter for the whole band

A block's own output is kept inside a soft knee, and that is not enough. What
clips a song is Unity's mix: sixty `AudioSource`s each handing over a signal that
peaks near one add up to a signal that peaks near sixty, and nothing inside a
block can see that, because a block only ever sees itself.

`Master` is the one number they share. Each block reports the loudest sample it is
about to play and is given the gain everybody is using; the gain is worked out from
the **power** sum, `sqrt(sum of squares)`, because separate notes are not in phase.
Adding peaks instead would say eight notes at 0.7 reach 5.6 and pull the song down
to a sixth, where they really reach about 2.

Three things it has to get right, all of which were wrong first:

* **The release is per buffer, not per block.** Every block asks once a buffer, so
  the step each takes is the release divided between them. Without that, a
  sixty-block machine released sixty times faster than a one-block one — the same
  limiter pumping on the song rather than riding the note.
* **A stopped source has to let go.** Besiege stops an `AudioSource` with nothing
  to play, and a stopped source's filter is never called, so the last peak it
  reported would sit in the total for as long as it stayed quiet. `Master.Quiet`
  is called wherever the source is stopped.
* **It is a buffer late, deliberately.** Waiting for the other blocks would mean
  blocking the audio thread on sixty other callbacks. One buffer is about twenty
  milliseconds and the per-block knee covers the transient inside it.

Measured, with every block at 0.7: one block is left alone, eight are cut to 0.429
and sixty to 0.157, and in both cases the band sums to the 0.85 ceiling exactly.
Silence releases in about 0.8 seconds whether there are eight blocks or sixty.

### And an estimate is not enough

`Master` works from the power sum because separate notes are not in phase. Notes of
*one* instrument in a chord are more in phase than that. Measured on the overdriven
guitar, a six-note chord of one sample reaches 2.75 where the power sum says 2.40 —
so holding the estimate to 0.85 lets the real signal reach 0.98, and a saturated
waveform arriving inside the one buffer `Master` runs late puts it over. That is
clipping you hear standing next to the blocks and not from across the level, where
the distance falloff has already taken it down.

No estimate fixes that, because the size of the error depends on what the notes
are. `BandLimiter` does not estimate: it is a `MonoBehaviour` on the object
carrying the `AudioListener`, so Unity hands it the finished mix and it reads the
peak of the very samples it is about to pass on. Output cannot exceed the ceiling —
the gain for a buffer comes from that buffer's own peak, and the release rises only
as far as that same peak allows.

That last clause is the whole of it, and it was wrong first: computing the headroom
only when the *current* gain would clip left the release free to climb past what the
buffer allowed, and a rising signal walked out over the ceiling one buffer at a
time. It took random noise to show it — every musical test case passed.

It sees the whole game and touches it only when the whole game is about to clip.
Under the ceiling the buffer is passed through untouched rather than multiplied by
one, so Besiege's own audio is bit for bit what it was.

### Why both

`Master` is not made redundant by `BandLimiter`; they do different jobs and the
second is much worse on its own. `BandLimiter` is the last thing before the
speakers, so everything it pulls down it pulls down *including Besiege's own audio*.
Left to face an unlimited band — sixty blocks arriving at several times the ceiling
— it would duck the whole game by fifteen or twenty decibels every time the song
played.

`Master` keeps this mod's own contribution near the ceiling before the mix, where
it can only affect this mod's blocks, and keeps their balance while it does. The
limiter is then trimming a few per cent rather than holding the door shut. Coarse
stage on our own audio, fine stage on the truth.

## Two kinds of volume slider

Besiege has two, and they reach a modded block differently:

* **The per-category sliders** — BLOCKS, SFX, MUSIC — are exposed parameters on an
  `AudioMixer`, written by `MusicController.LateUpdate`. They reach an instrument
  block because its `AudioSource` is routed through a mixer group like any other
  block's.
* **The master slider** sets `AudioListener.volume`
  (`OptionsMaster.SetMasterVolume`, and the slider's own callback). **Unity does not
  apply that to audio coming out of a mixer**, so the one slider a player reaches
  for first did nothing to the band — and does nothing for any mod that gives a
  block an `AudioSource`.

So the block applies it, from `OptionsMaster.BesiegeConfig.MasterVolume`, and only
where the game does not: a source with no mixer group is one the listener's own
volume still scales, and applying it there as well would work the slider twice.

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
