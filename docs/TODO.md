# Dep Pilot TODO

These features are planned ideas and are not yet implemented.

## High priority

- [ ] Add company login (SSO), multi-factor authentication, and user roles.
- [ ] Use HTTPS for all Master and Agent connections.
- [ ] Add secure Agent registration and credential rotation.
- [ ] Store passwords and tokens in a secret manager.
- [ ] Provide a production installation guide for real Linux hosts.
- [ ] Support safe database upgrades, backups, and recovery.
- [ ] Add monitoring and alerts for system failures.
- [ ] Set retention limits for logs and deployment history.
- [ ] Check host readiness before deployment and expire jobs that wait too long.
- [ ] Help operators check actual host state before retrying interrupted deployments.

## Next improvements

- [ ] Organize hosts into environments, such as staging and production.
- [ ] Require approval before production deployments.
- [ ] Review and approve playbook versions before use.
- [ ] Deploy to a small group first, then continue if health checks pass.
- [ ] Monitor application health after deployment.
- [ ] Add recovery or rollback steps that protect application data.
- [ ] Add PostgreSQL and multiple Masters when scale or availability requires them.
- [ ] Improve search, filtering, and pagination for larger host fleets.
- [ ] Export audit records to a protected external system.
- [ ] Add a secure release and Agent update process.
- [ ] Review the storage software's maintenance, support, and licensing before production use.

## Optional improvements

- [ ] Integrate deployments with CI/CD pipelines.
- [ ] Add reusable deployment templates.
- [ ] Detect unexpected changes on target hosts.
- [ ] Support Windows hosts if required.
- [ ] Add deployment schedules and reports.
