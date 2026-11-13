# ComfortCode PlayroomsMods fork

Deze fork bevat de originele bronbestanden van **TheWildLeon/PlayroomsCoopMod** en daarnaast een **nieuwe, afzonderlijke ComfortCode-plugin: [Comfort Shift HUD](PlayroomsShiftHUD/README.md)**.

De nieuwe mod toont resterende ballen in de huidige kamer, tijd tot het einde van de shift, aantal en namen van volledig opgeruimde kamers en een END DAY-banner. Bij shifteinde creëert hij optioneel lokaal werklicht, zonder de dagklok, co-op-synchronisatie of saves te veranderen. De originele PlayroomsCoop en New Game+ blijven onveranderd.

**Installatie:** de nieuwe mod wordt vanuit de eigen broncode op de Windows-pc gebouwd met de al werkende Unity 6000.6 BepInEx-patcher en bestaande IL2CPP-interop. Gebruik scripts/install-shift-hud.ps1 (eerst zonder -Apply; daarna met -Apply). Er is nog **geen** bevestigde echte Windows-/Unity-clienttest van deze mod. De originele release-ZIP's van TheWildLeon bevatten deze nieuwe plugin niet automatisch.

---
# Playrooms Co-op + New Game+

Two mods for [Playrooms](https://store.steampowered.com/app/5037510/Playrooms/), hosted together for now:

- **Playrooms Co-op 1.1.7**: online co-op for up to 4 players over Steam. Shared balls, brooms, pits, money, upgrades and more. Press **F8** to open the menu and invite others.
- **Playrooms New Game+ 1.2.4**: replay the game after the end credits with a head start, in Simple, SpeedRun or Balance Patch mode, plus Spooky, Tokens and a Starter kit.

Each mod works on its own, and they work together: New Game+ runs can be played in co-op. Grab the bundle to get both, or just the one you want.

## Downloads

On the [Releases](../../releases) page, each release has these zips. Each one includes BepInEx, so there's nothing else to download:

| Zip | Contains |
|---|---|
| `PlayroomsCoopNGPlus_Bundle_..._WithBepInEx.zip` | Both mods + BepInEx (recommended) |
| `PlayroomsCoop_1.1.7_WithBepInEx.zip` | Co-op + BepInEx |
| `PlayroomsNewGamePlus_1.2.4_WithBepInEx.zip` | New Game+ + BepInEx |

All of them include the same BepInEx and patcher, so installing more than one is fine.

## Co-op: main features

One player hosts and up to three friends join through an in-game menu (F8). Everyone plays in the host's Playroom at the same time, and each joining player's own save is set aside and put back untouched afterwards.

- Every player appears as a colored avatar with a name tag. Pick your color in the lobby
- Ball pickups, ball pits and vents are shared between players
- One shared wallet, plus upgrades and shop purchases
- Things bought in the world: vents, clock screens, robots and charging stations
- Robots and vacuum devices (including the robots' voice lines)
- You can even write on the whiteboard together. Maybe a game of tic-tac-toe?
- If you're caught while someone else is safe, you wake up in the clean room instead of losing the run. If nobody is safe, the run ends for everyone
- Unstuck (Help menu) moves you next to the nearest player, or to the nearest free spot if that isn't possible, with a 10 second cooldown
- Reset Room Balls and Bring Devices Home (Help menu) work for everyone. Restart Day is off in co-op, since it would put players in different days
- Balls stuck out of reach are moved back into play (this also works in solo)
- Opening a hallway door no longer closes the other open ones, so you can work in different rooms
- Waiting room doors stay open while anyone is using them
- School lockers and the hotel cleaning schedule are shared
- Robots are run by whichever player is nearest, which spreads the work between computers
- More players, more daytime scares: each extra player makes them more frequent, with shorter gaps (Normal and Hardcore; Cozy is unchanged)

## New Game+: main features

New Game+ unlocks once you've seen the end credits. Start it from the box next to the end screen, or from **New Game+ available** in the main menu, which lists every finished save. The new run goes into the next empty slot, and the finished save is never touched.

- **NG+ Simple**: a fresh world with the shop and every upgrade open from day 1
- **NG+ SpeedRun**: every upgrade level and tool you owned comes along
- **NG+ Balance Patch**: like Simple, with the hand vacuum retuned
- **Spooky**: twice the daytime scares, with shorter gaps
- **Tokens**: one free purchase per NG+ cycle (NG+4 = 4 tokens)
- **Starter kit**: charging station, robot vacuum, ball vacuum, hand vacuum, flashlight, radar and broom on day 1
- Collectables and Lexi the cat come along, terminal programs are there from day 1, and finishing a run unlocks the next one (NG+2, NG+3, ...)

The full guide is in `BepInEx\plugins\PlayroomsNewGamePlus\README.txt` after installing.

### New Game+ in co-op

- The host starts New Game+ and everyone else is brought into the new world like any co-op game
- Mode, options, SpeedRun items, the starter kit, collectables and open upgrades are shared
- Tokens are one shared pool, counted by the host, so two players can't spend the same last token
- Spooky stacks with Co-op's extra daytime scares for bigger groups
- Every player needs both mods installed

## Requirements

- [Playrooms](https://store.steampowered.com/app/5037510/Playrooms/) (full game, Steam)
- For co-op: every player needs the same mod versions, and you can only join or invite Steam friends who are playing Playrooms

## Installation

1. Make sure Playrooms is closed before continuing!
2. Find the game folder: in Steam, right-click Playrooms > Manage > Browse local files. It's the folder with `Playrooms.exe`. Use the full game's folder, not `Playrooms Demo`.
3. Download a zip from [Releases](../../releases) and unzip it into that folder. Windows "Extract All" adds a folder named after the zip to the path: remove that part so the path ends in `\Playrooms`. When it's right, the game folder looks like this (with the bundle):

   ```
   Playrooms
   ├── BepInEx
   │   ├── patchers
   │   │   └── PlayroomsPatcher
   │   └── plugins
   │       ├── PlayroomsCoop
   │       └── PlayroomsNewGamePlus
   ├── dotnet
   ├── doorstop_config.ini
   ├── winhttp.dll
   └── Playrooms.exe
   ```

   With only one mod, only that mod's folder is under `plugins`.

4. Launch the game. The first launch applies a patch that lets BepInEx read the game. It takes a little longer, and when it is finished you will be at the main menu.
5. Close and reopen the game one more time.

The included BepInEx is build be.793, unchanged. Its license and the licenses of the libraries it ships with are in `BepInEx\licenses`.

### Steam Deck and Linux

BepInEx needs one extra setting under Proton. In Steam, right-click Playrooms > Properties > General, and put this in **Launch Options**:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## How to play co-op

1. In game, press **F8** to open the co-op menu.
2. You will see friends who are online in Playrooms, as long as they also have the mod and followed these same instructions.
3. Host a session from the F8 menu and invite up to three friends from that menu. They press F8 to accept, pick a color, and everyone readies up.
4. Hit **New Game** or **Continue** and load in. If you aren't the host, you will stay at the main menu for a few moments until the game loads the host's save and joins you.

## Uninstalling

Delete the folder of each mod you want to remove:

- `BepInEx\plugins\PlayroomsCoop`
- `BepInEx\plugins\PlayroomsNewGamePlus`

Once neither mod is left, also delete `BepInEx\patchers\PlayroomsPatcher`. Your saves are never deleted.

## Troubleshooting

The log is at `BepInEx\LogOutput.log` in the game folder. If it contains `Playrooms Co-op 1.1.7 loaded` or `Playrooms New Game Plus 1.2.4 loaded`, that mod is running.

- **No log file, or the game starts like normal with no BepInEx:** BepInEx isn't loading. Check that `winhttp.dll` is right next to `Playrooms.exe` and not in a subfolder (see step 3). Some antivirus programs quarantine `winhttp.dll` because it loads mods. If yours did, restore it and add an exception. On Steam Deck or Linux, check the launch option above.
- **Log exists but a mod's "loaded" line is missing:** check that the mod files are in the places shown in step 3, not in an extra folder such as `PlayroomsCoop_1.1.7\BepInEx`.
- **Nothing happens when pressing F8:** click into the game window first, and check the log for the "loaded" line.
- **Your friend shows "no reply", "connecting..." or "can't connect":** Steam can't connect your games. On both PCs, allow Playrooms through Windows Firewall (Windows Security > Firewall & network protection > Allow an app through firewall) and any antivirus firewall, turn off VPNs, and restart Steam. School, work and dorm networks often block it.
- **Your friend shows "no mod reply":** the connection works, but their game didn't answer. Check their install and their log for the "loaded" line.
- **Your friend isn't listed in the F8 menu:** they must be on your Steam friends list, in-game in Playrooms, with the same mod versions.
- **No New Game+ in the menu:** only saves that reached the end credits count.
- **The first launch after a Playrooms update is slow:** this is normal. The patcher rebuilds its files whenever the game updates.
- **To force the patcher to rebuild:** open `BepInEx\config\com.playroomscoop.patcher.cfg`, set `ForceRegenerate = true`, and start the game.

**Reporting a bug:** open an issue on this page and attach `BepInEx\LogOutput.log` from the session where it happened. Say which mod, whether you were the host or a joining player, and what you were doing when it went wrong.

## Source

- `PlayroomsCoop/`: Co-op source
- `PlayroomsNewGamePlus/`: New Game+ source
- `patcher/`: the shared patcher that lets BepInEx run on the game's Unity version

Build steps are in `BUILD.txt`.

## Disclaimer

These mods are not endorsed by Two Pixels UG, the developers of Playrooms. They will not assist you in troubleshooting these mods, and they have no connection to the dev team. Please bring any issues up here so that I can fix them.

## Shout outs

Thanks to my wife, who loves these cozy clean-em-ups.

<a href="https://www.buymeacoffee.com/lyrawolf" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-violet.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;" ></a>
