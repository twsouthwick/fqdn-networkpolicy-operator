#!/bin/bash
set -e

# Install dotnet tools if not already installed
if ! command -v kubeops &> /dev/null; then
    echo "Installing KubeOps CLI..."
    dotnet tool install --global KubeOps.Cli
fi

# Wait for Docker to be ready
echo "Waiting for Docker..."
while ! docker info >/dev/null 2>&1; do
    sleep 1
done
echo "Docker is ready"

# Check if minikube is already running
if minikube status >/dev/null 2>&1; then
    echo "Minikube is already running"
else
    echo "Starting minikube..."
    minikube start --driver=docker --memory=4096 --cpus=2
fi

echo "Minikube is ready!"
kubectl cluster-info
