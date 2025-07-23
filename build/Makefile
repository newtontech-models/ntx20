ifneq (,$(wildcard ./.env))
    include .env
    export
endif

PROJECT_GIT_HASH := $(shell git rev-parse --short HEAD)

COMPANY_NAME := nanotrix
PROJECT_NAME := ntx20

PROJECT_GIT_TAG :=$(shell git tag)
PROJECT_GIT_COMM=$(shell git rev-list $(PROJECT_GIT_TAG).. --count)
export PROJECT_VERSION ?= $(PROJECT_GIT_TAG)-c$(PROJECT_GIT_COMM)x$(PROJECT_GIT_HASH)



build: apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz apps.ntx20.cloud/install/$(PROJECT_NAME).tgz status

push: build
	mc cp apps.ntx20.cloud/install/$(PROJECT_NAME).tgz heinrich/apps.ntx20.cloud/install/$(PROJECT_NAME).tgz
	mc cp apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz heinrich/apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz

publish: nupkg/$(PROJECT_NAME).$(PROJECT_VERSION).nupkg status
	dotnet nuget push  nupkg/$(PROJECT_NAME).$(PROJECT_VERSION).nupkg --source https://api.nuget.org/v3/index.json --api-key $(NUGET_API_KEY)

clean:
	rm -r install

apps.ntx20.cloud/install/$(PROJECT_NAME).tgz: apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz
	cp apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz apps.ntx20.cloud/install/$(PROJECT_NAME).tgz

apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz: install/$(PROJECT_NAME)-$(PROJECT_VERSION)
	cd install/$(PROJECT_NAME)-$(PROJECT_VERSION) && tar -zcvf ../../apps.ntx20.cloud/install/$(PROJECT_NAME)-$(PROJECT_VERSION).tgz .

install/$(PROJECT_NAME)-$(PROJECT_VERSION):
	cd ../$(PROJECT_NAME) && dotnet publish -c Release -o ../build/install/$(PROJECT_NAME)-$(PROJECT_VERSION) -f net9.0

nupkg/$(PROJECT_NAME).$(PROJECT_VERSION).nupkg:
	cd ../$(PROJECT_NAME) && dotnet pack -c Release -o ../build/nupkg 

status:
	git status -s