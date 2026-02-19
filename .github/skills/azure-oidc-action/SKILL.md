---
name: azure-oidc-setup
description: Guides through Azure AD and OIDC federation configuration for GitHub Actions deployments. Use when setting up Azure authentication, creating service principals, configuring federated credentials, or troubleshooting OIDC authentication errors like AADSTS700016 or AADSTS70021.
metadata:
  author: content-moderation
  version: "1.0"
---

# Azure OIDC Setup for GitHub Actions

You are an expert in configuring Azure AD and OIDC federation for GitHub Actions infrastructure deployments.

## Context

When helping users set up Azure authentication for GitHub Actions, always recommend OIDC federation over stored service principal secrets because:

- No secrets stored in GitHub
- Short-lived tokens (~10 minutes)
- No credential rotation required
- Better security posture and audit trail

## Required Azure Resources

1. **Azure AD Application** - Identity for GitHub Actions
2. **Service Principal** - For RBAC assignments
3. **Federated Credentials** - OIDC authentication configuration
4. **RBAC Role Assignment** - Contributor permission at appropriate scope

## Required GitHub Secrets

| Secret | Description |
|--------|-------------|
| `AZURE_CLIENT_ID` | Application (Client) ID from Azure AD App Registration |
| `AZURE_TENANT_ID` | Directory (Tenant) ID |
| `AZURE_SUBSCRIPTION_ID` | Target subscription ID |

## OIDC Subject Claim Formats

Configure the correct federated credential subject based on workflow trigger:

| Trigger | Subject Claim Format |
|---------|---------------------|
| Branch push | `repo:<owner>/<repo>:ref:refs/heads/<branch>` |
| Pull request | `repo:<owner>/<repo>:pull_request` |
| Environment | `repo:<owner>/<repo>:environment:<name>` |
| Tag | `repo:<owner>/<repo>:ref:refs/tags/<tag>` |

## Setup Commands

### Create App and Service Principal

```bash
APP_NAME="<project-name>-github-actions"
az ad app create --display-name "$APP_NAME"
CLIENT_ID=$(az ad app list --display-name "$APP_NAME" --query "[0].appId" -o tsv)
az ad sp create --id "$CLIENT_ID"
```

### Assign RBAC

```bash
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
az role assignment create \
  --assignee "$CLIENT_ID" \
  --role "Contributor" \
  --scope "/subscriptions/$SUBSCRIPTION_ID"
```

### Create Federated Credential

```bash
APP_OBJECT_ID=$(az ad app list --display-name "$APP_NAME" --query "[0].id" -o tsv)
az ad app federated-credential create \
  --id "$APP_OBJECT_ID" \
  --parameters '{
    "name": "github-main-branch",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:<GITHUB_ORG>/<GITHUB_REPO>:ref:refs/heads/main",
    "description": "GitHub Actions - main branch",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

## Common Troubleshooting

| Error | Cause | Solution |
|-------|-------|----------|
| `AADSTS700016` | Client ID incorrect or app doesn't exist | Verify app exists with `az ad app list` |
| `AADSTS70021` | Subject claim mismatch | Verify GitHub org/repo/branch matches federated credential exactly |
| `AuthorizationFailed` | Missing RBAC permissions | Re-assign Contributor role to service principal |
| `Service principal not found` | SP not created | Run `az ad sp create --id "$CLIENT_ID"` |

## Multi-Tenant Considerations

When users have multiple Azure tenants:

- Always ask which tenant and subscription to use
- Use explicit `--tenant` parameter when needed
- Don't assume default tenant is correct
- Verify with `az account show` before proceeding

## Security Best Practices

1. Use OIDC federation instead of stored secrets
2. Apply principle of least privilege - scope RBAC to resource group when possible
3. Enable branch protection on deployment branches
4. Review Azure AD sign-in logs periodically
5. Document federated credential configurations for auditing