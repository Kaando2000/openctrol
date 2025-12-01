"""Sensor platform for Openctrol integration."""

from datetime import timedelta
from typing import Any

from homeassistant.components.sensor import SensorEntity
from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback
from homeassistant.helpers.update_coordinator import DataUpdateCoordinator, UpdateFailed

from .api import OpenctrolApiClient, OpenctrolApiError
from .const import DATA_API_CLIENT, DOMAIN

SCAN_INTERVAL = timedelta(seconds=30)


async def async_setup_entry(
    hass: HomeAssistant,
    entry: ConfigEntry,
    async_add_entities: AddEntitiesCallback,
) -> None:
    """Set up the Openctrol sensor platform."""
    client: OpenctrolApiClient = hass.data[DOMAIN][entry.entry_id][DATA_API_CLIENT]

    coordinator = OpenctrolDataUpdateCoordinator(hass, client)
    await coordinator.async_config_entry_first_refresh()

    async_add_entities([OpenctrolStatusSensor(coordinator, entry)])


class OpenctrolDataUpdateCoordinator(DataUpdateCoordinator[dict[str, Any]]):
    """Class to manage fetching Openctrol data."""

    def __init__(self, hass: HomeAssistant, client: OpenctrolApiClient) -> None:
        """Initialize."""
        super().__init__(
            hass,
            logger=__name__,
            name=DOMAIN,
            update_interval=SCAN_INTERVAL,
        )
        self.client = client

    async def _async_update_data(self) -> dict[str, Any]:
        """Fetch data from Openctrol API."""
        try:
            return await self.client.async_get_health()
        except OpenctrolApiError as err:
            raise UpdateFailed(f"Error communicating with API: {err}") from err


class OpenctrolStatusSensor(SensorEntity):
    """Representation of an Openctrol status sensor."""

    def __init__(
        self, coordinator: OpenctrolDataUpdateCoordinator, entry: ConfigEntry
    ) -> None:
        """Initialize the sensor."""
        self.coordinator = coordinator
        self._entry = entry
        self._attr_unique_id = f"{entry.entry_id}_status"
        self._attr_name = f"{entry.title} Status"

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
    def extra_state_attributes(self) -> dict[str, Any]:
        """Return the state attributes."""
        if not self.coordinator.data:
            return {}

        data = self.coordinator.data
        attrs: dict[str, Any] = {
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

        return attrs

    @property
    def available(self) -> bool:
        """Return if entity is available."""
        return self.coordinator.last_update_success

    async def async_update(self) -> None:
        """Update the entity."""
        await self.coordinator.async_request_refresh()

