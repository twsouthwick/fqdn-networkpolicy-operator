# Development Guide

This document covers how to build, run, and contribute to the FQDN Network Policy Operator.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker](https://docs.docker.com/get-docker/) or [Podman](https://podman.io/)
- [kind](https://kind.sigs.k8s.io/) — for running a local Kubernetes cluster
- `kubectl` configured against your cluster

### Using Podman

If your host uses Podman (rootless by default), kind requires cgroup delegation.
Configure this **on your host** before opening the devcontainer:

**Podman Desktop / Podman Machine (macOS/Windows):**

```bash
podman machine stop
podman machine set --rootful
podman machine start
```

**Linux with systemd:**

```bash
sudo loginctl enable-linger $USER
sudo mkdir -p /etc/systemd/system/user@.service.d
echo -e '[Service]\nDelegate=yes' | \
  sudo tee /etc/systemd/system/user@.service.d/delegate.conf
sudo systemctl daemon-reload
```

Then rebuild the devcontainer.

## Project Structure

```
src/Operator/                    # .NET 10 / KubeOps operator
  Controllers/
    V1FqdnNetworkPolicyController.cs  # Reconciliation logic
  Entities/
    V1FqdnNetworkPolicyEntity.cs      # CRD schema / C# model
  Program.cs                          # Host setup
samples/fqdn-provider/           # Sample provider service (TypeScript/Express)
artifacts/k8s/                   # Generated CRD, RBAC, and deployment manifests
test/Operator.IntegrationTests/  # xUnit integration tests (requires a live cluster)
```

## Getting Started

### 1. Create a local cluster

```bash
kind create cluster --name fqdn-operator --wait 60s
```

### 2. Apply the CRD and RBAC

```bash
kubectl apply -f artifacts/k8s/fqdnnetworkpolicies_fqdnnetpol_swick_dev.yaml
kubectl apply -f artifacts/k8s/operator-role.yaml -f artifacts/k8s/operator-role-binding.yaml
```

### 3. Build the operator

```bash
dotnet build src/Operator/Operator.csproj
```

### 4. Run the operator locally

```bash
dotnet run --project src/Operator/Operator.csproj
```

The operator will connect to whichever cluster `kubectl` is currently pointing at.

## Sample Provider

The [`samples/fqdn-provider`](samples/fqdn-provider/) directory contains a minimal TypeScript/Express provider service. To deploy it to your local cluster:

```bash
docker build -t fqdn-provider:latest samples/fqdn-provider
kind load docker-image fqdn-provider:latest --name fqdn-operator
kubectl apply -f samples/fqdn-provider/deployment.yaml
```

To change what addresses and ports the provider exposes, edit the arrays in [samples/fqdn-provider/src/index.ts](samples/fqdn-provider/src/index.ts) and rebuild.

## Running Tests

Integration tests require a reachable Kubernetes cluster. Tests are automatically skipped when no cluster is available.

```bash
dotnet test test/Operator.IntegrationTests/Operator.IntegrationTests.csproj
```

## VS Code Tasks

Several VS Code tasks are defined to streamline common workflows:

| Task | Description |
|---|---|
| `build` | Build the operator project |
| `setup-operator` | Apply CRD and RBAC to the cluster |
| `build-and-setup` | Build and apply cluster prerequisites |
| `kind: create cluster` | Create the local kind cluster |
| `kind: delete cluster` | Delete the local kind cluster |
| `sample: deploy` | Build and deploy the sample provider |
| `start sample debug` | Full setup: build, deploy sample, add hosts entry, start port-forward |
| `stop sample debug` | Tear down debug setup |

## Tech Stack

- [.NET 10](https://dotnet.microsoft.com/) / ASP.NET Core
- [KubeOps](https://github.com/buehler/dotnet-operator-sdk) — Kubernetes operator SDK for .NET
- [kind](https://kind.sigs.k8s.io/) — local Kubernetes clusters for development and testing

## Contributing

Commit messages should follow the [Conventional Commits](https://www.conventionalcommits.org/) format:

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

Common types: `feat`, `fix`, `docs`, `refactor`, `test`, `chore`.
