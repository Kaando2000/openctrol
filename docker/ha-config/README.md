# Home Assistant Configuration Examples

This directory contains example configuration files for reference.

## Files

- **`configuration.yaml`**: Minimal Home Assistant configuration with debug logging enabled for the Openctrol integration
- **`ui-lovelace.yaml`**: Example Lovelace dashboard configuration with the Openctrol card

## Usage

### First Time Setup

When you first start the Docker container, Home Assistant will create its own `configuration.yaml` in the `ha-data/` directory. You can:

1. **Use the default config**: Home Assistant's default configuration will work fine
2. **Copy the example**: Copy `ha-config/configuration.yaml` to `ha-data/configuration.yaml` before first start
3. **Edit after start**: Edit `ha-data/configuration.yaml` directly after Home Assistant creates it

### Recommended: Copy Example Config

To use the example configuration with debug logging:

```powershell
# Before first start (when ha-data doesn't exist yet)
Copy-Item docker\ha-config\configuration.yaml docker\ha-data\configuration.yaml
```

Or manually copy the contents of `ha-config/configuration.yaml` into `ha-data/configuration.yaml` after Home Assistant creates it.

### Debug Logging

The example `configuration.yaml` includes:

```yaml
logger:
  default: info
  logs:
    custom_components.openctrol: debug
```

This enables detailed logging for the Openctrol integration, which is helpful for development and debugging.

## Note

These files are **not automatically used** by Home Assistant. They are examples for reference. The active configuration is always in `ha-data/configuration.yaml`.

