#!/bin/bash
set -euo pipefail

# Install dotnet tools if not already installed
dotnet tool restore

# Wait for Docker to be ready
echo "Waiting for Docker..."
while ! docker info >/dev/null 2>&1; do
    sleep 1
done
echo "Docker is ready"

# Ensure the persistent kind cluster is running
echo "Starting kind cluster..."
kind get clusters 2>/dev/null | grep -q '^fqdn-operator$' || kind create cluster --name fqdn-operator --wait 60s
kind export kubeconfig --name fqdn-operator
kubectl cluster-info --context kind-fqdn-operator
echo "kind cluster ready"

dotnet restore

# Build the fqdn-provider Docker image and load it into the kind cluster
echo "Building fqdn-provider image..."
docker build -t fqdn-provider:latest /workspaces/fqdn-networkpolicy-operator/samples/fqdn-provider
echo "Loading fqdn-provider image into kind cluster..."
kind load docker-image fqdn-provider:latest --name fqdn-operator
echo "fqdn-provider image ready"

alias k=kubectl