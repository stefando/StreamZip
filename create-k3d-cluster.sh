#!/bin/bash
set -e

# Define variables
CLUSTER_NAME="streamzip-cluster"
REGISTRY_NAME="streamzip-registry"
REGISTRY_PORT="5001"
IMAGE_NAME="streamzip"
IMAGE_TAG=$(date +%Y%m%d-%H%M%S)

# Check if k3d is installed
if ! command -v k3d &> /dev/null; then
    echo "k3d is not installed. Please install it first:"
    echo "brew install k3d"
    exit 1
fi

# Clean up any previous deployments
echo "Cleaning up previous deployments..."
k3d registry delete $REGISTRY_NAME 2>/dev/null || true
k3d cluster delete $CLUSTER_NAME 2>/dev/null || true

# Create registry
echo "Creating k3d registry: $REGISTRY_NAME"
k3d registry create $REGISTRY_NAME --port $REGISTRY_PORT
sleep 3  # Give registry time to start

# Check registry status
echo "Verifying registry is running..."
docker ps | grep k3d-$REGISTRY_NAME || { echo "Registry failed to start"; exit 1; }

# Create data directory if it doesn't exist
DATA_DIR="/Users/stefan.dobrovolny/data"
if [ ! -d "$DATA_DIR" ]; then
    echo "Creating data directory at $DATA_DIR"
    mkdir -p "$DATA_DIR"
    chmod 755 "$DATA_DIR"
fi

# Create cluster with registry
echo "Creating k3d cluster: $CLUSTER_NAME"
k3d cluster create $CLUSTER_NAME \
    --agents 1 \
    --registry-use k3d-$REGISTRY_NAME:$REGISTRY_PORT \
    --port "8080:80@loadbalancer" \
    --volume "$DATA_DIR:$DATA_DIR"

echo "Cluster created successfully!"

# Set kubectl context to the new cluster
kubectl config use-context k3d-$CLUSTER_NAME

# Build and push Docker image with timestamp tag for versioning
echo "Building Docker image with tag: $IMAGE_TAG"
docker build -t k3d-$REGISTRY_NAME:$REGISTRY_PORT/$IMAGE_NAME:$IMAGE_TAG .
docker tag k3d-$REGISTRY_NAME:$REGISTRY_PORT/$IMAGE_NAME:$IMAGE_TAG k3d-$REGISTRY_NAME:$REGISTRY_PORT/$IMAGE_NAME:latest

echo "Pushing Docker image to registry..."
docker push k3d-$REGISTRY_NAME:$REGISTRY_PORT/$IMAGE_NAME:$IMAGE_TAG
docker push k3d-$REGISTRY_NAME:$REGISTRY_PORT/$IMAGE_NAME:latest

# Create a temp deployment file with the correct registry URL
echo "Creating deployment with registry URL..."
sed "s|\${REGISTRY_URL}|k3d-$REGISTRY_NAME:$REGISTRY_PORT|g" k8s/deployment.yaml > k8s/deployment-with-registry.yaml

# Apply Kubernetes manifests
echo "Deploying application to Kubernetes..."
cp k8s/deployment-with-registry.yaml k8s/deployment.yaml.temp
mv k8s/deployment.yaml k8s/deployment.yaml.bak
mv k8s/deployment-with-registry.yaml k8s/deployment.yaml
kubectl apply -k k8s/
mv k8s/deployment.yaml.bak k8s/deployment.yaml

echo "Waiting for deployment to be ready..."
kubectl wait --for=condition=available --timeout=120s deployment/streamzip

echo "======================================================"
echo "StreamZip application deployed successfully!"
echo "Image tag: $IMAGE_TAG"
echo "Access the application at: http://localhost:8080/streamzip/download/{folderName}"
echo ""
echo "Mounted directory: /Users/stefan.dobrovolny/data"
echo "Place files in this directory to access them through the application"
echo "======================================================"

# Clean up temporary files
rm -f k8s/deployment.yaml.temp