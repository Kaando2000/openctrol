#!/usr/bin/env python3
"""
Comprehensive test script for Openctrol Agent API
Run this from terminal to test without restarting Home Assistant

Usage:
    python test_openctrol_comprehensive.py --host 192.168.1.100 --port 44325 --api-key "your-key"
    
Or set environment variables:
    export OPENCTROL_HOST=192.168.1.100
    export OPENCTROL_PORT=44325
    export OPENCTROL_API_KEY=your-key
    python test_openctrol_comprehensive.py
"""

import asyncio
import aiohttp
import json
import sys
import os
import argparse
from typing import Dict, Any, Optional

class Colors:
    GREEN = '\033[92m'
    RED = '\033[91m'
    YELLOW = '\033[93m'
    BLUE = '\033[94m'
    CYAN = '\033[96m'
    GRAY = '\033[90m'
    RESET = '\033[0m'

class OpenctrolTester:
    def __init__(self, host: str, port: int, api_key: Optional[str] = None, use_ssl: bool = False):
        self.host = host
        self.port = port
        self.api_key = api_key
        self.use_ssl = use_ssl
        self.protocol = "https" if use_ssl else "http"
        self.base_url = f"{self.protocol}://{host}:{port}"
        self.session: Optional[aiohttp.ClientSession] = None
        
    def _get_headers(self) -> Dict[str, str]:
        headers = {}
        if self.api_key:
            headers["X-Openctrol-Key"] = self.api_key
        return headers
    
    async def __aenter__(self):
        self.session = aiohttp.ClientSession()
        return self
    
    async def __aexit__(self, exc_type, exc_val, exc_tb):
        if self.session:
            await self.session.close()
    
    async def test_health(self) -> bool:
        """Test health endpoint."""
        print(f"{Colors.YELLOW}1. Testing GET /api/v1/health...{Colors.RESET}")
        try:
            async with self.session.get(
                f"{self.base_url}/api/v1/health",
                timeout=aiohttp.ClientTimeout(total=10)
            ) as response:
                if response.status == 200:
                    data = await response.json()
                    print(f"   {Colors.GREEN}✓ Health check successful{Colors.RESET}")
                    print(f"   {Colors.GRAY}Agent ID: {data.get('agent_id', 'N/A')}{Colors.RESET}")
                    print(f"   {Colors.GRAY}Version: {data.get('version', 'N/A')}{Colors.RESET}")
                    print(f"   {Colors.GRAY}Uptime: {data.get('uptime_seconds', 0)} seconds{Colors.RESET}")
                    print(f"   {Colors.GRAY}Active Sessions: {data.get('active_sessions', 0)}{Colors.RESET}")
                    return True
                else:
                    error_text = await response.text()
                    print(f"   {Colors.RED}✗ Health check failed: {response.status} - {error_text}{Colors.RESET}")
                    return False
        except Exception as e:
            print(f"   {Colors.RED}✗ Health check failed: {e}{Colors.RESET}")
            return False
    
    async def test_monitors(self) -> bool:
        """Test monitors endpoint and verify all monitors are returned."""
        print(f"\n{Colors.YELLOW}2. Testing GET /api/v1/rd/monitors...{Colors.RESET}")
        try:
            headers = self._get_headers()
            async with self.session.get(
                f"{self.base_url}/api/v1/rd/monitors",
                headers=headers,
                timeout=aiohttp.ClientTimeout(total=10)
            ) as response:
                if response.status == 200:
                    data = await response.json()
                    monitors = data.get("monitors", [])
                    current_id = data.get("current_monitor_id", "")
                    
                    print(f"   {Colors.GREEN}✓ Monitors enumeration successful{Colors.RESET}")
                    print(f"   {Colors.GRAY}Current Monitor: {current_id}{Colors.RESET}")
                    print(f"   {Colors.GRAY}Available Monitors: {len(monitors)}{Colors.RESET}")
                    
                    if len(monitors) == 0:
                        print(f"   {Colors.RED}⚠ WARNING: No monitors found!{Colors.RESET}")
                        return False
                    
                    for i, monitor in enumerate(monitors, 1):
                        primary = " (PRIMARY)" if monitor.get("is_primary") else ""
                        monitor_id = monitor.get("id", "N/A")
                        name = monitor.get("name", "N/A")
                        resolution = monitor.get("resolution") or f"{monitor.get('width', 0)}x{monitor.get('height', 0)}"
                        print(f"   {Colors.GRAY}  {i}. {monitor_id}{primary}: {resolution} - {name}{Colors.RESET}")
                    
                    # Test monitor selection with first monitor
                    if monitors:
                        test_monitor_id = monitors[0].get("id")
                        print(f"\n   {Colors.CYAN}Testing monitor selection with: {test_monitor_id}{Colors.RESET}")
                        return await self.test_select_monitor(test_monitor_id)
                    
                    return True
                else:
                    error_text = await response.text()
                    print(f"   {Colors.RED}✗ Monitors enumeration failed: {response.status} - {error_text}{Colors.RESET}")
                    if response.status == 401:
                        print(f"   {Colors.YELLOW}(Authentication required - provide --api-key){Colors.RESET}")
                    return False
        except Exception as e:
            print(f"   {Colors.RED}✗ Monitors enumeration failed: {e}{Colors.RESET}")
            return False
    
    async def test_select_monitor(self, monitor_id: str) -> bool:
        """Test monitor selection with proper DeviceId format."""
        print(f"\n{Colors.YELLOW}3. Testing POST /api/v1/rd/monitor (MonitorId: {monitor_id})...{Colors.RESET}")
        try:
            headers = self._get_headers()
            headers["Content-Type"] = "application/json"
            
            # Test with MonitorId (capital M) - what backend expects
            payload = {"MonitorId": monitor_id}
            
            async with self.session.post(
                f"{self.base_url}/api/v1/rd/monitor",
                headers=headers,
                json=payload,
                timeout=aiohttp.ClientTimeout(total=10)
            ) as response:
                if response.status == 200:
                    data = await response.json()
                    print(f"   {Colors.GREEN}✓ Monitor selection successful{Colors.RESET}")
                    print(f"   {Colors.GRAY}Response: {json.dumps(data, indent=2)}{Colors.RESET}")
                    return True
                else:
                    error_text = await response.text()
                    print(f"   {Colors.RED}✗ Monitor selection failed: {response.status} - {error_text}{Colors.RESET}")
                    print(f"   {Colors.GRAY}Payload sent: {json.dumps(payload, indent=2)}{Colors.RESET}")
                    return False
        except Exception as e:
            print(f"   {Colors.RED}✗ Monitor selection failed: {e}{Colors.RESET}")
            return False
    
    async def test_audio_status(self) -> bool:
        """Test audio status endpoint."""
        print(f"\n{Colors.YELLOW}4. Testing GET /api/v1/audio/status...{Colors.RESET}")
        try:
            headers = self._get_headers()
            async with self.session.get(
                f"{self.base_url}/api/v1/audio/status",
                headers=headers,
                timeout=aiohttp.ClientTimeout(total=10)
            ) as response:
                if response.status == 200:
                    data = await response.json()
                    master = data.get("master", {})
                    devices = data.get("devices", [])
                    
                    print(f"   {Colors.GREEN}✓ Audio status retrieved{Colors.RESET}")
                    print(f"   {Colors.GRAY}Master Volume: {master.get('volume', 0)*100:.0f}%, Muted: {master.get('muted', False)}{Colors.RESET}")
                    print(f"   {Colors.GRAY}Devices: {len(devices)}{Colors.RESET}")
                    
                    for device in devices:
                        default = " (DEFAULT)" if device.get("isDefault") or device.get("is_default") or device.get("IsDefault") else ""
                        name = device.get("name", device.get("id", "N/A"))
                        volume = device.get("volume", 0)
                        if isinstance(volume, float) and volume <= 1.0:
                            volume = volume * 100
                        print(f"   {Colors.GRAY}  - {name}{default}: {volume:.0f}%, Muted: {device.get('muted', False)}{Colors.RESET}")
                    
                    # Test setting default device if we have devices
                    if devices:
                        test_device_id = devices[0].get("id")
                        print(f"\n   {Colors.CYAN}Testing set default device with: {test_device_id}{Colors.RESET}")
                        return await self.test_set_default_device(test_device_id)
                    
                    return True
                else:
                    error_text = await response.text()
                    print(f"   {Colors.RED}✗ Audio status failed: {response.status} - {error_text}{Colors.RESET}")
                    if response.status == 401:
                        print(f"   {Colors.YELLOW}(Authentication required - provide --api-key){Colors.RESET}")
                    return False
        except Exception as e:
            print(f"   {Colors.RED}✗ Audio status failed: {e}{Colors.RESET}")
            return False
    
    async def test_set_default_device(self, device_id: str) -> bool:
        """Test setting default audio device with proper DeviceId format."""
        print(f"\n{Colors.YELLOW}5. Testing POST /api/v1/audio/default (DeviceId: {device_id})...{Colors.RESET}")
        try:
            headers = self._get_headers()
            headers["Content-Type"] = "application/json"
            
            # Test with DeviceId (capital D) - what backend expects
            payload = {"DeviceId": device_id}
            
            async with self.session.post(
                f"{self.base_url}/api/v1/audio/default",
                headers=headers,
                json=payload,
                timeout=aiohttp.ClientTimeout(total=10)
            ) as response:
                if response.status == 200:
                    data = await response.json()
                    print(f"   {Colors.GREEN}✓ Set default device successful{Colors.RESET}")
                    print(f"   {Colors.GRAY}Response: {json.dumps(data, indent=2)}{Colors.RESET}")
                    return True
                else:
                    error_text = await response.text()
                    print(f"   {Colors.RED}✗ Set default device failed: {response.status} - {error_text}{Colors.RESET}")
                    print(f"   {Colors.GRAY}Payload sent: {json.dumps(payload, indent=2)}{Colors.RESET}")
                    print(f"   {Colors.YELLOW}⚠ NOTE: Backend expects 'DeviceId' (capital D), not 'device_id'{Colors.RESET}")
                    return False
        except Exception as e:
            print(f"   {Colors.RED}✗ Set default device failed: {e}{Colors.RESET}")
            return False
    
    async def test_websocket_connection(self) -> bool:
        """Test WebSocket connection and input events."""
        print(f"\n{Colors.YELLOW}6. Testing WebSocket connection...{Colors.RESET}")
        try:
            # First create a desktop session
            headers = self._get_headers()
            headers["Content-Type"] = "application/json"
            payload = {"ha_id": "test-script", "ttl_seconds": 60}
            
            async with self.session.post(
                f"{self.base_url}/api/v1/sessions/desktop",
                headers=headers,
                json=payload,
                timeout=aiohttp.ClientTimeout(total=10)
            ) as response:
                if response.status != 200:
                    error_text = await response.text()
                    print(f"   {Colors.RED}✗ Failed to create session: {response.status} - {error_text}{Colors.RESET}")
                    return False
                
                session_data = await response.json()
                websocket_url = session_data.get("websocket_url")
                session_id = session_data.get("session_id")
                
                if not websocket_url:
                    print(f"   {Colors.RED}✗ No websocket_url in session response{Colors.RESET}")
                    return False
                
                print(f"   {Colors.GREEN}✓ Session created: {session_id}{Colors.RESET}")
                print(f"   {Colors.GRAY}WebSocket URL: {websocket_url}{Colors.RESET}")
                
                # Connect to WebSocket
                print(f"   {Colors.CYAN}Connecting to WebSocket...{Colors.RESET}")
                async with self.session.ws_connect(
                    websocket_url,
                    timeout=aiohttp.ClientTimeout(total=30)
                ) as ws:
                    print(f"   {Colors.GREEN}✓ WebSocket connected{Colors.RESET}")
                    
                    # Wait for hello message
                    try:
                        hello = await asyncio.wait_for(ws.receive(), timeout=5.0)
                        if hello.type == aiohttp.WSMsgType.TEXT:
                            hello_data = json.loads(hello.data)
                            print(f"   {Colors.GRAY}Received hello: {hello_data.get('type', 'unknown')}{Colors.RESET}")
                            if hello_data.get("type") == "hello":
                                monitors = hello_data.get("monitors", [])
                                print(f"   {Colors.GRAY}Monitors in hello: {len(monitors)}{Colors.RESET}")
                    
                    except asyncio.TimeoutError:
                        print(f"   {Colors.YELLOW}⚠ No hello message received (may be normal){Colors.RESET}")
                    
                    # Test sending a pointer move event
                    print(f"   {Colors.CYAN}Testing pointer move event...{Colors.RESET}")
                    move_msg = {"type": "pointer_move", "dx": 10, "dy": 10}
                    await ws.send_str(json.dumps(move_msg))
                    print(f"   {Colors.GREEN}✓ Pointer move sent{Colors.RESET}")
                    
                    # Test sending a click event
                    print(f"   {Colors.CYAN}Testing pointer click event...{Colors.RESET}")
                    click_down = {"type": "pointer_button", "button": "left", "action": "down"}
                    click_up = {"type": "pointer_button", "button": "left", "action": "up"}
                    await ws.send_str(json.dumps(click_down))
                    await ws.send_str(json.dumps(click_up))
                    print(f"   {Colors.GREEN}✓ Pointer click sent{Colors.RESET}")
                    
                    # Test sending a key combo
                    print(f"   {Colors.CYAN}Testing key combo (CTRL+A)...{Colors.RESET}")
                    key_down = {"type": "key", "key_code": 0x11, "action": "down"}  # CTRL down
                    key_a_down = {"type": "key", "key_code": 0x41, "action": "down", "ctrl": True}  # A down with CTRL
                    key_a_up = {"type": "key", "key_code": 0x41, "action": "up", "ctrl": True}  # A up with CTRL
                    key_up = {"type": "key", "key_code": 0x11, "action": "up"}  # CTRL up
                    await ws.send_str(json.dumps(key_down))
                    await ws.send_str(json.dumps(key_a_down))
                    await ws.send_str(json.dumps(key_a_up))
                    await ws.send_str(json.dumps(key_up))
                    print(f"   {Colors.GREEN}✓ Key combo sent{Colors.RESET}")
                    
                    # Clean up session
                    await self.session.post(
                        f"{self.base_url}/api/v1/sessions/desktop/{session_id}/end",
                        headers=headers,
                        timeout=aiohttp.ClientTimeout(total=10)
                    )
                    print(f"   {Colors.GREEN}✓ Session cleaned up{Colors.RESET}")
                    
                    return True
        except Exception as e:
            print(f"   {Colors.RED}✗ WebSocket test failed: {e}{Colors.RESET}")
            import traceback
            print(f"   {Colors.GRAY}{traceback.format_exc()}{Colors.RESET}")
            return False
    
    async def run_all_tests(self):
        """Run all tests."""
        print(f"\n{Colors.CYAN}{'='*60}{Colors.RESET}")
        print(f"{Colors.CYAN}Openctrol Agent API Comprehensive Test{Colors.RESET}")
        print(f"{Colors.CYAN}{'='*60}{Colors.RESET}")
        print(f"{Colors.GRAY}Base URL: {self.base_url}{Colors.RESET}")
        if self.api_key:
            print(f"{Colors.GRAY}API Key: {'*' * (len(self.api_key) - 4) + self.api_key[-4:]}{Colors.RESET}")
        else:
            print(f"{Colors.YELLOW}⚠ No API key provided - some endpoints may fail{Colors.RESET}")
        print()
        
        results = []
        results.append(("Health Check", await self.test_health()))
        results.append(("Monitors", await self.test_monitors()))
        results.append(("Audio Status", await self.test_audio_status()))
        results.append(("WebSocket", await self.test_websocket_connection()))
        
        # Summary
        print(f"\n{Colors.CYAN}{'='*60}{Colors.RESET}")
        print(f"{Colors.CYAN}Test Summary{Colors.RESET}")
        print(f"{Colors.CYAN}{'='*60}{Colors.RESET}")
        
        passed = sum(1 for _, result in results if result)
        total = len(results)
        
        for test_name, result in results:
            status = f"{Colors.GREEN}✓ PASS{Colors.RESET}" if result else f"{Colors.RED}✗ FAIL{Colors.RESET}"
            print(f"  {status} {test_name}")
        
        print(f"\n{Colors.CYAN}Total: {passed}/{total} tests passed{Colors.RESET}")
        
        if passed == total:
            print(f"{Colors.GREEN}All tests passed! ✓{Colors.RESET}")
            return 0
        else:
            print(f"{Colors.RED}Some tests failed. Check output above.{Colors.RESET}")
            return 1

async def main():
    parser = argparse.ArgumentParser(description="Test Openctrol Agent API")
    parser.add_argument("--host", default=os.getenv("OPENCTROL_HOST", "localhost"),
                       help="Agent host (default: localhost or OPENCTROL_HOST env)")
    parser.add_argument("--port", type=int, default=int(os.getenv("OPENCTROL_PORT", "44325")),
                       help="Agent port (default: 44325 or OPENCTROL_PORT env)")
    parser.add_argument("--api-key", default=os.getenv("OPENCTROL_API_KEY", ""),
                       help="API key (default: OPENCTROL_API_KEY env)")
    parser.add_argument("--ssl", action="store_true",
                       help="Use HTTPS")
    
    args = parser.parse_args()
    
    async with OpenctrolTester(args.host, args.port, args.api_key or None, args.ssl) as tester:
        exit_code = await tester.run_all_tests()
        sys.exit(exit_code)

if __name__ == "__main__":
    asyncio.run(main())

