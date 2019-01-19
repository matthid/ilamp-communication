#!/bin/bash

dotnet publish --self-contained --runtime ubuntu.18.04-x64

ssh matthid@192.168.178.25 'rm -rf ~/ilamp_test'

scp -r src/ilamp-communication/bin/Debug/netcoreapp2.1/ubuntu.18.04-x64/publish matthid@192.168.178.25:~/ilamp_test

echo "Copying successfull! Starting application"

ssh matthid@192.168.178.25 'chmod +x ~/ilamp_test/ilamp-communication && ~/ilamp_test/ilamp-communication'
