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
    INSIDE_CONTAINER=1 \
    EnableWindowsTargeting=true

# Node major is the current active LTS line — `@commitlint/cli` v21 (used by
# the commit-msg hook via `npx --no -- commitlint`) requires Node >= 20.
# Debian 12's apt ships nodejs 18, so install from NodeSource instead. The
# major track gets minor/patch updates automatically via apt; the major
# itself moves once per ~2 years and is bumped here by hand when the LTS
# line rolls over (Node 22 took over active LTS in Oct 2024).
ARG NODE_MAJOR=22

# Each host-side tool (typos / actionlint / lefthook / just) resolves its
# tag at build time by following the `releases/latest` 302 redirect on
# GitHub — no API token, no rate-limit pressure, no manual ARG bumps. The
# trade-off is build-time non-determinism (today's rebuild and tomorrow's
# may produce different versions), accepted in exchange for staying
# current without a bot-driven config flywheel. The host's mise / asdf
# can drift behind this container's bins; that's fine because Justfile
# routes every recipe through `docker compose run --rm dev`.

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        git \
        unzip \
        sudo \
        gnupg \
        clang \
        zlib1g-dev \
    && mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://deb.nodesource.com/gpgkey/nodesource-repo.gpg.key \
        | gpg --dearmor -o /etc/apt/keyrings/nodesource.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/nodesource.gpg] https://deb.nodesource.com/node_${NODE_MAJOR}.x nodistro main" \
        > /etc/apt/sources.list.d/nodesource.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

# typos — tag like `v1.46.2`; asset name embeds the same `v`-prefixed version.
RUN TYPOS_VERSION="$(curl -fsSL -o /dev/null -w '%{url_effective}' \
        'https://github.com/crate-ci/typos/releases/latest' | sed 's|.*/v||')" \
    && curl -fsSL "https://github.com/crate-ci/typos/releases/download/v${TYPOS_VERSION}/typos-v${TYPOS_VERSION}-x86_64-unknown-linux-musl.tar.gz" \
        | tar -xz -C /usr/local/bin ./typos \
    && chmod +x /usr/local/bin/typos

# actionlint — tag like `v1.7.12`; asset filename uses the bare version.
RUN ACTIONLINT_VERSION="$(curl -fsSL -o /dev/null -w '%{url_effective}' \
        'https://github.com/rhysd/actionlint/releases/latest' | sed 's|.*/v||')" \
    && curl -fsSL "https://github.com/rhysd/actionlint/releases/download/v${ACTIONLINT_VERSION}/actionlint_${ACTIONLINT_VERSION}_linux_amd64.tar.gz" \
        | tar -xz -C /usr/local/bin actionlint \
    && chmod +x /usr/local/bin/actionlint

# lefthook (v2 ships .deb/.apk/.rpm only — pick .deb on the Debian-based SDK image).
RUN LEFTHOOK_VERSION="$(curl -fsSL -o /dev/null -w '%{url_effective}' \
        'https://github.com/evilmartians/lefthook/releases/latest' | sed 's|.*/v||')" \
    && curl -fsSL "https://github.com/evilmartians/lefthook/releases/download/v${LEFTHOOK_VERSION}/lefthook_${LEFTHOOK_VERSION}_amd64.deb" -o /tmp/lefthook.deb \
    && dpkg -i /tmp/lefthook.deb \
    && rm /tmp/lefthook.deb

# just — tag is the bare version (no `v` prefix on releases since 1.x).
RUN JUST_VERSION="$(curl -fsSL -o /dev/null -w '%{url_effective}' \
        'https://github.com/casey/just/releases/latest' | sed 's|.*/||')" \
    && curl -fsSL "https://github.com/casey/just/releases/download/${JUST_VERSION}/just-${JUST_VERSION}-x86_64-unknown-linux-musl.tar.gz" \
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
