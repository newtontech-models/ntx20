# ntx20

## Installation

### Prerequisities
 1. Get [ASP.NET Runtime 9.0.x](https://dotnet.microsoft.com/download/dotnet/9.0)
 2. On Windows install [VS redistributables](https://aka.ms/vs/16/release/vc_redist.x64.exe)

### Setup and update
 2. Download [windows](./scripts/ntx20-get.bat) or [linux](./scripts/ntx20-get) ``ntx20-get`` script to any read/write directory with enough free space
 4. Add the directory to the PATH 
 5. Run ``ntx20-get`` to get the latest version

### Recommended
 1. Windows [CoreUtils](http://gnuwin32.sourceforge.net/packages/coreutils.htm) and [Make](http://gnuwin32.sourceforge.net/packages/make.htm)
 2. Linux Make `sudo apt-get install build-essential curl`

## Examples

* Run transcription with us openlex model:  `ntx20 run atran-us-openlex@https://usr:psw@yourcluster.com -i file.mp3 -w text:pnc -f`
* Get help: `ntx20 run atran-us-openlex@https://usr:psw@yourcluster.com -h`
* Set env variable cluster1=https://usr:psw@yourcluster.com for storing connection string and then call: `ntx20 run atran-us-openlex@cluster1`
* Environment for ntx20 process can be set in .env file in the root folder of ntx20 as key=value per line.

