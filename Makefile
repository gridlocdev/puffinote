# Puffinote monorepo Makefile
#
# There is no root solution. Each area builds, tests, and ships on its own:
#   app/                        Puffinote — composite shell (Desktop + CLI)
#   features/EditMarkdown/      Typage    — Markdown editor
#   features/EditSpreadsheets/  Cellestra — spreadsheet editor
#
# Common flows:
#   make build            build all three areas
#   make test             test all three areas
#   make format           auto-format all three areas
#   make check            format-verify + build + test all areas (CI gate / definition of done)
#   make typage-test      one area's suite (also: puffinote-*, cellestra-*)
#   make test-filter FILTER=SomeTest AREA=typage   run a subset while iterating
#   make publish          self-contained Release builds of all three apps into artifacts/
#   make install          put the three CLIs (puffinote, typage, cellestra) on PATH
#
# Warnings are errors here (TreatWarningsAsErrors=true), so "build" means "no warnings".

# ---- Solutions ----------------------------------------------------------------
PUFFINOTE_SLN := app/Puffinote.slnx
TYPAGE_SLN    := features/EditMarkdown/Typage.slnx
CELLESTRA_SLN := features/EditSpreadsheets/Cellestra.slnx

# ---- CLI entry projects (run headless in the Linux devcontainer) --------------
PUFFINOTE_CLI := app/Puffinote.Cli
TYPAGE_CLI    := features/EditMarkdown/Typage.Cli
CELLESTRA_CLI := features/EditSpreadsheets/Cellestra.Cli

# ---- Desktop (GUI) entry projects (run on a native host, not the container) ---
PUFFINOTE_DESKTOP := app/Puffinote.Desktop
TYPAGE_DESKTOP    := features/EditMarkdown/Typage.Desktop
CELLESTRA_DESKTOP := features/EditSpreadsheets/Cellestra.Desktop

DOTNET := dotnet

# ---- Packaging: where publish writes, and where install puts things -----------
# Override any of these on the command line, e.g.
#   make cellestra-publish RID=osx-x64 CONFIG=Debug
#   make install PREFIX=/usr/local
CONFIG    ?= Release
ARTIFACTS ?= artifacts
PREFIX    ?= $(HOME)/.local
BINDIR    ?= $(PREFIX)/bin
APPS_DIR  ?= /Applications

# Runtime identifier for the host, unless one is passed in. ($(OS) reports "Unix" for both
# macOS and Linux, so ask uname — same reasoning as BuildOSToken in Directory.Build.props.)
RID_OS   := $(if $(filter Darwin,$(shell uname -s)),osx,linux)
RID_ARCH := $(if $(filter arm64 aarch64,$(shell uname -m)),arm64,x64)
RID      ?= $(RID_OS)-$(RID_ARCH)

PUFFINOTE_OUT := $(ARTIFACTS)/puffinote/$(RID)
TYPAGE_OUT    := $(ARTIFACTS)/typage/$(RID)
CELLESTRA_OUT := $(ARTIFACTS)/cellestra/$(RID)

# Both surfaces publish self-contained, so an artifact runs on a machine with no .NET installed.
# The CLI additionally publishes as a single self-extracting file (native libraries included) so
# installing it is one copy onto PATH; the desktop apps keep their Avalonia/Skia natives beside
# the executable, which is what a macOS .app bundle expects (see build/package-mac.sh).
PUBLISH_CLI_FLAGS     := -c $(CONFIG) -r $(RID) --self-contained true \
                         -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
PUBLISH_DESKTOP_FLAGS := -c $(CONFIG) -r $(RID) --self-contained true

.DEFAULT_GOAL := help

# ==============================================================================
# Aggregate targets — act on all three areas
# ==============================================================================

.PHONY: all
all: build test ## Build and test every area

.PHONY: restore
restore: puffinote-restore typage-restore cellestra-restore ## Restore NuGet packages for every area

.PHONY: build
build: puffinote-build typage-build cellestra-build ## Build every area (warnings are errors)

.PHONY: test
test: puffinote-test typage-test cellestra-test ## Test every area

.PHONY: format
format: puffinote-format typage-format cellestra-format ## Auto-format every area

.PHONY: format-check
format-check: puffinote-format-check typage-format-check cellestra-format-check ## Verify formatting for every area (no changes)

.PHONY: clean
clean: puffinote-clean typage-clean cellestra-clean ## Remove build artifacts for every area

.PHONY: check
check: format-check build test ## Definition of done: formatting verified, all areas build clean and green

.PHONY: publish
publish: puffinote-publish typage-publish cellestra-publish ## Publish every app (CLI + desktop, self-contained) to artifacts/

.PHONY: install
install: puffinote-install typage-install cellestra-install ## Install every CLI into PREFIX/bin (default ~/.local/bin)

.PHONY: uninstall
uninstall: puffinote-uninstall typage-uninstall cellestra-uninstall ## Remove every installed CLI from PREFIX/bin

# ==============================================================================
# Puffinote — the composite shell (app/)
# ==============================================================================

.PHONY: puffinote-restore
puffinote-restore: ## Restore Puffinote packages
	$(DOTNET) restore $(PUFFINOTE_SLN)

.PHONY: puffinote-build
puffinote-build: ## Build the Puffinote shell
	$(DOTNET) build $(PUFFINOTE_SLN)

.PHONY: puffinote-test
puffinote-test: ## Test the Puffinote shell
	$(DOTNET) test $(PUFFINOTE_SLN)

.PHONY: puffinote-format
puffinote-format: ## Auto-format the Puffinote shell
	$(DOTNET) format $(PUFFINOTE_SLN)

.PHONY: puffinote-format-check
puffinote-format-check: ## Verify Puffinote formatting (fails if unformatted)
	$(DOTNET) format $(PUFFINOTE_SLN) --verify-no-changes

.PHONY: puffinote-clean
puffinote-clean: ## Clean Puffinote build artifacts
	$(DOTNET) clean $(PUFFINOTE_SLN)

.PHONY: puffinote-run
puffinote-run: ## Run the Puffinote desktop shell (native host — GUI won't launch in the container)
	$(DOTNET) run --project $(PUFFINOTE_DESKTOP)

.PHONY: puffinote-run-cli
puffinote-run-cli: ## Run the Puffinote CLI (pass ARGS="info", ARGS='search "todo"', etc.)
	$(DOTNET) run --project $(PUFFINOTE_CLI) -- $(ARGS)

.PHONY: puffinote-publish
puffinote-publish: puffinote-publish-cli puffinote-publish-desktop ## Publish the Puffinote CLI + shell to artifacts/puffinote/<rid>/

.PHONY: puffinote-publish-cli
puffinote-publish-cli: ## Publish the `puffinote` CLI (single self-contained file)
	$(DOTNET) publish $(PUFFINOTE_CLI) $(PUBLISH_CLI_FLAGS) -o $(PUFFINOTE_OUT)/cli

.PHONY: puffinote-publish-desktop
puffinote-publish-desktop: ## Publish the Puffinote desktop shell (self-contained)
	$(DOTNET) publish $(PUFFINOTE_DESKTOP) $(PUBLISH_DESKTOP_FLAGS) -o $(PUFFINOTE_OUT)/desktop

.PHONY: puffinote-install
puffinote-install: puffinote-publish-cli ## Install the `puffinote` CLI into PREFIX/bin (default ~/.local/bin)
	@mkdir -p $(BINDIR)
	install -m 0755 $(PUFFINOTE_OUT)/cli/puffinote $(BINDIR)/puffinote
	@echo "installed $(BINDIR)/puffinote — make sure $(BINDIR) is on your PATH"

.PHONY: puffinote-uninstall
puffinote-uninstall: ## Remove the installed `puffinote` CLI from PREFIX/bin
	rm -f $(BINDIR)/puffinote

# ==============================================================================
# Typage — the Markdown editor (features/EditMarkdown/)
# ==============================================================================

.PHONY: typage-restore
typage-restore: ## Restore Typage packages
	$(DOTNET) restore $(TYPAGE_SLN)

.PHONY: typage-build
typage-build: ## Build the Typage editor
	$(DOTNET) build $(TYPAGE_SLN)

.PHONY: typage-test
typage-test: ## Test the Typage editor
	$(DOTNET) test $(TYPAGE_SLN)

.PHONY: typage-format
typage-format: ## Auto-format the Typage editor
	$(DOTNET) format $(TYPAGE_SLN)

.PHONY: typage-format-check
typage-format-check: ## Verify Typage formatting (fails if unformatted)
	$(DOTNET) format $(TYPAGE_SLN) --verify-no-changes

.PHONY: typage-clean
typage-clean: ## Clean Typage build artifacts
	$(DOTNET) clean $(TYPAGE_SLN)

.PHONY: typage-run
typage-run: ## Run the Typage desktop editor (native host — GUI won't launch in the container)
	$(DOTNET) run --project $(TYPAGE_DESKTOP)

.PHONY: typage-run-cli
typage-run-cli: ## Run the Typage CLI (pass ARGS="--help", etc.)
	$(DOTNET) run --project $(TYPAGE_CLI) -- $(ARGS)

.PHONY: typage-publish
typage-publish: typage-publish-cli typage-publish-desktop ## Publish the Typage CLI + editor to artifacts/typage/<rid>/

.PHONY: typage-publish-cli
typage-publish-cli: ## Publish the `typage` CLI (single self-contained file)
	$(DOTNET) publish $(TYPAGE_CLI) $(PUBLISH_CLI_FLAGS) -o $(TYPAGE_OUT)/cli

.PHONY: typage-publish-desktop
typage-publish-desktop: ## Publish the Typage desktop editor (self-contained)
	$(DOTNET) publish $(TYPAGE_DESKTOP) $(PUBLISH_DESKTOP_FLAGS) -o $(TYPAGE_OUT)/desktop

.PHONY: typage-install
typage-install: typage-publish-cli ## Install the `typage` CLI into PREFIX/bin (default ~/.local/bin)
	@mkdir -p $(BINDIR)
	install -m 0755 $(TYPAGE_OUT)/cli/typage $(BINDIR)/typage
	@echo "installed $(BINDIR)/typage — make sure $(BINDIR) is on your PATH"

.PHONY: typage-uninstall
typage-uninstall: ## Remove the installed `typage` CLI from PREFIX/bin
	rm -f $(BINDIR)/typage

# ==============================================================================
# Cellestra — the spreadsheet editor (features/EditSpreadsheets/)
# ==============================================================================

.PHONY: cellestra-restore
cellestra-restore: ## Restore Cellestra packages
	$(DOTNET) restore $(CELLESTRA_SLN)

.PHONY: cellestra-build
cellestra-build: ## Build the Cellestra editor
	$(DOTNET) build $(CELLESTRA_SLN)

.PHONY: cellestra-test
cellestra-test: ## Test the Cellestra editor
	$(DOTNET) test $(CELLESTRA_SLN)

.PHONY: cellestra-format
cellestra-format: ## Auto-format the Cellestra editor
	$(DOTNET) format $(CELLESTRA_SLN)

.PHONY: cellestra-format-check
cellestra-format-check: ## Verify Cellestra formatting (fails if unformatted)
	$(DOTNET) format $(CELLESTRA_SLN) --verify-no-changes

.PHONY: cellestra-clean
cellestra-clean: ## Clean Cellestra build artifacts
	$(DOTNET) clean $(CELLESTRA_SLN)

.PHONY: cellestra-run
cellestra-run: ## Run the Cellestra desktop editor (native host — GUI won't launch in the container)
	$(DOTNET) run --project $(CELLESTRA_DESKTOP)

.PHONY: cellestra-run-cli
cellestra-run-cli: ## Run the Cellestra CLI (pass ARGS="--help", etc.)
	$(DOTNET) run --project $(CELLESTRA_CLI) -- $(ARGS)

.PHONY: cellestra-publish
cellestra-publish: cellestra-publish-cli cellestra-publish-desktop ## Publish the Cellestra CLI + editor to artifacts/cellestra/<rid>/

.PHONY: cellestra-publish-cli
cellestra-publish-cli: ## Publish the `cellestra` CLI (single self-contained file)
	$(DOTNET) publish $(CELLESTRA_CLI) $(PUBLISH_CLI_FLAGS) -o $(CELLESTRA_OUT)/cli

.PHONY: cellestra-publish-desktop
cellestra-publish-desktop: ## Publish the Cellestra desktop editor (self-contained)
	$(DOTNET) publish $(CELLESTRA_DESKTOP) $(PUBLISH_DESKTOP_FLAGS) -o $(CELLESTRA_OUT)/desktop

# Cellestra is the one area with macOS bundle metadata (Info.plist) and a packaging script, so it
# is the only app that can currently be installed as a .app. build/package-mac.sh owns the bundle
# layout, signing, and notarization — call it rather than reimplementing any of that here.
.PHONY: cellestra-publish-app
cellestra-publish-app: ## Build Cellestra.app via build/package-mac.sh (macOS only)
	CONFIG=$(CONFIG) RID=$(RID) features/EditSpreadsheets/build/package-mac.sh

.PHONY: cellestra-install
cellestra-install: cellestra-publish-cli ## Install the `cellestra` CLI into PREFIX/bin (default ~/.local/bin)
	@mkdir -p $(BINDIR)
	install -m 0755 $(CELLESTRA_OUT)/cli/cellestra $(BINDIR)/cellestra
	@echo "installed $(BINDIR)/cellestra — make sure $(BINDIR) is on your PATH"

.PHONY: cellestra-install-app
cellestra-install-app: cellestra-publish-app ## Install Cellestra.app into APPS_DIR (default /Applications, macOS only)
	rm -rf "$(APPS_DIR)/Cellestra.app"
	cp -R features/EditSpreadsheets/artifacts/Cellestra.app "$(APPS_DIR)/Cellestra.app"
	@echo "installed $(APPS_DIR)/Cellestra.app"

.PHONY: cellestra-uninstall
cellestra-uninstall: ## Remove the installed `cellestra` CLI from PREFIX/bin
	rm -f $(BINDIR)/cellestra

# ==============================================================================
# Iterating on a single test / subset (prefer this while developing)
#   make test-filter AREA=typage FILTER=SomeTest
# ==============================================================================

AREA   ?= puffinote
FILTER ?=
AREA_SLN = $(if $(filter typage,$(AREA)),$(TYPAGE_SLN),$(if $(filter cellestra,$(AREA)),$(CELLESTRA_SLN),$(PUFFINOTE_SLN)))

.PHONY: test-filter
test-filter: ## Run a filtered subset: make test-filter AREA=typage FILTER=SomeTest
	$(DOTNET) test $(AREA_SLN) --filter "FullyQualifiedName~$(FILTER)"

# ==============================================================================
# Help
# ==============================================================================

.PHONY: help
help: ## Show this help
	@grep -hE '^[a-zA-Z0-9_-]+:.*?## ' $(MAKEFILE_LIST) \
		| sort \
		| awk 'BEGIN {FS = ":.*?## "} {printf "  \033[36m%-26s\033[0m %s\n", $$1, $$2}'
