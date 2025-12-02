"""API client for Openctrol Agent."""

import aiohttp
from typing import Any, Dict, Optional


class OpenctrolApiError(Exception):
    """Exception raised for Openctrol API errors."""

    pass


class OpenctrolApiClient:
    """Async client for Openctrol Agent API."""

    def __init__(
        self,
        session: aiohttp.ClientSession,
        host: str,
        port: int,
        use_ssl: bool,
        api_key: Optional[str] = None,
    ) -> None:
        """Initialize the API client."""
        self._session = session
        self._host = host
        self._port = port
        self._use_ssl = use_ssl
        self._api_key = api_key

    @property
    def base_url(self) -> str:
        """Get the base URL for the API."""
        protocol = "https" if self._use_ssl else "http"
        return f"{protocol}://{self._host}:{self._port}"

    def _get_headers(self) -> Dict[str, str]:
        """Get headers with API key if available."""
        headers = {}
        if self._api_key:
            headers["X-Openctrol-Key"] = self._api_key
        return headers

    async def _get_json(self, url: str) -> Dict[str, Any]:
        """Helper to GET JSON from API."""
        headers = self._get_headers()
        async with self._session.get(
            url, headers=headers, timeout=aiohttp.ClientTimeout(total=10)
        ) as response:
            if response.status != 200:
                error_text = await response.text()
                raise OpenctrolApiError(
                    f"API request failed with status {response.status}: {error_text}"
                )
            return await response.json()

    async def _post_json(
        self, url: str, payload: Optional[Dict[str, Any]] = None
    ) -> None:
        """Helper to POST JSON to API (no response body expected)."""
        headers = self._get_headers()
        headers["Content-Type"] = "application/json"
        async with self._session.post(
            url,
            headers=headers,
            json=payload or {},
            timeout=aiohttp.ClientTimeout(total=10),
        ) as response:
            if response.status != 200:
                error_text = await response.text()
                raise OpenctrolApiError(
                    f"API request failed with status {response.status}: {error_text}"
                )

    async def async_get_health(self) -> Dict[str, Any]:
        """Get health status from the agent."""
        return await self._get_json(f"{self.base_url}/api/v1/health")

    async def async_power_action(
        self, action: str, force: Optional[bool] = None
    ) -> None:
        """Execute power action (restart, shutdown, wol)."""
        payload: Dict[str, Any] = {"action": action}
        if force is not None:
            payload["force"] = force
        await self._post_json(f"{self.base_url}/api/v1/power", payload)

    async def async_get_audio_status(self) -> Dict[str, Any]:
        """Get audio status (master volume and devices)."""
        return await self._get_json(f"{self.base_url}/api/v1/audio/status")

    async def async_set_master_volume(
        self, volume: Optional[int] = None, muted: Optional[bool] = None
    ) -> None:
        """Set master volume and/or mute state."""
        payload: Dict[str, Any] = {}
        if volume is not None:
            payload["volume"] = float(volume)  # API expects float
        if muted is not None:
            payload["muted"] = muted
        await self._post_json(f"{self.base_url}/api/v1/audio/master", payload)

    async def async_set_device_volume(
        self,
        device_id: str,
        volume: Optional[int] = None,
        muted: Optional[bool] = None,
    ) -> None:
        """Set device volume and/or mute state."""
        payload: Dict[str, Any] = {"device_id": device_id}
        if volume is not None:
            payload["volume"] = float(volume)  # API expects float
        if muted is not None:
            payload["muted"] = muted
        await self._post_json(f"{self.base_url}/api/v1/audio/device", payload)

    async def async_set_default_output_device(self, device_id: str) -> None:
        """Set default audio output device."""
        await self._post_json(
            f"{self.base_url}/api/v1/audio/default", {"device_id": device_id}
        )

    async def async_get_monitors(self) -> Dict[str, Any]:
        """Get available monitors and current selection."""
        return await self._get_json(f"{self.base_url}/api/v1/rd/monitors")

    async def async_select_monitor(self, monitor_id: str) -> None:
        """Select monitor for remote desktop capture."""
        await self._post_json(
            f"{self.base_url}/api/v1/rd/monitor", {"monitor_id": monitor_id}
        )

    async def async_create_desktop_session(
        self, ha_id: str, ttl_seconds: int = 900
    ) -> Dict[str, Any]:
        """Create a desktop session and return session details."""
        headers = self._get_headers()
        headers["Content-Type"] = "application/json"
        payload = {"ha_id": ha_id, "ttl_seconds": ttl_seconds}
        
        async with self._session.post(
            f"{self.base_url}/api/v1/sessions/desktop",
            headers=headers,
            json=payload,
            timeout=aiohttp.ClientTimeout(total=10),
        ) as response:
            if response.status != 200:
                error_text = await response.text()
                raise OpenctrolApiError(
                    f"Create desktop session failed with status {response.status}: {error_text}"
                )
            return await response.json()

    async def async_end_desktop_session(self, session_id: str) -> None:
        """End a desktop session."""
        await self._post_json(
            f"{self.base_url}/api/v1/sessions/desktop/{session_id}/end"
        )

