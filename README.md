## ZCouplers: Knuckle Couplers for Derail Valley

Zeibach's Couplers replaces the default buffer-and-chain couplers with AAR, SA3, Scharfenberg or link and pin couplers. It changes the coupler models and the coupling physics, and keeps working with the game's save system and with DV Multiplayer.


## Features

- Four coupler types: AAR knuckle, SA3 knuckle, Scharfenberg Typ 10 and link and pin, each with open and closed models and LODs
- One joint per coupled pair, with free play (slack), spring and damping values taken from each coupler's real draft gear
- Couplers break when pulled apart beyond their strength, not when pushed together
- Safe coupling speed per coupler type: hitting a car faster than that does not couple, and the cars need a new, slower contact after they stop pressing together
- Derailed cars don't couple until they are rerailed
- Buffer models can be shown or hidden on all cars
- Auto Air & MU Mode connects air hoses, opens brake valves and connects MU cables when coupling
- Auto Coupling Mode couples even when the couplers aren't readied
- Option to disable the front couplers on the S282
- Coupler readiness and locked state are saved with the game
- Gadgets snap to a point on the coupler model, so they stay reachable on every coupler type


## Download and Installation

1) Install Unity Mod Manager (UMM) for Derail Valley if you haven't already.
2) Download the latest release zip from the Releases page:
	https://github.com/mspielberg/dv-zcouplers/releases
3) Install the archive via UMM.
4) Start the game. Open UMM in-game and make sure "Zeibach's Couplers" is enabled.


## Using the Mod (in game)

Open Unity Mod Manager (Ctrl+F10 by default) and go to ZCouplers to change the settings:

- Coupler Profile:
  - AAR Knuckle
  - SA3 Knuckle
  - Scharfenberg Typ 10
  - Link and Pin
- Toggle Buffers Visuals: show or hide the buffers
- Strength Values: Recommended uses each coupler type's own values. Custom lets you set:
  - Coupler strength (MN)
  - Tension spring rate (MN/m), applies after recoupling or a restart
  - Compression damper rate (kN*s/m), applies after recoupling or a restart
  - Safe coupling speed (km/h)
- Auto couple threshold (mm): how far the couplers have to be pushed together before they couple
- Minimum separation distance (m): how far uncoupled cars have to move apart before they can couple again
- Auto Air & MU Mode (always on for Scharfenberg)
- Auto Coupling Mode (always on for Scharfenberg)
- Disable Front Couplers on S282
- Enable debug logging / Enable error logging


## Multiplayer

ZCouplers works with DV Multiplayer.

- The host decides all coupling. Clients follow the host's coupler states and joints.
- The host's settings, including the coupler type, are sent to every client. Clients get their own settings back after leaving.
- During a session the host can still change settings that apply right away. Coupler type, strength values preset, spring rate, damper rate and the S282 option are locked until the session ends.
- Clients can only change the logging settings while connected.
- Single player with DV Multiplayer installed behaves like normal single player, with every setting unlocked.


## Credits and License

- Author: Zeibach/mspielberg
- Maintainer: Fuggschen
- Soviet SA3 coupler model and texture contributed: Adrii_95
- Additional Coupler Visuals (open/closed state) for AAR/SA3: Rajaneesh R
- Scharfenberg Coupler: Micha77
- Mount Plates for AAR/SA3: whycofie
- Link&Pin Couplers: My Name is BorING
- Help with multiplayer code: SharkBaitDLS

Licensed under the MIT License. See `LICENSE`.
