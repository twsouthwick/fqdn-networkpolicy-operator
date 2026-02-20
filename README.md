# FQDN Network Policy Operator

A Kubernetes operator that creates and manages `NetworkPolicy` resources with egress rules based on FQDNs (Fully Qualified Domain Names). Because Kubernetes `NetworkPolicy` only supports IP-based egress rules, this operator bridges the gap by continuously resolving domain names to IP addresses and keeping the generated policies up to date.

## How It Works

1. You create a `FqdnNetworkPolicy` custom resource (CR) that describes your desired egress rules using domain names, raw IPs/CIDRs, or references to external _provider services_.
2. The operator reconciles the CR every 30 seconds (2 minutes on failure):
   - Resolves each FQDN in `domains` egress items to IP addresses via DNS.
   - Queries any referenced provider services for addresses and ports.
   - Creates or updates a `NetworkPolicy` in the same namespace, owned by the CR (so it is garbage-collected when the CR is deleted).
3. The CR's `status` is updated with the total resolved IP count and a `lastReconciled` timestamp.

```
provider CR  ──►  Operator  ──►  NetworkPolicy (IP-based egress)
                     │
                     ├─ DNS resolution of FQDNs
                     └─ HTTP calls to provider services → more IPs/FQDNs
```

## Custom Resource: `FqdnNetworkPolicy`

**API group/version:** `fqdnnetpol.swick.dev/v1alpha1`  
**Kind:** `FqdnNetworkPolicy`

### Spec

Each `spec.egress[]` item is **one of**:

**Domain-based rule** — resolved by the operator:

| Field | Description |
|---|---|
| `spec.egress[].domains` | List of FQDNs or raw IP/CIDR strings to resolve via DNS. |
| `spec.egress[].ports` | Port/protocol pairs applied to this egress rule. |

**Provider-based rule** — ports come from the provider service response:

| Field | Description |
|---|---|
| `spec.egress[].externalProvider` | Reference to a single external provider service (see below). |

| Other field | Description |
|---|---|
| `spec.policy` | *(required)* A standard `V1NetworkPolicySpec` (podSelector, policyTypes, static egress/ingress rules). Merged with operator-resolved rules. |

### Provider Service Reference

A _provider service_ is any HTTP service reachable within the cluster that exposes an endpoint returning a JSON body with the addresses it wants to allow and the ports those addresses should be reachable on:

```json
{
  "addresses": ["example.com", "10.0.0.1/32"],
  "ports": [
    { "port": 443, "protocol": "TCP" },
    { "port": 80, "protocol": "TCP" }
  ]
}
```

Each entry in `addresses` can be a hostname/FQDN (resolved via DNS), a plain IP, or a CIDR. The `ports` array maps directly to the `ports` field of the generated `V1NetworkPolicyEgressRule`.

| Field | Default | Description |
|---|---|---|
| `serviceName` | *(required)* | Kubernetes Service name. The operator calls `http://<serviceName>.<namespace>:<port><path>`. |
| `name` | — | Optional label for logging. |
| `port` | `7942` | Port the service listens on. |
| `path` | `/fqdnList` | HTTP path that returns the provider response. |

### Status

| Field | Description |
|---|---|
| `status.ready` | Whether the last reconciliation succeeded. |
| `status.ipCount` | Number of IP blocks resolved in the last reconciliation. |
| `status.domainCount` | Number of distinct FQDNs across all domain-based egress rules. |
| `status.warningCount` | Number of non-fatal warnings in the last reconciliation (e.g. failed DNS lookups). |
| `status.lastReconciled` | Timestamp of the last successful reconciliation. |
| `status.lastModified` | Timestamp of the last time the generated `NetworkPolicy` was actually changed. |
| `status.message` | Human-readable summary of the last reconciliation result. |

### Example

```yaml
apiVersion: fqdnnetpol.swick.dev/v1alpha1
kind: FqdnNetworkPolicy
metadata:
  name: my-egress-policy
  namespace: default
spec:
  egress:
    - domains:
        - google.com
        - api.example.com
      ports:
        - port: 443
          protocol: TCP
        - port: 80
          protocol: TCP
    - externalProvider:
        serviceName: fqdn-provider
        port: 7942
        path: /fqdnList
  policy:
    podSelector: {}
    policyTypes:
      - Egress
```

This generates a `NetworkPolicy` named `my-egress-policy` with one egress rule for the resolved `google.com`/`api.example.com` IPs on ports 80 and 443, plus a separate rule from the provider service (which supplies its own addresses and ports).

## Sample Provider Service

The [`samples/fqdn-provider`](samples/fqdn-provider/) directory contains a minimal TypeScript/Express provider. Edit the `addresses` and `ports` arrays in [samples/fqdn-provider/src/index.ts](samples/fqdn-provider/src/index.ts) to control what it exposes, then deploy it to your cluster.

## Project Structure

```
src/Operator/                 # .NET 10 / KubeOps operator
  Controllers/
    V1FqdnNetworkPolicyController.cs # Reconciliation logic
  Entities/
    V1FqdnNetworkPolicyEntity.cs   # CRD schema / C# model
  Program.cs                  # Host setup
samples/fqdn-provider/        # Sample provider (TypeScript/Express)
artifacts/k8s/                # Generated CRD, RBAC, and deployment manifests
test/Operator.IntegrationTests/  # xUnit integration tests (requires a live cluster)
```

## Prerequisites

- Kubernetes cluster (e.g. [kind](https://kind.sigs.k8s.io/))
- .NET 10 SDK
- `kubectl` configured against your cluster

## Getting Started

### 1. Create a kind cluster

```bash
kind create cluster --name fqdn-operator --wait 60s
```

### 2. Apply the CRD and RBAC

```bash
kubectl apply -f artifacts/k8s/fqdnnetworkpolicies_fqdnnetpol_swick_dev.yaml
kubectl apply -f artifacts/k8s/operator-role.yaml -f artifacts/k8s/operator-role-binding.yaml
```

### 3. Run the operator locally

```bash
dotnet run --project src/Operator/Operator.csproj
```

### 4. Deploy the sample provider

```bash
docker build -t fqdn-provider:latest samples/fqdn-provider
kind load docker-image fqdn-provider:latest --name fqdn-operator
kubectl apply -f samples/fqdn-provider/deployment.yaml
```

The sample deployment manifest also creates the `provider` CR that points the operator at the service.

## Running Tests

Integration tests require a reachable Kubernetes cluster:

```bash
dotnet test test/Operator.IntegrationTests/Operator.IntegrationTests.csproj
```

Tests are automatically skipped when no cluster is available.

## Tech Stack

- [.NET 10](https://dotnet.microsoft.com/) / ASP.NET Core
- [KubeOps](https://github.com/buehler/dotnet-operator-sdk) — Kubernetes operator SDK for .NET
- [kind](https://kind.sigs.k8s.io/) — local Kubernetes clusters for development and testing
