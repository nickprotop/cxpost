#!/bin/bash
# CXPost Uninstaller
# Removes CXPost binary, optionally config and data
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

INSTALL_DIR="$HOME/.local/bin"
CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/cxpost"
DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/cxpost"

echo "CXPost Uninstaller"
echo ""

# Remove binary
if [ -f "$INSTALL_DIR/cxpost" ]; then
    rm "$INSTALL_DIR/cxpost"
    echo "✓ Removed $INSTALL_DIR/cxpost"
else
    echo "  Binary not found at $INSTALL_DIR/cxpost"
fi

# Remove uninstaller
if [ -f "$INSTALL_DIR/cxpost-uninstall.sh" ]; then
    rm "$INSTALL_DIR/cxpost-uninstall.sh"
fi

# Ask about config
if [ -d "$CONFIG_DIR" ]; then
    echo ""
    echo "Config directory: $CONFIG_DIR"
    echo "  Contains: config.yaml, credentials"
    read -p "  Remove config? [y/N] " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        rm -rf "$CONFIG_DIR"
        echo "  ✓ Removed $CONFIG_DIR"
    else
        echo "  Kept $CONFIG_DIR"
    fi
fi

# Ask about data
if [ -d "$DATA_DIR" ]; then
    echo ""
    echo "Data directory: $DATA_DIR"
    echo "  Contains: mail.db, contacts.db (cached emails)"
    read -p "  Remove data? [y/N] " -n 1 -r
    echo
    if [[ $REPLY =~ ^[Yy]$ ]]; then
        rm -rf "$DATA_DIR"
        echo "  ✓ Removed $DATA_DIR"
    else
        echo "  Kept $DATA_DIR"
    fi
fi

# Clean PATH from shell config
for RC in "$HOME/.bashrc" "$HOME/.zshrc"; do
    if [ -f "$RC" ] && grep -q "$INSTALL_DIR" "$RC" 2>/dev/null; then
        sed -i "\|$INSTALL_DIR|d" "$RC"
        echo ""
        echo "✓ Removed PATH entry from $RC"
    fi
done

echo ""
echo "✓ CXPost uninstalled."
