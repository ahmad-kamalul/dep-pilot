# Third-party notices and source references

Dep Pilot is an independent public reference implementation. Third-party components are not relicensed by this repository, and no affiliation with their upstream projects is implied. Refer to the repository's license notice for rights granted over original application code.

Dependencies retain their respective licenses. Review the resolved package license metadata and transitive dependency licenses before redistribution.

| Component | Upstream / license reference |
|---|---|
| .NET / ASP.NET Core / EF Core | https://github.com/dotnet — primarily MIT; individual packages apply |
| SQLite | https://www.sqlite.org/copyright.html — public domain |
| xUnit | https://github.com/xunit/xunit — Apache-2.0 |
| Ansible Core | https://github.com/ansible/ansible — GPL-3.0-or-later |
| community.docker collection | https://github.com/ansible-collections/community.docker — collection file licenses apply |
| Requests | https://github.com/psf/requests — Apache-2.0 |
| Docker official images | https://github.com/docker-library/docker — image components have separate licenses |
| MinIO | https://github.com/minio/minio/tree/9e49d5e7a648f00e26f2246f4dc28e6b07f8c84a — AGPL-3.0 |

The MinIO image is built from that exact unmodified official source commit. Its upstream license is copied to `/licenses/MinIO-AGPL-3.0.txt`. The source URL and commit are included as image labels. This reference uses MinIO as an independently running service; review applicable obligations before distributing images or operating a modified version.

Technical references:

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Hosted services and background tasks](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services)
- [ASP.NET Core antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery)
- [Ansible local execution](https://docs.ansible.com/projects/ansible/latest/playbook_guide/playbooks_delegation.html)
- [Ansible Docker container module](https://docs.ansible.com/projects/ansible/latest/collections/community/docker/docker_container_module.html)
- [Docker Engine security](https://docs.docker.com/engine/security/)
