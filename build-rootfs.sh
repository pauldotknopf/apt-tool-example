#!/usr/bin/env bash

bash -c "cd ./images/runtime && apt-tool install && apt-tool generate-rootfs --overwrite --run-stage2"
bash -c "cd ./images/boot && apt-tool install && apt-tool generate-rootfs --overwrite --run-stage2"
