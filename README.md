# Sheltered Sounds

A client-only mod for **Vintage Story 1.22.7 / .NET 10** that keeps the vanilla cave music and ambient drones out of enclosed rooms, including underground homes, cellars, and greenhouses.

While you are inside, the Cave Mix cannot take over the music. A normal song already playing continues; otherwise the game's normal music selection and silence still apply. The mod does not force surface music to play underground or change the music frequency setting.

If you enter a room while cave audio is playing, it fades out over half a second at the next music update (normally once per second). Cave parts still loading when you enter are discarded before they can start. Once you leave, vanilla cave music rules apply again.

## Install

Copy `artifacts/shelteredsounds-1.0.0.zip` into the **Mods folder in your Vintage Story data directory**, then restart the client. Install only on your own client; multiplayer servers and other players do not need the mod. There is no configuration file.

## What counts as a room?

The mod uses Vintage Story's own `RoomRegistry`, with zero exits and fully loaded room data. It does not require sunlight, a greenhouse bonus, or the smaller dimensions used for cellars. Doors, trapdoors, chiseled walls, and room size limits follow the game's enclosure rules. An area that is too large or has an opening according to the registry is not protected. A small sealed natural cavity also counts as a room; the game does not distinguish it from a built enclosure.

Use `/debug rooms hi` in a world where you have permission to inspect enclosure detection; `/debug rooms unhi` removes the highlight. This mod changes only `CaveMusicTrack`, which contains the vanilla cave musical fragments and drones. Weather, creatures, machines, other world sounds, temporal effects, and custom music systems remain governed by the game and their respective mods.

## Build and verify

Install a **.NET 10 SDK** and Vintage Story **1.22.7**. macOS defaults to `/Applications/Vintage Story.app`. For another installation, set `VINTAGE_STORY` to the directory containing `VintagestoryAPI.dll`, `Mods/`, and `Lib/`.

```sh
export VINTAGE_STORY="/path/to/Vintage Story" # optional on macOS
bash scripts/build.sh
```

`DOTNET` can select a specific SDK executable. No NuGet dependencies or game downloads are needed. The build runs checks against the installed game's actual cave track, applies the Harmony patches, verifies the asynchronous loading guard, and creates and validates the ZIP. Game assemblies and Harmony are supplied by the game and are not bundled.

```sh
dotnet run --project tests/ShelteredSounds.Tests -c Release
dotnet build src/ShelteredSounds -c Release -t:Package
python3 scripts/validate_package.py
```

Client playtest checklist (requires an actual game session):

- Carry a normal song into an enclosed underground home: cave audio must not replace it.
- Wait inside a dark cellar and inside a greenhouse: no Cave Mix should start.
- Enter a room from a cave while its drones are playing: they should fade out at the next music update.
- Leave the room, or break a wall so it opens into a cave: vanilla cave playback becomes eligible again, subject to its usual cooldowns.
- Test your doors, trapdoors, and chiseled walls with `/debug rooms hi`.
- Save and reload, then repeat in single player and on a multiplayer server without a server installation of the mod.

## Implementation references

- [Official cave track source](https://github.com/anegostudios/vsapi/blob/master/Client/Audio/CaveMusicTrack.cs)
- [Official essentials mod source](https://github.com/anegostudios/vsessentialsmod)
- [Vintage Story room rules](https://wiki.vintagestory.at/Room)

The runtime patches are specific to the 1.22.7 cave track implementation. Other game versions need verification before being advertised as compatible.
