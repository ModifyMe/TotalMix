# TotalMix Volume Controller - Technical Breakdown

A comprehensive explanation of how the TotalMix remote volume controller works, covering the OSC protocol, code architecture, and communication flow.

---

## Overview

This program allows you to control RME TotalMix audio mixer software from a remote computer over the network using keyboard hotkeys. It uses the **OSC (Open Sound Control)** protocol to send commands via UDP.

```
┌─────────────────────┐         UDP/OSC          ┌─────────────────────┐
│  Remote Computer    │  ──────────────────────► │  TotalMix Computer  │
│  (runs this script) │       Port 7001          │  (Fireface card)    │
│                     │  ◄────────────────────── │                     │
│  Keyboard Hotkeys   │       Port 9001          │  Audio Interface    │
└─────────────────────┘       (feedback)         └─────────────────────┘
```

---

## What is OSC (Open Sound Control)?

OSC is a protocol designed for communication between computers, synthesizers, and multimedia devices. It's commonly used in audio/music software.

### OSC Message Format
An OSC message consists of:
1. **Address** - A URL-like path (e.g., `/1/volume1`)
2. **Type Tag** - Indicates data types (e.g., `,f` = float)
3. **Arguments** - The actual values

Example OSC message to set volume to 50%:
```
Address:   /1/volume1
Type Tag:  ,f
Value:     0.5 (float)
```

### Why UDP?
OSC uses UDP (User Datagram Protocol) because:
- **Low latency** - No connection handshake
- **No acknowledgment** - Fire-and-forget (fast, but no delivery guarantee)
- **Simple** - Just send packets to IP:port

---

## Code Structure

The program has three main parts:

```
totalmix_volume_controller.py
├── Import & Dependency Checks (lines 1-42)
├── TotalMixController Class (lines 45-173)
│   ├── OSC Constants (addresses)
│   ├── __init__() - Setup
│   ├── _start_listener() - Receive feedback
│   ├── _select_output_bus() - TotalMix quirk
│   ├── set_volume() - Main volume control
│   ├── volume_up/down() - Step controls
│   ├── toggle_mute() - Mute control
│   └── stop() - Exit
├── print_banner() - UI (lines 176-189)
└── main() - Entry point (lines 192-276)
```

---

## Detailed Code Breakdown

### 1. Imports and Dependency Checks (Lines 1-42)

```python
from pythonosc import udp_client
from pythonosc.dispatcher import Dispatcher
from pythonosc.osc_server import BlockingOSCUDPServer
import keyboard
```

**Purpose**: Load required libraries with graceful error handling.

| Library | What it does |
|---------|--------------|
| `pythonosc.udp_client` | Sends OSC messages via UDP |
| `pythonosc.dispatcher` | Routes incoming OSC messages to handlers |
| `pythonosc.osc_server` | Listens for incoming OSC messages |
| `keyboard` | Registers global hotkeys (system-wide) |

**Why try/except?** If libraries aren't installed, we show a helpful error message instead of a cryptic Python traceback.

---

### 2. OSC Address Constants (Lines 48-54)

```python
BUS_OUTPUT = "/1/busOutput"      # Select output bus
VOLUME_1 = "/1/volume1"          # First fader
MASTER_VOLUME = "/1/mastervolume"  # Master volume
MASTER_MUTE = "/1/mainMute"      # Mute toggle
```

**Purpose**: TotalMix uses specific OSC addresses to control different functions.

**Why `/1/`?** TotalMix supports multiple "pages" of controls (like TouchOSC). `/1/` refers to page 1.

**The Bus Problem**: TotalMix's OSC is designed for TouchOSC-style controllers. Volume faders are relative to the *currently selected bus*. You must first send `/1/busOutput 1.0` to select the output bus, then send `/1/volume1` etc.

---

### 3. The Constructor `__init__()` (Lines 61-93)

```python
def __init__(self, ip: str, port: int = 7001, receive_port: int = 9001, 
             step: float = 0.02, faders: list = None):
```

**Parameters**:
| Parameter | Default | Purpose |
|-----------|---------|---------|
| `ip` | Required | TotalMix computer's IP address |
| `port` | 7001 | Port to SEND commands to (TotalMix's incoming port) |
| `receive_port` | 9001 | Port to listen for feedback (TotalMix's outgoing port) |
| `step` | 0.02 | Volume change per keypress (~1dB) |
| `faders` | [1-6] | Which output faders to control |

**Key Actions**:
1. Store configuration
2. Build list of volume addresses (`/1/volume1`, `/1/volume2`, etc.)
3. Create UDP client for sending
4. Start background listener thread
5. Send initial bus selection command

---

### 4. The OSC Client (Line 84)

```python
self.client = udp_client.SimpleUDPClient(ip, port)
```

**What it does**: Creates a UDP socket that will send packets to `ip:port`.

**Important**: UDP is "connectionless" - this doesn't actually connect or verify the target is listening. It just creates a socket ready to send.

---

### 5. Feedback Listener (Lines 95-119)

```python
def _start_listener(self) -> None:
    def listener_thread():
        dispatcher = Dispatcher()
        dispatcher.set_default_handler(self._handle_osc_feedback)
        server = BlockingOSCUDPServer(("0.0.0.0", self.receive_port), dispatcher)
        server.serve_forever()
    
    thread = threading.Thread(target=listener_thread, daemon=True)
    thread.start()
```

**Purpose**: Listen for OSC messages FROM TotalMix (feedback).

**How it works**:
1. `Dispatcher` - Routes incoming messages to handler functions
2. `set_default_handler` - Catch-all for any message
3. `BlockingOSCUDPServer` - Listens on `0.0.0.0:9001` (all interfaces)
4. `daemon=True` - Thread dies when main program exits

**Why listen?** Two reasons:
- Confirms TotalMix is actually receiving our commands (connection verification)
- Gets current volume state from TotalMix to sync our display

---

### 6. Bus Selection (Lines 121-124)

```python
def _select_output_bus(self) -> None:
    self.client.send_message(self.BUS_OUTPUT, 1.0)
```

**Why this is necessary**: TotalMix's OSC implementation was designed for TouchOSC templates where you have "pages" of controls. When you send `/1/volume1`, it controls the first fader of the *currently selected row*.

By sending `/1/busOutput 1.0`, we tell TotalMix: "I want to control the OUTPUT faders" (the bottom row in TotalMix).

Without this, volume commands might control the wrong faders or do nothing.

---

### 7. Volume Control (Lines 126-149)

```python
def set_volume(self, value: float) -> None:
    value = max(self.MIN_VOLUME, min(self.MAX_VOLUME, value))
    self.current_volume = value
    
    self.client.send_message(self.BUS_OUTPUT, 1.0)  # Ensure bus is selected
    for addr in self.volume_addresses:
        self.client.send_message(addr, float(value))
    self.client.send_message(self.MASTER_VOLUME, float(value))
```

**Step by step**:
1. **Clamp value** - Keep between 0.0 and 1.0
2. **Store current** - Track locally for up/down stepping
3. **Select bus** - Ensure output bus is active (sent every time for reliability)
4. **Send to ALL faders** - Loop through faders [1,2,3,4,5,6] and send volume to each
5. **Send to master** - Also try the master volume address for compatibility
6. **Display** - Show visual volume bar in console

**Why send to multiple faders?** You wanted to control all 6 analog outputs simultaneously, so we send the same volume to each one.

---

### 8. Volume Up/Down (Lines 151-157)

```python
def volume_up(self) -> None:
    self.set_volume(self.current_volume + self.step)

def volume_down(self) -> None:
    self.set_volume(self.current_volume - self.step)
```

**Purpose**: Increment/decrement by `step` (default 0.02 = ~1dB).

**Why step = 0.02?** 
- Volume range is 0.0 to 1.0
- Unity gain (0dB) is at ~0.72
- 0.02 gives ~50 steps from 0 to 1.0
- Each step is roughly 1dB change

---

### 9. Mute Toggle (Lines 159-164)

```python
def toggle_mute(self) -> None:
    self.is_muted = not self.is_muted
    self.client.send_message(self.MASTER_MUTE, 1.0 if self.is_muted else 0.0)
```

**How OSC toggles work**: Send `1.0` to activate, `0.0` to deactivate.

---

### 10. Main Entry Point (Lines 192-276)

```python
def main():
    parser = argparse.ArgumentParser(...)
    # Parse command line arguments
    
    controller = TotalMixController(ip=args.ip, ...)
    
    # Register global hotkeys
    keyboard.add_hotkey("ctrl+shift+up", controller.volume_up)
    keyboard.add_hotkey("ctrl+shift+down", controller.volume_down)
    keyboard.add_hotkey("ctrl+shift+m", controller.toggle_mute)
    keyboard.add_hotkey("ctrl+shift+0", controller.set_unity_gain)
    keyboard.add_hotkey("ctrl+shift+q", controller.stop)
    
    # Main loop - wait for quit signal
    while controller.running:
        time.sleep(0.1)
```

**Flow**:
1. Parse command-line arguments (--ip, --port, --faders, etc.)
2. Create the controller (starts OSC client + listener)
3. Register global hotkeys using `keyboard` library
4. Enter main loop that just waits
5. When `controller.stop()` is called, the loop exits

**Why `time.sleep(0.1)`?** The hotkey library handles keypresses in a background thread. The main thread just needs to stay alive - sleeping reduces CPU usage.

---

## Network Communication Flow

When you press **Ctrl+Shift+Up**:

```
1. keyboard library detects hotkey
   ↓
2. Calls controller.volume_up()
   ↓
3. Calculates new volume (current + 0.02)
   ↓
4. Calls set_volume(new_value)
   ↓
5. Sends OSC messages via UDP:
   → /1/busOutput 1.0        (select output bus)
   → /1/volume1 0.52         (set fader 1)
   → /1/volume2 0.52         (set fader 2)
   → /1/volume3 0.52         (set fader 3)
   → /1/volume4 0.52         (set fader 4)
   → /1/volume5 0.52         (set fader 5)
   → /1/volume6 0.52         (set fader 6)
   → /1/mastervolume 0.52    (master volume)
   ↓
6. TotalMix receives UDP packets on port 7001
   ↓
7. TotalMix moves the faders
   ↓
8. TotalMix sends feedback to your IP on port 9001
   ↓
9. Our listener receives feedback
   ↓
10. Display updates in console
```

---

## TotalMix OSC Settings Explained

| Setting | Value | What it means |
|---------|-------|---------------|
| Enable OSC Control | ✓ | Turns on OSC reception |
| Remote Controller | 1 | Which controller slot (1-4) |
| In Use | ✓ | Activates this controller slot |
| Port incoming | 7001 | Port TotalMix LISTENS on |
| Port outgoing | 9001 | Port TotalMix SENDS feedback to |
| IP or Host Name | Your IP | Where to send feedback (YOUR computer's IP!) |

---

## Why Certain Design Decisions

### Why control multiple faders?
Your setup has 6 analog outputs, and you wanted them all to track together. So we loop and send to each.

### Why also send to `/1/mastervolume`?
Some TotalMix versions/interfaces respond to this address. Sending to both maximizes compatibility.

### Why select bus before every volume command?
TotalMix may "forget" the selected bus, or another OSC controller might change it. Sending it every time ensures reliability.

### Why use threads for the listener?
The listener blocks (waits for data). If we did this in the main thread, we couldn't respond to hotkeys. The background thread handles incoming messages while the main thread handles hotkeys.

### Why daemon=True for threads?
Daemon threads are killed automatically when the main program exits. Without this, the program might hang on exit waiting for the listener thread.

---

## File Summary

| File | Purpose |
|------|---------|
| `totalmix_volume_controller.py` | Main program - hotkeys + OSC control |
| `debug_osc.py` | Test tool - sends test OSC commands |
| `test_connectivity.py` | Network test - pings and shows settings |
| `TotalMixController/Program.cs` | C# version - same functionality, no Python needed |

---

# C# Version Breakdown

The C# version provides the same functionality but uses native Windows APIs instead of Python libraries. This makes it fully self-contained with no runtime dependencies.

## Code Structure

```
Program.cs
├── OscClient class (lines 13-73)
│   └── BuildOscMessage() - Manual OSC packet construction
├── TotalMixController class (lines 78-167)
│   └── Same logic as Python version
└── Program class (lines 169-356)
    ├── Windows API imports (DllImport)
    ├── Hotkey registration (RegisterHotKey)
    └── Windows message loop (GetMessage)
```

---

## Key Differences from Python Version

| Aspect | Python | C# |
|--------|--------|-----|
| OSC Library | `python-osc` (pre-built) | Manual packet construction |
| Hotkeys | `keyboard` library | Windows API `RegisterHotKey` |
| Event Loop | `time.sleep()` polling | Windows message pump |
| Dependencies | Requires pip install | Self-contained EXE |
| Size | ~9 MB (PyInstaller) | ~71 MB (includes .NET runtime) |

---

## 1. OscClient Class (Lines 13-73)

Unlike Python where we use the `python-osc` library, the C# version builds OSC packets manually.

### OSC Packet Format

```
┌─────────────────┬──────────────┬─────────────────┐
│  Address        │  Type Tag    │  Value          │
│  (padded to 4)  │  ",f\0\0"    │  (big-endian)   │
└─────────────────┴──────────────┴─────────────────┘
```

### BuildOscMessage() Explained (Lines 37-67)

```csharp
private byte[] BuildOscMessage(string address, float value)
{
    // Step 1: Calculate padded address length
    // OSC requires addresses to be padded to 4-byte boundaries
    int addressLen = address.Length + 1;  // +1 for null terminator
    int paddedAddressLen = (addressLen + 3) & ~3;  // Round up to multiple of 4
```

**Why `(addressLen + 3) & ~3`?**
This is a bitwise trick to round up to the nearest multiple of 4:
- `+ 3` ensures we round UP, not down
- `& ~3` clears the last 2 bits, making it divisible by 4

Example: `/1/volume1` (11 chars)
- `addressLen = 11 + 1 = 12`
- `paddedAddressLen = (12 + 3) & ~3 = 15 & 0xFFFFFFFC = 12` ✓

```csharp
    // Step 2: Create packet buffer
    byte[] packet = new byte[paddedAddressLen + 4 + 4];
    //                       address       type  float
```

```csharp
    // Step 3: Write address as ASCII bytes
    byte[] addressBytes = Encoding.ASCII.GetBytes(address);
    Array.Copy(addressBytes, 0, packet, 0, addressBytes.Length);
    // Rest is already 0 (null terminator + padding)
```

```csharp
    // Step 4: Write type tag ",f" (comma + 'f' for float)
    int typeTagPos = paddedAddressLen;
    packet[typeTagPos] = (byte)',';
    packet[typeTagPos + 1] = (byte)'f';
```

```csharp
    // Step 5: Write float in BIG-ENDIAN format
    // OSC always uses big-endian (network byte order)
    byte[] floatBytes = BitConverter.GetBytes(value);
    if (BitConverter.IsLittleEndian)
    {
        Array.Reverse(floatBytes);  // Convert to big-endian
    }
    Array.Copy(floatBytes, 0, packet, typeTagPos + 4, 4);
```

**Why big-endian?** OSC uses network byte order (big-endian) for cross-platform compatibility. x86/x64 processors are little-endian, so we reverse the bytes.

---

## 2. TotalMixController Class (Lines 78-167)

This is nearly identical to the Python version:

```csharp
public void SetVolume(float value)
{
    value = Math.Clamp(value, 0.0f, 1.0f);  // Keep in range
    _currentVolume = value;

    _oscClient.Send(BUS_OUTPUT, 1.0f);  // Select output bus
    
    foreach (int fader in _faders)
    {
        string address = $"/1/volume{fader}";
        _oscClient.Send(address, value);  // Send to each fader
    }
    
    _oscClient.Send(MASTER_VOLUME, value);  // Also send to master
}
```

---

## 3. Windows API Hotkeys (Lines 171-218)

The C# version uses native Windows APIs instead of a keyboard library.

### API Imports

```csharp
[DllImport("user32.dll")]
private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

[DllImport("user32.dll")]
private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

[DllImport("user32.dll")]
private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
```

**What is DllImport?** 
It allows C# to call functions in native Windows DLLs. `user32.dll` contains window management functions.

### Modifier Flags

```csharp
private const uint MOD_CONTROL = 0x0002;  // Ctrl key
private const uint MOD_SHIFT = 0x0004;    // Shift key
private const uint MOD_NOREPEAT = 0x4000; // Don't repeat when held
```

These are combined with bitwise OR: `MOD_CONTROL | MOD_SHIFT` = Ctrl+Shift

### Virtual Key Codes

```csharp
private const uint VK_UP = 0x26;    // Up arrow
private const uint VK_DOWN = 0x28;  // Down arrow
private const uint VK_M = 0x4D;     // M key
private const uint VK_0 = 0x30;     // 0 key
private const uint VK_Q = 0x51;     // Q key
```

These are Windows virtual key codes that uniquely identify each key.

### Registering Hotkeys

```csharp
uint mods = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
RegisterHotKey(IntPtr.Zero, HOTKEY_VOLUME_UP, mods, VK_UP);
```

Parameters:
- `IntPtr.Zero` - No window handle (system-wide hotkey)
- `HOTKEY_VOLUME_UP` - Our ID to identify this hotkey later
- `mods` - Modifier keys (Ctrl+Shift)
- `VK_UP` - The main key (Up arrow)

---

## 4. Windows Message Loop (Lines 283-308)

```csharp
while (_running && GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
{
    if (msg.message == WM_HOTKEY)
    {
        int id = (int)msg.wParam;  // Which hotkey was pressed?
        switch (id)
        {
            case HOTKEY_VOLUME_UP:
                controller.VolumeUp();
                break;
            // ... other cases ...
        }
    }
}
```

**How Windows hotkeys work:**
1. `RegisterHotKey()` tells Windows: "When this key combo is pressed, send me a message"
2. `GetMessage()` waits for any Windows message (blocking call)
3. When our hotkey is pressed, Windows sends a `WM_HOTKEY` message
4. `msg.wParam` contains our hotkey ID so we know which one was pressed
5. We call the appropriate controller method

**Why a message loop?**
Windows is event-driven. Instead of polling "is key pressed?", we ask Windows to notify us. This is more efficient (no CPU usage while waiting).

---

## 5. Cleanup (Lines 310-315)

```csharp
UnregisterHotKey(IntPtr.Zero, HOTKEY_VOLUME_UP);
UnregisterHotKey(IntPtr.Zero, HOTKEY_VOLUME_DOWN);
// ... etc ...
```

**Why unregister?**
Hotkeys are system resources. If we don't unregister them, they stay registered until reboot, and other programs can't use those key combinations.

---

## Comparison: Python vs C# Event Handling

### Python (using `keyboard` library)

```python
# Register callback function
keyboard.add_hotkey("ctrl+shift+up", controller.volume_up)

# Main loop just keeps program alive
while controller.running:
    time.sleep(0.1)  # Library handles hotkeys in background thread
```

The `keyboard` library:
- Runs a background thread
- Uses low-level keyboard hooks
- Calls our function when hotkey is pressed

### C# (using Windows API)

```csharp
// Register with Windows
RegisterHotKey(IntPtr.Zero, HOTKEY_VOLUME_UP, mods, VK_UP);

// Main loop processes Windows messages
while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
{
    if (msg.message == WM_HOTKEY)
        // Handle hotkey
}
```

The Windows API:
- Registers directly with the OS
- Uses Windows message queue
- GetMessage blocks until a message arrives

---

## Summary: Why Two Versions?

| Use Case | Recommended Version |
|----------|---------------------|
| Quick testing | Python (easier to modify) |
| Offline deployment | C# (single EXE, no runtime) |
| Integration with C# app | C# (same language) |
| Cross-platform potential | Python (with modifications) |
| Smaller file size | Python (~9 MB vs ~71 MB) |
