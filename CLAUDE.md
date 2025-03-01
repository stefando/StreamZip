# StreamZip Project Context

## Project Overview
StreamZip is a .NET web application designed to efficiently stream folders as ZIP archives in Kubernetes environments.

## Commands (Use Task)
- Use Task automation for all operations: `task <command>`
- List available tasks: `task`
- Create cluster & deploy: `task create-cluster`
- Clean resources: `task clean-resources`
- Test download: `task test-download`
- Test with rate limiting: `task test-download-limited`

## Key Information
- Uses .NET 8.0 (not 9.0 which causes compatibility issues)
- K8s deployment configured with Traefik ingress
- Volume mount: `/Users/stefan.dobrovolny/data` mapped to `/app/Folders`
- Content-length calculation times out after 20 seconds (returns 413 error)
- Content-length calculation is critical feature, use explicit content-length instead of chunked encoding
- Security context runs container as non-root with read-only volume
- Registry port 5001 (fixed for MacBook port conflict, default would be 5000)
- AllowSynchronousIO must be enabled due to ZipArchive requiring synchronous operations

## Environment Configuration
- Registry port 5001 must be used due to port conflicts on MacBook
- Task automation available for all development workflows
- Data directory must exist at `/Users/stefan.dobrovolny/data`
- Repository at: https://github.com/stefando/StreamZip

## Previous Session Notes
- Optimized for streaming large files from Kubernetes pods
- Modified error handling to return 413 for too-large folders rather than falling back to chunked encoding
- Added Task automation to simplify development workflow
- Fixed registry connection issues in cluster setup