# ntx20

## Installation

### Prerequisities
 1. Get [ASP.NET Runtime 9.0.x](https://dotnet.microsoft.com/download/dotnet/9.0)
 
 ### Install
 1. `dotnet tool search --prerelease ntx20`
 2. `dotnet tool install --prerelease ntx20 --tool-path $path`
 2. or `dotnet tool install --prerelease ntx20 -g`
  
### Update
1. `dotnet tool update --prerelease --tool-path $path ntx20`
2. or  `dotnet tool update --prerelease ntx20 -g`

### ntx20 app extension:
 #### Windows
- [CoreUtils](http://gnuwin32.sourceforge.net/packages/coreutils.htm) 
- [Make](http://gnuwin32.sourceforge.net/packages/make.htm)
- [VS redistributables](https://aka.ms/vs/17/release/vc_redist.x64.exe)
#### Linux
```sudo apt-get install build-essential curl```
 
## Examples

* Run transcription with us openlex model:  `ntx20 run atran-us-openlex@https://usr:psw@yourcluster.com -i file.mp3 -w text:pnc -f`
* Get help: `ntx20 run atran-us-openlex@https://usr:psw@yourcluster.com -h`
* Set env variable cluster1=https://usr:psw@yourcluster.com for storing connection string and then call: `ntx20 run atran-us-openlex@cluster1`
* Keep credentials outside of the connection string:

  ```bash
  export CLUSTER1=https://yourcluster.com
  export CLUSTER1_USERNAME=usr
  export CLUSTER1_PASSWORD='password used verbatim, without URL encoding'
  ntx20 run atran-us-openlex@CLUSTER1 --username-env CLUSTER1_USERNAME --password-env CLUSTER1_PASSWORD -i file.mp3
  ```

  Both options must be used together. Referenced variables must be set; empty values produce a warning. Do not combine these options with credentials in the connection string or an `Authorization` header supplied through `--head`.
* Environment for ntx20 process can be set in .env file in the root folder of ntx20 as key=value per line.
* Run websocket proxy live dictation with cz-atran-dictate model:  `ntx20 ws atran-cz-dictate@https://usr:psw@yourcluster.com` and open http://localhost:8080 
    
