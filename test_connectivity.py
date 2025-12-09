#!/usr/bin/env python3
"""
Simple connectivity test for TotalMix OSC.
This will help verify your network and OSC settings are correct.
"""

import socket
import argparse
import time

def test_udp_connectivity(target_ip: str, target_port: int) -> bool:
    """Test if we can send UDP packets to the target."""
    print(f"\n1. Testing UDP send to {target_ip}:{target_port}...")
    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.settimeout(2)
        # Send a simple OSC message (ping-like)
        # OSC format: address (null-terminated, padded to 4 bytes) + type tag + value
        message = b'/ping\x00\x00\x00,\x00\x00\x00'  # Simple OSC message
        sock.sendto(message, (target_ip, target_port))
        print(f"   ✓ UDP packet sent successfully (but no guarantee it arrived!)")
        sock.close()
        return True
    except Exception as e:
        print(f"   ✗ Failed to send: {e}")
        return False

def test_port_reachable(target_ip: str, target_port: int) -> bool:
    """Try to detect if the port might be open (TCP check, limited for UDP)."""
    print(f"\n2. Testing if {target_ip} is reachable (ICMP/ping)...")
    import subprocess
    try:
        # Windows ping
        result = subprocess.run(
            ['ping', '-n', '1', '-w', '1000', target_ip],
            capture_output=True, text=True, timeout=3
        )
        if result.returncode == 0:
            print(f"   ✓ Host {target_ip} is reachable!")
            return True
        else:
            print(f"   ✗ Host {target_ip} is NOT reachable (ping failed)")
            print(f"     Check: Are both computers on the same network?")
            return False
    except Exception as e:
        print(f"   ? Could not ping: {e}")
        return False

def get_local_ip() -> str:
    """Get this computer's local IP address."""
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(("8.8.8.8", 80))  # Doesn't actually send anything
        ip = s.getsockname()[0]
        s.close()
        return ip
    except:
        return "Unknown"

def main():
    parser = argparse.ArgumentParser(description="Test connectivity to TotalMix")
    parser.add_argument("--ip", "-i", required=True, help="TotalMix computer IP")
    parser.add_argument("--port", "-p", type=int, default=7009, help="OSC port")
    args = parser.parse_args()
    
    local_ip = get_local_ip()
    
    print("="*60)
    print("TotalMix OSC Connectivity Test")
    print("="*60)
    print(f"\nYour computer's IP: {local_ip}")
    print(f"Target TotalMix IP: {args.ip}")
    print(f"Target OSC Port:    {args.port}")
    
    # Test 1: Ping the host
    ping_ok = test_port_reachable(args.ip, args.port)
    
    # Test 2: Try to send UDP
    udp_ok = test_udp_connectivity(args.ip, args.port)
    
    print("\n" + "="*60)
    print("REQUIRED TOTALMIX SETTINGS")
    print("="*60)
    print(f"""
On the TotalMix computer, go to Options → Settings → OSC tab:

  1. ☐ Enable OSC Control (Options menu) - MUST be checked
  
  2. Remote Controller Select: 1
  
  3. ☐ In Use - MUST be checked
  
  4. Port incoming: {args.port}
     (This is where TotalMix LISTENS for commands)
  
  5. Port outgoing: 9009 (or any port for feedback)
     (This is where TotalMix SENDS status updates)
  
  6. IP or Host Name: {local_ip}
     ^^^ THIS IS CRITICAL! ^^^
     This must be YOUR computer's IP address so TotalMix
     knows to accept commands from you!

Also: Make sure Windows Firewall on the TotalMix PC allows
      UDP traffic on port {args.port}
""")
    
    print("="*60)
    if ping_ok and udp_ok:
        print("Network looks OK - check TotalMix OSC settings above!")
    else:
        print("Network issue detected - fix connectivity first!")
    print("="*60)

if __name__ == "__main__":
    main()
