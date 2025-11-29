# Security Policy

## Supported Versions

We provide security updates for the latest release version.

## Reporting a Vulnerability

If you discover a security vulnerability, please **do not** open a public issue.

Instead, please email security details to: [security@yourdomain.com] or open a private security advisory on GitHub.

Include:
- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if available)

We will respond within 48 hours and work with you to address the issue.

## Security Features

- API key authentication for REST endpoints
- Session-based authentication with token expiration
- HA ID allowlist (deny-all by default)
- HTTPS support with certificate configuration
- Secure config file permissions (Administrators/SYSTEM only)
- DPAPI encryption for certificate passwords
- Rate limiting on authentication failures
- Local network only (no internet exposure by default)

## Best Practices

- Always use HTTPS in production
- Set a strong API key
- Configure HA ID allowlist (empty = deny all)
- Keep the agent updated
- Review logs regularly for suspicious activity
- Run the service with least privilege (LocalSystem is acceptable for desktop access)

