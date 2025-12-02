# Home Assistant Integration Diagnosis & To-Do List

## Executive Summary

The Openctrol Home Assistant integration and card have foundational functionality but are missing critical features for a complete user-ready experience. The card currently supports basic touchpad/keyboard control and power management, but lacks video streaming, proper multi-screen support, and comprehensive audio device management.

## Current State Analysis

### ✅ Working Features
- Basic touchpad control (move, click, scroll)
- Keyboard input with modifier keys and shortcuts
- Power actions (restart, shutdown, WOL)
- Master volume control (basic)
- Monitor selection (manual entry only)
- Status sensor with health monitoring
- WebSocket connection for input events

### ❌ Missing/Broken Features

#### 1. Video Streaming (CRITICAL)
- **Status**: Not implemented
- **Issue**: Card has no video display capability
- **Impact**: Users cannot see the remote desktop screen
- **Required**: 
  - Desktop session creation/management
  - WebSocket video frame reception and rendering
  - Canvas/Video element for displaying frames

#### 2. Multi-Screen Viewing (HIGH PRIORITY)
- **Status**: Partially implemented
- **Issue**: 
  - Monitor list is not fetched from API (`async_get_monitors()` exists but unused)
  - Only manual monitor ID entry available
  - No visual monitor selection UI
  - Current selected monitor not displayed
- **Impact**: Users must manually type monitor IDs without knowing available options
- **Required**:
  - Fetch monitor list from `/api/v1/rd/monitors`
  - Display monitors in dropdown/buttons with names and resolutions
  - Show current selected monitor
  - Auto-refresh monitor list periodically

#### 3. Sound Controls (HIGH PRIORITY)
- **Status**: Partially implemented
- **Issue**:
  - Audio device list not fetched (`async_get_audio_status()` exists but unused)
  - Only manual device ID entry available
  - Master volume works but doesn't reflect current state
  - No per-device volume/mute controls
  - No audio session (application) controls
- **Impact**: Users cannot see or control individual audio devices/applications
- **Required**:
  - Fetch audio state from `/api/v1/audio/status` or `/api/v1/audio/state`
  - Display list of audio devices with current volume/mute state
  - Per-device volume sliders and mute buttons
  - Set default output device functionality
  - Display audio sessions (applications) with controls
  - Sync master volume display with actual state

#### 4. Session Management (MEDIUM PRIORITY)
- **Status**: Using deprecated endpoint
- **Issue**: 
  - WebSocket client uses `/api/v1/rd/session` (deprecated)
  - Should use session-based approach: `/api/v1/sessions/desktop` → `/ws/desktop?sess=...&token=...`
- **Impact**: May break with future API updates
- **Required**:
  - Implement session creation endpoint call
  - Update WebSocket connection to use session-based URL
  - Implement session cleanup on disconnect

#### 5. Code Issues (LOW PRIORITY)
- **Status**: Minor bugs
- **Issue**: 
  - `ATTR_FORCE` used in `__init__.py` but not imported from `const.py`
  - Missing error handling in some service calls
- **Impact**: Potential runtime errors
- **Required**: Fix imports and add error handling

## Detailed To-Do List

### Phase 1: Critical Features (Video Streaming)

#### 1.1 Desktop Session Management
- [ ] Add `async_create_desktop_session()` method to `api.py`
  - Call `POST /api/v1/sessions/desktop` with `ha_id` and `ttl_seconds`
  - Return session_id, websocket_url, expires_at
- [ ] Add `async_end_desktop_session()` method to `api.py`
  - Call `POST /api/v1/sessions/desktop/{session_id}/end`
- [ ] Add session management to `ws.py`
  - Create session before connecting WebSocket
  - Store session_id for cleanup
  - End session on disconnect/error

#### 1.2 Video Frame Reception
- [ ] Update `OpenctrolWsClient` to handle binary video frames
  - Parse OFRA header format (16 bytes: "OFRA" + width + height + format)
  - Queue frames for rendering
- [ ] Add frame buffer management
  - Handle frame queue overflow
  - Implement frame dropping strategy for performance

#### 1.3 Video Display in Card
- [ ] Add `<canvas>` or `<img>` element to card for video display
- [ ] Implement frame rendering
  - Decode JPEG frames from WebSocket
  - Draw frames to canvas or update img src
  - Handle aspect ratio and scaling
- [ ] Add video controls
  - Play/pause (connect/disconnect)
  - Fullscreen toggle
  - Quality/resolution display

#### 1.4 Video Integration with Touchpad
- [ ] Position touchpad overlay over video display
- [ ] Map touch coordinates to video coordinates
- [ ] Handle video area click detection vs. control buttons

### Phase 2: Multi-Screen Support

#### 2.1 Monitor List Fetching
- [ ] Add monitor list fetching to sensor coordinator
  - Call `async_get_monitors()` periodically
  - Store monitor list in coordinator data
- [ ] Expose monitor list in sensor attributes
  - Add `available_monitors` attribute with list
  - Add `selected_monitor_id` attribute

#### 2.2 Monitor Selection UI
- [ ] Update monitor panel in card
  - Replace text input with dropdown/button list
  - Display monitor name, resolution, and primary indicator
  - Show current selected monitor highlighted
- [ ] Add monitor refresh button
- [ ] Auto-select primary monitor on first load

#### 2.3 Multi-Screen Video Display
- [ ] Support switching video stream when monitor changes
  - Recreate session or update monitor selection
  - Handle video stream transition smoothly
- [ ] Display monitor info overlay on video
  - Show current monitor name/resolution
  - Show available monitors count

### Phase 3: Sound Controls

#### 3.1 Audio State Fetching
- [ ] Add audio state fetching to sensor coordinator
  - Call `async_get_audio_status()` periodically
  - Store audio state in coordinator data
- [ ] Expose audio state in sensor attributes
  - Add `audio_devices` attribute
  - Add `audio_sessions` attribute
  - Add `master_volume` and `master_muted` attributes

#### 3.2 Audio Device UI
- [ ] Update sound panel in card
  - Display list of audio devices with:
    - Device name
    - Current volume (0-100%)
    - Mute state indicator
    - Default device indicator
  - Per-device controls:
    - Volume slider
    - Mute toggle button
    - Set as default button
- [ ] Sync master volume display with actual state
  - Fetch current master volume on panel open
  - Update slider and mute button state

#### 3.3 Audio Session Controls
- [ ] Add audio session (application) controls
  - Display list of active audio sessions
  - Per-session volume and mute controls
  - Device routing display (if available)

#### 3.4 Audio API Methods
- [ ] Verify `async_set_device_volume()` handles volume correctly
  - API expects 0.0-1.0 float, card uses 0-100 int
  - Add conversion: `volume / 100.0`
- [ ] Add `async_set_session_volume()` method if needed
  - For per-application audio control

### Phase 4: Code Quality & Error Handling

#### 4.1 Fix Import Issues
- [ ] Fix `ATTR_FORCE` import in `__init__.py`
  - Add to imports from `const.py`
- [ ] Verify all constants are properly imported

#### 4.2 Error Handling
- [ ] Add comprehensive error handling
  - WebSocket connection failures
  - API call failures
  - Video frame decode errors
  - Session creation/cleanup errors
- [ ] Display user-friendly error messages in card
- [ ] Add retry logic for transient failures

#### 4.3 State Management
- [ ] Improve state synchronization
  - Ensure card reflects actual agent state
  - Handle state updates from multiple sources
  - Prevent race conditions

### Phase 5: User Experience Enhancements

#### 5.1 Loading States
- [ ] Add loading indicators
  - Video stream connecting
  - Monitor list loading
  - Audio state loading
  - Session creation

#### 5.2 Connection Status
- [ ] Improve connection status display
  - Show WebSocket connection state
  - Show video stream state separately
  - Show session expiry countdown

#### 5.3 Responsive Design
- [ ] Ensure card works on mobile devices
  - Touch-friendly controls
  - Responsive video display
  - Mobile-optimized panels

#### 5.4 Configuration
- [ ] Add card configuration options
  - Video quality preference
  - Auto-connect on card load
  - Default monitor selection
  - Panel visibility toggles (already exists, verify)

## Testing Checklist

### Video Streaming
- [ ] Video displays correctly
- [ ] Video updates smoothly (no stuttering)
- [ ] Video reconnects after disconnect
- [ ] Video handles monitor switch
- [ ] Video aspect ratio maintained

### Multi-Screen
- [ ] Monitor list displays correctly
- [ ] Monitor selection works
- [ ] Video switches to selected monitor
- [ ] Primary monitor auto-selected
- [ ] Monitor list refreshes

### Sound Controls
- [ ] Audio device list displays
- [ ] Master volume syncs with actual state
- [ ] Device volume controls work
- [ ] Device mute controls work
- [ ] Set default device works
- [ ] Audio sessions display (if implemented)

### Touchpad & Keyboard
- [ ] Touchpad works with video overlay
- [ ] Keyboard input works
- [ ] Modifier keys latch correctly
- [ ] Shortcuts work

### Power Controls
- [ ] Power actions work
- [ ] Confirmation dialogs appear
- [ ] Errors handled gracefully

### Error Handling
- [ ] Connection failures handled
- [ ] API errors displayed
- [ ] WebSocket reconnection works
- [ ] Session cleanup on errors

## API Endpoints Reference

### Required Endpoints
- `POST /api/v1/sessions/desktop` - Create desktop session
- `POST /api/v1/sessions/desktop/{id}/end` - End session
- `GET /api/v1/rd/monitors` - Get monitor list
- `POST /api/v1/rd/monitor` - Select monitor
- `GET /api/v1/audio/status` - Get audio status (or `/api/v1/audio/state`)
- `POST /api/v1/audio/master` - Set master volume
- `POST /api/v1/audio/device` - Set device volume
- `POST /api/v1/audio/default` - Set default device
- `WebSocket /ws/desktop?sess={session_id}&token={token}` - Video stream

### Current Usage
- `GET /api/v1/health` - ✅ Used
- `POST /api/v1/power` - ✅ Used
- `POST /api/v1/rd/monitor` - ✅ Used
- `POST /api/v1/audio/master` - ✅ Used
- `POST /api/v1/audio/device` - ✅ Used
- `POST /api/v1/audio/default` - ✅ Used
- `WebSocket /api/v1/rd/session` - ⚠️ Deprecated, should use session-based

## Implementation Priority

1. **P0 - Critical**: Video streaming (Phase 1)
2. **P1 - High**: Multi-screen support (Phase 2)
3. **P1 - High**: Sound controls (Phase 3)
4. **P2 - Medium**: Session management update (Phase 4.1)
5. **P2 - Medium**: Error handling (Phase 4.2)
6. **P3 - Low**: UX enhancements (Phase 5)

## Notes

- The card currently uses a deprecated WebSocket endpoint. Migration to session-based approach is recommended but not blocking if current endpoint still works.
- Audio volume API uses 0.0-1.0 float range, but card UI uses 0-100 integer. Conversion needed.
- Video frames are JPEG encoded with OFRA header format (16 bytes).
- Session TTL should be set appropriately (60-3600 seconds, clamped by API).
- HA installation ID (`ha_id`) needed for session creation - can use Home Assistant's installation ID or generate a unique one per card instance.

