#!/bin/bash
echo "Installing pydms from offline packages..."
pip install --no-index --find-links=. pydms

if [ $? -eq 0 ]; then
    echo "Installation complete!"
else
    echo "Installation failed. Please check errors above."
    exit 1
fi
