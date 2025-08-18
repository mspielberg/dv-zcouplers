## ZCouplers — Realistic Knuckle Couplers for Derail Valley

Zeibach's Couplers replaces the default buffer-and-chain couplers with realistic knuckle couplers (AAR and SA3). It updates visuals and physics, adds drawgear behavior, and integrates with the game's save system. Built as a Unity Mod Manager (UMM) plugin with Harmony patches.


## Features

- AAR and SA3 knuckle couplers with game-ready visuals (asset bundle embedded in the DLL)
- Custom coupling physics: tension/compression joints, spring and damping parameters
- Breakable couplers with stress tracking
- Toggleable buffer visuals across all cars and prefabs
- Steam loco options: selectively disable front couplers (and hide air hose/hardware) on S282/S060
- Full Automatic Mode option to auto-connect air and open brake valves when coupling
- Native save compatibility plus persistence of knuckle readiness/locked state


## Download and Installation

1) Install Unity Mod Manager (UMM) for Derail Valley if you haven't already.
2) Download the latest release zip from the Releases page:
	https://github.com/mspielberg/dv-zcouplers/releases
3) Install the archive via UMM.
4) Start the game. Open UMM in-game and ensure “Zeibach's Couplers” is enabled.


## Using the Mod (in game)

Open Unity Mod Manager (Ctrl+F10 by default) → ZCouplers to access settings:

- Coupler type (requires restart):
  - AAR Knuckle
  - SA3 Knuckle
- Show Buffers With Knuckles: show/hide buffer visuals while knuckles are enabled
- Knuckle strength (MN)
- Tension spring rate (MN/m)
- Compression damper rate (kN·s/m)
- Auto couple threshold (mm)
- Full Automatic Mode: auto connect air/open valves when coupling
- Disable Front Couplers on Steam Locos: hides and disables front couplers on S282A/S060
- Enable debug/error logging


## Credits and License

- Author: Zeibach/mspielberg
- Maintainer: Fuggschen
- Additional Coupler Visuals (open/closed state) for AAR/SA3: Rajaneesh R
- Schafenberg Coupler: Micha77

Licensed under the MIT License. See `LICENSE`.

