"""The Openctrol integration."""

import logging

from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant, ServiceCall
from homeassistant.exceptions import HomeAssistantError
from homeassistant.helpers import entity_registry as er
from homeassistant.helpers.aiohttp_client import async_get_clientsession

from .api import OpenctrolApiClient, OpenctrolApiError
from .const import (
    ATTR_ACTION,
    ATTR_BUTTON,
    ATTR_DEVICE_ID,
    ATTR_DX,
    ATTR_DY,
    ATTR_KEYS,
    ATTR_MONITOR_ID,
    ATTR_MUTED,
    ATTR_VOLUME,
    CONF_API_KEY,
    CONF_HOST,
    CONF_PORT,
    CONF_USE_SSL,
    DATA_API_CLIENT,
    DOMAIN,
    SERVICE_POWER_ACTION,
    SERVICE_SEND_KEY_COMBO,
    SERVICE_SEND_POINTER_EVENT,
    SERVICE_SELECT_MONITOR,
    SERVICE_SET_DEFAULT_OUTPUT_DEVICE,
    SERVICE_SET_DEVICE_VOLUME,
    SERVICE_SET_MASTER_VOLUME,
)
from .ws import OpenctrolWsClient

_LOGGER = logging.getLogger(__name__)


async def async_setup_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Set up Openctrol from a config entry."""
    host = entry.data[CONF_HOST]
    port = entry.data[CONF_PORT]
    use_ssl = entry.data.get(CONF_USE_SSL, False)
    api_key = entry.data.get(CONF_API_KEY) or None

    session = async_get_clientsession(hass)
    client = OpenctrolApiClient(
        session=session,
        host=host,
        port=port,
        use_ssl=use_ssl,
        api_key=api_key,
    )

    # Create WebSocket client
    ws_client = OpenctrolWsClient(hass, host, port, use_ssl, api_key)

    hass.data.setdefault(DOMAIN, {})[entry.entry_id] = {
        DATA_API_CLIENT: client,
        "ws_client": ws_client,
    }

    await hass.config_entries.async_forward_entry_setups(entry, ["sensor"])

    # Register services
    await _async_register_services(hass, entry)

    return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Unload a config entry."""
    # Close WebSocket connection if open
    entry_data = hass.data.get(DOMAIN, {}).get(entry.entry_id, {})
    ws_client: OpenctrolWsClient | None = entry_data.get("ws_client")
    if ws_client:
        try:
            await ws_client.async_close()
        except Exception:
            pass

    unload_ok = await hass.config_entries.async_unload_platforms(entry, ["sensor"])

    if unload_ok:
        hass.data[DOMAIN].pop(entry.entry_id)

    return unload_ok


def _get_entry_id_from_entity_id(hass: HomeAssistant, entity_id: str) -> str | None:
    """Get config entry ID from entity ID."""
    registry = er.async_get(hass)
    entity_entry = registry.async_get(entity_id)
    if entity_entry and entity_entry.config_entry_id:
        return entity_entry.config_entry_id
    return None


async def _async_register_services(hass: HomeAssistant, entry: ConfigEntry) -> None:
    """Register all Openctrol services."""

    async def send_pointer_event(call: ServiceCall) -> None:
        """Handle send_pointer_event service call."""
        entity_id = call.data.get("entity_id")
        if not entity_id:
            raise HomeAssistantError("entity_id is required")

        entry_id = _get_entry_id_from_entity_id(hass, entity_id)
        if not entry_id:
            raise HomeAssistantError(f"Could not find config entry for entity {entity_id}")

        entry_data = hass.data[DOMAIN].get(entry_id)
        if not entry_data:
            raise HomeAssistantError(f"Config entry {entry_id} not found")

        ws_client: OpenctrolWsClient = entry_data["ws_client"]
        event_type = call.data["type"]

        try:
            dx = call.data.get(ATTR_DX)
            dy = call.data.get(ATTR_DY)
            button = call.data.get(ATTR_BUTTON)
            await ws_client.async_send_pointer_event(event_type, dx, dy, button)
        except Exception as err:
            _LOGGER.error(f"Error sending pointer event: {err}", exc_info=True)
            raise HomeAssistantError(f"Failed to send pointer event: {err}") from err

    async def send_key_combo(call: ServiceCall) -> None:
        """Handle send_key_combo service call."""
        entity_id = call.data.get("entity_id")
        if not entity_id:
            raise HomeAssistantError("entity_id is required")

        entry_id = _get_entry_id_from_entity_id(hass, entity_id)
        if not entry_id:
            raise HomeAssistantError(f"Could not find config entry for entity {entity_id}")

        entry_data = hass.data[DOMAIN].get(entry_id)
        if not entry_data:
            raise HomeAssistantError(f"Config entry {entry_id} not found")

        ws_client: OpenctrolWsClient = entry_data["ws_client"]
        keys = call.data[ATTR_KEYS]

        try:
            await ws_client.async_send_key_combo(keys)
        except Exception as err:
            _LOGGER.error(f"Error sending key combo: {err}", exc_info=True)
            raise HomeAssistantError(f"Failed to send key combo: {err}") from err

    async def power_action(call: ServiceCall) -> None:
        """Handle power_action service call."""
        entity_id = call.data.get("entity_id")
        if not entity_id:
            raise HomeAssistantError("entity_id is required")

        entry_id = _get_entry_id_from_entity_id(hass, entity_id)
        if not entry_id:
            raise HomeAssistantError(f"Could not find config entry for entity {entity_id}")

        entry_data = hass.data[DOMAIN].get(entry_id)
        if not entry_data:
            raise HomeAssistantError(f"Config entry {entry_id} not found")

        client: OpenctrolApiClient = entry_data[DATA_API_CLIENT]
        action = call.data[ATTR_ACTION]
        force = call.data.get(ATTR_FORCE)

        try:
            await client.async_power_action(action, force)
        except OpenctrolApiError as err:
            _LOGGER.error(f"Power action failed: {err}", exc_info=True)
            raise HomeAssistantError(f"Power action failed: {err}") from err

    async def select_monitor(call: ServiceCall) -> None:
        """Handle select_monitor service call."""
        entity_id = call.data.get("entity_id")
        if not entity_id:
            raise HomeAssistantError("entity_id is required")

        entry_id = _get_entry_id_from_entity_id(hass, entity_id)
        if not entry_id:
            raise HomeAssistantError(f"Could not find config entry for entity {entity_id}")

        entry_data = hass.data[DOMAIN].get(entry_id)
        if not entry_data:
            raise HomeAssistantError(f"Config entry {entry_id} not found")

        client: OpenctrolApiClient = entry_data[DATA_API_CLIENT]
        monitor_id = call.data[ATTR_MONITOR_ID]

        try:
            await client.async_select_monitor(monitor_id)
        except OpenctrolApiError as err:
            _LOGGER.error(f"Select monitor failed: {err}", exc_info=True)
            raise HomeAssistantError(f"Select monitor failed: {err}") from err

    async def set_master_volume(call: ServiceCall) -> None:
        """Handle set_master_volume service call."""
        entity_id = call.data.get("entity_id")
        if not entity_id:
            raise HomeAssistantError("entity_id is required")

        entry_id = _get_entry_id_from_entity_id(hass, entity_id)
        if not entry_id:
            raise HomeAssistantError(f"Could not find config entry for entity {entity_id}")

        entry_data = hass.data[DOMAIN].get(entry_id)
        if not entry_data:
            raise HomeAssistantError(f"Config entry {entry_id} not found")

        client: OpenctrolApiClient = entry_data[DATA_API_CLIENT]
        volume = call.data.get(ATTR_VOLUME)
        muted = call.data.get(ATTR_MUTED)

        try:
            await client.async_set_master_volume(volume, muted)
        except OpenctrolApiError as err:
            _LOGGER.error(f"Set master volume failed: {err}", exc_info=True)
            raise HomeAssistantError(f"Set master volume failed: {err}") from err

    async def set_device_volume(call: ServiceCall) -> None:
        """Handle set_device_volume service call."""
        entity_id = call.data.get("entity_id")
        if not entity_id:
            raise HomeAssistantError("entity_id is required")

        entry_id = _get_entry_id_from_entity_id(hass, entity_id)
        if not entry_id:
            raise HomeAssistantError(f"Could not find config entry for entity {entity_id}")

        entry_data = hass.data[DOMAIN].get(entry_id)
        if not entry_data:
            raise HomeAssistantError(f"Config entry {entry_id} not found")

        client: OpenctrolApiClient = entry_data[DATA_API_CLIENT]
        device_id = call.data[ATTR_DEVICE_ID]
        volume = call.data.get(ATTR_VOLUME)
        muted = call.data.get(ATTR_MUTED)

        try:
            await client.async_set_device_volume(device_id, volume, muted)
        except OpenctrolApiError as err:
            _LOGGER.error(f"Set device volume failed: {err}", exc_info=True)
            raise HomeAssistantError(f"Set device volume failed: {err}") from err

    async def set_default_output_device(call: ServiceCall) -> None:
        """Handle set_default_output_device service call."""
        entity_id = call.data.get("entity_id")
        if not entity_id:
            raise HomeAssistantError("entity_id is required")

        entry_id = _get_entry_id_from_entity_id(hass, entity_id)
        if not entry_id:
            raise HomeAssistantError(f"Could not find config entry for entity {entity_id}")

        entry_data = hass.data[DOMAIN].get(entry_id)
        if not entry_data:
            raise HomeAssistantError(f"Config entry {entry_id} not found")

        client: OpenctrolApiClient = entry_data[DATA_API_CLIENT]
        device_id = call.data[ATTR_DEVICE_ID]

        try:
            await client.async_set_default_output_device(device_id)
        except OpenctrolApiError as err:
            _LOGGER.error(f"Set default device failed: {err}", exc_info=True)
            raise HomeAssistantError(f"Set default device failed: {err}") from err

    # Service schemas - services.yaml will be used for schema definition
    # These registrations use minimal schemas since services.yaml provides the full definition
    hass.services.async_register(
        DOMAIN,
        SERVICE_SEND_POINTER_EVENT,
        send_pointer_event,
    )

    hass.services.async_register(
        DOMAIN,
        SERVICE_SEND_KEY_COMBO,
        send_key_combo,
    )

    hass.services.async_register(
        DOMAIN,
        SERVICE_POWER_ACTION,
        power_action,
    )

    hass.services.async_register(
        DOMAIN,
        SERVICE_SELECT_MONITOR,
        select_monitor,
    )

    hass.services.async_register(
        DOMAIN,
        SERVICE_SET_MASTER_VOLUME,
        set_master_volume,
    )

    hass.services.async_register(
        DOMAIN,
        SERVICE_SET_DEVICE_VOLUME,
        set_device_volume,
    )

    hass.services.async_register(
        DOMAIN,
        SERVICE_SET_DEFAULT_OUTPUT_DEVICE,
        set_default_output_device,
    )

