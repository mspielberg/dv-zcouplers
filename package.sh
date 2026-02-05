#!/usr/bin/env bash

# Parse arguments
NO_ARCHIVE=false
OUTPUT_DIRECTORY="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

while [[ $# -gt 0 ]]; do
    case $1 in
        --no-archive)
            NO_ARCHIVE=true
            shift
            ;;
        --output-directory)
            OUTPUT_DIRECTORY="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1"
            echo "Usage: $0 [--no-archive] [--output-directory <path>]"
            exit 1
            ;;
    esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

DIST_DIR="$OUTPUT_DIRECTORY/dist"
mkdir -p "$DIST_DIR"

# Read mod info using jq or python
if command -v jq &> /dev/null; then
    MOD_ID=$(jq -r '.Id' resources/info.json)
    MOD_VERSION=$(jq -r '.Version' resources/info.json)
elif command -v python3 &> /dev/null; then
    MOD_ID=$(python3 -c "import json; print(json.load(open('resources/info.json'))['Id'])")
    MOD_VERSION=$(python3 -c "import json; print(json.load(open('resources/info.json'))['Version'])")
else
    echo "Error: Neither jq nor python3 found. Please install one of them."
    exit 1
fi

if [ "$NO_ARCHIVE" = true ]; then
    ZIP_WORK_DIR="$OUTPUT_DIRECTORY"
else
    ZIP_WORK_DIR="$DIST_DIR/tmp"
fi
ZIP_OUT_DIR="$ZIP_WORK_DIR/$MOD_ID"

# Clean previous staging (so stale or empty folders don't linger)
if [ -d "$ZIP_OUT_DIR" ]; then
    rm -rf "$ZIP_OUT_DIR"
fi
mkdir -p "$ZIP_OUT_DIR"

# Copy flat files
cp -f resources/info.json LICENSE "$ZIP_OUT_DIR/"

# Copy ONLY the contents of build (so build/ itself is not a top-level folder in the package)
if [ -d "build" ]; then
    # Copy all contents of build directory to ZIP_OUT_DIR
    cp -rf build/* "$ZIP_OUT_DIR/"
    
    # Validate Assets copied (helpful diagnostic if something goes wrong)
    if [ ! -d "$ZIP_OUT_DIR/Assets" ]; then
        echo "Warning: Assets folder missing from packaged output." >&2
    fi
else
    echo "Warning: build directory not found; skipping." >&2
fi

if [ "$NO_ARCHIVE" = false ]; then
    FILE_NAME="$DIST_DIR/${MOD_ID}_${MOD_VERSION}.zip"
    if [ -f "$FILE_NAME" ]; then
        rm -f "$FILE_NAME"
    fi
    
    # Use zip command to create archive
    if command -v zip &> /dev/null; then
        pushd "$ZIP_OUT_DIR" > /dev/null
        zip -r "$FILE_NAME" ./*
        if [ $? -ne 0 ]; then
            popd > /dev/null
            echo "Error: zip command failed" >&2
            exit 1
        fi
        popd > /dev/null
        echo "Created archive: $FILE_NAME"
    else
        echo "Error: zip command not found. Please install zip." >&2
        exit 1
    fi
else
    echo "Staged (no archive): $ZIP_OUT_DIR"
fi
