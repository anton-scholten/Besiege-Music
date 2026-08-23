# Machines that play a song

`tools/make-song.py` turns a MIDI file into a `.bsg` — a Besiege machine that
plays the score on Orchestra blocks.

```sh
./tools/make-song.py travelers.mid --instrument "Piano:Grand piano" --install
```

It has no dependencies: standard Python, and the MIDI parser is in the file.

## Why a score and not a YouTube link

A recording has to be turned back into notes before anything can play it, and
that is **polyphonic transcription** — an open research problem. The best of the
free models (Spotify's `basic-pitch`, Magenta's `onsets-and-frames`) are a
~100 MB neural network each, want `tensorflow` or `torch`, and on solo piano get
most of the melody, some of the accompaniment, and a scattering of notes that are
not there. Piano is their best case; anything with drums or several instruments is
worse. On top of that a YouTube video has to be downloaded and demuxed first,
which is another dependency and someone else's terms of service.

A score is already the notes, exactly, with their lengths and their tempo — the
thing transcription is trying and failing to recover. So the tool takes MIDI.

**Getting a MIDI.** MuseScore exports one from any score in its library
(*Download → MIDI*, or File → Export in the desktop app). Most game and film
soundtracks have a transcription there already. For the Outer Wilds example:
<https://musescore.com/user/36615991/scores/20587660>.

If you would rather transcribe audio anyway, the tool still takes it from there:
run `basic-pitch` over a downloaded track and hand the MIDI it writes to
`make-song.py`. Expect to edit it.

## How the machine works

Two kinds of block, laid out in a grid on one vertical plane:

- an **instrument block** per distinct pitch, set to that note and nothing else —
  an Orchestra block plays one note, so a tune is a row of blocks;
- a **timer block** per note in the score, `automatic` so it starts with the
  simulation, `wait` set to the note's moment and `emulation-time` to its length.

They are joined by **variables**, not keys. A Besiege key can carry a *message* —
a variable name — and `KeyInputController` keeps a table of which keys listen to
which name, so a timer emulating `orch_042` presses every key that names it.
Keyboard keys would do the same job and there are about a hundred of them;
variable names are unlimited, which is what a song needs.

The `wait` and `emulation-time` sliders are declared with `AddSliderUnclamped`, so
a note four minutes in is one timer with `wait=240`, not a chain of them.

Nothing is connected to anything. The blocks are laid out, not built: Besiege
loads them all the same and they drop to the ground when the simulation starts,
which does not stop the music. Raise `--height` if you would rather they fell
further, or lower it to keep them still.

## Repeated notes, and why there is a gap

An emulated key is **reference counted**: `MKey.UpdateEmulation` adds one on
press and takes one away on release, and a key is "down" while the count is above
nought. A second timer firing while the first still holds the same name takes the
count from one to two — which is not a press, so the repeated note would never
sound, and the note would not end until the last timer let go.

So the tool separates the score per block: every note is cut short of the next one
on its own block by `--gap` (60 ms by default), and a note that would start inside
that gap is dropped and counted rather than lost silently. Sixty milliseconds is
below what a listener notices as a shortened note and above a fixed step, which is
what the counter is sampled on.

## Options

| Option | What it does |
| --- | --- |
| `--instrument Family[:Type]` | the block for every track. `Piano`, `Strings:Cello`, … |
| `--track N=Family[:Type]` | the block for one track, repeatable |
| `--tempo BPM` | ignore the file's tempo map |
| `--transpose N` | in semitones |
| `--offset S` | quiet before the first note (default 1 s) |
| `--from S`, `--seconds S` | play part of the score |
| `--gap S` | silence between two notes on one block (default 0.06 s) |
| `--limit N` | most notes to place (default 1200) |
| `--columns N`, `--spacing N` | the grid |
| `--height N` | where the machine spawns |
| `--volume N`, `--range N` | scales every block's volume; how far they carry |
| `--no-drums` | treat channel 10 as pitched rather than as a kit |
| `--install` | write into Besiege's `SavedMachines` as well |
| `--self-test` | build from a made-up score and check the output |

Channel 10 is General MIDI percussion, and is mapped onto the struck blocks:
kick, snare and toms to **Drums**; hi-hat, crash and ride to **Cymbals**.
Anything unrecognised becomes a snare.

Velocity sets each block's volume, averaged over that pitch's notes — a block
cannot be struck harder, so the dynamics that survive are between parts rather
than within them.

## What to expect in game

A few hundred blocks is a machine Besiege loads happily; a few thousand is a
machine that loads slowly and simulates slowly, and the timers are what multiply.
`--limit` caps it, `--seconds` cuts the score, and a busy orchestral MIDI is worth
thinning in a score editor first.

The instruments are heard from where they stand, so a wide grid spreads the band
across the stereo field. Fly the camera into the middle of it.
