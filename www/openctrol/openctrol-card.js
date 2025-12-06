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
      _isMobile: { state: true },
      _mobileKeyboardVisible: { state: true },
    };
  }

  static get styles() {
    return css`
      :host {
        display: block;
        padding: 0;
        font-family: var(--ha-card-header-font-family, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Oxygen, Ubuntu, Cantarell, "Helvetica Neue", sans-serif);
      }

      .card {
        background: var(--card-background-color, white);
        border-radius: var(--ha-card-border-radius, 4px);
        box-shadow: var(--ha-card-box-shadow, 0 2px 4px rgba(0,0,0,0.1);
        overflow: hidden;
        padding: 0;
      }

      .top-bar {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 16px 24px;
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
        position: relative !important;
        width: calc(100% - 48px) !important;
        height: 400px !important;
        min-height: 400px !important;
        max-height: 400px !important;
        background: linear-gradient(135deg, #f0f0f0 0%, #e0e0e0 100%) !important;
        border: 3px solid #c8c8c8 !important;
        border-radius: 16px !important;
        margin: 24px !important;
        padding: 0 !important;
        touch-action: none !important; /* CRITICAL: Prevents browser navigation gestures (back/forward swipe) */
        -ms-touch-action: none !important; /* IE/Edge */
        user-select: none !important;
        -webkit-user-select: none !important;
        -moz-user-select: none !important;
        -ms-user-select: none !important;
        overflow: hidden !important;
        display: block !important;
        visibility: visible !important;
        opacity: 1 !important;
        z-index: 1 !important;
        box-sizing: border-box !important;
        flex-shrink: 0 !important;
        flex-grow: 0 !important;
        box-shadow: inset 0 4px 12px rgba(0,0,0,0.15), 0 2px 6px rgba(0,0,0,0.1) !important;
      }
      
      /* Ensure parent card doesn't hide touchpad */
      ha-card {
        display: block !important;
      }
      
      ha-card .card {
        display: flex !important;
        flex-direction: column !important;
        min-height: 500px !important;
      }
      
      ha-card .card > * {
        flex-shrink: 0 !important;
      }
      
      /* Force touchpad container to be visible - override any other styles */
      ha-card .touchpad-container,
      .touchpad-container,
      ha-card .card .touchpad-container {
        display: block !important;
        visibility: visible !important;
        opacity: 1 !important;
        height: 400px !important;
        min-height: 400px !important;
        max-height: 400px !important;
        width: calc(100% - 32px) !important;
        margin: 16px !important;
        padding: 0 !important;
        background: #f5f5f5 !important;
        border: 2px solid #e0e0e0 !important;
        border-radius: 8px !important;
        position: relative !important;
        box-sizing: border-box !important;
        flex-shrink: 0 !important;
        flex-grow: 0 !important;
      }
      
      /* Ensure touchpad inner element is visible */
      .touchpad-container .touchpad,
      ha-card .touchpad-container .touchpad {
        display: block !important;
        visibility: visible !important;
        width: 100% !important;
        height: 100% !important;
        position: relative !important;
        background: transparent !important;
      }

      .touchpad-container.offline {
        opacity: 0.5;
        pointer-events: none;
      }

      .touchpad {
        width: 100% !important;
        height: 100% !important;
        min-height: 100% !important;
        position: relative !important;
        display: block !important;
        visibility: visible !important;
        background: transparent !important;
        touch-action: none !important; /* CRITICAL: Prevents browser navigation gestures */
        -ms-touch-action: none !important; /* IE/Edge */
        user-select: none !important;
        -webkit-user-select: none !important;
        -moz-user-select: none !important;
        -ms-user-select: none !important;
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
        gap: 16px;
        padding: 0 24px 24px;
        justify-content: center;
        align-items: center;
        flex-wrap: wrap;
      }

      .click-button {
        flex: 2;
        min-width: 200px;
        padding: 36px 48px;
        border-radius: 20px;
        border: 2px solid #c0c0c0;
        background: linear-gradient(135deg, #f5f5f5 0%, #e5e5e5 100%);
        color: #333;
        font-size: 17px;
        font-weight: 500;
        cursor: pointer;
        transition: all 0.2s;
        display: flex;
        align-items: center;
        justify-content: center;
        gap: 12px;
        position: relative;
        box-shadow: inset 0 3px 6px rgba(0,0,0,0.12), 0 2px 4px rgba(0,0,0,0.08);
        font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
      }
      
      .scroll-move-button {
        flex: 1;
        min-width: 120px;
        max-width: 160px;
        padding: 24px 28px;
        border-radius: 14px;
        border: 2px solid var(--primary-color);
        background: white;
        color: var(--primary-color);
        font-size: 14px;
        font-weight: 500;
        cursor: pointer;
        transition: all 0.2s;
        display: flex;
        align-items: center;
        justify-content: center;
        order: 2;
      }

      .click-button:hover:not(:disabled) {
        background: var(--primary-color);
        color: white;
        transform: translateY(-2px);
        box-shadow: 0 4px 8px rgba(0,0,0,0.2);
      }

      .click-button:active:not(:disabled) {
        transform: translateY(0);
      }

      .click-button.active {
        background: var(--primary-color);
        color: white;
        box-shadow: 0 0 0 3px rgba(var(--primary-color-rgb, 0, 123, 255), 0.3);
        border-color: var(--primary-color);
      }

      .click-button:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }

      .click-button-icon {
        font-size: 24px;
        opacity: 0.8;
      }
      
      .click-button.active .click-button-icon {
        opacity: 1;
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

      .fullscreen-overlay {
        position: fixed;
        top: 0;
        left: 0;
        right: 0;
        bottom: 0;
        background: #000;
        z-index: 10000;
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
      }

      .fullscreen-video {
        width: 100%;
        height: 100%;
        object-fit: contain;
        position: absolute;
        top: 0;
        left: 0;
      }

      .fullscreen-controls {
        position: absolute;
        top: 16px;
        right: 16px;
        display: flex;
        gap: 8px;
        z-index: 10001;
      }

      .fullscreen-touchpad-overlay {
        position: absolute;
        bottom: 80px;
        left: 50%;
        transform: translateX(-50%);
        width: 400px;
        height: 300px;
        background: rgba(245, 245, 245, 0.9);
        border: 2px solid #e0e0e0;
        border-radius: 8px;
        touch-action: none;
        user-select: none;
        z-index: 10001;
        display: flex;
        align-items: center;
        justify-content: center;
        color: #666;
        font-size: 14px;
      }

      .fullscreen-bottom-controls {
        position: absolute;
        bottom: 16px;
        left: 50%;
        transform: translateX(-50%);
        display: flex;
        gap: 12px;
        z-index: 10001;
      }

      .fullscreen-button {
        padding: 12px 24px;
        border-radius: 8px;
        border: 2px solid rgba(255, 255, 255, 0.3);
        background: rgba(0, 0, 0, 0.7);
        color: white;
        font-size: 14px;
        cursor: pointer;
        transition: all 0.2s;
      }

      .fullscreen-button:hover {
        background: rgba(0, 0, 0, 0.9);
        border-color: rgba(255, 255, 255, 0.5);
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
    this._fullscreenOpen = false;
    this._fullscreenCanvas = null;
    this._fullscreenContext = null;
    this._fullscreenWebSocket = null;
    this._fullscreenSessionId = null;
    this._fullscreenDrawRegion = null; // Store draw region for touch coordinate mapping
    this._videoDrawRegion = null; // Store draw region for regular video canvas
    this._leftClickHeld = false; // Track left click toggle state
    this._rightClickHeld = false; // Track right click toggle state
    this._leftClickTimeouts = new Map(); // Track left click timeouts for long press
    this._rightClickTimeouts = new Map(); // Track right click timeouts for long press
    
    // Detect mobile device
    this._isMobile = this._detectMobile();
    this._mobileKeyboardVisible = false;
    this._mobileKeyboardInput = null;
  }
  
  _detectMobile() {
    // Check for touch support, screen size, and user agent
    const hasTouch = 'ontouchstart' in window || navigator.maxTouchPoints > 0;
    const isSmallScreen = window.innerWidth < 768;
    const userAgent = navigator.userAgent || navigator.vendor || window.opera;
    const isMobileUA = /android|webos|iphone|ipad|ipod|blackberry|iemobile|opera mini/i.test(userAgent.toLowerCase());
    
    return hasTouch && (isSmallScreen || isMobileUA);
  }
  
  firstUpdated() {
    // Setup mobile keyboard input element
    if (this._isMobile) {
      this._setupMobileKeyboard();
    }
  }
  
  _setupMobileKeyboard() {
    // Create hidden input for mobile keyboard
    const input = document.createElement("input");
    input.type = "text";
    input.style.position = "fixed";
    input.style.top = "-1000px";
    input.style.left = "-1000px";
    input.style.opacity = "0";
    input.style.pointerEvents = "none";
    input.autocomplete = "off";
    input.autocorrect = "off";
    input.autocapitalize = "off";
    input.spellcheck = false;
    
    input.addEventListener("keydown", (e) => {
      e.preventDefault();
      this._handleMobileKeyboardInput(e);
    });
    
    input.addEventListener("keyup", (e) => {
      e.preventDefault();
    });
    
    input.addEventListener("blur", () => {
      this._mobileKeyboardVisible = false;
      this.requestUpdate();
    });
    
    document.body.appendChild(input);
    this._mobileKeyboardInput = input;
  }
  
  _handleMobileKeyboardInput(e) {
    if (!this._isOnline) return;
    
    let keyName = null;
    
    // Map key codes to key names
    switch (e.key) {
      case " ":
        keyName = "SPACE";
        break;
      case "Backspace":
        keyName = "BACKSPACE";
        break;
      case "Enter":
        keyName = "ENTER";
        break;
      case "Escape":
      case "Esc":
        keyName = "ESC";
        break;
      case "Tab":
        keyName = "TAB";
        break;
      case "Delete":
        keyName = "DEL";
        break;
      default:
        // Handle letter and number keys
        if (e.key.length === 1) {
          const upperKey = e.key.toUpperCase();
          if ((upperKey >= "A" && upperKey <= "Z") || (upperKey >= "0" && upperKey <= "9")) {
            keyName = upperKey;
          }
        }
        break;
    }
    
    if (keyName) {
      // Combine with latched modifiers
      const combo = [...new Set([...this._latchedModifiers, keyName])];
      this._sendKeyCombo(combo);
    }
    
    // Clear input to prevent text from appearing
    if (this._mobileKeyboardInput) {
      this._mobileKeyboardInput.value = "";
    }
  }
  
  _toggleMobileKeyboard() {
    if (!this._mobileKeyboardInput) return;
    
    if (this._mobileKeyboardVisible) {
      this._mobileKeyboardInput.blur();
      this._mobileKeyboardVisible = false;
    } else {
      this._mobileKeyboardInput.focus();
      this._mobileKeyboardVisible = true;
    }
    this.requestUpdate();
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
    return "openctrol";
  }
  
  get _statusColor() {
    return this._isOnline ? "#4caf50" : "#f44336"; // Green for online, red for offline
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
      totalDistance: 0, // Track total movement distance for pan detection
    };
    this._lastMove = { x: touch.clientX, y: touch.clientY };
  }

  _handleTouchMove(e) {
    if (!this._isOnline || !this._touchStart) return;
    
    e.preventDefault();
    e.stopPropagation(); // Prevent browser navigation gestures
    const touch = e.touches[0] || e.changedTouches[0];
    const touchCount = e.touches.length;
    const dx = touch.clientX - this._lastMove.x;
    const dy = touch.clientY - this._lastMove.y;
    const distance = Math.sqrt(dx * dx + dy * dy);
    
    // Accumulate total movement distance
    if (this._touchStart) {
      this._touchStart.totalDistance += distance;
    }
    
    // Movement threshold: if moved more than 5 pixels, consider it a pan
    if (this._touchStart.totalDistance > 5 || Math.abs(dx) > 2 || Math.abs(dy) > 2) {
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
    
    e.preventDefault();
    e.stopPropagation(); // Prevent browser navigation gestures
    const touch = e.changedTouches[0];
    const duration = Date.now() - this._touchStart.time;
    const moved = this._touchStart.moved;
    // BUG FIX: Use touchCount from touchStart, not from changedTouches
    // When two fingers are lifted, there are two separate touchend events,
    // each with changedTouches.length = 1, so we need the original count
    const touchCount = this._touchStart.touchCount || 1;
    const totalDistance = this._touchStart.totalDistance || 0;
    
    // Clear move throttle
    if (this._moveThrottle) {
      clearTimeout(this._moveThrottle);
      this._moveThrottle = null;
    }
    
    // Differentiate between tap, long press, and pan:
    // - Tap: < 300ms, < 5px movement, single finger
    // - Long press: >= 300ms and < 1000ms, < 5px movement, single finger (right click)
    // - Pan: any movement > 5px (already handled in _handleTouchMove)
    
    if (!moved && totalDistance < 5) {
      // No significant movement - could be tap or long press
      if (touchCount === 2 && duration < 300) {
        // Two-finger tap = right click
        this._sendPointerEvent("click", null, null, "right");
      } else if (duration < 300) {
        // Single tap (< 300ms) = left click
        this._sendPointerEvent("click", null, null, "left");
      } else if (duration >= 300 && duration < 1000) {
        // Long press (300-1000ms) = right click
        this._sendPointerEvent("click", null, null, "right");
      }
      // If duration >= 1000ms, ignore (likely accidental hold)
    }
    // If moved or totalDistance >= 5, it was a pan - already handled in _handleTouchMove
    
    this._touchStart = null;
    this._lastMove = null;
  }

  async _sendPointerEvent(type, dx, dy, button) {
    if (!this._entity || !this._isOnline) {
      console.warn("Cannot send pointer event: entity not available or offline");
      return;
    }
    
    const data = {
      entity_id: this.config.entity,
      type: type,
    };
    
    if (dx !== null && dy !== null) {
      // Ensure dx and dy are numbers (not strings) and round to integers
      data.dx = Math.round(typeof dx === 'number' ? dx : parseFloat(dx) || 0);
      data.dy = Math.round(typeof dy === 'number' ? dy : parseFloat(dy) || 0);
    }
    
    if (button) {
      data.button = button;
    }
    
    // Retry logic for failed events
    const maxRetries = 3;
    let lastError = null;
    
    for (let attempt = 0; attempt < maxRetries; attempt++) {
      try {
        await this.hass.callService("openctrol", "send_pointer_event", data);
        return; // Success, exit retry loop
      } catch (err) {
        lastError = err;
        const errorMsg = err.message || err.toString() || "Unknown error";
        if (attempt < maxRetries - 1) {
          // Wait before retry (exponential backoff)
          await new Promise(resolve => setTimeout(resolve, 100 * (attempt + 1)));
        } else {
          // Log error on final failure
          console.error(`Failed to send pointer event (${type}) after ${maxRetries} attempts:`, errorMsg);
          // Only show alert for critical errors, not for every move event
          if (type === "click" || type === "button") {
            console.warn("Click/button event failed - this may indicate a connection issue");
          }
        }
      }
    }
  }

  async _sendPointerEventAbsolute(normalizedX, normalizedY) {
    // Send absolute pointer move with normalized 0-65535 coordinates
    if (!this._entity || !this._isOnline) {
      console.warn("Cannot send absolute pointer event: entity not available or offline");
      return;
    }
    
    const data = {
      entity_id: this.config.entity,
      type: "move",
      x: Math.round(normalizedX),
      y: Math.round(normalizedY),
      absolute: true, // Indicate this is absolute positioning
    };
    
    // Retry logic for failed events
    const maxRetries = 3;
    let lastError = null;
    
    for (let attempt = 0; attempt < maxRetries; attempt++) {
      try {
        await this.hass.callService("openctrol", "send_pointer_event", data);
        return; // Success, exit retry loop
      } catch (err) {
        lastError = err;
        const errorMsg = err.message || err.toString() || "Unknown error";
        if (attempt < maxRetries - 1) {
          await new Promise(resolve => setTimeout(resolve, 100 * (attempt + 1)));
        } else {
          console.error(`Failed to send absolute pointer event after ${maxRetries} attempts:`, errorMsg);
        }
      }
    }
  }

  async _sendPointerButton(button, action) {
    // Send pointer button down/up for toggle functionality
    if (!this._entity || !this._isOnline) {
      console.warn("Cannot send pointer button: entity not available or offline");
      return;
    }
    
    const data = {
      entity_id: this.config.entity,
      type: "button",
      button: button,
      action: action, // Use explicit action parameter instead of dx hack
    };
    
    try {
      await this.hass.callService("openctrol", "send_pointer_event", data);
    } catch (err) {
      console.error(`Failed to send pointer button ${action}:`, err);
      // Don't alert for every button event - just log
    }
  }

  async _sendClickAction(button) {
    // Send a complete click action (down + up) for normal button press
    if (!this._entity || !this._isOnline) {
      return;
    }
    
    // Send click event (which sends both down and up)
    await this._sendPointerEvent("click", null, null, button);
  }

  async _sendKeyCombo(keys) {
    if (!this._entity || !this._isOnline) {
      console.warn("Cannot send key combo: entity not available or offline");
      return; // Don't alert, just return silently
    }
    
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
    
    // Retry logic for failed key events
    const maxRetries = 3;
    let lastError = null;
    
    for (let attempt = 0; attempt < maxRetries; attempt++) {
      try {
        await this.hass.callService("openctrol", "send_key_combo", {
          entity_id: this.config.entity,
          keys: combo,
        });
        return; // Success, exit retry loop
      } catch (err) {
        lastError = err;
        const errorMsg = err.message || err.toString() || "Unknown error";
        if (attempt < maxRetries - 1) {
          // Wait before retry (exponential backoff)
          await new Promise(resolve => setTimeout(resolve, 100 * (attempt + 1)));
        } else {
          // Log error on final failure
          console.error(`Failed to send key combo (${combo.join('+')}) after ${maxRetries} attempts:`, errorMsg);
          // Show warning for keyboard events as they're user-initiated
          console.warn("Keyboard input failed - this may indicate a WebSocket connection issue");
        }
      }
    }
  }

  _handleKeyButton(key, isShortcut = false) {
    if (isShortcut) {
      // Shortcuts ignore latched modifiers
      this._sendKeyCombo(key);
      return;
    }
    
    // Regular key - send with latched modifiers
    const combo = [...new Set([...this._latchedModifiers, key])];
    this._sendKeyCombo(combo);
  }

  _handleKeyHold(key) {
    // Long press on modifier key toggles latch (without sending the key)
    const isModifier = ["CTRL", "ALT", "SHIFT", "WIN"].includes(key);
    if (isModifier) {
      // Toggle latch state for modifier keys
      this._toggleModifierLatch(key);
      console.log(`Modifier ${key} latch toggled. Latched modifiers:`, Array.from(this._latchedModifiers));
    } else {
      // For non-modifier keys, long press sends the key repeatedly
      // But we don't want to spam, so just send once
      this._sendKeyCombo([key]);
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

  async _handleSelectMonitor(monitorId) {
    console.log("_handleSelectMonitor called with:", monitorId);
    if (!this._entity || !this._isOnline) {
      console.warn("Cannot select monitor: entity=", this._entity, "online=", this._isOnline);
      alert("Cannot select monitor: Agent is offline");
      return;
    }
    
    if (!monitorId) {
      console.error("Monitor ID is required");
      alert("Monitor ID is required");
      return;
    }
    
    console.log("Selecting monitor:", monitorId, "entity:", this.config.entity);
    try {
      await this.hass.callService("openctrol", "select_monitor", {
        entity_id: this.config.entity,
        monitor_id: monitorId,
      });
      console.log("Monitor selection service call succeeded");
      
      // Wait a moment for the service to process and entity to update
      await new Promise(resolve => setTimeout(resolve, 300));
      
      // Refresh entity to update monitor selection (multiple attempts for reliability)
      for (let i = 0; i < 3; i++) {
        try {
          await this.hass.callService("homeassistant", "update_entity", {
            entity_id: this.config.entity,
          });
          // Small delay between refresh attempts
          await new Promise(resolve => setTimeout(resolve, 200));
        } catch (refreshErr) {
          console.warn("Entity refresh attempt", i + 1, "failed:", refreshErr);
        }
      }
      
      // Update local state to reflect changes immediately
      this.requestUpdate();
      
      // Force another update after a short delay to ensure UI reflects the change
      setTimeout(() => {
        this.requestUpdate();
      }, 500);
    } catch (err) {
      console.error("Failed to select monitor:", err);
      const errorMsg = err.message || err.toString() || "Unknown error";
      alert(`Failed to select monitor: ${errorMsg}`);
    }
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

  async _handleSetDefaultDevice(deviceId) {
    if (!this._entity || !this._isOnline) {
      alert("Cannot set default device: Agent is offline");
      return;
    }
    
    if (!deviceId) {
      alert("Device ID is required");
      return;
    }
    
    // Validate entity_id is in correct format (should be like "sensor.openctrol_...")
    const entityId = this.config?.entity;
    if (!entityId || !entityId.includes('.')) {
      console.error("Invalid entity ID format:", entityId);
      alert("Configuration error: Invalid entity ID. Please reconfigure the card.");
      return;
    }
    
    try {
      await this.hass.callService("openctrol", "set_default_output_device", {
        entity_id: entityId,
        device_id: deviceId,
      });
      // Refresh entity to update default device status
      await this.hass.callService("homeassistant", "update_entity", {
        entity_id: this.config.entity,
      });
      // Note: Service may not raise error even if device change fails (some systems don't support it)
      // The entity refresh will show the current default device status
    } catch (err) {
      console.error("Failed to set default device:", err);
      let errorMsg = "";
      if (err && typeof err === 'object') {
        errorMsg = err.message || err.detail || JSON.stringify(err);
      } else {
        errorMsg = String(err || "Unknown error");
      }
      
      // Provide user-friendly error message
      if (errorMsg.includes("invalid entity ID") || errorMsg.includes("Entity ID")) {
        alert(`Configuration error: Please ensure the card is configured with a valid entity ID (e.g., sensor.openctrol_agent_status). Error: ${errorMsg}`);
      } else if (errorMsg.includes("admin") || errorMsg.includes("privilege") || errorMsg.includes("COM")) {
        alert(`Failed to set default device: Some systems don't support programmatic device changes. You may need to change the default device in Windows Sound Settings. Error: ${errorMsg}`);
      } else {
        alert(`Failed to set default device: ${errorMsg}`);
      }
    }
  }

  _renderTopBar() {
    return html`
      <div class="top-bar">
        <div class="top-bar-left">
          ${this._isMobile ? html`
            <button
              class="icon-button"
              @click=${() => this._toggleMobileKeyboard()}
              title=${this._mobileKeyboardVisible ? "Hide Keyboard" : "Show Keyboard"}
            >
              ⌨
            </button>
          ` : ""}
          <h2 class="title" style="display: flex; align-items: center; gap: 8px;">
            ${this._entityTitle}
            <div class="status-indicator" style="width: 18px; height: 18px; border-radius: 50%; background: ${this._statusColor}; box-shadow: 0 0 6px ${this._statusColor}; flex-shrink: 0;"></div>
            ${this._isDegraded ? html`<span class="status-pill status-degraded" style="font-size: 10px; padding: 2px 6px;">DEGRADED</span>` : ""}
          </h2>
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
    // Always render touchpad - it's a core feature
    return html`
      <div 
        class="touchpad-container ${!this._isOnline ? "offline" : ""}" 
        style="display: block !important; visibility: visible !important; opacity: ${!this._isOnline ? '0.5' : '1'} !important; height: 400px !important; min-height: 400px !important; width: calc(100% - 32px) !important; margin: 16px !important; background: #f5f5f5 !important; border: 2px solid #e0e0e0 !important; border-radius: 8px !important; position: relative !important; box-sizing: border-box !important;"
      >
        <div
          class="touchpad"
          style="width: 100%; height: 100%; position: relative;"
          @touchstart=${this._handleTouchStart}
          @touchmove=${this._handleTouchMove}
          @touchend=${this._handleTouchEnd}
          @touchcancel=${this._handleTouchEnd}
          @mousedown=${(e) => {
            // Support mouse events for desktop
            if (!this._isOnline) return;
            this._touchStart = {
              x: e.clientX,
              y: e.clientY,
              time: Date.now(),
              moved: false,
              touchCount: 1,
            };
            this._lastMove = { x: e.clientX, y: e.clientY };
          }}
          @mousemove=${(e) => {
            if (!this._isOnline || !this._touchStart) return;
            e.preventDefault();
            e.stopPropagation();
            const dx = e.clientX - this._lastMove.x;
            const dy = e.clientY - this._lastMove.y;
            if (Math.abs(dx) > 2 || Math.abs(dy) > 2) {
              this._touchStart.moved = true;
              this._lastMove = { x: e.clientX, y: e.clientY };
              if (this._moveThrottle) {
                clearTimeout(this._moveThrottle);
              }
              this._moveThrottle = setTimeout(() => {
                if (this._scrollMode) {
                  this._sendPointerEvent("scroll", dx, dy);
                } else {
                  this._sendPointerEvent("move", dx, dy);
                }
                this._moveThrottle = null;
              }, 16);
            }
          }}
          @mouseup=${(e) => {
            if (!this._isOnline || !this._touchStart) return;
            const duration = Date.now() - this._touchStart.time;
            const moved = this._touchStart.moved;
            if (!moved && duration < 300) {
              this._sendPointerEvent("click", null, null, e.button === 2 ? "right" : e.button === 1 ? "middle" : "left");
            }
            this._touchStart = null;
            this._lastMove = null;
            if (this._moveThrottle) {
              clearTimeout(this._moveThrottle);
              this._moveThrottle = null;
            }
          }}
          @contextmenu=${(e) => e.preventDefault()}
        >
          ${!this._isOnline ? html`
            <div class="touchpad-overlay">
              Offline - Touchpad disabled
            </div>
          ` : html`
            <div class="touchpad-overlay" style="opacity: 0.3; pointer-events: none;">
              Touchpad Area - Drag to move, tap to click
            </div>
          `}
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
        
        // Get container dimensions (actual visible area)
        const container = this._videoCanvas.parentElement;
        if (!container) return;
        
        const containerWidth = container.clientWidth;
        const containerHeight = container.clientHeight;
        
        // Use the decoded image's actual dimensions (not the frame header dimensions)
        // This ensures we use the active pixels, accounting for any JPEG encoding quirks
        const imgWidth = img.naturalWidth || img.width || width;
        const imgHeight = img.naturalHeight || img.height || height;
        
        // Calculate aspect ratios using actual image dimensions vs container
        const imgAspect = imgWidth / imgHeight;
        const containerAspect = containerWidth / containerHeight;
        
        // Set canvas to match container size exactly (no scaling)
        // This ensures touch coordinates map 1:1 to the video feed
        this._videoCanvas.width = containerWidth;
        this._videoCanvas.height = containerHeight;
        
        // Calculate draw size to maintain aspect ratio while filling container
        // Use letterboxing/pillarboxing to preserve aspect ratio
        let drawWidth = containerWidth;
        let drawHeight = containerHeight;
        let offsetX = 0;
        let offsetY = 0;

        if (imgAspect > containerAspect) {
          // Image is wider than container - letterbox (black bars top/bottom)
          drawHeight = containerWidth / imgAspect;
          offsetY = (containerHeight - drawHeight) / 2;
        } else {
          // Image is taller than container - pillarbox (black bars left/right)
          drawWidth = containerHeight * imgAspect;
          offsetX = (containerWidth - drawWidth) / 2;
        }

        // Clear entire canvas (including black bars)
        this._videoContext.fillStyle = "#000";
        this._videoContext.fillRect(0, 0, containerWidth, containerHeight);
        
        // Draw image centered with aspect ratio preserved
        this._videoContext.drawImage(img, offsetX, offsetY, drawWidth, drawHeight);
        
        // Store draw region for touch coordinate mapping
        // Touch coordinates should map to the actual image area, not black bars
        this._videoDrawRegion = {
          x: offsetX,
          y: offsetY,
          width: drawWidth,
          height: drawHeight,
          imgWidth: imgWidth,
          imgHeight: imgHeight
        };
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
          class="click-button ${this._leftClickHeld ? "active" : ""}"
          @mousedown=${(e) => {
            e.preventDefault();
            e.stopPropagation();
            if (!this._isOnline) return;
            
            // Cancel any existing timeout to prevent race conditions
            const existingTimeout = this._leftClickTimeouts?.get("left");
            if (existingTimeout) {
              clearTimeout(existingTimeout);
            }
            
            if (!this._leftClickTimeouts) {
              this._leftClickTimeouts = new Map();
            }
            
            // Start long press timer - reduced to 300ms for better responsiveness
            const timeout = setTimeout(() => {
              // Long press - latch the button
              if (!this._leftClickHeld && this._isOnline) {
                this._leftClickHeld = true;
                this._sendPointerButton("left", "down").catch(err => {
                  console.error("Failed to send pointer button down:", err);
                });
                this.requestUpdate();
              }
              if (this._leftClickTimeouts) {
                this._leftClickTimeouts.delete("left");
              }
            }, 300); // 300ms for long press (reduced from 500ms)
            
            this._leftClickTimeouts.set("left", timeout);
          }}
          @mouseup=${(e) => {
            e.preventDefault();
            e.stopPropagation();
            
            const timeout = this._leftClickTimeouts?.get("left");
            if (timeout) {
              clearTimeout(timeout);
              this._leftClickTimeouts.delete("left");
              
              // Short press - send normal click action (unless already latched)
              if (!this._leftClickHeld) {
                // Send complete click action
                this._sendClickAction("left");
              }
            } else if (this._leftClickHeld) {
              // Button is latched - unlatch on click
              this._leftClickHeld = false;
              this._sendPointerButton("left", "up");
              this.requestUpdate();
            }
          }}
          @mouseleave=${() => {
            // Cancel long press if mouse leaves
            const timeout = this._leftClickTimeouts?.get("left");
            if (timeout) {
              clearTimeout(timeout);
              this._leftClickTimeouts.delete("left");
              
              // If not latched, send click on leave (user pressed and released quickly)
              if (!this._leftClickHeld) {
                this._sendClickAction("left");
              }
            }
          }}
          @touchstart=${(e) => {
            e.preventDefault();
            e.stopPropagation();
            if (!this._isOnline) return;
            
            // Cancel any existing timeout to prevent race conditions
            const existingTimeout = this._leftClickTimeouts?.get("left");
            if (existingTimeout) {
              clearTimeout(existingTimeout);
            }
            
            if (!this._leftClickTimeouts) {
              this._leftClickTimeouts = new Map();
            }
            
            const timeout = setTimeout(() => {
              // Long press - latch
              if (!this._leftClickHeld && this._isOnline) {
                this._leftClickHeld = true;
                this._sendPointerButton("left", "down").catch(err => {
                  console.error("Failed to send pointer button down:", err);
                });
                this.requestUpdate();
              }
              if (this._leftClickTimeouts) {
                this._leftClickTimeouts.delete("left");
              }
            }, 300); // 300ms for long press
            
            this._leftClickTimeouts.set("left", timeout);
          }}
          @touchend=${(e) => {
            e.preventDefault();
            e.stopPropagation();
            
            const timeout = this._leftClickTimeouts?.get("left");
            if (timeout) {
              clearTimeout(timeout);
              this._leftClickTimeouts.delete("left");
              
              if (!this._leftClickHeld) {
                // Short tap - send click
                this._sendClickAction("left");
              }
            } else if (this._leftClickHeld) {
              // Latched - unlatch on second tap
              this._leftClickHeld = false;
              this._sendPointerButton("left", "up");
              this.requestUpdate();
            }
          }}
          ?disabled=${!this._isOnline}
        >
          <span class="click-button-icon">🖱</span>
          ${this._leftClickHeld ? "Release" : "Left Click"}
        </button>
        <button
          class="scroll-move-button ${this._scrollMode ? "active" : ""}"
          @click=${() => { this._scrollMode = !this._scrollMode; this.requestUpdate(); }}
          ?disabled=${!this._isOnline}
        >
          ${this._scrollMode ? "Scroll" : "Move"}
        </button>
        <button
          class="click-button ${this._rightClickHeld ? "active" : ""}"
          @mousedown=${(e) => {
            e.preventDefault();
            e.stopPropagation();
            if (!this._isOnline) return;
            
            // Cancel any existing timeout to prevent race conditions
            const existingTimeout = this._rightClickTimeouts?.get("right");
            if (existingTimeout) {
              clearTimeout(existingTimeout);
            }
            
            if (!this._rightClickTimeouts) {
              this._rightClickTimeouts = new Map();
            }
            
            const timeout = setTimeout(() => {
              if (!this._rightClickHeld && this._isOnline) {
                this._rightClickHeld = true;
                this._sendPointerButton("right", "down").catch(err => {
                  console.error("Failed to send pointer button down:", err);
                });
                this.requestUpdate();
              }
              if (this._rightClickTimeouts) {
                this._rightClickTimeouts.delete("right");
              }
            }, 300); // 300ms for long press (reduced from 500ms)
            
            this._rightClickTimeouts.set("right", timeout);
          }}
          @mouseup=${(e) => {
            e.preventDefault();
            e.stopPropagation();
            
            const timeout = this._rightClickTimeouts?.get("right");
            if (timeout) {
              clearTimeout(timeout);
              this._rightClickTimeouts.delete("right");
              
              if (!this._rightClickHeld) {
                this._sendClickAction("right");
              }
            } else if (this._rightClickHeld) {
              this._rightClickHeld = false;
              this._sendPointerButton("right", "up");
              this.requestUpdate();
            }
          }}
          @mouseleave=${() => {
            const timeout = this._rightClickTimeouts?.get("right");
            if (timeout) {
              clearTimeout(timeout);
              this._rightClickTimeouts.delete("right");
              
              if (!this._rightClickHeld) {
                this._sendClickAction("right");
              }
            }
          }}
          @touchstart=${(e) => {
            e.preventDefault();
            e.stopPropagation();
            if (!this._isOnline) return;
            
            // Cancel any existing timeout to prevent race conditions
            const existingTimeout = this._rightClickTimeouts?.get("right");
            if (existingTimeout) {
              clearTimeout(existingTimeout);
            }
            
            if (!this._rightClickTimeouts) {
              this._rightClickTimeouts = new Map();
            }
            
            const timeout = setTimeout(() => {
              if (!this._rightClickHeld && this._isOnline) {
                this._rightClickHeld = true;
                this._sendPointerButton("right", "down").catch(err => {
                  console.error("Failed to send pointer button down:", err);
                });
                this.requestUpdate();
              }
              if (this._rightClickTimeouts) {
                this._rightClickTimeouts.delete("right");
              }
            }, 300); // 300ms for long press (reduced from 500ms)
            
            this._rightClickTimeouts.set("right", timeout);
          }}
          @touchend=${(e) => {
            e.preventDefault();
            e.stopPropagation();
            
            const timeout = this._rightClickTimeouts?.get("right");
            if (timeout) {
              clearTimeout(timeout);
              this._rightClickTimeouts.delete("right");
              
              if (!this._rightClickHeld) {
                this._sendClickAction("right");
              }
            } else if (this._rightClickHeld) {
              this._rightClickHeld = false;
              this._sendPointerButton("right", "up");
              this.requestUpdate();
            }
          }}
          ?disabled=${!this._isOnline}
        >
          <span class="click-button-icon">🖱</span>
          ${this._rightClickHeld ? "Release" : "Right Click"}
        </button>
      </div>
    `;
  }

  async _openFullscreenMonitor(monitorId) {
    if (!this._isOnline) {
      alert("Agent is offline. Cannot open monitor view.");
      return;
    }

    // Ensure monitor is selected before opening fullscreen
    if (monitorId) {
      try {
        await this._handleSelectMonitor(monitorId);
        // Wait a bit longer for monitor selection to propagate
        await new Promise(resolve => setTimeout(resolve, 800));
      } catch (err) {
        console.error("Failed to select monitor:", err);
        // Continue anyway - user may have already selected it
      }
    }

    this._fullscreenOpen = true;
    this.requestUpdate();

    // Create fullscreen overlay
    const overlay = document.createElement("div");
    overlay.className = "fullscreen-overlay";
    overlay.id = "openctrol-fullscreen-overlay";

    // Create video canvas
    const canvas = document.createElement("canvas");
    canvas.className = "fullscreen-video";
    canvas.id = "openctrol-fullscreen-canvas";
    this._fullscreenCanvas = canvas;
    this._fullscreenContext = canvas.getContext("2d");
    overlay.appendChild(canvas);

    // Create touchpad overlay
    const touchpadOverlay = document.createElement("div");
    touchpadOverlay.className = "fullscreen-touchpad-overlay";
    touchpadOverlay.innerHTML = "Touchpad Area - Drag to move, tap to click";
    this._setupFullscreenTouchpad(touchpadOverlay);
    overlay.appendChild(touchpadOverlay);

    // Create controls
    const controls = document.createElement("div");
    controls.className = "fullscreen-controls";
    controls.innerHTML = `
      <button class="fullscreen-button" id="openctrol-fullscreen-keyboard">⌨ Keyboard</button>
      <button class="fullscreen-button" id="openctrol-fullscreen-close">✕ Close</button>
    `;
    overlay.appendChild(controls);

    // Create bottom controls
    const bottomControls = document.createElement("div");
    bottomControls.className = "fullscreen-bottom-controls";
    bottomControls.innerHTML = `
      <button class="fullscreen-button" id="openctrol-fullscreen-left">Left Click</button>
      <button class="fullscreen-button" id="openctrol-fullscreen-right">Right Click</button>
      <button class="fullscreen-button" id="openctrol-fullscreen-middle">Middle Click</button>
    `;
    overlay.appendChild(bottomControls);

    document.body.appendChild(overlay);

    // Setup event handlers
    document.getElementById("openctrol-fullscreen-close").addEventListener("click", () => {
      this._closeFullscreenMonitor();
    });
    document.getElementById("openctrol-fullscreen-keyboard").addEventListener("click", () => {
      this._activePanel = "keyboard";
      this.requestUpdate();
    });
    document.getElementById("openctrol-fullscreen-left").addEventListener("click", () => {
      this._sendPointerEvent("click", null, null, "left");
    });
    document.getElementById("openctrol-fullscreen-right").addEventListener("click", () => {
      this._sendPointerEvent("click", null, null, "right");
    });
    document.getElementById("openctrol-fullscreen-middle").addEventListener("click", () => {
      this._sendPointerEvent("click", null, null, "middle");
    });

    // Connect video stream
    await this._connectFullscreenVideo();

    // Resize canvas to fit screen
    this._resizeFullscreenCanvas();
    window.addEventListener("resize", () => this._resizeFullscreenCanvas());
  }

  _setupFullscreenTouchpad(element) {
    let touchStart = null;
    let lastMove = null;
    let moveThrottle = null;

    const handleStart = (e) => {
      const point = e.touches ? e.touches[0] : e;
      touchStart = {
        x: point.clientX,
        y: point.clientY,
        time: Date.now(),
        moved: false,
      };
      lastMove = { x: point.clientX, y: point.clientY };
    };

    const handleMove = (e) => {
      if (!touchStart) return;
      e.preventDefault();
      e.stopPropagation(); // Prevent browser navigation gestures
      const point = e.touches ? e.touches[0] : e;
      
      // Calculate normalized absolute coordinates (0-65535) based on touch position
      // relative to the fullscreen canvas, mapping to the actual image area (not black bars)
      const canvas = this._fullscreenCanvas;
      if (canvas && this._fullscreenDrawRegion) {
        const rect = canvas.getBoundingClientRect();
        const drawRegion = this._fullscreenDrawRegion;
        
        // Calculate touch position relative to canvas
        const touchX = point.clientX - rect.left;
        const touchY = point.clientY - rect.top;
        
        // Map touch coordinates to the actual image draw region (excluding black bars)
        // If touch is outside the draw region, clamp to the nearest edge
        const relativeX = Math.max(0, Math.min(drawRegion.width, touchX - drawRegion.x));
        const relativeY = Math.max(0, Math.min(drawRegion.height, touchY - drawRegion.y));
        
        // Normalize to 0-65535 based on the image dimensions
        const normalizedX = Math.round((relativeX / drawRegion.width) * 65535);
        const normalizedY = Math.round((relativeY / drawRegion.height) * 65535);
        
        // Clamp to valid range
        const clampedX = Math.max(0, Math.min(65535, normalizedX));
        const clampedY = Math.max(0, Math.min(65535, normalizedY));
        
        const dx = point.clientX - lastMove.x;
        const dy = point.clientY - lastMove.y;
        if (Math.abs(dx) > 2 || Math.abs(dy) > 2) {
          touchStart.moved = true;
          lastMove = { x: point.clientX, y: point.clientY };
          if (moveThrottle) clearTimeout(moveThrottle);
          moveThrottle = setTimeout(() => {
            // Send absolute move with normalized coordinates
            this._sendPointerEventAbsolute(clampedX, clampedY);
            moveThrottle = null;
          }, 16);
        }
      } else {
        // Fallback to relative moves if canvas or draw region not available
        const dx = point.clientX - lastMove.x;
        const dy = point.clientY - lastMove.y;
        if (Math.abs(dx) > 2 || Math.abs(dy) > 2) {
          touchStart.moved = true;
          lastMove = { x: point.clientX, y: point.clientY };
          if (moveThrottle) clearTimeout(moveThrottle);
          moveThrottle = setTimeout(() => {
            this._sendPointerEvent("move", dx, dy);
            moveThrottle = null;
          }, 16);
        }
      }
    };

    const handleEnd = (e) => {
      if (!touchStart) return;
      const point = e.changedTouches ? e.changedTouches[0] : e;
      const duration = Date.now() - touchStart.time;
      const moved = touchStart.moved;
      
      // For taps, calculate absolute position for click
      // Map to the actual image area (not black bars)
      if (!moved && duration < 300) {
        const canvas = this._fullscreenCanvas;
        if (canvas && this._fullscreenDrawRegion) {
          const rect = canvas.getBoundingClientRect();
          const drawRegion = this._fullscreenDrawRegion;
          
          // Calculate touch position relative to canvas
          const touchX = point.clientX - rect.left;
          const touchY = point.clientY - rect.top;
          
          // Map touch coordinates to the actual image draw region (excluding black bars)
          const relativeX = Math.max(0, Math.min(drawRegion.width, touchX - drawRegion.x));
          const relativeY = Math.max(0, Math.min(drawRegion.height, touchY - drawRegion.y));
          
          // Normalize to 0-65535 based on the image dimensions
          const normalizedX = Math.round((relativeX / drawRegion.width) * 65535);
          const normalizedY = Math.round((relativeY / drawRegion.height) * 65535);
          const clampedX = Math.max(0, Math.min(65535, normalizedX));
          const clampedY = Math.max(0, Math.min(65535, normalizedY));
          
          // BUG FIX: Await the absolute move before sending click
          // _sendPointerEventAbsolute is async and has retry logic (up to 300ms),
          // so we must await it to ensure cursor is positioned before clicking
          (async () => {
            try {
              await this._sendPointerEventAbsolute(clampedX, clampedY);
              // Small delay to ensure cursor positioning is processed
              await new Promise(resolve => setTimeout(resolve, 50));
              const button = e.button === 2 ? "right" : e.button === 1 ? "middle" : "left";
              await this._sendPointerEvent("click", null, null, button);
            } catch (err) {
              console.error("Error sending absolute move and click:", err);
            }
          })();
        } else {
          const button = e.button === 2 ? "right" : e.button === 1 ? "middle" : "left";
          this._sendPointerEvent("click", null, null, button);
        }
      }
      touchStart = null;
      lastMove = null;
      if (moveThrottle) {
        clearTimeout(moveThrottle);
        moveThrottle = null;
      }
    };

    element.addEventListener("touchstart", handleStart);
    element.addEventListener("touchmove", handleMove);
    element.addEventListener("touchend", handleEnd);
    element.addEventListener("mousedown", handleStart);
    element.addEventListener("mousemove", handleMove);
    element.addEventListener("mouseup", handleEnd);
    element.addEventListener("contextmenu", (e) => e.preventDefault());
  }

  _resizeFullscreenCanvas() {
    if (!this._fullscreenCanvas) return;
    this._fullscreenCanvas.width = window.innerWidth;
    this._fullscreenCanvas.height = window.innerHeight;
  }

  async _connectFullscreenVideo() {
    try {
      const haId = this.hass.config?.location_name || this.config.entity || "home-assistant";
      
      // Create desktop session for video
      console.log("Creating desktop session for fullscreen video...");
      await this.hass.callService("openctrol", "create_desktop_session", {
        entity_id: this.config.entity,
        ha_id: haId,
        ttl_seconds: 3600,
      });

      // Wait for entity to update and refresh multiple times to ensure we get session info
      for (let i = 0; i < 3; i++) {
        await new Promise(resolve => setTimeout(resolve, 500));
        try {
          await this.hass.callService("homeassistant", "update_entity", {
            entity_id: this.config.entity,
          });
        } catch (refreshErr) {
          console.warn("Entity refresh attempt", i + 1, "failed:", refreshErr);
        }
      }

      // Get WebSocket URL from entity attributes
      const entity = this.hass.states[this.config.entity];
      console.log("Entity state:", entity);
      console.log("Entity attributes:", entity?.attributes);
      
      const websocketUrl = entity?.attributes?.latest_websocket_url;
      const sessionId = entity?.attributes?.latest_session_id;

      console.log("WebSocket URL:", websocketUrl);
      console.log("Session ID:", sessionId);

      if (!websocketUrl) {
        // Try one more refresh before giving up
        await this.hass.callService("homeassistant", "update_entity", {
          entity_id: this.config.entity,
        });
        await new Promise(resolve => setTimeout(resolve, 500));
        const retryEntity = this.hass.states[this.config.entity];
        const retryUrl = retryEntity?.attributes?.latest_websocket_url;
        if (retryUrl) {
          this._fullscreenSessionId = retryEntity?.attributes?.latest_session_id;
          await this._connectFullscreenWebSocket(retryUrl);
          return;
        }
        throw new Error("WebSocket URL not available. Please ensure a monitor is selected and try again.");
      }

      this._fullscreenSessionId = sessionId;
      
      // Connect to WebSocket
      await this._connectFullscreenWebSocket(websocketUrl);
    } catch (err) {
      console.error("Failed to connect fullscreen video:", err);
      alert(`Failed to connect video stream: ${err.message || err}`);
      if (this._fullscreenCanvas) {
        const ctx = this._fullscreenContext;
        ctx.fillStyle = "#000";
        ctx.fillRect(0, 0, this._fullscreenCanvas.width, this._fullscreenCanvas.height);
        ctx.fillStyle = "#f44336";
        ctx.font = "20px Arial";
        ctx.textAlign = "center";
        ctx.fillText(`Error: ${err.message || err}`, this._fullscreenCanvas.width / 2, this._fullscreenCanvas.height / 2);
      }
    }
  }

  async _connectFullscreenWebSocket(websocketUrl) {
    try {
      const ws = new WebSocket(websocketUrl);
      
      ws.onopen = () => {
        console.log("Fullscreen video WebSocket connected");
        if (this._fullscreenCanvas) {
          const ctx = this._fullscreenContext;
          ctx.fillStyle = "#000";
          ctx.fillRect(0, 0, this._fullscreenCanvas.width, this._fullscreenCanvas.height);
          ctx.fillStyle = "#4caf50";
          ctx.font = "20px Arial";
          ctx.textAlign = "center";
          ctx.fillText("Connected - Waiting for video stream...", this._fullscreenCanvas.width / 2, this._fullscreenCanvas.height / 2);
        }
      };
      
      ws.onmessage = (event) => {
        const handleData = (data) => {
          if (data instanceof ArrayBuffer) {
            this._handleFullscreenVideoFrame(new Uint8Array(data));
          } else if (data instanceof Blob) {
            data.arrayBuffer().then(buffer => {
              this._handleFullscreenVideoFrame(new Uint8Array(buffer));
            }).catch(err => {
              console.error("Error reading blob:", err);
            });
          }
        };
        handleData(event.data);
      };
      
      ws.onerror = () => {
        console.error("Fullscreen video WebSocket error");
        if (this._fullscreenCanvas) {
          const ctx = this._fullscreenContext;
          ctx.fillStyle = "#000";
          ctx.fillRect(0, 0, this._fullscreenCanvas.width, this._fullscreenCanvas.height);
          ctx.fillStyle = "#f44336";
          ctx.font = "20px Arial";
          ctx.textAlign = "center";
          ctx.fillText("WebSocket connection error", this._fullscreenCanvas.width / 2, this._fullscreenCanvas.height / 2);
        }
      };
      
      ws.onclose = () => {
        console.log("Fullscreen video WebSocket closed");
      };
      
      this._fullscreenWebSocket = ws;
    } catch (err) {
      console.error("Failed to create fullscreen WebSocket:", err);
      throw err;
    }
  }

  _handleFullscreenVideoFrame(data) {
    if (!data || data.length < 16) return;
    
    try {
      // Parse OFRA header
      const header = data.slice(0, 16);
      const magic = String.fromCharCode(...header.slice(0, 4));
      
      if (magic !== "OFRA") return;
      
      const view = new DataView(header.buffer, header.byteOffset, header.byteLength);
      const width = view.getUint32(4, true);
      const height = view.getUint32(8, true);
      const jpegData = data.slice(16);
      
      if (jpegData.length > 0 && this._fullscreenCanvas && this._fullscreenContext) {
        this._renderFullscreenFrame(jpegData, width, height);
      }
    } catch (err) {
      console.error("Error handling fullscreen video frame:", err);
    }
  }

  _renderFullscreenFrame(jpegData, width, height) {
    if (!this._fullscreenCanvas || !this._fullscreenContext || !jpegData || jpegData.length === 0) {
      return;
    }

    try {
      const base64 = btoa(String.fromCharCode(...jpegData));
      const img = new Image();
      
      img.onload = () => {
        if (!this._fullscreenCanvas || !this._fullscreenContext) return;
        
        const canvas = this._fullscreenCanvas;
        const ctx = this._fullscreenContext;
        
        // Use the decoded image's actual dimensions (not the frame header dimensions)
        // This ensures we use the active pixels, accounting for any JPEG encoding quirks
        const imgWidth = img.naturalWidth || img.width || width;
        const imgHeight = img.naturalHeight || img.height || height;
        
        // Calculate aspect ratios using actual image dimensions vs canvas
        const imgAspect = imgWidth / imgHeight;
        const canvasAspect = canvas.width / canvas.height;
        
        // Calculate draw size to maintain aspect ratio while filling canvas
        // Use letterboxing/pillarboxing to preserve aspect ratio
        let drawWidth = canvas.width;
        let drawHeight = canvas.height;
        let offsetX = 0;
        let offsetY = 0;
        
        if (imgAspect > canvasAspect) {
          // Image is wider than canvas - letterbox (black bars top/bottom)
          drawHeight = canvas.width / imgAspect;
          offsetY = (canvas.height - drawHeight) / 2;
        } else {
          // Image is taller than canvas - pillarbox (black bars left/right)
          drawWidth = canvas.height * imgAspect;
          offsetX = (canvas.width - drawWidth) / 2;
        }
        
        // Clear entire canvas (including black bars)
        ctx.fillStyle = "#000";
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        
        // Draw image centered with aspect ratio preserved
        ctx.drawImage(img, offsetX, offsetY, drawWidth, drawHeight);
        
        // Store draw region for touch coordinate mapping in fullscreen mode
        // Touch coordinates should map to the actual image area, not black bars
        this._fullscreenDrawRegion = {
          x: offsetX,
          y: offsetY,
          width: drawWidth,
          height: drawHeight,
          imgWidth: imgWidth,
          imgHeight: imgHeight
        };
      };
      
      img.onerror = () => {
        console.error("Error loading fullscreen video frame");
      };
      
      img.src = `data:image/jpeg;base64,${base64}`;
    } catch (err) {
      console.error("Error rendering fullscreen frame:", err);
    }
  }

  _closeFullscreenMonitor() {
    this._fullscreenOpen = false;
    const overlay = document.getElementById("openctrol-fullscreen-overlay");
    if (overlay) {
      overlay.remove();
    }
    window.removeEventListener("resize", this._resizeFullscreenCanvas);
    
    // Disconnect video
    if (this._fullscreenWebSocket) {
      this._fullscreenWebSocket.close();
      this._fullscreenWebSocket = null;
    }
    
    if (this._fullscreenSessionId) {
      this.hass.callService("openctrol", "end_desktop_session", {
        entity_id: this.config.entity,
        session_id: this._fullscreenSessionId,
      }).catch(() => {});
      this._fullscreenSessionId = null;
    }
    
    this._fullscreenCanvas = null;
    this._fullscreenContext = null;
    this.requestUpdate();
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
            @click=${() => {
              alert("Wake-on-LAN is not yet supported by the Openctrol Agent backend. This feature will be available in a future update.");
            }}
            title="Wake-on-LAN is not yet supported"
          >
            Wake on LAN (Not Available)
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
    
    // Normalize monitor data to handle both PascalCase (from API) and snake_case formats
    const normalizedMonitors = monitors.map(monitor => ({
      id: monitor.id || monitor.Id || "",
      name: monitor.name || monitor.Name || monitor.id || monitor.Id || "Unknown",
      width: monitor.width || monitor.Width || 0,
      height: monitor.height || monitor.Height || 0,
      is_primary: monitor.is_primary || monitor.IsPrimary || false,
      resolution: monitor.resolution || `${monitor.width || monitor.Width || 0}x${monitor.height || monitor.Height || 0}`
    }));
    
    return html`
      <div class="panel">
        <div class="panel-header">
          <h3 class="panel-title">Monitor Control</h3>
          <button class="close-button" @click=${() => { this._activePanel = null; this.requestUpdate(); }}>×</button>
        </div>
        <div class="panel-section">
          <div class="section-title">Monitor Selection (${normalizedMonitors.length} available)</div>
          ${normalizedMonitors.length > 0 ? html`
            <div class="monitor-list">
              ${normalizedMonitors.map(monitor => html`
                <button
                  class="monitor-button ${monitor.id === selectedMonitorId ? "active" : ""}"
                  @click=${async (e) => {
                    e.preventDefault();
                    e.stopPropagation();
                    console.log("Monitor button clicked:", monitor.id);
                    await this._handleSelectMonitor(monitor.id);
                  }}
                  ?disabled=${!this._isOnline}
                >
                  <div class="monitor-button-content">
                    <div class="monitor-name">${monitor.name}</div>
                    <div class="monitor-details">
                      ${monitor.resolution}
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
              <button
                class="action-button"
                style="width: 100%; margin-top: 16px;"
                @click=${() => this._openFullscreenMonitor(selectedMonitorId)}
                ?disabled=${!this._isOnline}
              >
                Open Fullscreen Monitor View
              </button>
            ` : html`
              <div class="info-text" style="color: orange;">
                No monitor selected. Please select a monitor to enable remote desktop capture.
              </div>
            `}
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
                @mousedown=${(e) => {
                  e.preventDefault();
                  const isModifier = ["CTRL", "ALT", "SHIFT", "WIN"].includes(key);
                  const timeout = setTimeout(() => {
                    // Long press - toggle latch for modifiers
                    if (isModifier) {
                      this._handleKeyHold(key);
                    }
                    this._keyHoldTimeouts.delete(key);
                  }, 500);
                  this._keyHoldTimeouts.set(key, timeout);
                }}
                @mouseup=${(e) => {
                  e.preventDefault();
                  const timeout = this._keyHoldTimeouts.get(key);
                  if (timeout) {
                    // Short tap - send the key
                    clearTimeout(timeout);
                    this._keyHoldTimeouts.delete(key);
                    this._handleKeyButton(key);
                  } else {
                    // Was a long press, already handled
                  }
                }}
                @mouseleave=${() => {
                  const timeout = this._keyHoldTimeouts.get(key);
                  if (timeout) {
                    // Mouse left before timeout - treat as short tap
                    clearTimeout(timeout);
                    this._keyHoldTimeouts.delete(key);
                    this._handleKeyButton(key);
                  }
                }}
                @click=${(e) => {
                  e.preventDefault();
                  // Check if modifier is already latched - if so, unlatch it
                  if (this._latchedModifiers.has(key)) {
                    this._toggleModifierLatch(key);
                    return;
                  }
                  // Fallback for touch devices - handle timeout if exists
                  const timeout = this._keyHoldTimeouts.get(key);
                  if (timeout) {
                    clearTimeout(timeout);
                    this._keyHoldTimeouts.delete(key);
                    this._handleKeyButton(key);
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
                  }
                  // If no timeout, it means it was held and already toggled, don't send again
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
                      ${(device.is_default || device.isDefault || device.IsDefault || device.default) ? html`<span class="default-badge">Default</span>` : ""}
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
                    ${!(device.is_default || device.isDefault || device.IsDefault || device.default) ? html`
                      <button
                        class="action-button set-default-button"
                        @click=${() => {
                          this._handleSetDefaultDevice(device.id);
                        }}
                        ?disabled=${!this._isOnline}
                      >
                        Set Default
                      </button>
                    ` : html`
                      <span class="default-badge" style="margin-left: 8px;">Active</span>
                    `}
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
          ${this._isMobile && this._mobileKeyboardVisible ? html`
            <div style="padding: 16px; background: #f5f5f5; border-top: 1px solid #e0e0e0;">
              <div style="font-size: 12px; color: #666; margin-bottom: 8px;">
                Mobile Keyboard Active - Type to send keys
              </div>
              <div style="font-size: 11px; color: #999;">
                Space, Backspace, Enter, and letter/number keys are supported
              </div>
            </div>
          ` : ""}
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

