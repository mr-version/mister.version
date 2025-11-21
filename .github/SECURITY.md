# Security Policy

## Supported Versions

We actively support the following versions of Mister.Version with security updates:

| Version | Supported          | .NET Versions      |
| ------- | ------------------ | ------------------ |
| 1.x.x   | :white_check_mark: | 8.0, 9.0, 10.0    |
| < 1.0   | :x:                | -                  |

## Reporting a Vulnerability

We take the security of Mister.Version seriously. If you discover a security vulnerability, please follow these steps:

### 1. **Do Not** Open a Public Issue

Please **do not** report security vulnerabilities through public GitHub issues, discussions, or pull requests.

### 2. Report Privately

Instead, please report security vulnerabilities by:

- Using GitHub's Security Advisory feature: [Report a vulnerability](https://github.com/mister-version/mister.version/security/advisories/new)
- Or emailing us directly at: security@mister-version.dev

### 3. Include Details

Please include as much of the following information as possible:

- Type of vulnerability
- Full paths of source file(s) related to the vulnerability
- Location of the affected source code (tag/branch/commit or direct URL)
- Step-by-step instructions to reproduce the issue
- Proof-of-concept or exploit code (if possible)
- Impact of the vulnerability, including how an attacker might exploit it

### 4. What to Expect

After you submit a report, you can expect:

- **Acknowledgment**: We will acknowledge receipt of your report within 48 hours
- **Investigation**: We will investigate and validate the vulnerability
- **Updates**: We will keep you informed about our progress
- **Fix Timeline**: We aim to release a fix within 90 days for confirmed vulnerabilities
- **Credit**: We will credit you in the security advisory (unless you prefer to remain anonymous)

## Security Best Practices

When using Mister.Version in your projects:

1. **Keep Updated**: Always use the latest stable version
2. **Review Permissions**: Ensure your CI/CD pipelines have minimal necessary permissions
3. **Validate Input**: When using configuration files, validate their sources
4. **Audit Dependencies**: Regularly review and update dependencies
5. **Monitor Advisories**: Watch this repository for security advisories

## Security Features

Mister.Version includes the following security considerations:

- Read-only Git repository access
- No external network calls except for Git operations
- Configuration validation
- Safe file system operations

## Disclosure Policy

When we receive a security vulnerability report:

1. We will confirm the vulnerability and determine its severity
2. We will develop a fix and prepare a release
3. We will coordinate disclosure with the reporter
4. We will publish a security advisory
5. We will credit the reporter (if they wish)

## Comments on This Policy

If you have suggestions on how this process could be improved, please submit a pull request or open an issue.

---

Thank you for helping keep Mister.Version and its users safe!
