#!/bin/sh
set -e

# Start Docker daemon in the background
if [ -x "$(command -v dockerd)" ]; then
    sudo rm -f /var/run/docker.pid
    sudo dockerd &>/tmp/dockerd.log &
    # Wait for Docker socket
    while [ ! -S /var/run/docker.sock ]; do
        sleep 0.5
    done
fi

exec "$@"
