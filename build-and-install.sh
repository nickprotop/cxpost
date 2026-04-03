#!/bin/bash
# CXPost Local Build & Install
# Builds from source and installs to ~/.local/bin
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

INSTALL_DIR="$HOME/.local/bin"
CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/cxpost"
DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/cxpost"

# Detect architecture
ARCH=$(uname -m)
case "$ARCH" in
    x86_64)  RID="linux-x64" ;;
    aarch64) RID="linux-arm64" ;;
    *)       echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac

# Get version from latest tag
VERSION=$(git describe --tags --abbrev=0 2>/dev/null || echo "v0.0.1")
VERSION="${VERSION#v}"

echo "Building CXPost v$VERSION for $RID..."

# Build
dotnet publish CXPost/CXPost.csproj \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:Version="$VERSION" \
    -o ./publish/"$RID"

# Create directories
mkdir -p "$INSTALL_DIR"
mkdir -p "$CONFIG_DIR"
mkdir -p "$DATA_DIR"

# Install binary
cp "./publish/$RID/CXPost" "$INSTALL_DIR/cxpost"
chmod +x "$INSTALL_DIR/cxpost"

echo ""
echo "✓ Installed cxpost to $INSTALL_DIR/cxpost"

# Ensure PATH includes ~/.local/bin
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    SHELL_RC=""
    if [ -f "$HOME/.zshrc" ]; then
        SHELL_RC="$HOME/.zshrc"
    elif [ -f "$HOME/.bashrc" ]; then
        SHELL_RC="$HOME/.bashrc"
    fi

    if [ -n "$SHELL_RC" ]; then
        if ! grep -q "$INSTALL_DIR" "$SHELL_RC" 2>/dev/null; then
            echo "export PATH=\"$INSTALL_DIR:\$PATH\"" >> "$SHELL_RC"
            echo "  Added $INSTALL_DIR to PATH in $SHELL_RC"
            echo "  Run: source $SHELL_RC"
        fi
    fi
fi

echo ""
echo "Run: cxpost"
