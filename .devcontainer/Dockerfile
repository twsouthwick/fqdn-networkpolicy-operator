FROM mcr.microsoft.com/devcontainers/dotnet:10.0

# Install kind
RUN <<EOF
    set -e
    KIND_VERSION=v0.27.0
    curl -Lo /tmp/kind "https://kind.sigs.k8s.io/dl/${KIND_VERSION}/kind-linux-$(dpkg --print-architecture)"
    chmod +x /tmp/kind
    mv /tmp/kind /usr/local/bin/kind
EOF

# Install kubectl
RUN <<EOF
    set -e
    curl -fsSL "https://dl.k8s.io/release/$(curl -L -s https://dl.k8s.io/release/stable.txt)/bin/linux/$(dpkg --print-architecture)/kubectl" -o /tmp/kubectl
    install -o root -g root -m 0755 /tmp/kubectl /usr/local/bin/kubectl
    rm /tmp/kubectl
EOF

# Install Helm
RUN <<EOF
    set -e
    curl -fsSL https://raw.githubusercontent.com/helm/helm/main/scripts/get-helm-3 | bash
EOF

# Install Docker CE (docker-in-docker)
RUN <<EOF
    set -e
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" > /etc/apt/sources.list.d/docker.list
    apt-get update
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
    apt-get clean
    rm -rf /var/lib/apt/lists/*
EOF

# Install Azure CLI
RUN <<EOF
    set -e
    curl -sL https://aka.ms/InstallAzureCLIDeb | bash
    apt-get clean
    rm -rf /var/lib/apt/lists/*
EOF

# Install GitHub CLI
RUN <<EOF
    set -e
    curl -fsSL https://cli.github.com/packages/githubcli-archive-keyring.gpg | dd of=/usr/share/keyrings/githubcli-archive-keyring.gpg
    chmod go+r /usr/share/keyrings/githubcli-archive-keyring.gpg
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/usr/share/keyrings/githubcli-archive-keyring.gpg] https://cli.github.com/packages stable main" > /etc/apt/sources.list.d/github-cli.list
    apt-get update
    apt-get install -y gh
    apt-get clean
    rm -rf /var/lib/apt/lists/*
EOF

# Add vscode user to docker group
RUN usermod -aG docker vscode

# Docker-in-docker entrypoint: start dockerd on container start
COPY docker-init.sh /usr/local/share/docker-init.sh
RUN chmod +x /usr/local/share/docker-init.sh
ENTRYPOINT ["/usr/local/share/docker-init.sh"]
CMD ["sleep", "infinity"]
