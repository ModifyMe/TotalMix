# TotalMix Remote Volume Controller

Control your RME TotalMix main output volume from another computer on the network using keyboard hotkeys via OSC (Open Sound Control).

## Prerequisites

- **TotalMix Computer**: Windows/Mac running TotalMix FX with a Fireface audio interface
- **Remote Computer**: Windows with Python 3.7+ installed
- **Network**: Both computers must be on the **same local network**

---

## Step 1: Install Python Dependencies (Remote Computer)

On the computer you want to control FROM, open a terminal and run:

```bash
pip install python-osc keyboard
```

> **Note**: On Windows, you must run the script as **Administrator** for global hotkeys to work.

---

## Step 2: Find Your IP Addresses

### On the TotalMix Computer (where Fireface is connected):
1. Open Command Prompt (Windows) or Terminal (Mac)
2. Run: `ipconfig` (Windows) or `ifconfig` (Mac)
3. Look for **IPv4 Address** under your network adapter (e.g., `192.168.1.101`)
4. **Write this down** - this is your TOTALMIX_IP

### On the Remote Computer (where you'll run this script):
1. Open Command Prompt
2. Run: `ipconfig`
3. Look for **IPv4 Address** (e.g., `192.168.1.102`)
4. **Write this down** - this is your REMOTE_IP

---

## Step 3: Configure TotalMix FX (on the Fireface computer)

This is the most important step! Open TotalMix FX and follow these steps exactly:

### 3.1 Enable OSC Control
1. Click **Options** in the menu bar
2. Check **Enable OSC Control** ✓

### 3.2 Configure OSC Settings
1. Click **Options** → **Settings**
2. Click the **OSC** tab
3. Configure these settings:

| Setting | Value | Explanation |
|---------|-------|-------------|
| **Remote Controller Select** | `1` | Which controller slot to use |
| **In Use** | ✓ (checked) | **MUST be checked!** |
| **Port incoming** | `7001` | Port TotalMix listens on (must match your `--port`) |
| **Port outgoing** | `9009` | Port for sending feedback (optional) |
| **IP or Host Name** | `YOUR_REMOTE_IP` | **CRITICAL**: Enter the IP of the computer running the Python script! |

> ⚠️ **IMPORTANT**: The "IP or Host Name" field must contain the IP address of your remote computer (the one running this script), NOT 127.0.0.1!

### Example Configuration:
```
Remote Controller Select: 1
In Use: ✓
Port incoming: 7001
Port outgoing: 9009
IP or Host Name: 192.168.1.102   ← Your remote computer's IP
```

---

## Step 4: Configure Windows Firewall (on the Fireface computer)

TotalMix needs to receive UDP packets from your remote computer:

### Option A: Allow TotalMix through Firewall
1. Open **Windows Defender Firewall**
2. Click **Allow an app or feature through Windows Defender Firewall**
3. Click **Change settings** → **Allow another app**
4. Browse to TotalMix FX executable and add it
5. Check both **Private** and **Public** boxes

### Option B: Create Firewall Rule for Port
1. Open **Windows Defender Firewall with Advanced Security**
2. Click **Inbound Rules** → **New Rule**
3. Select **Port** → Next
4. Select **UDP**, enter `7001` → Next
5. Select **Allow the connection** → Next
6. Check all profiles → Next
7. Name it "TotalMix OSC" → Finish

---

## Step 5: Test Connectivity

On your remote computer, run the connectivity test:

```bash
python test_connectivity.py --ip 192.168.1.101
```

Replace `192.168.1.101` with your TotalMix computer's IP.

This will:
- Ping the TotalMix computer to verify network connectivity
- Show you exactly what settings to use in TotalMix

---

## Step 6: Run the Volume Controller

On your remote computer:

```bash
python totalmix_volume_controller.py --ip 192.168.1.101
```

### Command Line Options:
| Option | Default | Description |
|--------|---------|-------------|
| `--ip`, `-i` | (required) | IP address of TotalMix computer |
| `--port`, `-p` | `7001` | OSC port (must match TotalMix "Port incoming") |
| `--fader`, `-f` | `4` | Output fader number (1-8). `4` = PH 7/8 phones output |
| `--step`, `-s` | `0.02` | Volume step size (~1dB per step) |

### Examples:
```bash
# Basic usage
python totalmix_volume_controller.py --ip 192.168.1.101

# Different output (fader 1 = first stereo output)
python totalmix_volume_controller.py --ip 192.168.1.101 --fader 1

# Finer volume control
python totalmix_volume_controller.py --ip 192.168.1.101 --step 0.01

# Different port
python totalmix_volume_controller.py --ip 192.168.1.101 --port 7001
```

---

## Step 7: Use the Hotkeys

Once running, use these keyboard shortcuts:

| Hotkey | Action |
|--------|--------|
| `Ctrl + Shift + Up` | Volume Up |
| `Ctrl + Shift + Down` | Volume Down |
| `Ctrl + Shift + M` | Mute / Unmute |
| `Ctrl + Shift + 0` | Set to 0dB (unity gain) |
| `Ctrl + Shift + Q` | Quit |

---

## Troubleshooting

### Nothing happens when I press hotkeys
- **Run as Administrator** - the keyboard library requires admin privileges for global hotkeys
- Check if the script is receiving keypresses (volume % should change on screen)

### Script runs but TotalMix doesn't respond
1. **Check "In Use"** - Must be checked in TotalMix OSC settings
2. **Check "IP or Host Name"** - Must be your REMOTE computer's IP, not localhost!
3. **Check ports match** - Script's `--port` must match TotalMix "Port incoming"
4. **Check firewall** - Allow UDP port 7009 through Windows Firewall on TotalMix PC
5. **Check network** - Run `ping 192.168.1.101` from remote to verify connectivity

### Wrong fader is moving
- The `--fader` option controls which output fader is adjusted
- Fader 1 = first stereo output, Fader 2 = second, etc.
- For Phones output (PH 7/8), try `--fader 4` (default)
- Check TotalMix to see which fader number corresponds to your main output

### "Permission denied" errors
- Run Command Prompt as Administrator
- Right-click on Terminal → "Run as administrator"

---

## How It Works

This script uses the OSC (Open Sound Control) protocol to communicate with TotalMix FX:

1. **Bus Selection**: First sends `/1/busOutput 1.0` to select the output fader row
2. **Volume Control**: Then sends `/1/volume{N}` with a value 0.0-1.0 to set volume
3. **Mute**: Sends `/1/mainMute` to toggle mute

OSC is a UDP-based protocol commonly used for audio/music software control.

---

## Files

| File | Description |
|------|-------------|
| `totalmix_volume_controller.py` | Main volume controller with hotkeys |
| `test_connectivity.py` | Network connectivity test |
| `debug_osc.py` | Debug tool to test OSC communication |
