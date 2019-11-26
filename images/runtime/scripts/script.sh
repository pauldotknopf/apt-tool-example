#!/usr/bin/env bash

# Create the user.
echo -en "root\nroot" | passwd root
adduser --disabled-password --gecos "" demo
echo -en "demo\ndemo" | passwd demo