# Audio: Stereo, Distance, Reverb Zones, Wall Muffling and Doppler

How a sound's stereo position, distance falloff, room reverb, muffling by walls and Doppler shift
are set up, and the `Scenes/AudioTest.obsc` sample that checks them. The FMOD side (loading, buses, the
listener) is in [Audio_Architecture.md](Audio_Architecture.md); volumes in [Audio_Volume.md](Audio_Volume.md).

## Audio component

Add **Audio** to a gameobject, drop a WAV/MP3/OGG/FLAC from Assets on its clip box.

| Setting | What it does |
|---|---|
| Volume | 0..1, times the bus (Output) and master volume. |
| Pitch | Playback speed: 2 = an octave up, 0.5 = an octave down. |
| Stereo Pan | -1 = left speaker only, 0 = centre, 1 = right only. Applies to the 2D part of the sound: all of a non-3D sound, `1 - Spatial` of a 3D one. |
| Reverb | 0..1, how much of the sound goes into the Reverb Zones' reverb. 0 for UI sounds and music. |
| 3D Spatial Audio | Positional: panned and attenuated from the camera (the listener). Off = plain 2D. |
| Min / Max Dist | Full volume within Min. Between Min and Max the Rolloff curve applies. |
| Rolloff | **Inverse**: real-world falloff (half at 2x Min, a quarter at 4x), stops getting quieter at Max, never silent. **Linear**: straight fade to silent at Max. **Linear Square**: holds longer near the source, then drops to silent at Max. **Inverse Tapered**: inverse near the source, silent at Max. |
| Spatial | 1 = fully positional, 0 = 2D. In between mixes both (a close, big source). |
| Spread | Degrees the speakers spread the sound over: 0 = a point, 360 = all around. |
| Walls | 0..1, how much walls between the sound and the camera muffle it (below). 0 = never (a voice-over placed in 3D). |
| Doppler | Pitch shift from motion: 0 = off, 1 = real, up to 5 = exaggerated. |

Inverse is the default, so scenes saved before these settings existed sound the same. Use a
Linear curve when a sound must be silent beyond some distance (so stations or rooms don't hear
each other).

Scripts: `PlayAudio()` plays the gameobject's component with these settings; `PlaySound(...)`
returns a `ScriptSound` with `Volume`, `Pitch`, `Pan` (write-only), `ReverbSend`, `DopplerLevel`,
`WallMuffling` (write-only), `Loop`, `Paused`. `AudioComponent.Emitter` gives a playing 3D sound's
position, velocity, walls, how much is stopped (`Occlusion`) and the extra treble loss (`Dullness`); `Audio.ListenerVelocity` the camera's; `Audio.WallOcclusion`
sets how much one wall blocks (default 0.6).

### Stereo / handedness

The world is right-handed with Z up; FMOD defaults to left-handed. `AudioManager.Initialize`
starts FMOD with `FMOD_INIT_3D_RIGHTHANDED`, so a sound to the camera's right comes out of the
right speaker. Before this flag left and right were swapped for every 3D sound.

## Wall muffling

Every frame `AudioManager.UpdateListener` casts rays from the camera to each playing 3D sound
against the **static colliders** (`ScenePhysics.RaycastStatic`: gameobjects with a Static Physics
component, the voxel world; dynamic bodies and triggers are see-through) in
`AudioOcclusion.Walls`:

- Each gameobject a ray passes through is one wall (a mesh wall's front and back faces count once).
  The sound's own gameobject never muffles it. At most 3 walls.
- Each wall acts by its **Sound Material** (below): it stops a share of the sound (**Block**) and takes
  a share of the treble on top (**Dullness**). Walls in a row multiply what gets through: concrete
  then wood lets `0.1 x 0.55` = 5.5 % through.
- Three rays: to the sound and to two points 0.6 m either side of it; what gets through is averaged,
  so a sound just inside a doorway is only partly muffled and walking past a door edge fades.
- The result, times the sound's **Walls** setting and eased over ~0.15 s, goes to FMOD:
  `set3DOcclusion(blocked, blocked x 0.5)` (quieter, with FMOD's own low-pass since
  `FMOD_INIT_CHANNEL_LOWPASS` is on) and `setLowPassGain(1 - dullness)` (the material's extra treble
  loss). The reverb path is occluded half as much, so a room behind a wall still rings a little.

Walls need colliders: give them a Physics component with Body Type Static. Works in the editor too
(colliders exist in Edit; only the simulation is off).

### Sound materials

Set on the **Physics** component of a Static collider (Inspector > Physics > Sound, shown for Static
bodies, with a line saying what the material does). `PhysicsComponent.SoundMaterial`, saved by name.

| Material | Block | Dullness | Sounds like |
|---|---|---|---|
| Default | 0.6 (`Audio.WallOcclusion`) | 0.5 | a generic wall (what everything was before materials) |
| Wood | 0.45 | 0.6 | voices through a door: clearly there, muffled |
| Plaster | 0.5 | 0.55 | a drywall partition |
| Glass | 0.4 | 0.3 | a window: quieter but still bright |
| Metal | 0.7 | 0.25 | a metal door or container: much quieter, rings through bright |
| Brick | 0.8 | 0.75 | a house wall: faint and dull |
| Concrete | 0.9 | 0.85 | a bunker wall: a bass thump |
| Stone | 0.92 | 0.8 | a castle wall |
| Fabric | 0.15 | 0.3 | a curtain or padding |
| Foliage | 0.08 | 0.15 | a hedge |
| Custom | Sound Block | Sound Dullness | your own values (0..1) on the Physics component |

Colliders without a Physics component (the voxel world) count as Default. The table is
`AudioOcclusion.PropertiesOf` (`Engine/Recources/AudioOcclusion.cs`).

## Doppler

`UpdateListener(camera, entities, dt)` works out the camera's and each 3D sound's velocity from how
far they moved since the last frame (smoothed over ~0.1 s) and gives them to FMOD, which shifts the
pitch at 340 m/s speed of sound (1 unit = 1 m). A jump faster than 120 m/s
(`AudioManager.MaxDopplerSpeed`) is a teleport: the velocity resets instead of a pitch spike. Sounds
at a fixed position (`PlaySoundAt`) only shift from the camera's motion. Lower **Doppler** on sounds
that shouldn't bend (music placed in 3D, UI).

## Reverb Zone component

Add **Reverb Zone** to a Cube gameobject and scale it to the room's inside. While the camera is
in the box, every sound gets the zone's reverb. The mesh is hidden while the zone is enabled
(select it in the hierarchy). Works in the editor (the inspector's Play button) and in Play.

| Setting | What it does |
|---|---|
| Preset | One of FMOD's environments: Room, Bathroom, Stone Room, Concert Hall, Cave, Hangar, Sewer Pipe, Underwater, Padded Cell, Forest... |
| Infinite | Applies everywhere instead of inside the box: an outdoor ambience under the rooms. |
| Blend Dist | Metres outside the box over which the reverb fades in (doorways). |
| Weight | 0..1 of the zone's reverb at full strength. |
| Priority | Zones blend lowest priority first; the highest wins where they overlap. On a tie the smaller box goes last, so a room inside a hall wins without a priority. |
| Decay Scale | Multiplies the preset's tail length. |
| Wet Level | dB added to the preset's reverb loudness (+6 about twice as loud). |

How it works: each frame `AudioManager.UpdateListener` calls `ReverbZoneComponent.Evaluate` with
the camera position and sets the result as FMOD reverb instance 0 (only when it changes). Inside
a zone the reverb is its preset; in a blend region the room's character (decay, delays, filters)
comes from the zone and its wet level is scaled as a gain, so fading in from silence is smooth.
Outside every zone the reverb is `FMOD.PRESET.OFF()`. The reverb follows the **listener**: a
sound playing in another room is heard with the listener's room reverb, as in most games.
`ReverbZoneComponent.ActiveZones` lists the zones in use with their weights; `Audio.Reverb` is the
reverb set this frame. Music never goes into the reverb.

## Sample scene: `Scenes/AudioTest.obsc`

Open it in Anvil and press Play (headphones help). The camera flies with Freecam; the
`audio-test` script (`Content/Scripts/AudioTestSceneScript.cs`) on it runs the stations and a HUD
(`UI/AudioTest`) showing the active reverb zones, the beacon's distance and expected volume, and
which test sounds are on. If FMOD did not start, the HUD says so.

| Station | Where | Keys |
|---|---|---|
| Stereo | start (x = 0), stand on the yellow pad | **1** 2D left/right check: three low pips must come from the LEFT, three high pips from the RIGHT. **2** a 2D pulse whose Stereo Pan sweeps left-right. **3** a 3D emitter orbiting you counter-clockwise: in front, left, behind, right (the HUD says which). |
| Distance | **8** (x = 60) | A beacon (Linear, Min 3 m, Max 25 m) with posts at 3 m (green), 10 m (yellow), 25 m (red) and 40 m (grey). Walk towards it. **R** cycles the rolloff curve, **T** shuttles the beacon out to 40 m and back. |
| Reverb | **9** (x < -40) | Bathroom, Concert Hall with a Padded Booth nested in one corner, Sewer Tunnel. **F** claps from where you stand, **C** turns the rooms' echo emitters (the nearest claps every 1.6 s) on/off. Outdoors is dry. The room walls are static colliders, so an echo emitter heard from outside its room is muffled. |
| Walls | **6** (-20, 12) | A radio playing in a closed wooden hut (door on its right, +X side) behind a free concrete wall. From the yellow pad: 2 walls, quiet and dull. Walk round the free wall: 1 wall (wood). Go to the door: clear. **M** cycles the free wall's Sound Material (Concrete, Brick, Stone, Plaster, Wood, Metal, Glass, Fabric, Foliage, Default) and recolours it, so you can compare them from the pad. The HUD shows walls, % quieter, % duller and the material. Room walls elsewhere: Bathroom Plaster, Concert Hall Wood, Padded Booth Fabric, Sewer Tunnel Concrete. |
| Doppler | **5** (0, -60) | **4** starts an engine sound racing along the track 10 m in front of you (35 m/s): pitch high as it comes, drops as it passes. **[ ]** slower / faster. Fly along the track with Shift to hear the listener's own motion. The HUD shows the expected pitch factor. |

**7** jumps back to the Stereo station.

Generated content: the scene by `Tools/Scripts/generate_audio_test_scene.py`, the sounds
(`Content/Audio/Test/clap.wav`, `pulse_loop.wav`, `engine_loop.wav`, `stereo_check.wav`) by
`Tools/Scripts/generate_audio_test_sounds.py`. Checks: `Tests/Components/AudioChecks.cs`
(`dotnet run --project Tests\Components\Components.csproj -- --audio-checks`).

## Limits

- Muffling is by straight rays: sound doesn't travel round corners or through a far doorway (only the
  0.6 m side rays soften edges). A wall muffles by its material, not its thickness.
- Walls without a Static collider (no Physics component, Dynamic bodies) don't muffle.
- 3 rays per playing 3D sound per frame: fine for tens of sounds, not hundreds.
- One reverb at a time (the listener's); a sound in the hall heard from the bathroom gets the bathroom's reverb.
