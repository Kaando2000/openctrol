# Troubleshooting Guide

## Current Issues and Status

### 1. Monitor Enumeration Issue ⚠️ **Agent-Side Problem**

**Problem**: Agent only reports 1 monitor when there are 2 displays active.

**Root Cause**: The agent uses `System.Windows.Forms.Screen.AllScreens` which may not work correctly when running as a Windows service. Windows services run in Session 0 and may not have access to user display sessions.

**Evidence**: Agent logs show `[API] Monitors enumeration returned 1 monitors` even though Windows shows 2 displays.

**Solution**: This needs to be fixed in the agent's C# code (`RemoteDesktopEngine.cs`). The agent should use a different API to enumerate monitors that works in service context, such as:
- `EnumDisplayMonitors` Win32 API
- WMI queries for display devices
- DirectX/DirectShow enumeration

**Integration Status**: The Home Assistant integration correctly handles whatever monitors the agent reports. The issue is in the agent's monitor enumeration logic.

### 2. Monitor Button Not Working ✅ **Fixed with Better Logging**

**Problem**: Monitor button click doesn't trigger any action.

**Fix Applied**:
- Added explicit `preventDefault()` and `stopPropagation()` to button click handler
- Added comprehensive console logging to track click events
- Added error handling and user feedback

**Testing**: Check browser console for:
- `"Monitor button clicked: <monitor-id>"`
- `"_handleSelectMonitor called with: <monitor-id>"`
- `"Selecting monitor: <monitor-id>"`
- Any error messages

### 3. Keyboard Keys Not Working ✅ **Fixed with Better Logging**

**Problem**: Keyboard keys don't work and no errors are logged.

**Fix Applied**:
- Added comprehensive logging at multiple levels:
  - Service handler logs when key combo is received
  - WebSocket client logs when messages are sent
  - Logs include endpoint type (deprecated vs session-based)
  - Logs include actual JSON messages being sent
- Fixed WebSocket message format to match endpoint type:
  - Deprecated endpoint: `{"type": "keyboard", "keys": ["CTRL", "A"]}`
  - Session-based endpoint: `{"type": "key", "key_code": 65, "action": "down"}`

**Testing**: Check Home Assistant logs for:
- `"Received send_key_combo service call with keys: [...]"`
- `"Calling async_send_key_combo with keys: [...]"`
- `"Sent key combo (deprecated/session format): [...]"`
- `"Successfully sent key combo: [...]"`

**If Still Not Working**:
1. Check if WebSocket connection is established (look for "WebSocket connected successfully")
2. Check agent logs for "Unknown WebSocket message type" errors
3. Verify the endpoint type being used (deprecated vs session-based)

### 4. Audio Default Device Error ⚠️ **Agent-Side Problem**

**Problem**: `System.InvalidCastException` when setting default audio device.

**Root Cause**: COM interface casting issue in agent's `AudioManager.cs`. The `PolicyConfigClient` COM object doesn't support the `IPolicyConfig` interface on this system.

**Error**: `Unable to cast COM object of type 'PolicyConfigClient' to interface type 'IPolicyConfig'`

**Solution**: This needs to be fixed in the agent's C# code. Possible solutions:
- Use a different COM interface for setting default audio device
- Use Windows Core Audio API directly
- Use PowerShell/Command-line tools as fallback

**Integration Status**: The integration correctly reports the error to the user. The error is handled gracefully with an alert message.

## Debugging Steps

### Check Home Assistant Logs

```powershell
cd docker
docker compose logs homeassistant --tail 100 | Select-String -Pattern "openctrol|key|monitor|WebSocket"
```

### Check Agent Logs

Agent logs are stored in:
- Event Viewer: `Application` log, source `OpenctrolAgent`
- File logs: `C:\ProgramData\Openctrol\logs\agent-YYYY-MM-DD.log`

### Check Browser Console

1. Open Home Assistant in browser
2. Press F12 to open Developer Tools
3. Go to Console tab
4. Look for:
   - `"Monitor button clicked: ..."`
   - `"Sending key combo: ..."`
   - `"Sending pointer event: ..."`
   - Any error messages

### Test WebSocket Connection

The integration automatically creates/reuses WebSocket sessions. Check logs for:
- `"WebSocket connected successfully"`
- `"Connecting to WebSocket: ..."`
- `"Reusing existing desktop session for input: ..."`

## Next Steps

1. **Monitor Enumeration**: Fix agent's monitor enumeration to use Win32 API instead of Windows Forms
2. **Keyboard Events**: Monitor logs to see if WebSocket messages are being sent correctly
3. **Audio Error**: Fix agent's audio COM interface usage
4. **Testing**: Test all functionality after fixes are applied

## Integration Code Changes Made

### `ws.py`
- Added endpoint type detection (`_is_deprecated_endpoint`)
- Fixed message format to match endpoint type
- Added comprehensive logging for keyboard events
- Improved session reuse logic

### `__init__.py`
- Added logging to `send_key_combo` service handler
- Improved error messages

### `openctrol-card.js`
- Added logging to monitor button click handler
- Added `preventDefault()` and `stopPropagation()` to prevent event bubbling
- Improved error handling and user feedback

