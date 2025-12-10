# pydms Offline Installation Package

This folder contains pydms and all its dependencies for offline installation.

## Contents
- `pydms-0.0.1.7-py3-none-any.whl` - pydms package
- `fire-0.7.1-py3-none-any.whl` - dependency
- `termcolor-3.2.0-py3-none-any.whl` - dependency
- `install.bat` - Windows install script
- `install.sh` - Linux/Mac install script

## Installation

### Windows
1. Copy this entire `pydms_offline` folder to the offline machine
2. Open a command prompt in this folder
3. Run: `install.bat`

### Linux/Mac
1. Copy this entire `pydms_offline` folder to the offline machine
2. Open a terminal in this folder
3. Run: `chmod +x install.sh && ./install.sh`

### Manual Installation
```bash
pip install --no-index --find-links=. pydms
```
