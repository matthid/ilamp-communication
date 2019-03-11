#!/bin/bash

# from http://fabiorehm.com/blog/2014/09/11/running-gui-apps-with-docker/
docker run -ti --net=host --privileged --rm -e DISPLAY=$DISPLAY -v /var/run/dbus:/var/run/dbus -v ~/projects:/home/developer/projects -v /tmp/.X11-unix:/tmp/.X11-unix my-ilamp-development
