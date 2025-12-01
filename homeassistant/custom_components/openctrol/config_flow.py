"""Config flow for Openctrol integration."""

from typing import Any

import voluptuous as vol

from homeassistant import config_entries
from homeassistant.core import HomeAssistant
from homeassistant.data_entry_flow import FlowResult
from homeassistant.helpers.aiohttp_client import async_get_clientsession

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


class OpenctrolConfigFlow(config_entries.ConfigFlow, domain=DOMAIN):
    """Handle a config flow for Openctrol."""

    VERSION = 1

    async def async_step_user(
        self, user_input: dict[str, Any] | None = None
    ) -> FlowResult:
        """Handle the initial step."""
        errors: dict[str, str] = {}

        if user_input is not None:
            try:
                # Validate connection by calling health endpoint
                session = async_get_clientsession(self.hass)
                client = OpenctrolApiClient(
                    session=session,
                    host=user_input[CONF_HOST],
                    port=user_input[CONF_PORT],
                    use_ssl=user_input.get(CONF_USE_SSL, DEFAULT_USE_SSL),
                    api_key=user_input.get(CONF_API_KEY) or None,
                )
                await client.async_get_health()

                # Create config entry
                return self.async_create_entry(
                    title=user_input.get("name", DEFAULT_NAME),
                    data={
                        CONF_HOST: user_input[CONF_HOST],
                        CONF_PORT: user_input[CONF_PORT],
                        CONF_USE_SSL: user_input.get(CONF_USE_SSL, DEFAULT_USE_SSL),
                        CONF_API_KEY: user_input.get(CONF_API_KEY) or "",
                    },
                )
            except OpenctrolApiError:
                errors["base"] = "cannot_connect"
            except Exception:
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

