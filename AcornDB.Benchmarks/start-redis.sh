#!/bin/bash

# Start Redis for AcornDB Benchmarks
# This script starts a Redis container for running cache comparison benchmarks

set -e

CONTAINER_NAME="redis-acorndb-benchmark"
REDIS_PORT=6379

echo "🌰 AcornDB Redis Benchmark Setup"
echo "================================="
echo ""

# Check if Docker is installed
if ! command -v docker &> /dev/null; then
    echo "❌ Error: Docker is not installed"
    echo "   Please install Docker: https://docs.docker.com/get-docker/"
    exit 1
fi

# Check if container already exists
if docker ps -a --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
    echo "📦 Container '${CONTAINER_NAME}' already exists"

    # Check if it's running
    if docker ps --format '{{.Names}}' | grep -q "^${CONTAINER_NAME}$"; then
        echo "✅ Redis is already running on port ${REDIS_PORT}"
    else
        echo "🔄 Starting existing container..."
        docker start ${CONTAINER_NAME}
        echo "✅ Redis started on port ${REDIS_PORT}"
    fi
else
    echo "🚀 Starting new Redis container..."
    docker run -d \
        --name ${CONTAINER_NAME} \
        -p ${REDIS_PORT}:6379 \
        redis:7-alpine

    echo "✅ Redis started on port ${REDIS_PORT}"
fi

# Wait for Redis to be ready
echo ""
echo "⏳ Waiting for Redis to be ready..."
sleep 2

# Test connection
if docker exec ${CONTAINER_NAME} redis-cli ping | grep -q "PONG"; then
    echo "✅ Redis is ready and responding to PING"
else
    echo "❌ Redis is not responding. Check logs:"
    echo "   docker logs ${CONTAINER_NAME}"
    exit 1
fi

echo ""
echo "🎉 Redis is ready for benchmarks!"
echo ""
echo "📊 Run benchmarks with:"
echo "   cd AcornDB.Benchmarks"
echo "   dotnet run redis"
echo ""
echo "🛑 Stop Redis with:"
echo "   docker stop ${CONTAINER_NAME}"
echo ""
echo "🗑️  Remove container with:"
echo "   docker rm ${CONTAINER_NAME}"
echo ""
