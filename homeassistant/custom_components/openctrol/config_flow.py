"""Config flow for Openctrol integration."""

import logging
from typing import Any, Dict, Optional

import voluptuous as vol

from homeassistant import config_entries
from homeassistant.helpers.aiohttp_client import async_get_clientsession

_LOGGER = logging.getLogger(__name__)

from .api import OpenctrolApiClient, OpenctrolApiError
from .const import (
    CONF_API_KEY,
    CONF_HOST,
    CONF_PORT,
    CONF_USE_SSL,
    DEFAULT_NAME,
    DEFAULT_PORT,
    DEFAULT_USE_SSL,
    DOMAIN,
)


class OpenctrolConfigFlow(config_entries.ConfigFlow):
    """Handle a config flow for Openctrol."""

    VERSION = 1

    async def async_step_user(
        self, user_input: Optional[Dict[str, Any]] = None
    ):
        """Handle the initial step."""
        errors: Dict[str, str] = {}

        if user_input is not None:
            # Validate required fields
            host = user_input.get(CONF_HOST, "").strip()
            port = user_input.get(CONF_PORT, DEFAULT_PORT)
            
            if not host:
                errors[CONF_HOST] = "required"
            if not isinstance(port, int) or port <= 0:
                errors[CONF_PORT] = "invalid"
            
            if not errors:
                # Check for existing entries with same host and port
                await self.async_set_unique_id(f"{host}:{port}")
                self._abort_if_unique_id_configured()

                try:
                    # Validate connection by calling health endpoint
                    session = async_get_clientsession(self.hass)
                    client = OpenctrolApiClient(
                        session=session,
                        host=host,
                        port=port,
                        use_ssl=user_input.get(CONF_USE_SSL, DEFAULT_USE_SSL),
                        api_key=user_input.get(CONF_API_KEY) or None,
                    )
                    await client.async_get_health()

                    # Create config entry
                    return self.async_create_entry(
                        title=user_input.get("name", DEFAULT_NAME),
                        data={
                            CONF_HOST: host,
                            CONF_PORT: port,
                            CONF_USE_SSL: user_input.get(CONF_USE_SSL, DEFAULT_USE_SSL),
                            CONF_API_KEY: user_input.get(CONF_API_KEY) or "",
                        },
                    )
                except OpenctrolApiError as err:
                    _LOGGER.error("Connection validation failed: %s", err)
                    errors["base"] = "cannot_connect"
                except Exception as err:
                    _LOGGER.exception("Unexpected error during config flow: %s", err)
                    errors["base"] = "unknown"

        data_schema = vol.Schema(
            {
                vol.Required("name", default=DEFAULT_NAME): str,
                vol.Required(CONF_HOST): str,
                vol.Required(CONF_PORT, default=DEFAULT_PORT): int,
                vol.Required(CONF_USE_SSL, default=DEFAULT_USE_SSL): bool,
                vol.Optional(CONF_API_KEY): str,
            }
        )

        return self.async_show_form(
            step_id="user", data_schema=data_schema, errors=errors
        )

