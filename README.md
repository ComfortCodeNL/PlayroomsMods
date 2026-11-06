<!-- Search Optimization Keywords: Playrooms multiplayer mod, Playrooms coop mod, how to play Playrooms with friends, Playrooms PC game modding, BepInEx 6 Playrooms guide -->


# Playrooms Co-op

Two-player online co-op for [Playrooms](https://store.steampowered.com/app/5037510/Playrooms/) over Steam. Shared balls, brooms, pits, money, upgrades and more. Press **F8** to open the menu and invite others.

## Main features

One player hosts and the other joins through an in-game menu (F8). You both play in the host's Playroom at the same time, and the joining player's own save is set aside and put back untouched afterwards.

### Shared between players

- Both players appear as colored avatars with name tags
- Ball pickups, ball pits and vents are shared between players
- One shared wallet, plus upgrades and shop purchases
- Things bought in the world: vents, clock screens, robots and charging stations
- Robots and vacuum devices (including the robots' voice lines)
- You can even write on the whiteboard together. Maybe a game of tic-tac-toe?
- If you're caught while your partner is safe, you wake up in the clean room instead of losing the run
- Unstuck (pause menu) moves you next to your partner, or to the clean room if that isn't possible, with a 10 second cooldown
- Balls stuck out of reach are moved back into play (this also works in solo)
- Opening a hallway door no longer closes the other open ones, so you can work in different rooms

## Requirements

- [Playrooms](https://store.steampowered.com/app/5037510/Playrooms/) (full game, Steam)
- [BepInEx 6 Bleeding Edge](https://builds.bepinex.dev/projects/bepinex_be), Unity IL2CPP x64 build
- Both players need this mod installed, at the same version
- You can only join or invite Steam friends who are playing Playrooms

## Installation

1. Make sure Playrooms is closed before continuing!
2. Download artifact **#791** of [BepInEx 6 Bleeding Edge](https://builds.bepinex.dev/projects/bepinex_be). The page lists many files, so make sure you get this exact one:

   `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.791+b782da4.zip`

   (Not Unity.Mono, and not x86.)
3. Extract it into the Playrooms game folder. In Steam, right-click Playrooms > Manage > Browse local files to find it. Use the full game's folder, not `Playrooms Demo`.

   Windows "Extract All" adds a folder named after the zip to the path. Remove that part so the path ends in `\Playrooms`. When it's right, these sit next to `Playrooms.exe`:

   ```
   Playrooms
   ├── BepInEx
   ├── dotnet
   ├── doorstop_config.ini
   ├── winhttp.dll
   └── Playrooms.exe
   ```

4. Download this mod's zip from [Releases](../../releases), open it, and drag the `BepInEx` folder from the zip into the same game folder. Let it merge with the existing `BepInEx` folder. If you have the files set up correctly, it should look like this:

   ```
   Playrooms
   └── BepInEx
       ├── patchers
       │   └── PlayroomsPatcher
       │       └── PlayroomsPatcher.dll
       └── plugins
           └── PlayroomsCoop
               ├── PlayroomsCoop.dll
               └── README.txt
   ```

5. Launch the game. The first launch applies a patch that lets BepInEx read the game. It takes a little longer, and when it is finished you will be at the main menu.
6. Close and reopen the game one more time.

### Steam Deck and Linux

BepInEx needs one extra setting under Proton. In Steam, right-click Playrooms > Properties > General, and put this in **Launch Options**:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## How to play

1. In game, press **F8** to open the co-op menu.
2. You will see friends who are online in Playrooms, as long as they also have the mod and followed these same instructions.
3. Host a session from the F8 menu and invite your friend from that menu. They press F8 to accept, and you both ready up.
4. Hit **New Game** or **Continue** and load in. If you aren't the host, you will stay at the main menu for a few moments until the game loads the host's save and joins you.

## Uninstalling

Delete these two folders:

- `BepInEx\plugins\PlayroomsCoop`
- `BepInEx\patchers\PlayroomsPatcher`

## Troubleshooting

The log is at `BepInEx\LogOutput.log` in the game folder. If it contains `Playrooms Co-op 1.0.0 loaded`, the mod is running.

- **No log file, or the game starts like normal with no BepInEx:** BepInEx isn't loading. Check that `winhttp.dll` is right next to `Playrooms.exe` and not in a subfolder (see step 3). Some antivirus programs quarantine `winhttp.dll` because it loads mods. If yours did, restore it and add an exception. On Steam Deck or Linux, check the launch option above.
- **Log exists but has no "Playrooms Co-op" line:** check that the mod files are in the places shown in step 4, not in an extra folder such as `PlayroomsCoop_1.0.0\BepInEx`.
- **Nothing happens when pressing F8:** click into the game window first, and check the log for the "loaded" line.
- **Your friend isn't listed in the F8 menu:** they must be on your Steam friends list, in-game in Playrooms, with the same version of the mod.
- **The first launch after a Playrooms update is slow:** this is normal. The patcher rebuilds its files whenever the game updates.
- **To force the patcher to rebuild:** open `BepInEx\config\com.playroomscoop.patcher.cfg`, set `ForceRegenerate = true`, and start the game.

**Reporting a bug:** open an issue on this page and attach `BepInEx\LogOutput.log` from the session where it happened. Say whether you were the host or the joining player, and what you were doing when it went wrong.

## Disclaimer

This mod is not endorsed by Two Pixels UG, the developers of Playrooms. They will not assist you in troubleshooting this mod, and it has no connection to the dev team. Please bring any issues up here so that I can fix them.

## Shout outs

Thanks to my wife, who loves these cozy clean-em-ups.
