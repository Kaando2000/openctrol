# Openctrol API Test Tools

These scripts allow you to test the Openctrol Agent API **without restarting Home Assistant**. This is much faster for development and debugging.

## Prerequisites

- Python 3.7+ with `aiohttp` installed
- Access to the Openctrol Agent (IP address and optional API key)

## Installation

```bash
pip install aiohttp
```

## Usage

### Comprehensive Test Script

Tests all API endpoints including health, monitors, audio, and WebSocket:

```bash
# With command-line arguments
python test_openctrol_comprehensive.py --host 192.168.1.100 --port 44325 --api-key "your-api-key"

# With environment variables (Windows PowerShell)
$env:OPENCTROL_HOST="192.168.1.100"
$env:OPENCTROL_PORT="44325"
$env:OPENCTROL_API_KEY="your-api-key"
python test_openctrol_comprehensive.py

# With environment variables (Linux/Mac)
export OPENCTROL_HOST=192.168.1.100
export OPENCTROL_PORT=44325
export OPENCTROL_API_KEY=your-api-key
python test_openctrol_comprehensive.py

# Without API key (if agent doesn't require authentication)
python test_openctrol_comprehensive.py --host 192.168.1.100 --port 44325
```

### Finding Your Agent IP and API Key

1. **Agent IP**: The IP address of the Windows PC running the Openctrol Agent
   - Check your router's DHCP client list
   - Or run `ipconfig` on the Windows PC
   - Or check Home Assistant config: `.homeassistant/config/.storage/core.config_entries`

2. **API Key**: 
   - Check the agent config: `C:\ProgramData\Openctrol\config.json` on the Windows PC
   - Or check Home Assistant config entry (if already configured)

3. **Port**: Default is `44325` (can be changed in agent config)

## What Gets Tested

1. **Health Check** - Verifies agent is running
2. **Monitors** - Lists all monitors and tests selection
3. **Audio Status** - Lists audio devices and tests default device selection
4. **WebSocket** - Tests connection and input events (pointer, keyboard)

## Example Output

```
============================================================
Openctrol Agent API Comprehensive Test
============================================================
Base URL: http://192.168.1.100:44325
API Key: ****************abcd

1. Testing GET /api/v1/health...
   ✓ Health check successful
   Agent ID: abc123...
   Version: 1.0.0
   Uptime: 12345 seconds
   Active Sessions: 1

2. Testing GET /api/v1/rd/monitors...
   ✓ Monitors enumeration successful
   Current Monitor: \\.\DISPLAY1
   Available Monitors: 3
     1. \\.\DISPLAY1 (PRIMARY): 1920x1080 - Generic PnP Monitor
     2. \\.\DISPLAY2: 1920x1080 - Generic PnP Monitor
     3. \\.\DISPLAY3: 2560x1440 - Generic PnP Monitor

3. Testing POST /api/v1/rd/monitor (MonitorId: \\.\DISPLAY1)...
   ✓ Monitor selection successful

4. Testing GET /api/v1/audio/status...
   ✓ Audio status retrieved
   Master Volume: 75%, Muted: False
   Devices: 2
     - Speakers (Realtek): 75%, Muted: False (DEFAULT)
     - Headphones (USB): 50%, Muted: False

5. Testing POST /api/v1/audio/default (DeviceId: {device-id})...
   ✓ Set default device successful

6. Testing WebSocket connection...
   ✓ Session created: session-123
   ✓ WebSocket connected
   ✓ Pointer move sent
   ✓ Pointer click sent
   ✓ Key combo sent
   ✓ Session cleaned up

============================================================
Test Summary
============================================================
  ✓ PASS Health Check
  ✓ PASS Monitors
  ✓ PASS Audio Status
  ✓ PASS WebSocket

Total: 4/4 tests passed
All tests passed! ✓
```

## Troubleshooting

- **Connection refused**: Check agent IP and port, ensure agent service is running
- **401 Unauthorized**: Provide correct API key with `--api-key` or `OPENCTROL_API_KEY` env var
- **No monitors found**: Check Windows display settings, ensure monitors are connected
- **WebSocket fails**: Check firewall rules, ensure port is accessible

## Benefits Over Home Assistant Testing

- ✅ **No restarts needed** - Test immediately after code changes
- ✅ **Faster feedback** - See results in seconds
- ✅ **Better debugging** - Direct API testing without HA abstraction
- ✅ **Isolated testing** - Test API independently of HA integration
- ✅ **CI/CD ready** - Can be automated in build pipelines

