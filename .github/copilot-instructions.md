# Copilot Instructions

## Commit Messages

Use [Conventional Commits](https://www.conventionalcommits.org/) format for all commit messages:

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

## Helm Chart

The chart lives in `charts/fqdn-networkpolicy-operator/`. Its Kubernetes manifests are thin templates over the generated files in `artifacts/k8s/`, which KubeOps regenerates on every build via `<GenerateOperatorResources>true</GenerateOperatorResources>` in `src/Operator/Operator.csproj`.

### Source of truth for each resource

| Chart template | Mirrors / sourced from |
|---|---|
| `templates/deployment.yaml` | `artifacts/k8s/deployment.yaml` |
| `templates/rbac.yaml` | `artifacts/k8s/operator-role.yaml` + `artifacts/k8s/operator-role-binding.yaml` |
| `templates/serviceaccount.yaml` | _(no artifact equivalent — chart-only resource)_ |
| `crds/` (populated at CI package time) | `artifacts/k8s/fqdnnetworkpolicies_fqdnnetpol_swick_dev.yaml` |

The `crds/` directory is **not committed** — it is populated during CI by copying the KubeOps-generated CRD file before running `helm package`.

### When the CRD changes

The CRD (`artifacts/k8s/fqdnnetworkpolicies_fqdnnetpol_swick_dev.yaml`) is auto-generated. No manual chart changes are needed — CI always copies the freshly built file into `crds/` before packaging.

### When RBAC rules change

If the operator needs new API permissions, update **`artifacts/k8s/operator-role.yaml`** (KubeOps may regenerate this — check after each build), then mirror the same `rules` entries into **`charts/fqdn-networkpolicy-operator/templates/rbac.yaml`**.

Both files must stay in sync. The chart template is the canonical install path for end users; the artifact file is what the integration-test cluster uses via `kubectl apply`.

### When the ServiceAccount changes

The chart uses a dedicated `ServiceAccount` (rendered by `templates/serviceaccount.yaml`). The `ClusterRoleBinding` in `templates/rbac.yaml` binds to it via `{{ include "fqdn-networkpolicy-operator.serviceAccountName" . }}`, which resolves to the Helm release's full name.

The `artifacts/k8s/operator-role-binding.yaml` binds to the `default` SA — this is intentional for the dev/CI kustomize path and should **not** be changed to match the chart.

### When the Deployment changes

Update `artifacts/k8s/deployment.yaml` first (KubeOps may regenerate it). Then mirror structural changes (new env vars, volume mounts, ports, etc.) into `charts/fqdn-networkpolicy-operator/templates/deployment.yaml`. Resource limits/requests are driven by `values.yaml` — update defaults there rather than hardcoding.

### Versioning

`Chart.yaml` has `version: 0.0.0` and `appVersion: 0.0.0` as placeholders. Both are overridden at package time by CI using GitVersion:

```
helm package charts/fqdn-networkpolicy-operator \
  --version <semVer> \
  --app-version <semVer>
```

The deployment template uses `{{ .Values.image.tag | default .Chart.AppVersion }}` so the correct image is pulled without users needing to set `image.tag` explicitly.

### Local validation

```bash
helm lint charts/fqdn-networkpolicy-operator
helm template fqdn-networkpolicy-operator charts/fqdn-networkpolicy-operator
```
