FROM ubuntu
# docker run --rm -ti -v E:/Projects/ilamp-communication:/ilamp ubuntu

WORKDIR /proj

RUN apt-get update && apt-get install -y curl zip unzip openjdk-8-jdk
RUN curl -s "https://get.sdkman.io" | bash
# RUN source ~/.bash_profile
RUN bash -c "source '/root/.sdkman/bin/sdkman-init.sh' && sdk install kotlin && sdk install kscript"
