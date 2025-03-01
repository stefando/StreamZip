# StreamZip

A .NET web application designed to efficiently stream folders as ZIP archives in Kubernetes environments.

## Requirements

- [k3d](https://k3d.io/) - Lightweight Kubernetes for development
- [Docker](https://www.docker.com/) - For building and running containers
- [kubectl](https://kubernetes.io/docs/tasks/tools/) - For managing Kubernetes resources
- [Task](https://taskfile.dev/) - Task runner for development automation
- [.NET 9](https://dotnet.microsoft.com/download) - For local development

## Getting Started

This project uses [Task](https://taskfile.dev/) to automate common development operations.

### Installation

1. Install Task from https://taskfile.dev/#/installation

### Available Tasks

List all available tasks:
```
task
```

Common tasks:
- `task create-cluster` - Create k3d cluster with registry and deploy application
- `task clean-resources` - Remove k3d resources (registry and cluster)
- `task test-download` - Test the download endpoint
- `task test-download-limited` - Test download with rate limiting
- `task build-image` - Build Docker image locally

## Data Directory

The application is configured to serve files from `/Users/stefan.dobrovolny/data` which is mounted 
into the container as `/app/Folders`. Place files in this directory to access them through the application.

## Accessing the Application

Once deployed, access the application at:
```
http://localhost:8080/streamzip/download/{folderName}
```

Where `{folderName}` is a folder inside your data directory.

## Testing with cURL

Test a normal download:
```
curl -v "http://localhost:8080/streamzip/download/{folderName}" -o downloaded.zip
```

Test with rate limiting:
```
curl -v "http://localhost:8080/streamzip/download/{folderName}" --limit-rate 100k -o downloaded.zip
```

## Note

Content-length calculation times out after 20 seconds (returns 413 error).