# Openctrol Card Testing Guide

This guide walks through testing the Openctrol custom Lovelace card to ensure all features work correctly.

## Prerequisites

1. **Home Assistant** running with Openctrol integration installed
2. **Openctrol Agent** running on Windows
3. **Browser** with developer tools access
4. **Windows PowerShell** for log monitoring

## 1. Baseline: Entity + Services Present

### Check Entity State

1. In Home Assistant, go to **Developer Tools → States**
2. Find your sensor entity: `sensor.openctrol_*_status` (e.g., `sensor.openctrol_pc_status`)
3. Verify it exists and check its attributes:
   - `agent_id`
   - `version`
   - `uptime_seconds`
   - `active_sessions`
   - `remote_desktop_is_running`
   - `remote_desktop_degraded`
   - `remote_desktop_state`
   - `remote_desktop_desktop_state`
   - `remote_desktop_last_frame_at`

### Check Services

1. Go to **Developer Tools → Services**
2. Verify these services exist:
   - `openctrol.send_pointer_event`
   - `openctrol.send_key_combo`
   - `openctrol.power_action`
   - `openctrol.select_monitor`
   - `openctrol.set_master_volume`
   - `openctrol.set_device_volume`
   - `openctrol.set_default_output_device`

✅ **If all services are visible, backend + HA integration are "wired."**

## 2. Card Wiring: Entity/Service Communication

### Add the Card

1. Edit your Lovelace dashboard
2. Add a card with this configuration:

```yaml
type: custom:openctrol-card
entity: sensor.openctrol_pc_status
title: "Test PC"
```

3. Save and view the dashboard

### Quick Functional Sanity Check

1. **Status Pill**: Should show **ONLINE** (green) when the status sensor is online
2. **Stop the Windows service**: 
   ```powershell
   Stop-Service OpenctrolAgent
   ```
3. **Wait ~30 seconds** for coordinator refresh
4. **Status pill** should change to **OFFLINE** (grey)
5. **Watch HA logs** (Settings → System → Logs) for any errors when interacting with the card

✅ **Card should render and status should update correctly**

## 3. Touchpad → Pointer Events

### Setup

**On HA side:**
- Open browser **Developer Tools → Console** to see errors
- Optionally open **Developer Tools → Network** to watch WebSocket messages

**On Windows side:**
- Open PowerShell and tail logs:
  ```powershell
  Get-Content "C:\ProgramData\Openctrol\logs\agent-*.log" -Tail 100 -Wait
  ```

### Test Pointer Movement

1. **Drag finger** across the touchpad
2. **Expected results:**
   - Mouse cursor moves on Windows PC
   - Openctrol logs show pointer events (if logged)
   - No errors in browser console

### Test Clicks

1. **Single tap** on touchpad → Should trigger left click
2. **Tap "Left Click" button** → Should trigger left click
3. **Tap "Right Click" button** → Should trigger right click
4. **Verify on Windows**: Click a desktop icon or open a context menu

### Test Scroll

1. **Enable "Scroll Mode"** toggle button
2. **Drag finger** on touchpad
3. **Expected**: Scroll events sent instead of move events
4. **Or use two-finger drag** (even with scroll mode off)
5. **Verify on Windows**: Scroll a long list or webpage

✅ **All pointer interactions should work smoothly**

## 4. Keyboard Panel: Toggle/Hold Modifiers

### Test Modifier Latching

1. **Open keyboard panel** (⌨ icon)
2. **Short tap CTRL**:
   - Should send combo with CTRL (may not have visible effect)
   - Button should NOT latch
3. **Hold CTRL** (>500ms):
   - Button should **visually latch** (highlighted/colored)
   - No key combo sent yet
4. **While CTRL is latched, tap a key** (e.g., C or arrow key):
   - Should send combo: `["CTRL", "C"]` or `["CTRL", "UP"]`
   - On Windows: If text is selected, should copy or move cursor
5. **Tap CTRL again** (short tap):
   - Latched state should **clear** (button returns to normal)
   - Next key tap should NOT include CTRL

### Test Multiple Modifiers

1. **Latch SHIFT** (hold >500ms)
2. **Latch CTRL** (hold >500ms)
3. **Tap arrow key**:
   - Should send: `["SHIFT", "CTRL", "UP"]` (or similar)
   - On Windows: Should select text with shift+ctrl+arrow

### Test Shortcuts

1. **Tap "Win + D"**:
   - Should send: `["WIN", "D"]`
   - On Windows: Should show desktop
2. **Tap "Win + L"** (careful - this locks the PC):
   - Should send: `["WIN", "L"]`
   - On Windows: Should lock screen
3. **Tap "Alt + F4"**:
   - Should send: `["ALT", "F4"]`
   - On Windows: Should close focused window
4. **Verify**: Shortcuts work **independently** of latched modifiers
5. **Verify**: Latched state does NOT change after using shortcuts

✅ **Modifier latching and shortcuts should work correctly**

## 5. Power Menu

⚠️ **Be deliberate here - these actions affect the system**

### Test Restart

1. **Open Power panel** (⚡ icon)
2. **Tap "Restart"**:
   - Should show **confirmation dialog**
3. **Tap "Cancel"**:
   - Dialog should close
   - No action taken
4. **Tap "Restart" again**:
   - Confirm dialog appears
5. **Tap "Restart" in dialog**:
   - Card → HA service: `openctrol.power_action(action: "restart")`
   - Openctrol logs should show power action
   - **Machine should restart**
6. **After reboot**:
   - Agent service should come back automatically
   - Status sensor should return to ONLINE
   - Card should still work

### Test Shutdown

1. **Tap "Shutdown"**:
   - Confirm dialog appears
2. **Cancel once** to verify confirmation works
3. **When ready, confirm Shutdown**:
   - Machine should shut down
   - Status sensor will go offline

### Test Wake on LAN

1. **Shutdown the machine** (via card or Windows)
2. **From another device**, tap "Wake on LAN" in the card
3. **Verify**: Machine wakes up (if WOL is configured)

✅ **Power actions should work with proper confirmation**

## 6. Sound Panel: Master Volume

### Test Volume Slider

1. **Open Sound panel** (🔊 icon)
2. **Move master volume slider**:
   - Should call: `openctrol.set_master_volume(volume: <value>)`
   - **On Windows**: System volume should change
   - Check Windows system tray volume icon
3. **Verify**: Volume changes smoothly as you drag

### Test Mute Toggle

1. **Tap mute/unmute button**:
   - Should call: `openctrol.set_master_volume(volume: <current>, muted: <toggled>)`
   - **On Windows**: System should mute/unmute
   - Button should show muted state visually

### Optional: Cross-check with API

1. **Run PowerShell test script**:
   ```powershell
   .\tools\api-tests\test-openctrol-api.ps1 -Host localhost -Port 44325
   ```
2. **Check `/api/v1/audio/status`** response
3. **Verify**: Volume and mute state match what you set in the card

✅ **Volume controls should work correctly**

## 7. Monitor Panel

**Note**: For v1, monitor list is not exposed via HA, so manual ID entry is used.

### Test Monitor Selection

1. **Open Monitor panel** (🖥 icon)
2. **Enter monitor ID** (e.g., "1", "2", "DISPLAY1" - depends on your format)
3. **Press Enter or blur the input**:
   - Should call: `openctrol.select_monitor(monitor_id: "<id>")`
4. **Watch Openctrol logs**:
   - Should show monitor selection being received and applied
5. **Verify on Windows**: Remote desktop should switch to selected monitor

### View Remote Desktop Status

1. **Check "Remote Desktop Status" section**:
   - Should show:
     - Running: Yes/No
     - State: (current state string)
     - Desktop State: (desktop state string)
2. **Verify**: Status matches actual agent state

✅ **Monitor selection should work (even with manual ID entry)**

## 8. Regression: Online/Offline Behavior

### Test Offline State

1. **Stop Openctrol Windows service**:
   ```powershell
   Stop-Service OpenctrolAgent
   ```
2. **Wait ~30 seconds** for status sensor to flip to OFFLINE
3. **Card should**:
   - Show **OFFLINE** status pill (grey)
   - **Disable/grey** touchpad (visual feedback)
   - **Disable** power/audio controls (buttons disabled)
4. **Try clicking touchpad**:
   - Service calls should **fail gracefully**
   - HA services may log errors (expected)
   - **Card should NOT crash or lock up**
   - No unhandled exceptions in browser console

### Test Recovery

1. **Start Openctrol service**:
   ```powershell
   Start-Service OpenctrolAgent
   ```
2. **Wait ~30 seconds**
3. **Status sensor** should return to ONLINE
4. **Card controls** should work again
5. **Test a click or key combo** to verify full recovery

✅ **Card should handle offline state gracefully and recover**

## Troubleshooting

### Card Not Loading

- Check browser console for JavaScript errors
- Verify card resource is added in Lovelace Resources
- Check file path: `config/www/openctrol/openctrol-card.js`

### Service Calls Failing

- Check HA logs for `HomeAssistantError` messages
- Verify entity ID is correct
- Check Openctrol agent logs for API errors
- Verify agent is running and accessible

### Touchpad Not Working

- Check browser console for errors
- Verify entity state is "online"
- Check HA service logs for `send_pointer_event` errors
- Verify WebSocket connection in Openctrol logs

### Modifier Keys Not Latching

- Check browser console for JavaScript errors
- Verify touch/mouse events are not being prevented
- Test with both mouse and touch to isolate input method issues

### Volume Not Changing

- Check HA service logs
- Verify `set_master_volume` service is being called
- Check Openctrol agent logs for audio API errors
- Verify audio manager is working on Windows

## Expected Service Call Patterns

When testing, you should see these service calls in HA logs:

```
openctrol.send_pointer_event: {entity_id: "sensor.openctrol_pc_status", type: "move", dx: 5, dy: 3}
openctrol.send_pointer_event: {entity_id: "sensor.openctrol_pc_status", type: "click", button: "left"}
openctrol.send_key_combo: {entity_id: "sensor.openctrol_pc_status", keys: ["CTRL", "C"]}
openctrol.power_action: {entity_id: "sensor.openctrol_pc_status", action: "restart", force: false}
openctrol.set_master_volume: {entity_id: "sensor.openctrol_pc_status", volume: 75, muted: false}
```

All service calls should include the `entity_id` and appropriate data fields.

