# linerule-cs dev container.
#
# Holds the .NET 10 SDK + every host-side tool used by `just <target>`. After
# `docker compose build`, every Justfile command can be run from the host via
# `docker compose run --rm dev <…>` without touching the host toolchain.
#
# Cross-platform build coverage:
#   - Linerule.Core / Config / Platform / *.Tests target net10.0 — built and
#     tested fully in this container.
#   - Linerule.Platform.Windows / Cli target net10.0-windows — reference
#     assemblies are platform-neutral so `dotnet build` is expected to succeed
#     here, but the produced binary only runs on Windows.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dev

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=0 \
    DOTNET_ENVIRONMENT=Development \
    INSIDE_CONTAINER=1

ARG TYPOS_VERSION=1.46.1
ARG ACTIONLINT_VERSION=1.7.12
ARG LEFTHOOK_VERSION=2.1.6
ARG JUST_VERSION=1.51.0

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        unzip \
        sudo \
        nodejs \
        npm \
        clang \
        zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

# typos
RUN curl -fsSL "https://github.com/crate-ci/typos/releases/download/v${TYPOS_VERSION}/typos-v${TYPOS_VERSION}-x86_64-unknown-linux-musl.tar.gz" \
        | tar -xz -C /usr/local/bin ./typos \
    && chmod +x /usr/local/bin/typos

# actionlint
RUN curl -fsSL "https://github.com/rhysd/actionlint/releases/download/v${ACTIONLINT_VERSION}/actionlint_${ACTIONLINT_VERSION}_linux_amd64.tar.gz" \
        | tar -xz -C /usr/local/bin actionlint \
    && chmod +x /usr/local/bin/actionlint

# lefthook (v2 ships .deb/.apk/.rpm only — pick .deb on the Debian-based SDK image).
RUN curl -fsSL "https://github.com/evilmartians/lefthook/releases/download/v${LEFTHOOK_VERSION}/lefthook_${LEFTHOOK_VERSION}_amd64.deb" -o /tmp/lefthook.deb \
    && dpkg -i /tmp/lefthook.deb \
    && rm /tmp/lefthook.deb

# just (so the in-container shell can re-enter the same Justfile recipes)
RUN curl -fsSL "https://github.com/casey/just/releases/download/${JUST_VERSION}/just-${JUST_VERSION}-x86_64-unknown-linux-musl.tar.gz" \
        | tar -xz -C /usr/local/bin just \
    && chmod +x /usr/local/bin/just

# Cache friendly: /workspace is the bind-mount target.
WORKDIR /workspace

# A non-root dev user mirroring the host UID/GID is wired up from compose.yml
# so files written from the container land owned by the host user.
ARG USERNAME=dev
ARG USER_UID=1000
ARG USER_GID=1000
RUN if getent passwd ${USER_UID} >/dev/null; then \
        userdel -r "$(getent passwd ${USER_UID} | cut -d: -f1)" 2>/dev/null || true; \
    fi \
    && if getent group ${USER_GID} >/dev/null; then \
        groupdel "$(getent group ${USER_GID} | cut -d: -f1)" 2>/dev/null || true; \
    fi \
    && groupadd --gid ${USER_GID} ${USERNAME} \
    && useradd --uid ${USER_UID} --gid ${USER_GID} --shell /bin/bash --create-home ${USERNAME} \
    && echo "${USERNAME} ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/${USERNAME}

USER ${USERNAME}

# Default: run an interactive bash; compose.yml overrides for one-shot recipes.
CMD ["bash"]
