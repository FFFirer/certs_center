#! /bin/bash

podman build --network=host -f Dockerfile -t certs_server:latest .
podman build --network=host -f Docketfile.cli -t certs_cli:latest .