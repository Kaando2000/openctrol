"""The Openctrol integration."""

import asyncio
import logging
from typing import Any, Dict, Optional, Tuple

from homeassistant.config_entries import ConfigEntry
from homeassistant.const import Platform
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
    ATTR_FORCE,
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

PLATFORMS = [Platform.SENSOR]


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

    # Create WebSocket client with entry_id for session management
    ws_client = OpenctrolWsClient(hass, host, port, use_ssl, api_key, entry.entry_id)

    entry_data = {
        DATA_API_CLIENT: client,
        "ws_client": ws_client,
        "api_client": client,  # Also store as api_client for session lookup
        "host": host,  # Store host for session lookup
        "port": port,  # Store port for session lookup
        "sessions": {},  # Initialize sessions dict
    }
    hass.data.setdefault(DOMAIN, {})[entry.entry_id] = entry_data

    await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)

    # Register services
    await _async_register_services(hass, entry)

    return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
    """Unload a config entry."""
    # Close WebSocket connection if open
    entry_data = hass.data.get(DOMAIN, {}).get(entry.entry_id, {})
    ws_client: Optional[OpenctrolWsClient] = entry_data.get("ws_client")
    if ws_client:
        try:
            await ws_client.async_close()
        except Exception:
            pass

    # Unregister services for this entry
    try:
        _async_unregister_services(hass)
    except Exception as err:
        _LOGGER.warning("Error unregistering services: %s", err)

    unload_ok = await hass.config_entries.async_unload_platforms(entry, PLATFORMS)

    if unload_ok:
        hass.data[DOMAIN].pop(entry.entry_id)

    return unload_ok


def _get_entry_id_from_entity_id(hass: HomeAssistant, entity_id: str) -> Optional[str]:
    """Get config entry ID from entity ID."""
    registry = er.async_get(hass)
    entity_entry = registry.async_get(entity_id)
    if entity_entry and entity_entry.config_entry_id:
        return entity_entry.config_entry_id
    return None


def _get_entry_data_from_entity_id(
    hass: HomeAssistant, entity_id: str
) -> Tuple[str, Dict[str, Any]]:
    """Get entry ID and entry data from entity ID.
    
    Returns:
        Tuple of (entry_id, entry_data)
    
    Raises:
        HomeAssistantError: If entity_id is invalid or entry not found
    """
    if not entity_id:
        raise HomeAssistantError("entity_id is required")
    
    entry_id = _get_entry_id_from_entity_id(hass, entity_id)
    if not entry_id:
        raise HomeAssistantError(f"Could not find config entry for entity {entity_id}")
    
    entry_data = hass.data[DOMAIN].get(entry_id)
    if not entry_data:
        raise HomeAssistantError(f"Config entry {entry_id} not found")
    
    return entry_id, entry_data


async def _async_register_services(hass: HomeAssistant, entry: ConfigEntry) -> None:
    """Register all Openctrol services."""

    async def send_pointer_event(call: ServiceCall) -> None:
        """Handle send_pointer_event service call."""
        _, entry_data = _get_entry_data_from_entity_id(hass, call.data.get("entity_id"))
        ws_client: Optional[OpenctrolWsClient] = entry_data.get("ws_client")
        if not ws_client:
            raise HomeAssistantError("WebSocket client not available")
        
        try:
            event_type = call.data.get("type")
            # Handle button down/up events for toggle
            if event_type == "button":
                button = call.data.get(ATTR_BUTTON)
                # Card passes action as dx parameter, fallback to action field or default to "down"
                action = call.data.get(ATTR_DX) or call.data.get("action", "down")
                # Ensure action is a string
                if not isinstance(action, str):
                    action = str(action)
                # Use dx parameter to pass action for button events
                await ws_client.async_send_pointer_event(
                    "button",
                    action,  # Pass action as dx parameter
                    None,
                    button,
                )
            else:
                # Ensure dx and dy are numbers (convert from string if needed)
                dx = call.data.get(ATTR_DX)
                dy = call.data.get(ATTR_DY)
                if dx is not None:
                    try:
                        dx = float(dx)
                    except (ValueError, TypeError):
                        _LOGGER.warning("Invalid dx value: %s, ignoring", dx)
                        dx = None
                if dy is not None:
                    try:
                        dy = float(dy)
                    except (ValueError, TypeError):
                        _LOGGER.warning("Invalid dy value: %s, ignoring", dy)
                        dy = None
                
                await ws_client.async_send_pointer_event(
                    event_type,
                    dx,
                    dy,
                    call.data.get(ATTR_BUTTON),
                )
        except RuntimeError as err:
            # RuntimeError usually means WebSocket connection failed
            _LOGGER.error("WebSocket connection error sending pointer event: %s", err, exc_info=True)
            raise HomeAssistantError(f"WebSocket connection failed: {err}") from err
        except Exception as err:
            _LOGGER.error("Error sending pointer event: %s", err, exc_info=True)
            raise HomeAssistantError(f"Failed to send pointer event: {err}") from err

    async def send_key_combo(call: ServiceCall) -> None:
        """Handle send_key_combo service call."""
        keys = call.data.get(ATTR_KEYS, [])
        if not keys:
            _LOGGER.warning("send_key_combo called with empty keys list")
            raise HomeAssistantError("keys list cannot be empty")
        
        _LOGGER.debug("Received send_key_combo service call with keys: %s", keys)
        _, entry_data = _get_entry_data_from_entity_id(hass, call.data.get("entity_id"))
        ws_client: Optional[OpenctrolWsClient] = entry_data.get("ws_client")
        if not ws_client:
            _LOGGER.error("WebSocket client not available for send_key_combo")
            raise HomeAssistantError("WebSocket client not available")
        
        try:
            _LOGGER.debug("Calling async_send_key_combo with keys: %s", keys)
            await ws_client.async_send_key_combo(keys)
            _LOGGER.debug("Successfully sent key combo: %s", keys)
        except RuntimeError as err:
            # RuntimeError usually means WebSocket connection failed
            _LOGGER.error("WebSocket connection error sending key combo: %s (keys: %s)", err, keys, exc_info=True)
            raise HomeAssistantError(f"WebSocket connection failed: {err}") from err
        except Exception as err:
            _LOGGER.error("Error sending key combo: %s (keys: %s)", err, keys, exc_info=True)
            raise HomeAssistantError(f"Failed to send key combo: {err}") from err

    async def power_action(call: ServiceCall) -> None:
        """Handle power_action service call."""
        _, entry_data = _get_entry_data_from_entity_id(hass, call.data.get("entity_id"))
        client: Optional[OpenctrolApiClient] = entry_data.get(DATA_API_CLIENT)
        if not client:
            raise HomeAssistantError("API client not available")
        
        try:
            await client.async_power_action(call.data.get(ATTR_ACTION), call.data.get(ATTR_FORCE))
        except OpenctrolApiError as err:
            _LOGGER.error("Power action failed: %s", err, exc_info=True)
            raise HomeAssistantError(f"Power action failed: {err}") from err

    async def select_monitor(call: ServiceCall) -> None:
        """Handle select_monitor service call."""
        entity_id, entry_data = _get_entry_data_from_entity_id(hass, call.data.get("entity_id"))
        client: Optional[OpenctrolApiClient] = entry_data.get(DATA_API_CLIENT)
        if not client:
            raise HomeAssistantError("API client not available")
        
        monitor_id = call.data.get(ATTR_MONITOR_ID)
        if not monitor_id:
            raise HomeAssistantError("monitor_id is required")
        
        try:
            await client.async_select_monitor(monitor_id)
            _LOGGER.info("Monitor selected successfully: %s", monitor_id)
            
            # Force immediate entity refresh to update monitor selection state
            from homeassistant.helpers import entity_registry as er
            registry = er.async_get(hass)
            entity_entry = registry.async_get(entity_id)
            if entity_entry:
                entry_id = entity_entry.config_entry_id
                # Get coordinator and force refresh
                coordinator = hass.data.get(DOMAIN, {}).get(entry_id, {}).get("coordinator")
                if coordinator:
                    # Force immediate refresh
                    await coordinator.async_request_refresh()
                    # Also trigger a manual update via service call as backup
                    await hass.services.async_call("homeassistant", "update_entity", {"entity_id": entity_id}, blocking=False)
                else:
                    # Fallback: just call update entity service
                    await hass.services.async_call("homeassistant", "update_entity", {"entity_id": entity_id}, blocking=False)
        except OpenctrolApiError as err:
            _LOGGER.error("Select monitor failed: %s", err, exc_info=True)
            raise HomeAssistantError(f"Select monitor failed: {err}") from err

    async def set_master_volume(call: ServiceCall) -> None:
        """Handle set_master_volume service call."""
        _, entry_data = _get_entry_data_from_entity_id(hass, call.data.get("entity_id"))
        client: Optional[OpenctrolApiClient] = entry_data.get(DATA_API_CLIENT)
        if not client:
            raise HomeAssistantError("API client not available")
        
        try:
            await client.async_set_master_volume(
                call.data.get(ATTR_VOLUME), call.data.get(ATTR_MUTED)
            )
        except OpenctrolApiError as err:
            _LOGGER.error("Set master volume failed: %s", err, exc_info=True)
            raise HomeAssistantError(f"Set master volume failed: {err}") from err

    async def set_device_volume(call: ServiceCall) -> None:
        """Handle set_device_volume service call."""
        _, entry_data = _get_entry_data_from_entity_id(hass, call.data.get("entity_id"))
        client: Optional[OpenctrolApiClient] = entry_data.get(DATA_API_CLIENT)
        if not client:
            raise HomeAssistantError("API client not available")
        
        device_id = call.data.get(ATTR_DEVICE_ID)
        if not device_id:
            raise HomeAssistantError("device_id is required")
        
        try:
            await client.async_set_device_volume(
                device_id,
                call.data.get(ATTR_VOLUME),
                call.data.get(ATTR_MUTED),
            )
        except OpenctrolApiError as err:
            _LOGGER.error("Set device volume failed: %s", err, exc_info=True)
            raise HomeAssistantError(f"Set device volume failed: {err}") from err

    async def set_default_output_device(call: ServiceCall) -> None:
        """Handle set_default_output_device service call."""
        entity_id, entry_data = _get_entry_data_from_entity_id(hass, call.data.get("entity_id"))
        client: Optional[OpenctrolApiClient] = entry_data.get(DATA_API_CLIENT)
        if not client:
            raise HomeAssistantError("API client not available")
        
        device_id = call.data.get(ATTR_DEVICE_ID)
        if not device_id:
            raise HomeAssistantError("device_id is required")
        
        try:
            await client.async_set_default_output_device(device_id)
            # Wait a moment for the change to take effect
            await asyncio.sleep(0.5)
            # Refresh entity to update default device status
            await hass.services.async_call("homeassistant", "update_entity", {"entity_id": entity_id})
            # Also refresh coordinator if available
            coordinator = entry_data.get("coordinator")
            if coordinator:
                await coordinator.async_request_refresh()
        except OpenctrolApiError as err:
            error_msg = str(err)
            # Provide more helpful error messages
            if "admin" in error_msg.lower() or "privilege" in error_msg.lower():
                _LOGGER.warning("Set default device failed - may require admin privileges: %s", err)
            elif "not found" in error_msg.lower():
                _LOGGER.error("Set default device failed - device not found: %s", err)
                raise HomeAssistantError(f"Audio device not found: {device_id}") from err
            else:
                _LOGGER.warning("Set default device failed (may require admin or device limitation): %s", err)
            # Don't raise error for most cases - just log warning (some systems can't change default device)
            # The backend already handles this gracefully

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

    async def create_desktop_session(call: ServiceCall) -> None:
        """Handle create_desktop_session service call.
        
        Session info is stored in entry_data and exposed via entity attributes.
        """
        entity_id, entry_data = _get_entry_data_from_entity_id(hass, call.data.get("entity_id"))
        client: Optional[OpenctrolApiClient] = entry_data.get(DATA_API_CLIENT)
        if not client:
            raise HomeAssistantError("API client not available")
        
        try:
            session_data = await client.async_create_desktop_session(
                call.data.get("ha_id", "home-assistant"),
                call.data.get("ttl_seconds", 900),
            )
            session_id = session_data.get("session_id", "")
            websocket_url = session_data.get("websocket_url", "")
            
            # Store session info in entry data for retrieval
            entry_data.setdefault("sessions", {})[session_id] = session_data
            # Store latest session info for entity attributes
            entry_data["latest_session"] = {
                "session_id": session_id,
                "websocket_url": websocket_url,
                "expires_at": session_data.get("expires_at", ""),
            }
            
            # Update entity attributes to expose session info
            from homeassistant.helpers import entity_registry as er
            registry = er.async_get(hass)
            entity_entry = registry.async_get(entity_id)
            if entity_entry:
                # Trigger entity update by requesting coordinator refresh
                coordinator = entry_data.get("coordinator")
                if coordinator:
                    await coordinator.async_request_refresh()
            
            _LOGGER.info(
                "Created desktop session %s. WebSocket URL: %s",
                session_id,
                websocket_url,
            )
        except OpenctrolApiError as err:
            _LOGGER.error("Create desktop session failed: %s", err, exc_info=True)
            raise HomeAssistantError(f"Create desktop session failed: {err}") from err

    async def end_desktop_session(call: ServiceCall) -> None:
        """Handle end_desktop_session service call."""
        _, entry_data = _get_entry_data_from_entity_id(hass, call.data.get("entity_id"))
        client: Optional[OpenctrolApiClient] = entry_data.get(DATA_API_CLIENT)
        if not client:
            raise HomeAssistantError("API client not available")
        
        session_id = call.data.get("session_id")
        if not session_id:
            raise HomeAssistantError("session_id is required")
        
        try:
            await client.async_end_desktop_session(session_id)
            # Clean up stored session info
            if "sessions" in entry_data:
                entry_data["sessions"].pop(session_id, None)
        except OpenctrolApiError as err:
            _LOGGER.error("End desktop session failed: %s", err, exc_info=True)
            raise HomeAssistantError(f"End desktop session failed: {err}") from err

    hass.services.async_register(
        DOMAIN,
        "create_desktop_session",
        create_desktop_session,
    )

    hass.services.async_register(
        DOMAIN,
        "end_desktop_session",
        end_desktop_session,
    )


def _async_unregister_services(hass: HomeAssistant) -> None:
    """Unregister all Openctrol services."""
    # Note: HA doesn't provide async_unregister, so we just log
    # Services will be overwritten if entry is reloaded
    _LOGGER.debug("Services unregistration requested (HA handles this automatically)")

