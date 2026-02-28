# ASG Electronically Assisted Tilt (EAT) Plugin for N.I.N.A.

![ASG Astronomy](http://asgastronomy.com/downloads/eat/ASG-Logo-dark.png)

A [N.I.N.A.](https://nighttime-imaging.eu/) plugin for controlling the **ASG Astronomy Electronically Assisted Tilt (EAT)** device — a camera device that provides precise motorized sensor tilt correction directly from within your imaging session.

---

## What is the EAT Device?

The ASG EAT Photon Cage is a hardware controller built by [ASG Astronomy](https://www.asgastronomy.com) that allows you to electronically adjust the tilt of your camera sensor relative to your optical train. Instead of manually adjusting tilt screws between imaging sessions, the EAT device lets you make precise motorized adjustments in real time — all without touching your equipment.

The device connects to your imaging PC via USB and communicates through a serial interface and runs on common 12v power sources for astronomy equipment.  The unique design offers a lightweight head unit that controls the camera, while a remote control box can be mounted to the side or elsewhere.  The slim profile and weight allow it to be mounted on astrographs, corrector plates, newtonians, refractors, reflectors, CDK's, RC's or just about any telescope.

---

## Features

- **Dockable control panel** inside NINA's imaging tab for easy access during sessions
- **Corner motor control** — independently adjust Top-Left, Top-Right, Bottom-Left, and Bottom-Right tilt motors
- **Directional control** — move Top, Bottom, Left, and Right edges simultaneously
- **Backfocus adjustment** — fine-tune backfocus distance from within NINA
- **Orientation support** — configure camera rotation (0°, 90°, 180°, 270°) so movement directions always match what you see on screen
- **Configurable step sizes** — set default step sizes and override per movement
- **Position readout** — live display of current motor positions for all four corners
- **Save to EEPROM** — persist motor positions to the device so they survive power cycles
- **Zero All** — reset software position values
- **Activity log** — scrollable log of all commands sent and responses received
- **Raw command input** — send direct serial commands to the device for advanced use
- **Auto-connect on startup** — automatically reconnect to the last used port when NINA opens
- **Plugin settings panel** — configure port, baud rate, step sizes, orientation, sensor color, and more

---

## Requirements

- [N.I.N.A. 3.0](https://nighttime-imaging.eu/download/) or later
- ASG Astronomy EAT hardware device
- Windows 10/11 x64
- USB connection to the EAT controller with 12v power

---

## Installation

### Via NINA Plugin Manager (Recommended)
1. Open N.I.N.A. and go to **Options → Plugins**
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
2. Apply power to your ASG device with 12v supply.  We recommend turning power OFF when not in use so the motors do not generate heat while imaging.
3. In NINA, open the **ASG Electronic Tilt** panel from the imaging tab dock
4. Select your COM port from the dropdown and set the baud rate (default: 9600)
5. Click **Connect**

### Controlling Tilt
- Use the **corner buttons** (TL, TR, BL, BR) to adjust individual motor positions
- Use the **directional buttons** (Top, Bottom, Left, Right) to move two motors simultaneously
- Set the **step size** for each movement — larger values move more, smaller values give finer control
- Click **Get Positions** to refresh the current position readout from the device

### Orientation
If your camera is rotated in your imaging train, set the **Orientation** in the plugin settings to match. The plugin will automatically remap movement directions so that pressing "Top" always moves the top edge of your sensor as seen in your image, regardless of physical camera rotation.  It's very important to work out the orientation before doing serious adjustments.  reflectors can flip imagery up/down and refractors can flip imagery side to side.  Note your long edge of your sensor when installing.  Run a baseline focusing curve with Hocus Focus, make a corner tilt adjustment, run again to verify correct position or reverse orientation.

### Saving Positions
Saving positions occurs after each move, no need to save settings.

---

## License

This project is licensed under the [MIT License](https://opensource.org/licenses/MIT).  
Copyright © ASG Astronomy 2025
