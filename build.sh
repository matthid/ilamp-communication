#!/bin/bash
source /usr/bin/mono-snapshot mono
xbuild /p:Configuration=Debug src/dbus-sharp.csproj
