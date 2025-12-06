"""WebSocket client for Openctrol Agent input events and video streaming."""

import aiohttp
import asyncio
import json
import logging
import struct
from typing import Any, Callable, Dict, Optional, TYPE_CHECKING

_LOGGER = logging.getLogger(__name__)

if TYPE_CHECKING:
    from .api import OpenctrolApiClient


class OpenctrolWsClient:
    """WebSocket client for sending pointer and keyboard input events and receiving video frames."""

    def __init__(
        self,
        hass: Any,
        host: str,
        port: int,
        use_ssl: bool,
        api_key: Optional[str] = None,
        entry_id: Optional[str] = None,
    ) -> None:
        """Initialize the WebSocket client."""
        self._hass = hass
        self._host = host
        self._port = port
        self._use_ssl = use_ssl
        self._api_key = api_key
        self._entry_id = entry_id  # Store entry_id for session lookup
        self._ws: aiohttp.ClientWebSocketResponse | None = None
        self._connected = False
        self._session_id: Optional[str] = None
        self._websocket_url: Optional[str] = None
        self._frame_callback: Optional[Callable[[bytes, int, int], None]] = None
        self._receive_task: Any = None
        self._is_deprecated_endpoint: bool = False  # Track if using deprecated endpoint format

    @property
    def _ws_url(self) -> str:
        """Get the WebSocket URL (deprecated - use session-based URL instead)."""
        protocol = "wss" if self._use_ssl else "ws"
        return f"{protocol}://{self._host}:{self._port}/api/v1/rd/session"
    
    def set_frame_callback(self, callback: Optional[Callable[[bytes, int, int], None]]) -> None:
        """Set callback for receiving video frames. Callback receives (jpeg_data, width, height)."""
        self._frame_callback = callback

    async def async_connect(self, websocket_url: Optional[str] = None) -> None:
        """Open a WebSocket connection to the agent.
        
        Args:
            websocket_url: Optional session-based WebSocket URL. If provided, uses this instead of deprecated endpoint.
        """
        # Verify connection is actually valid, not just flagged as connected
        if self._connected and self._ws:
            try:
                # Check if WebSocket is actually open
                if not self._ws.closed and self._ws.close_code is None:
                    _LOGGER.debug("WebSocket already connected and verified")
                    return
                else:
                    # WebSocket is closed, reset state
                    _LOGGER.debug("WebSocket was marked connected but is actually closed, reconnecting")
                    self._connected = False
                    self._ws = None
            except Exception:
                # WebSocket may be in invalid state, reset
                _LOGGER.debug("WebSocket state check failed, resetting connection")
                self._connected = False
                self._ws = None

        try:
            from homeassistant.helpers.aiohttp_client import async_get_clientsession
            session = async_get_clientsession(self._hass)
            
            # For input-only operations, we need a desktop session first
            # Try to reuse existing session or create one if we don't have a websocket_url
            if not websocket_url:
                try:
                    # Import here to avoid circular import
                    from .api import OpenctrolApiClient
                    
                    # Check if we have an existing session stored for this entry
                    entry_data = None
                    openctrol_data = self._hass.data.get("openctrol", {})
                    
                    if self._entry_id:
                        entry_data = openctrol_data.get(self._entry_id)
                    
                    # If no entry_data found, search all entries for sessions
                    if not entry_data or not isinstance(entry_data, dict):
                        for entry_id, data in openctrol_data.items():
                            if isinstance(data, dict) and ("sessions" in data or "latest_session" in data):
                                entry_data = data
                                if not self._entry_id:
                                    self._entry_id = entry_id
                                break
                    
                    # If still no entry_data, try to get it from entity registry
                    if not entry_data or not isinstance(entry_data, dict):
                        # Try to find entry by matching host/port
                        from homeassistant.helpers import entity_registry as er
                        registry = er.async_get(self._hass)
                        for entry_id, data in openctrol_data.items():
                            if isinstance(data, dict):
                                api_client = data.get("api_client")
                                if api_client and hasattr(api_client, "_host") and hasattr(api_client, "_port"):
                                    if api_client._host == self._host and api_client._port == self._port:
                                        entry_data = data
                                        if not self._entry_id:
                                            self._entry_id = entry_id
                                        break
                    
                    existing_session = None
                    if entry_data and isinstance(entry_data, dict):
                        # Check latest_session first (most recent)
                        if "latest_session" in entry_data:
                            latest = entry_data.get("latest_session")
                            if latest and isinstance(latest, dict) and latest.get("websocket_url"):
                                existing_session = latest
                        # Fallback to sessions dict
                        if not existing_session and "sessions" in entry_data:
                            sessions = entry_data.get("sessions", {})
                            if sessions and isinstance(sessions, dict):
                                # Get the first active session
                                for session_id, session_data in sessions.items():
                                    if isinstance(session_data, dict) and session_data.get("websocket_url"):
                                        existing_session = session_data
                                        break
                    
                    api_client = OpenctrolApiClient(
                        session=session,
                        host=self._host,
                        port=self._port,
                        use_ssl=self._use_ssl,
                        api_key=self._api_key,
                    )
                    
                    if existing_session and existing_session.get("websocket_url"):
                        # Reuse existing session
                        websocket_url = existing_session.get("websocket_url")
                        self._session_id = existing_session.get("session_id")
                        _LOGGER.info("Reusing existing desktop session for input: %s", self._session_id)
                    else:
                        # Create a new desktop session for input operations
                        session_data = await api_client.async_create_desktop_session(
                            ha_id="home-assistant",
                            ttl_seconds=3600,  # 1 hour session for input
                        )
                        websocket_url = session_data.get("websocket_url")
                        self._session_id = session_data.get("session_id")
                        # Store session in entry data for reuse
                        if entry_data and isinstance(entry_data, dict):
                            entry_data.setdefault("sessions", {})[self._session_id] = session_data
                            entry_data["latest_session"] = {
                                "session_id": self._session_id,
                                "websocket_url": websocket_url,
                                "expires_at": session_data.get("expires_at", ""),
                            }
                        _LOGGER.info("Created desktop session for input: %s", self._session_id)
                except Exception as session_err:
                    error_msg = str(session_err)
                    # If session limit reached, try to use existing session or deprecated endpoint
                    if "Maximum sessions limit" in error_msg or "session_creation_failed" in error_msg:
                        _LOGGER.warning("Session limit reached, trying to reuse existing session or deprecated endpoint: %s", session_err)
                        # Try to find existing session URL for this entry
                        if entry_data and isinstance(entry_data, dict):
                            # Check latest_session first
                            if "latest_session" in entry_data:
                                latest = entry_data.get("latest_session")
                                if latest and latest.get("websocket_url"):
                                    websocket_url = latest.get("websocket_url")
                                    self._session_id = latest.get("session_id")
                                    _LOGGER.info("Reusing latest session after limit error: %s", self._session_id)
                            # Fallback to sessions dict
                            elif "sessions" in entry_data:
                                sessions = entry_data.get("sessions", {})
                                if sessions:
                                    existing_session = list(sessions.values())[0]
                                    websocket_url = existing_session.get("websocket_url")
                                    if websocket_url:
                                        self._session_id = existing_session.get("session_id")
                                        _LOGGER.info("Reusing existing session after limit error: %s", self._session_id)
                    if not websocket_url:
                        # Last attempt: try to get session from any entry
                        for entry_id, data in self._hass.data.get("openctrol", {}).items():
                            if isinstance(data, dict):
                                if "latest_session" in data:
                                    latest = data.get("latest_session")
                                    if latest and isinstance(latest, dict) and latest.get("websocket_url"):
                                        websocket_url = latest.get("websocket_url")
                                        self._session_id = latest.get("session_id")
                                        entry_data = data
                                        if not self._entry_id:
                                            self._entry_id = entry_id
                                        _LOGGER.info("Found session in entry %s: %s", entry_id, self._session_id)
                                        break
                                if not websocket_url and "sessions" in data:
                                    sessions = data.get("sessions", {})
                                    if sessions and isinstance(sessions, dict):
                                        for sid, sess in sessions.items():
                                            if isinstance(sess, dict) and sess.get("websocket_url"):
                                                websocket_url = sess.get("websocket_url")
                                                self._session_id = sid
                                                entry_data = data
                                                if not self._entry_id:
                                                    self._entry_id = entry_id
                                                _LOGGER.info("Found session in entry %s: %s", entry_id, self._session_id)
                                                break
                                        if websocket_url:
                                            break
                        if not websocket_url:
                            _LOGGER.warning("Failed to create or reuse desktop session, trying deprecated endpoint: %s", session_err)
                            # Fall back to deprecated endpoint
                            websocket_url = None
                        else:
                            # Ensure entry_data is stored for this entry_id
                            if self._entry_id and entry_data:
                                if self._entry_id not in self._hass.data.get("openctrol", {}):
                                    self._hass.data.setdefault("openctrol", {})[self._entry_id] = {}
                                self._hass.data["openctrol"][self._entry_id].update(entry_data)
            
            # Use session-based URL if available, otherwise fall back to deprecated endpoint
            url = websocket_url or self._ws_url
            # Detect endpoint type based on URL pattern, not just websocket_url presence
            # Session-based URLs are like: ws://host:port/ws/desktop?token=...
            # Deprecated URLs are like: ws://host:port/api/v1/rd/session
            self._is_deprecated_endpoint = "/api/v1/rd/session" in url or (not websocket_url and url == self._ws_url)
            headers: Dict[str, str] = {}
            if self._api_key and self._is_deprecated_endpoint:
                # Session-based URLs include token, so no need for API key header
                headers["X-Openctrol-Key"] = self._api_key

            _LOGGER.info("Connecting to WebSocket: %s (deprecated=%s, headers=%s)", url, self._is_deprecated_endpoint, headers)
            try:
                self._ws = await session.ws_connect(
                    url,
                    headers=headers,
                    timeout=aiohttp.ClientTimeout(total=30),
                )
                # Verify connection is actually open
                if self._ws.closed:
                    raise RuntimeError("WebSocket connection closed immediately after connect")
                self._connected = True
                self._websocket_url = url
                _LOGGER.info("WebSocket connected successfully (deprecated endpoint: %s, URL: %s)", self._is_deprecated_endpoint, url)
            except Exception as conn_err:
                _LOGGER.error("WebSocket connection failed: %s (URL: %s, deprecated: %s)", conn_err, url, self._is_deprecated_endpoint)
                self._connected = False
                raise
            
            # Start receiving messages if frame callback is set
            if self._frame_callback:
                self._start_receiving()
        except aiohttp.ClientError as err:
            self._connected = False
            _LOGGER.error("WebSocket connection error: %s", err)
            raise
    
    def _start_receiving(self) -> None:
        """Start background task to receive WebSocket messages."""
        if self._receive_task:
            return
        
        async def receive_loop() -> None:
            if not self._ws:
                return
            try:
                async for msg in self._ws:
                    if msg.type == aiohttp.WSMsgType.BINARY:
                        await self._handle_binary_message(msg.data)
                    elif msg.type == aiohttp.WSMsgType.ERROR:
                        _LOGGER.error("WebSocket error: %s", self._ws.exception())
                        break
                    elif msg.type == aiohttp.WSMsgType.CLOSE:
                        _LOGGER.info("WebSocket closed")
                        break
            except Exception as err:
                _LOGGER.error("Error in WebSocket receive loop: %s", err)
            finally:
                self._receive_task = None
        
        # Create task in Home Assistant event loop
        self._receive_task = self._hass.async_create_task(receive_loop())
    
    async def _handle_binary_message(self, data: bytes) -> None:
        """Handle binary WebSocket message (video frame with OFRA header)."""
        if len(data) < 16:
            _LOGGER.warning("Received binary message too short for OFRA header")
            return
        
        try:
            # Parse OFRA header: "OFRA" (4 bytes) + width (4 bytes) + height (4 bytes) + format (4 bytes)
            header = data[:16]
            magic = header[:4]
            
            if magic != b"OFRA":
                _LOGGER.warning("Invalid frame magic: %s", magic)
                return
            
            width = struct.unpack("<I", header[4:8])[0]
            height = struct.unpack("<I", header[8:12])[0]
            format_type = struct.unpack("<I", header[12:16])[0]
            jpeg_data = data[16:]
            
            _LOGGER.debug("Received frame: %dx%d, format=%d, size=%d", width, height, format_type, len(jpeg_data))
            
            if self._frame_callback:
                try:
                    self._frame_callback(jpeg_data, width, height)
                except Exception as err:
                    _LOGGER.error("Error in frame callback: %s", err)
        except Exception as err:
            _LOGGER.error("Error parsing binary frame: %s", err)

    async def async_close(self) -> None:
        """Close the WebSocket connection."""
        # Cancel receive task if running
        if self._receive_task and not self._receive_task.done():
            self._receive_task.cancel()
            try:
                await self._receive_task
            except Exception:
                pass
            self._receive_task = None
        
        if self._ws and not self._ws.closed:
            try:
                await self._ws.close()
                _LOGGER.info("WebSocket connection closed")
            except Exception as err:
                _LOGGER.warning("Error closing WebSocket: %s", err)
        self._connected = False
        self._ws = None
        self._session_id = None
        self._websocket_url = None

    async def async_send_pointer_event(
        self,
        event_type: str,
        dx: float | None = None,
        dy: float | None = None,
        button: Optional[str] = None,
    ) -> None:
        """Send a pointer event (move, click, or scroll)."""
        # Ensure connection with retry logic
        max_retries = 3
        retry_delay = 0.3
        
        for attempt in range(max_retries):
            # Check connection state - verify both flag and actual WebSocket state
            if not self._connected or not self._ws or self._ws.closed:
                try:
                    _LOGGER.debug("WebSocket not connected for pointer event, attempting connection (attempt %d/%d)", attempt + 1, max_retries)
                    await self.async_connect()
                    # Verify connection was actually established
                    if self._connected and self._ws and not self._ws.closed:
                        _LOGGER.debug("WebSocket connected successfully for pointer event")
                        break
                    else:
                        raise RuntimeError("Connection established but WebSocket state invalid")
                except Exception as conn_err:
                    if attempt < max_retries - 1:
                        _LOGGER.debug("WebSocket connection attempt %d failed, retrying: %s", attempt + 1, conn_err)
                        await asyncio.sleep(retry_delay * (attempt + 1))  # Exponential backoff
                        continue
                    else:
                        _LOGGER.error("Failed to connect WebSocket for pointer event after %d attempts: %s", max_retries, conn_err)
                        raise RuntimeError(f"WebSocket not connected: {conn_err}") from conn_err
            else:
                # Connection appears valid, verify it's actually working
                try:
                    # Quick check - if WebSocket is in a bad state, reconnect
                    if self._ws.closed:
                        self._connected = False
                        continue
                    break
                except Exception:
                    # WebSocket may be in invalid state, force reconnect
                    self._connected = False
                    self._ws = None
                    continue

        # Final verification
        if not self._ws or self._ws.closed:
            _LOGGER.error("WebSocket connection verification failed after retries")
            raise RuntimeError("WebSocket not connected after retry attempts")

        # Build message according to endpoint format (deprecated vs session-based)
        if self._is_deprecated_endpoint:
            # Deprecated endpoint format: {"type": "pointer", "event": "move", "dx": ..., "dy": ...}
            if event_type == "move":
                if dx is None or dy is None:
                    raise ValueError("dx and dy are required for move events")
                message = {
                    "type": "pointer",
                    "event": "move",
                    "dx": float(dx),
                    "dy": float(dy),
                }
            elif event_type == "click":
                if button is None:
                    raise ValueError("button is required for click events")
                message = {
                    "type": "pointer",
                    "event": "click",
                    "button": button.lower(),
                }
            elif event_type == "button":
                # Deprecated endpoint doesn't support separate down/up actions
                # Convert button down/up to click events
                if button is None:
                    raise ValueError("button is required for button events")
                # For deprecated endpoint, send click event (which sends both down and up)
                # This means toggle functionality won't work perfectly, but at least it won't error
                message = {
                    "type": "pointer",
                    "event": "click",
                    "button": button.lower(),
                }
                _LOGGER.debug("Converting button event to click for deprecated endpoint (toggle not supported)")
            elif event_type == "scroll":
                if dx is None or dy is None:
                    raise ValueError("dx and dy are required for scroll events")
                message = {
                    "type": "pointer",
                    "event": "scroll",
                    "dx": float(dx),
                    "dy": float(dy),
                }
            else:
                raise ValueError(f"Unknown pointer event type: {event_type}")
            
            # Send single message for deprecated endpoint
            try:
                message_json = json.dumps(message)
                _LOGGER.debug("Sending pointer event (deprecated format) to %s: %s", self._websocket_url, message_json)
                await self._ws.send_str(message_json)
                _LOGGER.debug("Sent pointer event (deprecated format): %s", event_type)
            except Exception as err:
                _LOGGER.error("Error sending pointer event: %s", err)
                self._connected = False
                raise
        else:
            # Session-based endpoint format: {"type": "pointer_move", "dx": ..., "dy": ...}
            if event_type == "move":
                if dx is None or dy is None:
                    raise ValueError("dx and dy are required for move events")
                # Ensure dx and dy are integers (agent expects integers)
                message = {
                    "type": "pointer_move",
                    "dx": int(round(dx)),
                    "dy": int(round(dy)),
                }
            elif event_type == "click":
                if button is None:
                    raise ValueError("button is required for click events")
                # Send both down and up actions for a click
                message_down = {
                    "type": "pointer_button",
                    "button": button.lower(),
                    "action": "down",
                }
                message_up = {
                    "type": "pointer_button",
                    "button": button.lower(),
                    "action": "up",
                }
                # Send down first, then up
                try:
                    await self._ws.send_str(json.dumps(message_down))
                    await self._ws.send_str(json.dumps(message_up))
                    # Reduced logging
                except Exception as err:
                    _LOGGER.error("Error sending pointer click: %s", err)
                    self._connected = False
                    raise
                return
            elif event_type == "button":
                # Handle button down/up for toggle functionality
                # dx parameter contains the action ("down" or "up")
                if button is None:
                    raise ValueError("button is required for button events")
                # Ensure action is a valid string - dx can be "down", "up", or a string representation
                action = "down"  # Default
                if dx is not None:
                    action_str = str(dx).lower()
                    if action_str in ("down", "up"):
                        action = action_str
                    else:
                        _LOGGER.warning("Invalid button action: %s, defaulting to 'down'", dx)
                message = {
                    "type": "pointer_button",
                    "button": button.lower(),
                    "action": action,
                }
                try:
                    message_json = json.dumps(message)
                    await self._ws.send_str(message_json)
                    _LOGGER.debug("Sent pointer button event: %s", message_json)
                except Exception as err:
                    _LOGGER.error("Error sending pointer button: %s", err)
                    self._connected = False
                    raise
                return
            elif event_type == "scroll":
                if dx is None or dy is None:
                    raise ValueError("dx and dy are required for scroll events")
                # Ensure delta_x and delta_y are integers
                message = {
                    "type": "pointer_wheel",
                    "delta_x": int(round(dx)),
                    "delta_y": int(round(dy)),
                }
            else:
                raise ValueError(f"Unknown pointer event type: {event_type}")

            # Send message
            try:
                message_json = json.dumps(message)
                # Reduced logging - only log errors
                await self._ws.send_str(message_json)
            except Exception as err:
                _LOGGER.error("Error sending pointer event: %s", err)
                self._connected = False
                raise

    def _map_key_name_to_code(self, key_name: str) -> Optional[int]:
        """Map key name to Windows virtual key code."""
        key_upper = key_name.upper()
        mapping = {
            "TAB": 0x09,
            "ENTER": 0x0D,
            "ESC": 0x1B,
            "ESCAPE": 0x1B,
            "SPACE": 0x20,
            "BACKSPACE": 0x08,
            "DEL": 0x2E,
            "DELETE": 0x2E,
            "INSERT": 0x2D,
            "HOME": 0x24,
            "END": 0x23,
            "PAGEUP": 0x21,
            "PAGEDOWN": 0x22,
            "UP": 0x26,
            "DOWN": 0x28,
            "LEFT": 0x25,
            "RIGHT": 0x27,
            "F1": 0x70,
            "F2": 0x71,
            "F3": 0x72,
            "F4": 0x73,
            "F5": 0x74,
            "F6": 0x75,
            "F7": 0x76,
            "F8": 0x77,
            "F9": 0x78,
            "F10": 0x79,
            "F11": 0x7A,
            "F12": 0x7B,
            "CTRL": 0x11,
            "CONTROL": 0x11,
            "ALT": 0x12,
            "SHIFT": 0x10,
            "WIN": 0x5B,
            "WINDOWS": 0x5B,
        }
        
        # Handle letter keys (A-Z)
        if len(key_upper) == 1 and key_upper.isalpha():
            return ord(key_upper)
        
        # Handle number keys (0-9)
        if len(key_upper) == 1 and key_upper.isdigit():
            return ord(key_upper)
        
        return mapping.get(key_upper)

    async def async_send_key_combo(self, keys: list[str]) -> None:
        """Send a keyboard key combination.
        
        Sends individual key down/up events for each key in the combo.
        Modifiers are sent first (down), then main keys, then keys released (up) in reverse order.
        Supports modifier-only combinations (e.g., ["CTRL"]).
        """
        # Ensure connection with retry logic (same as pointer events)
        max_retries = 3
        retry_delay = 0.3
        
        for attempt in range(max_retries):
            # Check connection state - verify both flag and actual WebSocket state
            if not self._connected or not self._ws or self._ws.closed:
                try:
                    _LOGGER.debug("WebSocket not connected for key combo, attempting connection (attempt %d/%d)", attempt + 1, max_retries)
                    await self.async_connect()
                    # Verify connection was actually established
                    if self._connected and self._ws and not self._ws.closed:
                        _LOGGER.debug("WebSocket connected successfully for key combo")
                        break
                    else:
                        raise RuntimeError("Connection established but WebSocket state invalid")
                except Exception as conn_err:
                    if attempt < max_retries - 1:
                        _LOGGER.debug("WebSocket connection attempt %d failed, retrying: %s", attempt + 1, conn_err)
                        await asyncio.sleep(retry_delay * (attempt + 1))  # Exponential backoff
                        continue
                    else:
                        _LOGGER.error("Failed to connect WebSocket for key combo after %d attempts: %s", max_retries, conn_err)
                        raise RuntimeError(f"WebSocket not connected: {conn_err}") from conn_err
            else:
                # Connection appears valid, verify it's actually working
                try:
                    # Quick check - if WebSocket is in a bad state, reconnect
                    if self._ws.closed:
                        self._connected = False
                        continue
                    break
                except Exception:
                    # WebSocket may be in invalid state, force reconnect
                    self._connected = False
                    self._ws = None
                    continue

        # Final verification
        if not self._ws or self._ws.closed:
            _LOGGER.error("WebSocket connection verification failed after retries")
            raise RuntimeError("WebSocket not connected after retry attempts")
        
        if not keys:
            raise ValueError("keys list cannot be empty")

        if not keys:
            raise ValueError("keys list cannot be empty")

        # Separate modifiers from main keys
        modifier_flags = {"ctrl": False, "alt": False, "shift": False, "win": False}
        modifier_key_codes = []  # Store modifier key codes for sending
        main_keys = []
        
        # Map key codes to their modifier flags for exclusion logic
        VK_CONTROL = 0x11
        VK_MENU = 0x12  # Alt
        VK_SHIFT = 0x10
        VK_LWIN = 0x5B
        
        for key in keys:
            key_upper = key.upper()
            if key_upper in ("CTRL", "CONTROL"):
                modifier_flags["ctrl"] = True
                modifier_key_codes.append(VK_CONTROL)
            elif key_upper in ("ALT",):
                modifier_flags["alt"] = True
                modifier_key_codes.append(VK_MENU)
            elif key_upper in ("SHIFT",):
                modifier_flags["shift"] = True
                modifier_key_codes.append(VK_SHIFT)
            elif key_upper in ("WIN", "WINDOWS"):
                modifier_flags["win"] = True
                modifier_key_codes.append(VK_LWIN)
            else:
                key_code = self._map_key_name_to_code(key)
                if key_code is not None:
                    main_keys.append(key_code)
                else:
                    _LOGGER.warning("Unknown key name: %s", key)

        # Check if we have any keys to send (modifiers or main keys)
        if not modifier_key_codes and not main_keys:
            _LOGGER.warning("No valid keys to send in combo: %s", keys)
            return

        try:
            if self._is_deprecated_endpoint:
                # Deprecated endpoint format: {"type": "keyboard", "keys": ["CTRL", "A"]}
                # Convert key codes back to names for deprecated endpoint
                key_names = []
                for key_code in modifier_key_codes + main_keys:
                    # Map key codes back to names
                    if key_code == VK_CONTROL:
                        key_names.append("CTRL")
                    elif key_code == VK_MENU:
                        key_names.append("ALT")
                    elif key_code == VK_SHIFT:
                        key_names.append("SHIFT")
                    elif key_code == VK_LWIN:
                        key_names.append("WIN")
                    else:
                        # Try to map key code to name
                        if 0x41 <= key_code <= 0x5A:  # A-Z
                            key_names.append(chr(key_code))
                        elif 0x30 <= key_code <= 0x39:  # 0-9
                            key_names.append(chr(key_code))
                        else:
                            # Fallback: use original key name from input
                            for orig_key in keys:
                                if self._map_key_name_to_code(orig_key) == key_code:
                                    key_names.append(orig_key.upper())
                                    break
                
                message = {
                    "type": "keyboard",
                    "keys": key_names,
                }
                message_json = json.dumps(message)
                _LOGGER.debug("Sending key combo (deprecated format) to %s: %s", self._websocket_url, message_json)
                await self._ws.send_str(message_json)
                _LOGGER.info("Sent key combo (deprecated format): %s -> %s", keys, message_json)
            else:
                # Session-based endpoint format: {"type": "key", "key_code": ..., "action": "down"}
                # Send key down events: modifiers first, then main keys
                all_keys_down = modifier_key_codes + main_keys
                
                for key_code in all_keys_down:
                    message = {
                        "type": "key",
                        "key_code": key_code,
                        "action": "down",
                    }
                    # Add modifier flags, but exclude the flag for the key being pressed itself
                    # (e.g., don't set ctrl:true when pressing CTRL key)
                    if modifier_flags["ctrl"] and key_code != VK_CONTROL:
                        message["ctrl"] = True
                    if modifier_flags["alt"] and key_code != VK_MENU:
                        message["alt"] = True
                    if modifier_flags["shift"] and key_code != VK_SHIFT:
                        message["shift"] = True
                    if modifier_flags["win"] and key_code != VK_LWIN:
                        message["win"] = True
                    
                    await self._ws.send_str(json.dumps(message))
                
                # Send key up events in reverse order: main keys first, then modifiers
                all_keys_up = list(reversed(main_keys)) + list(reversed(modifier_key_codes))
                
                for key_code in all_keys_up:
                    message = {
                        "type": "key",
                        "key_code": key_code,
                        "action": "up",
                    }
                    # Add modifier flags, but exclude the flag for the key being released itself
                    # For up events, we need to check if OTHER modifiers are still held
                    # Since we're releasing in reverse order, modifiers are released last,
                    # so we should include flags for modifiers that are still held
                    if modifier_flags["ctrl"] and key_code != VK_CONTROL:
                        message["ctrl"] = True
                    if modifier_flags["alt"] and key_code != VK_MENU:
                        message["alt"] = True
                    if modifier_flags["shift"] and key_code != VK_SHIFT:
                        message["shift"] = True
                    if modifier_flags["win"] and key_code != VK_LWIN:
                        message["win"] = True
                    
                    message_json = json.dumps(message)
                    await self._ws.send_str(message_json)
                    # Reduced logging - only log errors
                
                # Only log if there's an error, not every key combo
        except Exception as err:
            _LOGGER.error("Error sending key combo: %s (keys: %s, endpoint: %s)", err, keys, "deprecated" if self._is_deprecated_endpoint else "session", exc_info=True)
            self._connected = False
            raise

