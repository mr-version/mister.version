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

### Architecture Gap Identified
**Conventional Commits:** The TypeScript `calculate` action analyzes conventional commits for semantic versioning, but the C# core library does NOT. This creates inconsistency:
- ✅ GitHub Actions users get conventional commits support
- ❌ CLI users don't
- ❌ MSBuild users don't
- ❌ Direct library consumers don't

---

## Expansion Opportunities

### Priority 1: Conventional Commits in Core Library 🚀
**Impact:** Very High | **Effort:** Medium | **Status:** ✅ Completed (except TypeScript deprecation)

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
- [ ] Deprecate duplicate logic in TypeScript `calculate` action
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
- [ ] Create new GitHub Action: `mr-version/changelog`
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

### Priority 4: Version Policies (Lock-Step & Grouped) 📋
**Impact:** Medium | **Effort:** Medium-High | **Status:** Not Started

#### Problem
In some monorepos, related projects should share versions or all projects should version together. Currently only independent versioning is supported.

#### Solution
Add version policy engine supporting lock-step, grouped, and independent strategies.

#### Implementation Tasks
- [ ] Create `VersionPolicy` enum
- [ ] Create `VersionGroup` model
- [ ] Create `IVersionPolicyEngine` interface
- [ ] Implement version policy logic
  - [ ] Lock-step: All projects share one version
  - [ ] Grouped: Related projects share versions
  - [ ] Independent: Current behavior (default)
- [ ] Update `VersionCalculator` to respect policies
- [ ] Add configuration support
- [ ] Update CLI to show policy information
- [ ] Update report generation to group by policy
- [ ] Add validation for policy conflicts
- [ ] Write tests
- [ ] Update documentation

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

### Priority 5: CalVer Support 📅
**Impact:** Low-Medium | **Effort:** Medium | **Status:** Not Started

#### Problem
Some organizations prefer calendar-based versioning over semantic versioning.

#### Solution
Add CalVer as an alternative versioning scheme with configurable formats.

#### Implementation Tasks
- [ ] Create `VersionScheme` enum (SemVer, CalVer)
- [ ] Create `CalVerConfig` model
- [ ] Create `ICalVerCalculator` interface
- [ ] Implement CalVer calculation logic
  - [ ] YYYY.MM.PATCH format
  - [ ] YY.0M.PATCH format
  - [ ] YYYY.WW.PATCH format (week-based)
  - [ ] Custom formats
- [ ] Update `VersionCalculator` to support both schemes
- [ ] Add configuration options
- [ ] Update parsing and formatting
- [ ] Update CLI output
- [ ] Write tests
- [ ] Update documentation

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

### Priority 6: Enhanced Validation & Constraints ✅
**Impact:** Low-Medium | **Effort:** Low | **Status:** Not Started

#### Problem
No enforcement of version policies or constraints to prevent mistakes.

#### Solution
Add validation engine for version constraints and rules.

#### Implementation Tasks
- [ ] Create `VersionConstraints` model
- [ ] Create `IVersionValidator` interface
- [ ] Implement validation logic
  - [ ] Minimum version enforcement
  - [ ] Version range restrictions
  - [ ] Dependency compatibility checks
  - [ ] Major version approval requirements
- [ ] Add configuration options
- [ ] Add validation to build process
- [ ] Add validation to CLI
- [ ] Add validation to GitHub Actions
- [ ] Write tests
- [ ] Update documentation

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

### Phase 3: Advanced Features (2 weeks)
**Goal:** Add version policies and alternative schemes

14. Implement version policy engine
15. Add lock-step and grouped versioning
16. Implement CalVer support
17. Add validation and constraints
18. Testing and documentation

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

## Notes

Last Updated: 2025-11-20
Status: Phase 1 & 2 Completed, Phase 3 Planned
Next Review: When starting Phase 3 (Version Policies and CalVer)
