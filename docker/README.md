# Docker Home Assistant Test Server

A containerized Home Assistant test environment for developing and testing the Openctrol integration and card without affecting your production Home Assistant installation.

## Features

- **Isolated Test Environment**: Runs completely separate from production Home Assistant
- **Live Code Reloading**: Changes to integration code are picked up automatically (reload integration in UI)
- **No Server Restarts**: Test changes immediately without restarting the container
- **Persistent Configuration**: All settings persist between container restarts
- **Easy Reset**: Delete `ha-data` directory to start fresh

## Prerequisites

- Docker Desktop (Windows/Mac) or Docker Engine (Linux)
- Docker Compose (usually included with Docker Desktop)
- At least 2GB free disk space
- Openctrol Agent running on the host machine or network

## Quick Start

### Windows (PowerShell)

```powershell
# Start the test server
.\docker\start.ps1

# Or manually
cd docker
docker-compose up -d
```

### Linux/Mac

```bash
# Start the test server
cd docker
docker-compose up -d
```

### Access Home Assistant

1. Open your browser: **http://localhost:8123**
2. Complete the initial setup wizard (create admin account)
3. Configure the Openctrol integration:
   - Go to **Settings → Devices & Services → Add Integration**
   - Search for "Openctrol"
   - Enter agent details:
     - **Host**: `host.docker.internal` (or your actual host IP)
     - **Port**: `44325` (or your agent port)
     - **API Key**: Your agent API key (if configured)

## Stopping the Server

### Windows (PowerShell)

```powershell
.\docker\stop.ps1

# Or manually
cd docker
docker-compose down
```

### Linux/Mac

```bash
cd docker
docker-compose down
```

## Development Workflow

### 1. Making Changes to Integration Code

1. Edit files in `homeassistant/custom_components/openctrol/`
2. In Home Assistant UI: **Developer Tools → YAML → Reload Integration**
3. Select "Openctrol Agent" and click "RELOAD"
4. Test your changes immediately

### 2. Making Changes to the Card

1. Edit `www/openctrol/openctrol-card.js`
2. In Home Assistant UI: **Settings → Dashboards → Resources**
3. Find the Openctrol card resource and click the refresh icon (or remove and re-add)
4. Refresh your browser page (Ctrl+F5 or Cmd+Shift+R)
5. Test your changes

### 3. Viewing Logs

```powershell
# Windows
cd docker
docker-compose logs -f homeassistant

# Linux/Mac
cd docker
docker-compose logs -f homeassistant
```

Or view logs in Home Assistant UI: **Settings → System → Logs**

## Network Configuration

### Accessing Agent from Container

The container needs to access the Openctrol Agent running on your host machine.

#### Windows/Mac

Use `host.docker.internal` as the agent host when configuring the integration. This hostname automatically resolves to your host machine's IP address.

**Example configuration:**
- Host: `host.docker.internal`
- Port: `44325`
- API Key: (your API key)

#### Linux

On Linux, `host.docker.internal` may not work. Use one of these options:

1. **Use host network mode** (edit `docker-compose.yml`):
   ```yaml
   network_mode: host
   ```
   Then use `localhost` or `127.0.0.1` as the agent host.

2. **Use actual host IP**:
   - Find your host IP: `ip addr show` or `hostname -I`
   - Use that IP address when configuring the integration
   - Ensure the agent is accessible from the Docker network

3. **Use host.docker.internal** (if supported):
   - Some Linux Docker setups support `host.docker.internal`
   - Try it first, fall back to options 1 or 2 if it doesn't work

### Firewall Considerations

Ensure the Openctrol Agent port (default 44325) is accessible:
- **Windows**: Check Windows Firewall rules
- **Linux**: Check iptables/firewalld rules
- The agent should accept connections from Docker containers

## Directory Structure

```
docker/
├── docker-compose.yml          # Docker Compose configuration
├── .dockerignore               # Files to exclude from Docker context
├── README.md                   # This file
├── start.ps1                   # Windows start script
├── stop.ps1                    # Windows stop script
├── ha-config/                  # Initial configuration files
│   ├── configuration.yaml     # Minimal HA config
│   └── ui-lovelace.yaml        # Example Lovelace config
└── ha-data/                    # Persistent data (created at runtime)
    ├── .storage/               # Home Assistant storage
    ├── configuration.yaml      # Active configuration (editable)
    ├── custom_components/      # Mounted from repo (read-only)
    └── www/                    # Mounted from repo (read-only)
```

## Configuration Files

### `ha-config/configuration.yaml`

Example minimal configuration. After first start, Home Assistant will create its own `configuration.yaml` in `ha-data/`. You can copy this example or edit the generated file directly. The example includes debug logging for the Openctrol integration.

### `ha-data/` Directory

This directory contains all Home Assistant data:
- **Persistent**: Survives container restarts
- **Editable**: You can edit `ha-data/configuration.yaml` directly
- **Reset**: Delete the entire `ha-data/` directory to start fresh

## Adding the Openctrol Card

1. **Add Card Resource**:
   - Go to **Settings → Dashboards → Resources**
   - Click **"+ ADD RESOURCE"**
   - **URL**: `/local/openctrol/openctrol-card.js`
   - **Type**: JavaScript Module
   - Click **CREATE**

2. **Add Card to Dashboard**:
   - Edit your dashboard
   - Click **"+ ADD CARD"**
   - Search for "openctrol" or select "Custom: Openctrol Card"
   - Configure:
     - **Entity**: `sensor.openctrol_agent_status` (or your entity ID)
   - Click **SAVE**

## Troubleshooting

### Container Won't Start

**Check Docker is running:**
```powershell
docker ps
```

**Check for port conflicts:**
- Ensure port 8123 is not in use by another service
- Change port in `docker-compose.yml` if needed: `"8124:8123"`

**View container logs:**
```powershell
cd docker
docker-compose logs homeassistant
```

### Can't Connect to Agent

**Verify agent is running:**
- Check Windows Services for "Openctrol Agent"
- Test agent health: `http://localhost:44325/api/v1/health`

**Check network connectivity:**
- From container: `docker exec -it openctrol-ha-test ping host.docker.internal`
- Try using actual host IP instead of `host.docker.internal`

**Check firewall:**
- Ensure agent port (44325) is accessible
- Windows: Check Windows Firewall rules
- Linux: Check iptables/firewalld

### Integration Not Loading

**Check logs:**
- Home Assistant UI: **Settings → System → Logs**
- Filter by "openctrol" or "custom_components"
- Look for import errors or configuration issues

**Verify file mounts:**
```powershell
docker exec -it openctrol-ha-test ls -la /config/custom_components/openctrol
```

**Reload integration:**
- **Developer Tools → YAML → Reload Integration**
- Select "Openctrol Agent"

### Card Not Appearing

**Verify resource is added:**
- **Settings → Dashboards → Resources**
- Ensure `/local/openctrol/openctrol-card.js` is listed

**Check browser console:**
- Open browser DevTools (F12)
- Check Console tab for JavaScript errors
- Check Network tab to verify card file loads

**Verify file mount:**
```powershell
docker exec -it openctrol-ha-test ls -la /config/www/openctrol
```

### Changes Not Reflecting

**Integration code changes:**
- Must reload integration: **Developer Tools → YAML → Reload Integration**
- Or restart container: `docker-compose restart homeassistant`

**Card code changes:**
- Refresh card resource in **Settings → Dashboards → Resources**
- Hard refresh browser: Ctrl+F5 (Windows) or Cmd+Shift+R (Mac)

## Resetting the Test Environment

To start completely fresh:

```powershell
# Stop container
cd docker
docker-compose down

# Delete all data
Remove-Item -Recurse -Force ha-data

# Start fresh
docker-compose up -d
```

**Warning**: This deletes all configuration, entities, and history!

## Advanced Usage

### Custom Port

Edit `docker-compose.yml`:
```yaml
ports:
  - "8124:8123"  # Use port 8124 on host
```

### View Container Shell

```powershell
docker exec -it openctrol-ha-test bash
```

### Backup Configuration

```powershell
# Backup ha-data directory
Compress-Archive -Path docker\ha-data -DestinationPath docker\ha-data-backup.zip
```

### Restore Configuration

```powershell
# Stop container
cd docker
docker-compose down

# Restore backup
Expand-Archive -Path docker\ha-data-backup.zip -DestinationPath docker\

# Start container
docker-compose up -d
```

## Benefits Over Production Testing

- ✅ **No Production Risk**: Test changes safely without affecting your home automation
- ✅ **Fast Iteration**: No need to restart Home Assistant server
- ✅ **Easy Reset**: Delete `ha-data` to start over
- ✅ **Isolated**: Can run alongside production Home Assistant
- ✅ **Development-Friendly**: Debug logging enabled by default
- ✅ **Version Control**: Integration code changes tracked in git

## Support

For issues with:
- **Docker setup**: Check Docker logs and this README
- **Integration**: Check Home Assistant logs and integration code
- **Agent**: Verify agent is running and accessible
- **Network**: Check firewall and network configuration

## Next Steps

1. Start the test server: `.\docker\start.ps1`
2. Complete Home Assistant setup wizard
3. Configure Openctrol integration
4. Add Openctrol card to dashboard
5. Start developing and testing!

