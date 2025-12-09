#!/usr/bin/env python3
"""
Debug script to test OSC communication with TotalMix.
This will help identify the correct settings and addresses.
"""

import argparse
import time
import threading
from pythonosc import udp_client
from pythonosc.dispatcher import Dispatcher
from pythonosc.osc_server import BlockingOSCUDPServer

def handle_osc_message(address, *args):
    """Print any OSC message received from TotalMix."""
    print(f"  RECEIVED: {address} = {args}")

def start_listener(port):
    """Start a listener to receive OSC messages from TotalMix."""
    dispatcher = Dispatcher()
    dispatcher.set_default_handler(handle_osc_message)
    
    try:
        server = BlockingOSCUDPServer(("0.0.0.0", port), dispatcher)
        print(f"✓ Listening for TotalMix feedback on port {port}")
        server.serve_forever()
    except OSError as e:
        print(f"✗ Could not listen on port {port}: {e}")

def main():
    parser = argparse.ArgumentParser(description="Debug TotalMix OSC connection")
    parser.add_argument("--ip", "-i", required=True, help="TotalMix IP address")
    parser.add_argument("--send-port", "-s", type=int, default=7009, 
                        help="Port to SEND to TotalMix (TotalMix 'Incoming Port')")
    parser.add_argument("--receive-port", "-r", type=int, default=9009,
                        help="Port to RECEIVE from TotalMix (TotalMix 'Outgoing Port')")
    args = parser.parse_args()
    
    print("\n" + "="*60)
    print("TotalMix OSC Debug Tool")
    print("="*60)
    
    print(f"""
IMPORTANT: Check TotalMix OSC settings (Options → Settings → OSC tab):
  - 'Enable OSC Control' must be checked
  - 'Remote Controller Select': Set to 1
  - 'In Use': Must be checked
  - 'Incoming Port': This is where TotalMix LISTENS (your --send-port: {args.send_port})
  - 'Outgoing Port': This is where TotalMix SENDS feedback (your --receive-port: {args.receive_port})
  - 'Remote Controller IP': Can be left at 0.0.0.0 or set to your PC's IP
""")
    
    # Start listener in background thread
    listener_thread = threading.Thread(target=start_listener, args=(args.receive_port,), daemon=True)
    listener_thread.start()
    time.sleep(0.5)
    
    # Create sender
    client = udp_client.SimpleUDPClient(args.ip, args.send_port)
    print(f"✓ Sending to TotalMix at {args.ip}:{args.send_port}")
    
    print("\n" + "-"*60)
    print("Sending test commands... Watch TotalMix for changes!")
    print("-"*60 + "\n")
    
    # Test various OSC addresses
    test_commands = [
        ("/1/busOutput", 1.0, "Select output bus"),
        ("/1/volume1", 0.5, "Set fader 1 to 50%"),
        ("/1/volume2", 0.5, "Set fader 2 to 50%"),
        ("/1/volume3", 0.5, "Set fader 3 to 50%"),
        ("/1/volume4", 0.5, "Set fader 4 to 50%"),
        ("/1/mastervolume", 0.5, "Set master volume to 50%"),
        ("/1/mainDim", 1.0, "Toggle DIM (should be visible!)"),
    ]
    
    for address, value, description in test_commands:
        print(f"Sending: {address} = {value}  ({description})")
        client.send_message(address, float(value))
        time.sleep(0.5)
    
    print("\n" + "-"*60)
    print("Did you see any changes in TotalMix?")
    print("- If DIM turned on, OSC is working!")
    print("- If nothing happened, check:")
    print("  1. Firewall on TotalMix PC (allow UDP port " + str(args.send_port) + ")")
    print("  2. OSC settings in TotalMix")
    print("  3. Make sure ports aren't swapped (incoming vs outgoing)")
    print("-"*60)
    
    # Undo DIM
    time.sleep(1)
    print("\nUndoing DIM...")
    client.send_message("/1/mainDim", 0.0)
    
    print("\nListening for 5 more seconds for any feedback from TotalMix...")
    print("(Move a fader in TotalMix to see if we receive its value)")
    time.sleep(5)
    
    print("\nDone!")

if __name__ == "__main__":
    main()
