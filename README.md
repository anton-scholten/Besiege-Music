# Besiege Orchestra

Nine instrument blocks, in [Besiege](https://store.steampowered.com/app/346010/Besiege/).

Piano, guitar, bass, strings, brass, woodwind, mallets, drums and cymbals. Each
block plays one note on one key, heard from where the block is — so a machine can
carry a band, and you hear it move.

**[UI Factory](https://steamcommunity.com/sharedfiles/filedetails/?id=2913469777)**
(Workshop item `2913469777`) is optional. With it the blocks get a proper panel;
without it they use Besiege's ordinary block mapper and everything still works.

## Install

Either subscribe to the mod on Steam, or if you don't use Steam you can clone the repo then:

```sh
./tools/install.sh              # symlink into Besiege_Data/Mods
./tools/install.sh --copy       # copy instead
./tools/install.sh --uninstall
```

Set `BESIEGE_DIR` if your install isn't found automatically. Start Besiege, enable **Orchestra** in the mods menu, and the nine blocks appear in the toolbar. No C# toolchain is needed, the build uses Besiege's own compiler.

## Options

Every block has these:

| Setting | What it does |
| --- | --- |
| Play | Key that sounds the note. Default `N` |
| Type | Which instrument of that family |
| Note | Pitch, as a MIDI note number. 60 is middle C |
| Volume | 0 to 1 |
| Range | How far it carries before falling silent |
| Toggle | On, the key starts and stops. Off, it plays while held |

Each block adds its own on top:

| Block | Adds |
| --- | --- |
| Piano | Sustain, Release |
| Guitar | Pluck, Palm mute |
| Bass | Slap |
| Strings | Pizzicato, Vibrato |
| Brass | Mute, Attack, Vibrato |
| Woodwind | Breath, Vibrato |
| Mallets | Hardness, Motor |
| Drums | Tune, Decay, Damping |
| Cymbals | Size, Open |

They mean what they say rather than switching to another recording. **Pizzicato**
drops the loop out of a bowed note and cuts its tail; **Mute** and **Palm mute**
are the same low-pass, because a hand on the strings and a mute in a bell come to
the same thing; **Size** on a cymbal lengthens the decay *and* crowds the
partials, because that is what a bigger plate does.

## One block, one note

A block plays a single note, and a tune is a row of blocks each triggered by its
own variable. That is not a shortcut — Besiege binds automation variables to
keys and to nothing else, so a note slider could never be driven by a variable.

Blocks are still polyphonic, eight voices each. That is not for chords, which
come from separate blocks, but so striking a block again does not cut off what is
still ringing.

## Sound

**Synthesised** — mallets, drums, cymbals. A bank of decaying partials for
anything metal: rigid metal rings at frequencies that are not harmonics, which is
why a crash starts bright and ends as a hum, and why **Size** and **Decay** here
change the physics rather than pick a different recording. Drums are a pitched
body that falls as it decays, plus noise for the skin.

**Sampled** — piano, guitar, bass, strings, brass, woodwind. Three recorded notes
per instrument, so pitch-shifting never stretches more than about three
semitones. The 84 clips are cut from [GeneralUser GS](https://github.com/mrbumpy409/GeneralUser-GS)
at build time and come to 776 KB; see [docs/SAMPLES.md](docs/SAMPLES.md) for how,
and how to cut them from a different font.

## Adding an instrument

No code. Each block's XML declares its own types and controls:

```xml
<Type name="Vibraphone" engine="modal" decay="4.0" brightness="0.45"
      inharmonicity="1.18" noise="0.08" />
<Extra kind="slider" key="MotorKey" name="Motor" min="0" max="1" default="0" />
```

`engine` is `modal`, `drum` or `sampler`. A sampled type names its clips instead,
`samples="piano_grand_36 piano_grand_60 piano_grand_84"`, where the trailing
number is the MIDI note each was recorded at.

## The panel

![the panel](docs/panel.jpg)

With UI Factory installed, opening a block's mapper brings up a panel in
Besiege's own interface: the instrument in the game's own `< Grand piano >`
selector, the note shown as a note name and snapping to semitones, and a row for
every control the block declared — so a new instrument gets its panel for
nothing. The speaker in the corner plays the block where it stands, with the
settings as they are, so an instrument can be chosen by ear without starting the
machine. The panel opens where it was last closed.

The key is shown but not editable there. Rebinding needs Besiege's own key
capture, which lives in the mapper open behind the window, so that is where it
stays.

## Notes

Each block wears its own instrument: low-poly models from
[Poly Pizza](https://poly.pizza), converted and stood upright by
`tools/make-block-meshes.py`. They carry no textures of their own, only a flat
colour per material, so the tool gathers those into a small palette and points
each triangle at its own patch — which is why a block's texture is a few dozen
bytes.


Sound is generated in `OnAudioFilterRead`, which the mixer calls on the buffer it
is about to play, so a note starts when the key does. The obvious alternative, a
streaming `AudioClip` fed through a PCM reader callback, is read well ahead of
being heard and answers the key late — it buys Unity's spatialisation at the cost
of the thing the block is for. So each block places itself instead: a gain per
ear, worked out each frame from where it stands relative to the listener.

That callback runs on the audio thread, where nothing may touch Unity, allocate,
or lock. Settings cross over as plain volatile fields and note events through a
single-producer ring buffer.

Details land in `Player.log` and in the in-game console with `show_logs true`.

AI agent? see [AGENTS.md](AGENTS.md) for layout, build, and any relevant info.

## Credits

The block models are Creative Commons Attribution (CC-BY 3.0), from Poly Pizza:

| Block | Model | By |
| --- | --- | --- |
| Piano | [Piano](https://poly.pizza/m/7U-93vxPOER) | jeremy |
| Guitar | [Electric guitar](https://poly.pizza/m/0hg94uOO-sS) | jeremy |
| Bass | [Acoustic guitar](https://poly.pizza/m/afr6GCpce_I) | jeremy |
| Strings | [Violin](https://poly.pizza/m/fhj0GK-0kJu) | jeremy |
| Brass | [Trumpet](https://poly.pizza/m/0Mj5XgeGtKJ) | jeremy |
| Woodwind | [Saxophone](https://poly.pizza/m/6A2UAKdCNy7) | jeremy |
| Drums | [Drum](https://poly.pizza/m/5Wp2emwd7xw) | jeremy |
| Mallets | [Xylophone](https://poly.pizza/m/a-OYg3WVXfV) | Daniel Melchior |
| Cymbals | [Cymbal](https://poly.pizza/m/f8SdBE98BXE) | Poly by Google |

Sampled instruments are cut from [GeneralUser GS](https://github.com/mrbumpy409/GeneralUser-GS),
which is permissively licensed; only the cut samples are redistributed.

## Licence

MIT. Besiege is Spiderling Studios'; nothing of theirs is redistributed here.
Sampled instruments are cut from an open SoundFont — see
[docs/SAMPLES.md](docs/SAMPLES.md) for which, and under what terms.
