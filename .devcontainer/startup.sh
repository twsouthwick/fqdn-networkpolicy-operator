#!/bin/bash
set -euo pipefail

# Fix gh credential helper path if it points to a non-existent binary
# (VS Code copies the host's ~/.gitconfig which may reference a different gh location)
GH_PATH="$(command -v gh 2>/dev/null || true)"
if [ -n "$GH_PATH" ]; then
    git config --global --get-all credential.https://github.com.helper 2>/dev/null \
        | grep -q 'gh auth git-credential' && \
        git config --global --replace-all credential.https://github.com.helper "!${GH_PATH} auth git-credential" || true
    git config --global --get-all credential.https://gist.github.com.helper 2>/dev/null \
        | grep -q 'gh auth git-credential' && \
        git config --global --replace-all credential.https://gist.github.com.helper "!${GH_PATH} auth git-credential" || true
fi

# Install dotnet tools if not already installed
dotnet tool restore

# The docker-in-docker feature resets iptables to legacy, which lacks
# NAT table support in this container environment. Switch to nft backend.
if ! sudo iptables -t nat -L >/dev/null 2>&1; then
    echo "Switching iptables to nft backend..."
    sudo update-alternatives --set iptables /usr/sbin/iptables-nft
    sudo update-alternatives --set ip6tables /usr/sbin/ip6tables-nft
fi

# If dockerd is not running (e.g. docker-init.sh failed due to iptables),
# clean up stale state and start it directly.
if ! docker info >/dev/null 2>&1; then
    echo "Starting Docker daemon..."
    sudo pkill dockerd 2>/dev/null || true
    sudo pkill containerd 2>/dev/null || true
    sleep 1
    sudo find /run /var/run -iname 'docker*.pid' -delete 2>/dev/null || :
    sudo find /run /var/run -iname 'container*.pid' -delete 2>/dev/null || :
    sudo rm -f /var/run/docker.sock
    sudo sh -c 'dockerd &>/tmp/dockerd.log &'
fi

# Wait for Docker to be ready
echo "Waiting for Docker daemon..."
while ! docker info >/dev/null 2>&1; do
    sleep 1
done
echo "Docker daemon is ready"

# Ensure the persistent kind cluster is running
start_kind_cluster() {
    echo "Starting kind cluster..."
    if kind get clusters 2>/dev/null | grep -q '^fqdn-operator$'; then
        echo "kind cluster already exists"
    else
        if ! kind create cluster --name fqdn-operator --config /workspaces/fqdn-networkpolicy-operator/.devcontainer/kind-config.yaml --wait 60s 2>&1; then
            echo "ERROR: Failed to create kind cluster."
            return 1
        fi
    fi
    kind export kubeconfig --name fqdn-operator
    kubectl cluster-info --context kind-fqdn-operator
    echo "kind cluster ready"
}

if ! start_kind_cluster; then
    echo "Continuing without a kind cluster. Create one manually with: kind create cluster --name fqdn-operator"
    dotnet restore
    exit 0
fi

dotnet restore

# Build the fqdn-provider Docker image and load it into the kind cluster
echo "Building fqdn-provider image..."
docker build -t fqdn-provider:latest /workspaces/fqdn-networkpolicy-operator/samples/fqdn-provider
echo "Loading fqdn-provider image into kind cluster..."
kind load docker-image fqdn-provider:latest --name fqdn-operator
echo "fqdn-provider image ready"

alias k=kubectl