# linerule-cs dev container.
#
# Tooling versions are driven by `mise.toml` at the repo root — the host
# installs from the same manifest, so container and host run identical
# binaries. After `docker compose build`, every Justfile recipe can be
# run via `docker compose run --rm dev <…>` without touching the host
# toolchain.
#
# Cross-platform build coverage:
#   - Linerule.Core / Platform / Input / *.Tests target net10.0 — built
#     and tested fully here.
#   - Linerule.Platform.Windows / Cli / App target net10.0-windows —
#     reference assemblies are platform-neutral so `dotnet build` is
#     expected to succeed, but the produced binary only runs on Windows.
#     AOT publish for win-x64 is unsupported from Linux ("Cross-OS native
#     compilation is not supported" — Microsoft.NETCore.Native.Publish.targets);
#     the CI windows-latest runner is the only path to a runnable .exe.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dev

ENV DEBIAN_FRONTEND=noninteractive \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_GENERATE_ASPNET_CERTIFICATE=0 \
    DOTNET_ENVIRONMENT=Development \
    INSIDE_CONTAINER=1

# System deps: ca-certificates / curl / git / unzip / sudo are baseline
# utilities; clang + zlib1g-dev are NativeAOT prerequisites (ILCompiler
# shells out to clang for linking). Node and npm intentionally not
# installed — bun (via mise) replaces them for commitlint.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates curl git unzip sudo \
        clang zlib1g-dev \
    && rm -rf /var/lib/apt/lists/*

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

# Install mise (latest stable). The repo's mise.toml drives tool versions;
# pre-installing at image-build time bakes the binaries into a layer so
# `docker compose run --rm dev <…>` doesn't pay an install cost per run.
RUN curl -fsSL https://mise.run | sh

ENV PATH="/home/${USERNAME}/.local/bin:/home/${USERNAME}/.local/share/mise/shims:${PATH}" \
    MISE_TRUSTED_CONFIG_PATHS=/workspace

# Snapshot of mise.toml for build-time install. The runtime bind mount
# (compose.yml: `.:/workspace`) shadows this with the live host manifest,
# so an edit on the host is visible inside the container immediately —
# but already-installed tools persist in the image layer below.
COPY --chown=${USER_UID}:${USER_GID} mise.toml /workspace/mise.toml

# Install every tool in mise.toml EXCEPT dotnet — the base image already
# ships /usr/share/dotnet/dotnet and adding a second SDK via mise would
# double the image size. `mise install <list>` skips dotnet, leaving the
# base image's binary as the one mise's shim layer falls through to.
RUN mise install actionlint lefthook typos bun just

# Default: interactive bash; compose.yml overrides for one-shot recipes.
CMD ["bash"]
