# .NET 10 SDK — matches global.json (10.0.300, rollForward latestFeature).
# The :10.0 tag tracks the latest 10.0.x SDK, satisfying the pin.
FROM mcr.microsoft.com/dotnet/sdk:10.0

# Native libraries Cellestra's headless UI tests need: they draw with REAL Skia by default
# (UseHeadlessDrawing=false + .UseSkia(), see features/EditSpreadsheets/Cellestra.UiTests/TestAppBuilder.cs),
# so SkiaSharp/Avalonia must resolve fontconfig, freetype and a few X11 stubs. (Typage's and Puffinote's
# UI tests use Avalonia's null drawing unconditionally and need none of this.)
# `just` drives the repo's justfile — the documented entry point for every build/test/format flow
# (`just check` is the definition of done). It isn't in this base image's apt archive, so it comes
# from the upstream installer below instead.
# git/sudo/curl round out the dev experience. openssh-client provides
# ssh-keygen, which git invokes to make SSH-signed commits via the forwarded
# agent (gpg.format=ssh); without it `git commit -S` fails on ssh-keygen.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libfreetype6 \
        libice6 \
        libsm6 \
        libx11-6 \
        fonts-liberation \
        git \
        curl \
        sudo \
        openssh-client \
    && rm -rf /var/lib/apt/lists/*

# just, from the upstream installer (pinned; --to a system path so every user gets it).
RUN curl --proto '=https' --tlsv1.2 -sSf https://just.systems/install.sh \
    | bash -s -- --tag 1.57.0 --to /usr/local/bin \
    && just --version

# Non-root user to match the devcontainer convention. The :10.0 base image is
# Ubuntu-based and already ships an `ubuntu` user/group at UID/GID 1000, so we
# rename whatever occupies 1000 to `vscode` instead of creating it fresh (a bare
# groupadd/useradd fails with "GID '1000' already exists").
ARG USERNAME=vscode
ARG USER_UID=1000
ARG USER_GID=$USER_UID
RUN if getent group $USER_GID >/dev/null; then \
        groupmod --new-name $USERNAME "$(getent group $USER_GID | cut -d: -f1)"; \
    else \
        groupadd --gid $USER_GID $USERNAME; \
    fi \
    && if getent passwd $USER_UID >/dev/null; then \
        usermod --login $USERNAME --gid $USER_GID --home /home/$USERNAME --move-home \
            "$(getent passwd $USER_UID | cut -d: -f1)"; \
    else \
        useradd --uid $USER_UID --gid $USER_GID -m -s /bin/bash $USERNAME; \
    fi \
    && echo "$USERNAME ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/$USERNAME \
    && chmod 0440 /etc/sudoers.d/$USERNAME

USER $USERNAME

# Opt out of the .NET first-run telemetry/banner noise inside the container.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
