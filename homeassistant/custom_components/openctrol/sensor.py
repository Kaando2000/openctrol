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
            data["monitors"] = monitors_data.get("monitors", [])
            data["selected_monitor_id"] = monitors_data.get("current_monitor_id", "")
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

        data = self.coordinator.data
        attrs: Dict[str, Any] = {
            "agent_id": data.get("agent_id"),
            "version": data.get("version"),
            "uptime_seconds": data.get("uptime_seconds"),
            "active_sessions": data.get("active_sessions", 0),
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
            attrs["available_monitors"] = monitors
            attrs["monitor_count"] = len(monitors)
        if selected_monitor_id := data.get("selected_monitor_id"):
            attrs["selected_monitor_id"] = selected_monitor_id

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

        return attrs

    @property
    def available(self) -> bool:
        """Return if entity is available."""
        return self.coordinator.last_update_success

