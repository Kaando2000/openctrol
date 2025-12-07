# Openctrol Lovelace Card

A custom Lovelace card for controlling Openctrol Agent from Home Assistant.

## Installation

1. Copy the `openctrol-card.js` file to your Home Assistant `www/openctrol/` directory:
   ```
   config/www/openctrol/openctrol-card.js
   ```

2. Add the card as a resource in your Lovelace configuration:
   - Go to Settings → Dashboards → Resources
   - Click "Add Resource"
   - URL: `/local/openctrol/openctrol-card.js`
   - Type: JavaScript Module

## Configuration

### Basic Configuration

```yaml
type: custom:openctrol-card
entity: sensor.openctrol_pc_status
```

### Full Configuration Options

```yaml
type: custom:openctrol-card
entity: sensor.openctrol_pc_status
title: "Living Room PC"  # Optional: override entity friendly name
show_power: true         # Optional: show power button (default: true)
show_monitor: true       # Optional: show monitor button (default: true)
show_keyboard: true      # Optional: show keyboard button (default: true)
show_sound: true         # Optional: show sound button (default: true)
```

## Features

### Touchpad
- **Move**: Drag finger across touchpad to move mouse cursor
- **Click**: Single tap for left click
- **Right Click**: Use the "Right Click" button or two-finger tap
- **Scroll**: Enable scroll mode toggle, or use two-finger drag
- **Scroll Mode**: Toggle button to switch between move and scroll modes

### Power Control
- Restart system (with confirmation)
- Shutdown system (with confirmation)
- Wake on LAN

### Monitor Control
- Select monitor by ID (e.g., DISPLAY1, DISPLAY2)
- View remote desktop status

### Keyboard
- **Modifier Keys**: CTRL, ALT, SHIFT, WIN
  - Tap: Send key combo with current latched modifiers
  - Right-click (or long-press): Toggle latch state
- **Special Keys**: TAB, ESC, SPACE, DEL, ENTER, BACKSPACE
- **Shortcuts**: Pre-defined combos (Win+D, Alt+F4, Win+L, Ctrl+Alt+Del)
- **Arrow Keys**: UP, DOWN, LEFT, RIGHT
- **Navigation Keys**: HOME, END, PAGEUP, PAGEDOWN
- **Function Keys**: F1-F12

### Sound Mixer
- Master volume slider (0-100%)
- Mute/unmute toggle
- Device selection (manual device ID entry for now)

## Requirements

- Home Assistant with the Openctrol custom integration installed
- Openctrol Agent running and accessible
- Status sensor entity must be configured (e.g., `sensor.openctrol_pc_status`)

## Notes

- The card requires the entity to be "online" for most interactions
- When offline, the touchpad is disabled but the UI remains visible
- All interactions go through Home Assistant services - the card never directly contacts the agent
- Service calls are fire-and-forget; the card relies on entity state updates for feedback

