# Openctrol Agent API Documentation

## Overview

The Openctrol Agent exposes a REST API and WebSocket endpoint for remote desktop control, power management, and audio control.

## Base URL

- HTTP: `http://<agent-ip>:44325`
- HTTPS: `https://<agent-ip>:44325` (when certificate is configured)

## REST API Endpoints

### Health Check

**GET** `/api/v1/health`

Returns the health status of the agent.

**Response:**
```json
{
  "agent_id": "guid-string",
  "uptime_seconds": 12345,
  "remote_desktop": {
    "is_running": true,
    "last_frame_at": "2024-01-01T12:00:00Z",
    "state": "desktop"
  },
  "active_sessions": 1
}
```

### Create Desktop Session

**POST** `/api/v1/sessions/desktop`

Creates a new desktop session and returns a WebSocket URL for connection.

**Request:**
```json
{
  "ha_id": "home-assistant-id",
  "ttl_seconds": 900
}
```

**Response:**
```json
{
  "session_id": "guid-string",
  "websocket_url": "ws://<agent-ip>:44325/ws/desktop?sess=<session-id>&token=<token>",
  "expires_at": "2024-01-01T12:15:00Z"
}
```

### End Desktop Session

**POST** `/api/v1/sessions/desktop/{session-id}/end`

Ends an active desktop session.

**Response:** 200 OK

### Power Control

**POST** `/api/v1/power`

Controls system power (restart or shutdown).

**Request:**
```json
{
  "action": "restart"  // or "shutdown"
}
```

**Response:** 200 OK

### Audio State

**GET** `/api/v1/audio/state`

Returns the current audio state including devices and sessions.

**Response:**
```json
{
  "default_output_device_id": "device-id",
  "devices": [
    {
      "id": "device-id",
      "name": "Device Name",
      "volume": 0.75,
      "muted": false,
      "is_default": true
    }
  ],
  "sessions": [
    {
      "id": "session-id",
      "name": "Application Name",
      "volume": 0.5,
      "muted": false,
      "output_device_id": "device-id"
    }
  ]
}
```

### Set Device Volume

**POST** `/api/v1/audio/device`

Sets the volume and mute state for an audio device.

**Request:**
```json
{
  "device_id": "device-id",
  "volume": 0.75,
  "muted": false,
  "set_default": false
}
```

**Fields:**
- `device_id` (required): The ID of the audio device
- `volume` (required): Volume level between 0.0 and 1.0
- `muted` (required): Whether the device is muted
- `set_default` (optional): If `true`, also sets this device as the default output device

**Response:** 200 OK

### Set Session Volume

**POST** `/api/v1/audio/session`

Sets the volume and mute state for an audio session.

**Request:**
```json
{
  "session_id": "session-id",
  "volume": 0.5,
  "muted": false,
  "output_device_id": "device-id"
}
```

**Fields:**
- `session_id` (required): The ID of the audio session (application)
- `volume` (required): Volume level between 0.0 and 1.0
- `muted` (required): Whether the session is muted
- `output_device_id` (optional): If provided, routes this session to the specified output device

**Response:** 200 OK

## WebSocket Protocol

### Connection

Connect to: `/ws/desktop?sess=<session-id>&token=<token>`

The session ID and token are obtained from the Create Desktop Session endpoint.

### Hello Message

Upon successful connection, the server sends a hello message:

```json
{
  "type": "hello",
  "agent_id": "guid-string",
  "session_id": "guid-string",
  "version": "1.0",
  "monitors": [
    {
      "id": "DISPLAY1",
      "name": "Primary Display",
      "width": 1920,
      "height": 1080,
      "is_primary": true
    }
  ]
}
```

### Frame Format

Frames are sent as binary WebSocket messages with the following format:

- **Header (16 bytes):**
  - Bytes 0-3: ASCII "OFRA" (Openctrol Frame)
  - Bytes 4-7: int32 width
  - Bytes 8-11: int32 height
  - Bytes 12-15: int32 format (1 = JPEG)
- **Payload:** JPEG-encoded image data

### Input Messages

Send JSON messages to control input:

#### Pointer Move (Relative)
```json
{
  "type": "pointer_move",
  "dx": 10,
  "dy": -5
}
```

#### Pointer Move (Absolute)
```json
{
  "type": "pointer_move",
  "absolute": true,
  "x": 100,
  "y": 200
}
```

#### Pointer Button
```json
{
  "type": "pointer_button",
  "button": "left",  // "left", "right", or "middle"
  "action": "down"   // "down" or "up"
}
```

#### Pointer Wheel
```json
{
  "type": "pointer_wheel",
  "delta_x": 0,
  "delta_y": 120
}
```

#### Key
```json
{
  "type": "key",
  "key_code": 65,  // Virtual key code
  "action": "down", // "down" or "up"
  "ctrl": false,
  "alt": false,
  "shift": false,
  "win": false
}
```

#### Text
```json
{
  "type": "text",
  "text": "Hello",
  "ctrl": false,
  "alt": false,
  "shift": false,
  "win": false
}
```

#### Monitor Select
```json
{
  "type": "monitor_select",
  "monitor_id": "DISPLAY2"
}
```

#### Quality (Not Implemented in v1)
```json
{
  "type": "quality",
  "quality": 80
}
```
**Note:** Quality setting messages are silently ignored in v1.0. This feature may be implemented in future versions.

## Error Responses

All endpoints return standardized JSON error responses on failure:

**Error Response Format:**
```json
{
  "error": "error_code",
  "details": "Human readable error message"
}
```

**HTTP Status Codes:**
- `400 Bad Request` - Invalid request format or parameters
- `401 Unauthorized` - Invalid or expired token, or HA ID not allowed
- `404 Not Found` - Resource not found (e.g., session ID not found)
- `500 Internal Server Error` - Server error (e.g., audio operation failed)
- `503 Service Unavailable` - Service not available (stub or service not initialized)

**Example Error Response:**
```json
{
  "error": "invalid_audio_device_request",
  "details": "Device ID is required"
}
```

## Security

- Session tokens expire after the TTL specified during session creation
- Token validation failures are rate-limited (5 failures per minute per client)
- HA IDs can be restricted via the `AllowedHaIds` configuration
- HTTPS is supported when a certificate is configured

