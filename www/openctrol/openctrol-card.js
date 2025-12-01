/**
 * Openctrol Custom Lovelace Card
 * 
 * A touch-first remote control card for Openctrol Agent.
 * Requires Home Assistant frontend and Openctrol integration.
 * 
 * Card type: custom:openctrol-card
 * Required config: entity (Openctrol status sensor entity_id)
 */

// LitElement, html, and css are provided by Home Assistant frontend
class OpenctrolCard extends LitElement {
  static get properties() {
    return {
      hass: { type: Object },
      config: { type: Object },
      _entity: { state: true },
      _activePanel: { state: true },
      _latchedModifiers: { state: true },
      _masterVolume: { state: true },
      _masterMuted: { state: true },
      _touchStart: { state: true },
      _lastMove: { state: true },
      _scrollMode: { state: true },
      _powerConfirm: { state: true },
    };
  }

  static get styles() {
    return css`
      :host {
        display: block;
        padding: 16px;
        font-family: var(--ha-card-header-font-family, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Oxygen, Ubuntu, Cantarell, "Helvetica Neue", sans-serif);
      }

      .card {
        background: var(--card-background-color, white);
        border-radius: var(--ha-card-border-radius, 4px);
        box-shadow: var(--ha-card-box-shadow, 0 2px 4px rgba(0,0,0,0.1);
        overflow: hidden;
      }

      .top-bar {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 12px 16px;
        background: var(--ha-card-header-background, var(--primary-color));
        color: var(--ha-card-header-color, white);
        border-bottom: 1px solid rgba(0,0,0,0.1);
      }

      .top-bar-left {
        display: flex;
        align-items: center;
        gap: 12px;
        flex: 1;
      }

      .title {
        font-size: 18px;
        font-weight: 500;
        margin: 0;
      }

      .status-pill {
        padding: 4px 12px;
        border-radius: 12px;
        font-size: 12px;
        font-weight: 600;
        text-transform: uppercase;
      }

      .status-online {
        background: #4caf50;
        color: white;
      }

      .status-offline {
        background: #9e9e9e;
        color: white;
      }

      .status-degraded {
        background: #ff9800;
        color: white;
        margin-left: 8px;
      }

      .top-bar-right {
        display: flex;
        gap: 8px;
      }

      .icon-button {
        width: 40px;
        height: 40px;
        border-radius: 50%;
        border: none;
        background: rgba(255, 255, 255, 0.2);
        color: white;
        cursor: pointer;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 20px;
        transition: background 0.2s;
      }

      .icon-button:hover {
        background: rgba(255, 255, 255, 0.3);
      }

      .icon-button.active {
        background: rgba(255, 255, 255, 0.4);
      }

      .icon-button:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }

      .touchpad-container {
        position: relative;
        width: 100%;
        height: 400px;
        background: #f5f5f5;
        border: 2px solid #e0e0e0;
        border-radius: 8px;
        margin: 16px;
        touch-action: none;
        user-select: none;
        overflow: hidden;
      }

      .touchpad-container.offline {
        opacity: 0.5;
        pointer-events: none;
      }

      .touchpad {
        width: 100%;
        height: 100%;
        position: relative;
      }

      .touchpad-overlay {
        position: absolute;
        top: 0;
        left: 0;
        right: 0;
        bottom: 0;
        display: flex;
        align-items: center;
        justify-content: center;
        color: #999;
        font-size: 14px;
      }

      .button-row {
        display: flex;
        gap: 8px;
        padding: 0 16px 16px;
        justify-content: center;
      }

      .action-button {
        padding: 12px 24px;
        border-radius: 8px;
        border: 2px solid var(--primary-color);
        background: white;
        color: var(--primary-color);
        font-size: 14px;
        font-weight: 500;
        cursor: pointer;
        transition: all 0.2s;
        min-width: 100px;
      }

      .action-button:hover {
        background: var(--primary-color);
        color: white;
      }

      .action-button:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }

      .panel {
        position: fixed;
        bottom: 0;
        left: 0;
        right: 0;
        background: white;
        border-top-left-radius: 16px;
        border-top-right-radius: 16px;
        box-shadow: 0 -4px 16px rgba(0,0,0,0.2);
        max-height: 70vh;
        overflow-y: auto;
        z-index: 1000;
        padding: 16px;
      }

      .panel-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-bottom: 16px;
        padding-bottom: 12px;
        border-bottom: 1px solid #e0e0e0;
      }

      .panel-title {
        font-size: 18px;
        font-weight: 600;
        margin: 0;
      }

      .close-button {
        width: 32px;
        height: 32px;
        border-radius: 50%;
        border: none;
        background: #f5f5f5;
        cursor: pointer;
        display: flex;
        align-items: center;
        justify-content: center;
        font-size: 20px;
      }

      .panel-section {
        margin-bottom: 24px;
      }

      .section-title {
        font-size: 14px;
        font-weight: 600;
        color: #666;
        margin-bottom: 12px;
        text-transform: uppercase;
      }

      .power-button {
        width: 100%;
        padding: 16px;
        margin-bottom: 8px;
        border-radius: 8px;
        border: 2px solid #e0e0e0;
        background: white;
        font-size: 16px;
        font-weight: 500;
        cursor: pointer;
        transition: all 0.2s;
      }

      .power-button.restart {
        border-color: #ff9800;
        color: #ff9800;
      }

      .power-button.restart:hover {
        background: #ff9800;
        color: white;
      }

      .power-button.shutdown {
        border-color: #f44336;
        color: #f44336;
      }

      .power-button.shutdown:hover {
        background: #f44336;
        color: white;
      }

      .power-button.wol {
        border-color: #2196f3;
        color: #2196f3;
      }

      .power-button.wol:hover {
        background: #2196f3;
        color: white;
      }

      .confirm-dialog {
        background: rgba(0, 0, 0, 0.5);
        position: fixed;
        top: 0;
        left: 0;
        right: 0;
        bottom: 0;
        z-index: 2000;
        display: flex;
        align-items: center;
        justify-content: center;
      }

      .confirm-box {
        background: white;
        border-radius: 12px;
        padding: 24px;
        max-width: 400px;
        width: 90%;
      }

      .confirm-text {
        font-size: 16px;
        margin-bottom: 20px;
        text-align: center;
      }

      .confirm-buttons {
        display: flex;
        gap: 12px;
      }

      .confirm-button {
        flex: 1;
        padding: 12px;
        border-radius: 8px;
        border: none;
        font-size: 14px;
        font-weight: 500;
        cursor: pointer;
      }

      .confirm-button.cancel {
        background: #f5f5f5;
        color: #333;
      }

      .confirm-button.confirm {
        background: var(--primary-color);
        color: white;
      }

      .keyboard-row {
        display: flex;
        gap: 8px;
        margin-bottom: 12px;
        flex-wrap: wrap;
      }

      .key-button {
        padding: 12px 16px;
        border-radius: 8px;
        border: 2px solid #e0e0e0;
        background: white;
        font-size: 14px;
        font-weight: 500;
        cursor: pointer;
        transition: all 0.2s;
        min-width: 60px;
        text-align: center;
      }

      .key-button:hover {
        border-color: var(--primary-color);
        background: var(--primary-color);
        color: white;
      }

      .key-button.latched {
        background: var(--primary-color);
        color: white;
        border-color: var(--primary-color);
      }

      .shortcut-button {
        padding: 12px 20px;
        border-radius: 8px;
        border: 2px solid #2196f3;
        background: white;
        color: #2196f3;
        font-size: 14px;
        font-weight: 500;
        cursor: pointer;
        transition: all 0.2s;
      }

      .shortcut-button:hover {
        background: #2196f3;
        color: white;
      }

      .volume-slider {
        width: 100%;
        height: 6px;
        border-radius: 3px;
        background: #e0e0e0;
        outline: none;
        -webkit-appearance: none;
      }

      .volume-slider::-webkit-slider-thumb {
        -webkit-appearance: none;
        appearance: none;
        width: 20px;
        height: 20px;
        border-radius: 50%;
        background: var(--primary-color);
        cursor: pointer;
      }

      .volume-slider::-moz-range-thumb {
        width: 20px;
        height: 20px;
        border-radius: 50%;
        background: var(--primary-color);
        cursor: pointer;
        border: none;
      }

      .volume-controls {
        display: flex;
        align-items: center;
        gap: 12px;
        margin-bottom: 16px;
      }

      .volume-value {
        min-width: 50px;
        text-align: center;
        font-weight: 600;
      }

      .mute-button {
        padding: 8px 16px;
        border-radius: 8px;
        border: 2px solid #e0e0e0;
        background: white;
        font-size: 14px;
        cursor: pointer;
      }

      .mute-button.muted {
        background: #f44336;
        color: white;
        border-color: #f44336;
      }

      .monitor-input {
        width: 100%;
        padding: 12px;
        border-radius: 8px;
        border: 2px solid #e0e0e0;
        font-size: 14px;
        margin-bottom: 12px;
      }

      .info-text {
        font-size: 12px;
        color: #666;
        margin-top: 8px;
      }

      .error-message {
        padding: 16px;
        background: #ffebee;
        color: #c62828;
        border-radius: 8px;
        margin: 16px;
        text-align: center;
      }
    `;
  }

  constructor() {
    super();
    this._entity = null;
    this._activePanel = null;
    this._latchedModifiers = new Set();
    this._masterVolume = 50;
    this._masterMuted = false;
    this._touchStart = null;
    this._lastMove = null;
    this._scrollMode = false;
    this._powerConfirm = null;
    this._moveThrottle = null;
    this._keyHoldTimeouts = new Map(); // Track hold timeouts per key
  }

  setConfig(config) {
    if (!config.entity) {
      throw new Error("Entity is required");
    }
    this.config = {
      title: null,
      show_power: true,
      show_monitor: true,
      show_keyboard: true,
      show_sound: true,
      ...config,
    };
  }

  set hass(hass) {
    this._hass = hass;
    if (hass && this.config) {
      this._entity = hass.states[this.config.entity];
    }
  }

  get hass() {
    return this._hass;
  }

  get _isOnline() {
    return this._entity?.state === "online";
  }

  get _isDegraded() {
    return this._entity?.attributes?.remote_desktop_degraded === true;
  }

  get _entityTitle() {
    if (this.config.title) {
      return this.config.title;
    }
    return this._entity?.attributes?.friendly_name || this.config.entity;
  }

  _handleTouchStart(e) {
    if (!this._isOnline) return;
    
    const touch = e.touches[0] || e.changedTouches[0];
    const touchCount = e.touches.length;
    
    this._touchStart = {
      x: touch.clientX,
      y: touch.clientY,
      time: Date.now(),
      moved: false,
      touchCount: touchCount,
    };
    this._lastMove = { x: touch.clientX, y: touch.clientY };
  }

  _handleTouchMove(e) {
    if (!this._isOnline || !this._touchStart) return;
    
    e.preventDefault();
    const touch = e.touches[0] || e.changedTouches[0];
    const touchCount = e.touches.length;
    const dx = touch.clientX - this._lastMove.x;
    const dy = touch.clientY - this._lastMove.y;
    
    if (Math.abs(dx) > 2 || Math.abs(dy) > 2) {
      this._touchStart.moved = true;
      this._lastMove = { x: touch.clientX, y: touch.clientY };
      
      // Throttle move events
      if (this._moveThrottle) {
        clearTimeout(this._moveThrottle);
      }
      
      this._moveThrottle = setTimeout(() => {
        // Two-finger drag = scroll, or scroll mode enabled
        if (this._scrollMode || touchCount > 1) {
          this._sendPointerEvent("scroll", dx, dy);
        } else {
          this._sendPointerEvent("move", dx, dy);
        }
        this._moveThrottle = null;
      }, 16); // ~60fps
    }
  }

  _handleTouchEnd(e) {
    if (!this._isOnline || !this._touchStart) return;
    
    const touch = e.changedTouches[0];
    const duration = Date.now() - this._touchStart.time;
    const moved = this._touchStart.moved;
    const touchCount = e.changedTouches.length;
    
    // Two-finger tap = right click
    if (touchCount === 2 && !moved && duration < 300) {
      this._sendPointerEvent("click", null, null, "right");
    } else if (!moved && duration < 300) {
      // Single tap - left click
      this._sendPointerEvent("click", null, null, "left");
    } else if (!moved && duration >= 300 && duration < 600) {
      // Long press - treat as click for v1
      this._sendPointerEvent("click", null, null, "left");
    }
    
    this._touchStart = null;
    this._lastMove = null;
    if (this._moveThrottle) {
      clearTimeout(this._moveThrottle);
      this._moveThrottle = null;
    }
  }

  _sendPointerEvent(type, dx, dy, button) {
    if (!this._entity || !this._isOnline) return;
    
    const data = {
      entity_id: this.config.entity,
      type: type,
    };
    
    if (dx !== null && dy !== null) {
      data.dx = dx;
      data.dy = dy;
    }
    
    if (button) {
      data.button = button;
    }
    
    this.hass.callService("openctrol", "send_pointer_event", data).catch((err) => {
      console.error("Failed to send pointer event:", err);
    });
  }

  _sendKeyCombo(keys) {
    if (!this._entity || !this._isOnline) return;
    
    // If keys is already an array (from shortcut), use it directly
    // Otherwise combine with latched modifiers
    const combo = Array.isArray(keys) && keys.length > 1 
      ? keys 
      : [...this._latchedModifiers, ...(Array.isArray(keys) ? keys : [keys])];
    
    this.hass.callService("openctrol", "send_key_combo", {
      entity_id: this.config.entity,
      keys: combo,
    }).catch((err) => {
      console.error("Failed to send key combo:", err);
    });
  }

  _handleKeyButton(key, isShortcut = false) {
    if (isShortcut) {
      // Shortcuts ignore latched modifiers
      this._sendKeyCombo(key);
      return;
    }
    
    // Regular key - send with latched modifiers
    this._sendKeyCombo([key]);
  }

  _handleKeyHold(key) {
    // Long press on modifier key toggles latch
    const isModifier = ["CTRL", "ALT", "SHIFT", "WIN"].includes(key);
    if (isModifier) {
      this._toggleModifierLatch(key);
    }
  }

  _toggleModifierLatch(key) {
    if (this._latchedModifiers.has(key)) {
      this._latchedModifiers.delete(key);
    } else {
      this._latchedModifiers.add(key);
    }
    this.requestUpdate();
  }

  _handlePowerAction(action) {
    if (action === "restart" || action === "shutdown") {
      this._powerConfirm = action;
      this.requestUpdate();
    } else {
      this._callPowerAction(action);
    }
  }

  _callPowerAction(action) {
    if (!this._entity || !this._isOnline) {
      console.warn("Cannot execute power action: agent is offline");
      return;
    }
    
    this.hass.callService("openctrol", "power_action", {
      entity_id: this.config.entity,
      action: action,
      force: false,
    }).catch((err) => {
      console.error("Failed to execute power action:", err);
    });
    
    this._powerConfirm = null;
    this._activePanel = null;
    this.requestUpdate();
  }

  _handleSelectMonitor(monitorId) {
    if (!this._entity || !this._isOnline) return;
    
    this.hass.callService("openctrol", "select_monitor", {
      entity_id: this.config.entity,
      monitor_id: monitorId,
    }).catch((err) => {
      console.error("Failed to select monitor:", err);
    });
  }

  _handleMasterVolumeChange(e) {
    const volume = parseInt(e.target.value, 10);
    this._masterVolume = volume;
    this._updateMasterVolume(volume, this._masterMuted);
  }

  _handleMuteToggle() {
    this._masterMuted = !this._masterMuted;
    this._updateMasterVolume(this._masterVolume, this._masterMuted);
  }

  _updateMasterVolume(volume, muted) {
    if (!this._entity || !this._isOnline) return;
    
    this.hass.callService("openctrol", "set_master_volume", {
      entity_id: this.config.entity,
      volume: volume,
      muted: muted,
    }).catch((err) => {
      console.error("Failed to set master volume:", err);
    });
  }

  _renderTopBar() {
    return html`
      <div class="top-bar">
        <div class="top-bar-left">
          <h2 class="title">${this._entityTitle}</h2>
          <span class="status-pill ${this._isOnline ? "status-online" : "status-offline"}">
            ${this._isOnline ? "ONLINE" : "OFFLINE"}
          </span>
          ${this._isDegraded ? html`<span class="status-pill status-degraded">DEGRADED</span>` : ""}
        </div>
        <div class="top-bar-right">
          ${this.config.show_power ? html`
            <button
              class="icon-button ${this._activePanel === "power" ? "active" : ""}"
              @click=${() => this._activePanel = this._activePanel === "power" ? null : "power"}
              ?disabled=${!this._isOnline}
            >
              ⚡
            </button>
          ` : ""}
          ${this.config.show_monitor ? html`
            <button
              class="icon-button ${this._activePanel === "monitor" ? "active" : ""}"
              @click=${() => this._activePanel = this._activePanel === "monitor" ? null : "monitor"}
              ?disabled=${!this._isOnline}
            >
              🖥
            </button>
          ` : ""}
          ${this.config.show_keyboard ? html`
            <button
              class="icon-button ${this._activePanel === "keyboard" ? "active" : ""}"
              @click=${() => this._activePanel = this._activePanel === "keyboard" ? null : "keyboard"}
              ?disabled=${!this._isOnline}
            >
              ⌨
            </button>
          ` : ""}
          ${this.config.show_sound ? html`
            <button
              class="icon-button ${this._activePanel === "sound" ? "active" : ""}"
              @click=${() => this._activePanel = this._activePanel === "sound" ? null : "sound"}
              ?disabled=${!this._isOnline}
            >
              🔊
            </button>
          ` : ""}
        </div>
      </div>
    `;
  }

  _renderTouchpad() {
    return html`
      <div class="touchpad-container ${!this._isOnline ? "offline" : ""}">
        <div
          class="touchpad"
          @touchstart=${this._handleTouchStart}
          @touchmove=${this._handleTouchMove}
          @touchend=${this._handleTouchEnd}
          @touchcancel=${this._handleTouchEnd}
        >
          ${!this._isOnline ? html`
            <div class="touchpad-overlay">
              Offline - Touchpad disabled
            </div>
          ` : ""}
        </div>
      </div>
    `;
  }

  _renderButtonRow() {
    return html`
      <div class="button-row">
        <button
          class="action-button"
          @click=${() => this._sendPointerEvent("click", null, null, "left")}
          ?disabled=${!this._isOnline}
        >
          Left Click
        </button>
        <button
          class="action-button"
          @click=${() => this._sendPointerEvent("click", null, null, "right")}
          ?disabled=${!this._isOnline}
        >
          Right Click
        </button>
        <button
          class="action-button"
          @click=${() => this._sendPointerEvent("click", null, null, "middle")}
          ?disabled=${!this._isOnline}
        >
          Middle Click
        </button>
        <button
          class="action-button ${this._scrollMode ? "active" : ""}"
          @click=${() => { this._scrollMode = !this._scrollMode; this.requestUpdate(); }}
          ?disabled=${!this._isOnline}
        >
          ${this._scrollMode ? "Scroll Mode" : "Move Mode"}
        </button>
      </div>
    `;
  }

  _renderPowerPanel() {
    return html`
      <div class="panel">
        <div class="panel-header">
          <h3 class="panel-title">Power Control</h3>
          <button class="close-button" @click=${() => { this._activePanel = null; this.requestUpdate(); }}>×</button>
        </div>
        <div class="panel-section">
          <button
            class="power-button restart"
            @click=${() => this._handlePowerAction("restart")}
          >
            Restart
          </button>
          <button
            class="power-button shutdown"
            @click=${() => this._handlePowerAction("shutdown")}
          >
            Shutdown
          </button>
          <button
            class="power-button wol"
            @click=${() => this._handlePowerAction("wol")}
          >
            Wake on LAN
          </button>
        </div>
      </div>
    `;
  }

  _renderMonitorPanel() {
    const rdState = this._entity?.attributes?.remote_desktop_state || "unknown";
    const rdDesktopState = this._entity?.attributes?.remote_desktop_desktop_state || "unknown";
    const rdRunning = this._entity?.attributes?.remote_desktop_is_running || false;
    
    return html`
      <div class="panel">
        <div class="panel-header">
          <h3 class="panel-title">Monitor Control</h3>
          <button class="close-button" @click=${() => { this._activePanel = null; this.requestUpdate(); }}>×</button>
        </div>
        <div class="panel-section">
          <div class="section-title">Monitor Selection</div>
          <input
            type="text"
            class="monitor-input"
            placeholder="Monitor ID (e.g., DISPLAY1)"
            @change=${(e) => {
              const monitorId = e.target.value.trim();
              if (monitorId) {
                this._handleSelectMonitor(monitorId);
              }
            }}
          />
          <div class="info-text">
            Enter the monitor ID to select. Common values: DISPLAY1, DISPLAY2, etc.
          </div>
        </div>
        <div class="panel-section">
          <div class="section-title">Remote Desktop Status</div>
          <div class="info-text">
            Running: ${rdRunning ? "Yes" : "No"}<br>
            State: ${rdState}<br>
            Desktop State: ${rdDesktopState}
          </div>
        </div>
      </div>
    `;
  }

  _renderKeyboardPanel() {
    const modifiers = ["CTRL", "ALT", "SHIFT", "WIN"];
    const specialKeys = ["TAB", "ESC", "SPACE", "DEL", "ENTER", "BACKSPACE"];
    const shortcuts = [
      { label: "Win + D", keys: ["WIN", "D"] },
      { label: "Alt + F4", keys: ["ALT", "F4"] },
      { label: "Win + L", keys: ["WIN", "L"] },
      { label: "Ctrl + Alt + Del", keys: ["CTRL", "ALT", "DEL"] },
    ];
    const arrows = ["UP", "DOWN", "LEFT", "RIGHT"];
    const navKeys = ["HOME", "END", "PAGEUP", "PAGEDOWN"];
    const functionKeys = Array.from({ length: 12 }, (_, i) => `F${i + 1}`);
    
    return html`
      <div class="panel">
        <div class="panel-header">
          <h3 class="panel-title">Keyboard</h3>
          <button class="close-button" @click=${() => { this._activePanel = null; this.requestUpdate(); }}>×</button>
        </div>
        <div class="panel-section">
          <div class="section-title">Modifiers & Special Keys</div>
          <div class="keyboard-row">
            ${modifiers.map(key => html`
              <button
                class="key-button ${this._latchedModifiers.has(key) ? "latched" : ""}"
                @click=${() => {
                  const timeout = this._keyHoldTimeouts.get(key);
                  if (timeout) {
                    clearTimeout(timeout);
                    this._keyHoldTimeouts.delete(key);
                    // It was a short tap, send the key
                    this._handleKeyButton(key);
                  } else {
                    // No timeout means it was already held, just send
                    this._handleKeyButton(key);
                  }
                }}
                @mousedown=${() => {
                  const timeout = setTimeout(() => {
                    this._handleKeyHold(key);
                    this._keyHoldTimeouts.delete(key);
                  }, 500);
                  this._keyHoldTimeouts.set(key, timeout);
                }}
                @mouseup=${() => {
                  const timeout = this._keyHoldTimeouts.get(key);
                  if (timeout) {
                    clearTimeout(timeout);
                    this._keyHoldTimeouts.delete(key);
                  }
                }}
                @mouseleave=${() => {
                  const timeout = this._keyHoldTimeouts.get(key);
                  if (timeout) {
                    clearTimeout(timeout);
                    this._keyHoldTimeouts.delete(key);
                  }
                }}
                @touchstart=${(e) => {
                  e.preventDefault();
                  const timeout = setTimeout(() => {
                    this._handleKeyHold(key);
                    this._keyHoldTimeouts.delete(key);
                  }, 500);
                  this._keyHoldTimeouts.set(key, timeout);
                }}
                @touchend=${(e) => {
                  e.preventDefault();
                  const timeout = this._keyHoldTimeouts.get(key);
                  if (timeout) {
                    clearTimeout(timeout);
                    this._keyHoldTimeouts.delete(key);
                    // Short tap, send the key
                    this._handleKeyButton(key);
                  } else {
                    // Already held, just send
                    this._handleKeyButton(key);
                  }
                }}
              >
                ${key}
              </button>
            `)}
            ${specialKeys.map(key => html`
              <button
                class="key-button"
                @click=${() => this._handleKeyButton(key)}
              >
                ${key}
              </button>
            `)}
          </div>
        </div>
        <div class="panel-section">
          <div class="section-title">Shortcuts</div>
          <div class="keyboard-row">
            ${shortcuts.map(shortcut => html`
              <button
                class="shortcut-button"
                @click=${() => this._handleKeyButton(shortcut.keys, true)}
              >
                ${shortcut.label}
              </button>
            `)}
          </div>
        </div>
        <div class="panel-section">
          <div class="section-title">Arrow Keys</div>
          <div class="keyboard-row">
            ${arrows.map(key => html`
              <button
                class="key-button"
                @click=${() => this._handleKeyButton(key)}
              >
                ${key}
              </button>
            `)}
          </div>
        </div>
        <div class="panel-section">
          <div class="section-title">Navigation Keys</div>
          <div class="keyboard-row">
            ${navKeys.map(key => html`
              <button
                class="key-button"
                @click=${() => this._handleKeyButton(key)}
              >
                ${key}
              </button>
            `)}
          </div>
        </div>
        <div class="panel-section">
          <div class="section-title">Function Keys</div>
          <div class="keyboard-row">
            ${functionKeys.map(key => html`
              <button
                class="key-button"
                @click=${() => this._handleKeyButton(key)}
              >
                ${key}
              </button>
            `)}
          </div>
        </div>
      </div>
    `;
  }

  _renderSoundPanel() {
    return html`
      <div class="panel">
        <div class="panel-header">
          <h3 class="panel-title">Sound Mixer</h3>
          <button class="close-button" @click=${() => { this._activePanel = null; this.requestUpdate(); }}>×</button>
        </div>
        <div class="panel-section">
          <div class="section-title">Master Volume</div>
          <div class="volume-controls">
            <input
              type="range"
              class="volume-slider"
              min="0"
              max="100"
              .value=${this._masterVolume}
              @input=${this._handleMasterVolumeChange}
            />
            <span class="volume-value">${this._masterVolume}%</span>
            <button
              class="mute-button ${this._masterMuted ? "muted" : ""}"
              @click=${this._handleMuteToggle}
            >
              ${this._masterMuted ? "🔇 Muted" : "🔊 Unmuted"}
            </button>
          </div>
        </div>
        <div class="panel-section">
          <div class="section-title">Device Controls</div>
          <div class="info-text">
            Device-level controls will be available when audio device information is exposed via Home Assistant entities or attributes.
          </div>
          <input
            type="text"
            class="monitor-input"
            placeholder="Device ID (manual entry)"
            @change=${(e) => {
              const deviceId = e.target.value.trim();
              if (deviceId && this._isOnline) {
                this.hass.callService("openctrol", "set_default_output_device", {
                  entity_id: this.config.entity,
                  device_id: deviceId,
                }).catch((err) => {
                  console.error("Failed to set default device:", err);
                });
              }
            }}
          />
        </div>
      </div>
    `;
  }

  _renderConfirmDialog() {
    if (!this._powerConfirm) return "";
    
    const actionLabel = this._powerConfirm === "restart" ? "restart" : "shutdown";
    
    return html`
      <div class="confirm-dialog" @click=${(e) => {
        if (e.target.classList.contains("confirm-dialog")) {
          this._powerConfirm = null;
          this.requestUpdate();
        }
      }}>
        <div class="confirm-box">
          <div class="confirm-text">
            Are you sure you want to ${actionLabel} the system?
          </div>
          <div class="confirm-buttons">
            <button
              class="confirm-button cancel"
              @click=${() => {
                this._powerConfirm = null;
                this.requestUpdate();
              }}
            >
              Cancel
            </button>
            <button
              class="confirm-button confirm"
              @click=${() => this._callPowerAction(this._powerConfirm)}
            >
              ${actionLabel.charAt(0).toUpperCase() + actionLabel.slice(1)}
            </button>
          </div>
        </div>
      </div>
    `;
  }

  render() {
    if (!this.config || !this.hass) {
      return html`<ha-card><div class="error-message">Card not configured</div></ha-card>`;
    }

    if (!this._entity) {
      return html`
        <ha-card>
          <div class="error-message">
            Entity ${this.config.entity} not found. Please check your configuration.
          </div>
        </ha-card>
      `;
    }

    return html`
      <ha-card>
        <div class="card">
          ${this._renderTopBar()}
          ${this._renderTouchpad()}
          ${this._renderButtonRow()}
        </div>
        ${this._activePanel === "power" ? this._renderPowerPanel() : ""}
        ${this._activePanel === "monitor" ? this._renderMonitorPanel() : ""}
        ${this._activePanel === "keyboard" ? this._renderKeyboardPanel() : ""}
        ${this._activePanel === "sound" ? this._renderSoundPanel() : ""}
        ${this._renderConfirmDialog()}
      </ha-card>
    `;
  }

  getCardSize() {
    return 5;
  }
}

customElements.define("openctrol-card", OpenctrolCard);

// Register card with Home Assistant
window.customCards = window.customCards || [];
window.customCards.push({
  type: "openctrol-card",
  name: "Openctrol Card",
  description: "Remote control card for Openctrol Agent",
  preview: true,
  documentationURL: "https://github.com/Kaando2000/openctrol",
});

