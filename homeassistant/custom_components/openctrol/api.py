"""API client for Openctrol Agent."""

import aiohttp
from typing import Any


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
        api_key: str | None = None,
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

    def _get_headers(self) -> dict[str, str]:
        """Get headers with API key if available."""
        headers = {}
        if self._api_key:
            headers["X-Openctrol-Key"] = self._api_key
        return headers

    async def async_get_health(self) -> dict[str, Any]:
        """Get health status from the agent."""
        url = f"{self.base_url}/api/v1/health"
        headers = self._get_headers()

        try:
            async with self._session.get(
                url, headers=headers, timeout=aiohttp.ClientTimeout(total=10)
            ) as response:
                if response.status != 200:
                    raise OpenctrolApiError(
                        f"Health check failed with status {response.status}"
                    )
                return await response.json()
        except aiohttp.ClientError as err:
            raise OpenctrolApiError(f"Network error: {err}") from err

    async def async_power_action(
        self, action: str, force: bool | None = None
    ) -> None:
        """Execute power action (restart, shutdown, wol)."""
        url = f"{self.base_url}/api/v1/power"
        headers = self._get_headers()
        headers["Content-Type"] = "application/json"

        payload: dict[str, Any] = {"action": action}
        if force is not None:
            payload["force"] = force

        try:
            async with self._session.post(
                url,
                headers=headers,
                json=payload,
                timeout=aiohttp.ClientTimeout(total=10),
            ) as response:
                if response.status != 200:
                    error_text = await response.text()
                    raise OpenctrolApiError(
                        f"Power action failed with status {response.status}: {error_text}"
                    )
        except aiohttp.ClientError as err:
            raise OpenctrolApiError(f"Network error: {err}") from err

    async def async_get_audio_status(self) -> dict[str, Any]:
        """Get audio status (master volume and devices)."""
        url = f"{self.base_url}/api/v1/audio/status"
        headers = self._get_headers()

        try:
            async with self._session.get(
                url, headers=headers, timeout=aiohttp.ClientTimeout(total=10)
            ) as response:
                if response.status != 200:
                    error_text = await response.text()
                    raise OpenctrolApiError(
                        f"Audio status failed with status {response.status}: {error_text}"
                    )
                return await response.json()
        except aiohttp.ClientError as err:
            raise OpenctrolApiError(f"Network error: {err}") from err

    async def async_set_master_volume(
        self, volume: int | None = None, muted: bool | None = None
    ) -> None:
        """Set master volume and/or mute state."""
        url = f"{self.base_url}/api/v1/audio/master"
        headers = self._get_headers()
        headers["Content-Type"] = "application/json"

        payload: dict[str, Any] = {}
        if volume is not None:
            payload["volume"] = volume
        if muted is not None:
            payload["muted"] = muted

        try:
            async with self._session.post(
                url,
                headers=headers,
                json=payload,
                timeout=aiohttp.ClientTimeout(total=10),
            ) as response:
                if response.status != 200:
                    error_text = await response.text()
                    raise OpenctrolApiError(
                        f"Set master volume failed with status {response.status}: {error_text}"
                    )
        except aiohttp.ClientError as err:
            raise OpenctrolApiError(f"Network error: {err}") from err

    async def async_set_device_volume(
        self,
        device_id: str,
        volume: int | None = None,
        muted: bool | None = None,
    ) -> None:
        """Set device volume and/or mute state."""
        url = f"{self.base_url}/api/v1/audio/device"
        headers = self._get_headers()
        headers["Content-Type"] = "application/json"

        payload: dict[str, Any] = {"device_id": device_id}
        if volume is not None:
            payload["volume"] = volume
        if muted is not None:
            payload["muted"] = muted

        try:
            async with self._session.post(
                url,
                headers=headers,
                json=payload,
                timeout=aiohttp.ClientTimeout(total=10),
            ) as response:
                if response.status != 200:
                    error_text = await response.text()
                    raise OpenctrolApiError(
                        f"Set device volume failed with status {response.status}: {error_text}"
                    )
        except aiohttp.ClientError as err:
            raise OpenctrolApiError(f"Network error: {err}") from err

    async def async_set_default_output_device(self, device_id: str) -> None:
        """Set default audio output device."""
        url = f"{self.base_url}/api/v1/audio/default"
        headers = self._get_headers()
        headers["Content-Type"] = "application/json"

        payload = {"device_id": device_id}

        try:
            async with self._session.post(
                url,
                headers=headers,
                json=payload,
                timeout=aiohttp.ClientTimeout(total=10),
            ) as response:
                if response.status != 200:
                    error_text = await response.text()
                    raise OpenctrolApiError(
                        f"Set default device failed with status {response.status}: {error_text}"
                    )
        except aiohttp.ClientError as err:
            raise OpenctrolApiError(f"Network error: {err}") from err

    async def async_get_monitors(self) -> dict[str, Any]:
        """Get available monitors and current selection."""
        url = f"{self.base_url}/api/v1/rd/monitors"
        headers = self._get_headers()

        try:
            async with self._session.get(
                url, headers=headers, timeout=aiohttp.ClientTimeout(total=10)
            ) as response:
                if response.status != 200:
                    error_text = await response.text()
                    raise OpenctrolApiError(
                        f"Get monitors failed with status {response.status}: {error_text}"
                    )
                return await response.json()
        except aiohttp.ClientError as err:
            raise OpenctrolApiError(f"Network error: {err}") from err

    async def async_select_monitor(self, monitor_id: str) -> None:
        """Select monitor for remote desktop capture."""
        url = f"{self.base_url}/api/v1/rd/monitor"
        headers = self._get_headers()
        headers["Content-Type"] = "application/json"

        payload = {"monitor_id": monitor_id}

        try:
            async with self._session.post(
                url,
                headers=headers,
                json=payload,
                timeout=aiohttp.ClientTimeout(total=10),
            ) as response:
                if response.status != 200:
                    error_text = await response.text()
                    raise OpenctrolApiError(
                        f"Select monitor failed with status {response.status}: {error_text}"
                    )
        except aiohttp.ClientError as err:
            raise OpenctrolApiError(f"Network error: {err}") from err

