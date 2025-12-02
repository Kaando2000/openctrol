"""WebSocket client for Openctrol Agent input events and video streaming."""

import aiohttp
import json
import logging
import struct
from typing import Any, Callable, Dict, Optional

_LOGGER = logging.getLogger(__name__)


class OpenctrolWsClient:
    """WebSocket client for sending pointer and keyboard input events and receiving video frames."""

    def __init__(
        self,
        hass: Any,
        host: str,
        port: int,
        use_ssl: bool,
        api_key: Optional[str] = None,
    ) -> None:
        """Initialize the WebSocket client."""
        self._hass = hass
        self._host = host
        self._port = port
        self._use_ssl = use_ssl
        self._api_key = api_key
        self._ws: aiohttp.ClientWebSocketResponse | None = None
        self._connected = False
        self._session_id: Optional[str] = None
        self._websocket_url: Optional[str] = None
        self._frame_callback: Optional[Callable[[bytes, int, int], None]] = None
        self._receive_task: Any = None

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
        if self._connected and self._ws and not self._ws.closed:
            _LOGGER.debug("WebSocket already connected")
            return

        try:
            from homeassistant.helpers.aiohttp_client import async_get_clientsession
            session = async_get_clientsession(self._hass)
            
            # Use session-based URL if provided, otherwise fall back to deprecated endpoint
            url = websocket_url or self._ws_url
            headers: Dict[str, str] = {}
            if self._api_key and not websocket_url:
                # Session-based URLs include token, so no need for API key header
                headers["X-Openctrol-Key"] = self._api_key

            _LOGGER.info("Connecting to WebSocket: %s", url)
            self._ws = await session.ws_connect(
                url,
                headers=headers,
                timeout=aiohttp.ClientTimeout(total=30),
            )
            self._connected = True
            self._websocket_url = url
            _LOGGER.info("WebSocket connected successfully")
            
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
        # Ensure connection
        if not self._connected or not self._ws or self._ws.closed:
            await self.async_connect()

        if not self._ws or self._ws.closed:
            raise RuntimeError("WebSocket not connected")

        # Build message
        message: Dict[str, Any] = {"type": "pointer", "event": event_type}
        
        if event_type == "move":
            if dx is None or dy is None:
                raise ValueError("dx and dy are required for move events")
            message["dx"] = dx
            message["dy"] = dy
        elif event_type == "click":
            if button is None:
                raise ValueError("button is required for click events")
            message["button"] = button
        elif event_type == "scroll":
            if dx is None or dy is None:
                raise ValueError("dx and dy are required for scroll events")
            message["dx"] = dx
            message["dy"] = dy
        else:
            raise ValueError(f"Unknown pointer event type: {event_type}")

        # Send message
        try:
            await self._ws.send_str(json.dumps(message))
            _LOGGER.debug("Sent pointer event: %s", event_type)
        except Exception as err:
            _LOGGER.error("Error sending pointer event: %s", err)
            self._connected = False
            raise

    async def async_send_key_combo(self, keys: list[str]) -> None:
        """Send a keyboard key combination."""
        # Ensure connection
        if not self._connected or not self._ws or self._ws.closed:
            await self.async_connect()

        if not self._ws or self._ws.closed:
            raise RuntimeError("WebSocket not connected")

        if not keys:
            raise ValueError("keys list cannot be empty")

        # Send message
        try:
            await self._ws.send_str(json.dumps({"type": "keyboard", "keys": keys}))
            _LOGGER.debug("Sent key combo: %s", keys)
        except Exception as err:
            _LOGGER.error("Error sending key combo: %s", err)
            self._connected = False
            raise

