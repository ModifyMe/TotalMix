# TotalMix Remote Volume Controller

Control your RME TotalMix main output volume from another computer on the network using keyboard hotkeys.

## Requirements

- Python 3.7+
- TotalMix FX running on the target computer with OSC enabled

## Installation

```bash
pip install python-osc keyboard
```

> **Note**: On Windows, run the script as Administrator for global hotkeys to work properly.

## TotalMix Setup (On the computer with the Fireface)

1. Open **TotalMix FX**
2. Go to **Options → Settings → OSC** tab
3. Enable **OSC Control**
4. Set **Remote Controller** to `1`
5. Set **Incoming Port** to `7001` (or your preferred port)
6. Check **"In Use"**
7. Note the IP address of this computer (e.g., `192.168.1.100`)

Also ensure your Windows Firewall allows incoming UDP connections on port 7001.

## Usage

```bash
# Basic usage
python totalmix_volume_controller.py --ip 192.168.1.100

# Custom port
python totalmix_volume_controller.py --ip 192.168.1.100 --port 7001

# Smaller volume steps (finer control)
python totalmix_volume_controller.py --ip 192.168.1.100 --step 0.01
```

## Hotkeys

| Hotkey | Action |
|--------|--------|
| `Ctrl + Shift + Up` | Volume Up |
| `Ctrl + Shift + Down` | Volume Down |
| `Ctrl + Shift + M` | Mute / Unmute |
| `Ctrl + Shift + 0` | Set to 0dB (unity gain) |
| `Ctrl + Shift + Q` | Quit |

## Troubleshooting

### No response from TotalMix
- Verify the IP address is correct
- Check that OSC is enabled in TotalMix settings
- Ensure port 7001 is open in Windows Firewall on the TotalMix computer
- Try using `127.0.0.1` if testing on the same machine

### "Permission denied" or hotkeys not working
- Run the script as Administrator (Windows)
- The keyboard library requires elevated privileges for global hotkeys

### Finding the correct IP
On the TotalMix computer, run:
- **Windows**: `ipconfig` in CMD
- **macOS/Linux**: `ifconfig` or `ip addr`

Look for the IPv4 address on your local network (usually starts with `192.168.` or `10.`).

## OSC Reference

The script uses these OSC addresses:
- `/1/mastervolume` - Main output volume (0.0 to 1.0)
- `/1/mainMute` - Main output mute toggle

Volume is sent as a float where:
- `0.0` = -∞ dB (silence)
- `0.72` ≈ 0 dB (unity gain)
- `1.0` = +6 dB (maximum)
