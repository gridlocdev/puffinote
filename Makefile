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
		| awk 'BEGIN {FS = ":.*?## "} {printf "  \033[36m%-24s\033[0m %s\n", $$1, $$2}'
