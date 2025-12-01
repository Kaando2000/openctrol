"""WebSocket client for Openctrol Agent input events."""

import aiohttp
import json
import logging
from typing import Any

_LOGGER = logging.getLogger(__name__)


class OpenctrolWsClient:
    """WebSocket client for sending pointer and keyboard input events."""

    def __init__(
        self,
        hass: Any,
        host: str,
        port: int,
        use_ssl: bool,
        api_key: str | None = None,
    ) -> None:
        """Initialize the WebSocket client."""
        self._hass = hass
        self._host = host
        self._port = port
        self._use_ssl = use_ssl
        self._api_key = api_key
        self._ws: aiohttp.ClientWebSocketResponse | None = None
        self._connected = False

    @property
    def _ws_url(self) -> str:
        """Get the WebSocket URL."""
        protocol = "wss" if self._use_ssl else "ws"
        return f"{protocol}://{self._host}:{self._port}/api/v1/rd/session"

    async def async_connect(self) -> None:
        """Open a WebSocket connection to the agent."""
        if self._connected and self._ws and not self._ws.closed:
            _LOGGER.debug("WebSocket already connected")
            return

        try:
            from homeassistant.helpers.aiohttp_client import async_get_clientsession
            session = async_get_clientsession(self._hass)
            headers: dict[str, str] = {}
            if self._api_key:
                headers["X-Openctrol-Key"] = self._api_key

            _LOGGER.info(f"Connecting to WebSocket: {self._ws_url}")
            self._ws = await session.ws_connect(
                self._ws_url,
                headers=headers,
                timeout=aiohttp.ClientTimeout(total=30),
            )
            self._connected = True
            _LOGGER.info("WebSocket connected successfully")
        except aiohttp.ClientError as err:
            self._connected = False
            _LOGGER.error(f"WebSocket connection error: {err}")
            raise

    async def async_close(self) -> None:
        """Close the WebSocket connection."""
        if self._ws and not self._ws.closed:
            try:
                await self._ws.close()
                _LOGGER.info("WebSocket connection closed")
            except Exception as err:
                _LOGGER.warning(f"Error closing WebSocket: {err}")
        self._connected = False
        self._ws = None

    async def async_send_pointer_event(
        self,
        event_type: str,
        dx: float | None = None,
        dy: float | None = None,
        button: str | None = None,
    ) -> None:
        """Send a pointer event (move, click, or scroll)."""
        if not self._connected or not self._ws or self._ws.closed:
            await self.async_connect()

        if not self._ws:
            raise RuntimeError("WebSocket not connected")

        message: dict[str, Any] = {
            "type": "pointer",
            "event": event_type,
        }

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

        try:
            await self._ws.send_str(json.dumps(message))
            _LOGGER.debug(f"Sent pointer event: {event_type}")
        except Exception as err:
            _LOGGER.error(f"Error sending pointer event: {err}")
            self._connected = False
            raise

    async def async_send_key_combo(self, keys: list[str]) -> None:
        """Send a keyboard key combination."""
        if not self._connected or not self._ws or self._ws.closed:
            await self.async_connect()

        if not self._ws:
            raise RuntimeError("WebSocket not connected")

        if not keys:
            raise ValueError("keys list cannot be empty")

        message = {
            "type": "keyboard",
            "keys": keys,
        }

        try:
            await self._ws.send_str(json.dumps(message))
            _LOGGER.debug(f"Sent key combo: {keys}")
        except Exception as err:
            _LOGGER.error(f"Error sending key combo: {err}")
            self._connected = False
            raise

