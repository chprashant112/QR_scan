dotnet add package OpenCvSharp4

dotnet add package OpenCvSharp4.runtime.win

dotnet add package OpenCvSharp4.Extensions

dotnet add package System.IO.Ports




dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
