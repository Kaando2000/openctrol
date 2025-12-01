# Openctrol HTTP + WebSocket API Specification

This document describes the public API endpoints exposed by the Openctrol Agent for Home Assistant integration and other clients.

## Base URL

- HTTP: `http://<host>:<port>` (default port: 44325)
- HTTPS: `https://<host>:<port>` (when certificate is configured)

## Authentication

All endpoints except `/api/v1/health` require authentication via API key when configured:

- **Header**: `X-Openctrol-Key: <api-key>` OR
- **Header**: `Authorization: Bearer <api-key>`

If no API key is configured in `AgentConfig.ApiKey`, authentication is disabled (development mode only).

**Unauthenticated requests** return:
```json
{
  "error": "unauthorized",
  "details": "Missing or invalid credentials"
}
```
with HTTP status `401 Unauthorized`.

## Error Response Format

All error responses follow this format:
```json
{
  "error": "error_code",
  "details": "Human-readable error message"
}
```

Common HTTP status codes:
- `400 Bad Request`: Invalid request parameters
- `401 Unauthorized`: Missing or invalid API key
- `403 Forbidden`: Access denied (e.g., localhost-only endpoint)
- `404 Not Found`: Resource not found (e.g., invalid device/monitor ID)
- `500 Internal Server Error`: Server-side error
- `503 Service Unavailable`: Required service not available

---

## Endpoints

### 1. GET /api/v1/health

**Auth**: None (public health check)

**Description**: Returns agent health status and basic information.

**Response** (200 OK):
```json
{
  "agent_id": "guid-string",
  "version": "1.0.0",
  "uptime_seconds": 12345,
  "remote_desktop": {
    "is_running": true,
    "last_frame_at": "2024-01-01T12:00:00Z",
    "state": "desktop",
    "desktop_state": "desktop",
    "degraded": false
  },
  "active_sessions": 1
}
```

**Fields**:
- `agent_id`: Unique identifier for this agent instance
- `version`: Agent version string
- `uptime_seconds`: Seconds since agent started
- `remote_desktop.is_running`: Whether remote desktop capture is active
- `remote_desktop.last_frame_at`: ISO 8601 timestamp of last captured frame
- `remote_desktop.state`: Current desktop state string
- `remote_desktop.desktop_state`: Normalized desktop state ("desktop", "login_screen", "locked", "unknown")
- `remote_desktop.degraded`: Whether capture is in degraded mode
- `active_sessions`: Number of active desktop sessions

---

### 2. POST /api/v1/power

**Auth**: Required (API key)

**Description**: Execute power management actions (restart, shutdown).

**Request Body**:
```json
{
  "action": "restart" | "shutdown" | "wol",
  "force": false
}
```

**Fields**:
- `action` (required): Action to perform
  - `"restart"`: Restart the system
  - `"shutdown"`: Shutdown the system
  - `"wol"`: Wake-on-LAN (if supported, otherwise returns 400)
- `force` (optional, default: `false`): Force the action (ignored for now, reserved for future use)

**Response** (200 OK):
```json
{
  "status": "ok",
  "action": "restart"
}
```

**Error Responses**:
- `400 Bad Request`: Invalid action (e.g., "wol" not supported)
- `503 Service Unavailable`: Power manager not available

**Notes**:
- These actions are **destructive** and will immediately restart or shutdown the system
- The response may be sent before the action completes (system may restart/shutdown before HTTP response is fully sent)

---

### 3. GET /api/v1/audio/status

**Auth**: Required (API key)

**Description**: Get current audio state including master volume, devices, and sessions.

**Response** (200 OK):
```json
{
  "master": {
    "volume": 60,
    "muted": false
  },
  "devices": [
    {
      "id": "device-id-1",
      "name": "Speakers",
      "is_default": true,
      "volume": 60,
      "muted": false
    },
    {
      "id": "device-id-2",
      "name": "Headset",
      "is_default": false,
      "volume": 70,
      "muted": false
    }
  ]
}
```

**Fields**:
- `master.volume`: Master volume (0-100, percentage)
- `master.muted`: Master mute state
- `devices[]`: Array of audio output devices
  - `id`: Device identifier (use for device-specific operations)
  - `name`: Human-readable device name
  - `is_default`: Whether this is the default output device
  - `volume`: Device volume (0-100, percentage)
  - `muted`: Device mute state

**Note**: Master volume is derived from the default output device.

---

### 4. POST /api/v1/audio/master

**Auth**: Required (API key)

**Description**: Set master volume and/or mute state.

**Request Body**:
```json
{
  "volume": 60,
  "muted": false
}
```

**Fields**:
- `volume` (optional): Master volume (0-100, percentage). If omitted, volume is unchanged.
- `muted` (optional): Mute state (boolean). If omitted, mute state is unchanged.

**Response** (200 OK):
```json
{
  "status": "ok"
}
```

**Error Responses**:
- `400 Bad Request`: Invalid volume range
- `503 Service Unavailable`: Audio manager not available

---

### 5. POST /api/v1/audio/device

**Auth**: Required (API key)

**Description**: Set volume and/or mute state for a specific audio device.

**Request Body**:
```json
{
  "device_id": "device-id-1",
  "volume": 70,
  "muted": false
}
```

**Fields**:
- `device_id` (required): Device identifier from `/api/v1/audio/status`
- `volume` (optional): Device volume (0-100, percentage). If omitted, volume is unchanged.
- `muted` (optional): Mute state (boolean). If omitted, mute state is unchanged.

**Response** (200 OK):
```json
{
  "status": "ok"
}
```

**Error Responses**:
- `400 Bad Request`: Invalid device_id or volume range
- `404 Not Found`: Device ID not found
- `503 Service Unavailable`: Audio manager not available

---

### 6. POST /api/v1/audio/default

**Auth**: Required (API key)

**Description**: Set the default audio output device.

**Request Body**:
```json
{
  "device_id": "device-id-2"
}
```

**Fields**:
- `device_id` (required): Device identifier from `/api/v1/audio/status`

**Response** (200 OK):
```json
{
  "status": "ok"
}
```

**Error Responses**:
- `400 Bad Request`: Invalid device_id
- `404 Not Found`: Device ID not found
- `501 Not Implemented`: Setting default device not supported on this system
- `503 Service Unavailable`: Audio manager not available

---

### 7. GET /api/v1/rd/monitors

**Auth**: Required (API key)

**Description**: Enumerate available monitors and get current monitor selection.

**Response** (200 OK):
```json
{
  "monitors": [
    {
      "id": "DISPLAY1",
      "name": "\\\\.\\DISPLAY1",
      "resolution": "2560x1440",
      "width": 2560,
      "height": 1440,
      "is_primary": true
    },
    {
      "id": "DISPLAY2",
      "name": "\\\\.\\DISPLAY2",
      "resolution": "1920x1080",
      "width": 1920,
      "height": 1080,
      "is_primary": false
    }
  ],
  "current_monitor_id": "DISPLAY1"
}
```

**Fields**:
- `monitors[]`: Array of monitor information
  - `id`: Monitor identifier (use for monitor selection)
  - `name`: System monitor name
  - `resolution`: Resolution string (e.g., "2560x1440")
  - `width`: Monitor width in pixels
  - `height`: Monitor height in pixels
  - `is_primary`: Whether this is the primary monitor
- `current_monitor_id`: Currently selected monitor for remote desktop capture

**Error Responses**:
- `503 Service Unavailable`: Remote desktop engine not available

---

### 8. POST /api/v1/rd/monitor

**Auth**: Required (API key)

**Description**: Select which monitor to capture for remote desktop.

**Request Body**:
```json
{
  "monitor_id": "DISPLAY2"
}
```

**Fields**:
- `monitor_id` (required): Monitor identifier from `/api/v1/rd/monitors`

**Response** (200 OK):
```json
{
  "status": "ok",
  "monitor_id": "DISPLAY2"
}
```

**Error Responses**:
- `400 Bad Request`: Invalid monitor_id format
- `404 Not Found`: Monitor ID not found
- `503 Service Unavailable`: Remote desktop engine not available

**Notes**:
- Changing the monitor will affect which monitor is captured for remote desktop sessions
- Input coordinates are relative to the selected monitor

---

### 9. WebSocket /api/v1/rd/session

**Auth**: Required (API key via query parameter or header)

**Description**: WebSocket endpoint for sending pointer and keyboard input events to the agent.

**Connection**:
- **URL**: `ws://<host>:<port>/api/v1/rd/session` or `wss://<host>:<port>/api/v1/rd/session`
- **Authentication**: API key must be provided via:
  - Query parameter: `?api_key=<key>` OR
  - Header: `X-Openctrol-Key: <key>` (during WebSocket upgrade)

**Message Format**: All messages are JSON text frames.

#### Pointer Events

**Move (relative)**:
```json
{
  "type": "pointer",
  "event": "move",
  "dx": 10.5,
  "dy": -5.2
}
```

**Click**:
```json
{
  "type": "pointer",
  "event": "click",
  "button": "left" | "right" | "middle"
}
```

**Scroll**:
```json
{
  "type": "pointer",
  "event": "scroll",
  "dx": 0.0,
  "dy": 120.0
}
```

**Fields**:
- `type`: Always `"pointer"` for pointer events
- `event`: Event type (`"move"`, `"click"`, `"scroll"`)
- `dx`, `dy`: Relative movement or scroll delta (float, pixels)
- `button`: Mouse button (`"left"`, `"right"`, `"middle"`) - required for click events

#### Keyboard Events

**Key Combination**:
```json
{
  "type": "keyboard",
  "keys": ["CTRL", "ALT", "DEL"]
}
```

**Fields**:
- `type`: Always `"keyboard"` for keyboard events
- `keys`: Array of key names to press simultaneously, then release

**Supported Key Names**:
- Modifiers: `"CTRL"`, `"ALT"`, `"SHIFT"`, `"WIN"`
- Special keys: `"TAB"`, `"ESC"`, `"ENTER"`, `"SPACE"`, `"DEL"`, `"BACKSPACE"`
- Function keys: `"F1"` through `"F12"`
- Arrow keys: `"UP"`, `"DOWN"`, `"LEFT"`, `"RIGHT"`
- Other common keys: `"HOME"`, `"END"`, `"PAGEUP"`, `"PAGEDOWN"`, `"INSERT"`

**Behavior**:
- All keys in the array are pressed simultaneously, then all are released
- Order in array does not matter for modifiers
- Single key presses: `["A"]` or `["ENTER"]`

#### Error Messages

If the server receives an invalid message, it may send an error frame:
```json
{
  "type": "error",
  "message": "Invalid message format"
}
```

**Connection Lifecycle**:
- Client opens WebSocket connection
- Client sends input events as needed
- Server may close connection on error or timeout
- Client should handle reconnection if needed

**Notes**:
- Input coordinates are relative to the currently selected monitor (see `/api/v1/rd/monitor`)
- The WebSocket does not currently send video frames (that's handled by `/ws/desktop` with session tokens)
- This endpoint is designed for Home Assistant remote control card input

---

## Implementation Notes

### Volume Representation

- All volume values in API responses are **0-100 (percentage)**
- Internal audio system uses **0.0-1.0 (float)**
- Conversion: API percentage = internal float × 100

### Monitor Selection

- Monitor IDs follow the pattern `DISPLAY1`, `DISPLAY2`, etc.
- The primary monitor is typically `DISPLAY1` but not guaranteed
- Changing monitor selection affects:
  - Remote desktop capture source
  - Input coordinate interpretation (relative to selected monitor)

### Security

- All authenticated endpoints require valid API key
- WebSocket connections must authenticate during upgrade
- API keys should be strong, random strings
- Use HTTPS in production to encrypt API key transmission

### Rate Limiting

- No explicit rate limiting is implemented
- Clients should be reasonable (e.g., don't send 1000 pointer events per second)
- Server may close WebSocket connections if overwhelmed

---

## Example Usage

### PowerShell: Get Health
```powershell
$response = Invoke-RestMethod -Uri "http://localhost:44325/api/v1/health" -Method Get
$response | ConvertTo-Json
```

### PowerShell: Set Master Volume
```powershell
$body = @{
    volume = 75
    muted = $false
} | ConvertTo-Json

$headers = @{
    "X-Openctrol-Key" = "your-api-key"
    "Content-Type" = "application/json"
}

Invoke-RestMethod -Uri "http://localhost:44325/api/v1/audio/master" -Method Post -Body $body -Headers $headers
```

### curl: Get Monitors
```bash
curl -H "X-Openctrol-Key: your-api-key" \
     http://localhost:44325/api/v1/rd/monitors
```

### JavaScript: WebSocket Input
```javascript
const ws = new WebSocket('ws://localhost:44325/api/v1/rd/session?api_key=your-api-key');

ws.onopen = () => {
  // Send pointer move
  ws.send(JSON.stringify({
    type: "pointer",
    event: "move",
    dx: 10,
    dy: 5
  }));
  
  // Send click
  ws.send(JSON.stringify({
    type: "pointer",
    event: "click",
    button: "left"
  }));
  
  // Send keyboard combo
  ws.send(JSON.stringify({
    type: "keyboard",
    keys: ["CTRL", "ALT", "DEL"]
  }));
};
```

