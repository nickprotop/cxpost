#!/bin/bash
# CXPost Installer
# Downloads and installs the latest release from GitHub
# Usage: curl -fsSL https://raw.githubusercontent.com/nickprotop/cxpost/main/install.sh | bash
# Copyright (c) Nikolaos Protopapas. All rights reserved.
# Licensed under the MIT License.

set -e

REPO="nickprotop/cxpost"
INSTALL_DIR="$HOME/.local/bin"
CONFIG_DIR="${XDG_CONFIG_HOME:-$HOME/.config}/cxpost"
DATA_DIR="${XDG_DATA_HOME:-$HOME/.local/share}/cxpost"

echo "Installing CXPost..."

# Detect architecture
ARCH=$(uname -m)
case "$ARCH" in
    x86_64)  BINARY="cxpost-linux-x64" ;;
    aarch64) BINARY="cxpost-linux-arm64" ;;
    *)
        echo "Error: Unsupported architecture: $ARCH"
        echo "CXPost supports x86_64 and aarch64 (ARM64)."
        exit 1 ;;
esac

# Get latest release info
echo "Fetching latest release..."
RELEASE_INFO=$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest")
TAG=$(echo "$RELEASE_INFO" | grep '"tag_name"' | head -1 | sed 's/.*"tag_name": "\(.*\)".*/\1/')
VERSION="${TAG#v}"

if [ -z "$TAG" ]; then
    echo "Error: Could not determine latest release."
    exit 1
fi

echo "Latest version: $VERSION"

# Download binary
DOWNLOAD_URL="https://github.com/$REPO/releases/download/$TAG/$BINARY"
echo "Downloading $BINARY..."

mkdir -p "$INSTALL_DIR"
curl -fsSL "$DOWNLOAD_URL" -o "$INSTALL_DIR/cxpost"
chmod +x "$INSTALL_DIR/cxpost"

# Create directories
mkdir -p "$CONFIG_DIR"
mkdir -p "$DATA_DIR"

# Download uninstaller
curl -fsSL "https://raw.githubusercontent.com/$REPO/main/uninstall.sh" -o "$INSTALL_DIR/cxpost-uninstall.sh"
chmod +x "$INSTALL_DIR/cxpost-uninstall.sh"

# Ensure PATH
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
            echo "Added $INSTALL_DIR to PATH in $SHELL_RC"
        fi
    fi
fi

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  ✓ CXPost v$VERSION installed!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""
echo "  Binary:  $INSTALL_DIR/cxpost"
echo "  Config:  $CONFIG_DIR/"
echo "  Data:    $DATA_DIR/"
echo ""
echo "  Run:     cxpost"
echo "  Remove:  cxpost-uninstall.sh"
echo ""
if [[ ":$PATH:" != *":$INSTALL_DIR:"* ]]; then
    echo "  Note: Restart your shell or run:"
    echo "    source ~/.bashrc  (or ~/.zshrc)"
fi
