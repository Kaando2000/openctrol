"""Constants for the Openctrol integration."""

DOMAIN = "openctrol"

CONF_HOST = "host"
CONF_PORT = "port"
CONF_API_KEY = "api_key"
CONF_USE_SSL = "use_ssl"

DEFAULT_NAME = "Openctrol Agent"
DEFAULT_PORT = 44325
DEFAULT_USE_SSL = False

DATA_API_CLIENT = "api_client"

# Service names
SERVICE_SEND_POINTER_EVENT = "send_pointer_event"
SERVICE_SEND_KEY_COMBO = "send_key_combo"
SERVICE_POWER_ACTION = "power_action"
SERVICE_SELECT_MONITOR = "select_monitor"
SERVICE_SET_MASTER_VOLUME = "set_master_volume"
SERVICE_SET_DEVICE_VOLUME = "set_device_volume"
SERVICE_SET_DEFAULT_OUTPUT_DEVICE = "set_default_output_device"

# Service attribute keys
ATTR_DX = "dx"
ATTR_DY = "dy"
ATTR_BUTTON = "button"
ATTR_KEYS = "keys"
ATTR_ACTION = "action"
ATTR_FORCE = "force"
ATTR_MONITOR_ID = "monitor_id"
ATTR_VOLUME = "volume"
ATTR_MUTED = "muted"
ATTR_DEVICE_ID = "device_id"
ATTR_EVENT = "event"

