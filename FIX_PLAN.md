# Openctrol Integration - Critical Issues Fix Plan

## Problem Analysis

### Issue 1: "Unknown pointer event type: button" Error
**Root Cause:**
- The `button` event type is only implemented for session-based WebSocket endpoint
- When session creation fails (limit reached), code falls back to deprecated endpoint
- Deprecated endpoint doesn't support `button` event type, only `click`, `move`, `scroll`
- Error occurs at line 368 in `ws.py`: `raise ValueError(f"Unknown pointer event type: {event_type}")`

**Impact:**
- All mouse button toggle functionality fails
- Touchpad buttons don't work
- Click buttons don't work

**Solution:**
1. Add `button` event type handling to deprecated endpoint
2. Convert button down/up to click events for deprecated endpoint (since it doesn't support separate down/up)
3. OR: Always use click events for deprecated endpoint, button events only for session-based

### Issue 2: Session Reuse Not Working
**Root Cause:**
- Session lookup logic searches for existing sessions but may not find them
- Sessions might not be stored correctly in entry_data
- Session URLs might be expired or invalid

**Impact:**
- Can't reuse existing sessions
- Always hits session limit
- Falls back to deprecated endpoint which has limited functionality

**Solution:**
1. Improve session storage: Ensure sessions are stored with entry_id
2. Add session validation: Check if stored session URL is still valid
3. Better error handling: If session reuse fails, try to end old session and create new one
4. Add logging to track session lookup process

### Issue 3: Card Header Design
**Root Cause:**
- Current header shows "PC: [computer name]" with small status icon
- User wants: Just "openctrol" title with bigger status icon on the title

**Impact:**
- UI doesn't match user requirements

**Solution:**
1. Change title to just "openctrol"
2. Make status icon bigger (16-20px instead of 12px)
3. Position status icon directly on/in the title text

### Issue 4: Touchpad Not Working
**Root Cause:**
- Button events fail due to Issue 1
- WebSocket connection might not be established
- Event handlers might not be properly attached

**Impact:**
- Touchpad area doesn't respond to input
- Mouse movement doesn't work
- Clicks don't work

**Solution:**
1. Fix button event handling (Issue 1)
2. Ensure WebSocket connection before sending events
3. Add proper error handling and user feedback
4. Verify event handlers are attached correctly

### Issue 5: Button Layout
**Root Cause:**
- Current layout: All buttons same size in a row
- User wants: Left/Right click buttons bigger, scroll/move button small and in the middle

**Impact:**
- UI doesn't match user requirements

**Solution:**
1. Make left/right click buttons larger (flex: 2 or min-width: 150px)
2. Make scroll/move button smaller (flex: 1 or min-width: 80px)
3. Position scroll/move button between left and right buttons
4. Update CSS for proper spacing and alignment

### Issue 6: Keyboard Keys Not Working / No Hold Toggle
**Root Cause:**
- WebSocket connection might not be established
- Key event format might be incorrect
- Hold/toggle logic might have bugs
- Modifier keys might not be handled correctly

**Impact:**
- Keyboard keys don't send input
- Hold to toggle doesn't work
- Modifier keys don't latch

**Solution:**
1. Verify WebSocket connection before sending keys
2. Check key event format matches backend expectations
3. Fix hold/toggle logic: Short tap = send key, Long press (500ms+) = toggle latch
4. Ensure modifier keys are handled separately from regular keys
5. Add proper error handling and logging

### Issue 7: Monitor Enumeration - Only 1 Monitor Shown
**Root Cause:**
- Windows service context limitations (Session 0 isolation)
- `Screen.AllScreens` might only see primary monitor in service
- Win32 API enumeration might not be finding all monitors
- Monitor enumeration code might have bugs

**Impact:**
- Only 1 monitor shown instead of 2
- Can't select second monitor
- Monitor button doesn't work

**Solution:**
1. Improve monitor enumeration: Try multiple methods
2. Use WTS API to get active session's display configuration
3. Query display configuration from active user session
4. Add detailed logging to track enumeration process
5. Fallback: Allow manual monitor ID entry if enumeration fails

## Implementation Plan

### Phase 1: Critical Fixes (Must Fix First)
1. **Fix button event type error**
   - Add button event handling to deprecated endpoint
   - Convert button down/up to appropriate format for deprecated endpoint
   - Test with both session-based and deprecated endpoints

2. **Fix session reuse**
   - Improve session storage and lookup
   - Add session validation
   - Better error handling

3. **Fix WebSocket connection**
   - Ensure connection before all input operations
   - Add retry logic
   - Better error messages

### Phase 2: UI/UX Fixes
4. **Fix card header**
   - Change title to "openctrol"
   - Make status icon bigger and position on title

5. **Fix button layout**
   - Resize left/right buttons
   - Resize scroll/move button
   - Reposition buttons

### Phase 3: Functionality Fixes
6. **Fix keyboard keys**
   - Verify WebSocket connection
   - Fix hold/toggle logic
   - Test all key types

7. **Fix monitor enumeration**
   - Improve enumeration methods
   - Add logging
   - Test with multiple monitors

## Testing Checklist

Before considering the project "bulletproof", verify:

- [ ] Button events work with both session-based and deprecated endpoints
- [ ] Session reuse works correctly
- [ ] Touchpad responds to mouse/touch input
- [ ] Left/Right click buttons work with toggle
- [ ] Scroll/Move button works
- [ ] Keyboard keys send input correctly
- [ ] Keyboard hold/toggle works for modifier keys
- [ ] All monitors are detected and listed
- [ ] Monitor selection works
- [ ] Card header shows "openctrol" with status icon
- [ ] Button layout matches requirements
- [ ] No errors in logs
- [ ] WebSocket connection is stable
- [ ] All error cases are handled gracefully

## Files to Modify

1. `homeassistant/custom_components/openctrol/ws.py`
   - Add button event handling to deprecated endpoint
   - Improve session reuse logic
   - Better WebSocket connection handling

2. `homeassistant/custom_components/openctrol/__init__.py`
   - Improve session storage
   - Better error handling

3. `www/openctrol/openctrol-card.js`
   - Fix header design
   - Fix button layout
   - Fix keyboard hold/toggle logic
   - Improve error handling

4. `src/Openctrol.Agent/RemoteDesktop/RemoteDesktopEngine.cs`
   - Improve monitor enumeration
   - Add better logging

