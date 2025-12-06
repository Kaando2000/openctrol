# Quick Setup Guide

## Server Status

The Home Assistant test server is starting. It typically takes 1-2 minutes to fully initialize on first start.

## Access Home Assistant

Once ready, open your browser and navigate to:

**http://localhost:8123**

## Initial Setup

1. **Complete the Setup Wizard**
   - Create an admin account (username and password)
   - Set your location and timezone
   - Complete the onboarding process

## Configure Openctrol Integration

### Step 1: Add the Integration

1. Go to **Settings** → **Devices & Services**
2. Click **"+ ADD INTEGRATION"** (bottom right)
3. Search for **"Openctrol"** or **"openctrol"**
4. Click on the Openctrol integration

### Step 2: Enter Agent Details

Fill in the configuration form:

- **Host**: `host.docker.internal` (or your actual host machine IP)
- **Port**: `44325` (or your agent's port)
- **API Key**: (leave empty if not configured, or enter your API key)
- **Use SSL**: Unchecked (unless your agent uses HTTPS)

Click **SUBMIT**

### Step 3: Verify Connection

- The integration should create an entity: `sensor.openctrol_agent_status`
- Check the entity state - it should show "online" if connected

## Add the Openctrol Card

### Step 1: Add Card Resource

1. Go to **Settings** → **Dashboards** → **Resources**
2. Click **"+ ADD RESOURCE"** (bottom right)
3. Configure:
   - **URL**: `/local/openctrol/openctrol-card.js`
   - **Resource Type**: JavaScript Module
4. Click **CREATE**

### Step 2: Add Card to Dashboard

1. Go to your dashboard (Overview or create a new one)
2. Click the **⋮** (three dots) menu → **Edit Dashboard**
3. Click **"+ ADD CARD"**
4. Search for **"openctrol"** or scroll to find **"Custom: Openctrol Card"**
5. Configure:
   - **Entity**: `sensor.openctrol_agent_status` (or your entity ID)
6. Click **SAVE**

## Verify Everything Works

1. **Check Entity Status**: The card should show "Online" if the agent is connected
2. **Test Power Controls**: Try the Restart/Shutdown buttons
3. **Test Touchpad**: Drag in the touchpad area to move the cursor
4. **Test Mouse Buttons**: Click the left/right/middle mouse buttons
5. **Test Keyboard**: Try clicking keyboard buttons
6. **Test Audio**: Open the sound menu and check audio devices
7. **Test Monitor Selection**: Open the screen menu and select a monitor

## Troubleshooting

### Can't Access Home Assistant

- Wait 1-2 minutes for first-time initialization
- Check container status: `docker ps --filter "name=openctrol-ha-test"`
- View logs: `docker compose logs homeassistant`

### Integration Not Found

- The integration is automatically loaded from `homeassistant/custom_components/openctrol`
- Check logs for errors: `docker compose logs homeassistant | Select-String "openctrol"`

### Can't Connect to Agent

- Verify agent is running on the host machine
- Test agent health: `http://localhost:44325/api/v1/health` (from host)
- Try using your actual host IP instead of `host.docker.internal`
- Check Windows Firewall allows connections on port 44325

### Card Not Appearing

- Verify resource is added: **Settings** → **Dashboards** → **Resources**
- Hard refresh browser: Ctrl+F5 (Windows) or Cmd+Shift+R (Mac)
- Check browser console (F12) for JavaScript errors

## Useful Commands

```powershell
# View logs
cd docker
docker compose logs -f homeassistant

# Stop server
docker compose down

# Start server
docker compose up -d

# Restart server
docker compose restart homeassistant

# Check container status
docker ps --filter "name=openctrol-ha-test"
```

## Next Steps

1. ✅ Server is starting
2. ⏳ Wait for Home Assistant to be ready (1-2 minutes)
3. ⏳ Access http://localhost:8123
4. ⏳ Complete setup wizard
5. ⏳ Configure Openctrol integration
6. ⏳ Add Openctrol card to dashboard
7. ⏳ Test all features

## Development Workflow

After initial setup:

1. **Make code changes** to integration or card
2. **Reload integration**: Developer Tools → YAML → Reload Integration → Openctrol Agent
3. **Refresh card resource**: Settings → Dashboards → Resources → Refresh icon
4. **Hard refresh browser**: Ctrl+F5
5. **Test changes** immediately - no container restart needed!

