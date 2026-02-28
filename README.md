<p align="center">
  <img src="https://asgastronomy.com/downloads/eat/ASG-Logo-dark.png" alt="ASG Astronomy" width="200"/>
</p>

<h1 align="center">ASG Electronically Assisted Tilt (EAT) Plugin for N.I.N.A.</h1>

<p align="center">
  Precise motorized sensor tilt correction and backfocus control from within N.I.N.A.'s imaging tab.
</p>

<p align="center">
  <a href="https://github.com/Josh-Jones-76/ASG.EAT.Plugin/releases/latest"><img src="https://img.shields.io/github/v/release/Josh-Jones-76/ASG.EAT.Plugin?style=flat-square" alt="Latest Release"/></a>
  <a href="https://opensource.org/licenses/MIT"><img src="https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square" alt="License: MIT"/></a>
  <img src="https://img.shields.io/badge/NINA-3.0%2B-blue?style=flat-square" alt="NINA 3.0+"/>
</p>

---

## What is the EAT Device?

The ASG EAT Photon Cage is a hardware controller built by [ASG Astronomy](https://www.asgastronomy.com) that allows you to electronically adjust the tilt of your camera sensor relative to your optical train. Instead of manually adjusting tilt screws between imaging sessions, the EAT device lets you make precise motorized adjustments in real time — all without touching your equipment.

The device connects to your imaging PC via USB and communicates through a serial interface and runs on common 12v power sources for astronomy equipment. The unique design offers a lightweight head unit that controls the camera, while a remote control box can be mounted to the side or elsewhere. The slim profile and weight allow it to be mounted on astrographs, corrector plates, newtonians, refractors, reflectors, CDK's, RC's or just about any telescope.

---

## Screenshots

| Dockable Control Panel | Plugin Settings |
|:---:|:---:|
| ![Dock Window](https://asgastronomy.com/downloads/eat/nina/nina%20dock%20window.png) | ![Plugin Settings](https://asgastronomy.com/downloads/eat/nina/nina%20plugin%20settings.png) |

---

## Features

- **Dockable control panel** inside NINA's imaging tab for easy access during sessions
- **Corner motor control** — independently adjust Top-Left, Top-Right, Bottom-Left, and Bottom-Right tilt motors
- **Directional control** — move Top, Bottom, Left, and Right edges simultaneously using coordinated 4-motor movements
- **Backfocus adjustment** — move all four motors together with ~1.8 micron per step resolution and ~2mm total range
- **Orientation support** — configure camera rotation (0°, 90°, 180°, 270°) so movement directions always match what you see on screen
- **Real-time position tracking** — live display of current motor positions for all four corners
- **Configurable step sizes** — set default step sizes and override per movement
- **Save to EEPROM** — persist motor positions to the device so they survive power cycles
- **Activity log** — timestamped log of all commands sent and responses received with serial traffic monitoring
- **Raw command input** — send direct serial commands to the device for advanced use
- **Auto-connect on startup** — automatically reconnect to the last used port when NINA opens
- **Motor configuration** — configurable speed, max speed, and acceleration with settings saved to the device
- **Sensor color customization** — choose from 16 color presets for the tilt visualization
- **Connection status LED** — visual indicator: gray (disconnected), green (idle), red (moving)

---

## Requirements

- [N.I.N.A. 3.0](https://nighttime-imaging.eu/download/) or later
- ASG Astronomy EAT hardware device
- Windows 10/11 x64
- USB connection to the EAT controller with 12v power

---

## Hardware Specifications

| Specification | Detail |
|---|---|
| **Device** | ASG Electronically Assisted Tilt (EAT) |
| **Controller** | Arduino-based (firmware V7+) |
| **Connection** | USB Serial (COM port) |
| **Supported Baud Rates** | 9600, 19200, 38400, 57600, 115200 |
| **Weight** | 23 oz (head unit) |
| **Diameter** | 136mm |
| **Step Resolution** | ~1.8 microns per step |
| **Total Range** | ~2mm in/out |

---

## Installation

### Via NINA Plugin Manager (Recommended)
1. Open N.I.N.A. and go to **Options > Plugins**
2. Search for **ASG Electronically Assisted Tilt**
3. Click **Install** and restart NINA
4. Plugin offers the settings, dockable window allows controlled movements.

### Manual Installation
1. Download the latest `ASG.EAT.Plugin.dll` from the [Releases](https://github.com/Josh-Jones-76/ASG.EAT.Plugin/releases) page
2. Copy the DLL to:
   ```
   %LOCALAPPDATA%\NINA\Plugins\3.0.0\ASG.EAT.Plugin\
   ```
3. Restart N.I.N.A.

---

## How to Use

### Connecting to the Device
1. Plug your ASG EAT device into your PC via USB
2. Apply power to your ASG device with 12v supply. We recommend turning power OFF when not in use so the motors do not generate heat while imaging.
3. In NINA, open the **ASG Electronic Tilt** panel from the imaging tab dock
4. Select your COM port from the dropdown and set the baud rate (default: 9600)
5. Click **Connect**

### Controlling Tilt
- Use the **corner buttons** (TL, TR, BL, BR) to adjust individual motor positions
- Use the **directional buttons** (Top, Bottom, Left, Right) to move two motors simultaneously
- Set the **step size** for each movement — larger values move more, smaller values give finer control
- Click **Get Positions** to refresh the current position readout from the device

### Orientation
If your camera is rotated in your imaging train, set the **Orientation** in the plugin settings to match. The plugin will automatically remap movement directions so that pressing "Top" always moves the top edge of your sensor as seen in your image, regardless of physical camera rotation. It's very important to work out the orientation before doing serious adjustments. Reflectors can flip imagery up/down and refractors can flip imagery side to side. Note your long edge of your sensor when installing. Run a baseline focusing curve with Hocus Focus, make a corner tilt adjustment, run again to verify correct position or reverse orientation.

### Saving Positions
Saving positions occurs after each move, no need to save settings.

---

## Configuration

Access plugin settings via **Options > ASG Electronically Assisted Tilt** in NINA.

- **Default Step Size** — Number of steps per button press (1–50)
- **Motor Step Size** — Microns per motor step (default 1.8)
- **Motor Speed / Max Speed / Acceleration** — Tune motor performance and save to device
- **Camera Orientation** — Match directional controls to your sensor rotation
- **Sensor Color** — Customize the tilt visualization color
- **Show Activity Log** — Toggle the serial traffic log
- **Show Raw Command** — Toggle the advanced command input
- **Auto-Connect** — Reconnect automatically on NINA startup

Settings are persisted locally at `%localappdata%\NINA\Plugins\ASG.EAT.Plugin\settings.json`.

---

## Support

- **Website:** [asgastronomy.com](https://www.asgastronomy.com)
- **Issues:** [GitHub Issues](https://github.com/Josh-Jones-76/ASG.EAT.Plugin/issues)
- **Releases:** [GitHub Releases](https://github.com/Josh-Jones-76/ASG.EAT.Plugin/releases)
- **Contact:** [sales@asgastronomy.com](mailto:sales@asgastronomy.com)

## License

This project is licensed under the [MIT License](https://opensource.org/licenses/MIT).
Copyright (c) ASG Astronomy 2025
