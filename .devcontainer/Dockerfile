FROM mcr.microsoft.com/dotnet/sdk:10.0

# Install common utilities and create non-root user for devcontainer
ARG USERNAME=vscode
ARG USER_UID=1000
ARG USER_GID=$USER_UID

RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
    && apt-get -y install --no-install-recommends \
        git \
        curl \
        wget \
        ca-certificates \
        gnupg \
        lsb-release \
        sudo \
        openssh-client \
        less \
        procps \
        vim \
        nano \
    && apt-get autoremove -y && apt-get clean -y && rm -rf /var/lib/apt/lists/*

# Install Azure CLI and GitHub CLI
RUN <<EOF
    set -e
    # Azure CLI
    curl -sL https://aka.ms/InstallAzureCLIDeb | bash

    # GitHub CLI
    curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg -o /usr/share/keyrings/githubcli-archive-keyring.gpg
    chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" > /etc/apt/sources.list.d/github-cli.list
    apt-get update
    apt-get install -y gh
    apt-get clean -y
    rm -rf /var/lib/apt/lists/*
EOF

# Create the vscode user (handle case where GID/UID may already exist)
RUN <<EOF
    set -e
    if getent group $USER_GID > /dev/null 2>&1; then
        groupmod -n $USERNAME $(getent group $USER_GID | cut -d: -f1) 2>/dev/null || true
    else
        groupadd --gid $USER_GID $USERNAME
    fi
    if getent passwd $USER_UID > /dev/null 2>&1; then
        usermod -l $USERNAME -d /home/$USERNAME -m $(getent passwd $USER_UID | cut -d: -f1) 2>/dev/null || true
    else
        useradd --uid $USER_UID --gid $USER_GID -m $USERNAME
    fi
    echo "$USERNAME ALL=(root) NOPASSWD:ALL" > /etc/sudoers.d/$USERNAME
    chmod 0440 /etc/sudoers.d/$USERNAME
EOF

# Install kind
RUN <<EOF
    set -e
    KIND_VERSION=v0.27.0
    curl -Lo /tmp/kind "https://kind.sigs.k8s.io/dl/${KIND_VERSION}/kind-linux-$(dpkg --print-architecture)"
    chmod +x /tmp/kind
    mv /tmp/kind /usr/local/bin/kind
EOF

# Set the default user
USER $USERNAME
