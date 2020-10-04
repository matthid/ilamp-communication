FROM ubuntu:20.04

RUN apt-get update && \
    apt-get install -y \
        gnupg2 ca-certificates software-properties-common apt-transport-https wget \
        bluez bluetooth usbutils screen sudo libasound2 git && \
    apt-key adv --keyserver packages.microsoft.com --recv-keys EB3E94ADBE1229CF && \
    apt-key adv --keyserver packages.microsoft.com --recv-keys 52E16F86FEE04B979B07E28DB02C46DF417A0893 && \
    sh -c 'echo "deb [arch=amd64] https://packages.microsoft.com/repos/microsoft-ubuntu-bionic-prod bionic main" > /etc/apt/sources.list.d/dotnetdev.list' && \
    apt-get update && \
    apt-get install dotnet-sdk-2.1 -y && \
    wget -q https://packages.microsoft.com/keys/microsoft.asc -O- | apt-key add - && \
    add-apt-repository "deb [arch=amd64] https://packages.microsoft.com/repos/vscode stable main" && \
    apt-get install code -y
    
# Replace 1000 with your user / group id
RUN export uid=1000 gid=1000 && \
    mkdir -p /home/developer && \
    echo "developer:x:${uid}:${gid}:Developer,,,:/home/developer:/bin/bash" >> /etc/passwd && \
    echo "developer:x:${uid}:" >> /etc/group && \
    echo "developer ALL=(ALL) NOPASSWD: ALL" > /etc/sudoers.d/developer && \
    chmod 0440 /etc/sudoers.d/developer && \
    chown ${uid}:${gid} -R /home/developer
    
USER developer
ENV HOME /home/developer
CMD /bin/bash
