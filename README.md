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
2. Download artifact **#791** of [BepInEx 6 Bleeding Edge](https://builds.bepinex.dev/projects/bepinex_be) (the Unity IL2CPP win x64 build).
3. Extract it into the Playrooms game folder. In Steam, right-click Playrooms > Manage > Browse local files to find it. It should be in the same location as the `.exe` file.
4. Launch the game. It should fail with an error, but it will build the correct file structure in the game folder.
5. Extract this mod's zip and drag the `BepInEx` folder from the zip into the same game folder as step 3. If you have the files set up correctly, it should look like this:

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

6. Launch the game again. This time a patch will be applied to allow BepInEx to read the game. It will take some time, and when it is finished you will be at the main menu.
7. Close and reopen the game one more time.

## How to play

1. In game, press **F8** to open the co-op menu.
2. You will see friends who are online in Playrooms, as long as they also have the mod and followed these same instructions.
3. Host a session from the F8 menu and invite your friend from that menu. They press F8 to accept, and you both ready up.
4. Hit **New Game** or **Continue** and load in. If you aren't the host, you will stay at the main menu for a few moments until the game loads the host's save and joins you.

## Uninstalling

Delete these two folders:

- `BepInEx\plugins\PlayroomsCoop`
- `BepInEx\patchers\PlayroomsPatcher`

## Disclaimer

This mod is not endorsed by Two Pixels UG, the developers of Playrooms. They will not assist you in troubleshooting this mod, and it has no connection to the dev team. Please bring any issues up here so that I can fix them.

## Shout outs

Thanks to my wife, who loves these cozy clean-em-ups.
