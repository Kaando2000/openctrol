"""Sensor platform for Openctrol integration."""

import logging
from datetime import timedelta
from typing import Any, Dict, Optional

from homeassistant.components.sensor import SensorEntity
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.helpers.update_coordinator import CoordinatorEntity, DataUpdateCoordinator, UpdateFailed

from .api import OpenctrolApiClient, OpenctrolApiError
from .const import DATA_API_CLIENT, DOMAIN

_LOGGER = logging.getLogger(__name__)

SCAN_INTERVAL = timedelta(seconds=30)


async def async_setup_entry(
    hass: HomeAssistant,
    entry: ConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up the Openctrol sensor platform."""
    try:
        entry_data = hass.data.get(DOMAIN, {}).get(entry.entry_id)
        if not entry_data:
            _LOGGER.error("Openctrol entry data not found for entry %s", entry.entry_id)
            return

        client: Optional[OpenctrolApiClient] = entry_data.get(DATA_API_CLIENT)
        if not client:
            _LOGGER.error("Openctrol API client not found for entry %s", entry.entry_id)
            return

        coordinator = OpenctrolDataUpdateCoordinator(hass, client)
        
        # Store coordinator in entry_data so service handlers can trigger refreshes
        entry_data = hass.data.get(DOMAIN, {}).get(entry.entry_id, {})
        if isinstance(entry_data, dict):
            entry_data["coordinator"] = coordinator
        
        # Try to refresh, but don't fail if it doesn't work initially
        try:
            await coordinator.async_config_entry_first_refresh()
        except Exception as refresh_err:
            _LOGGER.warning(
                "Initial coordinator refresh failed for entry %s (entity will still be created): %s",
                entry.entry_id,
                refresh_err,
            )

        sensor = OpenctrolStatusSensor(coordinator, entry)
        async_add_entities([sensor], update_before_add=True)
        _LOGGER.info(
            "Openctrol sensor entity created for entry %s: unique_id=%s, name=%s",
            entry.entry_id,
            sensor.unique_id,
            sensor.name,
        )
    except Exception as err:
        _LOGGER.error("Failed to set up Openctrol sensor for entry %s: %s", entry.entry_id, err, exc_info=True)
        raise


class OpenctrolDataUpdateCoordinator(DataUpdateCoordinator[Dict[str, Any]]):
    """Class to manage fetching Openctrol data."""

    def __init__(self, hass: HomeAssistant, client: OpenctrolApiClient) -> None:
        """Initialize."""
        super().__init__(
            hass,
            logger=_LOGGER,
            name=DOMAIN,
            update_interval=SCAN_INTERVAL,
        )
        self.client = client

    async def _async_update_data(self) -> Dict[str, Any]:
        """Fetch data from Openctrol API."""
        data: Dict[str, Any] = {}
        
        # Fetch health status (required)
        try:
            health_data = await self.client.async_get_health()
            data.update(health_data)
        except OpenctrolApiError as err:
            raise UpdateFailed(f"Error communicating with API: {err}") from err
        
        # Fetch monitors (optional - don't fail if unavailable)
        try:
            monitors_data = await self.client.async_get_monitors()
            # API returns {"Monitors": [...], "CurrentMonitorId": "..."}
            monitors_raw = monitors_data.get("Monitors") or monitors_data.get("monitors", [])
            # Normalize monitor data to snake_case for consistency
            data["monitors"] = [
                {
                    "id": m.get("Id") or m.get("id", ""),
                    "name": m.get("Name") or m.get("name", ""),
                    "width": m.get("Width") or m.get("width", 0),
                    "height": m.get("Height") or m.get("height", 0),
                    "is_primary": m.get("IsPrimary") or m.get("is_primary", False),
                }
                for m in monitors_raw
            ]
            data["selected_monitor_id"] = monitors_data.get("CurrentMonitorId") or monitors_data.get("current_monitor_id") or monitors_data.get("selected_monitor_id", "")
        except Exception as err:
            _LOGGER.debug("Failed to fetch monitors: %s", err)
            data["monitors"] = []
            data["selected_monitor_id"] = ""
        
        # Fetch audio status (optional - don't fail if unavailable)
        try:
            data["audio"] = await self.client.async_get_audio_status()
        except Exception as err:
            _LOGGER.debug("Failed to fetch audio status: %s", err)
            data["audio"] = {}
        
        return data


class OpenctrolStatusSensor(CoordinatorEntity, SensorEntity):
    """Representation of an Openctrol status sensor."""

    def __init__(
        self, coordinator: OpenctrolDataUpdateCoordinator, entry: ConfigEntry
    ) -> None:
        """Initialize the sensor."""
        super().__init__(coordinator)
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_status"
        self._attr_name = f"{entry.title} Status"
        self._attr_device_class = None

    @property
    def native_value(self) -> str:
        """Return the state of the sensor."""
        if not self.coordinator.last_update_success or not self.coordinator.data:
            return "offline"
        
        # Check if remote desktop is running and not degraded
        remote_desktop = self.coordinator.data.get("remote_desktop", {})
        is_running = remote_desktop.get("is_running", False)
        is_degraded = remote_desktop.get("degraded", False)
        
        if is_running and not is_degraded:
            return "online"
        return "offline"

    @property
    def extra_state_attributes(self) -> Dict[str, Any]:
        """Return the state attributes."""
        if not self.coordinator.data:
            return {}

        from homeassistant.core import callback
        from homeassistant.helpers import entity_registry as er
        
        data = self.coordinator.data
        # Get computer name from entry title or host
        computer_name = self._entry.title if hasattr(self._entry, 'title') else None
        if not computer_name or computer_name.endswith(" Status"):
            # Try to get from entry data
            entry_data = self.hass.data.get("openctrol", {}).get(self._entry.entry_id, {})
            if isinstance(entry_data, dict):
                api_client = entry_data.get("api_client")
                if api_client and hasattr(api_client, "_host"):
                    computer_name = api_client._host
        
        attrs: Dict[str, Any] = {
            "agent_id": data.get("agent_id"),
            "version": data.get("version"),
            "uptime_seconds": data.get("uptime_seconds"),
            "active_sessions": data.get("active_sessions", 0),
            "computer_name": computer_name or "Unknown PC",
        }

        # Include remote_desktop data if present
        if remote_desktop := data.get("remote_desktop"):
            attrs["remote_desktop_is_running"] = remote_desktop.get("is_running", False)
            attrs["remote_desktop_state"] = remote_desktop.get("state")
            attrs["remote_desktop_desktop_state"] = remote_desktop.get("desktop_state")
            attrs["remote_desktop_last_frame_at"] = remote_desktop.get("last_frame_at")
            attrs["remote_desktop_degraded"] = remote_desktop.get("degraded", False)

        # Include monitors data if present
        if monitors := data.get("monitors"):
            # Ensure monitors are normalized (already normalized in coordinator, but double-check)
            normalized_monitors = []
            for monitor in monitors:
                normalized_monitors.append({
                    "id": monitor.get("id", ""),
                    "name": monitor.get("name", ""),
                    "width": monitor.get("width", 0),
                    "height": monitor.get("height", 0),
                    "is_primary": monitor.get("is_primary", False),
                    "resolution": f"{monitor.get('width', 0)}x{monitor.get('height', 0)}"
                })
            attrs["available_monitors"] = normalized_monitors
            attrs["monitor_count"] = len(normalized_monitors)
        else:
            attrs["available_monitors"] = []
            attrs["monitor_count"] = 0
        if selected_monitor_id := data.get("selected_monitor_id"):
            attrs["selected_monitor_id"] = selected_monitor_id
        else:
            attrs["selected_monitor_id"] = ""

        # Include audio data if present
        if audio := data.get("audio"):
            if master := audio.get("master"):
                attrs["master_volume"] = master.get("volume", 0)
                attrs["master_muted"] = master.get("muted", False)
            if devices := audio.get("devices"):
                attrs["audio_devices"] = devices
                attrs["audio_device_count"] = len(devices)
            if sessions := audio.get("sessions"):
                attrs["audio_sessions"] = sessions
                attrs["audio_session_count"] = len(sessions)

        # Include latest session info if available (from service calls)
        from homeassistant.core import HomeAssistant
        hass: HomeAssistant = self.hass
        entry_data = hass.data.get(DOMAIN, {}).get(self._entry.entry_id, {})
        if latest_session := entry_data.get("latest_session"):
            attrs["latest_session_id"] = latest_session.get("session_id", "")
            attrs["latest_websocket_url"] = latest_session.get("websocket_url", "")
            attrs["latest_session_expires_at"] = latest_session.get("expires_at", "")

        return attrs

    @property
    def available(self) -> bool:
        """Return if entity is available."""
        return self.coordinator.last_update_success

