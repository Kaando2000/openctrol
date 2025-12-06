#!/usr/bin/env python3
"""
Helper script to extract Openctrol agent connection details from Home Assistant config.
This helps you quickly get the host, port, and API key for testing.

Usage:
    python extract_ha_config.py
    
Outputs environment variable commands you can copy-paste.
"""

import json
import os
import sys
from pathlib import Path

def find_ha_config():
    """Find Home Assistant config directory."""
    # Common locations
    possible_paths = [
        Path.home() / ".homeassistant",
        Path.home() / "homeassistant",
        Path("/config"),  # Docker
        Path("C:/Users") / os.getenv("USERNAME", "user") / ".homeassistant",
    ]
    
    # Check environment variable
    ha_config = os.getenv("HOMEASSISTANT_CONFIG")
    if ha_config:
        possible_paths.insert(0, Path(ha_config))
    
    for path in possible_paths:
        storage_path = path / "config" / ".storage"
        if storage_path.exists():
            return storage_path
    
    return None

def extract_openctrol_config():
    """Extract Openctrol config from Home Assistant."""
    storage_path = find_ha_config()
    if not storage_path:
        print("Could not find Home Assistant config directory.")
        print("Set HOMEASSISTANT_CONFIG environment variable to point to your HA config dir.")
        return None
    
    # Look for core.config_entries
    config_entries_file = storage_path / "core.config_entries"
    if not config_entries_file.exists():
        print(f"Config entries file not found: {config_entries_file}")
        return None
    
    try:
        with open(config_entries_file, 'r', encoding='utf-8') as f:
            data = json.load(f)
        
        entries = data.get("data", {}).get("entries", [])
        for entry in entries:
            if entry.get("domain") == "openctrol":
                data = entry.get("data", {})
                return {
                    "host": data.get("host", ""),
                    "port": data.get("port", 44325),
                    "api_key": data.get("api_key", ""),
                    "use_ssl": data.get("use_ssl", False),
                }
        
        print("Openctrol integration not found in Home Assistant config.")
        return None
    except Exception as e:
        print(f"Error reading config: {e}")
        return None

if __name__ == "__main__":
    config = extract_openctrol_config()
    if config:
        print("\n" + "="*60)
        print("Openctrol Agent Connection Details")
        print("="*60)
        print(f"Host: {config['host']}")
        print(f"Port: {config['port']}")
        print(f"API Key: {config['api_key'][:4]}...{config['api_key'][-4:] if len(config['api_key']) > 8 else '****'}")
        print(f"Use SSL: {config['use_ssl']}")
        print("\n" + "="*60)
        print("Environment Variables (Windows PowerShell):")
        print("="*60)
        print(f'$env:OPENCTROL_HOST="{config["host"]}"')
        print(f'$env:OPENCTROL_PORT="{config["port"]}"')
        print(f'$env:OPENCTROL_API_KEY="{config["api_key"]}"')
        if config['use_ssl']:
            print('python test_openctrol_comprehensive.py --ssl')
        else:
            print('python test_openctrol_comprehensive.py')
        print("\n" + "="*60)
        print("Environment Variables (Linux/Mac):")
        print("="*60)
        print(f'export OPENCTROL_HOST="{config["host"]}"')
        print(f'export OPENCTROL_PORT="{config["port"]}"')
        print(f'export OPENCTROL_API_KEY="{config["api_key"]}"')
        if config['use_ssl']:
            print('python test_openctrol_comprehensive.py --ssl')
        else:
            print('python test_openctrol_comprehensive.py')
        print("\n" + "="*60)
        print("Direct Command:")
        print("="*60)
        cmd = f'python test_openctrol_comprehensive.py --host {config["host"]} --port {config["port"]} --api-key "{config["api_key"]}"'
        if config['use_ssl']:
            cmd += ' --ssl'
        print(cmd)
    else:
        print("\nTo run tests manually, use:")
        print("  python test_openctrol_comprehensive.py --host <agent-ip> --port 44325 --api-key <api-key>")
        print("\nOr set environment variables:")
        print("  OPENCTROL_HOST=<agent-ip>")
        print("  OPENCTROL_PORT=44325")
        print("  OPENCTROL_API_KEY=<api-key>")

