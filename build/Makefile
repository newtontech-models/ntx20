PROJECT_GIT_HASH := $(shell git rev-parse --short HEAD)

COMPANY_NAME := nanotrix
PROJECT_NAME := ntx20

export PROJECT_VERSION := $(shell git rev-parse --abbrev-ref HEAD)-$(PROJECT_GIT_HASH)


all: apps.ntx20.cloud/install/$(PROJECT_NAME).tgz status

push: all
	mc cp apps.ntx20.cloud/install/$(PROJECT_NAME).tgz heinrich/apps.ntx20.cloud/install/$(PROJECT_NAME).tgz

clean:
	rm -r install

apps.ntx20.cloud/install/$(PROJECT_NAME).tgz: apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz
	cp apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz apps.ntx20.cloud/install/$(PROJECT_NAME).tgz

apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz: install/$(PROJECT_NAME)-$(PROJECT_VERSION)
	cd install/$(PROJECT_NAME)-$(PROJECT_VERSION) && tar -zcvf ../../apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz .

install/$(PROJECT_NAME)-$(PROJECT_VERSION):
	cd ../$(PROJECT_NAME) && dotnet publish -c Release -o ../build/install/$(PROJECT_NAME)-$(PROJECT_VERSION) -f net9.0
	
status:
	git status -s