FROM mcr.microsoft.com/devcontainers/dotnet:10.0

# Install kind
RUN <<EOF
    set -e
    KIND_VERSION=v0.27.0
    curl -Lo /tmp/kind "https://kind.sigs.k8s.io/dl/${KIND_VERSION}/kind-linux-$(dpkg --print-architecture)"
    chmod +x /tmp/kind
    mv /tmp/kind /usr/local/bin/kind
EOF
