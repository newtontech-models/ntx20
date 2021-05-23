PROJECT_GIT_HASH := $(shell git rev-parse --short HEAD)

COMPANY_NAME := nanotrix
PROJECT_NAME := ntx20

export PROJECT_VERSION := $(shell git rev-parse --abbrev-ref HEAD)-$(PROJECT_GIT_HASH)


all: apps.nanotrix.cloud/install/$(PROJECT_NAME).tgz status

push: all
	mc cp apps.nanotrix.cloud/install/$(PROJECT_NAME).tgz wasabi/apps.nanotrix.cloud/install/$(PROJECT_NAME).tgz

clean:
	rm -r install

apps.nanotrix.cloud/install/$(PROJECT_NAME).tgz: apps.nanotrix.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz
	cp apps.nanotrix.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz apps.nanotrix.cloud/install/$(PROJECT_NAME).tgz

apps.nanotrix.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz: install/$(PROJECT_NAME)-$(PROJECT_VERSION)
	cd install/$(PROJECT_NAME)-$(PROJECT_VERSION) && tar -zcvf ../../apps.nanotrix.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz .

install/$(PROJECT_NAME)-$(PROJECT_VERSION):
	cd ../$(PROJECT_NAME) && dotnet publish -c Release -o ../build/install/$(PROJECT_NAME)-$(PROJECT_VERSION) -f net5.0
	
status:
	git status -s