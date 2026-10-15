#!/bin/bash

# Convert the comma-separated list into an array
IFS=',' read -r -a array <<< "$ADMINS"

# Iterate over the array
for item in "${array[@]}"; do
  echo "Item: $item"
done