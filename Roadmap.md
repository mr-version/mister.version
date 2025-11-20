# Mister.Version - Expansion Roadmap

## Overview
This document outlines potential expansion opportunities for Mister.Version based on analysis of the current codebase and ecosystem.

## Current State Summary

### Existing Capabilities
- ✅ Change-based semantic versioning
- ✅ Dependency-aware version calculation
- ✅ MSBuild integration via NuGet package
- ✅ CLI tool for reporting and analysis
- ✅ Multiple report formats (Text, JSON, CSV, Mermaid, DOT, ASCII)
- ✅ Branch-specific versioning (main, dev, release, feature)
- ✅ GitHub Actions ecosystem (setup, calculate, tag, report, release)
- ✅ Comprehensive testing infrastructure
- ✅ Performance caching for large monorepos

### GitHub Actions Status
**Implementation Status:** 5 of 6 production actions are fully implemented as separate repositories:
- ✅ **setup** - Install Mister.Version CLI (TypeScript action)
- ✅ **calculate** - Calculate versions for projects (TypeScript action)
- ✅ **report** - Generate version reports (TypeScript action)
- ✅ **tag** - Create git tags (TypeScript action)
- ✅ **release** - Complete release workflow (Composite action)
- ⏳ **changelog** - Placeholder repository (TypeScript wrapper needed)

---

## Expansion Opportunities

### Priority 1: Conventional Commits in Core Library 🚀
**Impact:** Very High | **Effort:** Medium | **Status:** ✅ Completed

#### Problem
Currently the C# core always bumps patch version when changes are detected. The TypeScript GitHub Action has separate logic to analyze conventional commits, creating duplication and inconsistency.

#### Solution
Add commit analysis to the core library so all consumers benefit.

#### Implementation Tasks
- [x] Create `ICommitAnalyzer` interface in Core library
- [x] Implement `ConventionalCommitAnalyzer` class
  - [x] Parse commit messages for conventional commit patterns
  - [x] Detect BREAKING CHANGE in commit body/footer
  - [x] Handle `!` suffix for breaking changes
  - [x] Support configurable commit patterns
- [x] Add `CommitClassification` model
- [x] Add `VersionBumpType` enum (Major, Minor, Patch, None)
- [x] Update `VersionCalculator` to use commit analysis
  - [x] Get commits between base tag and HEAD
  - [x] Analyze commits to determine bump type
  - [x] Apply appropriate version increment
- [x] Add configuration options to `VersionConfig.cs`
  - [x] `commitConventions.enabled`
  - [x] `commitConventions.majorPatterns`
  - [x] `commitConventions.minorPatterns`
  - [x] `commitConventions.patchPatterns`
  - [x] `commitConventions.ignorePatterns`
- [x] Update MSBuild properties for configuration
- [x] Update CLI to show semantic bump reasoning
- [x] ~~Deprecate duplicate logic in TypeScript `calculate` action~~ (N/A - no duplication exists, implemented only in C#)
- [x] Write comprehensive unit tests
- [x] Update documentation and examples

#### Configuration Example
```yaml
# mr-version.yml
commitConventions:
  enabled: true
  majorPatterns: ["BREAKING CHANGE:", "!:"]
  minorPatterns: ["feat:", "feature:"]
  patchPatterns: ["fix:", "bugfix:", "chore:", "docs:"]
  ignorePatterns: ["chore(deps):", "style:"]
```

#### Benefits
- Consistent behavior across all tools (MSBuild, CLI, GitHub Actions)
- Industry standard practice (Angular, Node.js ecosystem)
- Better semantic versioning that matches actual code changes
- Removes code duplication between C# and TypeScript
- Foundation for changelog generation

---

### Priority 2: Changelog Generation 📝
**Impact:** High | **Effort:** Medium | **Status:** ✅ Completed (except GitHub Action)

#### Problem
The `report` action generates version reports but NOT changelogs. There's no automatic release notes generation from commit history.

#### Solution
Add changelog generation capabilities to core library and expose via CLI, MSBuild, and GitHub Actions.

#### Implementation Tasks
- [x] Create `IChangelogGenerator` interface
- [x] Implement `ChangelogGenerator` class
  - [x] Group commits by type (Breaking Changes, Features, Fixes, etc.)
  - [x] Support multiple output formats (Markdown, Plain Text, JSON)
  - [x] Link to GitHub issues/PRs when detected
  - [x] Support custom grouping and filtering
- [x] Add changelog models
  - [x] `ChangelogEntry`
  - [x] `ChangelogSection`
  - [x] `ChangelogConfig`
- [x] Integrate with `VersionCalculator`
- [x] Add CLI command: `mr-version changelog`
  - [x] Options for version range
  - [x] Options for output format
  - [x] Options for file output
- [ ] Create new GitHub Action: `mr-version/changelog` (placeholder repo exists, needs TypeScript implementation)
- [x] Add MSBuild property: `GenerateChangelog=true`
- [x] Add configuration options
- [x] Write tests
- [x] Update documentation

#### Configuration Example
```yaml
# mr-version.yml
changelog:
  enabled: true
  outputFormat: markdown
  outputPath: CHANGELOG.md
  groupBy: type
  includeCommitLinks: true
  sections:
    - name: "Breaking Changes"
      patterns: ["BREAKING CHANGE:", "!:"]
      emoji: "💥"
    - name: "Features"
      patterns: ["feat:", "feature:"]
      emoji: "🚀"
    - name: "Bug Fixes"
      patterns: ["fix:", "bugfix:"]
      emoji: "🐛"
```

#### Output Example
```markdown
## v2.1.0 (2025-11-20)

### 💥 Breaking Changes
- BREAKING CHANGE: Remove deprecated API (#126)

### 🚀 Features
- feat: Add CalVer support (#123)
- feat(core): Implement version policies (#125)

### 🐛 Bug Fixes
- fix: Resolve NuGet dependency version issue (#124)
```

#### Benefits
- Automatic release notes generation
- Better communication of changes
- Integration with GitHub Releases
- Saves developer time
- Builds on conventional commits

---

### Priority 3: File Pattern-Based Change Detection 🔍
**Impact:** Medium-High | **Effort:** Low-Medium | **Status:** ✅ Completed

#### Problem
Currently any file change triggers a version bump. Documentation-only changes or test-only changes shouldn't necessarily trigger new releases.

#### Solution
Add file pattern configuration to control which changes are significant and what bump level they require.

#### Implementation Tasks
- [x] Create `ChangeDetectionConfig` model
- [x] Add pattern matching service
  - [x] Support glob patterns
  - [x] Support include/exclude logic
  - [x] Support significance rules
- [x] Update `GitService.ProjectHasChangedSinceTag`
  - [x] Filter changes by ignore patterns
  - [x] Classify changes by significance rules
  - [x] Return change classification info
- [x] Add configuration options
- [x] Add `sourceOnlyMode` option
- [x] Update `VersionCalculator` to use classifications
- [x] Combine with conventional commits analysis
- [x] Write tests
- [x] Update documentation

#### Configuration Example
```yaml
# mr-version.yml
changeDetection:
  # Don't bump version for these changes
  ignorePatterns:
    - "**/*.md"
    - "**/*.txt"
    - "**/docs/**"
    - "**/.editorconfig"
    - "**/.gitignore"

  # Require specific bump levels for these files
  significanceRules:
    major:
      - "**/PublicApi/**"
      - "**/Contracts/**"
      - "**/Interfaces/**"
    minor:
      - "**/Features/**"
    patch:
      - "**/Internal/**"

  # Only version when source code changes (ignore test/doc changes)
  sourceOnlyMode: false
```

#### Benefits
- Reduced noise in versioning
- Documentation PRs don't trigger releases
- Public API changes can force major versions (safety)
- More control over versioning behavior
- Better alignment with semantic versioning principles

---

### Priority 4: Git Integration Enhancements 🔧
**Impact:** Medium | **Effort:** Medium | **Status:** ✅ Completed

#### Problem
Limited support for advanced Git repository scenarios like shallow clones, custom tag naming conventions, submodule tracking, and branch-based metadata.

#### Solution
Add comprehensive Git integration features to handle various repository scenarios and CI/CD requirements.

#### Implementation Tasks
- [x] Create `GitIntegrationConfig` model
- [x] Implement shallow clone detection and support
  - [x] Detect shallow clones via `Repository.Info.IsShallow`
  - [x] Add fallback versioning for limited history
  - [x] Log warnings when history is unavailable
- [x] Implement custom tag pattern support
  - [x] Support placeholders: `{name}`, `{prefix}`, `{version}`
  - [x] Per-project tag patterns
  - [x] Wildcard project matching
- [x] Implement tag ancestry validation
  - [x] Filter unreachable tags from other branches
  - [x] Use LibGit2Sharp commit reachability
  - [x] Auto-disable for shallow clones
- [x] Implement submodule change detection
  - [x] Monitor `.gitmodules` file changes
  - [x] Detect gitlink (submodule pointer) updates
  - [x] Trigger version bumps on submodule changes
- [x] Implement branch metadata in versions
  - [x] Add sanitized branch name to build metadata
  - [x] Only for non-stable branches
  - [x] Configurable via `IncludeBranchInMetadata`
- [x] Add MSBuild properties (9 properties)
- [x] Integrate with VersionCalculator
- [x] Write unit tests
- [x] Update documentation with examples

#### Configuration Example
```xml
<PropertyGroup>
  <!-- Shallow clone support -->
  <MonoRepoShallowCloneSupport>true</MonoRepoShallowCloneSupport>
  <MonoRepoShallowCloneFallbackVersion>1.0.0</MonoRepoShallowCloneFallbackVersion>

  <!-- Custom tag patterns -->
  <MonoRepoCustomTagPatterns>MyLib={name}_{prefix}{version};MyApp={name}/{prefix}{version}</MonoRepoCustomTagPatterns>

  <!-- Submodule detection -->
  <MonoRepoSubmoduleSupport>true</MonoRepoSubmoduleSupport>

  <!-- Branch metadata -->
  <MonoRepoIncludeBranchInMetadata>true</MonoRepoIncludeBranchInMetadata>

  <!-- Tag ancestry validation -->
  <MonoRepoValidateTagAncestry>true</MonoRepoValidateTagAncestry>
</PropertyGroup>
```

#### Benefits
- CI/CD pipelines with shallow clones work seamlessly
- Flexible tag naming conventions for different projects
- Automatic version bumps when submodules update
- Branch names in version metadata for traceability
- Prevents using versions from unreachable tags
- Better support for complex Git workflows

---

### Priority 5: Additional Directory Monitoring 📁
**Impact:** Medium | **Effort:** Low-Medium | **Status:** ✅ Completed

#### Problem
Projects in monorepos often depend on shared code directories that exist outside the project folder. Currently, only changes within the project directory and its dependencies trigger version bumps. Shared libraries and common utilities aren't monitored.

#### Solution
Add configuration to monitor additional directories beyond the project directory, with support for both MSBuild properties and YAML configuration.

#### Implementation Tasks
- [x] Add `AdditionalMonitorPaths` to `ChangeDetectionConfig`
- [x] Add `AdditionalMonitorPaths` to `VersionOptions`
- [x] Add `AdditionalMonitorPaths` to `ProjectVersionConfig` (YAML)
- [x] Update `GitService.ClassifyProjectChanges` to include additional directories
- [x] Add `MonoRepoAdditionalMonitorPaths` MSBuild property
- [x] Wire through `MonoRepoVersionTask` and `VersioningService`
- [x] Update both targets files to pass property
- [x] Implement path merging (YAML + MSBuild)
- [x] Support absolute and relative paths
- [x] Respect file pattern rules (major/minor/patch/ignore)
- [x] Write comprehensive unit tests
- [x] Update documentation with examples

#### Configuration Example
```xml
<PropertyGroup>
  <!-- Monitor additional directories -->
  <MonoRepoAdditionalMonitorPaths>../shared/common;../libs/core;/absolute/path/to/utils</MonoRepoAdditionalMonitorPaths>

  <!-- File patterns apply to additional paths -->
  <MonoRepoIgnoreFilePatterns>**/*.md;**/docs/**</MonoRepoIgnoreFilePatterns>
  <MonoRepoMajorFilePatterns>**/PublicApi/**</MonoRepoMajorFilePatterns>
</PropertyGroup>
```

```yaml
# mr-version.yml
projects:
  MyApi:
    additionalMonitorPaths:
      - "../shared/common"
      - "../libs/authentication"

  MyWeb:
    additionalMonitorPaths:
      - "../shared/frontend"
      - "../libs/ui-components"
```

#### Benefits
- Monitor shared libraries and common utilities
- Changes in shared code trigger appropriate version bumps
- Support for both MSBuild and YAML configuration
- Per-project customization via YAML
- Respect file pattern rules for smart versioning
- Essential for monorepos with shared code

---

### Priority 6: Version Policies (Lock-Step & Grouped) 📋
**Impact:** Medium | **Effort:** Medium-High | **Status:** ✅ Completed

#### Problem
In some monorepos, related projects should share versions or all projects should version together. Currently only independent versioning is supported.

#### Solution
Add version policy engine supporting lock-step, grouped, and independent strategies.

#### Implementation Tasks
- [x] Create `VersionPolicy` enum
- [x] Create `VersionGroup` model
- [x] Create `IVersionPolicyEngine` interface
- [x] Implement version policy logic
  - [x] Lock-step: All projects share one version
  - [x] Grouped: Related projects share versions
  - [x] Independent: Current behavior (default)
- [x] Update `VersionCalculator` to respect policies
- [x] Add configuration support
- [x] Update CLI to show policy information
- [x] Update report generation to group by policy
- [x] Add validation for policy conflicts
- [x] Write tests
- [x] Update documentation

#### Configuration Example
```yaml
# mr-version.yml
versionPolicy: grouped  # independent, lockstep, grouped

versionGroups:
  # Core libraries share version
  core-libraries:
    projects: [Mister.Version.Core, Mister.Version]
    strategy: lockstep

  # Tools version independently
  tools:
    projects: [Mister.Version.CLI]
    strategy: independent
```

#### Scenarios
- **Lock-step:** Major release bumps all projects to 2.0.0
- **Grouped:** Breaking change in Core bumps both Core and MSBuild task
- **Independent:** CLI can be at 3.5.0 while Core is at 2.1.0

#### Benefits
- Coordinated releases for related projects
- Flexibility for different versioning strategies
- Better support for complex monorepos
- Clear communication about project relationships

---

### Priority 7: CalVer Support 📅
**Impact:** Low-Medium | **Effort:** Medium | **Status:** ✅ Completed

#### Problem
Some organizations prefer calendar-based versioning over semantic versioning.

#### Solution
Add CalVer as an alternative versioning scheme with configurable formats.

#### Implementation Tasks
- [x] Create `VersionScheme` enum (SemVer, CalVer)
- [x] Create `CalVerConfig` model
- [x] Create `ICalVerCalculator` interface
- [x] Implement CalVer calculation logic
  - [x] YYYY.MM.PATCH format
  - [x] YY.0M.PATCH format
  - [x] YYYY.WW.PATCH format (week-based)
  - [x] YYYY.0M.PATCH format
- [x] Update `VersionCalculator` to support both schemes
- [x] Add configuration options to VersionConfig and VersionOptions
- [x] Add MSBuild properties (MonoRepoVersionScheme, MonoRepoCalVerFormat, etc.)
- [x] Integrate with VersioningService and MonoRepoVersionTask
- [x] Implement CalVerCalculator with date-based version generation
- [x] Update CLI output for CalVer display (added scheme info to detailed and JSON output)
- [x] Write comprehensive unit tests (CalVerCalculatorTests.cs and CalVerIntegrationTests.cs)
- [x] Update documentation and examples (comprehensive CalVer section in README.md)

#### Configuration Example
```yaml
# mr-version.yml
versionScheme: calver  # or semver (default)
calver:
  format: "YYYY.MM.PATCH"  # Ubuntu style
  # Alternative formats:
  # - "YY.0M.PATCH"
  # - "YYYY.WW.PATCH" (week-based)
  startDate: "2025-01-01"
  resetPatchMonthly: true
```

#### Examples
- `2025.11.0` (November 2025, first release)
- `2025.11.5` (November 2025, sixth release)
- `25.11.0` (short year format)

#### Benefits
- Alternative for organizations with release cadences
- Common in operating systems (Ubuntu) and other projects
- Clear time-based versioning
- Flexibility in versioning approaches

---

### Priority 8: Enhanced Validation & Constraints ✅
**Impact:** Low-Medium | **Effort:** Low | **Status:** ✅ Completed

#### Problem
No enforcement of version policies or constraints to prevent mistakes.

#### Solution
Add validation engine for version constraints and rules.

#### Implementation Tasks
- [x] Create `VersionConstraints` model
- [x] Create `IVersionValidator` interface
- [x] Implement validation logic
  - [x] Minimum version enforcement
  - [x] Version range restrictions
  - [x] Dependency compatibility checks
  - [x] Major version approval requirements
  - [x] Blocked versions list
  - [x] Monotonic increase requirement
  - [x] Custom validation rules (pattern, range)
- [x] Add configuration options
- [x] Add validation to build process (VersionCalculator)
- [x] Add validation to CLI
- [x] Add MSBuild properties for all constraints
- [ ] Add validation to GitHub Actions
- [x] Write comprehensive unit tests (50+ test cases)
- [x] Update documentation

#### Configuration Example
```yaml
# mr-version.yml
constraints:
  # Never go below this version
  minimumVersion: "2.0.0"

  # Stay in version range
  allowedRange: "3.x.x"

  # Validate dependency compatibility
  validateDependencyVersions: true

  # Prevent accidental major bumps without approval
  requireMajorApproval: true
```

#### Benefits
- Safety rails for versioning
- Prevent mistakes
- Enforce organizational policies
- Better governance

---

## Implementation Roadmap

### Phase 1: Semantic Versioning Foundation (2-3 weeks) ✅
**Goal:** Add conventional commits support to core library

1. ✅ Implement `ICommitAnalyzer` and `ConventionalCommitAnalyzer`
2. ✅ Update `VersionCalculator` to use commit analysis
3. ✅ Add configuration for commit patterns
4. ✅ Update CLI to show semantic bump reasoning
5. ⏳ Deprecate duplicate logic in TypeScript actions
6. ✅ Comprehensive testing
7. ✅ Documentation updates

**Deliverables:**
- ✅ Consistent conventional commit support across all tools
- ✅ Foundation for changelog generation
- ✅ Better semantic versioning

### Phase 2: Release Automation (2 weeks) ✅
**Goal:** Add changelog generation and advanced change detection

8. ✅ Implement `IChangelogGenerator`
9. ✅ Add markdown/JSON/text format support
10. ✅ Add file pattern-based change detection
11. ✅ New CLI command: `mr-version changelog`
12. ⏳ New GitHub Action: `mr-version/changelog`
13. ✅ Testing and documentation

**Deliverables:**
- ✅ Automatic changelog generation
- ✅ More granular change detection
- ✅ Complete release workflow

### Phase 2.5: Git Integration Enhancements (1-2 weeks) ✅
**Goal:** Add advanced Git repository support

14. ✅ Implement shallow clone detection and fallback versioning
15. ✅ Add custom tag pattern support with placeholders
16. ✅ Add tag ancestry validation
17. ✅ Add submodule change detection
18. ✅ Add branch metadata in version build metadata
19. ✅ MSBuild properties and documentation

**Deliverables:**
- ✅ CI/CD pipeline compatibility (shallow clones)
- ✅ Flexible tag naming conventions
- ✅ Submodule tracking support
- ✅ Better Git workflow support

### Phase 3: Advanced Features (2 weeks)
**Goal:** Add version policies and alternative schemes

20. Implement version policy engine
21. Add lock-step and grouped versioning
22. Implement CalVer support
23. Add validation and constraints
24. Testing and documentation

**Deliverables:**
- Coordinated versioning strategies
- Alternative versioning schemes
- Safety validations

---

## Dependencies

### Phase Dependencies
- Phase 2 depends on Phase 1 (changelog generation needs commit analysis)
- Phase 3 is independent but benefits from Phase 1 & 2

### External Dependencies
- LibGit2Sharp (already used)
- YAML configuration (already used)
- MSBuild SDK (already used)

---

## Testing Strategy

### Unit Tests
- Commit message parsing
- Version calculation with different bump types
- Changelog generation
- File pattern matching
- Version policy enforcement
- CalVer calculation

### Integration Tests
- End-to-end versioning scenarios
- Multi-project monorepo scenarios
- Different branch types
- Configuration precedence

### GitHub Actions Tests
- Existing test infrastructure
- Add tests for new features
- Ensure backward compatibility

---

## Documentation Updates Needed

### README.md
- Update feature list
- Add conventional commits section
- Add changelog section
- Add version policies section
- Add CalVer section

### Configuration Documentation
- New YAML options
- New MSBuild properties
- Examples for each feature

### GitHub Actions Documentation
- Update action READMEs
- Add new changelog action docs
- Update workflow examples

### CLI Documentation
- New commands
- New options
- Updated examples

---

## Backward Compatibility

### Breaking Changes to Avoid
- Maintain existing default behavior (patch bumps)
- Make all new features opt-in via configuration
- Keep existing MSBuild properties working
- Keep existing CLI commands working

### Migration Path
- Conventional commits: Opt-in via `commitConventions.enabled: true`
- File patterns: Opt-in via `changeDetection` config
- Version policies: Default to `independent` (current behavior)
- CalVer: Opt-in via `versionScheme: calver`

---

## Success Metrics

### Adoption
- Number of projects using conventional commits
- Number of projects using changelog generation
- GitHub Actions usage statistics

### Quality
- Test coverage (maintain >80%)
- Performance (no regression in large monorepos)
- User feedback and issues

### Impact
- Reduced manual versioning effort
- Better semantic version accuracy
- Improved release communication

---

## Open Questions

1. Should conventional commits be enabled by default in a future major version?
2. What commit patterns should be default? (Angular, Conventional Commits spec, custom?)
3. Should changelog generation be integrated into the `report` action or separate?
4. What level of validation should be default vs opt-in?
5. Should CalVer support include automatic date detection vs manual configuration?

---

## Resources

### References
- [Conventional Commits Specification](https://www.conventionalcommits.org/)
- [Semantic Versioning](https://semver.org/)
- [Calendar Versioning](https://calver.org/)
- [Angular Commit Convention](https://github.com/angular/angular/blob/main/CONTRIBUTING.md#commit)

### Related Projects
- conventional-changelog
- semantic-release
- commitizen
- standard-version

---

## Next Steps

### Outstanding Tasks

#### 1. Implement Changelog GitHub Action (Priority 2)
**Status:** Placeholder repository exists
**Effort:** 2-3 days
**Description:**
- Create TypeScript wrapper around `mr-version changelog` CLI command
- Implement action.yml with inputs for output format, file path, etc.
- Build dist/ bundle for GitHub Actions execution
- Add support for posting changelog as PR comment
- Test with example workflows

**Repository:** https://github.com/mr-version/changelog

#### 2. Add Validation to GitHub Actions (Priority 8)
**Status:** Not started
**Effort:** 1-2 days
**Description:**
- Integrate validation errors/warnings into action outputs
- Add validation configuration inputs to calculate/release actions
- Display validation errors as GitHub Action annotations
- Add `validation-mode` input (strict/warn/off)
- Update action documentation with validation examples

**Affects:** calculate, release actions

#### 3. Update Documentation
**Status:** Not started
**Effort:** 2-3 hours
**Description:**
- Update `.github/actions/README.md` with accurate status
- Add workflow examples for changelog action once implemented
- Document validation options in action READMEs
- Update main README.md to reference all 6 GitHub Actions

### Future Enhancements

- Publish actions to GitHub Actions Marketplace
- Add integration tests for GitHub Actions workflows
- Implement action versioning strategy (tags/releases)
- Add telemetry/analytics for action usage
- Create action badges and branding

---

## Notes

Last Updated: 2025-11-20
Status: Priorities 1-8 Completed (C# Core 100%, GitHub Actions 83%)
Next Review: After changelog action implementation
Current Version: 3.0.0

### GitHub Actions Implementation
GitHub Actions ecosystem implemented as separate repositories (submodules):
- **setup** (https://github.com/mr-version/setup) - TypeScript action to install Mister.Version CLI
- **calculate** (https://github.com/mr-version/calculate) - TypeScript action for version calculation with 13 input options
- **report** (https://github.com/mr-version/report) - TypeScript action for generating version reports in multiple formats
- **tag** (https://github.com/mr-version/tag) - TypeScript action for creating git tags with GPG signing support
- **release** (https://github.com/mr-version/release) - Composite action orchestrating setup → calculate → tag → report
- **changelog** (https://github.com/mr-version/changelog) - Placeholder repository, needs TypeScript wrapper implementation

All actions are referenced as git submodules in `.github/actions/` using HTTPS URLs for broad accessibility.

### Recent Completion: Priority 8 - Enhanced Validation & Constraints
Added version validation engine with comprehensive constraint support:
- Created `VersionConstraints` model with support for multiple constraint types
- Implemented `IVersionValidator` interface and `VersionValidator` service
- Added validation for minimum/maximum versions, allowed ranges, blocked versions
- Implemented monotonic increase requirement and major version approval workflow
- Added custom validation rules (pattern-based and range-based)
- Integrated validation into VersionCalculator with automatic validation
- Added MSBuild properties (9 new properties: MonoRepoValidationEnabled, MonoRepoMinimumVersion, etc.)
- Enhanced CLI output to display validation errors and warnings
- Comprehensive unit tests (50+ test cases in VersionValidatorTests.cs)
- Foundation for preventing version mistakes and enforcing organizational policies

### Previous Completion: Priority 7 - CalVer Support
Added Calendar Versioning (CalVer) as an alternative to SemVer:
- Created `VersionScheme` enum and `CalVerConfig` model
- Implemented `CalVerCalculator` with four format options (YYYY.MM.PATCH, YY.0M.PATCH, YYYY.WW.PATCH, YYYY.0M.PATCH)
- Integrated CalVer into VersionCalculator and VersionResult
- Added MSBuild properties (MonoRepoVersionScheme, MonoRepoCalVerFormat, etc.)
- Enhanced CLI output to display CalVer information (scheme, format, settings)
- Comprehensive unit tests (50+ test cases in CalVerCalculatorTests.cs and CalVerIntegrationTests.cs)
- Complete documentation in README.md with examples and use cases
- Foundation for date-based versioning in monorepos

### Previous Completion: Priority 6 - Version Policies (Lock-Step & Grouped)
Added version policy engine for coordinating versions across projects:
- Created `VersionPolicy` enum (Independent, LockStep, Grouped)
- Implemented `VersionPolicyEngine` with pattern matching and validation
- Added `VersionGroup` model for grouped versioning
- YAML configuration support via `versionPolicy` section
- Automatic validation of policy configurations
- Wildcard pattern support for project matching
- Comprehensive unit tests (50+ test cases)
- Foundation for coordinated versioning in monorepos

### Previous Completion: Priority 5 - Additional Directory Monitoring
Added ability to monitor additional directories outside project folders:
- Monitor shared libraries and common utilities for changes
- Support both MSBuild properties and YAML configuration
- Respect file pattern rules (major/minor/patch/ignore)
- Support absolute and relative paths
- Per-project customization via YAML
- Automatic path merging and deduplication
- Comprehensive unit tests and documentation

### Previous Completions:
- **Priority 4**: Git Integration Enhancements (shallow clones, custom tags, submodules, branch metadata)
- **Priority 3**: File Pattern-Based Change Detection (smart versioning, ignore patterns)
- **Priority 2**: Changelog Generation (markdown, text, JSON formats)
- **Priority 1**: Conventional Commits Support (semantic version bump detection)
