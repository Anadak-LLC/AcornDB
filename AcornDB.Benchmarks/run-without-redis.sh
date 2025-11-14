#!/bin/bash
echo "🌰 Running AcornDB Benchmarks (Redis tests will be skipped)"
echo "==========================================================="
echo ""
echo "Note: Redis benchmarks will be automatically skipped if Redis is not available."
echo "      AcornDB benchmarks will run normally."
echo ""
dotnet run -c Release redis
