# Node 22 (the repo's engines floor) with pnpm via corepack and `just`, the entry point for every
# build/test/format flow (`just check` is the definition of done).
FROM mcr.microsoft.com/devcontainers/javascript-node:22

# just isn't in the base image's apt archive, so it comes from the upstream installer (pinned).
RUN curl --proto '=https' --tlsv1.2 -sSf https://just.systems/install.sh \
    | bash -s -- --tag 1.57.0 --to /usr/local/bin \
    && just --version

RUN corepack enable
