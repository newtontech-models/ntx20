
cd ../ntx20
dotnet publish -c Release -o ../build/install -f net5.0 || exit /b
cd ../build

mkdir apps.nanotrix.cloud\install
cd install
wsl tar --mtime='2021-01-01' -zcvf ../apps.nanotrix.cloud/install/ntx20.tgz .
cd ..
rmdir /s /q install