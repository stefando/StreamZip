# StreamZip Project Context

## Project Overview
StreamZip is a .NET web application designed to efficiently stream folders as ZIP archives in Kubernetes environments.

## Commands
- Deploy to k3d cluster: `./create-k3d-cluster.sh`
- Test download with curl: `curl -v "http://localhost:8080/streamzip/download/{folderName}" -o downloaded.zip`
- Test with rate limiting: `curl -v "http://localhost:8080/streamzip/download/{folderName}" --limit-rate 100k -o downloaded.zip`

## Key Information
- Uses .NET 9
- K8s deployment configured with Traefik ingress
- Volume mount: `/Users/stefan.dobrovolny/data` mapped to `/app/Folders`
- Content-length calculation times out after 20 seconds (returns 413 error)
- Security context runs container as non-root with read-only volume

## Previous Session Notes
- Optimized for streaming large files from Kubernetes pods
- Modified error handling to return 413 for too-large folders rather than falling back to chunked encoding
- Repository created at: https://github.com/stefando/StreamZip