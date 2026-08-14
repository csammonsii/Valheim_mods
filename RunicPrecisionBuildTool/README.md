# Runic Precision Build Tool

**Build beyond the grid without abandoning Valheim's building system.**

Runic Precision Build Tool adds precise three-axis rotation and world-axis positioning to the
vanilla hammer workflow. Rotate beams into ornamental patterns, form complex roof lines, recess
lights into stone, or make tiny alignment corrections—all while retaining Valheim's familiar
placement preview and snapping behavior.

![Precision beam pattern](https://raw.githubusercontent.com/csammonsii/Valheim_mods/master/RunicPrecisionBuildTool/media/runic_precision_beam_pattern.png)

![Advanced angled construction](https://raw.githubusercontent.com/csammonsii/Valheim_mods/master/RunicPrecisionBuildTool/media/runic_precision_angled_build.png)

## Features

- Pitch and roll any rotatable build piece in addition to vanilla yaw.
- Move the placement ghost along fixed world X, Y, and Z axes.
- Normal and fine adjustment steps for both rotation and movement.
- Preserve transformed snap points while rotating pieces.
- Deliberately inset pieces into other geometry for detailed construction.
- Toggleable RGB orientation rings show the piece's local axes and the fixed world planes.
- Optional controls appear inside Valheim's native bottom build-hint strip.
- Reset the current placement state without reopening the hammer menu.
- Client-side operation with no custom data added to placed pieces or world saves.
- No Jotunn dependency.

## Controls

| Control | Action |
|---|---|
| Mouse wheel | Vanilla yaw rotation |
| `Left Alt + Wheel` | Pitch around the piece's current local axis |
| `Left Alt + Left Shift + Wheel` | Roll/twist around the piece's current local axis |
| `Left Alt + Left/Right Arrow` | Move along world X |
| `Left Alt + Up/Down Arrow` | Move along world Y |
| `Left Alt + Page Up/Page Down` | Move along world Z |
| Hold `V` while adjusting | Use the fine step |
| `Left Alt + R` | Reset rotation, yaw, movement offset, and snap selection |
| `G` | Toggle the 3D guides and expanded native control hints |

Bright red, green, and blue rings represent the object's current local X, Y, and Z axes. The
larger translucent rings remain aligned to the world, making the object's orientation easy to read.

## Installation

### Mod manager

Install through Thunderstore Mod Manager or r2modman. The required BepInEx pack will be resolved
automatically.

### Manual

1. Install **BepInExPack Valheim**.
2. Place `RunicPrecisionBuildTool.dll` in `BepInEx/plugins/RunicPrecisionBuildTool/`.
3. Start Valheim.

## Configuration

After the first launch, configuration is written to:

`BepInEx/config/chazman.RunicPrecisionBuildTool.cfg`

The normal and fine rotation steps, normal and fine movement distances, control modifiers, and
general enable state can be changed there. The default fine modifier is `V`; this mod deliberately
does not use Ctrl so it can coexist with build-camera controls.

## Multiplayer and compatibility

Runic Precision Build Tool is client-side. A dedicated server and other players do not normally
need the mod because Valheim already synchronizes the final position and quaternion of a placed
piece.

The mod does not add custom components or metadata to placed pieces and does not change the world
save format. Removing it leaves already placed pieces intact. Servers with restrictive mod policies
or server-side placement validation may still prohibit or reject unusual placements.

The tool avoids globally consuming inputs and leaves ordinary building unchanged unless its
modifier keys are held. If another mod uses the same shortcuts, change that mod's bindings or the
available Runic Precision configuration entries.

## Support and community

Questions, bug reports, screenshots, compatibility reports, and feature discussion are welcome in
the **Chazman Mods Discord**:

**https://discord.gg/7HKHTCdFqY**

When reporting a problem, please include:

- Your Valheim version.
- The Runic Precision Build Tool version.
- Your BepInEx log.
- The affected build piece and exact control sequence.
- Other building or camera mods installed.

## Credits

Created by **Chazman** for the Valheim building community.

Runic Precision Build Tool is an independent mod and is not affiliated with Iron Gate Studio.

