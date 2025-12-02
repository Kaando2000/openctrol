/**
 * Openctrol Custom Lovelace Card
 * 
 * A touch-first remote control card for Openctrol Agent.
 * Requires Home Assistant frontend and Openctrol integration.
 * 
 * Card type: custom:openctrol-card
 * Required config: entity (Openctrol status sensor entity_id)
 */

// Import LitElement, html, and css from lit
// Home Assistant provides lit globally, but for standalone ES modules we use a CDN fallback
import { LitElement, html, css } from "https://cdn.jsdelivr.net/npm/lit@3/+esm";

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

      .monitor-list {
        display: flex;
        flex-direction: column;
        gap: 8px;
        margin-bottom: 12px;
      }

      .monitor-button {
        width: 100%;
        padding: 12px 16px;
        border-radius: 8px;
        border: 2px solid #e0e0e0;
        background: white;
        cursor: pointer;
        transition: all 0.2s;
        display: flex;
        justify-content: space-between;
        align-items: center;
        text-align: left;
      }

      .monitor-button:hover:not(:disabled) {
        border-color: var(--primary-color);
        background: #f5f5f5;
      }

      .monitor-button.active {
        border-color: var(--primary-color);
        background: var(--primary-color);
        color: white;
      }

      .monitor-button:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }

      .monitor-button-content {
        flex: 1;
      }

      .monitor-name {
        font-weight: 600;
        font-size: 14px;
        margin-bottom: 4px;
      }

      .monitor-details {
        font-size: 12px;
        color: #666;
        display: flex;
        align-items: center;
        gap: 8px;
      }

      .monitor-button.active .monitor-details {
        color: rgba(255, 255, 255, 0.9);
      }

      .primary-badge {
        background: rgba(0, 0, 0, 0.1);
        padding: 2px 8px;
        border-radius: 4px;
        font-size: 10px;
        font-weight: 600;
        text-transform: uppercase;
      }

      .monitor-button.active .primary-badge {
        background: rgba(255, 255, 255, 0.2);
      }

      .selected-indicator {
        font-size: 20px;
        font-weight: bold;
        margin-left: 12px;
      }

      .device-list {
        display: flex;
        flex-direction: column;
        gap: 16px;
      }

      .device-item {
        padding: 12px;
        border: 1px solid #e0e0e0;
        border-radius: 8px;
        background: #fafafa;
      }

      .device-header {
        margin-bottom: 12px;
      }

      .device-name {
        font-weight: 600;
        font-size: 14px;
        display: flex;
        align-items: center;
        gap: 8px;
      }

      .default-badge {
        background: var(--primary-color);
        color: white;
        padding: 2px 8px;
        border-radius: 4px;
        font-size: 10px;
        font-weight: 600;
        text-transform: uppercase;
      }

      .device-controls {
        display: flex;
        align-items: center;
        gap: 12px;
      }

      .device-slider {
        flex: 1;
      }

      .device-volume {
        min-width: 50px;
      }

      .device-mute {
        padding: 8px 12px;
        min-width: 50px;
      }

      .set-default-button {
        padding: 8px 16px;
        font-size: 12px;
        min-width: auto;
      }

      .video-container {
        position: relative;
        width: 100%;
        background: #000;
        border-radius: 8px;
        overflow: hidden;
        margin: 16px;
        min-height: 300px;
        display: flex;
        align-items: center;
        justify-content: center;
      }

      .video-canvas {
        max-width: 100%;
        max-height: 100%;
        width: auto;
        height: auto;
        display: block;
      }

      .video-overlay {
        position: absolute;
        top: 0;
        left: 0;
        right: 0;
        bottom: 0;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        background: rgba(0, 0, 0, 0.7);
        color: white;
        gap: 12px;
      }

      .video-controls {
        display: flex;
        gap: 8px;
        padding: 8px;
        background: rgba(0, 0, 0, 0.5);
        border-radius: 4px;
      }

      .video-button {
        padding: 8px 16px;
        border-radius: 4px;
        border: 1px solid rgba(255, 255, 255, 0.3);
        background: rgba(255, 255, 255, 0.1);
        color: white;
        font-size: 12px;
        cursor: pointer;
        transition: all 0.2s;
      }

      .video-button:hover {
        background: rgba(255, 255, 255, 0.2);
      }

      .video-status {
        font-size: 14px;
        font-weight: 500;
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
    if (!config || !config.entity) {
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
      const newEntity = hass.states[this.config.entity];
      if (newEntity !== this._entity) {
        this._entity = newEntity;
        // Sync master volume from entity attributes
        if (this._entity?.attributes) {
          const vol = this._entity.attributes.master_volume;
          const muted = this._entity.attributes.master_muted;
          if (vol !== undefined && vol !== null) {
            this._masterVolume = Math.round(vol);
          }
          if (muted !== undefined && muted !== null) {
            this._masterMuted = muted;
          }
        }
        this.requestUpdate();
      }
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
    
    // If keys is already an array (from shortcut), use it directly (ignore latched modifiers)
    // Otherwise combine with latched modifiers (set union to avoid duplicates)
    let combo;
    if (Array.isArray(keys) && keys.length > 1) {
      // Shortcut - use as-is, ignore latched modifiers
      combo = keys;
    } else {
      // Regular key - combine with latched modifiers (set union)
      const keyArray = Array.isArray(keys) ? keys : [keys];
      combo = [...new Set([...this._latchedModifiers, ...keyArray])];
    }
    
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

  _handleDeviceVolumeChange(deviceId, volume, muted) {
    if (!this._entity || !this._isOnline) return;
    
    this.hass.callService("openctrol", "set_device_volume", {
      entity_id: this.config.entity,
      device_id: deviceId,
      volume: volume,
      muted: muted,
    }).catch((err) => {
      console.error("Failed to set device volume:", err);
    });
  }

  _handleSetDefaultDevice(deviceId) {
    if (!this._entity || !this._isOnline) return;
    
    this.hass.callService("openctrol", "set_default_output_device", {
      entity_id: this.config.entity,
      device_id: deviceId,
    }).catch((err) => {
      console.error("Failed to set default device:", err);
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

  _renderVideo() {
    return html`
      <div class="video-container">
        <canvas
          class="video-canvas"
          @ref=${(el) => {
            if (el && el !== this._videoCanvas) {
              this._videoCanvas = el;
              this._videoContext = el.getContext("2d");
              if (this._isOnline && !this._videoConnected && !this._videoConnecting) {
                this._connectVideo();
              }
            }
          }}
        ></canvas>
        ${!this._videoConnected ? html`
          <div class="video-overlay">
            ${this._videoConnecting ? html`
              <div class="video-status">Connecting to video stream...</div>
            ` : this._videoError ? html`
              <div class="video-status" style="color: #f44336;">Error: ${this._videoError}</div>
              <button class="video-button" @click=${() => this._connectVideo()}>Retry</button>
            ` : html`
              <div class="video-status">Video stream not connected</div>
              ${this._isOnline ? html`
                <button class="video-button" @click=${() => this._connectVideo()}>Connect</button>
              ` : html`
                <div class="video-status">Agent is offline</div>
              `}
            `}
          </div>
        ` : html`
          <div class="video-controls" style="position: absolute; top: 8px; right: 8px;">
            <button class="video-button" @click=${() => this._disconnectVideo()}>Disconnect</button>
          </div>
        `}
      </div>
    `;
  }

  async _connectVideo() {
    if (!this._isOnline || this._videoConnecting || this._videoConnected) {
      return;
    }

    this._videoConnecting = true;
    this._videoError = null;
    this.requestUpdate();

    try {
      const haId = this.hass.config?.location_name || this.config.entity || "home-assistant";
      
      // Create desktop session
      await this.hass.callService("openctrol", "create_desktop_session", {
        entity_id: this.config.entity,
        ha_id: haId,
        ttl_seconds: 900,
      });

      // Note: Video streaming requires WebSocket API support or entity state storage
      // for session info. This is a placeholder implementation.
      this._videoError = "Video streaming requires WebSocket API support. " +
        "Session management is implemented but connection method needs configuration.";
      this._videoConnecting = false;
      this.requestUpdate();
    } catch (err) {
      console.error("Failed to connect video:", err);
      this._videoError = err.message || "Connection failed";
      this._videoConnecting = false;
      this.requestUpdate();
    }
  }

  async _connectVideoWebSocket(websocketUrl) {
    try {
      const ws = new WebSocket(websocketUrl);
      
      ws.onopen = () => {
        this._videoConnected = true;
        this._videoConnecting = false;
        this.requestUpdate();
      };
      
      ws.onmessage = (event) => {
        const handleData = (data) => {
          if (data instanceof ArrayBuffer) {
            this._handleVideoFrame(new Uint8Array(data));
          } else if (data instanceof Blob) {
            data.arrayBuffer().then(buffer => {
              this._handleVideoFrame(new Uint8Array(buffer));
            }).catch(err => {
              console.error("Error reading blob:", err);
            });
          }
        };
        
        handleData(event.data);
      };
      
      ws.onerror = () => {
        this._videoError = "WebSocket connection error";
        this._videoConnected = false;
        this._videoConnecting = false;
        this.requestUpdate();
      };
      
      ws.onclose = () => {
        this._videoConnected = false;
        this.requestUpdate();
      };
      
      this._videoWebSocket = ws;
    } catch (err) {
      console.error("Failed to create WebSocket:", err);
      this._videoError = err.message || "Failed to create WebSocket connection";
      this._videoConnecting = false;
      this.requestUpdate();
    }
  }

  _handleVideoFrame(data) {
    if (!data || data.length < 16) return;
    
    try {
      // Parse OFRA header: "OFRA" (4 bytes) + width (4) + height (4) + format (4)
      const header = data.slice(0, 16);
      const magic = String.fromCharCode(...header.slice(0, 4));
      
      if (magic !== "OFRA") return;
      
      const view = new DataView(header.buffer, header.byteOffset, header.byteLength);
      const width = view.getUint32(4, true);
      const height = view.getUint32(8, true);
      const jpegData = data.slice(16);
      
      if (jpegData.length > 0) {
        this._renderFrame(jpegData, width, height);
      }
    } catch (err) {
      console.error("Error handling video frame:", err);
    }
  }

  async _disconnectVideo() {
    // Close WebSocket connection
    if (this._videoWebSocket) {
      this._videoWebSocket.close();
      this._videoWebSocket = null;
    }
    
    // End session via service call
    if (this._videoSessionId) {
      try {
        await this.hass.callService("openctrol", "end_desktop_session", {
          entity_id: this.config.entity,
          session_id: this._videoSessionId,
        });
      } catch (err) {
        console.error("Failed to end session:", err);
      }
      this._videoSessionId = null;
    }
    
    this._videoConnected = false;
    this.requestUpdate();
  }

  _renderFrame(jpegData, width, height) {
    if (!this._videoCanvas || !this._videoContext || !jpegData || jpegData.length === 0) {
      return;
    }

    try {
      // Convert to base64
      const binary = String.fromCharCode.apply(null, jpegData);
      const base64 = btoa(binary);
      const img = new Image();
      
      img.onload = () => {
        if (!this._videoCanvas || !this._videoContext) return;
        
        // Resize canvas to container
        const container = this._videoCanvas.parentElement;
        if (container) {
          this._videoCanvas.width = container.clientWidth;
          this._videoCanvas.height = container.clientHeight;
        }
        
        // Calculate aspect-preserving draw size
        const canvasAspect = this._videoCanvas.width / this._videoCanvas.height;
        const imgAspect = width / height;
        
        let drawWidth = this._videoCanvas.width;
        let drawHeight = this._videoCanvas.height;
        let offsetX = 0;
        let offsetY = 0;

        if (imgAspect > canvasAspect) {
          drawHeight = this._videoCanvas.width / imgAspect;
          offsetY = (this._videoCanvas.height - drawHeight) / 2;
        } else {
          drawWidth = this._videoCanvas.height * imgAspect;
          offsetX = (this._videoCanvas.width - drawWidth) / 2;
        }

        this._videoContext.clearRect(0, 0, this._videoCanvas.width, this._videoCanvas.height);
        this._videoContext.drawImage(img, offsetX, offsetY, drawWidth, drawHeight);
      };
      
      img.onerror = () => {
        console.error("Error loading video frame");
      };
      
      img.src = `data:image/jpeg;base64,${base64}`;
    } catch (err) {
      console.error("Error rendering frame:", err);
    }
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
    const monitors = this._entity?.attributes?.available_monitors || [];
    const selectedMonitorId = this._entity?.attributes?.selected_monitor_id || "";
    
    return html`
      <div class="panel">
        <div class="panel-header">
          <h3 class="panel-title">Monitor Control</h3>
          <button class="close-button" @click=${() => { this._activePanel = null; this.requestUpdate(); }}>×</button>
        </div>
        <div class="panel-section">
          <div class="section-title">Monitor Selection</div>
          ${monitors.length > 0 ? html`
            <div class="monitor-list">
              ${monitors.map(monitor => html`
                <button
                  class="monitor-button ${monitor.id === selectedMonitorId ? "active" : ""}"
                  @click=${() => this._handleSelectMonitor(monitor.id)}
                  ?disabled=${!this._isOnline}
                >
                  <div class="monitor-button-content">
                    <div class="monitor-name">${monitor.name || monitor.id}</div>
                    <div class="monitor-details">
                      ${monitor.resolution || `${monitor.width}x${monitor.height}`}
                      ${monitor.is_primary ? html`<span class="primary-badge">Primary</span>` : ""}
                    </div>
                  </div>
                  ${monitor.id === selectedMonitorId ? html`<span class="selected-indicator">✓</span>` : ""}
                </button>
              `)}
            </div>
            ${selectedMonitorId ? html`
              <div class="info-text">
                Currently selected: <strong>${selectedMonitorId}</strong>
              </div>
            ` : ""}
          ` : html`
            <div class="info-text">
              No monitors available. ${this._isOnline ? "Please wait for monitor list to load." : "Agent is offline."}
            </div>
          `}
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
    const audioDevices = this._entity?.attributes?.audio_devices || [];
    
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
              ?disabled=${!this._isOnline}
            />
            <span class="volume-value">${this._masterVolume}%</span>
            <button
              class="mute-button ${this._masterMuted ? "muted" : ""}"
              @click=${this._handleMuteToggle}
              ?disabled=${!this._isOnline}
            >
              ${this._masterMuted ? "🔇 Muted" : "🔊 Unmuted"}
            </button>
          </div>
        </div>
        <div class="panel-section">
          <div class="section-title">Audio Devices</div>
          ${audioDevices.length > 0 ? html`
            <div class="device-list">
              ${audioDevices.map(device => html`
                <div class="device-item">
                  <div class="device-header">
                    <div class="device-name">
                      ${device.name || device.id}
                      ${device.is_default ? html`<span class="default-badge">Default</span>` : ""}
                    </div>
                  </div>
                  <div class="device-controls">
                    <input
                      type="range"
                      class="volume-slider device-slider"
                      min="0"
                      max="100"
                      .value=${Math.round(device.volume || 0)}
                      @input=${(e) => {
                        const volume = parseInt(e.target.value, 10);
                        this._handleDeviceVolumeChange(device.id, volume, device.muted);
                      }}
                      ?disabled=${!this._isOnline}
                    />
                    <span class="volume-value device-volume">${Math.round(device.volume || 0)}%</span>
                    <button
                      class="mute-button device-mute ${device.muted ? "muted" : ""}"
                      @click=${() => {
                        this._handleDeviceVolumeChange(device.id, device.volume, !device.muted);
                      }}
                      ?disabled=${!this._isOnline}
                    >
                      ${device.muted ? "🔇" : "🔊"}
                    </button>
                    ${!device.is_default ? html`
                      <button
                        class="action-button set-default-button"
                        @click=${() => {
                          this._handleSetDefaultDevice(device.id);
                        }}
                        ?disabled=${!this._isOnline}
                      >
                        Set Default
                      </button>
                    ` : ""}
                  </div>
                </div>
              `)}
            </div>
          ` : html`
            <div class="info-text">
              No audio devices available. ${this._isOnline ? "Please wait for device list to load." : "Agent is offline."}
            </div>
          `}
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

    if (!this.config?.entity) {
      return html`
        <ha-card>
          <div class="error-message">
            Entity not configured. Please configure the entity in the card settings.
          </div>
        </ha-card>
      `;
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

