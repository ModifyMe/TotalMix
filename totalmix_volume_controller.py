#!/usr/bin/env python3
"""
TotalMix Volume Controller
==========================
Control RME TotalMix main output volume via OSC from another computer.

Usage:
    python totalmix_volume_controller.py --ip <TOTALMIX_IP> [--port <PORT>]
    
Hotkeys:
    Ctrl + Shift + Up    : Volume Up
    Ctrl + Shift + Down  : Volume Down
    Ctrl + Shift + M     : Mute/Unmute
    Ctrl + Shift + 0     : Set to 0dB (unity gain)
    Ctrl + Shift + Q     : Quit

Requirements:
    pip install python-osc keyboard
"""

import argparse
import sys
import threading
import time
from typing import Optional

try:
    from pythonosc import udp_client
    from pythonosc.dispatcher import Dispatcher
    from pythonosc.osc_server import BlockingOSCUDPServer
except ImportError:
    print("Error: python-osc library not found.")
    print("Install it with: pip install python-osc")
    sys.exit(1)

try:
    import keyboard
except ImportError:
    print("Error: keyboard library not found.")
    print("Install it with: pip install keyboard")
    print("Note: On Windows, run as Administrator for global hotkeys.")
    sys.exit(1)


class TotalMixController:
    """Controls TotalMix volume via OSC protocol."""
    
    # OSC addresses for TotalMix
    # TotalMix uses a bus-based system - you need to select the bus first
    BUS_OUTPUT = "/1/busOutput"      # Select output bus (1.0 to activate)
    VOLUME_1 = "/1/volume1"          # First fader of selected bus (main out)
    MASTER_VOLUME = "/1/mastervolume"  # Alternative: direct master volume
    MASTER_MUTE = "/1/mainMute"
    MAIN_DIM = "/1/mainDim"
    
    # Volume values
    UNITY_GAIN = 0.7197  # Approximately 0dB in TotalMix (varies by interface)
    MIN_VOLUME = 0.0
    MAX_VOLUME = 1.0
    
    def __init__(self, ip: str, port: int = 7001, receive_port: int = 9001, 
                 step: float = 0.02, faders: list = None):
        """
        Initialize the TotalMix controller.
        
        Args:
            ip: IP address of the computer running TotalMix
            port: OSC port to SEND to (TotalMix 'Port incoming', default 7001)
            receive_port: OSC port to RECEIVE on (TotalMix 'Port outgoing', default 9001)
            step: Volume step size (0.02 = roughly 1dB)
        """
        self.ip = ip
        self.port = port
        self.receive_port = receive_port
        self.step = step
        self.current_volume = 0.5  # Start at middle, will be updated
        self.is_muted = False
        self.running = True
        self.faders = faders if faders else [1, 2, 3, 4, 5, 6]  # Default: all 6 analog stereo outputs
        self.volume_addresses = [f"/1/volume{f}" for f in self.faders]
        self.received_feedback = False
        
        # Create OSC client (note: UDP is connectionless, no actual connection is made)
        self.client = udp_client.SimpleUDPClient(ip, port)
        print(f"→ Sending OSC to {ip}:{port}")
        print(f"→ Listening for feedback on port {receive_port}")
        print(f"→ Controlling faders: {self.faders} (all analog outputs)")
        
        # Start listener thread for feedback from TotalMix
        self._start_listener()
        
        # Select the output bus on startup
        self._select_output_bus()
        
    def _handle_osc_feedback(self, address: str, *args) -> None:
        """Handle incoming OSC messages from TotalMix."""
        if not self.received_feedback:
            self.received_feedback = True
            print("\n✓ Received feedback from TotalMix - connection verified!")
        
        # Update our volume if TotalMix sends it
        if address in self.volume_addresses and args:
            self.current_volume = float(args[0])
            
    def _start_listener(self) -> None:
        """Start background thread to listen for OSC feedback."""
        def listener_thread():
            try:
                dispatcher = Dispatcher()
                dispatcher.set_default_handler(self._handle_osc_feedback)
                server = BlockingOSCUDPServer(("0.0.0.0", self.receive_port), dispatcher)
                print(f"✓ Listening on port {self.receive_port}")
                server.serve_forever()
            except OSError as e:
                print(f"⚠ Could not listen on port {self.receive_port}: {e}")
        
        thread = threading.Thread(target=listener_thread, daemon=True)
        thread.start()
        time.sleep(0.2)  # Give listener time to start
        
    def _select_output_bus(self) -> None:
        """Select the output bus so volume commands work on main output."""
        self.client.send_message(self.BUS_OUTPUT, 1.0)
        print("→ Sent bus selection command")
        
    def set_volume(self, value: float) -> None:
        """Set master volume to specific value (0.0 to 1.0)."""
        value = max(self.MIN_VOLUME, min(self.MAX_VOLUME, value))
        self.current_volume = value
        
        # Send to both addresses for maximum compatibility
        # First ensure output bus is selected, then send volume to ALL faders
        self.client.send_message(self.BUS_OUTPUT, 1.0)
        for addr in self.volume_addresses:
            self.client.send_message(addr, float(value))
        # Also try the direct master volume address
        self.client.send_message(self.MASTER_VOLUME, float(value))
        
        # Convert to approximate dB for display
        if value <= 0:
            db_str = "-∞ dB"
        else:
            # Rough approximation: 0.7197 ≈ 0dB, logarithmic scale
            db = 20 * (value - self.UNITY_GAIN) / self.UNITY_GAIN * 3
            db_str = f"{db:+.1f} dB"
        
        bar_length = int(value * 30)
        bar = "█" * bar_length + "░" * (30 - bar_length)
        print(f"\r🔊 Volume: [{bar}] {value:.1%} (~{db_str})  ", end="", flush=True)
        
    def volume_up(self) -> None:
        """Increase volume by one step."""
        self.set_volume(self.current_volume + self.step)
        
    def volume_down(self) -> None:
        """Decrease volume by one step."""
        self.set_volume(self.current_volume - self.step)
        
    def toggle_mute(self) -> None:
        """Toggle mute on/off."""
        self.is_muted = not self.is_muted
        self.client.send_message(self.MASTER_MUTE, 1.0 if self.is_muted else 0.0)
        status = "🔇 MUTED" if self.is_muted else "🔊 UNMUTED"
        print(f"\r{status}                                          ", end="", flush=True)
        
    def set_unity_gain(self) -> None:
        """Set volume to 0dB (unity gain)."""
        self.set_volume(self.UNITY_GAIN)
        print(" [0dB]", end="", flush=True)
        
    def stop(self) -> None:
        """Stop the controller."""
        self.running = False


def print_banner():
    """Print startup banner."""
    print("""
╔══════════════════════════════════════════════════════════════╗
║             TotalMix Remote Volume Controller                ║
╠══════════════════════════════════════════════════════════════╣
║  Hotkeys:                                                    ║
║    Ctrl + Shift + Up     →  Volume Up                        ║
║    Ctrl + Shift + Down   →  Volume Down                      ║
║    Ctrl + Shift + M      →  Mute / Unmute                    ║
║    Ctrl + Shift + 0      →  Set to 0dB (unity gain)          ║
║    Ctrl + Shift + Q      →  Quit                             ║
╚══════════════════════════════════════════════════════════════╝
""")


def main():
    parser = argparse.ArgumentParser(
        description="Control TotalMix main volume via OSC",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  python totalmix_volume_controller.py --ip 192.168.1.100
  python totalmix_volume_controller.py --ip 192.168.1.100 --port 7001 --step 0.01

TotalMix Setup:
  1. Open TotalMix FX on the host computer
  2. Go to Options → Settings → OSC tab
  3. Enable OSC Control
  4. Set Remote Controller to 1
  5. Set Incoming Port (default 7001)
  6. Check "In Use"
        """
    )
    
    parser.add_argument(
        "--ip", "-i",
        required=True,
        help="IP address of the computer running TotalMix"
    )
    parser.add_argument(
        "--port", "-p",
        type=int,
        default=7001,
        help="OSC port (default: 7001)"
    )
    parser.add_argument(
        "--step", "-s",
        type=float,
        default=0.02,
        help="Volume step size, 0.01-0.1 (default: 0.02, ~1dB)"
    )
    parser.add_argument(
        "--faders", "-f",
        type=str,
        default="1,2,3,4,5,6",
        help="Output fader numbers, comma-separated (default: '1,2,3,4,5,6' for all analog outputs)"
    )
    
    args = parser.parse_args()
    
    print_banner()
    print(f"Connecting to TotalMix at {args.ip}:{args.port}...")
    
    # Parse faders from comma-separated string
    faders_list = [int(f.strip()) for f in args.faders.split(',')]
    
    try:
        controller = TotalMixController(
            ip=args.ip,
            port=args.port,
            step=args.step,
            faders=faders_list
        )
    except Exception as e:
        print(f"✗ Failed to connect: {e}")
        sys.exit(1)
    
    # Register hotkeys
    keyboard.add_hotkey("ctrl+shift+up", controller.volume_up)
    keyboard.add_hotkey("ctrl+shift+down", controller.volume_down)
    keyboard.add_hotkey("ctrl+shift+m", controller.toggle_mute)
    keyboard.add_hotkey("ctrl+shift+0", controller.set_unity_gain)
    keyboard.add_hotkey("ctrl+shift+q", controller.stop)
    
    print("\n✓ Hotkeys registered. Press Ctrl+Shift+Q to quit.\n")
    print("Waiting for input...\n")
    
    # Keep running until quit
    try:
        while controller.running:
            time.sleep(0.1)
    except KeyboardInterrupt:
        pass
    
    print("\n\nGoodbye!")
    keyboard.unhook_all()


if __name__ == "__main__":
    main()
